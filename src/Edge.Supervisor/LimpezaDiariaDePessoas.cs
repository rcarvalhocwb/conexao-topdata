using Access.Infrastructure.SQLite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edge.Supervisor;

/// <summary>Limpeza diária em lotes pequenos, sem bloquear a partida ou derrubar as catracas.</summary>
internal sealed class LimpezaDiariaDePessoas(RetencaoDePessoas retencao, ILogger<LimpezaDiariaDePessoas> log,
    Func<DateTimeOffset>? relogio = null, TimeSpan? primeiraEspera = null, TimeSpan? intervalo = null) : BackgroundService
{
    private readonly Func<DateTimeOffset> _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(primeiraEspera ?? TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExecutarAsync(stoppingToken);
                await Task.Delay(intervalo ?? TimeSpan.FromDays(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task<int> ExecutarAsync(CancellationToken cancelamento = default)
    {
        var total = 0;
        try
        {
            var agora = _relogio();
            int lote;
            do
            {
                cancelamento.ThrowIfCancellationRequested();
                lote = retencao.LimparVencidos(agora);
                total += lote;
                // Libera o escritor e a thread entre lotes.
                await Task.Yield();
            } while (lote == RetencaoDePessoas.TamanhoDoLote);
            Feita(log, total, null);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested) { throw; }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Sem mensagem/exceção completa: erros de SQL podem conter dados pessoais.
            Falhou(log, total, erro.GetType().Name, null);
        }
        return total;
    }

    private static readonly Action<ILogger, int, Exception?> Feita = LoggerMessage.Define<int>(
        LogLevel.Information, new EventId(1, "RetencaoDePessoas"), "Retenção diária: {Apagadas} pessoas apagadas.");
    private static readonly Action<ILogger, int, string, Exception?> Falhou = LoggerMessage.Define<int, string>(
        LogLevel.Error, new EventId(2, "RetencaoDePessoasFalhou"), "Retenção diária interrompida após {Apagadas} exclusões ({Tipo}).");
}
