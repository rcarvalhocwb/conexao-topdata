using Access.Application.Devices;
using Access.Domain.Devices;

namespace Unit.Tests.Devices;

public sealed class DeviceStateMachineTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);
    private const string Correlacao = "teste-001";

    private static DeviceStateMachine Maquina(DeviceState inicial = DeviceState.Disabled) =>
        new("catraca-08", inicial);

    private static void Disparar(DeviceStateMachine m, params DeviceTrigger[] gatilhos)
    {
        foreach (var g in gatilhos)
        {
            Assert.True(
                m.TryFire(g, Agora, Correlacao, out _),
                $"gatilho {g} recusado no estado {m.Current}");
        }
    }

    [Fact]
    public void Caminho_completo_ate_a_operacao()
    {
        var m = Maquina();

        Disparar(
            m,
            DeviceTrigger.Habilitar,
            DeviceTrigger.EquipamentoApareceu,
            DeviceTrigger.ConexaoOk,
            DeviceTrigger.IdentidadeLida,
            DeviceTrigger.CompatibilidadeOk,
            DeviceTrigger.ConfiguracaoEnviada,
            DeviceTrigger.ConfiguracaoEnviada,
            DeviceTrigger.ConfiguracaoEnviada,
            DeviceTrigger.DadosOfflineSincronizados,
            DeviceTrigger.ConfiguracaoEnviada,
            DeviceTrigger.ConfiguracaoEnviada);

        Assert.Equal(DeviceState.Polling, m.Current);
        Assert.Equal(11, m.History.Count);
    }

    /// <summary>
    /// Nenhuma configuração pode ser enviada antes de o firmware ser conferido contra a
    /// matriz. Ver docs/ADR/ADR-0010-capability-discovery.md
    /// </summary>
    [Fact]
    public void Firmware_incompativel_nunca_chega_a_configurar()
    {
        var m = Maquina(DeviceState.VerificandoCompatibilidade);

        Disparar(m, DeviceTrigger.CompatibilidadeRecusada);

        Assert.Equal(DeviceState.FirmwareIncompativel, m.Current);
        Assert.False(m.CanFire(DeviceTrigger.ConfiguracaoEnviada));
        Assert.False(m.CanFire(DeviceTrigger.CompatibilidadeOk));
    }

    /// <summary>
    /// Depois do giro, o fluxo volta a configurar as entradas — é assim que o leitor é
    /// reabilitado. Pular isso trava o leitor (manual, seção 7.2.3).
    /// </summary>
    [Fact]
    public void Apos_o_giro_o_leitor_e_reabilitado()
    {
        var m = Maquina(DeviceState.MonitoraGiroCatraca);

        Disparar(m, DeviceTrigger.GiroConfirmado);

        Assert.Equal(DeviceState.ConfigurarEntradasOnline, m.Current);
    }

    [Fact]
    public void Apos_negar_o_acesso_o_leitor_tambem_e_reabilitado()
    {
        var m = Maquina(DeviceState.ValidarAcesso);

        Disparar(m, DeviceTrigger.AcessoNegado, DeviceTrigger.MensagemExibida);

        Assert.Equal(DeviceState.ConfigurarEntradasOnline, m.Current);
    }

    /// <summary>
    /// Origem 5 sem origem 6 significa que ninguém girou. O fluxo precisa seguir mesmo
    /// assim — a catraca não pode ficar esperando um giro que não vem.
    /// </summary>
    [Fact]
    public void Tempo_de_acionamento_esgotado_nao_trava_a_catraca()
    {
        var m = Maquina(DeviceState.MonitoraGiroCatraca);

        Disparar(m, DeviceTrigger.TempoDeAcionamentoEsgotado);

        Assert.Equal(DeviceState.ConfigurarEntradasOnline, m.Current);
    }

    [Fact]
    public void Gatilho_invalido_nao_muda_o_estado_e_nao_lanca()
    {
        var m = Maquina(DeviceState.Polling);

        var aceitou = m.TryFire(DeviceTrigger.GiroConfirmado, Agora, Correlacao, out var registro);

        Assert.False(aceitou);
        Assert.Null(registro);
        Assert.Equal(DeviceState.Polling, m.Current);
        Assert.Empty(m.History);
    }

    /// <summary>
    /// Uma transição ambígua — duas linhas com o mesmo (origem, gatilho) — seria um bug
    /// latente: o destino dependeria da ordem da tabela.
    /// </summary>
    [Fact]
    public void Tabela_nao_tem_transicao_ambigua()
    {
        var ambiguas = DeviceStateMachine.Transitions
            .GroupBy(t => (t.From, t.Trigger))
            .Where(g => g.Select(x => x.To).Distinct().Count() > 1)
            .Select(g => $"{g.Key.From} + {g.Key.Trigger} -> {string.Join("/", g.Select(x => x.To))}")
            .ToList();

        Assert.True(ambiguas.Count == 0, $"Transições ambíguas: {string.Join("; ", ambiguas)}");
    }

    [Fact]
    public void Todo_estado_operacional_e_alcancavel()
    {
        var alcancaveis = new HashSet<DeviceState> { DeviceState.Disabled };
        var fila = new Queue<DeviceState>([DeviceState.Disabled]);

        while (fila.Count > 0)
        {
            var atual = fila.Dequeue();
            foreach (var t in DeviceStateMachine.Transitions.Where(t => t.From == atual))
            {
                if (alcancaveis.Add(t.To))
                {
                    fila.Enqueue(t.To);
                }
            }
        }

        var inalcancaveis = Enum.GetValues<DeviceState>().Except(alcancaveis).ToList();

        Assert.True(
            inalcancaveis.Count == 0,
            $"Estados inalcançáveis a partir de Disabled: {string.Join(", ", inalcancaveis)}");
    }

    [Fact]
    public void Qualquer_estado_operacional_aceita_desabilitar_e_falha_fatal()
    {
        foreach (var estado in Enum.GetValues<DeviceState>())
        {
            if (estado is DeviceState.Disabled or DeviceState.FalhaFatalDeDependencia)
            {
                continue;
            }

            Assert.True(Maquina(estado).CanFire(DeviceTrigger.Desabilitar), $"{estado} não aceita Desabilitar");
            Assert.True(Maquina(estado).CanFire(DeviceTrigger.DependenciaFatal), $"{estado} não aceita DependenciaFatal");
        }
    }

    [Fact]
    public void Transicoes_ficam_registradas_para_auditoria()
    {
        var m = Maquina();

        m.TryFire(DeviceTrigger.Habilitar, Agora, "corr-42", out var registro);

        Assert.NotNull(registro);
        Assert.Equal(DeviceState.Disabled, registro.From);
        Assert.Equal(DeviceTrigger.Habilitar, registro.Trigger);
        Assert.Equal(DeviceState.Discovering, registro.To);
        Assert.Equal(Agora, registro.At);
        Assert.Equal("corr-42", registro.CorrelationId);
    }

    /// <summary>
    /// A meta de decisão é p95 ≤ 100 ms; o tempo-limite fica acima disso, com folga,
    /// mas ainda muito abaixo da paciência de quem está na fila.
    /// </summary>
    [Fact]
    public void Validar_acesso_tem_tempo_limite_curto()
    {
        var limite = DeviceStateMachine.TimeoutFor(DeviceState.ValidarAcesso);

        Assert.NotNull(limite);
        Assert.InRange(limite.Value, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Polling_nao_tem_tempo_limite_de_estado()
    {
        // O laço bloqueante tem watchdog próprio no worker; um timeout de estado aqui
        // derrubaria a operação normal.
        Assert.Null(DeviceStateMachine.TimeoutFor(DeviceState.Polling));
    }

    [Fact]
    public void Coleta_de_bilhetes_repete_no_mesmo_estado_ate_esvaziar()
    {
        var m = Maquina(DeviceState.ColetarBilhetes);

        Disparar(m, DeviceTrigger.BilheteColetado, DeviceTrigger.BilheteColetado, DeviceTrigger.BilheteColetado);
        Assert.Equal(DeviceState.ColetarBilhetes, m.Current);

        Disparar(m, DeviceTrigger.SemBilhetes);
        Assert.Equal(DeviceState.Polling, m.Current);
    }

    /// <summary>
    /// Ao voltar do modo off-line, os bilhetes são coletados ANTES de retomar a
    /// operação on-line — senão eles se perdem na próxima configuração.
    /// </summary>
    [Fact]
    public void Volta_do_offline_coleta_bilhetes_antes_de_operar()
    {
        var m = Maquina(DeviceState.OfflineAutonomo);

        Disparar(m, DeviceTrigger.ConexaoOk);

        Assert.Equal(DeviceState.ColetarBilhetes, m.Current);
    }
}
