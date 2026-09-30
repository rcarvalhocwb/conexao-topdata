using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// Erro na recepção de eventos entra no caminho de falha, e não no silêncio (defeito F6,
/// docs/34 §2).
/// </summary>
/// <remarks>
/// Antes, todo retorno ≠ 0 de <c>ReceberDadosOnLine</c> virava "sem eventos": a catraca
/// caída continuava "em Polling" até o watchdog pegar. Agora o laço conta, abre caminho para
/// o disjuntor e reconecta — e volta a operar quando a comunicação volta.
/// </remarks>
public sealed class RecepcaoComErroTests
{
    private static DeviceConfiguration Configuracao() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 0,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Aproxime o ingresso",
        PerfilFisico = GatePhysicalProfile.Padrao,
    };

    [Fact]
    public void Erro_na_recepcao_conta_reconecta_e_volta_a_operar()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        void Ate(DeviceState alvo)
        {
            for (var i = 0; i < 40 && catraca.Maquina.Current != alvo; i++)
            {
                agora += TimeSpan.FromMinutes(1);
                laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
            }

            Assert.Equal(alvo, catraca.Maquina.Current);
        }

        Ate(DeviceState.Polling);
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio

        costura.Retornos["ReceberDadosOnLine"] = 1;
        var feito = laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);
        Assert.Equal(1, catraca.ErrosDeRecepcao);
        Assert.Equal(1, adapter.ErrosDeRecepcao);
        Assert.Equal(1, catraca.Disjuntor.FalhasSeguidas);
        Assert.Contains("retorno=1", feito, StringComparison.Ordinal);
        Assert.Contains("ReceberDadosOnLine", feito, StringComparison.Ordinal);

        // A comunicação volta: o mesmo laço reconecta sozinho, sem ninguém reiniciar nada.
        costura.Retornos.Remove("ReceberDadosOnLine");
        Ate(DeviceState.Polling);
        Assert.Equal(1, catraca.ErrosDeRecepcao);
    }

    /// <summary>O 8 continua sendo falha de dependência: insistir não adianta.</summary>
    [Fact]
    public void Retorno_oito_na_recepcao_continua_falha_de_dependencia()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio
        costura.Retornos["ReceberDadosOnLine"] = 8;
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal(DeviceState.FalhaFatalDeDependencia, catraca.Maquina.Current);
    }
}
