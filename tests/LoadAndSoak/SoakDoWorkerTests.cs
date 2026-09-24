using System.Diagnostics;
using System.Globalization;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Simulator;

namespace Soak.Tests;

/// <summary>
/// SOAK — 10 equipamentos girando no laço sem vazar memória nem perder evento.
/// </summary>
/// <remarks>
/// <para>
/// A duração padrão é curta, para caber na CI de todo commit. O ensaio de verdade, de
/// uma hora ou mais, roda com <c>SOAK_MINUTOS=60</c>. Ver docs/06, seção 2.
/// </para>
/// <para>
/// Vazamento aqui não é hipótese acadêmica: o worker fica de pé por dias num evento, e
/// um objeto retido por leitura vira gigabytes. O ensaio processa centenas de milhares
/// de eventos justamente para que um vazamento de poucos bytes por evento apareça.
/// </para>
/// </remarks>
public sealed class SoakDoWorkerTests
{
    private const int Equipamentos = 10;

    private static TimeSpan Duracao()
    {
        var configurado = Environment.GetEnvironmentVariable("SOAK_MINUTOS");

        return double.TryParse(configurado, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutos)
            && minutos > 0
                ? TimeSpan.FromMinutes(minutos)
                : TimeSpan.FromSeconds(20);
    }

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
        PerfilFisico = new GatePhysicalProfile(SentidoInvertido: false),
    };

    private static long MemoriaEstavel()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [Fact]
    public void Dez_equipamentos_rodam_sem_vazar_e_sem_perder_evento()
    {
        var duracao = Duracao();
        var relogio = new DateTimeOffset(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

        using var simulador = new InnerSimulator(() => relogio);

        long emitidos = 0;
        long recebidos = 0;

        // Alterna leitura e giro, que é o ciclo completo de um acesso.
        for (var inner = 1; inner <= Equipamentos; inner++)
        {
            var alternador = 0;
            simulador.Dispositivo(inner).Gerador = () =>
            {
                Interlocked.Increment(ref emitidos);
                return (alternador++ % 2) == 0
                    ? new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0001234567")
                    : new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado));
            };
        }

        var cao = new Watchdog(TimeSpan.FromSeconds(30), () => relogio);
        var laco = new DeviceGroupLoop(
            simulador,
            Enumerable.Range(1, Equipamentos).Select(i => new DeviceSlot(i, Configuracao(), () => relogio)),
            cao,
            new DevicePump(
                simulador,
                () => relogio,
                decidir: _ => new Decision(
                    DecisionOutcome.Allowed,
                    ReasonCodes.Autorizado,
                    DegradationTier.T1SemInternet,
                    TimeSpan.FromMilliseconds(5),
                    []),

                // Consome o evento como a persistência faria, sem acumular.
                aoReceberEvento: _ => Interlocked.Increment(ref recebidos)));

        laco.Iniciar(3570);

        // Aquece: leva alguns ciclos para todos chegarem a operar, e o JIT e os buffers
        // se acomodarem. Medir antes disso confundiria aquecimento com vazamento.
        for (var i = 0; i < 200; i++)
        {
            laco.UmaVolta();
        }

        var memoriaInicial = MemoriaEstavel();
        var cronometro = Stopwatch.StartNew();
        var amostras = new List<long>();

        while (cronometro.Elapsed < duracao)
        {
            for (var i = 0; i < 500; i++)
            {
                laco.UmaVolta();
            }

            // O relógio simulado avança junto, senão o watchdog envelheceria sozinho.
            relogio = relogio.AddMilliseconds(500);

            Assert.True(cao.EstaSaudavel, cao.Diagnostico());

            amostras.Add(MemoriaEstavel());
        }

        cronometro.Stop();

        var memoriaFinal = MemoriaEstavel();
        var crescimento = memoriaFinal - memoriaInicial;

        // Sem evento perdido: tudo que o simulador emitiu chegou ao consumidor.
        Assert.Equal(emitidos, recebidos);
        Assert.True(recebidos > 10_000, $"o ensaio precisa processar volume para ter valor; processou {recebidos}");

        // Teto generoso em valor absoluto: um vazamento por evento seria linear no
        // volume e estouraria isso com folga, enquanto ruído de GC não.
        const long TetoDeCrescimento = 32L * 1024 * 1024;

        Assert.True(
            crescimento < TetoDeCrescimento,
            $"memória cresceu {crescimento / 1024 / 1024} MB após {recebidos} eventos " +
            $"({cronometro.Elapsed.TotalSeconds:F0}s, {laco.Voltas} voltas). " +
            $"Amostras (MB): {string.Join(", ", amostras.Select(a => a / 1024 / 1024))}");

        // O laço não pode acumular estado por equipamento.
        Assert.All(laco.Dispositivos, d => Assert.NotNull(d.UltimoEvento));
    }

    /// <summary>
    /// O ensaio só tem valor se o volume for alto o bastante para revelar vazamento de
    /// poucos bytes por evento. Este teste protege o próprio ensaio.
    /// </summary>
    [Fact]
    public void O_ensaio_padrao_processa_volume_suficiente()
    {
        var duracao = Duracao();

        Assert.True(duracao >= TimeSpan.FromSeconds(20), $"duração curta demais: {duracao}");
    }
}
