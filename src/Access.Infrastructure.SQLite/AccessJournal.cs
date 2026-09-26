using System.Globalization;
using System.Text;
using Access.Domain.Access;
using Access.Domain.Devices;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Item a ser sincronizado com um sistema externo, mais tarde.</summary>
/// <param name="AggregateType">Tipo do agregado de origem.</param>
/// <param name="AggregateId">Identificador do agregado.</param>
/// <param name="PayloadJson">Conteúdo a enviar.</param>
/// <param name="Priority">0 = bloqueio emergencial … 9 = histórico.</param>
/// <param name="Connector">Conector de destino.</param>
/// <param name="IdempotencyKey">Chave que impede envio duplicado.</param>
public sealed record OutboxItem(
    string AggregateType,
    string AggregateId,
    string PayloadJson,
    int Priority,
    string Connector,
    string IdempotencyKey);

/// <summary>Comando enviado ao equipamento.</summary>
public sealed record IssuedCommand(string Command, string? ParamsJson, string IdempotencyKey);

/// <summary>Resultado de uma tentativa de registro de acesso.</summary>
/// <param name="Gravado">Falso quando o evento já existia (reenvio).</param>
/// <param name="EventId">Identificador do evento.</param>
/// <param name="DecisionId">Identificador da decisão, quando gravada.</param>
public sealed record JournalResult(bool Gravado, Guid EventId, Guid? DecisionId);

/// <summary>
/// Grava evento, decisão, comando e itens de sincronização <b>numa única transação</b>.
/// </summary>
/// <remarks>
/// <para>
/// É esta atomicidade que sustenta o critério CA-02: se o processo morrer no
/// microssegundo seguinte, ou tudo aconteceu, ou nada aconteceu. Não existe o estado
/// "gravou o acesso mas perdeu a sincronização".
/// </para>
/// <para>
/// A nuvem nunca é escrita aqui. O que sai daqui é uma linha na outbox, drenada depois.
/// Ver docs/ADR/ADR-0002-local-first.md e ADR-0003.
/// </para>
/// </remarks>
public sealed class AccessJournal
{
    private readonly SqliteConnectionFactory _fabrica;

    public AccessJournal(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>
    /// Registra um ciclo de acesso completo.
    /// </summary>
    /// <remarks>
    /// Reenvio do mesmo evento (mesma chave <c>device/boot/seq</c>) é ignorado em
    /// silêncio e devolve <see cref="JournalResult.Gravado"/> falso — é o caminho normal
    /// depois de uma reconexão, não um erro.
    /// </remarks>
    public JournalResult Registrar(
        DeviceEvent evento,
        Decision? decisao = null,
        IssuedCommand? comando = null,
        IReadOnlyCollection<OutboxItem>? outbox = null,
        string? credencialNormalizada = null,
        string? gateId = null)
    {
        ArgumentNullException.ThrowIfNull(evento);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        if (!InserirEvento(conexao, transacao, evento))
        {
            // Já existia. Nada a fazer — e nada a desfazer.
            transacao.Rollback();
            return new JournalResult(Gravado: false, evento.EventId, DecisionId: null);
        }

        Guid? decisionId = null;

        if (decisao is not null)
        {
            decisionId = Guid.CreateVersion7(evento.ReceivedTime);
            InserirDecisao(conexao, transacao, decisionId.Value, evento, decisao, credencialNormalizada, gateId);
        }

        if (comando is not null)
        {
            InserirComando(conexao, transacao, evento, decisionId, comando);
        }

        if (outbox is { Count: > 0 })
        {
            foreach (var item in outbox)
            {
                InserirOutbox(conexao, transacao, evento.ReceivedTime, item);
            }
        }

        transacao.Commit();
        return new JournalResult(Gravado: true, evento.EventId, decisionId);
    }

    /// <summary>
    /// Registra a passagem física. Só deve ser chamado com prova de giro (origem 6).
    /// </summary>
    public void RegistrarPassagemFisica(
        DeviceEvent eventoDeGiro,
        Guid? decisionId,
        string direcao)
    {
        ArgumentNullException.ThrowIfNull(eventoDeGiro);
        ArgumentException.ThrowIfNullOrWhiteSpace(direcao);

        if (!eventoDeGiro.Origin.ConfirmaPassagemFisica)
        {
            throw new InvalidOperationException(
                $"Passagem física exige prova de giro (origem 6). Recebido: {eventoDeGiro.Origin}. " +
                "Ver docs/ADR/ADR-0007-autorizacao-versus-passagem.md");
        }

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO physical_passage (id, decision_id, device_id, raw_event_id, direction, confirmed_at)
            VALUES ($id, $decisao, $dispositivo, $evento, $direcao, $em);
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(eventoDeGiro.ReceivedTime).ToString());
        comando.Parameters.AddWithValue("$decisao", (object?)decisionId?.ToString() ?? DBNull.Value);
        comando.Parameters.AddWithValue("$dispositivo", eventoDeGiro.Key.DeviceId);
        comando.Parameters.AddWithValue("$evento", eventoDeGiro.EventId.ToString());
        comando.Parameters.AddWithValue("$direcao", direcao);
        comando.Parameters.AddWithValue("$em", Iso(eventoDeGiro.ReceivedTime));
        comando.ExecuteNonQuery();
    }

