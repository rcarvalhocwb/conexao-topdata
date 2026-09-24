using Access.Application.Devices;
using Access.Domain.Devices;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Cenários do simulador exigidos por docs/06, seção 4.
/// </summary>
public sealed class SimuladorTests
{
    private static DeviceConfiguration ConfiguracaoDeUrna() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 3,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 2,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 3,
        TempoDoAcionamento2 = 6,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 2,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Apresente o ingresso",
        PerfilFisico = new GatePhysicalProfile(SentidoInvertido: false),
    };

    private static InnerSimulator SimuladorAberto(int porta = 3570)
    {
        var simulador = new InnerSimulator();
        simulador.AbrirPorta(porta);
        return simulador;
    }

    [Fact]
    public void Abrir_a_porta_duas_vezes_e_recusado()
    {
        using var simulador = SimuladorAberto();

        Assert.Throws<InvalidOperationException>(() => simulador.AbrirPorta(3570));
    }

    [Fact]
    public void Comando_antes_de_abrir_a_porta_e_recusado()
    {
        using var simulador = new InnerSimulator();

        Assert.Throws<InvalidOperationException>(() => simulador.TestarConexao(1));
    }

    [Theory]
    [InlineData(KnownEventOrigin.Leitor1)]
    [InlineData(KnownEventOrigin.Leitor2)]
    [InlineData(KnownEventOrigin.Teclado)]
    [InlineData(KnownEventOrigin.QrCode)]
    [InlineData(KnownEventOrigin.SensorBiometrico)]
    public void Entrega_leituras_de_cada_forma_de_entrada(KnownEventOrigin origem)
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(origem), "0001234567"));

        var (resultado, evento) = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.True(resultado.IsOk);
        Assert.NotNull(evento);
        Assert.Equal(origem, evento.Origin.Known);
        Assert.Equal("0001234567", evento.RawCardData);
    }

    [Fact]
    public void Sem_roteiro_devolve_sem_eventos_e_nao_erro()
    {
        using var simulador = SimuladorAberto();

        var (resultado, evento) = simulador.AguardarEvento(1, TimeSpan.FromMilliseconds(50));

        Assert.Equal(AdapterStatus.SemEventos, resultado.Status);
        Assert.Null(evento);
    }

    /// <summary>
    /// Retorno 8 é o GPF documentado. Precisa ser distinguível de erro comum, porque a
    /// causa e a ação do operador são completamente diferentes.
    /// </summary>
    [Fact]
    public void Retorno_8_e_classificado_como_falha_de_dependencia()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).RetornoForcado = 8;

        var resultado = simulador.TestarConexao(1);

        Assert.Equal(AdapterStatus.FalhaDeDependencia, resultado.Status);
        Assert.Equal(8, resultado.NativeReturn);
    }

    /// <summary>
    /// Retorno fora do conjunto documentado é preservado, não vira "erro genérico".
    /// </summary>
    [Fact]
    public void Retorno_desconhecido_preserva_o_valor_bruto()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).RetornoForcado = 217;

        var resultado = simulador.TestarConexao(1);

        Assert.Equal(AdapterStatus.RetornoDesconhecido, resultado.Status);
        Assert.Equal(217, resultado.NativeReturn);
    }

    [Fact]
    public void Origem_desconhecida_chega_intacta()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.FromRaw(14)));

        var (_, evento) = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.NotNull(evento);
        Assert.False(evento.Origin.IsKnown);
        Assert.Equal(14, evento.Origin.Raw);
    }

    [Fact]
    public void Desconexao_vira_erro_de_comunicacao()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).Desconectado = true;

        var (resultado, evento) = simulador.AguardarEvento(1, TimeSpan.FromMilliseconds(50));

        Assert.Equal(AdapterStatus.ErroDeComunicacao, resultado.Status);
        Assert.Null(evento);
    }

    /// <summary>
    /// A DLL travada não devolve. Quem resolve é o watchdog do worker — e o simulador
    /// precisa reproduzir isso para que o watchdog possa ser testado.
    /// </summary>
    [Fact]
    public void Laco_travado_nao_devolve_por_conta_propria()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).LacoTravado = true;

        Assert.Throws<TimeoutException>(() => simulador.AguardarEvento(1, TimeSpan.FromMilliseconds(50)));
    }

    /// <summary>
    /// SIM-URNA-01: leitura, recolhimento confirmado, liberação, giro confirmado.
    /// </summary>
    [Fact]
    public void Fluxo_da_urna_completo_ate_o_giro()
    {
        using var simulador = SimuladorAberto();
        var dispositivo = simulador.Dispositivo(1);

        simulador.EnviarConfiguracaoCompleta(1, ConfiguracaoDeUrna());

        dispositivo.Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor2), "0001234567"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.CartaoRecolhidoUrna)),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));

        var leitura = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1)).Evento;
        Assert.Equal(KnownEventOrigin.Leitor2, leitura!.Origin.Known);

        simulador.AcionarReleDaUrna(1, TimeSpan.FromSeconds(6));

        var recolhimento = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1)).Evento;
        Assert.Equal(KnownEventOrigin.CartaoRecolhidoUrna, recolhimento!.Origin.Known);
        Assert.False(recolhimento.Origin.ConfirmaPassagemFisica);

        simulador.LiberarGiro(1, GateDirection.Entrada);

        var giro = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1)).Evento;
        Assert.True(giro!.Origin.ConfirmaPassagemFisica);

        Assert.Equal(1, dispositivo.AcionamentosDaUrna);
        Assert.Equal(1, dispositivo.LiberacoesPedidas[GateDirection.Entrada]);
    }

    /// <summary>SIM-URNA-02: recolheu, mas ninguém girou (origem 5 em vez da 6).</summary>
    [Fact]
    public void Recolhimento_sem_giro_nao_confirma_passagem()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.CartaoRecolhidoUrna)),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.FimTempoAcionamento)));

        simulador.AguardarEvento(1, TimeSpan.FromSeconds(1));
        var fim = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1)).Evento;

        Assert.Equal(KnownEventOrigin.FimTempoAcionamento, fim!.Origin.Known);
        Assert.False(fim.Origin.ConfirmaPassagemFisica);
    }

    /// <summary>SIM-URNA-06: urna cheia emite a origem 20 e o fluxo precisa parar.</summary>
    [Fact]
    public void Urna_cheia_emite_origem_20()
    {
        using var simulador = SimuladorAberto();
        var dispositivo = simulador.Dispositivo(1);
        dispositivo.UrnaCheia = true;

        simulador.AcionarReleDaUrna(1, TimeSpan.FromSeconds(6));
        var (_, evento) = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.Equal(KnownEventOrigin.UrnaCheia, evento!.Origin.Known);
        Assert.Equal(0, dispositivo.AcionamentosDaUrna);
    }

    [Fact]
    public void Tempo_de_acionamento_acima_do_limite_do_manual_e_recusado()
    {
        using var simulador = SimuladorAberto();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => simulador.AcionarReleDaUrna(1, TimeSpan.FromSeconds(51)));
    }

    [Fact]
    public void Mensagem_maior_que_o_display_e_recusada()
    {
        using var simulador = SimuladorAberto();

        Assert.Throws<ArgumentException>(
            () => simulador.ExibirMensagemTemporaria(1, new string('x', 33), TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Configuracao_invalida_e_recusada_antes_de_chegar_ao_equipamento()
    {
        using var simulador = SimuladorAberto();

        // Relé 2 configurado para urna, mas leitor 2 desabilitado: a fenda não receberia
        // leitura nenhuma. É a falha descrita no manual, seção 7.2.5.
        var invalida = ConfiguracaoDeUrna() with { OperacaoDoLeitor2 = 0 };

        var erro = Assert.Throws<ArgumentException>(
            () => simulador.EnviarConfiguracaoCompleta(1, invalida));

        Assert.Contains("leitor 2", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Coleta_de_bilhetes_esvazia_a_memoria_e_termina()
    {
        using var simulador = SimuladorAberto();
        var quando = new DateTimeOffset(2026, 9, 24, 18, 0, 0, TimeSpan.Zero);

        simulador.Dispositivo(1).ComBilhetes(
            new Bilhete(10, quando, "0000000001"),
            new Bilhete(11, quando, "0000000002"),
            new Bilhete(12, quando, "0000000003"));

        var coletados = new List<Bilhete>();
        while (true)
        {
            var (resultado, bilhete) = simulador.ColetarBilhete(1);
            if (resultado.Status == AdapterStatus.SemBilhetes)
            {
                break;
            }

            coletados.Add(bilhete!);
        }

        Assert.Equal(3, coletados.Count);
        Assert.Equal([10, 11, 12], coletados.Select(b => b.Tipo));

        // Segunda passada não devolve nada: os bilhetes foram removidos na coleta.
        Assert.Equal(AdapterStatus.SemBilhetes, simulador.ColetarBilhete(1).Resultado.Status);
    }

    [Fact]
    public void Relogio_do_equipamento_com_desvio_e_reportado_sem_correcao()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromMinutes(-7);

        simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0000000001"));

        var (_, evento) = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.NotNull(evento!.ClockDrift);
        Assert.Equal(TimeSpan.FromMinutes(-7), evento.ClockDrift!.Value);
    }

    /// <summary>
    /// Reinício do equipamento gera novo bootId: a sequência recomeça sem colidir com o
    /// ciclo anterior.
    /// </summary>
    [Fact]
    public void Reinicio_gera_novo_boot_id()
    {
        using var simulador = SimuladorAberto();
        var dispositivo = simulador.Dispositivo(1);

        dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1"));
        var antes = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1)).Evento!;

        dispositivo.Reiniciar();
        dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1"));
        var depois = simulador.AguardarEvento(1, TimeSpan.FromSeconds(1)).Evento!;

        Assert.NotEqual(antes.Key.BootId, depois.Key.BootId);
        Assert.Equal(1, depois.Key.DeviceSeq);
        Assert.NotEqual(antes.Key, depois.Key);
    }

    [Fact]
    public void Varios_equipamentos_sao_atendidos_pelo_mesmo_worker()
    {
        using var simulador = SimuladorAberto();

        for (var inner = 1; inner <= 20; inner++)
        {
            simulador.Dispositivo(inner).Roteirizar(
                new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), $"{inner:D10}"));
        }

        var lidos = new List<string>();
        for (var inner = 1; inner <= 20; inner++)
        {
            var (_, evento) = simulador.AguardarEvento(inner, TimeSpan.FromSeconds(1));
            lidos.Add(evento!.Key.DeviceId);
        }

        Assert.Equal(20, lidos.Distinct().Count());
    }

    /// <summary>
    /// A DLL não é thread-safe. O simulador recusa uso concorrente para que o defeito
    /// apareça no teste, e não no portão.
    /// </summary>
    [Fact]
    public async Task Uso_concorrente_e_recusado()
    {
        using var simulador = SimuladorAberto();
        simulador.Dispositivo(1).LacoTravado = true;

        // Esta thread entra no adapter e fica presa lá dentro, segurando o acesso.
        var travado = Task.Run(() => simulador.AguardarEvento(1, TimeSpan.FromSeconds(30)));

        // Espera a thread realmente entrar.
        var entrou = SpinWait.SpinUntil(() => simulador.ChamadasNativas > 0, TimeSpan.FromSeconds(5));
        Assert.True(entrou, "a thread não chegou a entrar no adapter");

        // Com alguém lá dentro, qualquer outra chamada precisa ser recusada.
        var erro = Assert.Throws<InvalidOperationException>(() => simulador.TestarConexao(2));
        Assert.Contains("thread-safe", erro.Message, StringComparison.Ordinal);

        simulador.Dispositivo(1).LiberarLaco();
        await travado.ConfigureAwait(true);
    }
}
