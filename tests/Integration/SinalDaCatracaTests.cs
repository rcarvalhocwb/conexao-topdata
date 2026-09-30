using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Sinal da catraca não é leitura (defeito F4, docs/34 §2).
/// </summary>
/// <remarks>
/// Em Polling, toda origem que não fosse 5 ou 6 ia para a decisão. Urna cheia, cartão
/// recolhido, sensores e teclas chegam sem código: viravam negação, o display mostrava
/// "Acesso nao autorizado" a quem estava na frente e o leitor era rearmado. Hoje isso está
/// dormente só porque urna e sensores estão desligados.
/// </remarks>
public sealed class SinalDaCatracaTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

    private readonly InnerSimulator _sim = new(() => Agora);
    private readonly List<DeviceEvent> _recebidos = [];
    private readonly List<DeviceEvent> _decididos = [];
    private readonly DevicePump _laco;
    private readonly DeviceSlot _catraca;

    public SinalDaCatracaTests()
    {
        _sim.AbrirPorta(3570);
        _laco = new DevicePump(
            _sim,
            () => Agora,
            decidir: e =>
            {
                _decididos.Add(e);
                return new Decision(
                    DecisionOutcome.Allowed, ReasonCodes.Autorizado, DegradationTier.T1SemInternet, TimeSpan.Zero, []);
            },
            aoReceberEvento: _recebidos.Add);

        _catraca = new DeviceSlot(1, Configuracao(), () => Agora);

        for (var i = 0; i < 30 && _catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(DeviceState.Polling, _catraca.Maquina.Current);

        // O primeiro passo em Polling acerta o relógio.
        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(1, _sim.Dispositivo(1).AcertosDeRelogio);
    }

    public void Dispose() => _sim.Dispose();

    private static DeviceConfiguration Configuracao() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 1,
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

    [Theory]
    [InlineData(20)] // urna cheia
    [InlineData(7)]  // cartão recolhido pela urna
    [InlineData(8)]  // sensor 1
    [InlineData(65)] // tecla de função
    [InlineData(14)] // desconhecida: preservada, e não é leitura
    public void Sinal_em_polling_e_registrado_sem_decisao_nem_negacao(int origem)
    {
        var dispositivo = _sim.Dispositivo(1);
        var reabilitacoesAntes = dispositivo.ReabilitacoesDoLeitor;
        dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.FromRaw(origem)));

        var feito = _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));

        // Registrado, com o valor bruto (ADR-0018)...
        var recebido = Assert.Single(_recebidos);
        Assert.Equal(origem, recebido.Origin.Raw);
        Assert.Contains("sem decisão", feito, StringComparison.Ordinal);

        // ...mas sem tentativa, sem "Acesso nao autorizado" e sem rearmar o leitor.
        Assert.Empty(_decididos);
        Assert.Empty(dispositivo.MensagensTemporarias);
        Assert.Equal(DeviceState.Polling, _catraca.Maquina.Current);

        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(reabilitacoesAntes, dispositivo.ReabilitacoesDoLeitor);
        Assert.Empty(dispositivo.LiberacoesPedidas);
        Assert.Equal(DeviceState.Polling, _catraca.Maquina.Current);
        Assert.Contains(_catraca.Maquina.History, t => t.Trigger == DeviceTrigger.SinalDaCatraca);
    }

    /// <summary>A leitura de QR Code (origem 21) continua indo para a decisão.</summary>
    [Fact]
    public void Leitura_de_qr_com_codigo_decide()
    {
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.QrCode), "0000000101"));

        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(DeviceState.ValidarAcesso, _catraca.Maquina.Current);

        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));

        var decidido = Assert.Single(_decididos);
        Assert.Equal(KnownEventOrigin.QrCode, decidido.Origin.Known);
        Assert.Equal(DeviceState.LiberarCatraca, _catraca.Maquina.Current);
    }

    /// <summary>Urna cheia no meio da fila não atrapalha a leitura seguinte.</summary>
    [Fact]
    public void Leitura_depois_de_um_sinal_decide_normalmente()
    {
        _sim.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.UrnaCheia)),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0000000101"));

        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));
        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));
        _laco.Passo(_catraca, TimeSpan.FromMilliseconds(10));

        var decidido = Assert.Single(_decididos);
        Assert.Equal(KnownEventOrigin.Leitor1, decidido.Origin.Known);
        Assert.Equal(2, _recebidos.Count);
    }
}
