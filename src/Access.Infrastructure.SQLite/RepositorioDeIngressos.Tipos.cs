using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Um tipo de entrada (INTEIRA, MEIA, SOCIAL…), como a base o guarda (<c>ticket_type</c>).</summary>
/// <param name="Codigo">O que vai em <c>ticket.category</c>: 2 a 20 letras maiúsculas, dígitos ou "_".</param>
/// <param name="Nome">O nome que o operador lê ("Meia-entrada"), 1 a 40.</param>
/// <param name="Ordem">Posição nas listas e nos relatórios.</param>
/// <param name="Ativo">Desativado nega na catraca todos os cartões do tipo (B.2).</param>
/// <param name="Cartoes">Quantos cartões usam o tipo agora. Só leitura.</param>
public sealed record TipoDeEntrada(string Codigo, string Nome, int Ordem, bool Ativo, int Cartoes = 0);

public sealed partial class RepositorioDeIngressos
{
    /// <summary>O resultado de gravar um tipo de entrada.</summary>
    public enum ResultadoDoTipo
    {
        Gravado,
        CodigoInvalido,
        NomeInvalido,
        OrdemInvalida,
        AutorInvalido,
    }

    /// <summary>Os tipos de entrada, na ordem das listas, com quantos cartões cada um tem.</summary>
    public IReadOnlyList<TipoDeEntrada> TiposDeEntrada()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT tt.code, tt.display_name, tt.sort_order, tt.active,
                   (SELECT COUNT(*) FROM ticket t WHERE t.category = tt.code)
            FROM ticket_type tt
            ORDER BY tt.sort_order, tt.code;
            """;

        var lista = new List<TipoDeEntrada>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new TipoDeEntrada(
                leitor.GetString(0), leitor.GetString(1), leitor.GetInt32(2), leitor.GetInt32(3) == 1, leitor.GetInt32(4)));
        }

        return lista;
    }

    /// <summary>
    /// Cria ou altera um tipo de entrada. O código não muda depois de criado: é ele que está
    /// gravado em cada cartão.
    /// </summary>
    public ResultadoDoTipo GravarTipoDeEntrada(TipoDeEntrada tipo, string autor, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(tipo);

        var codigo = (tipo.Codigo ?? string.Empty).Trim();
        if (!CodigoDeTipoValido(codigo))
        {
            return ResultadoDoTipo.CodigoInvalido;
        }

        var nome = (tipo.Nome ?? string.Empty).Trim();
        if (nome.Length is < 1 or > 40)
        {
            return ResultadoDoTipo.NomeInvalido;
        }

        if (tipo.Ordem is < 0 or > 9999)
        {
            return ResultadoDoTipo.OrdemInvalida;
        }

        var por = (autor ?? string.Empty).Trim();
        if (por.Length is < 2 or > 80)
        {
            return ResultadoDoTipo.AutorInvalido;
        }

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO ticket_type (code, display_name, sort_order, active, created_at, created_by)
            VALUES ($codigo, $nome, $ordem, $ativo, $em, $por)
            ON CONFLICT (code) DO UPDATE SET
                display_name = excluded.display_name,
                sort_order   = excluded.sort_order,
                active       = excluded.active,
                updated_at   = excluded.created_at,
                updated_by   = excluded.created_by;
            """;
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$nome", nome);
        comando.Parameters.AddWithValue("$ordem", tipo.Ordem);
        comando.Parameters.AddWithValue("$ativo", tipo.Ativo ? 1 : 0);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$por", por);
        comando.ExecuteNonQuery();
        return ResultadoDoTipo.Gravado;
    }

    /// <summary>
    /// Cria, ativos e com o nome de costume, os tipos que ainda não existem. Os que já existem
    /// ficam como estão (o operador pode tê-los renomeado ou desativado).
    /// </summary>
    /// <returns>Quantos tipos foram criados.</returns>
    public int GarantirTiposDeEntrada(IEnumerable<string> codigos, string autor, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(codigos);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var criados = 0;
        var ordem = ProximaOrdem(conexao, transacao);

        foreach (var codigo in codigos.Select(c => (c ?? string.Empty).Trim()).Where(CodigoDeTipoValido).Distinct(StringComparer.Ordinal))
        {
            using var comando = conexao.CreateCommand();
            comando.Transaction = transacao;
            comando.CommandText =
                """
                INSERT OR IGNORE INTO ticket_type (code, display_name, sort_order, active, created_at, created_by)
                VALUES ($codigo, $nome, $ordem, 1, $em, $por);
                """;
            comando.Parameters.AddWithValue("$codigo", codigo);
            comando.Parameters.AddWithValue("$nome", NomeDeCostume(codigo));
            comando.Parameters.AddWithValue("$ordem", OrdemDeCostume(codigo) ?? ordem);
            comando.Parameters.AddWithValue("$em", Iso(agora));
            comando.Parameters.AddWithValue("$por", autor);

            if (comando.ExecuteNonQuery() == 1)
            {
                criados++;
                ordem += 10;
            }
        }

        transacao.Commit();
        return criados;
    }

    /// <summary>A regra do código de tipo da migração 011: 2 a 20 de A–Z, 0–9 e "_".</summary>
    public static bool CodigoDeTipoValido(string? codigo) =>
        codigo is { Length: >= 2 and <= 20 } && codigo.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_');

    /// <summary>O nome com que um tipo nasce, quando ninguém deu um.</summary>
    public static string NomeDeCostume(string codigo) => codigo switch
    {
        "INTEIRA" => "Inteira",
        "MEIA" => "Meia-entrada",
        "SOCIAL" => "Social",
        "CORTESIA" => "Cortesia",
        _ => CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(codigo.Replace('_', ' ').ToLowerInvariant()),
    };

    private static int? OrdemDeCostume(string codigo) => codigo switch
    {
        "INTEIRA" => 10,
        "MEIA" => 20,
        "SOCIAL" => 30,
        _ => null,
    };

    private static int ProximaOrdem(SqliteConnection conexao, SqliteTransaction transacao)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT COALESCE(MAX(sort_order), 30) + 10 FROM ticket_type;";
        return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
