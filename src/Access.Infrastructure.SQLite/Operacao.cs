using System.Globalization;
using Access.Domain.Credentials;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>A situação de uma catraca, como o worker que a atende a vê.</summary>
public sealed record SituacaoDoEquipamento(
    string DeviceId,
    int Inner,
    string Worker,
    string Estado,
    bool Online,
    string? Firmware,
    int TentativasDeReconexao,
    DateTimeOffset? UltimoEventoEm,
    string? UltimaDecisao,
    DateTimeOffset AtualizadoEm);

/// <summary>
/// Uma tentativa, pronta para a tela: o código já vem mascarado. <c>Sequencia</c> é a
/// ordem de gravação; o painel pede "depois de N".
/// </summary>
public sealed record TentativaParaOPainel(
    long Sequencia,
    Guid Id,
    string DeviceId,
    string Portao,
    DateTimeOffset Em,
    bool Liberou,
    string Motivo,
    string? Categoria,
    string? Provedor,
    string CodigoMascarado,
    bool Girou);

/// <summary>Contagens do evento até agora, para o topo do painel.</summary>
public sealed record ResumoDaOperacao(
    long Liberados,
    long Negados,
    long Giros,
    long LiberadosNosUltimos5Minutos,
    long PendentesDeEnvio,
    DateTimeOffset? PendenteMaisAntigo,
    long CartasMortas);

/// <summary>
/// O que o serviço e o painel leem da operação, e o que os workers escrevem dela.
/// </summary>
/// <remarks>
/// A base local é o canal entre o worker e o serviço (ADR-0024): o worker grava e segue
/// girando catraca mesmo com o serviço fora; o serviço lê quando pode. Nada aqui devolve
/// código de cartão ou ingresso inteiro.
/// </remarks>
public sealed class Operacao
{
    private readonly SqliteConnectionFactory _fabrica;

    public Operacao(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Grava a situação das catracas de um worker, numa transação.</summary>
    public void GravarSituacao(IReadOnlyCollection<SituacaoDoEquipamento> situacoes)
    {
        ArgumentNullException.ThrowIfNull(situacoes);

        if (situacoes.Count == 0)
        {
            return;
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        foreach (var s in situacoes)
        {
            using var comando = conexao.CreateCommand();
            comando.Transaction = transacao;
            comando.CommandText =
                """
                INSERT INTO device_status
                    (device_id, inner_number, worker, state, online, firmware, reconnect_attempts,
                     last_event_at, last_decision, updated_at)
                VALUES ($id, $inner, $worker, $estado, $online, $firmware, $reconexoes,
                        $evento, $decisao, $em)
                ON CONFLICT (device_id) DO UPDATE SET
                    inner_number = excluded.inner_number, worker = excluded.worker,
                    state = excluded.state, online = excluded.online, firmware = excluded.firmware,
                    reconnect_attempts = excluded.reconnect_attempts,
                    last_event_at = excluded.last_event_at, last_decision = excluded.last_decision,
                    updated_at = excluded.updated_at;
                """;
            comando.Parameters.AddWithValue("$id", s.DeviceId);
            comando.Parameters.AddWithValue("$inner", s.Inner);
            comando.Parameters.AddWithValue("$worker", s.Worker);
            comando.Parameters.AddWithValue("$estado", s.Estado);
            comando.Parameters.AddWithValue("$online", s.Online ? 1 : 0);
            comando.Parameters.AddWithValue("$firmware", (object?)s.Firmware ?? DBNull.Value);
            comando.Parameters.AddWithValue("$reconexoes", s.TentativasDeReconexao);
            comando.Parameters.AddWithValue("$evento", (object?)IsoOuNulo(s.UltimoEventoEm) ?? DBNull.Value);
            comando.Parameters.AddWithValue("$decisao", (object?)s.UltimaDecisao ?? DBNull.Value);
            comando.Parameters.AddWithValue("$em", Iso(s.AtualizadoEm));
            comando.ExecuteNonQuery();
        }

        transacao.Commit();
    }

    /// <summary>Todas as catracas que algum worker já reportou.</summary>
    public IReadOnlyList<SituacaoDoEquipamento> ListarSituacao()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT device_id, inner_number, worker, state, online, firmware, reconnect_attempts,
                   last_event_at, last_decision, updated_at
            FROM device_status
            ORDER BY inner_number;
            """;

        var lista = new List<SituacaoDoEquipamento>();
        using var leitor = comando.ExecuteReader();

        while (leitor.Read())
        {
            lista.Add(new SituacaoDoEquipamento(
                leitor.GetString(0),
                leitor.GetInt32(1),
                leitor.GetString(2),
                leitor.GetString(3),
                leitor.GetInt64(4) == 1,
                leitor.IsDBNull(5) ? null : leitor.GetString(5),
                leitor.GetInt32(6),
                leitor.IsDBNull(7) ? null : Data(leitor.GetString(7)),
                leitor.IsDBNull(8) ? null : leitor.GetString(8),
                Data(leitor.GetString(9))));
        }

        return lista;
    }

    /// <summary>Tentativas gravadas depois da sequência informada, em ordem.</summary>
    /// <param name="depoisDe">Última sequência já vista; 0 para o começo.</param>
    /// <param name="limite">Máximo de linhas.</param>
    public IReadOnlyList<TentativaParaOPainel> TentativasDepoisDe(long depoisDe, int limite = 200)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT rowid, id, device_id, gate_id, at, outcome, reason, category, provider_id,
                   qr_normalized, passage_confirmed_at
            FROM ticket_use_attempt
            WHERE rowid > $depois
            ORDER BY rowid
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$depois", depoisDe);
        comando.Parameters.AddWithValue("$limite", limite);

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

    /// <summary>A maior sequência gravada; o painel começa daqui para não reler o dia.</summary>
    public long UltimaSequencia()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COALESCE(MAX(rowid), 0) FROM ticket_use_attempt;";
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Contagens para o topo do painel.</summary>
    public ResumoDaOperacao Resumir(DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();

        long Escalar(string sql, params (string Nome, object Valor)[] parametros)
        {
            using var comando = conexao.CreateCommand();
            comando.CommandText = sql;
            foreach (var (nome, valor) in parametros)
            {
                comando.Parameters.AddWithValue(nome, valor);
            }

            return Convert.ToInt64(comando.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }

        var liberados = Escalar("SELECT COUNT(*) FROM ticket_use_attempt WHERE outcome = 'consumido';");
        var negados = Escalar("SELECT COUNT(*) FROM ticket_use_attempt WHERE outcome = 'negado';");
        var giros = Escalar("SELECT COUNT(*) FROM ticket_use_attempt WHERE passage_confirmed_at IS NOT NULL;");
        var recentes = Escalar(
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE outcome = 'consumido' AND at >= $desde;",
            ("$desde", Iso(agora.AddMinutes(-5))));
        var pendentes = Escalar("SELECT COUNT(*) FROM outbox WHERE sent_at IS NULL;");
        var mortas = Escalar("SELECT COUNT(*) FROM dead_letter;");

        DateTimeOffset? maisAntigo = null;
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "SELECT MIN(created_at) FROM outbox WHERE sent_at IS NULL;";
            if (comando.ExecuteScalar() is string texto)
            {
                maisAntigo = Data(texto);
            }
        }

        return new ResumoDaOperacao(liberados, negados, giros, recentes, pendentes, maisAntigo, mortas);
    }

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? IsoOuNulo(DateTimeOffset? valor) => valor is { } v ? Iso(v) : null;

    private static DateTimeOffset Data(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
