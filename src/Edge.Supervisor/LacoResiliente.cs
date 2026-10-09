using Microsoft.Extensions.Logging;

namespace Edge.Supervisor;

/// <summary>
/// Roda o corpo de um serviço em segundo plano sem deixar exceção escapar.
/// </summary>
/// <remarks>
/// <para>
/// Achado E2-02 do docs/41: uma exceção que escapa de um <c>BackgroundService</c> parava o host
/// inteiro (padrão do .NET), o que encerrava todos os workers. O processo saía com código 0, e o
/// Windows não aplicava a recuperação do serviço: as catracas ficavam paradas até alguém intervir.
/// </para>
/// <para>
/// Aqui a falha vai para o registro e o corpo recomeça depois de uma espera. Nenhum desses laços
/// (cópia, supervisão, painel ao vivo, retenção) é necessário para a catraca decidir. Parar o serviço
/// por causa de um deles é pior do que o laço ficar alguns segundos sem rodar.
/// </para>
/// </remarks>
public static class LacoResiliente
{
    /// <summary>Espera padrão antes de recomeçar um laço que falhou.</summary>
    public static readonly TimeSpan EsperaPadrao = TimeSpan.FromSeconds(30);

    private static readonly Action<ILogger, string, string, double, Exception?> Falhou =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Error,
            new EventId(100, "LacoFalhou"),
            "{Laco} falhou ({Motivo}) e recomeça em {Segundos} s.");

    /// <summary>
    /// Roda <paramref name="corpo"/> até ele terminar normalmente ou até o pedido de parada.
    /// Uma exceção é registrada e o corpo é chamado de novo depois de <paramref name="espera"/>.
    /// </summary>
    public static async Task RodarAsync(
        string laco,
        Func<CancellationToken, Task> corpo,
        ILogger log,
        CancellationToken parar,
        TimeSpan? espera = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laco);
        ArgumentNullException.ThrowIfNull(corpo);
        ArgumentNullException.ThrowIfNull(log);

        var esperar = espera ?? EsperaPadrao;

        while (!parar.IsCancellationRequested)
        {
            try
            {
                await corpo(parar).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (parar.IsCancellationRequested)
            {
                return;
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                Falhou(log, laco, $"{erro.GetType().Name}: {erro.Message}", esperar.TotalSeconds, erro);
            }

            try
            {
                await Task.Delay(esperar, parar).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
