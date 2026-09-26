using System.Diagnostics.Metrics;

namespace Shared.Observability;

/// <summary>
/// Métricas mínimas da borda, conforme docs/06, seção 5.
/// </summary>
/// <remarks>
/// Usa <see cref="Meter"/> da biblioteca padrão, então exporta por OpenTelemetry sem
/// que o núcleo precise conhecer o exportador.
/// </remarks>
public sealed class MetricasDoEdge : IDisposable
{
    public const string NomeDoMeter = "ConexaoTopdata.Edge";

    private readonly Meter _meter;

    public MetricasDoEdge(string? versao = null)
    {
        _meter = new Meter(NomeDoMeter, versao ?? "1.0.0");

        EventosRecebidos = _meter.CreateCounter<long>(
            "topdata.eventos_recebidos", "evento", "Eventos recebidos dos equipamentos.");

        OrigensDesconhecidas = _meter.CreateCounter<long>(
            "topdata.origens_desconhecidas", "evento",
            "Eventos cuja origem não consta da tabela oficial. Crescimento aqui é firmware novo ou lacuna de documentação.");

        Decisoes = _meter.CreateCounter<long>(
            "topdata.decisoes", "decisão", "Decisões de acesso, por resultado e motivo.");

        PassagensConfirmadas = _meter.CreateCounter<long>(
            "topdata.passagens_confirmadas", "passagem",
            "Passagens com prova física (origem 6). NÃO é o mesmo que autorizações.");

        AutorizacoesSemConfirmacao = _meter.CreateCounter<long>(
            "topdata.autorizacoes_sem_confirmacao", "autorização",
            "Autorizou mas não houve prova de giro. Métrica de saúde da instalação.");

        Reconexoes = _meter.CreateCounter<long>(
            "topdata.reconexoes", "reconexão", "Reconexões por equipamento.");

        RetornosNativos = _meter.CreateCounter<long>(
            "topdata.retornos_nativos", "retorno", "Retornos da DLL, incluindo os desconhecidos.");

        WorkersReiniciados = _meter.CreateCounter<long>(
            "topdata.workers_reiniciados", "reinício", "Workers reiniciados pelo supervisor.");

        LatenciaDaDecisao = _meter.CreateHistogram<double>(
            "topdata.latencia_decisao", "ms", "Tempo da decisão local. Meta: p95 ≤ 100 ms.");

        LatenciaDoComando = _meter.CreateHistogram<double>(
            "topdata.latencia_comando", "ms", "Tempo de uma chamada à DLL.");

        IdadeDoItemMaisAntigoDaOutbox = _meter.CreateHistogram<double>(
            "topdata.outbox_idade_mais_antigo", "s",
            "Idade do item mais antigo ainda não sincronizado. É o sinal de que a sincronização parou.");
    }

    public Counter<long> EventosRecebidos { get; }

    public Counter<long> OrigensDesconhecidas { get; }

    public Counter<long> Decisoes { get; }

    public Counter<long> PassagensConfirmadas { get; }

    public Counter<long> AutorizacoesSemConfirmacao { get; }

    public Counter<long> Reconexoes { get; }

    public Counter<long> RetornosNativos { get; }

    public Counter<long> WorkersReiniciados { get; }

    public Histogram<double> LatenciaDaDecisao { get; }

    public Histogram<double> LatenciaDoComando { get; }

    public Histogram<double> IdadeDoItemMaisAntigoDaOutbox { get; }

    /// <summary>
    /// Observa a quantidade de equipamentos conectados por worker.
    /// </summary>
    public void ObservarConectados(Func<int> leitura, string worker)
    {
        ArgumentNullException.ThrowIfNull(leitura);
        ArgumentException.ThrowIfNullOrWhiteSpace(worker);

        _meter.CreateObservableGauge(
            "topdata.equipamentos_conectados",
            () => new Measurement<int>(leitura(), new KeyValuePair<string, object?>("worker", worker)),
            "equipamento",
            "Equipamentos conectados neste worker.");
    }

    public void Dispose() => _meter.Dispose();
}
