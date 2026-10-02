using System.Globalization;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker.Resiliencia;

namespace Edge.Worker;

/// <summary>Um equipamento sob responsabilidade deste worker.</summary>
public sealed class DeviceSlot
{
    public DeviceSlot(int inner, DeviceConfiguration configuracao, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        Inner = inner;
        Configuracao = configuracao;
        Maquina = new DeviceStateMachine($"inner-{inner}", DeviceState.Discovering);
        Disjuntor = new CircuitBreaker(relogio: relogio);
    }

    public int Inner { get; }

    /// <summary>
    /// Configuração enviada a cada conexão. Só muda por <see cref="TipoDeComando.AplicarConfiguracao"/>,
    /// e só passa a valer na reconexão que o comando provoca (ADR-0020: sempre completa).
    /// </summary>
    public DeviceConfiguration Configuracao { get; internal set; }

    /// <summary>Configuração relida, esperando o comando que a aplica.</summary>
    internal DeviceConfiguration? ConfiguracaoNova { get; set; }

    /// <summary>
    /// Quando a catraca aceitou pela última vez a configuração (retorno 0 de
    /// <c>EnviarConfiguracoes</c>, EI-030). Nulo até o primeiro envio aceito deste worker.
    /// </summary>
    /// <remarks>
    /// Etapa A.5 do docs/35: "salva × aplicada". Não muda quando <see cref="Configuracao"/> é
    /// trocada pelo "Aplicar agora" — essa troca acontece antes da reconexão —, só quando o
    /// envio dá certo. Muda a cada envio aceito, inclusive na reconexão com a mesma
    /// configuração: é a última vez que a catraca a recebeu.
    /// </remarks>
    public DateTimeOffset? ConfiguracaoAplicadaEm { get; internal set; }

    /// <summary>
    /// A versão (<see cref="VersaoDaConfiguracao"/>) da configuração que a catraca aceitou por
    /// último. Nula até o primeiro envio aceito deste worker.
    /// </summary>
    public string? ConfiguracaoVersao { get; internal set; }

    /// <summary>Comandos do operador esperando a catraca ficar livre (Polling).</summary>
    internal Queue<ComandoDeCatraca> Comandos { get; } = new();

    /// <summary>O comando sendo executado agora, quando leva mais de um passo.</summary>
    internal ComandoEmCurso? EmCurso { get; set; }

    /// <summary>Quantos comandos esperam a vez.</summary>
    public int ComandosNaFila => Comandos.Count + (EmCurso is null ? 0 : 1);

    /// <summary>
    /// O bilhete que já saiu da catraca e ainda não está durável na base (R-68; Etapa A.9).
    /// Enquanto houver um, o laço não chama <c>ColetarBilhete</c> de novo: o próximo passo desta
    /// catraca é tentar gravá-lo.
    /// </summary>
    public BilheteColetado? BilheteAGravar { get; internal set; }

    /// <summary>A coleta em andamento: identificação e contagens.</summary>
    internal ColetaDeBilhetes? Coleta { get; set; }

    /// <summary>Tentativas de gravar um bilhete coletado que a base recusou (base ocupada, por exemplo).</summary>
    public long FalhasAoGravarBilhete { get; internal set; }

    /// <summary>Entrega um comando para ser executado quando a catraca estiver livre.</summary>
    /// <param name="comando">O pedido.</param>
    /// <param name="configuracaoNova">
    /// Obrigatória em <see cref="TipoDeComando.AplicarConfiguracao"/>: a configuração
    /// completa, já validada, que a reconexão vai enviar.
    /// </param>
    public void Enfileirar(ComandoDeCatraca comando, DeviceConfiguration? configuracaoNova = null)
    {
        ArgumentNullException.ThrowIfNull(comando);

        if (comando.Inner != Inner)
        {
            throw new ArgumentException($"Comando da catraca {comando.Inner} entregue à catraca {Inner}.", nameof(comando));
        }

        if (comando.Tipo is TipoDeComando.AplicarConfiguracao)
        {
            if (configuracaoNova is null)
            {
                throw new ArgumentNullException(nameof(configuracaoNova), "Aplicar configuração exige a configuração nova.");
            }

            var problemas = configuracaoNova.Validar();
            if (problemas.Count > 0)
            {
                throw new ArgumentException("Configuração nova inválida: " + string.Join(" ", problemas), nameof(configuracaoNova));
            }

            ConfiguracaoNova = configuracaoNova;
        }

        Comandos.Enqueue(comando);
    }

    public DeviceStateMachine Maquina { get; }

    public CircuitBreaker Disjuntor { get; }

    /// <summary>Tentativas seguidas de reconexão, para o cálculo do backoff.</summary>
    public int TentativasDeReconexao { get; internal set; }

    /// <summary>Quando este equipamento pode ser tentado de novo.</summary>
    public DateTimeOffset? EsperarAte { get; internal set; }

    /// <summary>
    /// Quantas vezes a espera por evento voltou com erro (retorno nativo ≠ 0 que não é
    /// falha de dependência).
    /// </summary>
    /// <remarks>
    /// Antes esses retornos viravam "sem eventos" e a queda só aparecia quando o watchdog
    /// pegava (defeito F6, docs/34 §2; ADR-0018: contar, nunca calar).
    /// </remarks>
    public long ErrosDeRecepcao { get; internal set; }

    /// <summary>Identidade lida do equipamento, quando já conhecida.</summary>
    public FirmwareInfo? Firmware { get; internal set; }

    /// <summary>
    /// Último evento recebido deste equipamento.
    /// </summary>
    /// <remarks>
    /// Guarda apenas o último, de propósito. Uma fila aqui cresceria sem limite num
    /// worker que roda por dias, e não teria serventia: a decisão é sempre sobre a
    /// leitura mais recente. Quem precisa de todos os eventos é a persistência, que os
    /// recebe pelo sink do <c>DevicePump</c> no instante em que chegam.
    /// </remarks>
    public DeviceEvent? UltimoEvento { get; internal set; }

    /// <summary>Última decisão tomada para este equipamento.</summary>
    public Decision? UltimaDecisao { get; internal set; }

    /// <summary>Quando acertar o relógio da catraca. Marcado a cada conexão.</summary>
    public DateTimeOffset? AcertarRelogioEm { get; internal set; }

    /// <summary>Quando conferir o relógio da catraca de novo.</summary>
    public DateTimeOffset? ConferirRelogioEm { get; internal set; }

    /// <summary>Último acerto do relógio que a catraca aceitou.</summary>
    public DateTimeOffset? RelogioAcertadoEm { get; internal set; }

    /// <summary>Última conferência do relógio.</summary>
    public DateTimeOffset? RelogioConferidoEm { get; internal set; }

