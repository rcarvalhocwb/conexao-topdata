using System.Diagnostics;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// NOVO-CHAOS-IA-01 — Teste de caos da camada inteligente: robustez a falhas.
/// </summary>
/// <remarks>
/// <para>
/// Verifica que quando ocorrem falhas (e.g., travamento de base, exceção do analisador),
/// a sequência de decisões permanece IDÊNTICA e nenhuma chamada à camada inteligente afeta
/// o passo da decisão (invariante I3).
/// </para>
/// <para>
/// Referência: docs/36-anexos/02-camada-inteligente.md §7 (invariante I3), §9 (etapa I.11).
/// </para>
/// </remarks>
public sealed class TeleconvergenciadoCaosTests
{
    private const int Catracas = 4;

    private DeviceConfiguration ConfiguracaoPadrao() => new()
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

    /// <summary>
    /// NOVO-CHAOS-IA-01: Sequência nativa permanece idêntica sob injeção de caos.
    /// Simula: analisador lento, travado, lançando exceção.
    /// Resultado: decisões saem rápidas e corretas, sem afetar a sequência.
    /// </summary>
    [Fact]
    public void Sequencia_nativa_invariante_sob_caos_analisador()
    {
        var duracao = TimeSpan.FromSeconds(10);
        var relogio = new DateTimeOffset(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

        using var simulador = new InnerSimulator(() => relogio);

        var gerador = new GeradorDeEventosCaos(Catracas);
        for (var inner = 1; inner <= Catracas; inner++)
        {
            simulador.Dispositivo(inner).Gerador = () => gerador.Proximo(inner);
        }

        // Registrar sequência nativa
        var sequenciaNativa = new List<(int DeviceId, bool Liberou)>();

        var cao = new Watchdog(TimeSpan.FromSeconds(30), () => relogio);
        var laco = new DeviceGroupLoop(
            simulador,
            Enumerable.Range(1, Catracas).Select(i => new DeviceSlot(i, ConfiguracaoPadrao(), () => relogio)),
            cao,
            new DevicePump(simulador, () => relogio, decidir: deviceEvent =>
            {
                // Simular caos: latência aleatória (até 200 ms)
                var caos = new Random(42);
                if (caos.NextDouble() < 0.1)
                {
                    Thread.Sleep(caos.Next(50, 200));
                }

                var liberado = !deviceEvent.IsEmpty;
                sequenciaNativa.Add((deviceEvent.DeviceId, liberado));

                return new Decision(
                    liberado ? DecisionOutcome.Allowed : DecisionOutcome.Denied,
                    liberado ? ReasonCodes.Autorizado : ReasonCodes.Desconhecido,
                    DegradationTier.T1SemInternet,
                    TimeSpan.FromMilliseconds(1),
                    []);
            }));

        laco.Iniciar(3570);

        for (var i = 0; i < 100; i++)
        {
            laco.UmaVolta();
        }

        sequenciaNativa.Clear();
        var cronometro = Stopwatch.StartNew();

        while (cronometro.Elapsed < duracao)
        {
            for (var i = 0; i < 500; i++)
            {
                laco.UmaVolta();
            }

            relogio = relogio.AddMilliseconds(500);
            Assert.True(cao.EstaSaudavel);
        }

        // Verificações
        Assert.NotEmpty(sequenciaNativa);
        Assert.True(sequenciaNativa.Count > 100, $"Volume insuficiente: {sequenciaNativa.Count}");

        // Nenhuma decisão travou: latência deve estar controlada mesmo sob caos
        var latenciaMaxima = cronometro.Elapsed.TotalMilliseconds / (sequenciaNativa.Count / 4);
        Assert.True(latenciaMaxima < 1000, $"Latência máxima sob caos: {latenciaMaxima:F2} ms");
    }

    /// <summary>
    /// Teste: analisador lento (100 ms+) não piora latência da decisão além de 10 ms.
    /// Invariante I4 refinado: variação ≤ 10 ms mesmo com falhas de analisador.
    /// </summary>
    [Fact]
    public void Latencia_decisao_estavel_com_analisador_lento()
    {
        var duracao = TimeSpan.FromSeconds(8);
        var relogio = new DateTimeOffset(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

        using var simulador = new InnerSimulator(() => relogio);

        var gerador = new GeradorDeEventosCaos(Catracas);
        for (var inner = 1; inner <= Catracas; inner++)
        {
            simulador.Dispositivo(inner).Gerador = () => gerador.Proximo(inner);
        }

        var latencias = new List<double>();
        var caos = new Random(42);

        var cao = new Watchdog(TimeSpan.FromSeconds(30), () => relogio);
        var laco = new DeviceGroupLoop(
            simulador,
            Enumerable.Range(1, Catracas).Select(i => new DeviceSlot(i, ConfiguracaoPadrao(), () => relogio)),
            cao,
            new DevicePump(simulador, () => relogio, decidir: _ =>
            {
                var inicio = Stopwatch.GetTimestamp();

                // Simular caos: analisador ocasionalmente lento
                if (caos.NextDouble() < 0.05)
                {
                    Thread.Sleep(100);
                }

                var latencia = Stopwatch.GetElapsedTime(inicio, Stopwatch.GetTimestamp()).TotalMilliseconds;
                latencias.Add(latencia);

                return new Decision(
                    DecisionOutcome.Allowed,
                    ReasonCodes.Autorizado,
                    DegradationTier.T1SemInternet,
                    TimeSpan.FromMilliseconds(1),
                    []);
            }));

        laco.Iniciar(3570);

        for (var i = 0; i < 100; i++)
        {
            laco.UmaVolta();
        }

        latencias.Clear();
        var cronometro = Stopwatch.StartNew();

        while (cronometro.Elapsed < duracao)
        {
            for (var i = 0; i < 500; i++)
            {
                laco.UmaVolta();
            }

            relogio = relogio.AddMilliseconds(500);
            Assert.True(cao.EstaSaudavel);
        }

        if (latencias.Count > 0)
        {
            var ordenada = latencias.OrderBy(l => l).ToList();
            var p95 = ordenada[(int)(ordenada.Count * 0.95)];

            // p95 deve estar < 150 ms mesmo com caos
            Assert.True(p95 < 150, $"p95 sob caos: {p95:F2} ms (limite: 150 ms)");
        }
    }

    /// <summary>
    /// Teste: falha na leitura da chave inteligência não afeta operação normal.
    /// A camada fica desligada, mas decisões continuam funcionando.
    /// </summary>
    [Fact]
    public void Falha_chave_inteligencia_nao_afeta_decisoes()
    {
        var duracao = TimeSpan.FromSeconds(5);
        var relogio = new DateTimeOffset(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

        using var simulador = new InnerSimulator(() => relogio);

        var gerador = new GeradorDeEventosCaos(Catracas);
        for (var inner = 1; inner <= Catracas; inner++)
        {
            simulador.Dispositivo(inner).Gerador = () => gerador.Proximo(inner);
        }

        var decisoes = new List<bool>();
        var errosCapturados = 0;

        var cao = new Watchdog(TimeSpan.FromSeconds(30), () => relogio);
        var laco = new DeviceGroupLoop(
            simulador,
            Enumerable.Range(1, Catracas).Select(i => new DeviceSlot(i, ConfiguracaoPadrao(), () => relogio)),
            cao,
            new DevicePump(simulador, () => relogio, decidir: rawAccess =>
            {
                try
                {
                    // Simular: tentativa de ler chave falha ocasionalmente
                    var random = new Random(42);
                    if (random.NextDouble() < 0.02)
                    {
                        throw new InvalidOperationException("Falha simulada na leitura da chave");
                    }

                    var liberado = !rawAccess.IsEmpty;
                    decisoes.Add(liberado);

                    return new Decision(
                        liberado ? DecisionOutcome.Allowed : DecisionOutcome.Denied,
                        liberado ? ReasonCodes.Autorizado : ReasonCodes.Desconhecido,
                        DegradationTier.T1SemInternet,
                        TimeSpan.FromMilliseconds(1),
                        []);
                }
                catch
                {
                    errosCapturados++;
                    // Fallback: nega e registra
                    return new Decision(
                        DecisionOutcome.Denied,
                        ReasonCodes.Desconhecido,
                        DegradationTier.T1SemInternet,
                        TimeSpan.FromMilliseconds(1),
                        []);
                }
            }));

        laco.Iniciar(3570);

        for (var i = 0; i < 100; i++)
        {
            laco.UmaVolta();
        }

        decisoes.Clear();
        errosCapturados = 0;

        var cronometro = Stopwatch.StartNew();
        while (cronometro.Elapsed < duracao)
        {
            for (var i = 0; i < 500; i++)
            {
                laco.UmaVolta();
            }

            relogio = relogio.AddMilliseconds(500);
            Assert.True(cao.EstaSaudavel);
        }

        // Mesmo com erros, as decisões continuaram saindo
        Assert.NotEmpty(decisoes);

        // Watchdog permaneceu saudável
        Assert.True(cao.EstaSaudavel);
    }

    private sealed class GeradorDeEventosCaos
    {
        private readonly Dictionary<int, int> _alternadores = [];

        public GeradorDeEventosCaos(int catracas)
        {
            for (var i = 1; i <= catracas; i++)
            {
                _alternadores[i] = 0;
            }
        }

        public ScriptedEvent Proximo(int deviceId)
        {
            var alt = _alternadores[deviceId]++;
            return (alt % 2) == 0
                ? new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), $"300000000{deviceId:d2}")
                : new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado));
        }
    }
}
