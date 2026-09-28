using Access.Application.Devices;
using Access.Domain.Devices;

namespace Simulator;

/// <summary>Um evento programado para o simulador entregar.</summary>
/// <param name="Origem">Origem a emitir.</param>
/// <param name="Cartao">Conteúdo do campo cartão, quando houver.</param>
/// <param name="Complemento">Complemento da origem.</param>
/// <param name="AtrasoAntes">Tempo a "passar" antes de entregar este evento.</param>
public sealed record ScriptedEvent(
    EventOrigin Origem,
    string? Cartao = null,
    byte Complemento = 0,
    TimeSpan AtrasoAntes = default);

/// <summary>
/// Um equipamento simulado, roteirizável.
/// </summary>
/// <remarks>
/// Reproduz os comportamentos que a documentação descreve — inclusive os ruins: retorno
/// 8, queda de comunicação, urna cheia, cartão preso, giro que não acontece e relógio
/// errado. Um defeito que só aparece em campo, uma vez, vira roteiro aqui e nunca mais
/// escapa. Ver docs/09-plano-de-bancada.md, seção "Gravação para regressão".
/// </remarks>
public sealed class SimulatedDevice : IDisposable
{
    private readonly ManualResetEventSlim _destravar = new(initialState: false);
    private int _entrouNoLacoTravado;
    private readonly Queue<ScriptedEvent> _eventos = new();
    private readonly Queue<Bilhete> _bilhetes = new();
    private long _sequencia;

    public SimulatedDevice(int inner)
    {
        Inner = inner;
        BootId = $"boot-{inner}-1";
    }

    public int Inner { get; }

    public string BootId { get; private set; }

    /// <summary>Identidade devolvida por <c>ReceberVersaoFirmware</c>.</summary>
    public FirmwareInfo Firmware { get; set; } = new(16, 1, 4, 2, 0, TemBiometria: false);

    /// <summary>Desvio do relógio do equipamento em relação ao da borda.</summary>
    public TimeSpan DesvioDeRelogio { get; set; }

    /// <summary>Quando definido, toda chamada devolve este retorno bruto.</summary>
    /// <remarks>Use 8 para reproduzir o GPF documentado.</remarks>
    public int? RetornoForcado { get; set; }

    /// <summary>Simula cabo removido ou equipamento desligado.</summary>
    public bool Desconectado { get; set; }

    /// <summary>
    /// Simula o laço bloqueante travado: <c>AguardarEvento</c> fica preso, segurando o
    /// acesso à DLL, até <see cref="LiberarLaco"/> ou até estourar o tempo de espera.
    /// </summary>
    /// <remarks>
    /// Segurar de verdade é o ponto: uma thread presa lá dentro é o que impede qualquer
    /// outra de entrar, e é isso que o watchdog do worker precisa detectar.
    /// </remarks>
    public bool LacoTravado { get; set; }

    /// <summary>Destrava o laço, encerrando a espera.</summary>
    public void LiberarLaco() => _destravar.Set();

    /// <summary>
    /// Verdadeiro quando uma thread está realmente presa no laço travado, segurando o
    /// acesso à DLL. É o sinal que os testes esperam — e não "a chamada começou".
    /// </summary>
    public bool EntrouNoLacoTravado => Volatile.Read(ref _entrouNoLacoTravado) == 1;

    /// <summary>Quando verdadeiro, a próxima leitura na urna emite a origem 20.</summary>
    public bool UrnaCheia { get; set; }

    /// <summary>Contagem de liberações de giro pedidas, por sentido.</summary>
    public Dictionary<GateDirection, int> LiberacoesPedidas { get; } = [];

    /// <summary>Quantas vezes o relé da urna foi acionado.</summary>
    public int AcionamentosDaUrna { get; private set; }

    /// <summary>
    /// Quantas vezes o leitor foi reabilitado. Um ciclo de acesso que não incrementa
    /// este contador deixa a catraca surda para a próxima pessoa.
    /// </summary>
    public int ReabilitacoesDoLeitor { get; private set; }

    /// <summary>Configurações completas recebidas, na ordem.</summary>
    public List<DeviceConfiguration> ConfiguracoesRecebidas { get; } = [];

    /// <summary>Programa eventos para serem entregues em ordem.</summary>
    public SimulatedDevice Roteirizar(params ScriptedEvent[] eventos)
    {
        ArgumentNullException.ThrowIfNull(eventos);
        foreach (var e in eventos)
        {
            _eventos.Enqueue(e);
        }

        return this;
    }

    /// <summary>
    /// Gerador contínuo de eventos, para ensaios de longa duração.
    /// </summary>
    /// <remarks>
    /// Devolver <c>null</c> significa "sem evento agora". Usado quando roteirizar uma
    /// lista finita não serve — num soak de uma hora, a lista teria de ter milhões de
    /// itens e o próprio teste seria o vazamento.
    /// </remarks>
    public Func<ScriptedEvent?>? Gerador { get; set; }

    /// <summary>Programa bilhetes na memória do equipamento.</summary>
    public SimulatedDevice ComBilhetes(params Bilhete[] bilhetes)
    {
        ArgumentNullException.ThrowIfNull(bilhetes);
        foreach (var b in bilhetes)
        {
            _bilhetes.Enqueue(b);
        }

        return this;
    }

    /// <summary>Simula um reinício: novo <c>bootId</c> e sequência recomeçando.</summary>
    public void Reiniciar()
    {
        var geracao = int.Parse(BootId.Split('-')[^1], provider: null) + 1;
        BootId = $"boot-{Inner}-{geracao}";
        _sequencia = 0;
    }

    internal bool TemEventoPendente => _eventos.Count > 0;

    internal ScriptedEvent? ProximoEvento() =>
        _eventos.Count > 0 ? _eventos.Dequeue() : Gerador?.Invoke();

    internal Bilhete? ProximoBilhete() => _bilhetes.Count > 0 ? _bilhetes.Dequeue() : null;

    internal long ProximaSequencia() => Interlocked.Increment(ref _sequencia);

    internal void RegistrarLiberacao(GateDirection direcao) =>
        LiberacoesPedidas[direcao] = LiberacoesPedidas.GetValueOrDefault(direcao) + 1;

    internal void RegistrarAcionamentoDaUrna() => AcionamentosDaUrna++;

    internal void RegistrarReabilitacaoDoLeitor() => ReabilitacoesDoLeitor++;

    /// <summary>Espera o destravamento. Devolve falso quando o tempo estoura.</summary>
    internal bool EsperarDestravar(TimeSpan limite)
    {
        Volatile.Write(ref _entrouNoLacoTravado, 1);
        try
        {
            return _destravar.Wait(limite);
        }
        finally
        {
            Volatile.Write(ref _entrouNoLacoTravado, 0);
        }
    }

    public void Dispose() => _destravar.Dispose();
}
