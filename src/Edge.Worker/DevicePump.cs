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

/// <summary>Um comando que leva mais de um passo: liberação manual, reconexão.</summary>
internal sealed class ComandoEmCurso(ComandoDeCatraca comando, DateTimeOffset iniciadoEm)
{
    public ComandoDeCatraca Comando { get; } = comando;

    public DateTimeOffset IniciadoEm { get; } = iniciadoEm;

    /// <summary>A catraca aceitou a liberação.</summary>
    public bool Liberou { get; set; }

    /// <summary>Veio o giro (origem 6) depois da liberação.</summary>
    public bool Girou { get; set; }

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
    public DevicePump(
        ITopdataInnerAdapter adapter,
        Func<DateTimeOffset>? relogio = null,
        IReadOnlySet<byte>? linhasHomologadas = null,
        Func<DeviceEvent, Decision>? decidir = null,
        Action<DeviceEvent>? aoReceberEvento = null,
        bool acertarRelogioAoDivergir = false,
        Action<ComandoDeCatraca, SituacaoDoComando, string>? aoConcluirComando = null,
        Action<string>? antesDaLiberacaoManual = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
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

        if (dispositivo.EsperarAte is { } ate && agora < ate)
        {
            return "aguardando backoff";
        }

        if (!dispositivo.Disjuntor.PermitePassar)
        {
            return "disjuntor aberto";
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

        return dispositivo.Maquina.Current switch
        {
            DeviceState.Discovering or DeviceState.Reconectar or DeviceState.Degradado => Conectar(dispositivo, agora),
            DeviceState.Conectar => Conectar(dispositivo, agora),
            DeviceState.LendoIdentidade => LerIdentidade(dispositivo, agora),
            DeviceState.VerificandoCompatibilidade => VerificarCompatibilidade(dispositivo, agora),
            DeviceState.EnviarCfgOffline => EnviarConfiguracao(dispositivo, agora, "cfg offline"),
            DeviceState.EnviarConfigMudOnlineOffline => EnviarConfiguracao(dispositivo, agora, "cfg mudança automática"),
            DeviceState.EnviarCfgOnline => EnviarConfiguracao(dispositivo, agora, "cfg online"),
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

    private string EnviarConfiguracao(DeviceSlot d, DateTimeOffset agora, string etapa)
    {
        // Sempre a configuração COMPLETA: o que não for setado volta ao padrão da DLL.
        // Ver ADR-0020.
        var resultado = _adapter.EnviarConfiguracaoCompleta(d.Inner, d.Configuracao);

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
                d.Disjuntor.RegistrarSucesso();
                Disparar(d, DeviceTrigger.SemEventos, agora);
                if (resultado.NativeReturn != 0)
                {
                    // Retorno ≠ 0 que o adaptador, com a reconexão desligada até HIL-EVT-01,
                    // devolveu como "sem eventos": conta sempre e deixa o bruto no registro (F6).
                    // Se a DLL devolver ≠ 0 em toda volta sem evento, uma linha por volta
                    // afogaria o registro: registra a 1ª, a 10ª, a 100ª... e o total fica no contador.
                    d.ErrosDeRecepcao++;
                    return EhMarco(d.ErrosDeRecepcao)
                        ? $"sem eventos com retorno {resultado} — erro de recepção nº {d.ErrosDeRecepcao}, " +
                            "sem reconectar até HIL-EVT-01"
                        : "sem eventos";
                }

                return "sem eventos";

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
        var (resultado, bilhete) = _adapter.ColetarBilhete(d.Inner);

        switch (resultado.Status)
        {
            case AdapterStatus.Ok when bilhete is not null:
                // Quem chama precisa commitar o bilhete ANTES do próximo passo: ele já
                // foi removido da memória do equipamento (risco R-68).
                Disparar(d, DeviceTrigger.BilheteColetado, agora);
                return $"bilhete tipo {bilhete.Tipo}";

            case AdapterStatus.SemBilhetes:
                Disparar(d, DeviceTrigger.SemBilhetes, agora);
                return "sem bilhetes";

            default:
                Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
                return $"erro ao coletar bilhete ({resultado})";
        }
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
        // 3; defeito F1 do docs/34 §2). Ingresso e liberação manual passam por aqui.
        var direcao = d.Configuracao.PerfilFisico.LiberacaoDaEntrada;

        var resultado = _adapter.LiberarGiro(d.Inner, direcao);

        if (resultado.IsOk)
        {
            if (d.EmCurso is { Comando.Tipo: TipoDeComando.LiberacaoManual } manual)
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
                // O giro que vier é do operador: a tentativa pendente do último ingresso
                // termina aqui, sem giro, e não leva a passagem de outra pessoa.
                _antesDaLiberacaoManual?.Invoke(d.Maquina.DeviceId);
                d.EmCurso = new ComandoEmCurso(comando, agora);
                Disparar(d, DeviceTrigger.LiberacaoManualSolicitada, agora);
                return $"{rotulo}: liberação manual pedida";

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

        if (emCurso.Comando.Tipo is TipoDeComando.LiberacaoManual)
        {
            if (estado is DeviceState.LiberarCatraca or DeviceState.MonitoraGiroCatraca)
            {
                // A_CONFIRMAR: a saída de MonitoraGiro depende da origem 5 (fim do tempo de
                // acionamento). Se a catraca não mandar, o comando não fica aberto para sempre.
                if (agora - emCurso.IniciadoEm <= LimiteDaLiberacaoManual)
                {
                    return null;
                }

                d.EmCurso = null;
                var semSinal = emCurso.Girou
                    ? "liberada; girou"
                    : $"liberada; a catraca não informou giro nem fim do tempo em {LimiteDaLiberacaoManual.TotalSeconds:0} s";
                Concluir(emCurso.Comando, SituacaoDoComando.Concluido, semSinal);
                return semSinal;
            }

            d.EmCurso = null;
            var (situacao, resultado) = emCurso switch
            {
                { Liberou: false } => (SituacaoDoComando.Falhou, "a catraca não recebeu a liberação"),
                { Girou: true } => (SituacaoDoComando.Concluido, "liberada; girou"),
                _ => (SituacaoDoComando.Concluido, "liberada; ninguém girou"),
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
