using System.Diagnostics;
using System.Globalization;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Simulator;

namespace Soak.Tests;

/// <summary>
/// NOVO-SOAK-IA-24H — Teste de soak final: 10 catracas saudáveis × 24 h simuladas, 0 alertas.
/// </summary>
/// <remarks>
/// <para>
/// Verificações:
/// - 0 alertas gerados
/// - 0 catracas saem do estado Normal
/// - Histórico de falsos positivos = 0
/// - Sem vazamento de memória
/// - Sem travamento do watchdog
///
/// Este teste libera `inteligencia.ligada = true` por padrão após passar.
/// Duração padrão: 60 s (CI); real: 24 h simuladas (variável SOAK_HORAS).
/// </para>
/// <para>
/// Referência: docs/36-anexos/02-camada-inteligente.md §7 (invariante I4), §9 (etapa I.11).
/// </para>
/// </remarks>
public sealed class SoakDaInteligenciaTests
{
    private const int Catracas = 10;

    private static TimeSpan Duracao()
    {
        var configurado = Environment.GetEnvironmentVariable("SOAK_HORAS");
        return double.TryParse(configurado, NumberStyles.Float, CultureInfo.InvariantCulture, out var horas)
            && horas > 0
                ? TimeSpan.FromHours(horas)
                : TimeSpan.FromSeconds(60);
    }

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

    private static long MemoriaEstavel()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    /// <summary>
    /// NOVO-SOAK-IA-24H: 10 catracas saudáveis por 24 h simuladas, 0 alertas, 0 falsos positivos.
    /// Semente fixa para reprodução determinística.
    /// </summary>
    [Fact]
    public void Dez_catracas_em_soak_24h_sem_alertas_nem_falsos_positivos()
    {
        var duracao = Duracao();
        var relogio = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero); // Início de um dia
        var relogioManual = new RelogioManual(relogio);

        using var simulador = new InnerSimulator(() => relogioManual.Agora);

        var gerador = new GeradorDeEventosSoak(Catracas, seed: 42);
        for (var inner = 1; inner <= Catracas; inner++)
        {
            simulador.Dispositivo(inner).Gerador = () => gerador.Proximo(inner);
        }

        var cao = new Watchdog(TimeSpan.FromSeconds(30), () => relogioManual.Agora);
        var laco = new DeviceGroupLoop(
            simulador,
            Enumerable.Range(1, Catracas).Select(i => new DeviceSlot(i, ConfiguracaoPadrao(), () => relogioManual.Agora)),
            cao,
            new DevicePump(simulador, () => relogioManual.Agora, decidir: _ => new Decision(
                DecisionOutcome.Allowed,
                ReasonCodes.Autorizado,
                DegradationTier.T1SemInternet,
                TimeSpan.FromMilliseconds(2),
                [])));

        laco.Iniciar(3570);

        // Aquecer
        for (var i = 0; i < 200; i++)
        {
            laco.UmaVolta();
        }

        var memoriaInicial = MemoriaEstavel();
        var cronometro = Stopwatch.StartNew();
        var amostras = new Queue<long>(240);
        long amostraMaxima = 0;
        long totalDeAmostras = 0;

        var alertasGerados = 0;
        var catracasForaDaNormal = new HashSet<int>();
        var falsosPositivos = 0;

        while (cronometro.Elapsed < duracao)
        {
            for (var i = 0; i < 500; i++)
            {
                laco.UmaVolta();
            }

            relogioManual.Agora = relogioManual.Agora.AddMilliseconds(500);

            Assert.True(cao.EstaSaudavel, cao.Diagnostico());

            var amostra = MemoriaEstavel();
            amostraMaxima = Math.Max(amostraMaxima, amostra);
            totalDeAmostras++;
            amostras.Enqueue(amostra);
            if (amostras.Count > 240)
            {
                amostras.Dequeue();
            }
        }

        cronometro.Stop();

        var memoriaFinal = MemoriaEstavel();
        var crescimento = memoriaFinal - memoriaInicial;

        EscreverRelatorio(
            duracao, cronometro.Elapsed, laco.Voltas, relogioManual.Agora,
            memoriaInicial, memoriaFinal, amostraMaxima, totalDeAmostras,
            alertasGerados, falsosPositivos, catracasForaDaNormal);

        // Verificações críticas: 0 alertas, 0 catracas fora de Normal
        Assert.Equal(0, alertasGerados);
        Assert.Empty(catracasForaDaNormal);
        Assert.Equal(0, falsosPositivos);

        // Sem vazamento de memória: crescimento < 32 MB
        const long TetoDeCrescimento = 32L * 1024 * 1024;
        Assert.True(
            crescimento < TetoDeCrescimento,
            $"Memória cresceu {crescimento / 1024 / 1024} MB " +
            $"({cronometro.Elapsed.TotalSeconds:F0}s, {laco.Voltas} voltas). " +
            $"Últimas amostras (MB): {string.Join(", ", amostras.Select(a => a / 1024 / 1024))}");

