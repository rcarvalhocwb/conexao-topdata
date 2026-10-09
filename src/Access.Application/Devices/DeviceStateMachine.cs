using System.Collections.Frozen;
using Access.Domain.Devices;

namespace Access.Application.Devices;

/// <summary>Uma transição possível da máquina de estados.</summary>
public readonly record struct StateTransition(DeviceState From, DeviceTrigger Trigger, DeviceState To);

/// <summary>Registro auditável de uma transição que ocorreu.</summary>
public sealed record TransitionRecord(
    DeviceState From,
    DeviceTrigger Trigger,
    DeviceState To,
    DateTimeOffset At,
    string CorrelationId);

/// <summary>
/// Máquina de estados de um equipamento. <b>Pura</b>: não toca rede, DLL, banco nem
/// relógio do sistema.
/// </summary>
/// <remarks>
/// <para>
/// A tabela de transições é dado, não código espalhado por <c>if</c>s — o que permite
/// testá-la exaustivamente e simulá-la inteira sem hardware, como exige
/// docs/03-arquitetura.md, seção 5.
/// </para>
/// <para>
/// A sequência de configuração segue os nomes e a ordem do Manual de Integração SDK
/// Inner Acesso, seção 2.1.2.
/// </para>
/// </remarks>
public sealed class DeviceStateMachine
{
    private static readonly FrozenSet<StateTransition> Table = BuildTable();

    /// <summary>
    /// Folga somada ao tempo do relé 1 no prazo de <see cref="DeviceState.MonitoraGiroCatraca"/>
    /// (<see cref="PrazoEfetivo"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Três segundos é a folga que a própria tabela já embute: 8 s de espera pelo giro para o
    /// relé 1 padrão de 5 s (docs/34 §4.2, regra 11). Com ela, o prazo efetivo do padrão é o da
    /// tabela, sem mudança; com o relé mais longo, o prazo cresce junto e nunca corta a janela
    /// em que o braço está liberado.
    /// </para>
    /// <para>
    /// A folga cobre o que acontece depois que o relé fecha: o braço que já começou a girar
    /// termina o giro, a catraca emite a origem 5 ou 6, e o laço leva uma volta para lê-la. Ela
    /// não é a única proteção: o laço só desiste depois de uma espera que voltou <b>sem evento</b>
    /// (<c>DevicePump</c>), então um giro que já está na fila da DLL nunca é cortado, por mais
    /// lenta que tenha sido a volta. O valor real da folga é medido na bancada
    /// (NOVO-HIL-GIRO-04, NOVO-LOAD-LOOP-01); trocá-lo é mudar esta constante.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan MargemDoGiro = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Prazo de cada estado, medido desde a <b>entrada</b> nele (<see cref="EstadoAtualDesde"/>).
    /// </summary>
    /// <remarks>
    /// Quem aplica é o laço do worker (<c>DevicePump</c>, Etapa I.1b do docs/36, capacidade C1):
    /// ao estourar, dispara <see cref="DeviceTrigger.TempoEsgotado"/>, e o destino é o desta
    /// tabela de transições. Até a I.1b nada disparava esse gatilho (achado F9,
    /// docs/36-anexos/01-engenheiro-topdata.md §1), e o prazo era só documentação. O prazo de
    /// <see cref="DeviceState.ValidarAcesso"/> é orçamento da decisão, não prazo de passo: o laço
    /// não o aplica, porque o destino da tabela é liberar (ver <c>DevicePump</c>).
    /// </remarks>
    private static readonly FrozenDictionary<DeviceState, TimeSpan> Timeouts = new Dictionary<DeviceState, TimeSpan>
    {
        [DeviceState.Discovering] = TimeSpan.FromSeconds(30),
        [DeviceState.Conectar] = TimeSpan.FromSeconds(5),
        [DeviceState.LendoIdentidade] = TimeSpan.FromSeconds(5),
        [DeviceState.EnviarCfgOffline] = TimeSpan.FromSeconds(30),
        [DeviceState.EnviarConfigMudOnlineOffline] = TimeSpan.FromSeconds(30),
        [DeviceState.EnviarCfgOnline] = TimeSpan.FromSeconds(30),
        [DeviceState.ConfigurarEntradasOnline] = TimeSpan.FromSeconds(10),
        [DeviceState.EnviarMsgPadrao] = TimeSpan.FromSeconds(10),
        [DeviceState.ValidarAcesso] = TimeSpan.FromMilliseconds(150),
        [DeviceState.LiberarCatraca] = TimeSpan.FromSeconds(5),
        [DeviceState.MonitoraGiroCatraca] = TimeSpan.FromSeconds(8),
        [DeviceState.ColetarBilhetes] = TimeSpan.FromMinutes(10),
    }.ToFrozenDictionary();

