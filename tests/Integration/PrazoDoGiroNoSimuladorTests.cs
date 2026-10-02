using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Edge.Worker;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Etapa I.1b do docs/36 (C1): o prazo dos estados aplicado pelo laço, contra o simulador e o
/// decisor de ingresso de verdade. O par com a costura falsa (chamadas nativas exatas) está em
/// <c>HardwareInLoop.Tests.PrazoDoGiroTests</c>.
/// </summary>
public sealed class PrazoDoGiroNoSimuladorTests : IDisposable
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 2, 21, 0, 0, TimeSpan.Zero);

    private readonly InnerSimulator _sim;
    private readonly List<(ComandoDeCatraca Comando, SituacaoDoComando Situacao, string Resultado)> _desfechos = [];
    private readonly List<string> _desistencias = [];
    private DateTimeOffset _agora = Inicio;

    public PrazoDoGiroNoSimuladorTests()
    {
        _sim = new InnerSimulator(() => _agora);
        _sim.AbrirPorta(3570);
    }

    public void Dispose() => _sim.Dispose();

    /// <summary>A base de ingressos, sem SQLite: todo código é consumido; conta as confirmações.</summary>
    private sealed class BaseQueLibera : IValidadorDeIngressos
    {
        public List<Guid> Tentativas { get; } = [];

        public List<Guid> Confirmadas { get; } = [];

        public (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
            string qrNormalizado, string gateId, string deviceId, DateTimeOffset agora, KnownEventOrigin? leitor, int? origemBruta)
        {
            var id = Guid.NewGuid();
            Tentativas.Add(id);
            return (new ResultadoDoUso(MotivoDoUso.Consumido, Guid.NewGuid(), "teste", Categoria: "inteira"), id);
        }

        public void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em) => Confirmadas.Add(tentativaId);
    }

    private SimulatedDevice Catraca1 => _sim.Dispositivo(1);

    private (DevicePump Bomba, DeviceSlot Catraca) EmOperacao(
        Func<DeviceEvent, Decision>? decidir = null,
        Action<DeviceEvent>? aoReceberEvento = null,
        Action<string>? aoDesistirDoGiro = null,
        IGravadorDeBilhetes? gravador = null)
    {
        var bomba = new DevicePump(
            _sim,
            () => _agora,
            decidir: decidir ?? (_ => throw new InvalidOperationException("nenhuma leitura neste teste")),
            aoReceberEvento: aoReceberEvento,
            aoConcluirComando: (c, s, r) => _desfechos.Add((c, s, r)),
            gravadorDeBilhetes: gravador,
            aoDesistirDoGiro: aoDesistirDoGiro ?? _desistencias.Add);
        var catraca = new DeviceSlot(1, PadroesDeFabrica.TopFit4, () => _agora);

        for (var i = 0; i < 30 && Catraca1.AcertosDeRelogio == 0; i++)
        {
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        return (bomba, catraca);
    }

    private static List<string> Passos(DevicePump bomba, DeviceSlot catraca, int quantos)
    {
        var feitos = new List<string>(quantos);
        for (var i = 0; i < quantos; i++)
        {
            feitos.Add(bomba.Passo(catraca, TimeSpan.Zero));
        }

        return feitos;
    }

    private static int Liberacoes(SimulatedDevice d) => d.LiberacoesPedidas.Values.Sum();

    [Fact]
    public void Liberacao_manual_sem_origem_5_nem_6_conclui_pelo_prazo_e_rearma_o_leitor()
    {
        var (bomba, catraca) = EmOperacao();
        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberacaoManual, "Ana (portaria)", _agora, motivo: "Leitor não lê o QR");
        Assert.True(comando is not null, string.Join(" ", problemas));
        catraca.Enfileirar(comando);

        Passos(bomba, catraca, 2);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
        var liberadaEm = _agora;
        var rearmesAntes = Catraca1.ReabilitacoesDoLeitor;

        _agora = liberadaEm + TimeSpan.FromSeconds(7);
        Passos(bomba, catraca, 3);
        Assert.Empty(_desfechos);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);

        _agora = liberadaEm + TimeSpan.FromSeconds(8);
        var feitos = Passos(bomba, catraca, 3);

        Assert.Contains(feitos, f => f.Contains("giro não confirmado: prazo de 8 s", StringComparison.Ordinal));
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(rearmesAntes + 1, Catraca1.ReabilitacoesDoLeitor);
        Assert.Equal(["inner-1"], _desistencias);

        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Concluido, situacao);
        Assert.Equal("liberada; giro não confirmado: prazo de 8 s", resultado);
    }

    /// <summary>
    /// Ingresso liberado e não girado: a regra de hoje (o uso consumido na leitura, a tentativa
    /// encerrada sem giro, como a origem 5 faria) não muda. O que muda é que a pista volta, e um
    /// giro que chegue depois do prazo não confirma a passagem de quem foi liberado.
    /// </summary>
    [Fact]
    public void Ingresso_liberado_e_nao_girado_termina_sem_giro_e_a_proxima_leitura_e_atendida()
    {
        var base_ = new BaseQueLibera();
        var decisor = new DecisorDeIngresso(base_);
        var (bomba, catraca) = EmOperacao(decisor.Decidir, decisor.AoReceberEvento, decisor.DescartarPendente);

        Catraca1.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1000000001"));
        Passos(bomba, catraca, 3);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
        Assert.Equal(1, Liberacoes(Catraca1));

        _agora += TimeSpan.FromSeconds(8);
        Passos(bomba, catraca, 3);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(1, decisor.AutorizacoesSemGiro);
        Assert.Equal(0, decisor.PassagensConfirmadas);

        // Giro tardio, com a pista já em Polling: não é da pessoa liberada.
        Catraca1.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Passos(bomba, catraca, 1);
        Assert.Empty(base_.Confirmadas);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);

        // A próxima pessoa é atendida, e o giro dela confirma a passagem dela.
        Catraca1.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1000000002"));
        Passos(bomba, catraca, 3);
        Assert.Equal(2, Liberacoes(Catraca1));
        Catraca1.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Passos(bomba, catraca, 3);

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal([base_.Tentativas[1]], base_.Confirmadas);
        Assert.Equal(1, decisor.PassagensConfirmadas);
    }

    /// <summary>
    /// Liberação presa sem poder chamar a catraca (disjuntor aberto): o prazo de LiberarCatraca
    /// (5 s) segue a tabela — reconectar —, a liberação não sai atrasada e a tentativa termina sem
    /// giro.
    /// </summary>
    [Fact]
    public void Liberacao_impedida_pelo_disjuntor_reconecta_no_prazo_sem_liberar_atrasado()
    {
        var (bomba, catraca) = EmOperacao(decidir: _ => new Decision(
            DecisionOutcome.Allowed, ReasonCodes.Autorizado, DegradationTier.T1SemInternet, TimeSpan.FromMilliseconds(2), []));

        Catraca1.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1000000001"));
        Passos(bomba, catraca, 2);
        Assert.Equal(DeviceState.LiberarCatraca, catraca.Maquina.Current);
        var entrouEm = _agora;

        for (var i = 0; i < 5; i++)
        {
            catraca.Disjuntor.RegistrarFalha();
        }

        _agora = entrouEm + TimeSpan.FromSeconds(4);
        Assert.Equal("disjuntor aberto", bomba.Passo(catraca, TimeSpan.Zero));
        Assert.Empty(_desistencias);

        _agora = entrouEm + TimeSpan.FromSeconds(5);
        Assert.Equal(
            "prazo de 5 s em LiberarCatraca esgotado — Reconectar (disjuntor aberto)",
            bomba.Passo(catraca, TimeSpan.Zero));
        Assert.Equal(["inner-1"], _desistencias);
        Assert.Equal(0, Liberacoes(Catraca1));

        // Fechado o disjuntor, a catraca volta pela reconexão sem liberar o giro velho.
        _agora = entrouEm + TimeSpan.FromSeconds(40);
        Passos(bomba, catraca, 12);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(0, Liberacoes(Catraca1));
    }

    /// <summary>
    /// A coleta passa do prazo do estado (10 min): para antes de pedir o próximo bilhete, o que
    /// já saiu está gravado, o resto fica na catraca, e a catraca volta a operar pela reconexão.
    /// </summary>
    [Fact]
    public void Coleta_que_passa_do_prazo_para_antes_do_proximo_bilhete_e_a_catraca_volta()
    {
        var gravados = new List<BilheteColetado>();
        var gravador = new GravadorEmLista(gravados);
        Catraca1.ComBilhetes([.. Enumerable.Range(1, 5).Select(i => new Bilhete(10, Inicio.AddMinutes(-i), $"9999{i:D10}"))]);
        var (bomba, catraca) = EmOperacao(gravador: gravador);

        var (comando, problemas) = ComandoDeCatraca.Criar(1, TipoDeComando.ColetarBilhetes, "Ana (portaria)", _agora);
        Assert.True(comando is not null, string.Join(" ", problemas));
        catraca.Enfileirar(comando);

        // Pedido, coleta do 1º, gravação do 1º, coleta do 2º, gravação do 2º.
        Passos(bomba, catraca, 5);
        Assert.Equal(2, gravados.Count);
        Assert.Equal(DeviceState.ColetarBilhetes, catraca.Maquina.Current);

        _agora += TimeSpan.FromMinutes(10);
        var feito = bomba.Passo(catraca, TimeSpan.Zero);

        Assert.StartsWith("prazo de 600 s em ColetarBilhetes esgotado — Degradado", feito, StringComparison.Ordinal);
        Assert.Equal(3, Catraca1.BilhetesNaMemoria);
        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Falhou, situacao);
        Assert.StartsWith("coleta interrompida (estado Degradado); 2 bilhete(s) coletado(s): 2 gravado(s)", resultado, StringComparison.Ordinal);

        Passos(bomba, catraca, 12);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(2, gravados.Count);
    }

    private sealed class GravadorEmLista(List<BilheteColetado> gravados) : IGravadorDeBilhetes
    {
        public DesfechoDaGravacaoDoBilhete Gravar(BilheteColetado bilhete)
        {
            gravados.Add(bilhete);
            return DesfechoDaGravacaoDoBilhete.Gravado;
        }
    }
}
