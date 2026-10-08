using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// Achado E1-04 do docs/41: uma exceção no passo de uma catraca (a base ocupada ao gravar o giro,
/// por exemplo) subia até o processo do worker e derrubava todas as catracas do grupo. Agora o passo
/// que falha vira uma linha no registro e o laço segue para as outras.
/// </summary>
public sealed class GrupoSobreviveAFalhaDeUmaCatracaTests
{
    private static readonly HashSet<byte> LinhaDaCosturaFalsa = [4];

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
    public void Excecao_no_passo_de_uma_catraca_nao_derruba_o_laco_do_grupo()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var explodir = false;
        var pump = new DevicePump(
            adapter,
            () => agora,
            LinhaDaCosturaFalsa,
            decidir: _ => new Decision(DecisionOutcome.Denied, ReasonCodes.CredencialDesconhecida, DegradationTier.T1SemInternet, TimeSpan.Zero, []),
            aoReceberEvento: _ =>
            {
                if (explodir)
                {
                    throw new InvalidOperationException("database is locked");
                }
            });
        var laco = new DeviceGroupLoop(
            adapter,
            [new DeviceSlot(1, Configuracao(), () => agora), new DeviceSlot(2, Configuracao(), () => agora)],
            new Watchdog(relogio: () => agora),
            pump);

        for (var i = 0; i < 40 && laco.Dispositivos.Any(d => d.Maquina.Current != DeviceState.Polling); i++)
        {
            laco.UmaVolta();
        }

        Assert.All(laco.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));

        // O primeiro passo em Polling acerta o relógio; o seguinte já espera evento.
        laco.UmaVolta();

        explodir = true;
        costura.OrigemADevolver = (byte)KnownEventOrigin.QrCode;
        costura.CartaoADevolver = "0000000101";

        IReadOnlyList<(int Inner, string Acao)>? acoes = null;
        var erro = Record.Exception(() => acoes = laco.UmaVolta());

        Assert.Null(erro);
        Assert.NotNull(acoes);
        Assert.Equal(2, acoes.Count);
        Assert.All(acoes, a => Assert.Contains("falha inesperada no passo (InvalidOperationException", a.Acao, StringComparison.Ordinal));

        // A volta seguinte roda normalmente.
        explodir = false;
        costura.OrigemADevolver = 0;
        costura.CartaoADevolver = string.Empty;
        Assert.Null(Record.Exception(() => laco.UmaVolta()));
    }
}