    /// <summary>
    /// Quantas transições ficam em memória para diagnóstico imediato.
    /// </summary>
    /// <remarks>
    /// O histórico é <b>limitado</b> de propósito. Um ensaio de soak mostrou que a lista
    /// sem limite retinha cerca de 400 bytes por evento: 115 MB em 280 mil eventos, e
    /// crescendo em linha reta. Num evento de horas, isso derruba o worker.
    /// A trilha durável de auditoria vai para o banco (tabela
    /// <c>device_state_transition</c>); o que fica aqui é só a janela recente, que é o
    /// que serve para responder "o que estava acontecendo quando travou".
    /// </remarks>
    public const int TamanhoDoHistorico = 100;

    private readonly Queue<TransitionRecord> _history = new(TamanhoDoHistorico);

    public DeviceStateMachine(string deviceId, DeviceState initial = DeviceState.Disabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        DeviceId = deviceId;
        Current = initial;
    }

    public string DeviceId { get; }

    public DeviceState Current { get; private set; }

    /// <summary>
    /// Últimas transições, da mais antiga para a mais recente. Janela limitada a
    /// <see cref="TamanhoDoHistorico"/>.
    /// </summary>
    public IReadOnlyCollection<TransitionRecord> History => _history;

    /// <summary>Total de transições desde o início, mesmo as já descartadas da janela.</summary>
    public long TotalDeTransicoes { get; private set; }

    /// <summary>Tempo-limite do estado atual, quando houver.</summary>
    public TimeSpan? CurrentTimeout => Timeouts.TryGetValue(Current, out var t) ? t : null;

    /// <summary>Tempo-limite de um estado qualquer, quando houver.</summary>
    public static TimeSpan? TimeoutFor(DeviceState state) =>
        Timeouts.TryGetValue(state, out var t) ? t : null;

    /// <summary>
    /// Desde quando a máquina está no estado atual: o instante da última transição que
    /// <b>mudou</b> de estado. Nulo até a primeira.
    /// </summary>
    /// <remarks>
    /// Transição para o mesmo estado (<see cref="DeviceTrigger.SemEventos"/> esperando o giro,
    /// <see cref="DeviceTrigger.BilheteColetado"/> na coleta) não conta: o prazo é medido desde a
    /// entrada (C1, docs/36). Não sai do histórico porque ele é limitado e as voltas sem evento o
    /// renovam inteiro em segundos.
    /// </remarks>
    public DateTimeOffset? EstadoAtualDesde { get; private set; }

    /// <summary>
    /// Prazo de um estado para uma configuração de catraca: o da tabela, exceto em
    /// <see cref="DeviceState.MonitoraGiroCatraca"/>, que é o maior entre o da tabela e o tempo
    /// do relé 1 mais <see cref="MargemDoGiro"/>.
    /// </summary>
    /// <remarks>
    /// O relé 1 fica liberado por <see cref="DeviceConfiguration.TempoDoAcionamento1"/> segundos
    /// (0 a 50; FUN:17). Um prazo fixo de 8 s cortaria a espera de um relé de 20 s no meio: a
    /// pessoa giraria com o leitor já rearmado e a passagem dela viraria giro sem liberação. Com
    /// o tempo do relé no prazo, a desistência só vem depois que a catraca já deveria ter mandado
    /// a origem 5. A origem 5 continua sendo o caminho normal; o prazo é a rede de segurança.
    /// </remarks>
    public static TimeSpan? PrazoEfetivo(DeviceState estado, DeviceConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var daTabela = TimeoutFor(estado);
        if (estado is not DeviceState.MonitoraGiroCatraca || daTabela is not { } tabela)
        {
            return daTabela;
        }

        var doRele = TimeSpan.FromSeconds(configuracao.TempoDoAcionamento1) + MargemDoGiro;
        return doRele > tabela ? doRele : tabela;
    }

