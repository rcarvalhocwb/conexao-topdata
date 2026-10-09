using Access.Infrastructure.SQLite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edge.Supervisor;

/// <summary>
/// Faz cópia de segurança de <c>acesso.db</c> ao iniciar e depois a cada seis horas, e mantém as
/// 14 mais recentes.
/// </summary>
/// <remarks>
/// Falha de cópia não derruba o serviço: a catraca e o operador não dependem dela para atender.
/// Mas a falha vai para o registro com o motivo, porque uma cópia que falha em silêncio é o que se
/// descobre quando é preciso restaurar.
/// </remarks>
internal sealed class AgendadorDeCopias(
    CopiaDeSeguranca copia,
    ILogger<AgendadorDeCopias> log,
    TimeSpan? primeiraEspera = null,
    TimeSpan? intervalo = null) : BackgroundService
{
    public const int CopiasMantidas = 14;

    private readonly TimeSpan _primeiraEspera = primeiraEspera ?? TimeSpan.FromMinutes(1);
    private readonly TimeSpan _intervalo = intervalo ?? TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_primeiraEspera, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            Executar();

            try
            {
                await Task.Delay(_intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Uma rodada: cria, confere e limpa. Nunca lança.</summary>
    internal void Executar()
    {
        try
        {
            var caminho = copia.Criar(DateTimeOffset.Now);
            var apagadas = copia.Limpar(CopiasMantidas);
            Log.CopiaFeita(log, Path.GetFileName(caminho), apagadas, null);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Qualquer falha, inclusive SqliteException (disco cheio, base ocupada além do busy_timeout,
            // cópia ilegível na conferência): antes só três tipos eram capturados, e uma SqliteException
            // parava o serviço inteiro (achado E2-02 do docs/41).
            Log.CopiaFalhou(log, erro.Message, erro);
        }
    }

    private static class Log
    {
        private static readonly Action<ILogger, string, int, Exception?> Feita = LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1, "CopiaDeSegurancaFeita"),
            "Cópia de segurança feita e verificada: {Arquivo}. Cópias antigas apagadas: {Apagadas}.");

        private static readonly Action<ILogger, string, Exception?> Falhou = LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, "CopiaDeSegurancaFalhou"),
            "A cópia de segurança falhou: {Motivo}");

        public static void CopiaFeita(ILogger log, string arquivo, int apagadas, Exception? erro) =>
            Feita(log, arquivo, apagadas, erro);

        public static void CopiaFalhou(ILogger log, string motivo, Exception erro) =>
            Falhou(log, motivo, erro);
    }
}
