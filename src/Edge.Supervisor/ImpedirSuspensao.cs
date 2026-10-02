using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edge.Supervisor;

/// <summary>
/// Enquanto o serviço roda, o Windows não suspende o PC por inatividade.
/// </summary>
/// <remarks>
/// <para>
/// Num evento, ninguém mexe no mouse do PC das catracas por horas. Com o plano de energia
/// padrão, o Windows suspende o computador, e as catracas perdem o sistema no meio da fila.
/// </para>
/// <para>
/// Não muda o plano de energia do Windows: pede, com <c>SetThreadExecutionState</c>, que o
/// sistema continue acordado enquanto o serviço estiver rodando, e o pedido some quando ele
/// para. A tela pode apagar normalmente. Não impede quem mandar suspender ou desligar de
/// propósito. O pedido aparece em <c>powercfg /requests</c>.
/// </para>
/// </remarks>
internal sealed class ImpedirSuspensao(ILogger<ImpedirSuspensao> log) : BackgroundService
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        // O pedido vale enquanto a thread que o fez existir: por isso uma thread própria,
        // que dura o mesmo que o serviço, e não uma do pool.
        var pronto = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // Zero é falha: não derruba nada, mas fica no registro para o suporte.
            if (SetThreadExecutionState(EsContinuous | EsSystemRequired) == 0)
            {
                Recusado(log, null);
            }

            stoppingToken.WaitHandle.WaitOne();
            _ = SetThreadExecutionState(EsContinuous);
            pronto.TrySetResult();
        })
        {
            IsBackground = true,
            Name = "Impedir suspensão",
        };
        thread.Start();
        return pronto.Task;
    }

    private static readonly Action<ILogger, Exception?> Recusado = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, "SuspensaoNaoImpedida"),
        "O Windows recusou o pedido para não suspender o computador durante a operação.");

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint SetThreadExecutionState(uint estado);
}
