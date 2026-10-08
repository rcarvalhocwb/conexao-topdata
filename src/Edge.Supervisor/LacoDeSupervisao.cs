using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edge.Supervisor;

/// <summary>
/// Gira a supervisão enquanto o serviço está de pé.
/// </summary>
/// <remarks>
/// O <see cref="WorkerSupervisor"/> é deliberadamente síncrono e sem relógio próprio, para
/// ser testável sem esperar tempo passar. Quem o faz girar é este laço.
/// </remarks>
internal sealed class LacoDeSupervisao(WorkerSupervisor supervisor, ILogger<LacoDeSupervisao> log) : BackgroundService
{
    /// <summary>Intervalo entre rondas.</summary>
    /// <remarks>
    /// Curto o bastante para um worker morto voltar antes de a fila sentir; longo o bastante
    /// para não transformar supervisão em consumo de CPU.
    /// </remarks>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

    // Worker saudável é informação (console); qualquer outra situação é aviso e vai também para o
    // registro em arquivo, onde o suporte reconstrói o que aconteceu (achado E2-05 do docs/41).
    private static readonly Action<ILogger, string, string, string, Exception?> Normal =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(1, "Supervisao"),
            "{Worker}: {Situacao} · {Acao}");

    private static readonly Action<ILogger, string, string, string, Exception?> Anormal =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(2, "SupervisaoAnormal"),
            "{Worker}: {Situacao} · {Acao}");

    // Última linha de cada worker: o registro em arquivo recebe só as mudanças, não uma linha a cada 2 s.
    private readonly Dictionary<string, string> _ultima = new(StringComparer.Ordinal);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        LacoResiliente.RodarAsync(nameof(LacoDeSupervisao), Rodar, log, stoppingToken, TimeSpan.FromSeconds(5));

    private async Task Rodar(CancellationToken parar)
    {
        Relatar(supervisor.Iniciar());

        using var relogio = new PeriodicTimer(Intervalo);

        while (await relogio.WaitForNextTickAsync(parar).ConfigureAwait(false))
        {
            Relatar(supervisor.Supervisionar());
        }
    }

    private void Relatar(IReadOnlyList<AcaoDeSupervisao> acoes)
    {
        foreach (var acao in acoes)
        {
            Console.WriteLine($"[supervisão] {acao.Worker}: {acao.Situacao} — {acao.Acao}");

            var linha = $"{acao.Situacao} · {acao.Acao}";
            var nova = !_ultima.TryGetValue(acao.Worker, out var anterior) || anterior != linha;
            _ultima[acao.Worker] = linha;

            if (!nova || acao.Acao == "ok")
            {
                continue;
            }

            var registrar = acao.Situacao is SituacaoDoWorker.Saudavel ? Normal : Anormal;
            registrar(log, acao.Worker, acao.Situacao.ToString(), acao.Acao, null);
        }
    }
}
