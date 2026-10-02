using System.Globalization;
using Access.Application.Devices;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Um comando e o que aconteceu com ele, para o histórico do painel.</summary>
public sealed record RegistroDeComando(
    ComandoDeCatraca Comando,
    SituacaoDoComando Situacao,
    DateTimeOffset? RecebidoEm,
    DateTimeOffset? ConcluidoEm,
    string? Resultado);

/// <summary>
/// Os comandos do operador na base local, tabela <c>operator_command</c> (migração 009).
/// </summary>
/// <remarks>
/// O serviço pede (<see cref="Pedir"/>) e lista; o worker pega, executa e conclui. A
/// tabela é auditoria: gatilhos impedem apagar e mudar o pedido depois de gravado.
/// </remarks>
public sealed class FilaDeComandosSqlite : IFilaDeComandos
{
    private readonly SqliteConnectionFactory _fabrica;

    public FilaDeComandosSqlite(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Grava um pedido.</summary>
    public void Pedir(ComandoDeCatraca comando)
    {
        ArgumentNullException.ThrowIfNull(comando);

        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            INSERT INTO operator_command
                (id, inner_number, kind, text, duration_s, reason, requested_by, requested_at, expires_at)
            VALUES ($id, $inner, $tipo, $texto, $duracao, $motivo, $quem, $em, $expira);
            """;
        sql.Parameters.AddWithValue("$id", comando.Id.ToString());
        sql.Parameters.AddWithValue("$inner", comando.Inner);
        sql.Parameters.AddWithValue("$tipo", comando.Tipo.ToString());
        sql.Parameters.AddWithValue("$texto", (object?)comando.Texto ?? DBNull.Value);
        sql.Parameters.AddWithValue("$duracao", comando.DuracaoSegundos);
        sql.Parameters.AddWithValue("$motivo", (object?)comando.Motivo ?? DBNull.Value);
        sql.Parameters.AddWithValue("$quem", comando.Operador);
        sql.Parameters.AddWithValue("$em", Iso(comando.PedidoEm));
        sql.Parameters.AddWithValue("$expira", Iso(comando.ExpiraEm));
        sql.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<ComandoDeCatraca> Pendentes(IReadOnlyCollection<int> inners)
    {
        ArgumentNullException.ThrowIfNull(inners);

        if (inners.Count == 0)
        {
            return [];
        }

        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        var nomes = inners.Select((inner, i) =>
        {
            var nome = string.Create(CultureInfo.InvariantCulture, $"$i{i}");
            sql.Parameters.AddWithValue(nome, inner);
            return nome;
        }).ToList();
        sql.CommandText =
            $"""
            SELECT {Colunas}
            FROM operator_command
            WHERE status = 'pendente' AND inner_number IN ({string.Join(", ", nomes)})
            ORDER BY requested_at
            LIMIT 50;
            """;

        return [.. Ler(sql).Select(r => r.Comando)];
    }

    /// <inheritdoc />
    public bool Receber(Guid id, DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            UPDATE operator_command SET status = 'recebido', taken_at = $em
            WHERE id = $id AND status = 'pendente' AND expires_at > $em;
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$em", Iso(agora));
        return sql.ExecuteNonQuery() == 1;
    }

    /// <inheritdoc />
    public void Concluir(Guid id, SituacaoDoComando situacao, string resultado, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        if (situacao is not (SituacaoDoComando.Concluido or SituacaoDoComando.Falhou or SituacaoDoComando.Expirado))
        {
            throw new ArgumentOutOfRangeException(nameof(situacao), situacao, "Concluir só aceita situação final.");
        }

        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            UPDATE operator_command SET status = $situacao, finished_at = $em, result = $resultado
            WHERE id = $id AND status IN ('pendente', 'recebido');
            """;
        sql.Parameters.AddWithValue("$id", id.ToString());
        sql.Parameters.AddWithValue("$situacao", Texto(situacao));
        sql.Parameters.AddWithValue("$em", Iso(agora));
        sql.Parameters.AddWithValue("$resultado", resultado.Length > 300 ? resultado[..300] : resultado);
        sql.ExecuteNonQuery();
    }

    /// <summary>Pedidos que ninguém pegou a tempo viram "expirado". Devolve quantos.</summary>
    public int ExpirarVencidos(DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            UPDATE operator_command
            SET status = 'expirado', finished_at = $em,
                result = 'ninguém executou a tempo: o programa da catraca estava parado ou ocupado'
            WHERE status = 'pendente' AND expires_at <= $em;
            """;
        sql.Parameters.AddWithValue("$em", Iso(agora));
        return sql.ExecuteNonQuery();
    }

    /// <summary>Os comandos mais recentes, de uma catraca ou de todas.</summary>
    public IReadOnlyList<RegistroDeComando> Listar(int? inner, int limite = 100)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            $"""
            SELECT {Colunas}
            FROM operator_command
            WHERE $inner IS NULL OR inner_number = $inner
            ORDER BY requested_at DESC
            LIMIT $limite;
            """;
        sql.Parameters.AddWithValue("$inner", (object?)inner ?? DBNull.Value);
        sql.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 500));
        return Ler(sql);
    }

    private const string Colunas =
        "id, inner_number, kind, text, duration_s, reason, requested_by, requested_at, expires_at, status, taken_at, finished_at, result";

    private static List<RegistroDeComando> Ler(SqliteCommand sql)
    {
        var lista = new List<RegistroDeComando>();
        using var leitor = sql.ExecuteReader();

        while (leitor.Read())
        {
            var comando = new ComandoDeCatraca(
                Guid.Parse(leitor.GetString(0)),
                leitor.GetInt32(1),
                Enum.Parse<TipoDeComando>(leitor.GetString(2)),
                leitor.IsDBNull(3) ? null : leitor.GetString(3),
                leitor.GetInt32(4),
                leitor.IsDBNull(5) ? null : leitor.GetString(5),
                leitor.GetString(6),
                Data(leitor.GetString(7)),
                Data(leitor.GetString(8)));

            lista.Add(new RegistroDeComando(
                comando,
                Situacao(leitor.GetString(9)),
                leitor.IsDBNull(10) ? null : Data(leitor.GetString(10)),
                leitor.IsDBNull(11) ? null : Data(leitor.GetString(11)),
                leitor.IsDBNull(12) ? null : leitor.GetString(12)));
        }

        return lista;
    }

    private static string Texto(SituacaoDoComando situacao) => situacao switch
    {
        SituacaoDoComando.Pendente => "pendente",
        SituacaoDoComando.Recebido => "recebido",
        SituacaoDoComando.Concluido => "concluido",
        SituacaoDoComando.Falhou => "falhou",
        SituacaoDoComando.Expirado => "expirado",
        _ => throw new ArgumentOutOfRangeException(nameof(situacao)),
    };

    private static SituacaoDoComando Situacao(string texto) => texto switch
    {
        "pendente" => SituacaoDoComando.Pendente,
        "recebido" => SituacaoDoComando.Recebido,
        "concluido" => SituacaoDoComando.Concluido,
        "falhou" => SituacaoDoComando.Falhou,
        "expirado" => SituacaoDoComando.Expirado,
        _ => throw new FormatException($"situação de comando desconhecida: '{texto}'"),
    };

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Data(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
