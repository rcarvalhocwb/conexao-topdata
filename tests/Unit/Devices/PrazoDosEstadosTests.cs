using Access.Application.Devices;
using Access.Domain.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// Etapa I.1b do docs/36 (C1): o que a máquina de estados oferece para o laço aplicar o prazo —
/// o instante de entrada no estado e o prazo efetivo do giro.
/// </summary>
public sealed class PrazoDosEstadosTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);

    private static void Disparar(DeviceStateMachine m, DeviceTrigger gatilho, DateTimeOffset em) =>
        Assert.True(m.TryFire(gatilho, em, "teste-prazo", out _), $"gatilho {gatilho} recusado no estado {m.Current}");

    [Fact]
    public void Entrada_no_estado_e_marcada_e_voltas_no_mesmo_estado_nao_a_renovam()
    {
        var m = new DeviceStateMachine("catraca-08", DeviceState.Polling);
        Assert.Null(m.EstadoAtualDesde);

        Disparar(m, DeviceTrigger.EventoRecebido, Agora);
        Disparar(m, DeviceTrigger.AcessoPermitido, Agora.AddMilliseconds(20));
        Disparar(m, DeviceTrigger.ComandoDeLiberacaoOk, Agora.AddMilliseconds(40));
        Assert.Equal(DeviceState.MonitoraGiroCatraca, m.Current);
        Assert.Equal(Agora.AddMilliseconds(40), m.EstadoAtualDesde);

        // Mais de 100 voltas sem evento: o histórico (limitado) já não tem a entrada, o marco tem.
        for (var i = 1; i <= DeviceStateMachine.TamanhoDoHistorico + 5; i++)
        {
            Disparar(m, DeviceTrigger.SemEventos, Agora.AddMilliseconds(40 + (i * 50)));
        }

        Assert.DoesNotContain(m.History, t => t.To is DeviceState.MonitoraGiroCatraca && t.From is not DeviceState.MonitoraGiroCatraca);
        Assert.Equal(Agora.AddMilliseconds(40), m.EstadoAtualDesde);

        Disparar(m, DeviceTrigger.TempoEsgotado, Agora.AddSeconds(8));
        Assert.Equal(DeviceState.ConfigurarEntradasOnline, m.Current);
        Assert.Equal(Agora.AddSeconds(8), m.EstadoAtualDesde);
    }

    [Fact]
    public void Transicao_recusada_nao_muda_a_entrada()
    {
        var m = new DeviceStateMachine("catraca-08", DeviceState.Polling);
        Disparar(m, DeviceTrigger.EventoRecebido, Agora);

        Assert.False(m.TryFire(DeviceTrigger.GiroConfirmado, Agora.AddSeconds(1), "teste-prazo", out _));
        Assert.Equal(Agora, m.EstadoAtualDesde);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(5, 8)]
    [InlineData(6, 9)]
    [InlineData(20, 23)]
    [InlineData(50, 53)]
    public void Prazo_do_giro_e_o_maior_entre_a_tabela_e_o_rele_mais_a_margem(byte tempoDoRele1, int esperado)
    {
        var configuracao = PadroesDeFabrica.TopFit4 with { TempoDoAcionamento1 = tempoDoRele1 };

        Assert.Equal(TimeSpan.FromSeconds(3), DeviceStateMachine.MargemDoGiro);
        Assert.Equal(
            TimeSpan.FromSeconds(esperado),
            DeviceStateMachine.PrazoEfetivo(DeviceState.MonitoraGiroCatraca, configuracao));
    }

    /// <summary>Só o prazo do giro depende da catraca; os outros são os da tabela.</summary>
    [Fact]
    public void Prazo_dos_outros_estados_e_o_da_tabela()
    {
        var configuracao = PadroesDeFabrica.TopFit4 with { TempoDoAcionamento1 = 50 };

        foreach (var estado in Enum.GetValues<DeviceState>().Where(e => e is not DeviceState.MonitoraGiroCatraca))
        {
            Assert.Equal(DeviceStateMachine.TimeoutFor(estado), DeviceStateMachine.PrazoEfetivo(estado, configuracao));
        }
    }

    /// <summary>
    /// Todo estado com prazo tem para onde ir quando ele estoura: o laço dispara o gatilho, e um
    /// gatilho sem transição seria um prazo que não faz nada (era o caso de todos até a I.1b).
    /// </summary>
    [Fact]
    public void Todo_estado_com_prazo_tem_transicao_de_tempo_esgotado()
    {
        var semSaida = Enum.GetValues<DeviceState>()
            .Where(e => DeviceStateMachine.TimeoutFor(e) is not null)
            .Where(e => !DeviceStateMachine.Transitions.Any(t => t.From == e && t.Trigger is DeviceTrigger.TempoEsgotado))
            .ToList();

        Assert.True(semSaida.Count == 0, $"Estados com prazo e sem TempoEsgotado: {string.Join(", ", semSaida)}");
    }

    /// <summary>
    /// O destino do prazo do giro é rearmar o leitor (o mesmo da origem 5), nunca reconectar
    /// nem liberar de novo.
    /// </summary>
    [Fact]
    public void Prazo_do_giro_leva_ao_rearme_do_leitor_como_a_origem_5()
    {
        var porPrazo = DeviceStateMachine.Transitions.Single(t =>
            t.From is DeviceState.MonitoraGiroCatraca && t.Trigger is DeviceTrigger.TempoEsgotado);
        var porOrigem5 = DeviceStateMachine.Transitions.Single(t =>
            t.From is DeviceState.MonitoraGiroCatraca && t.Trigger is DeviceTrigger.TempoDeAcionamentoEsgotado);

        Assert.Equal(DeviceState.ConfigurarEntradasOnline, porPrazo.To);
        Assert.Equal(porOrigem5.To, porPrazo.To);
    }
}
