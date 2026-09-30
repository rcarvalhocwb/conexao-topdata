using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// A sequência nativa de uma conexão, com o laço e o adapter de verdade.
/// </summary>
/// <remarks>
/// A mensagem padrão saía de dentro da montagem da configuração, entre as funções de buffer
/// e <c>EnviarConfiguracoes</c> — contra a ADR-0006 — e três vezes por conexão (defeito F7,
/// docs/34 §2). Tirá-la de lá só é seguro se o passo próprio do laço (<c>EnviarMsgPadrao</c>)
/// continuar a entregá-la; é o que este teste prova.
/// </remarks>
public sealed class SequenciaDeConexaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);

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
    public void Mensagem_padrao_chega_uma_vez_depois_de_rearmar_o_leitor()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => Agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => Agora);

        for (var i = 0; i < 30 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);

        // Três envios completos (cfg off-line, mudança, on-line — F3 fica para a Etapa A.7)...
        Assert.Equal(3, costura.Chamadas.Count(c => c == "EnviarConfiguracoes"));

        // ...e a mensagem padrão uma vez só, pelo passo próprio, depois de rearmar o leitor.
        Assert.Single(costura.Chamadas, c => c == "EnviarMensagemPadraoOnLine");
        Assert.Equal(["EnviarFormasEntradasOnLine", "EnviarMensagemPadraoOnLine"], costura.Chamadas.TakeLast(2));
        Assert.Equal("Aproxime o ingresso", costura.UltimaMensagem);
    }
}
