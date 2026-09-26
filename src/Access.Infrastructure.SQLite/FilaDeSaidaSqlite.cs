using System.Globalization;
using Microsoft.Data.Sqlite;
using Sync.Core;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// A outbox durável, servida ao drenador.
/// </summary>
/// <remarks>
/// <para>
/// Toda a política — ordem, repetição, cartas mortas — mora em <c>Sync.Core</c>. Aqui só
/// há SQL. A separação é o que permite testar a política sem banco e o banco sem rede.
/// </para>
/// <para>
/// <b>Datas são gravadas normalizadas em UTC</b> (<c>"O"</c> com deslocamento
/// <c>+00:00</c>). Em SQLite a comparação é de texto: misturar deslocamentos faria
/// <c>next_attempt_at &lt;= agora</c> mentir para quem estivesse em outro fuso.
/// </para>
/// </remarks>
public sealed class FilaDeSaidaSqlite : IFilaDeSaida
{
    private readonly SqliteConnectionFactory _fabrica;

    public FilaDeSaidaSqlite(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ConectoresComPendenciaAsync(
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        cancelamento.ThrowIfCancellationRequested();

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        // Ordena pelo item mais urgente de cada conector: o destino que guarda a
        // revogação de um cartão roubado é atendido antes do que só tem histórico.
        comando.CommandText =
            """
            SELECT connector
            FROM outbox
            WHERE sent_at IS NULL
              AND (next_attempt_at IS NULL OR next_attempt_at <= $agora)
            GROUP BY connector
            ORDER BY MIN(priority) ASC, MIN(created_at) ASC;
            """;
        comando.Parameters.AddWithValue("$agora", Iso(agora));

        var conectores = new List<string>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            conectores.Add(leitor.GetString(0));
        }

        return Task.FromResult<IReadOnlyList<string>>(conectores);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ItemDeSaida>> ProximosAsync(
        string conector,
        int limite,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conector);
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        cancelamento.ThrowIfCancellationRequested();

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, connector, aggregate_type, aggregate_id, payload_json,
                   priority, idempotency_key, attempts, created_at
            FROM outbox
            WHERE connector = $conector
              AND sent_at IS NULL
              AND (next_attempt_at IS NULL OR next_attempt_at <= $agora)
            ORDER BY priority ASC, created_at ASC
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$conector", conector);
        comando.Parameters.AddWithValue("$agora", Iso(agora));
        comando.Parameters.AddWithValue("$limite", limite);

        var itens = new List<ItemDeSaida>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            itens.Add(new ItemDeSaida(
                Id: leitor.GetString(0),
                Conector: leitor.GetString(1),
                TipoDoAgregado: leitor.GetString(2),
                IdDoAgregado: leitor.GetString(3),
                PayloadJson: leitor.GetString(4),
                Prioridade: leitor.GetInt32(5),
                ChaveDeIdempotencia: leitor.GetString(6),
                Tentativas: leitor.GetInt32(7),
                CriadoEm: DateTimeOffset.Parse(leitor.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return Task.FromResult<IReadOnlyList<ItemDeSaida>>(itens);
    }

    /// <inheritdoc />
    public Task MarcarEnviadosAsync(
        IReadOnlyCollection<string> ids,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancelamento.ThrowIfCancellationRequested();

        if (ids.Count == 0)
        {
            return Task.CompletedTask;
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;

        // Uma transação para o lote inteiro: ou o lote confirmado some da fila, ou nada
        // some. Confirmar metade e cair no meio faria a outra metade ser reenviada — o
        // que é seguro, mas é trabalho e ruído que não precisam existir.
        comando.CommandText = "UPDATE outbox SET sent_at = $em WHERE id = $id AND sent_at IS NULL;";
        var parametroEm = comando.Parameters.Add("$em", SqliteType.Text);
        var parametroId = comando.Parameters.Add("$id", SqliteType.Text);
        parametroEm.Value = Iso(agora);

        foreach (var id in ids)
        {
            parametroId.Value = id;
            comando.ExecuteNonQuery();
        }

        transacao.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AdiarAsync(
        string id,
        int tentativas,
        DateTimeOffset proximaTentativaEm,
        string erro,
        CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancelamento.ThrowIfCancellationRequested();

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            UPDATE outbox
            SET attempts = $tentativas, next_attempt_at = $proxima, last_error = $erro
            WHERE id = $id AND sent_at IS NULL;
            """;
        comando.Parameters.AddWithValue("$tentativas", tentativas);
        comando.Parameters.AddWithValue("$proxima", Iso(proximaTentativaEm));
        comando.Parameters.AddWithValue("$erro", Truncar(erro));
        comando.Parameters.AddWithValue("$id", id);
        comando.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoverParaCartasMortasAsync(
        string id,
        string erro,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancelamento.ThrowIfCancellationRequested();

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        // Copiar e apagar na MESMA transação. O contrário — apagar e depois copiar —
        // perde o item se o processo morrer no meio, e perder um item é a única coisa
        // que esta fila não pode fazer.
        using (var copia = conexao.CreateCommand())
        {
            copia.Transaction = transacao;
            copia.CommandText =
                """
                INSERT INTO dead_letter
                    (id, outbox_id, connector, aggregate_type, aggregate_id, payload_json,
                     priority, idempotency_key, attempts, error, created_at, failed_at)
                SELECT $novoId, id, connector, aggregate_type, aggregate_id, payload_json,
                       priority, idempotency_key, attempts, $erro, created_at, $em
                FROM outbox
                WHERE id = $id;
                """;
            copia.Parameters.AddWithValue("$novoId", Guid.CreateVersion7(agora).ToString());
            copia.Parameters.AddWithValue("$erro", Truncar(erro));
            copia.Parameters.AddWithValue("$em", Iso(agora));
            copia.Parameters.AddWithValue("$id", id);
            copia.ExecuteNonQuery();
        }

        using (var remocao = conexao.CreateCommand())
        {
            remocao.Transaction = transacao;
            remocao.CommandText = "DELETE FROM outbox WHERE id = $id;";
            remocao.Parameters.AddWithValue("$id", id);
            remocao.ExecuteNonQuery();
        }

        transacao.Commit();
        return Task.CompletedTask;
    }

    /// <summary>Quantos itens aguardam envio, por conector.</summary>
    /// <remarks>É o número que a operação olha para saber se o backlog está encolhendo.</remarks>
    public IReadOnlyDictionary<string, long> BacklogPorConector()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            "SELECT connector, COUNT(*) FROM outbox WHERE sent_at IS NULL GROUP BY connector;";

        var resultado = new Dictionary<string, long>(StringComparer.Ordinal);
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            resultado[leitor.GetString(0)] = leitor.GetInt64(1);
        }

        return resultado;
    }

    /// <summary>Itens em cartas mortas ainda não reprocessados.</summary>
    public long ContarCartasMortas()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM dead_letter WHERE reprocessed_at IS NULL;";
        return (long)(comando.ExecuteScalar() ?? 0L);
    }

    // O erro vai para o banco e para a tela. Um stack trace inteiro polui as duas coisas,
    // e o diagnóstico está sempre nas primeiras linhas.
    private static string Truncar(string erro) =>
        string.IsNullOrWhiteSpace(erro) ? "(sem detalhe)"
        : erro.Length <= 500 ? erro
        : erro[..500];

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