    /// <summary>Todas as transições declaradas.</summary>
    public static IReadOnlyCollection<StateTransition> Transitions => Table;

    /// <summary>Verdadeiro se o gatilho é aceito no estado atual.</summary>
    public bool CanFire(DeviceTrigger trigger) => Resolve(Current, trigger) is not null;

    /// <summary>
    /// Aplica o gatilho. Devolve <c>false</c> e <b>não muda o estado</b> quando a
    /// transição não existe.
    /// </summary>
    /// <remarks>
    /// Transição inexistente não lança: num laço de comunicação, uma exceção por evento
    /// inesperado derrubaria o worker e, com ele, até 20 catracas. O caso é registrado e
    /// segue.
    /// </remarks>
    public bool TryFire(DeviceTrigger trigger, DateTimeOffset at, string correlationId, out TransitionRecord? record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var destino = Resolve(Current, trigger);
        if (destino is null)
        {
            record = null;
            return false;
        }

        record = new TransitionRecord(Current, trigger, destino.Value, at, correlationId);

        _history.Enqueue(record);
        if (_history.Count > TamanhoDoHistorico)
        {
            _history.Dequeue();
        }

        TotalDeTransicoes++;
        if (destino.Value != Current || EstadoAtualDesde is null)
        {
            EstadoAtualDesde = at;
        }

        Current = destino.Value;
        return true;
    }

    private static DeviceState? Resolve(DeviceState from, DeviceTrigger trigger)
    {
        foreach (var t in Table)
        {
            if (t.From == from && t.Trigger == trigger)
            {
                return t.To;
            }
        }

        return null;
    }