    /// <summary>
    /// Relógio da catraca menos o da borda, na última conferência, em segundos inteiros
    /// (a catraca não guarda fração). Positivo: a catraca está adiantada.
    /// </summary>
    public TimeSpan? DivergenciaDoRelogio { get; internal set; }

    /// <summary>A catraca devolveu uma data impossível (relógio zerado, por exemplo).</summary>
    public bool RelogioInvalido { get; internal set; }

    /// <summary>Verdadeiro quando a última conferência passou do limite de divergência.</summary>
    public bool RelogioDivergente =>
        RelogioInvalido || DivergenciaDoRelogio is { } divergencia && divergencia.Duration() > DevicePump.LimiteDeDivergenciaDoRelogio;
}

/// <summary>Uma coleta de bilhetes: quem a pediu (o comando, quando houver) e o que já foi feito.</summary>
internal sealed class ColetaDeBilhetes(Guid id)
{
    public Guid Id { get; } = id;

    /// <summary>Devolvidos pela catraca (cada um já saiu da memória dela).</summary>
    public int Coletados { get; set; }

    /// <summary>Gravados agora na base.</summary>
    public int Gravados { get; set; }

    /// <summary>Que a base já tinha (mesmo bilhete de novo, ou 128 com o original gravado).</summary>
    public int Repetidos { get; set; }

    public string Resumo() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Coletados} bilhete(s) coletado(s): {Gravados} gravado(s), {Repetidos} já estava(m) na base");
}

/// <summary>Um comando que leva mais de um passo: liberação manual, reconexão, coleta de bilhetes.</summary>
internal sealed class ComandoEmCurso(ComandoDeCatraca comando, DateTimeOffset iniciadoEm)
{
    public ComandoDeCatraca Comando { get; } = comando;

    public DateTimeOffset IniciadoEm { get; } = iniciadoEm;

    /// <summary>A catraca aceitou a liberação.</summary>
    public bool Liberou { get; set; }

    /// <summary>Veio o giro (origem 6) depois da liberação.</summary>
    public bool Girou { get; set; }

    /// <summary>
    /// O laço desistiu de esperar o giro pelo prazo (C1, docs/36), sem origem 5 nem 6. Guarda
    /// o prazo aplicado, para o desfecho dizer qual foi.
    /// </summary>
    public TimeSpan? GiroNaoConfirmadoNoPrazo { get; set; }

    /// <summary>A reconexão pedida já saiu de Polling (para não concluir antes de começar).</summary>
    public bool SaiuDeOperacao { get; set; }
}

/// <summary>
/// Executa um passo da máquina de estados de um equipamento.
/// </summary>
/// <remarks>
/// Um passo faz <b>no máximo uma</b> chamada bloqueante ao adapter. É o que permite ao
/// laço alternar entre equipamentos numa única thread, como o manual recomenda
/// (seção 2.1.1).
/// </remarks>
public sealed class DevicePump
{
    /// <summary>Acima disto o relógio da catraca é dado como divergente (docs/24 §1).</summary>
    public static readonly TimeSpan LimiteDeDivergenciaDoRelogio = TimeSpan.FromSeconds(30);

    /// <summary>De quanto em quanto tempo o relógio é conferido.</summary>
    public static readonly TimeSpan IntervaloDeConferenciaDoRelogio = TimeSpan.FromHours(1);

    /// <summary>
    /// A primeira conferência vem logo depois do acerto: é ela que mostra se a catraca
    /// guardou mesmo a hora enviada.
    /// </summary>
    public static readonly TimeSpan PrimeiraConferenciaDoRelogio = TimeSpan.FromMinutes(1);

    /// <summary>Depois de uma falha no acerto ou na leitura, espera isto para tentar de novo.</summary>
    public static readonly TimeSpan EsperaAposFalhaNoRelogio = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Quanto uma reconexão pedida pelo operador pode levar para a catraca voltar a
    /// atender, antes de o comando ser dado como falho.
    /// </summary>
    public static readonly TimeSpan LimiteDaReconexaoPedida = TimeSpan.FromMinutes(2);

    /// <summary>Quanto esperar pelo giro, ou pelo fim do tempo, de uma liberação manual.</summary>
    public static readonly TimeSpan LimiteDaLiberacaoManual = TimeSpan.FromSeconds(60);

    private readonly ITopdataInnerAdapter _adapter;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly IReadOnlySet<byte> _linhasHomologadas;
    private readonly Func<DeviceEvent, Decision>? _decidir;
    private readonly Action<DeviceEvent>? _aoReceberEvento;
    private readonly bool _acertarRelogioAoDivergir;
    private readonly Action<ComandoDeCatraca, SituacaoDoComando, string>? _aoConcluirComando;
    private readonly Action<string>? _antesDaLiberacaoManual;
    private readonly bool _sequenciaOficial;
    private readonly IGravadorDeBilhetes? _gravadorDeBilhetes;
    private readonly Action<string>? _aoDesistirDoGiro;

