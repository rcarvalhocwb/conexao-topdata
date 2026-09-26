using System.Globalization;
using System.Text;
using Access.Domain.Credentials;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Filtro da tela de acessos. Tudo opcional.</summary>
/// <param name="Inner">Só esta catraca.</param>
/// <param name="Liberados">Verdadeiro: só liberados; falso: só negados; nulo: todos.</param>
/// <param name="Desde">A partir deste instante.</param>
/// <param name="Ate">Até este instante.</param>
/// <param name="Categoria">Só esta categoria.</param>
/// <param name="Limite">Máximo de linhas (1 a 1000).</param>
public sealed record FiltroDeTentativas(
    int? Inner = null,
    bool? Liberados = null,
    DateTimeOffset? Desde = null,
    DateTimeOffset? Ate = null,
    string? Categoria = null,
    int Limite = 200);

/// <summary>Contagem de uma linha da prestação de contas.</summary>
public sealed record LinhaDeContagem(string Chave, long Liberados, long Giros, long Negados);

/// <summary>Contagem por hora.</summary>
public sealed record LinhaHoraria(DateTimeOffset Hora, long Liberados, long Negados);

/// <summary>A prestação de contas de um intervalo, reproduzível: mesmo intervalo, mesmos números.</summary>
public sealed record ContasDaOperacao(
    IReadOnlyList<LinhaDeContagem> PorCategoria,
    IReadOnlyList<LinhaDeContagem> PorCatraca,
    IReadOnlyList<LinhaHoraria> PorHora,
    IReadOnlyList<(string Motivo, long Quantidade)> Negativas,
    long Liberados,
    long Giros,
    long Negados);

/// <summary>O que se sabe de um código consultado pelo operador. Nada vem inteiro.</summary>
public sealed record SituacaoDoCodigo(
    string CodigoMascarado,
    string Provedor,
    string? Categoria,
    string Situacao,
    long UsosFeitos,
    long? UsosMaximos,
    DateTimeOffset? UltimoUso,
    IReadOnlyList<TentativaParaOPainel> Historico);

/// <summary>Um provedor e quantos códigos ele tem na base.</summary>
public sealed record ProvedorComContagem(
    string Id,
    string Nome,
    long Codigos,
    bool Reutilizavel,
    int IntervaloDeReusoSegundos,
    bool SomenteNaUrna,
    bool Habilitado);

/// <summary>Consultas das telas do operador sobre a base local.</summary>
/// <remarks>
/// Só leitura. Todo código que sai daqui sai mascarado; a consulta por código recebe o
/// código inteiro (o operador o digitou) e devolve só a máscara.
/// </remarks>
public sealed class ConsultasDaOperacao
{
    /// <summary>Uso "sem limite" do cartão da nuvem (ver FonteDeCartoesDoPainel).</summary>
    private const long SemLimite = int.MaxValue;

    private readonly SqliteConnectionFactory _fabrica;

    public ConsultasDaOperacao(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Tentativas do mais recente para o mais antigo, e se há mais além do limite.</summary>
    public (IReadOnlyList<TentativaParaOPainel> Tentativas, bool HaMais) ListarTentativas(FiltroDeTentativas filtro)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var limite = Math.Clamp(filtro.Limite, 1, 1000);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        var onde = new StringBuilder("WHERE 1 = 1");

        if (filtro.Inner is { } inner)
        {
            onde.Append(" AND device_id = $dispositivo");
            comando.Parameters.AddWithValue("$dispositivo", string.Create(CultureInfo.InvariantCulture, $"inner-{inner}"));
        }

        if (filtro.Liberados is { } liberados)
        {
            onde.Append(" AND outcome = $desfecho");
            comando.Parameters.AddWithValue("$desfecho", liberados ? "consumido" : "negado");
        }

        if (filtro.Desde is { } desde)
        {
            onde.Append(" AND at >= $desde");
            comando.Parameters.AddWithValue("$desde", Iso(desde));
        }

        if (filtro.Ate is { } ate)
        {
            onde.Append(" AND at <= $ate");
            comando.Parameters.AddWithValue("$ate", Iso(ate));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Categoria))
        {
            onde.Append(" AND category = $categoria");
            comando.Parameters.AddWithValue("$categoria", filtro.Categoria);
        }

