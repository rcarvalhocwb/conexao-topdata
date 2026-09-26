using Microsoft.Extensions.Hosting;

namespace Edge.Supervisor;

/// <summary>
/// Gira a supervisão enquanto o serviço está de pé.
/// </summary>
/// <remarks>
/// O <see cref="WorkerSupervisor"/> é deliberadamente síncrono e sem relógio próprio, para
/// ser testável sem esperar tempo passar. Quem o faz girar é este laço.
/// </remarks>
internal sealed class LacoDeSupervisao(WorkerSupervisor supervisor) : BackgroundService
{
    /// <summary>Intervalo entre rondas.</summary>
    /// <remarks>
    /// Curto o bastante para um worker morto voltar antes de a fila sentir; longo o bastante
    /// para não transformar supervisão em consumo de CPU.
    /// </remarks>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        supervisor.Iniciar();

        using var relogio = new PeriodicTimer(Intervalo);

        while (await relogio.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var acao in supervisor.Supervisionar())
            {
                Console.WriteLine($"[supervisão] {acao.Worker}: {acao.Situacao} — {acao.Acao}");
            }
        }
    }
}