    /// <param name="adapter">Acesso à EasyInner.</param>
    /// <param name="relogio">Relógio da borda.</param>
    /// <param name="linhasHomologadas">Linhas de firmware da matriz (ADR-0010).</param>
    /// <param name="decidir">Motor de decisão.</param>
    /// <param name="aoReceberEvento">Recebe cada evento no instante em que chega.</param>
    /// <param name="acertarRelogioAoDivergir">
    /// Acerta sozinho, com a catraca em operação, quando a conferência horária achar
    /// divergência. Desligado por padrão: o fluxo oficial só acerta na conexão, e acertar
    /// com a catraca atendendo é <c>A_CONFIRMAR_COM_TOPDATA</c> (docs/21, INT-CLK-02).
    /// Desligado, a divergência só é informada — e o operador acerta pelo painel.
    /// </param>
    /// <param name="aoConcluirComando">Recebe o desfecho de cada comando do operador.</param>
    /// <param name="antesDaLiberacaoManual">
    /// Chamado com o id do equipamento logo antes de uma liberação manual. O decisor usa
    /// para encerrar a tentativa pendente: o giro que vier é do operador, não do último
    /// ingresso lido.
    /// </param>
    /// <param name="sequenciaOficial">
    /// Segue a sequência oficial de conexão (docs/34 §4.3): os estados <c>EnviarCfgOffline</c>,
    /// <c>EnviarConfigMudOnlineOffline</c> e <c>EnviarCfgOnline</c> mandam, cada um, a sua etapa
    /// (<see cref="ITopdataInnerAdapter.EnviarEtapaDaSequenciaOficial"/>). Desligado por padrão: os
    /// três mandam a mesma configuração completa, como sempre (defeito F3, docs/34 §2). Chave
    /// técnica <c>catraca.sequencia_oficial</c>, <c>A_CONFIRMAR_COM_TOPDATA</c> até o ensaio
    /// INT-SM-021 (Etapa A.7 do docs/35). Não liga a contingência: a mudança automática segue com
    /// o valor da configuração, 0 no padrão (D8).
    /// </param>
    /// <param name="gravadorDeBilhetes">
    /// Quem torna durável cada bilhete coletado (Etapa A.9 do docs/35). A gravação é um passo
    /// próprio, sem chamada nativa, entre um <c>ColetarBilhete</c> e o próximo (R-68). Sem ele,
    /// este laço <b>não coleta</b>: o comando falha e o estado de coleta sai sem chamar a catraca —
    /// coletar sem gravar apagaria marcações da memória dela.
    /// </param>
    /// <param name="aoDesistirDoGiro">
    /// Chamado com o id do equipamento quando o laço desiste de esperar o giro pelo prazo de
    /// <see cref="DeviceState.MonitoraGiroCatraca"/>, sem origem 5 nem 6 (Etapa I.1b do docs/36,
    /// C1). O decisor encerra a tentativa pendente sem giro, como faria com a origem 5: um giro
    /// que chegue depois não é da pessoa liberada, e confirmá-la com ele daria a passagem a outro.
    /// </param>
    public DevicePump(
        ITopdataInnerAdapter adapter,
        Func<DateTimeOffset>? relogio = null,
        IReadOnlySet<byte>? linhasHomologadas = null,
        Func<DeviceEvent, Decision>? decidir = null,
        Action<DeviceEvent>? aoReceberEvento = null,
        bool acertarRelogioAoDivergir = false,
        Action<ComandoDeCatraca, SituacaoDoComando, string>? aoConcluirComando = null,
        Action<string>? antesDaLiberacaoManual = null,
        bool sequenciaOficial = false,
        IGravadorDeBilhetes? gravadorDeBilhetes = null,
        Action<string>? aoDesistirDoGiro = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _aoDesistirDoGiro = aoDesistirDoGiro;
        _sequenciaOficial = sequenciaOficial;
        _gravadorDeBilhetes = gravadorDeBilhetes;
        _acertarRelogioAoDivergir = acertarRelogioAoDivergir;
        _aoConcluirComando = aoConcluirComando;
        _antesDaLiberacaoManual = antesDaLiberacaoManual;
        _adapter = adapter;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);

        // O motor de decisão é de fora: o laço não decide acesso, só transporta.
        _decidir = decidir;

        // Quem persiste recebe cada evento no instante em que chega, e não por uma fila
        // acumulada no slot.
        _aoReceberEvento = aoReceberEvento;

