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

/// <summary>A memória de bilhetes de um equipamento simulado.</summary>
/// <param name="NaMemoria">Os que ainda não foram devolvidos, na ordem.</param>
/// <param name="AConfirmar">O último devolvido, ainda sem confirmação (só com <see cref="SimulatedDevice.ConfirmaNaProximaColeta"/>).</param>
public sealed record MemoriaDeBilhetes(IReadOnlyList<Bilhete> NaMemoria, Bilhete? AConfirmar);

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
    private readonly LinkedList<Bilhete> _bilhetes = new();
    private Bilhete? _aConfirmar;
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

    /// <summary>Quantas vezes o relógio foi acertado (<c>EnviarRelogio</c>).</summary>
    public int AcertosDeRelogio { get; set; }

    /// <summary>Quando definido, só as funções de relógio devolvem este retorno bruto.</summary>
    public int? RetornoDoRelogio { get; set; }

    /// <summary>
    /// Quando definido, só o envio da configuração completa devolve este retorno bruto, e a
    /// configuração não conta como recebida.
    /// </summary>
    /// <remarks>
    /// É o <c>EnviarConfiguracoes</c> (EI-030) recusado com a conexão de pé: o teste da Etapa
    /// A.5 (salva × aplicada) precisa dele para provar que a versão aplicada não muda sem o
    /// retorno 0. <see cref="RetornoForcado"/> não serve, porque derruba também a conexão.
    /// </remarks>
    public int? RetornoDaConfiguracao { get; set; }

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

    /// <summary>Mensagens temporárias exibidas, na ordem.</summary>
    public List<(string Texto, TimeSpan Duracao)> MensagensTemporarias { get; } = [];

    /// <summary>Configurações completas recebidas, na ordem.</summary>
    public List<DeviceConfiguration> ConfiguracoesRecebidas { get; } = [];

    /// <summary>
    /// Etapas da sequência oficial de conexão recebidas, na ordem (Etapa A.7 do docs/35; chave
    /// <c>catraca.sequencia_oficial</c>). Vazia com a chave desligada.
    /// </summary>
    public List<EtapaDaSequenciaOficial> EtapasDaSequenciaOficial { get; } = [];

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
            _bilhetes.AddLast(b);
        }

        return this;
    }

    /// <summary>
    /// Como a memória trata o bilhete devolvido. Falso (padrão): sai na hora em que é devolvido,
    /// a leitura literal de FUN:40 ("remove o bilhete"). Verdadeiro: fica "a confirmar" até o
    /// próximo <c>ColetarBilhete</c>; se a conexão cair antes, volta na próxima conexão como
    /// tipo 128, "já retornado em coleta anterior" (manual 5.2.2).
    /// </summary>
    /// <remarks>
    /// Qual das duas a catraca faz é <c>A_CONFIRMAR_COM_TOPDATA</c> (ensaio NOVO-INT-REC-07,
    /// docs/21 §6E). O tipo 128 só faz sentido se existir alguma confirmação; por isso a Etapa A.9
    /// grava antes de pedir o próximo e deduplica o 128 — vale nas duas. Também é A_CONFIRMAR
    /// que o 128 traga a data e o código do original; aqui traz.
    /// </remarks>
    public bool ConfirmaNaProximaColeta { get; set; }

    /// <summary>Quantos bilhetes ainda estão na memória (fora o que espera confirmação).</summary>
    public int BilhetesNaMemoria => _bilhetes.Count;

    /// <summary>A memória de bilhetes, para guardar fora do processo (teste de queda).</summary>
    public MemoriaDeBilhetes ExportarBilhetes() => new([.. _bilhetes], _aConfirmar);

    /// <summary>Restaura uma memória exportada, como se o equipamento nunca tivesse desligado.</summary>
    public void RestaurarBilhetes(MemoriaDeBilhetes memoria)
    {
        ArgumentNullException.ThrowIfNull(memoria);
        _bilhetes.Clear();
        foreach (var b in memoria.NaMemoria)
        {
            _bilhetes.AddLast(b);
        }

        _aConfirmar = memoria.AConfirmar;
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

    internal Bilhete? ProximoBilhete()
    {
        // Pedir o próximo confirma o anterior.
        _aConfirmar = null;

        if (_bilhetes.First is not { } primeiro)
        {
            return null;
        }

        _bilhetes.RemoveFirst();
        if (ConfirmaNaProximaColeta)
        {
            _aConfirmar = primeiro.Value;
        }

        return primeiro.Value;
    }

    /// <summary>
    /// Conexão nova: o bilhete devolvido e não confirmado volta à frente da memória como tipo 128.
    /// </summary>
    internal void AoConectar()
    {
        if (_aConfirmar is { } pendente)
        {
            _bilhetes.AddFirst(pendente with { Tipo = Bilhete.TipoRepetido });
            _aConfirmar = null;
        }
    }

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
