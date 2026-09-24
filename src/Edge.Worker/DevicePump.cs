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

    public DeviceConfiguration Configuracao { get; }

    public DeviceStateMachine Maquina { get; }

    public CircuitBreaker Disjuntor { get; }

    /// <summary>Tentativas seguidas de reconexão, para o cálculo do backoff.</summary>
    public int TentativasDeReconexao { get; internal set; }

    /// <summary>Quando este equipamento pode ser tentado de novo.</summary>
    public DateTimeOffset? EsperarAte { get; internal set; }

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
    private readonly ITopdataInnerAdapter _adapter;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly IReadOnlySet<byte> _linhasHomologadas;
    private readonly Func<DeviceEvent, Decision>? _decidir;
    private readonly Action<DeviceEvent>? _aoReceberEvento;

    public DevicePump(
        ITopdataInnerAdapter adapter,
        Func<DateTimeOffset>? relogio = null,
        IReadOnlySet<byte>? linhasHomologadas = null,
        Func<DeviceEvent, Decision>? decidir = null,
        Action<DeviceEvent>? aoReceberEvento = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
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

        var agora = _relogio();

        if (dispositivo.EsperarAte is { } ate && agora < ate)
        {
            return "aguardando backoff";
        }

        if (!dispositivo.Disjuntor.PermitePassar)
        {
            return "disjuntor aberto";
        }

        return dispositivo.Maquina.Current switch
        {
            DeviceState.Discovering or DeviceState.Reconectar => Conectar(dispositivo, agora),
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
        if (d.Maquina.Current is DeviceState.Discovering)
        {
            Disparar(d, DeviceTrigger.EquipamentoApareceu, agora);
        }

        var resultado = _adapter.TestarConexao(d.Inner);

        if (resultado.IsOk)
        {
            d.Disjuntor.RegistrarSucesso();
            d.TentativasDeReconexao = 0;
            d.EsperarAte = null;
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
        var resultado = _adapter.ConfigurarEntradasOnline(d.Inner);

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
                _aoReceberEvento?.Invoke(evento);
                Disparar(
                    d,
                    evento.Origin.ConfirmaPassagemFisica
                        ? DeviceTrigger.GiroConfirmado
                        : evento.Origin.Known is KnownEventOrigin.FimTempoAcionamento
                            ? DeviceTrigger.TempoDeAcionamentoEsgotado
                            : DeviceTrigger.EventoRecebido,
                    agora);
                return $"evento {evento.Origin}";

            case AdapterStatus.SemEventos:
                d.Disjuntor.RegistrarSucesso();
                Disparar(d, DeviceTrigger.SemEventos, agora);
                return "sem eventos";

            case AdapterStatus.FalhaDeDependencia:
                // Retorno 8 não é problema de rede: é DLL, .NET Framework ou
                // arquitetura. Insistir não adianta.
                Disparar(d, DeviceTrigger.DependenciaFatal, agora);
                return $"falha de dependência ({resultado}) — worker inutilizável";

            default:
                Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
                return $"erro ao aguardar evento ({resultado})";
        }
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
        // O sentido vem do perfil físico do portão, definido no comissionamento —
        // nunca de constante em código. Ver docs/04, seção 3.
        var direcao = d.Configuracao.PerfilFisico.SentidoInvertido
            ? GateDirection.Saida
            : GateDirection.Entrada;

        var resultado = _adapter.LiberarGiro(d.Inner, direcao);

        if (resultado.IsOk)
        {
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