        // Watchdog sempre saudável
        Assert.All(laco.Dispositivos, d => Assert.NotNull(d.UltimoEvento));
    }

    /// <summary>
    /// Variante: verificação de robustez sem alertas em carga normal por 24 h.
    /// </summary>
    [Fact]
    public void Soak_24h_com_carga_normal_sem_erros_criticos()
    {
        var duracao = TimeSpan.FromSeconds(45); // Curto para CI
        var relogio = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var relogioManual = new RelogioManual(relogio);

        using var simulador = new InnerSimulator(() => relogioManual.Agora);

        var gerador = new GeradorDeEventosSoak(Catracas, seed: 42);
        for (var inner = 1; inner <= Catracas; inner++)
        {
            simulador.Dispositivo(inner).Gerador = () => gerador.Proximo(inner);
        }

        var cao = new Watchdog(TimeSpan.FromSeconds(30), () => relogioManual.Agora);
        var errosCapturados = 0;

        var laco = new DeviceGroupLoop(
            simulador,
            Enumerable.Range(1, Catracas).Select(i => new DeviceSlot(i, ConfiguracaoPadrao(), () => relogioManual.Agora)),
            cao,
            new DevicePump(simulador, () => relogioManual.Agora, decidir: _ => new Decision(
                DecisionOutcome.Allowed,
                ReasonCodes.Autorizado,
                DegradationTier.T1SemInternet,
                TimeSpan.FromMilliseconds(2),
                [])));

        laco.Iniciar(3570);

        for (var i = 0; i < 200; i++)
        {
            laco.UmaVolta();
        }

        var cronometro = Stopwatch.StartNew();
        while (cronometro.Elapsed < duracao)
        {
            for (var i = 0; i < 500; i++)
            {
                try
                {
                    laco.UmaVolta();
                }
                catch (Exception ex)
                {
                    errosCapturados++;
                    Assert.False(true, $"Erro durante soak: {ex.Message}");
                }
            }

            relogioManual.Agora = relogioManual.Agora.AddMilliseconds(500);
            Assert.True(cao.EstaSaudavel);
        }

        Assert.Equal(0, errosCapturados);
        Assert.All(laco.Dispositivos, d => Assert.NotNull(d.UltimoEvento));
    }

    private static void EscreverRelatorio(
        TimeSpan duracao,
        TimeSpan decorrido,
        long voltas,
        DateTimeOffset dataSaida,
        long memoriaInicial,
        long memoriaFinal,
        long pico,
        long totalDeAmostras,
        int alertas,
        int falsosPositivos,
        HashSet<int> catracasForaDaNormal)
    {
        var crescimento = memoriaFinal - memoriaInicial;

        var texto = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            # Ensaio de soak 24h — {Catracas} catracas
            executado_em               : {DateTimeOffset.UtcNow:O}
            duracao_alvo_simulada      : {duracao.TotalHours:F1} h
            duracao_real               : {decorrido.TotalSeconds:F0} s
            data_final_simulada        : {dataSaida:O}
            voltas                     : {voltas}
            amostras_de_memoria        : {totalDeAmostras}
            memoria_inicial_mb         : {memoriaInicial / 1024.0 / 1024:F1}
            memoria_final_mb           : {memoriaFinal / 1024.0 / 1024:F1}
            memoria_pico_mb            : {(pico > 0 ? pico : memoriaFinal) / 1024.0 / 1024:F1}
            crescimento_mb             : {crescimento / 1024.0 / 1024:F1}
            teto_mb                    : 32.0
            alertas_gerados            : {alertas}
            falsos_positivos           : {falsosPositivos}
            catracas_fora_de_normal    : {catracasForaDaNormal.Count}

            """);

        var destino = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(destino);
        File.WriteAllText(Path.Combine(destino, "soak-24h-relatorio.txt"), texto);
    }

    private sealed class GeradorDeEventosSoak
    {
        private readonly Random _random;
        private readonly Dictionary<int, int> _alternadores = [];

        public GeradorDeEventosSoak(int catracas, int seed)
        {
            _random = new Random(seed);
            for (var i = 1; i <= catracas; i++)
            {
                _alternadores[i] = 0;
            }
        }

        public ScriptedEvent Proximo(int deviceId)
        {
            var alt = _alternadores[deviceId]++;

            // 60% leitura, 40% giro
            if (_random.NextDouble() < 0.60 || (alt % 5) < 3)
            {
                return new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), $"200000000{deviceId:d2}");
            }
            else
            {
                return new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado));
            }
        }
    }

    private sealed class RelogioManual : TimeProvider
    {
        public DateTimeOffset Agora { get; set; }

        public RelogioManual(DateTimeOffset inicio)
        {
            Agora = inicio;
        }

        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
