using System.Globalization;
using Access.Domain.Credentials;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Um ingresso consumido sem prova de giro, que o operador pode estornar.</summary>
/// <param name="Tentativa">A tentativa consumida.</param>
/// <param name="Em">Quando a leitura foi autorizada.</param>
/// <param name="Catraca">O equipamento (<c>device_id</c>).</param>
/// <param name="Portao">O portão.</param>
/// <param name="Provedor">De quem é o ingresso.</param>
/// <param name="Categoria">Inteira, meia etc., quando houver.</param>
/// <param name="Codigo">O código mascarado (nunca o código inteiro).</param>
/// <param name="FalhaDaLiberacao">Por que a liberação falhou; nulo quando liberou e não veio o giro.</param>
public sealed record UsoSemPassagem(
    Guid Tentativa,
    DateTimeOffset Em,
    string Catraca,
    string Portao,
    string? Provedor,
    string? Categoria,
    string Codigo,
    string? FalhaDaLiberacao);

/// <summary>
/// Usos sem passagem e o estorno pelo operador (achado E1-05 do docs/41; tabela <c>ticket_use_refund</c>,
/// migração 019).
/// </summary>
/// <remarks>
/// <para>
/// Decisão do dono do produto: quando a liberação falha depois da autorização, o ingresso continua
/// consumido (a catraca não devolve o uso sozinha), e o operador estorna, com nome e motivo, depois
/// de conferir com a pessoa. O estorno vale nesta borda: o uso que já foi enviado à nuvem e ao
/// provedor fica como foi, porque o contrato do retorno (docs/18 §10) não tem estorno.
/// </para>
/// <para>
/// Só depois de <see cref="EsperaAntesDoEstorno"/>: antes disso o giro ainda pode chegar, e um uso
/// estornado que depois gira daria uma entrada sem uso.
/// </para>
/// </remarks>
public sealed class EstornosDeUso
{
    /// <summary>Quanto esperar depois da leitura antes de aceitar o estorno (o relé vai até 50 s).</summary>
    public static readonly TimeSpan EsperaAntesDoEstorno = TimeSpan.FromMinutes(2);

    /// <summary>O que o estorno faz e não faz, para o operador ler antes de confirmar.</summary>
    public const string Alcance =
        "O ingresso volta a valer nesta borda e a entrada deixa de contar nos relatórios. " +
        "O uso que já foi enviado à nuvem e ao provedor fica como foi.";

    private const string StatusValido = "valido";
    private const string StatusConsumido = "consumido";

    private readonly SqliteConnectionFactory _fabrica;