        // Nada é configurado sem que o firmware conste da matriz (ADR-0010).
        _linhasHomologadas = linhasHomologadas ?? new HashSet<byte> { 14, 16 };
    }

    /// <summary>Executa um passo. Devolve o que foi feito, para log e métrica.</summary>
    public string Passo(DeviceSlot dispositivo, TimeSpan limiteDeEspera)
    {
        ArgumentNullException.ThrowIfNull(dispositivo);

        ExpirarNaFila(dispositivo, _relogio());
        var feito = PassoDaMaquina(dispositivo, limiteDeEspera);
        return AcompanharComando(dispositivo, _relogio()) is { } desfecho ? $"{feito} · {desfecho}" : feito;
    }

    // Catraca fora do ar ou presa num estado: o comando não espera para sempre na fila.
    private void ExpirarNaFila(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.Comandos.Count == 0 || d.Comandos.All(c => agora < c.ExpiraEm))
        {
            return;
        }

        var validos = new List<ComandoDeCatraca>(d.Comandos.Count);
        while (d.Comandos.TryDequeue(out var comando))
        {
            if (agora < comando.ExpiraEm)
            {
                validos.Add(comando);
            }
            else
            {
                Concluir(comando, SituacaoDoComando.Expirado, $"a catraca não ficou livre a tempo (estado {d.Maquina.Current}); não executado");
            }
        }

        foreach (var comando in validos)
        {
            d.Comandos.Enqueue(comando);
        }
    }

    private string PassoDaMaquina(DeviceSlot dispositivo, TimeSpan limiteDeEspera)
    {
        var agora = _relogio();

        // Bilhete coletado e ainda não gravado: este passo é a gravação, e só ela (R-68). Vem
        // antes do backoff e do disjuntor porque não fala com a catraca — e antes de tudo o
        // mais porque a catraca já apagou o bilhete: aqui está a única cópia.
        if (dispositivo.BilheteAGravar is { } pendente)
        {
            return GravarBilhete(dispositivo, pendente);
        }

        if (dispositivo.EsperarAte is { } ate && agora < ate)
        {
            return PrazoComOPassoImpedido(dispositivo, agora, "aguardando backoff") ?? "aguardando backoff";
        }

        if (!dispositivo.Disjuntor.PermitePassar)
        {
            return PrazoComOPassoImpedido(dispositivo, agora, "disjuntor aberto") ?? "disjuntor aberto";
        }

        // Comandos do operador e relógio só em Polling: é o ponto ocioso e seguro, sem
        // ninguém no meio de uma passagem. Uma chamada por passo, como o resto.
        if (dispositivo.Maquina.Current is DeviceState.Polling)
        {
            if (dispositivo.EmCurso is null && dispositivo.Comandos.TryDequeue(out var comando))
            {
                return Executar(dispositivo, comando, agora);
            }

            if (CuidarDoRelogio(dispositivo, agora) is { } relogio)
            {
                return relogio;
            }
        }

        // A coleta é de vários passos, um bilhete por vez: o prazo do estado (10 min) corta
        // antes de pedir o próximo, nunca entre a coleta e a gravação (R-68, tratada acima).
        if (dispositivo.Maquina.Current is DeviceState.ColetarBilhetes
            && PrazoEstourado(dispositivo, agora) is { } prazoDaColeta)
        {
            return EsgotarPrazo(dispositivo, agora, prazoDaColeta);
        }

        return dispositivo.Maquina.Current switch
        {
            DeviceState.Discovering or DeviceState.Reconectar or DeviceState.Degradado => Conectar(dispositivo, agora),
            DeviceState.Conectar => Conectar(dispositivo, agora),
            DeviceState.LendoIdentidade => LerIdentidade(dispositivo, agora),
            DeviceState.VerificandoCompatibilidade => VerificarCompatibilidade(dispositivo, agora),
            DeviceState.EnviarCfgOffline =>
                EnviarConfiguracao(dispositivo, agora, "cfg offline", EtapaDaSequenciaOficial.ConfiguracaoOffLine),
            DeviceState.EnviarConfigMudOnlineOffline =>
                EnviarConfiguracao(dispositivo, agora, "cfg mudança automática", EtapaDaSequenciaOficial.MudancaAutomatica),
            DeviceState.EnviarCfgOnline =>
                EnviarConfiguracao(dispositivo, agora, "cfg online", EtapaDaSequenciaOficial.ConfiguracaoOnLine),
            DeviceState.SincronizandoDadosOffline => SincronizarDadosOffline(dispositivo, agora),
            DeviceState.ConfigurarEntradasOnline => ConfigurarEntradas(dispositivo, agora),
            DeviceState.EnviarMsgPadrao => EnviarMensagemPadrao(dispositivo, agora),
            DeviceState.Polling or DeviceState.MonitoraGiroCatraca => Aguardar(dispositivo, agora, limiteDeEspera),
            DeviceState.ValidarAcesso => Decidir(dispositivo, agora),
            DeviceState.LiberarCatraca => Liberar(dispositivo, agora),
            DeviceState.EnviarMsgAcessoNegado => ExibirNegado(dispositivo, agora),
            DeviceState.ColetarBilhetes => ColetarBilhete(dispositivo, agora),
            _ => $"estado sem ação de laço: {dispositivo.Maquina.Current}",
        };
    }

    private string Conectar(DeviceSlot d, DateTimeOffset agora)
    {
        // De Discovering, Reconectar ou Degradado, o caminho para tentar de novo passa por
        // Conectar. Antes, só Discovering disparava este gatilho: a catraca que caía ficava
        // em Reconectar para sempre, mesmo com a conexão de volta (teste
        // Catraca_que_cai_volta_a_operar_quando_a_conexao_volta).
        if (d.Maquina.Current is DeviceState.Discovering or DeviceState.Reconectar or DeviceState.Degradado)
        {
            Disparar(d, DeviceTrigger.EquipamentoApareceu, agora);
        }

        var resultado = _adapter.TestarConexao(d.Inner);

        if (resultado.IsOk)
        {
            d.Disjuntor.RegistrarSucesso();
            d.TentativasDeReconexao = 0;
            d.EsperarAte = null;

            // A cada conexão, o relógio é acertado assim que a catraca chegar a Polling —
            // o fluxo oficial acerta na passagem para on-line (manual 4.6.1).
            d.AcertarRelogioEm = agora;
            d.ConferirRelogioEm = null;
            Disparar(d, DeviceTrigger.ConexaoOk, agora);
            return "conectado";
        }

        Falhar(d, agora, DeviceTrigger.ConexaoFalhou);
        return $"falha ao conectar ({resultado})";
    }

    private string LerIdentidade(DeviceSlot d, DateTimeOffset agora)
    {
        var (resultado, firmware) = _adapter.LerFirmware(d.Inner);

        if (!resultado.IsOk || firmware is null)
        {
            Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
            return $"falha ao ler firmware ({resultado})";
        }

        d.Firmware = firmware;
        Disparar(d, DeviceTrigger.IdentidadeLida, agora);
        return $"firmware lido: {firmware}";
    }

    private string VerificarCompatibilidade(DeviceSlot d, DateTimeOffset agora)
    {
        // Sem chamada nativa: é conferência contra a matriz.
        if (d.Firmware is { } firmware && _linhasHomologadas.Contains(firmware.Linha))
        {
            Disparar(d, DeviceTrigger.CompatibilidadeOk, agora);
            return "compatível";
        }

        Disparar(d, DeviceTrigger.CompatibilidadeRecusada, agora);
        return $"firmware não homologado (linha {d.Firmware?.Linha.ToString(provider: null) ?? "?"}) — não será configurado";
    }

    private string EnviarConfiguracao(DeviceSlot d, DateTimeOffset agora, string etapa, EtapaDaSequenciaOficial daSequenciaOficial)
    {
        // Sempre a configuração COMPLETA: o que não for setado volta ao padrão da DLL.
        // Ver ADR-0020. Com a sequência oficial, cada estado manda a sua etapa — e as duas
        // configurações levam os mesmos campos comuns (docs/34 §4.3). Uma chamada ao adapter
        // por passo, nos dois casos: a máquina de estados não muda.
        var resultado = _sequenciaOficial
            ? _adapter.EnviarEtapaDaSequenciaOficial(d.Inner, d.Configuracao, daSequenciaOficial)
            : _adapter.EnviarConfiguracaoCompleta(d.Inner, d.Configuracao);

        if (resultado.IsOk)
        {
            // Só aqui a configuração passa a ser "aplicada" (Etapa A.5): EnviarConfiguracoes
            // devolveu 0. Falha em qualquer passo da montagem ou no envio não toca a versão.
            // Calcular é CPU pura, sem chamada nativa: o passo continua com uma só.
            d.ConfiguracaoVersao = VersaoDaConfiguracao.Calcular(d.Configuracao);
            d.ConfiguracaoAplicadaEm = agora;
            Disparar(d, DeviceTrigger.ConfiguracaoEnviada, agora);
            return etapa + " enviada";
        }

        Falhar(d, agora, DeviceTrigger.ConfiguracaoFalhou);
        return $"falha em {etapa} ({resultado})";
    }

    private static string SincronizarDadosOffline(DeviceSlot d, DateTimeOffset agora)
    {
        // Fase 1: ainda não há lista a enviar. A transição existe para que a ordem
        // — dados de contingência ANTES de operar — já esteja correta.
        Disparar(d, DeviceTrigger.DadosOfflineSincronizados, agora);
        return "dados offline sincronizados (vazio nesta fase)";
    }

    private string ConfigurarEntradas(DeviceSlot d, DateTimeOffset agora)
    {
        var resultado = _adapter.ConfigurarEntradasOnline(d.Inner, d.Configuracao);

        if (resultado.IsOk)
        {
            Disparar(d, DeviceTrigger.ConfiguracaoEnviada, agora);
            return "leitor reabilitado";
        }

        Falhar(d, agora, DeviceTrigger.ConfiguracaoFalhou);
        return $"falha ao reabilitar o leitor ({resultado})";
    }

    private string EnviarMensagemPadrao(DeviceSlot d, DateTimeOffset agora)
    {
        var resultado = _adapter.EnviarMensagemPadrao(d.Inner, d.Configuracao.MensagemPadrao);

        if (resultado.IsOk)
        {
            Disparar(d, DeviceTrigger.ConfiguracaoEnviada, agora);
            return "mensagem padrão enviada";
        }

        Falhar(d, agora, DeviceTrigger.ConfiguracaoFalhou);
        return $"falha na mensagem padrão ({resultado})";
    }

    private string Aguardar(DeviceSlot d, DateTimeOffset agora, TimeSpan limite)
    {
        var (resultado, evento) = _adapter.AguardarEvento(d.Inner, limite);

        switch (resultado.Status)
        {
            case AdapterStatus.Ok when evento is not null:
                d.Disjuntor.RegistrarSucesso();
                d.UltimoEvento = evento;

                if (evento.Origin.ConfirmaPassagemFisica && d.EmCurso is { Liberou: true } manual)
                {
                    manual.Girou = true;
                }

                // Todo evento vai inteiro para quem registra, leitura ou não (ADR-0018).
                _aoReceberEvento?.Invoke(evento);

                // Só leitura vai para a decisão. Sinal da catraca (cartão recolhido, sensor,
                // urna cheia, tecla, origem desconhecida) sem código virava negação, com
                // "Acesso nao autorizado" no display e o leitor rearmado (F4, docs/34 §2).
                var gatilho = evento.Origin switch
                {
                    { ConfirmaPassagemFisica: true } => DeviceTrigger.GiroConfirmado,
                    { Known: KnownEventOrigin.FimTempoAcionamento } => DeviceTrigger.TempoDeAcionamentoEsgotado,
                    { EhLeitura: true } => DeviceTrigger.EventoRecebido,
                    _ => DeviceTrigger.SinalDaCatraca,
                };

                Disparar(d, gatilho, agora);
                return gatilho is DeviceTrigger.SinalDaCatraca
                    ? $"sinal da catraca {evento.Origin} — registrado, sem decisão"
                    : $"evento {evento.Origin}";

            case AdapterStatus.SemEventos:
            {
                d.Disjuntor.RegistrarSucesso();
                string? erroDeRecepcao = null;
                if (resultado.NativeReturn != 0)
                {
                    // Retorno ≠ 0 que o adaptador, com a reconexão desligada até HIL-EVT-01,
                    // devolveu como "sem eventos": conta sempre e deixa o bruto no registro (F6).
                    // Se a DLL devolver ≠ 0 em toda volta sem evento, uma linha por volta
                    // afogaria o registro: registra a 1ª, a 10ª, a 100ª... e o total fica no contador.
                    d.ErrosDeRecepcao++;
                    if (EhMarco(d.ErrosDeRecepcao))
                    {
                        erroDeRecepcao = $"sem eventos com retorno {resultado} — erro de recepção nº {d.ErrosDeRecepcao}, " +
                            "sem reconectar até HIL-EVT-01";
                    }
                }

                // Prazo do giro (C1, docs/36): só depois de uma espera que voltou vazia. Um giro
                // ou uma origem 5 que já esteja na fila da DLL sai antes, por mais que a volta
                // tenha demorado; o prazo nunca corta o que a catraca já mandou.
                if (d.Maquina.Current is DeviceState.MonitoraGiroCatraca && PrazoEstourado(d, agora) is { } prazo)
                {
                    var desistiu = EsgotarPrazo(d, agora, prazo);
                    return erroDeRecepcao is null ? desistiu : $"{desistiu} · {erroDeRecepcao}";
                }

                Disparar(d, DeviceTrigger.SemEventos, agora);
                return erroDeRecepcao ?? "sem eventos";
            }

            case AdapterStatus.FalhaDeDependencia:
                // Retorno 8 não é problema de rede: é DLL, .NET Framework ou
                // arquitetura. Insistir não adianta.
                Disparar(d, DeviceTrigger.DependenciaFatal, agora);
                return $"falha de dependência ({resultado}) — worker inutilizável";

            default:
                // Retorno ≠ 0 de ReceberDadosOnLine não é silêncio (F6, docs/34 §2): conta,
                // registra com o bruto e segue pelo caminho de falha de sempre — disjuntor,
                // backoff e reconexão —, sem derrubar o laço das outras catracas. Da DLL real só
                // chega aqui com ReconectarEmErroDeRecepcao ligado no adaptador (HIL-EVT-01).
                d.ErrosDeRecepcao++;
                Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
                return $"erro ao aguardar evento ({resultado}) — erro de recepção nº {d.ErrosDeRecepcao}";
        }
    }

    /// <summary>1, 10, 100, 1000...: quando um erro repetido volta ao registro.</summary>
    private static bool EhMarco(long n)
    {
        while (n >= 10 && n % 10 == 0)
        {
            n /= 10;
        }

        return n == 1;
    }

    private string ColetarBilhete(DeviceSlot d, DateTimeOffset agora)
    {
        if (_gravadorDeBilhetes is null)
        {
            // Sem quem grave, nenhum bilhete sai da catraca: coletar e descartar perdia a
            // marcação (defeito F5, docs/34 §2). Sai do estado sem chamar a DLL.
            d.Coleta = null;
            Disparar(d, DeviceTrigger.SemBilhetes, agora);
            return "coleta não feita: este laço não tem onde gravar bilhetes (R-68)";
        }

        var (resultado, bilhete) = _adapter.ColetarBilhete(d.Inner);

        switch (resultado.Status)
        {
            case AdapterStatus.Ok when bilhete is not null:
            {
                // O bilhete já saiu da memória do equipamento (FUN:40). O próximo passo desta
                // catraca é gravá-lo; só depois vem o próximo ColetarBilhete (R-68).
                var coleta = d.Coleta ??= new ColetaDeBilhetes(Guid.CreateVersion7(agora));
                coleta.Coletados++;
                d.BilheteAGravar = new BilheteColetado(d.Inner, bilhete, coleta.Id, coleta.Coletados, agora);
                d.Disjuntor.RegistrarSucesso();
                Disparar(d, DeviceTrigger.BilheteColetado, agora);
                return string.Create(CultureInfo.InvariantCulture, $"bilhete tipo {bilhete.Tipo} coletado (nº {coleta.Coletados}); gravando antes do próximo");
            }

            case AdapterStatus.SemBilhetes:
                if (d.EmCurso is not { Comando.Tipo: TipoDeComando.ColetarBilhetes })
                {
                    d.Coleta = null;
                }

                Disparar(d, DeviceTrigger.SemBilhetes, agora);
                return "sem bilhetes";

            default:
                Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
                return $"erro ao coletar bilhete ({resultado})";
        }
    }

    // Passo sem chamada nativa: a base, e só ela. Falhou, o bilhete fica com o worker e o
    // próximo passo desta catraca tenta de novo; a catraca não é chamada enquanto isso.
    private string GravarBilhete(DeviceSlot d, BilheteColetado pendente)
    {
        if (_gravadorDeBilhetes is null)
        {
            // Inalcançável: só se coleta com gravador. Se acontecer, não some com o bilhete.
            return "bilhete coletado sem onde gravar; a catraca não será chamada";
        }

        DesfechoDaGravacaoDoBilhete desfecho;
        try
        {
            desfecho = _gravadorDeBilhetes.Gravar(pendente);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            d.FalhasAoGravarBilhete++;
            return EhMarco(d.FalhasAoGravarBilhete)
                ? $"bilhete coletado ainda não gravado ({erro.GetType().Name}, falha nº {d.FalhasAoGravarBilhete}); a coleta espera a base"
                : "bilhete coletado ainda não gravado; a coleta espera a base";
        }

        d.BilheteAGravar = null;
        var coleta = d.Coleta;

        if (desfecho is DesfechoDaGravacaoDoBilhete.Gravado)
        {
            if (coleta is not null)
            {
                coleta.Gravados++;
            }

            return string.Create(CultureInfo.InvariantCulture, $"bilhete nº {pendente.Ordem} gravado (tipo {pendente.Bilhete.Tipo})");
        }

        if (coleta is not null)
        {
            coleta.Repetidos++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"bilhete nº {pendente.Ordem} já estava na base (tipo {pendente.Bilhete.Tipo}); não gravado de novo");
    }

    private string Decidir(DeviceSlot d, DateTimeOffset agora)
    {
        // Sem motor de decisão configurado, o laço PARA aqui em vez de escolher sozinho.
        // Liberar por omissão seria uma política de segurança tomada por engano.
        if (_decidir is null)
        {
            return "aguardando motor de decisão (nenhum configurado)";
        }

        var evento = d.UltimoEvento;
        if (evento is null)
        {
            Disparar(d, DeviceTrigger.AcessoNegado, agora);
            return "sem evento para decidir";
        }

        var decisao = _decidir(evento);
        d.UltimaDecisao = decisao;

        Disparar(d, decisao.ShouldRelease ? DeviceTrigger.AcessoPermitido : DeviceTrigger.AcessoNegado, agora);
        return $"decisão {decisao.Outcome} ({decisao.Reason})";
    }

    private string Liberar(DeviceSlot d, DateTimeOffset agora)
    {
        // A função exata vem do perfil físico do portão, definido no comissionamento —
        // nunca de constante em código nem de combinação de sinalizadores (docs/04, seção
        // 3; defeito F1 do docs/34 §2). Ingresso e liberação manual passam por aqui, e
        // também as liberações de saída e nos dois sentidos pedidas pelo operador (Etapa A.8).
        var direcao = d.EmCurso?.Comando.Tipo switch
        {
            TipoDeComando.LiberarSaida => d.Configuracao.PerfilFisico.LiberacaoDaSaida,
            TipoDeComando.LiberarDoisSentidos => GateDirection.DoisSentidos,
            _ => d.Configuracao.PerfilFisico.LiberacaoDaEntrada,
        };

        var resultado = _adapter.LiberarGiro(d.Inner, direcao);

        if (resultado.IsOk)
        {
            if (d.EmCurso is { } manual && ComandoDeCatraca.EhLiberacao(manual.Comando.Tipo))
            {
                manual.Liberou = true;
            }

            Disparar(d, DeviceTrigger.ComandoDeLiberacaoOk, agora);
            return $"giro liberado ({direcao})";
        }

        Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
        return $"falha ao liberar giro ({resultado})";
    }

    private string ExibirNegado(DeviceSlot d, DateTimeOffset agora)
    {
        var resultado = _adapter.ExibirMensagemTemporaria(d.Inner, "Acesso nao autorizado", TimeSpan.FromSeconds(3));

        if (resultado.IsOk)
        {
            Disparar(d, DeviceTrigger.MensagemExibida, agora);
            return "mensagem de negação exibida";
        }

        Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
        return $"falha ao exibir negação ({resultado})";
    }

    private string Executar(DeviceSlot d, ComandoDeCatraca comando, DateTimeOffset agora)
    {
        // Nome e motivo digitados ficam só na auditoria (operator_command): texto livre pode
        // conter qualquer coisa, e o registro do worker não é lugar para isso.
        var rotulo = $"comando {comando.Tipo}";

        if (agora >= comando.ExpiraEm)
        {
            Concluir(comando, SituacaoDoComando.Expirado, "a catraca não ficou livre a tempo; não executado");
            return $"{rotulo}: expirado, não executado";
        }

        switch (comando.Tipo)
        {
            case TipoDeComando.AcertarRelogio:
            {
                var (ok, feito) = AcertarRelogio(d, agora);
                Concluir(comando, ok ? SituacaoDoComando.Concluido : SituacaoDoComando.Falhou, feito);
                return $"{rotulo}: {feito}";
            }

            case TipoDeComando.MensagemTemporaria:
            {
                var resultado = _adapter.ExibirMensagemTemporaria(
                    d.Inner, comando.Texto ?? string.Empty, TimeSpan.FromSeconds(comando.DuracaoSegundos));
                var feito = resultado.IsOk ? "mensagem exibida" : $"a catraca recusou a mensagem ({resultado})";
                Concluir(comando, resultado.IsOk ? SituacaoDoComando.Concluido : SituacaoDoComando.Falhou, feito);
                return $"{rotulo}: {feito}";
            }

            case TipoDeComando.LiberacaoManual:
            case TipoDeComando.LiberarSaida:
            case TipoDeComando.LiberarDoisSentidos:
                // O giro que vier é do operador: a tentativa pendente do último ingresso
                // termina aqui, sem giro, e não leva a passagem de outra pessoa. Saída e dois
                // sentidos seguem o mesmo caminho da liberação manual — liberar, monitorar o
                // giro, reabilitar o leitor —; só a função nativa muda, em Liberar.
                _antesDaLiberacaoManual?.Invoke(d.Maquina.DeviceId);
                d.EmCurso = new ComandoEmCurso(comando, agora);
                Disparar(d, DeviceTrigger.LiberacaoManualSolicitada, agora);
                return comando.Tipo switch
                {
                    TipoDeComando.LiberarSaida => $"{rotulo}: liberação de saída pedida",
                    TipoDeComando.LiberarDoisSentidos => $"{rotulo}: liberação nos dois sentidos pedida",
                    _ => $"{rotulo}: liberação manual pedida",
                };

            case TipoDeComando.BipCurto:
            case TipoDeComando.BipLongo:
            {
                // Uma chamada, como a mensagem temporária. Só por pedido do operador: o bip
                // nunca é acoplado à decisão automática (cada chamada reduz a vazão, docs/34 §8).
                var bip = comando.Tipo is TipoDeComando.BipCurto ? TipoDeBip.Curto : TipoDeBip.Longo;
                var resultado = _adapter.AcionarBip(d.Inner, bip);
                var feito = resultado.IsOk ? "bip acionado" : $"a catraca recusou o bip ({resultado})";
                Concluir(comando, resultado.IsOk ? SituacaoDoComando.Concluido : SituacaoDoComando.Falhou, feito);
                return $"{rotulo}: {feito}";
            }

            case TipoDeComando.ReiniciarConexao:
            case TipoDeComando.AplicarConfiguracao:
                if (comando.Tipo is TipoDeComando.AplicarConfiguracao)
                {
                    if (d.ConfiguracaoNova is not { } nova)
                    {
                        Concluir(comando, SituacaoDoComando.Falhou, "nenhuma configuração nova foi carregada");
                        return $"{rotulo}: sem configuração nova";
                    }

                    d.Configuracao = nova;
                    d.ConfiguracaoNova = null;
                }

                d.EmCurso = new ComandoEmCurso(comando, agora);
                Disparar(d, DeviceTrigger.ReconexaoSolicitada, agora);
                return $"{rotulo}: reconectando";

            case TipoDeComando.ColetarBilhetes:
                if (_gravadorDeBilhetes is null)
                {
                    Concluir(comando, SituacaoDoComando.Falhou, "este worker não tem onde gravar bilhetes; nada foi coletado");
                    return $"{rotulo}: sem onde gravar, não executado";
                }

                // A coleta tira a catraca de Polling até a memória esvaziar: durante ela, a
                // catraca não atende leitura neste laço. Por isso é só por pedido (docs/34 §8).
                d.EmCurso = new ComandoEmCurso(comando, agora);
                d.Coleta = new ColetaDeBilhetes(comando.Id);
                Disparar(d, DeviceTrigger.IniciarColetaDeBilhetes, agora);
                return $"{rotulo}: coleta iniciada";

            default:
                Concluir(comando, SituacaoDoComando.Falhou, "comando sem execução neste worker");
                return $"{rotulo}: desconhecido";
        }
    }

    /// <summary>Fecha o comando de vários passos quando a catraca termina o que ele pediu.</summary>
    private string? AcompanharComando(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.EmCurso is not { } emCurso)
        {
            return null;
        }

        var estado = d.Maquina.Current;

        if (emCurso.Comando.Tipo is TipoDeComando.ColetarBilhetes)
        {
            // Termina quando a catraca sai da coleta e o último bilhete já está na base.
            if (estado is DeviceState.ColetarBilhetes || d.BilheteAGravar is not null)
            {
                return null;
            }

            d.EmCurso = null;
            var resumo = d.Coleta?.Resumo() ?? "nenhum bilhete coletado";
            d.Coleta = null;

            if (estado is DeviceState.Polling)
            {
                Concluir(emCurso.Comando, SituacaoDoComando.Concluido, $"memória da catraca vazia; {resumo}");
                return resumo;
            }

            // Erro de comunicação ou tempo esgotado no meio: o que já foi coletado está gravado;
            // o que ficou na catraca sai no próximo pedido.
            var interrompida = $"coleta interrompida (estado {estado}); {resumo}; o restante fica na catraca para o próximo pedido";
            Concluir(emCurso.Comando, SituacaoDoComando.Falhou, interrompida);
            return interrompida;
        }

        if (ComandoDeCatraca.EhLiberacao(emCurso.Comando.Tipo))
        {
            var liberada = emCurso.Comando.Tipo switch
            {
                TipoDeComando.LiberarSaida => "liberada na saída",
                TipoDeComando.LiberarDoisSentidos => "liberada nos dois sentidos",
                _ => "liberada",
            };

            if (estado is DeviceState.LiberarCatraca or DeviceState.MonitoraGiroCatraca)
            {
                // A saída de MonitoraGiro é a origem 5 (fim do tempo de acionamento) ou, se ela
                // não vier, o prazo do estado (C1, docs/36: tempo do relé 1 + 3 s, no máximo
                // 53 s). Este limite é a última defesa, para o comando não ficar aberto para sempre.
                if (agora - emCurso.IniciadoEm <= LimiteDaLiberacaoManual)
                {
                    return null;
                }

                d.EmCurso = null;
                var semSinal = emCurso.Girou
                    ? $"{liberada}; girou"
                    : $"{liberada}; a catraca não informou giro nem fim do tempo em {LimiteDaLiberacaoManual.TotalSeconds:0} s";
                Concluir(emCurso.Comando, SituacaoDoComando.Concluido, semSinal);
                return semSinal;
            }

            d.EmCurso = null;
            var (situacao, resultado) = emCurso switch
            {
                { Liberou: false } => (SituacaoDoComando.Falhou, "a catraca não recebeu a liberação"),
                { Girou: true } => (SituacaoDoComando.Concluido, $"{liberada}; girou"),
                { GiroNaoConfirmadoNoPrazo: { } prazo } =>
                    (SituacaoDoComando.Concluido, $"{liberada}; {GiroNaoConfirmado(prazo)}"),
                _ => (SituacaoDoComando.Concluido, $"{liberada}; ninguém girou"),
            };
            Concluir(emCurso.Comando, situacao, resultado);
            return resultado;
        }

        // Reconexão pedida: conclui quando a catraca volta a atender.
        if (estado is not DeviceState.Polling)
        {
            emCurso.SaiuDeOperacao = true;
        }
        else if (emCurso.SaiuDeOperacao)
        {
            d.EmCurso = null;
            var resultado = emCurso.Comando.Tipo is TipoDeComando.AplicarConfiguracao
                ? "configuração enviada; catraca atendendo"
                : "reconectada; catraca atendendo";
            Concluir(emCurso.Comando, SituacaoDoComando.Concluido, resultado);
            return resultado;
        }

        if (agora - emCurso.IniciadoEm > LimiteDaReconexaoPedida)
        {
            d.EmCurso = null;
            var resultado = $"a catraca não voltou a atender em {LimiteDaReconexaoPedida.TotalMinutes:0} min (estado {estado})";
            Concluir(emCurso.Comando, SituacaoDoComando.Falhou, resultado);
            return resultado;
        }

        return null;
    }

    private void Concluir(ComandoDeCatraca comando, SituacaoDoComando situacao, string resultado) =>
        _aoConcluirComando?.Invoke(comando, situacao, resultado);

    private string? CuidarDoRelogio(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.AcertarRelogioEm is { } acertar && agora >= acertar)
        {
            return AcertarRelogio(d, agora).Feito;
        }

        if (d.ConferirRelogioEm is { } conferir && agora >= conferir)
        {
            return ConferirRelogio(d, agora);
        }

        return null;
    }

    // Falha no relógio nunca derruba a catraca: não conta no disjuntor nem dispara
    // reconexão. Se a comunicação caiu de fato, a próxima espera por evento percebe.
    private (bool Ok, string Feito) AcertarRelogio(DeviceSlot d, DateTimeOffset agora)
    {
        AdapterResult resultado;
        try
        {
            resultado = _adapter.AcertarRelogio(d.Inner, agora);
        }
        catch (ArgumentOutOfRangeException)
        {
            // O relógio do PC está fora de 2000–2099: acertar a catraca por ele seria pior.
            d.AcertarRelogioEm = null;
            d.ConferirRelogioEm = agora + PrimeiraConferenciaDoRelogio;
            return (false, "relógio do PC fora do intervalo que a catraca guarda — relógio da catraca não acertado");
        }

        if (!resultado.IsOk)
        {
            d.AcertarRelogioEm = agora + EsperaAposFalhaNoRelogio;
            return (false, $"falha ao acertar o relógio ({resultado}) — a operação segue");
        }

        d.RelogioAcertadoEm = agora;
        d.AcertarRelogioEm = null;
        d.ConferirRelogioEm = agora + PrimeiraConferenciaDoRelogio;
        return (true, "relógio acertado");
    }

    private string ConferirRelogio(DeviceSlot d, DateTimeOffset agora)
    {
        var (resultado, lido) = _adapter.LerRelogio(d.Inner);

        if (!resultado.IsOk || lido is not { } relogio)
        {
            d.ConferirRelogioEm = agora + EsperaAposFalhaNoRelogio;
            return $"falha ao ler o relógio ({resultado}) — a operação segue";
        }

        d.RelogioConferidoEm = agora;
        d.ConferirRelogioEm = agora + IntervaloDeConferenciaDoRelogio;
        d.RelogioInvalido = relogio == DateTimeOffset.MinValue;
        d.DivergenciaDoRelogio = d.RelogioInvalido
            ? null
            : TimeSpan.FromSeconds(Math.Round((relogio - agora).TotalSeconds));

        if (!d.RelogioDivergente)
        {
            return string.Create(CultureInfo.InvariantCulture, $"relógio conferido ({d.DivergenciaDoRelogio!.Value.TotalSeconds:+0;-0;0} s)");
        }

        var descricao = d.RelogioInvalido
            ? "data inválida"
            : string.Create(CultureInfo.InvariantCulture, $"{d.DivergenciaDoRelogio!.Value.TotalSeconds:+0;-0} s");

        if (_acertarRelogioAoDivergir)
        {
            d.AcertarRelogioEm = agora;
            return $"relógio divergente ({descricao}) — será acertado";
        }

        return $"relógio divergente ({descricao}) — acerte pelo painel";
    }

    /// <summary>
    /// O prazo do estado atual, quando já estourou; nulo quando não estourou, quando o estado não
    /// tem prazo ou quando o laço não o aplica.
    /// </summary>
    /// <remarks>
    /// Medido desde a entrada no estado (<see cref="DeviceStateMachine.EstadoAtualDesde"/>), pelo
    /// relógio injetado. O prazo de <see cref="DeviceState.ValidarAcesso"/> (150 ms) nunca é
    /// aplicado aqui: é o orçamento da decisão (medido em <see cref="Decision"/>), não um prazo
    /// de passo, e o destino da tabela para ele é <see cref="DeviceState.LiberarCatraca"/> —
    /// aplicá-lo liberaria o giro sem decisão sempre que a vez da catraca no laço demorasse mais
    /// de 150 ms, ou com o motor de decisão ausente. Falha de base nega, nunca libera (ADR-0013).
    /// </remarks>
    private static TimeSpan? PrazoEstourado(DeviceSlot d, DateTimeOffset agora)
    {
        var estado = d.Maquina.Current;
        if (estado is DeviceState.ValidarAcesso
            || d.Maquina.EstadoAtualDesde is not { } desde
            || DeviceStateMachine.PrazoEfetivo(estado, d.Configuracao) is not { } prazo)
        {
            return null;
        }

        return agora - desde >= prazo ? prazo : null;
    }

    /// <summary>
    /// O prazo dos estados de uma chamada só (conexão, identidade, configuração, rearme,
    /// mensagem, liberação), quando o passo não pôde fazer a chamada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nesses estados o próprio passo resolve o estado: a chamada dá certo e a máquina segue, ou
    /// falha e vai para a reconexão. O prazo só tem o que fazer quando o passo é impedido de
    /// chamar — espera de backoff ou disjuntor aberto — e o estado ficaria parado. Aí o caminho
    /// é o da tabela: reconectar, degradar ou quarentenar.
    /// </para>
    /// <para>
    /// A espera pela vez no laço <b>não</b> conta contra eles. Com até 20 catracas numa thread
    /// e chamadas que bloqueiam segundos (conexão a uma catraca morta, envio da configuração),
    /// uma volta passa de 5 s numa reconexão em massa: aplicar o prazo no começo do passo
    /// mandaria a catraca sadia, que acabou de conectar, para a quarentena (prazo de
    /// <see cref="DeviceState.LendoIdentidade"/>) ou prenderia as catracas num ciclo de
    /// configurar e reconectar sem nunca chegar a enviar.
    /// </para>
    /// </remarks>
    private string? PrazoComOPassoImpedido(DeviceSlot d, DateTimeOffset agora, string motivo) =>
        PrazoEstourado(d, agora) is { } prazo ? $"{EsgotarPrazo(d, agora, prazo)} ({motivo})" : null;

    /// <summary>Dispara o tempo esgotado do estado atual, com o efeito próprio de cada um.</summary>
    private string EsgotarPrazo(DeviceSlot d, DateTimeOffset agora, TimeSpan prazo)
    {
        var estado = d.Maquina.Current;
        Disparar(d, DeviceTrigger.TempoEsgotado, agora);

        if (estado is not DeviceState.MonitoraGiroCatraca)
        {
            return $"prazo de {Segundos(prazo)} s em {estado} esgotado — {d.Maquina.Current}";
        }

        // Liberou e não veio origem 5 nem 6: o mesmo desfecho da origem 5, sem inventar giro. A
        // tentativa pendente termina sem giro e o leitor é rearmado no próximo passo
        // (MonitoraGiroCatraca → ConfigurarEntradasOnline → EnviarMsgPadrao → Polling).
        _aoDesistirDoGiro?.Invoke(d.Maquina.DeviceId);
        if (d.EmCurso is { Liberou: true, Girou: false } manual)
        {
            manual.GiroNaoConfirmadoNoPrazo = prazo;
        }

        return $"{GiroNaoConfirmado(prazo)}, sem origem 5 nem 6 — rearmando o leitor";
    }

    private static string GiroNaoConfirmado(TimeSpan prazo) => $"giro não confirmado: prazo de {Segundos(prazo)} s";

    private static string Segundos(TimeSpan prazo) =>
        prazo.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Disparar(DeviceSlot d, DeviceTrigger gatilho, DateTimeOffset agora) =>
        d.Maquina.TryFire(gatilho, agora, $"pump-{d.Inner}-{agora.UtcTicks}", out _);

    private static void Falhar(DeviceSlot d, DateTimeOffset agora, DeviceTrigger gatilho)
    {
        d.Disjuntor.RegistrarFalha();
        d.TentativasDeReconexao++;
        d.EsperarAte = agora + new BackoffComJitter().Para(d.TentativasDeReconexao);
        Disparar(d, gatilho, agora);
    }
}