    /// <summary>Itens pendentes na outbox, do mais prioritário ao mais antigo.</summary>
    public IReadOnlyList<(string Id, string Connector, int Priority)> OutboxPendente(int limite = 100)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, connector, priority
            FROM outbox
            WHERE sent_at IS NULL
            ORDER BY priority ASC, created_at ASC
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$limite", limite);

        var resultado = new List<(string, string, int)>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            resultado.Add((leitor.GetString(0), leitor.GetString(1), leitor.GetInt32(2)));
        }

        return resultado;
    }

    /// <summary>Conta eventos gravados para um dispositivo.</summary>
    public long ContarEventos(string deviceId)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM raw_event WHERE device_id = $d;";
        comando.Parameters.AddWithValue("$d", deviceId);
        return (long)(comando.ExecuteScalar() ?? 0L);
    }

    /// <summary>Conta eventos cuja origem não consta da tabela oficial.</summary>
    public long ContarOrigensDesconhecidas()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM raw_event WHERE origin_known IS NULL;";
        return (long)(comando.ExecuteScalar() ?? 0L);
    }

    private static bool InserirEvento(SqliteConnection conexao, SqliteTransaction transacao, DeviceEvent evento)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT OR IGNORE INTO raw_event
                (id, device_id, boot_id, device_seq, origin_raw, origin_known,
                 payload, device_time, received_time, correlation_id)
            VALUES
                ($id, $dispositivo, $boot, $seq, $origemBruta, $origemConhecida,
                 $payload, $horaDoEquipamento, $horaDeRecepcao, $correlacao);
            """;

        comando.Parameters.AddWithValue("$id", evento.EventId.ToString());
        comando.Parameters.AddWithValue("$dispositivo", evento.Key.DeviceId);
        comando.Parameters.AddWithValue("$boot", evento.Key.BootId);
        comando.Parameters.AddWithValue("$seq", evento.Key.DeviceSeq);
        comando.Parameters.AddWithValue("$origemBruta", evento.Origin.Raw);
        comando.Parameters.AddWithValue(
            "$origemConhecida",
            (object?)evento.Origin.Known?.ToString() ?? DBNull.Value);
        comando.Parameters.AddWithValue("$payload", Encoding.UTF8.GetBytes(evento.RawCardData ?? string.Empty));
        comando.Parameters.AddWithValue(
            "$horaDoEquipamento",
            evento.DeviceTime is { } dt ? Iso(dt) : DBNull.Value);
        comando.Parameters.AddWithValue("$horaDeRecepcao", Iso(evento.ReceivedTime));
        comando.Parameters.AddWithValue("$correlacao", evento.CorrelationId);

        return comando.ExecuteNonQuery() == 1;
    }

    private static void InserirDecisao(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        Guid decisionId,
        DeviceEvent evento,
        Decision decisao,
        string? credencial,
        string? gateId)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO access_decision
                (id, raw_event_id, device_id, gate_id, credential_value, outcome,
                 reason_code, rule_trace_json, elapsed_ms, degradation_tier, decided_at)
            VALUES
                ($id, $evento, $dispositivo, $gate, $credencial, $resultado,
                 $motivo, $trilha, $duracao, $nivel, $em);
            """;

        comando.Parameters.AddWithValue("$id", decisionId.ToString());
        comando.Parameters.AddWithValue("$evento", evento.EventId.ToString());
        comando.Parameters.AddWithValue("$dispositivo", evento.Key.DeviceId);
        comando.Parameters.AddWithValue("$gate", (object?)gateId ?? DBNull.Value);
        comando.Parameters.AddWithValue("$credencial", (object?)credencial ?? DBNull.Value);
        comando.Parameters.AddWithValue("$resultado", decisao.Outcome.ToString());
        comando.Parameters.AddWithValue("$motivo", decisao.Reason.Value);
        comando.Parameters.AddWithValue("$trilha", (object?)SerializarTrilha(decisao) ?? DBNull.Value);
        comando.Parameters.AddWithValue("$duracao", (long)decisao.Elapsed.TotalMilliseconds);
        comando.Parameters.AddWithValue("$nivel", decisao.Tier.ToString());
        comando.Parameters.AddWithValue("$em", Iso(evento.ReceivedTime));
        comando.ExecuteNonQuery();
    }

    private static void InserirComando(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        DeviceEvent evento,
        Guid? decisionId,
        IssuedCommand comandoEmitido)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO device_command
                (id, device_id, decision_id, command, params_json, issued_at, idempotency_key)
            VALUES
                ($id, $dispositivo, $decisao, $comando, $parametros, $em, $idempotencia);
            """;

        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(evento.ReceivedTime).ToString());
        comando.Parameters.AddWithValue("$dispositivo", evento.Key.DeviceId);
        comando.Parameters.AddWithValue("$decisao", (object?)decisionId?.ToString() ?? DBNull.Value);
        comando.Parameters.AddWithValue("$comando", comandoEmitido.Command);
        comando.Parameters.AddWithValue("$parametros", (object?)comandoEmitido.ParamsJson ?? DBNull.Value);
        comando.Parameters.AddWithValue("$em", Iso(evento.ReceivedTime));
        comando.Parameters.AddWithValue("$idempotencia", comandoEmitido.IdempotencyKey);
        comando.ExecuteNonQuery();
    }

    private static void InserirOutbox(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        DateTimeOffset em,
        OutboxItem item)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT OR IGNORE INTO outbox
                (id, aggregate_type, aggregate_id, payload_json, priority,
                 connector, idempotency_key, created_at)
            VALUES
                ($id, $tipo, $agregado, $conteudo, $prioridade, $conector, $idempotencia, $em);
            """;

        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(em).ToString());
        comando.Parameters.AddWithValue("$tipo", item.AggregateType);
        comando.Parameters.AddWithValue("$agregado", item.AggregateId);
        comando.Parameters.AddWithValue("$conteudo", item.PayloadJson);
        comando.Parameters.AddWithValue("$prioridade", item.Priority);
        comando.Parameters.AddWithValue("$conector", item.Connector);
        comando.Parameters.AddWithValue("$idempotencia", item.IdempotencyKey);
        comando.Parameters.AddWithValue("$em", Iso(em));
        comando.ExecuteNonQuery();
    }

    private static string? SerializarTrilha(Decision decisao) =>
        decisao.Trace.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(decisao.Trace);

    private static string Iso(DateTimeOffset valor) => valor.ToString("O", CultureInfo.InvariantCulture);
}