        comando.CommandText =
            $"""
            SELECT rowid, id, device_id, gate_id, at, outcome, reason, category, provider_id,
                   qr_normalized, passage_confirmed_at
            FROM ticket_use_attempt
            {onde}
            ORDER BY rowid DESC
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$limite", limite + 1);

        var lista = Ler(comando);
        var haMais = lista.Count > limite;
        return (haMais ? lista.Take(limite).ToList() : lista, haMais);
    }

    /// <summary>A prestação de contas entre dois instantes.</summary>
    public ContasDaOperacao Contas(DateTimeOffset desde, DateTimeOffset ate)
    {
        using var conexao = _fabrica.Abrir();

        var intervalo = new (string, object)[] { ("$desde", Iso(desde)), ("$ate", Iso(ate)) };

        var porCategoria = Linhas(
            conexao,
            """
            SELECT COALESCE(category, ''),
                   SUM(outcome = 'consumido'), SUM(passage_confirmed_at IS NOT NULL), SUM(outcome = 'negado')
            FROM ticket_use_attempt
            WHERE at >= $desde AND at <= $ate AND outcome = 'consumido'
            GROUP BY 1 ORDER BY 2 DESC;
            """,
            intervalo);

        var porCatraca = Linhas(
            conexao,
            """
            SELECT device_id,
                   SUM(outcome = 'consumido'), SUM(passage_confirmed_at IS NOT NULL), SUM(outcome = 'negado')
            FROM ticket_use_attempt
            WHERE at >= $desde AND at <= $ate
            GROUP BY 1 ORDER BY 1;
            """,
            intervalo);

        var porHora = new List<LinhaHoraria>();
        using (var comando = conexao.CreateCommand())
        {
            // Hora em UTC, que é como está gravado; a tela converte para a hora local.
            comando.CommandText =
                """
                SELECT substr(at, 1, 13), SUM(outcome = 'consumido'), SUM(outcome = 'negado')
                FROM ticket_use_attempt
                WHERE at >= $desde AND at <= $ate
                GROUP BY 1 ORDER BY 1;
                """;
            Parametros(comando, intervalo);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                porHora.Add(new LinhaHoraria(
                    DateTimeOffset.ParseExact(leitor.GetString(0) + ":00:00+00:00", "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                    leitor.GetInt64(1),
                    leitor.GetInt64(2)));
            }
        }

        var negativas = new List<(string, long)>();
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                SELECT reason, COUNT(*) FROM ticket_use_attempt
                WHERE at >= $desde AND at <= $ate AND outcome = 'negado'
                GROUP BY 1 ORDER BY 2 DESC;
                """;
            Parametros(comando, intervalo);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                negativas.Add((leitor.GetString(0), leitor.GetInt64(1)));
            }
        }

        return new ContasDaOperacao(
            porCategoria,
            porCatraca,
            porHora,
            negativas,
            porCatraca.Sum(l => l.Liberados),
            porCatraca.Sum(l => l.Giros),
            porCatraca.Sum(l => l.Negados));
    }

    /// <summary>
    /// O que se sabe de um código digitado pelo operador. Procura como foi cadastrado e
    /// como a catraca o leria; nulo se não existe.
    /// </summary>
    public SituacaoDoCodigo? ConsultarCodigo(string codigo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        var procurado = codigo.Trim();

        using var conexao = _fabrica.Abrir();

        string qr;
        SituacaoDoCodigo situacao;

        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                SELECT t.qr_normalized, p.name, t.category, t.status, t.used_count, t.max_uses, t.last_used_at
                FROM ticket t JOIN ticket_provider p ON p.id = t.provider_id
                WHERE t.qr_normalized = $codigo OR t.qr_raw = $codigo
                LIMIT 1;
                """;
            comando.Parameters.AddWithValue("$codigo", procurado);
            using var leitor = comando.ExecuteReader();

            if (!leitor.Read())
            {
                return null;
            }

            qr = leitor.GetString(0);
            var maximo = leitor.GetInt64(5);

            situacao = new SituacaoDoCodigo(
                CredentialValue.Mascarar(qr),
                leitor.GetString(1),
                leitor.IsDBNull(2) ? null : leitor.GetString(2),
                leitor.GetString(3),
                leitor.GetInt64(4),
                maximo >= SemLimite ? null : maximo,
                leitor.IsDBNull(6) ? null : Data(leitor.GetString(6)),
                []);
        }

        using var historico = conexao.CreateCommand();
        historico.CommandText =
            """
            SELECT rowid, id, device_id, gate_id, at, outcome, reason, category, provider_id,
                   qr_normalized, passage_confirmed_at
            FROM ticket_use_attempt
            WHERE qr_normalized = $qr
            ORDER BY rowid DESC
            LIMIT 20;
            """;
        historico.Parameters.AddWithValue("$qr", qr);

        return situacao with { Historico = Ler(historico) };
    }

    /// <summary>Provedores cadastrados e quantos códigos cada um tem.</summary>
    public IReadOnlyList<ProvedorComContagem> Provedores()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT p.id, p.name, (SELECT COUNT(*) FROM ticket t WHERE t.provider_id = p.id),
                   p.reusable, p.reuse_interval_seconds, p.urn_only, p.enabled
            FROM ticket_provider p ORDER BY p.id;
            """;

        var lista = new List<ProvedorComContagem>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new ProvedorComContagem(
                leitor.GetString(0),
                leitor.GetString(1),
                leitor.GetInt64(2),
                leitor.GetInt64(3) == 1,
                leitor.GetInt32(4),
                leitor.GetInt64(5) == 1,
                leitor.GetInt64(6) == 1));
        }

        return lista;
    }

    private static List<LinhaDeContagem> Linhas(SqliteConnection conexao, string sql, (string, object)[] parametros)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        Parametros(comando, parametros);

        var linhas = new List<LinhaDeContagem>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            linhas.Add(new LinhaDeContagem(leitor.GetString(0), leitor.GetInt64(1), leitor.GetInt64(2), leitor.GetInt64(3)));
        }

        return linhas;
    }

    private static void Parametros(SqliteCommand comando, (string Nome, object Valor)[] parametros)
    {
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }
    }

    private static List<TentativaParaOPainel> Ler(SqliteCommand comando)
    {
        var lista = new List<TentativaParaOPainel>();
        using var leitor = comando.ExecuteReader();

        while (leitor.Read())
        {
            lista.Add(new TentativaParaOPainel(
                leitor.GetInt64(0),
                Guid.Parse(leitor.GetString(1)),
                leitor.GetString(2),
                leitor.GetString(3),
                Data(leitor.GetString(4)),
                string.Equals(leitor.GetString(5), "consumido", StringComparison.Ordinal),
                leitor.GetString(6),
                leitor.IsDBNull(7) ? null : leitor.GetString(7),
                leitor.IsDBNull(8) ? null : leitor.GetString(8),
                CredentialValue.Mascarar(leitor.GetString(9)),
                !leitor.IsDBNull(10)));
        }

        return lista;
    }

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Data(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
