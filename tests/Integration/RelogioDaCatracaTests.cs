using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Fase 4a: o relógio da catraca é acertado a cada conexão e conferido a cada hora.
/// </summary>
/// <remarks>
/// Uma catraca com hora errada carimba cada passagem com hora errada, e o relatório por
/// hora (docs/25) sai errado sem ninguém perceber. Mas o relógio nunca pode parar a
/// catraca: falha nele é informada, não derruba a operação.
/// </remarks>
public sealed class RelogioDaCatracaTests : IDisposable
{
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private readonly InnerSimulator _sim;
    private DateTimeOffset _agora = Inicio;

    public RelogioDaCatracaTests()
    {
        _sim = new InnerSimulator(() => _agora);
        _sim.AbrirPorta(3570);
    }

    public void Dispose() => _sim.Dispose();

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
        PerfilFisico = GatePhysicalProfile.Padrao,
    };

    private (DevicePump Bomba, DeviceSlot Catraca) Montar(bool acertarAoDivergir = false) =>
        (new DevicePump(_sim, () => _agora, acertarRelogioAoDivergir: acertarAoDivergir),
         new DeviceSlot(1, Configuracao(), () => _agora));

    private static List<string> Passos(DevicePump bomba, DeviceSlot catraca, int quantos) =>
        [.. Enumerable.Range(0, quantos).Select(_ => bomba.Passo(catraca, TimeSpan.Zero))];

    /// <summary>Anda até Polling e dá mais um passo — o primeiro passo em Polling.</summary>
    private static List<string> AteOperar(DevicePump bomba, DeviceSlot catraca)
    {
        var feitos = new List<string>();
        for (var i = 0; i < 20 && catraca.Maquina.Current is not DeviceState.Polling; i++)
        {
            feitos.Add(bomba.Passo(catraca, TimeSpan.Zero));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        feitos.Add(bomba.Passo(catraca, TimeSpan.Zero));
        return feitos;
    }

    [Fact]
    public void Relogio_atrasado_e_acertado_no_primeiro_passo_em_polling()
    {
        var (bomba, catraca) = Montar();
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromMinutes(-7);

        var feitos = AteOperar(bomba, catraca);

        Assert.Equal("relógio acertado", feitos[^1]);
        Assert.Equal(1, _sim.Dispositivo(1).AcertosDeRelogio);
        Assert.Equal(TimeSpan.Zero, _sim.Dispositivo(1).DesvioDeRelogio);
        Assert.Equal(Inicio, catraca.RelogioAcertadoEm);

        // O acerto não tira a catraca de operação.
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
    }

    [Fact]
    public void Relogio_e_conferido_um_minuto_depois_do_acerto_e_depois_a_cada_hora()
    {
        var (bomba, catraca) = Montar();
        AteOperar(bomba, catraca);

        // Antes do minuto: só operação.
        Assert.Equal("sem eventos", Passos(bomba, catraca, 1)[0]);

        _agora += TimeSpan.FromMinutes(1);
        Assert.Equal("relógio conferido (0 s)", Passos(bomba, catraca, 1)[0]);
        Assert.Equal(TimeSpan.Zero, catraca.DivergenciaDoRelogio);
        Assert.False(catraca.RelogioDivergente);

        // 30 s ainda está dentro do limite.
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromSeconds(30);
        _agora += TimeSpan.FromMinutes(59);
        Assert.Equal("sem eventos", Passos(bomba, catraca, 1)[0]);
        _agora += TimeSpan.FromMinutes(1);
        Assert.Equal("relógio conferido (+30 s)", Passos(bomba, catraca, 1)[0]);
        Assert.False(catraca.RelogioDivergente);
    }

    [Fact]
    public void Divergencia_acima_de_30_s_e_informada_e_nao_acertada_sozinha_por_padrao()
    {
        var (bomba, catraca) = Montar();
        AteOperar(bomba, catraca);
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromSeconds(-45);

        _agora += TimeSpan.FromMinutes(1);
        var feito = Passos(bomba, catraca, 1)[0];

        Assert.Equal("relógio divergente (-45 s) — acerte pelo painel", feito);
        Assert.True(catraca.RelogioDivergente);
        Assert.Equal(TimeSpan.FromSeconds(-45), catraca.DivergenciaDoRelogio);

        // Nas próximas voltas a catraca só opera: acerto automático está desligado.
        Assert.All(Passos(bomba, catraca, 5), p => Assert.Equal("sem eventos", p));
        Assert.Equal(1, _sim.Dispositivo(1).AcertosDeRelogio);
    }

    [Fact]
    public void Com_acerto_automatico_ligado_a_divergencia_e_corrigida_na_volta_seguinte()
    {
        var (bomba, catraca) = Montar(acertarAoDivergir: true);
        AteOperar(bomba, catraca);
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromMinutes(3);

        _agora += TimeSpan.FromMinutes(1);
        Assert.Equal("relógio divergente (+180 s) — será acertado", Passos(bomba, catraca, 1)[0]);
        Assert.Equal("relógio acertado", Passos(bomba, catraca, 1)[0]);

        Assert.Equal(2, _sim.Dispositivo(1).AcertosDeRelogio);
        Assert.Equal(TimeSpan.Zero, _sim.Dispositivo(1).DesvioDeRelogio);
    }

    [Fact]
    public void Falha_no_relogio_nao_derruba_a_catraca_e_e_tentada_de_novo_em_5_minutos()
    {
        var (bomba, catraca) = Montar();
        _sim.Dispositivo(1).RetornoDoRelogio = 1;

        var feitos = AteOperar(bomba, catraca);

        Assert.StartsWith("falha ao acertar o relógio", feitos[^1]);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.True(catraca.Disjuntor.PermitePassar);
        Assert.Equal(0, catraca.TentativasDeReconexao);

        // Segue atendendo sem insistir no relógio a cada volta.
        Assert.All(Passos(bomba, catraca, 5), p => Assert.Equal("sem eventos", p));

        _sim.Dispositivo(1).RetornoDoRelogio = null;
        _agora += TimeSpan.FromMinutes(5);
        Assert.Equal("relógio acertado", Passos(bomba, catraca, 1)[0]);
    }

    [Fact]
    public void Catraca_que_reconecta_tem_o_relogio_acertado_de_novo()
    {
        var (bomba, catraca) = Montar();
        AteOperar(bomba, catraca);

        _sim.Dispositivo(1).Desconectado = true;
        Passos(bomba, catraca, 1);
        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);

        // Religada com o relógio perdido.
        _sim.Dispositivo(1).Desconectado = false;
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromHours(-5);
        for (var i = 0; i < 30 && _sim.Dispositivo(1).AcertosDeRelogio < 2; i++)
        {
            _agora += TimeSpan.FromMinutes(1);
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(2, _sim.Dispositivo(1).AcertosDeRelogio);
        Assert.Equal(TimeSpan.Zero, _sim.Dispositivo(1).DesvioDeRelogio);
    }

    /// <summary>
    /// O relógio só é mexido em Polling: com alguém no meio de uma passagem, a vez é da
    /// passagem.
    /// </summary>
    [Fact]
    public void Relogio_nao_e_acertado_com_a_catraca_no_meio_de_uma_passagem()
    {
        var bomba = new DevicePump(_sim, () => _agora, decidir: _ => new Access.Domain.Access.Decision(
            Access.Domain.Access.DecisionOutcome.Allowed,
            Access.Domain.Access.ReasonCodes.Autorizado,
            Access.Domain.Access.DegradationTier.T1SemInternet,
            TimeSpan.FromMilliseconds(5),
            []));
        var catraca = new DeviceSlot(1, Configuracao(), () => _agora);
        for (var i = 0; i < 20 && catraca.Maquina.Current is not DeviceState.Polling; i++)
        {
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        // Um cartão chega junto com a primeira volta em Polling.
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0000000001"));
        Assert.Equal("relógio acertado", bomba.Passo(catraca, TimeSpan.Zero));
        Assert.StartsWith("evento", bomba.Passo(catraca, TimeSpan.Zero));

        // Validar, liberar e monitorar o giro: nenhum desses passos mexe no relógio.
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromMinutes(10);
        _agora += TimeSpan.FromHours(2);
        var passagem = Passos(bomba, catraca, 3);
        Assert.DoesNotContain(passagem, p => p.Contains("relógio", StringComparison.Ordinal));
    }
}
