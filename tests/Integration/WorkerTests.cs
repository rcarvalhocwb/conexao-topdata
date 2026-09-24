using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Edge.Worker.Resiliencia;
using Simulator;

namespace Integration.Tests;

public sealed class WorkerTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Relógio ajustável e seguro entre threads. <see cref="DateTimeOffset"/> não cabe
    /// numa leitura atômica, então o teste guarda ticks e usa Interlocked — sem isso, a
    /// thread do laço pode ler um valor rasgado.
    /// </summary>
    private sealed class RelogioDeTeste(DateTimeOffset inicio)
    {
        private long _ticks = inicio.UtcTicks;

        public DateTimeOffset Agora => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Avancar(TimeSpan quanto) => Interlocked.Add(ref _ticks, quanto.Ticks);

        public void DefinirPara(DateTimeOffset quando) => Interlocked.Exchange(ref _ticks, quando.UtcTicks);
    }

    private static DeviceConfiguration Configuracao() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 3,
        OperacaoDoLeitor1 = 3,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 2,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Bem-vindo",
        PerfilFisico = new GatePhysicalProfile(SentidoInvertido: false),
    };

    private static Decision Autorizado() => new(
        DecisionOutcome.Allowed,
        ReasonCodes.Autorizado,
        DegradationTier.T1SemInternet,
        TimeSpan.FromMilliseconds(9),
        []);

    private static (InnerSimulator Sim, DeviceGroupLoop Laco, Watchdog Cao) Montar(
        int quantidade = 1,
        Func<DateTimeOffset>? relogio = null,
        Func<DeviceEvent, Decision>? decidir = null)
    {
        var simulador = new InnerSimulator(relogio);
        var cao = new Watchdog(TimeSpan.FromSeconds(30), relogio ?? (() => Inicio));
        var slots = Enumerable.Range(1, quantidade).Select(i => new DeviceSlot(i, Configuracao(), relogio));
        var laco = new DeviceGroupLoop(
            simulador,
            slots,
            cao,
            new DevicePump(simulador, relogio ?? (() => Inicio), decidir: decidir));

        laco.Iniciar(3570);
        return (simulador, laco, cao);
    }

    [Fact]
    public void Equipamento_chega_a_operar_dando_voltas_no_laco()
    {
        var (sim, laco, _) = Montar();
        using var _sim = sim;

        for (var i = 0; i < 12; i++)
        {
            laco.UmaVolta();
        }

        Assert.Equal(DeviceState.Polling, laco.Dispositivos[0].Maquina.Current);
    }

    /// <summary>
    /// ADR-0010: nada é configurado antes de o firmware constar da matriz.
    /// </summary>
    [Fact]
    public void Firmware_nao_homologado_nunca_recebe_configuracao()
    {
        var (sim, laco, _) = Montar();
        using var _sim = sim;

        // Linha 7 (Inner net) não está na lista de homologadas do pump.
        sim.Dispositivo(1).Firmware = new FirmwareInfo(7, 1, 3, 0, 0, TemBiometria: false);

        for (var i = 0; i < 12; i++)
        {
            laco.UmaVolta();
        }

        Assert.Equal(DeviceState.FirmwareIncompativel, laco.Dispositivos[0].Maquina.Current);
        Assert.Empty(sim.Dispositivo(1).ConfiguracoesRecebidas);
    }

    /// <summary>
    /// A cada ciclo de acesso o leitor precisa ser reabilitado, senão a catraca fica
    /// surda para a próxima pessoa (manual, 7.2.3).
    /// </summary>
    [Fact]
    public void Ciclo_de_acesso_reabilita_o_leitor()
    {
        var (sim, laco, _) = Montar(decidir: _ => Autorizado());
        using var _sim = sim;

        for (var i = 0; i < 12; i++)
        {
            laco.UmaVolta();
        }

        var reabilitacoesAntes = sim.Dispositivo(1).ReabilitacoesDoLeitor;

        sim.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0001234567"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));

        for (var i = 0; i < 8; i++)
        {
            laco.UmaVolta();
        }

        Assert.True(
            sim.Dispositivo(1).ReabilitacoesDoLeitor > reabilitacoesAntes,
            "o leitor precisa ser reabilitado depois do ciclo de acesso");
    }

    [Fact]
    public void Retorno_8_leva_o_worker_a_falha_fatal_em_vez_de_insistir()
    {
        var (sim, laco, _) = Montar();
        using var _sim = sim;

        for (var i = 0; i < 12; i++)
        {
            laco.UmaVolta();
        }

        Assert.Equal(DeviceState.Polling, laco.Dispositivos[0].Maquina.Current);

        sim.Dispositivo(1).RetornoForcado = 8;
        laco.UmaVolta();

        Assert.Equal(DeviceState.FalhaFatalDeDependencia, laco.Dispositivos[0].Maquina.Current);
    }

    [Fact]
    public void Desconexao_aciona_backoff_crescente()
    {
        var relogio = new RelogioDeTeste(Inicio);
        var (sim, laco, _) = Montar(relogio: () => relogio.Agora);
        using var _sim = sim;

        sim.Dispositivo(1).Desconectado = true;

        var esperas = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            var antes = relogio.Agora;
            laco.UmaVolta();
            var ate = laco.Dispositivos[0].EsperarAte;
            Assert.NotNull(ate);
            esperas.Add(ate.Value - antes);

            // Avança o relógio além da espera, para permitir a próxima tentativa.
            relogio.DefinirPara(ate.Value + TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(4, laco.Dispositivos[0].TentativasDeReconexao);
        // Com jitter não dá para exigir monotonia estrita; a ordem de grandeza tem que crescer.
        Assert.True(esperas[^1] > esperas[0], $"backoff não cresceu: {string.Join(", ", esperas)}");
    }

    [Fact]
    public void Disjuntor_abre_depois_de_falhas_seguidas_e_poupa_a_thread()
    {
        var relogio = new RelogioDeTeste(Inicio);
        var (sim, laco, _) = Montar(relogio: () => relogio.Agora);
        using var _sim = sim;

        sim.Dispositivo(1).Desconectado = true;

        for (var i = 0; i < 6; i++)
        {
            laco.UmaVolta();
            relogio.DefinirPara((laco.Dispositivos[0].EsperarAte ?? relogio.Agora) + TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(EstadoDoDisjuntor.Aberto, laco.Dispositivos[0].Disjuntor.Estado);

        var chamadasAntes = sim.ChamadasNativas;
        Assert.Equal("disjuntor aberto", laco.UmaVolta()[0].Acao);
        Assert.Equal(chamadasAntes, sim.ChamadasNativas);
    }

    [Fact]
    public void Acima_do_teto_de_equipamentos_o_worker_recusa_iniciar()
    {
        using var simulador = new InnerSimulator();
        var slots = Enumerable.Range(1, DeviceGroupLoop.TetoAbsoluto + 1)
            .Select(i => new DeviceSlot(i, Configuracao()));

        var erro = Assert.Throws<ArgumentException>(
            () => new DeviceGroupLoop(simulador, slots, new Watchdog()));

        Assert.Contains("teto", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inner_repetido_no_mesmo_worker_e_recusado()
    {
        using var simulador = new InnerSimulator();
        var slots = new[] { new DeviceSlot(7, Configuracao()), new DeviceSlot(7, Configuracao()) };

        var erro = Assert.Throws<ArgumentException>(
            () => new DeviceGroupLoop(simulador, slots, new Watchdog()));

        Assert.Contains("repetido", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Vinte_equipamentos_chegam_a_operar_no_mesmo_worker()
    {
        var (sim, laco, _) = Montar(DeviceGroupLoop.TetoRecomendado);
        using var _sim = sim;

        for (var i = 0; i < 12; i++)
        {
            laco.UmaVolta();
        }

        Assert.All(laco.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));
    }

    /// <summary>
    /// O watchdog bate a cada equipamento, então o laço saudável nunca fica velho.
    /// </summary>
    [Fact]
    public void Laco_saudavel_mantem_o_watchdog_batendo()
    {
        var relogio = new RelogioDeTeste(Inicio);
        var (sim, laco, cao) = Montar(5, () => relogio.Agora);
        using var _sim = sim;

        for (var i = 0; i < 10; i++)
        {
            relogio.Avancar(TimeSpan.FromSeconds(1));
            laco.UmaVolta();
            Assert.True(cao.EstaSaudavel, cao.Diagnostico());
        }
    }

    /// <summary>
    /// Uma catraca travada bloqueia as demais DESTE worker — a DLL é sequencial. O que
    /// protege o resto do parque não é o laço, é o watchdog somado ao particionamento.
    /// </summary>
    [Fact]
    public async Task Catraca_travada_bloqueia_o_worker_e_o_watchdog_percebe()
    {
        var relogio = new RelogioDeTeste(Inicio);
        var (sim, laco, cao) = Montar(3, () => relogio.Agora);
        using var _sim = sim;

        sim.Dispositivo(2).LacoTravado = true;

        var laçoTravado = Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 20; i++)
                {
                    laco.UmaVolta();
                }
            }
            catch (TimeoutException)
            {
                // O laço morre ao estourar a espera — quem age antes disso é o watchdog.
            }
        });

        // Espera a thread ficar REALMENTE presa dentro do adapter, e não apenas ter
        // começado a volta: é a posse do acesso que faz o batimento envelhecer.
        Assert.True(
            SpinWait.SpinUntil(() => sim.Dispositivo(2).EntrouNoLacoTravado, TimeSpan.FromSeconds(10)),
            "o laço não ficou preso no equipamento travado");

        relogio.Avancar(TimeSpan.FromSeconds(31));

        Assert.False(cao.EstaSaudavel, cao.Diagnostico());
        Assert.Contains("SEM BATIMENTO", cao.Diagnostico(), StringComparison.Ordinal);

        sim.Dispositivo(2).LiberarLaco();
        await laçoTravado.ConfigureAwait(true);
    }

    [Fact]
    public void Diagnostico_do_watchdog_diz_onde_travou()
    {
        var cao = new Watchdog(TimeSpan.FromSeconds(5), () => Inicio);
        cao.Bater("inner 8 em MonitoraGiroCatraca");

        Assert.Contains("inner 8", cao.Diagnostico(), StringComparison.Ordinal);
        Assert.Contains("MonitoraGiroCatraca", cao.Diagnostico(), StringComparison.Ordinal);
    }
}