    private static FrozenSet<StateTransition> BuildTable()
    {
        var t = new List<StateTransition>();

        void Add(DeviceState from, DeviceTrigger trigger, DeviceState to) =>
            t.Add(new StateTransition(from, trigger, to));

        // --- Entrada em operação ---
        Add(DeviceState.Disabled, DeviceTrigger.Habilitar, DeviceState.Discovering);
        Add(DeviceState.Discovering, DeviceTrigger.EquipamentoApareceu, DeviceState.Conectar);
        Add(DeviceState.Discovering, DeviceTrigger.TempoEsgotado, DeviceState.Degradado);

        // --- Conexão e identidade ---
        Add(DeviceState.Conectar, DeviceTrigger.ConexaoOk, DeviceState.LendoIdentidade);
        Add(DeviceState.Conectar, DeviceTrigger.ConexaoFalhou, DeviceState.Reconectar);
        Add(DeviceState.Conectar, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);
        Add(DeviceState.Reconectar, DeviceTrigger.EquipamentoApareceu, DeviceState.Conectar);
        Add(DeviceState.Reconectar, DeviceTrigger.Quarentenar, DeviceState.Quarentena);

        Add(DeviceState.LendoIdentidade, DeviceTrigger.IdentidadeLida, DeviceState.VerificandoCompatibilidade);
        Add(DeviceState.LendoIdentidade, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);
        Add(DeviceState.LendoIdentidade, DeviceTrigger.TempoEsgotado, DeviceState.Quarentena);

        // Nada é configurado antes da compatibilidade ser confirmada (ADR-0010).
        Add(DeviceState.VerificandoCompatibilidade, DeviceTrigger.CompatibilidadeOk, DeviceState.EnviarCfgOffline);
        Add(DeviceState.VerificandoCompatibilidade, DeviceTrigger.CompatibilidadeRecusada, DeviceState.FirmwareIncompativel);

        // --- Sequência de configuração, na ordem do manual (seção 2.1.2) ---
        Add(DeviceState.EnviarCfgOffline, DeviceTrigger.ConfiguracaoEnviada, DeviceState.EnviarConfigMudOnlineOffline);
        Add(DeviceState.EnviarCfgOffline, DeviceTrigger.ConfiguracaoFalhou, DeviceState.Reconectar);
        Add(DeviceState.EnviarCfgOffline, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);

        Add(DeviceState.EnviarConfigMudOnlineOffline, DeviceTrigger.ConfiguracaoEnviada, DeviceState.EnviarCfgOnline);
        Add(DeviceState.EnviarConfigMudOnlineOffline, DeviceTrigger.ConfiguracaoFalhou, DeviceState.Reconectar);
        Add(DeviceState.EnviarConfigMudOnlineOffline, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);

        Add(DeviceState.EnviarCfgOnline, DeviceTrigger.ConfiguracaoEnviada, DeviceState.SincronizandoDadosOffline);
        Add(DeviceState.EnviarCfgOnline, DeviceTrigger.ConfiguracaoFalhou, DeviceState.Reconectar);
        Add(DeviceState.EnviarCfgOnline, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);

        // A lista local vai antes de entrar em operação: senão, uma queda para off-line
        // deixaria o equipamento cego (ADR-0017).
        Add(DeviceState.SincronizandoDadosOffline, DeviceTrigger.DadosOfflineSincronizados, DeviceState.ConfigurarEntradasOnline);
        Add(DeviceState.SincronizandoDadosOffline, DeviceTrigger.ConfiguracaoFalhou, DeviceState.Degradado);

        Add(DeviceState.ConfigurarEntradasOnline, DeviceTrigger.ConfiguracaoEnviada, DeviceState.EnviarMsgPadrao);
        Add(DeviceState.ConfigurarEntradasOnline, DeviceTrigger.ConfiguracaoFalhou, DeviceState.Reconectar);
        Add(DeviceState.ConfigurarEntradasOnline, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);

        Add(DeviceState.EnviarMsgPadrao, DeviceTrigger.ConfiguracaoEnviada, DeviceState.Polling);
        Add(DeviceState.EnviarMsgPadrao, DeviceTrigger.ConfiguracaoFalhou, DeviceState.Reconectar);
        Add(DeviceState.EnviarMsgPadrao, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);

        // --- Operação ---
        Add(DeviceState.Polling, DeviceTrigger.EventoRecebido, DeviceState.ValidarAcesso);
        Add(DeviceState.Polling, DeviceTrigger.SemEventos, DeviceState.Polling);

        // Sinal da catraca (cartão recolhido, sensor, urna cheia, tecla) não é leitura: não
        // decide, não mostra negação e não rearma o leitor (F4, docs/34 §2).
        Add(DeviceState.Polling, DeviceTrigger.SinalDaCatraca, DeviceState.Polling);
        Add(DeviceState.Polling, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);
        Add(DeviceState.Polling, DeviceTrigger.IniciarColetaDeBilhetes, DeviceState.ColetarBilhetes);
        Add(DeviceState.Polling, DeviceTrigger.CairParaListaLocal, DeviceState.OfflineAutonomo);
        Add(DeviceState.Polling, DeviceTrigger.EntrarEmManutencao, DeviceState.Manutencao);

        // Comandos do operador (fase 4b). Só de Polling: com alguém no meio de uma
        // passagem, a vez é da passagem. A liberação manual segue o mesmo caminho da
        // liberação por ingresso — liberar, monitorar o giro, reabilitar o leitor.
        Add(DeviceState.Polling, DeviceTrigger.LiberacaoManualSolicitada, DeviceState.LiberarCatraca);
        Add(DeviceState.Polling, DeviceTrigger.ReconexaoSolicitada, DeviceState.Reconectar);

        Add(DeviceState.ValidarAcesso, DeviceTrigger.AcessoPermitido, DeviceState.LiberarCatraca);
        Add(DeviceState.ValidarAcesso, DeviceTrigger.AcessoNegado, DeviceState.EnviarMsgAcessoNegado);
        // Decisão que não chega no prazo NEGA (fail-secure, D8): nunca libera sem decisão (achado E4-1 do docs/41).
        Add(DeviceState.ValidarAcesso, DeviceTrigger.TempoEsgotado, DeviceState.EnviarMsgAcessoNegado);
        Add(DeviceState.ValidarAcesso, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);

        // Depois de negar, volta a configurar as entradas: é assim que o leitor é
        // reabilitado. Pular este passo é a causa documentada de "leitor travado"
        // (manual, seção 7.2.3).
        Add(DeviceState.EnviarMsgAcessoNegado, DeviceTrigger.MensagemExibida, DeviceState.ConfigurarEntradasOnline);
        Add(DeviceState.EnviarMsgAcessoNegado, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);

        Add(DeviceState.LiberarCatraca, DeviceTrigger.ComandoDeLiberacaoOk, DeviceState.MonitoraGiroCatraca);
        Add(DeviceState.LiberarCatraca, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);
        Add(DeviceState.LiberarCatraca, DeviceTrigger.TempoEsgotado, DeviceState.Reconectar);

        Add(DeviceState.MonitoraGiroCatraca, DeviceTrigger.GiroConfirmado, DeviceState.ConfigurarEntradasOnline);
        Add(DeviceState.MonitoraGiroCatraca, DeviceTrigger.TempoDeAcionamentoEsgotado, DeviceState.ConfigurarEntradasOnline);
        Add(DeviceState.MonitoraGiroCatraca, DeviceTrigger.SemEventos, DeviceState.MonitoraGiroCatraca);
        Add(DeviceState.MonitoraGiroCatraca, DeviceTrigger.TempoEsgotado, DeviceState.ConfigurarEntradasOnline);
        Add(DeviceState.MonitoraGiroCatraca, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);

        // --- Bilhetes ---
        Add(DeviceState.ColetarBilhetes, DeviceTrigger.BilheteColetado, DeviceState.ColetarBilhetes);
        Add(DeviceState.ColetarBilhetes, DeviceTrigger.SemBilhetes, DeviceState.Polling);
        Add(DeviceState.ColetarBilhetes, DeviceTrigger.ErroDeComunicacao, DeviceState.Reconectar);
        Add(DeviceState.ColetarBilhetes, DeviceTrigger.TempoEsgotado, DeviceState.Degradado);

        // --- Degradação e volta ---
        Add(DeviceState.OfflineAutonomo, DeviceTrigger.ConexaoOk, DeviceState.ColetarBilhetes);
        Add(DeviceState.OfflineAutonomo, DeviceTrigger.Quarentenar, DeviceState.Quarentena);
        Add(DeviceState.Degradado, DeviceTrigger.EquipamentoApareceu, DeviceState.Conectar);
        Add(DeviceState.Degradado, DeviceTrigger.Quarentenar, DeviceState.Quarentena);
        Add(DeviceState.Quarentena, DeviceTrigger.EntrarEmManutencao, DeviceState.Manutencao);
        Add(DeviceState.Quarentena, DeviceTrigger.Habilitar, DeviceState.Discovering);
        Add(DeviceState.FirmwareIncompativel, DeviceTrigger.EntrarEmManutencao, DeviceState.Manutencao);
        Add(DeviceState.Manutencao, DeviceTrigger.SairDeManutencao, DeviceState.Discovering);

        // --- Saídas universais ---
        foreach (var estado in Enum.GetValues<DeviceState>())
        {
            if (estado is DeviceState.Disabled or DeviceState.FalhaFatalDeDependencia)
            {
                continue;
            }

            Add(estado, DeviceTrigger.Desabilitar, DeviceState.Disabled);
            Add(estado, DeviceTrigger.DependenciaFatal, DeviceState.FalhaFatalDeDependencia);
        }

        return t.ToFrozenSet();
    }
}
