using Access.Application.Devices;
using Shared.Observability;

namespace Edge.Worker;

/// <summary>
/// Laço de comunicação de um worker: uma thread, vários equipamentos, em rodízio.
/// </summary>
/// <remarks>
/// <para>
/// A thread única não é escolha de desempenho: a EasyInner.dll não é thread-safe e a
/// própria Topdata recomenda uma thread dedicada executando a máquina de estados de
/// todas as catracas do processo (manual, seções 2.1.1 e 6.2).
/// </para>
/// <para>
/// <b>Consequência que precisa estar clara:</b> uma catraca travada bloqueia as demais
/// <i>deste worker</i>. O isolamento é entre workers — por isso o teto de equipamentos
/// por processo e o agrupamento por afinidade física (ADR-0005), e por isso o watchdog
/// mata o worker em vez de esperar.
/// </para>
/// </remarks>
public sealed class DeviceGroupLoop
{
    /// <summary>Teto de fábrica, com margem sobre o limite prático de ~30 da Topdata.</summary>
    public const int TetoRecomendado = 20;

    /// <summary>Teto absoluto sem teste de carga aprovado (ADR-0005).</summary>
    public const int TetoAbsoluto = 25;

    private readonly ITopdataInnerAdapter _adapter;
    private readonly DevicePump _pump;
    private readonly List<DeviceSlot> _dispositivos;
    private readonly TimeSpan _limiteDeEspera;
    private readonly LogEstruturado? _log;

    public DeviceGroupLoop(
        ITopdataInnerAdapter adapter,
        IEnumerable<DeviceSlot> dispositivos,
        Watchdog watchdog,
        DevicePump? pump = null,
        TimeSpan? limiteDeEspera = null,
        LogEstruturado? log = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(dispositivos);
        ArgumentNullException.ThrowIfNull(watchdog);

        _dispositivos = [.. dispositivos];

        if (_dispositivos.Count == 0)
        {
            throw new ArgumentException("Um worker sem equipamentos não tem o que fazer.", nameof(dispositivos));
        }

        if (_dispositivos.Count > TetoAbsoluto)
        {
            throw new ArgumentException(
                $"{_dispositivos.Count} equipamentos excede o teto de {TetoAbsoluto} por worker. " +
                "A Topdata documenta ~30 por instância da DLL; acima do teto, distribua em mais workers — " +
                "cada um na sua porta TCP. Ver docs/ADR/ADR-0005 e ADR-0021.",
                nameof(dispositivos));
        }

        var duplicados = _dispositivos.GroupBy(d => d.Inner).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicados.Count > 0)
        {
            throw new ArgumentException(
                $"Número de Inner repetido no mesmo worker: {string.Join(", ", duplicados)}.",
                nameof(dispositivos));
        }

        _adapter = adapter;
        _pump = pump ?? new DevicePump(adapter);
        Watchdog = watchdog;
        _limiteDeEspera = limiteDeEspera ?? TimeSpan.FromMilliseconds(500);
        _log = log;
    }

    public Watchdog Watchdog { get; }

    public IReadOnlyList<DeviceSlot> Dispositivos => _dispositivos;

    /// <summary>Quantas voltas completas o laço deu.</summary>
    public long Voltas { get; private set; }

    /// <summary>Abre a porta de comunicação. Uma única vez, antes do laço.</summary>
    public AdapterResult Iniciar(int porta) => _adapter.AbrirPorta(porta);

    /// <summary>
    /// Dá uma volta no rodízio: um passo por equipamento, batendo o watchdog a cada um.
    /// </summary>
    /// <returns>O que foi feito em cada equipamento, para log.</returns>
    public IReadOnlyList<(int Inner, string Acao)> UmaVolta()
    {
        var acoes = new List<(int, string)>(_dispositivos.Count);

        foreach (var dispositivo in _dispositivos)
        {
            Watchdog.Bater($"inner {dispositivo.Inner} em {dispositivo.Maquina.Current}");

            var estadoAntes = dispositivo.Maquina.Current;
            var acao = _pump.Passo(dispositivo, _limiteDeEspera);
            acoes.Add((dispositivo.Inner, acao));

            // Todo log passa pelo redator: nem a ação nem o número de cartão que ela
            // possa conter chegam ao disco em texto claro.
            _log?.Informacao(acao, $"volta-{Voltas}-inner-{dispositivo.Inner}", new Dictionary<string, object?>
            {
                ["inner"] = dispositivo.Inner,
                ["estadoAntes"] = estadoAntes.ToString(),
                ["estadoDepois"] = dispositivo.Maquina.Current.ToString(),
            });
        }

        Voltas++;
        return acoes;
    }

    /// <summary>
    /// Roda o laço até o cancelamento. <b>Bloqueia a thread que chama</b> — deve ser a
    /// thread dedicada do worker, nunca a da interface.
    /// </summary>
    public void Executar(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            UmaVolta();
        }
    }

    /// <summary>Fecha a porta. Obrigatório ao encerrar.</summary>
    public AdapterResult Encerrar() => _adapter.FecharPorta();
}