    public EstornosDeUso(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>
    /// Os usos consumidos sem prova de giro, mais recentes primeiro, já fora da espera do giro. Os que
    /// tiveram a liberação falhando vêm com a causa.
    /// </summary>
    /// <param name="agora">Agora.</param>
    /// <param name="somenteComFalha">Só os que a liberação falhou.</param>
    /// <param name="limite">Máximo de linhas.</param>
    public IReadOnlyList<UsoSemPassagem> Listar(DateTimeOffset agora, bool somenteComFalha = false, int limite = 100)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            SELECT id, at, device_id, gate_id, provider_id, category, qr_normalized, release_failure
            FROM ticket_use_attempt
            WHERE outcome = 'consumido' AND passage_confirmed_at IS NULL AND ticket_id IS NOT NULL AND at <= $ate
              AND ($falha = 0 OR release_failed_at IS NOT NULL)
            ORDER BY at DESC
            LIMIT $limite;
            """;
        sql.Parameters.AddWithValue("$ate", Iso(agora - EsperaAntesDoEstorno));
        sql.Parameters.AddWithValue("$falha", somenteComFalha ? 1 : 0);
        sql.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 500));

        var lista = new List<UsoSemPassagem>();
        using var leitor = sql.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new UsoSemPassagem(
                Guid.Parse(leitor.GetString(0)),
                Data(leitor.GetString(1)),
                leitor.GetString(2),
                leitor.GetString(3),
                leitor.IsDBNull(4) ? null : leitor.GetString(4),
                leitor.IsDBNull(5) ? null : leitor.GetString(5),
                CredentialValue.Mascarar(leitor.GetString(6)),
                leitor.IsDBNull(7) ? null : leitor.GetString(7)));
        }

        return lista;
    }

    /// <summary>
    /// Estorna um uso sem passagem: a tentativa vira <c>estornado</c>, o ingresso recebe o uso de volta, e
    /// o estorno fica gravado com quem pediu e por quê. Nunca lança por regra: o que impede vem em
    /// <c>Problemas</c>.
    /// </summary>
    /// <param name="tentativa">A tentativa consumida.</param>
    /// <param name="operador">Identificação de quem pede, conferida pelo serviço.</param>
    /// <param name="motivo">Por quê.</param>
    /// <param name="agora">Agora.</param>
    public (bool Estornado, IReadOnlyList<string> Problemas) Estornar(Guid tentativa, string? operador, string? motivo, DateTimeOffset agora)
    {
        var quem = operador?.Trim() ?? string.Empty;
        var porque = motivo?.Trim() ?? string.Empty;
        var problemas = new List<string>();

        if (quem.Length < 2)
        {
            problemas.Add("Informe quem está estornando.");
        }

        if (porque.Length < 3)
        {
            problemas.Add("Informe o motivo do estorno.");
        }

        if (problemas.Count > 0)
        {
            return (false, problemas);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        string? ingresso;
        using (var leitura = conexao.CreateCommand())
        {
            leitura.Transaction = transacao;
            leitura.CommandText =
                """
                SELECT a.ticket_id, a.outcome, a.passage_confirmed_at, a.at
                FROM ticket_use_attempt a
                WHERE a.id = $id;
                """;
            leitura.Parameters.AddWithValue("$id", tentativa.ToString());
            using var leitor = leitura.ExecuteReader();
            if (!leitor.Read())
            {
                return (false, ["Tentativa não encontrada."]);
            }

            ingresso = leitor.IsDBNull(0) ? null : leitor.GetString(0);
            var desfecho = leitor.GetString(1);
            var girou = !leitor.IsDBNull(2);
            var em = Data(leitor.GetString(3));

            if (desfecho == "estornado")
            {
                return (false, ["Este uso já foi estornado."]);
            }

            if (desfecho != "consumido" || ingresso is null)
            {
                return (false, ["Só um uso consumido pode ser estornado."]);
            }

            if (girou)
            {
                return (false, ["A catraca confirmou o giro deste uso: a pessoa passou, e o uso não se estorna."]);
            }

            if (agora - em < EsperaAntesDoEstorno)
            {
                return (false, [$"Espere {EsperaAntesDoEstorno.TotalMinutes:0} min depois da leitura: o giro ainda pode chegar."]);
            }
        }

        Executar(conexao, transacao,
            $"""
            UPDATE ticket
            SET used_count      = used_count - 1,
                status          = CASE WHEN status = '{StatusConsumido}' THEN '{StatusValido}' ELSE status END,
                first_used_at   = CASE WHEN used_count - 1 = 0 THEN NULL ELSE first_used_at END,
                last_used_epoch = CASE WHEN used_count - 1 = 0 THEN NULL ELSE last_used_epoch END
            WHERE id = $ingresso AND used_count > 0;
            """,
            ("$ingresso", ingresso));

        Executar(conexao, transacao,
            "UPDATE ticket_use_attempt SET outcome = 'estornado' WHERE id = $id AND outcome = 'consumido';",
            ("$id", tentativa.ToString()));

        Executar(conexao, transacao,
            """
            INSERT INTO ticket_use_refund (attempt_id, ticket_id, refunded_at, refunded_by, reason)
            VALUES ($id, $ingresso, $em, $quem, $motivo);
            """,
            ("$id", tentativa.ToString()),
            ("$ingresso", ingresso),
            ("$em", Iso(agora)),
            ("$quem", quem.Length > Access.Application.Devices.ComandoDeCatraca.LimiteDoOperador
                ? quem[..Access.Application.Devices.ComandoDeCatraca.LimiteDoOperador] : quem),
            ("$motivo", porque.Length > 300 ? porque[..300] : porque));

        transacao.Commit();
        return (true, []);
    }

    private static void Executar(SqliteConnection conexao, SqliteTransaction transacao, string sql, params (string Nome, object Valor)[] parametros)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }

        comando.ExecuteNonQuery();
    }

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Data(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
