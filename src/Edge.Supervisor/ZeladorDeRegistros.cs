using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edge.Supervisor;

/// <summary>
/// Mantém a pasta de registros dentro da retenção: apaga o que tem mais de 30 dias e, se ainda
/// passar de 200 MB, apaga do mais antigo para o mais novo.
/// </summary>
/// <remarks>
/// Nunca apaga o que foi escrito nas últimas 24 horas: o arquivo do dia pode estar em uso, e é o
/// registro de quem está investigando um problema agora. Os números (30 dias, 200 MB) são proposta
/// aprovada pelo responsável do projeto (pergunta A06), não um valor vindo de documento técnico.
/// </remarks>
public static class ZeladorDeRegistros
{
    public const int DiasMantidos = 30;
    public const long TetoEmBytes = 200L * 1024 * 1024;

    private static readonly TimeSpan Protegido = TimeSpan.FromHours(24);

    /// <summary>Aplica a retenção. Devolve quantos arquivos foram apagados.</summary>
    public static int Limpar(string pasta, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);

        if (!Directory.Exists(pasta))
        {
            return 0;
        }

        var limiteDoTempo = agora.UtcDateTime.AddDays(-DiasMantidos);
        var protegidoAte = agora.UtcDateTime - Protegido;
        var apagados = 0;

        var arquivos = Directory.EnumerateFiles(pasta, "*.log")
            .Select(caminho => new FileInfo(caminho))
            .OrderBy(a => a.LastWriteTimeUtc)
            .ToList();

        foreach (var arquivo in arquivos.Where(a => a.LastWriteTimeUtc < limiteDoTempo && a.LastWriteTimeUtc < protegidoAte))
        {
            arquivo.Delete();
            apagados++;
        }

        var restantes = arquivos.Where(a => a.Exists).ToList();
        var total = restantes.Sum(a => a.Length);

        foreach (var arquivo in restantes)
        {
            if (total <= TetoEmBytes)
            {
                break;
            }

            if (arquivo.LastWriteTimeUtc >= protegidoAte)
            {
                // Chegou nos arquivos recentes: o teto não justifica apagar o que está em uso.
                break;
            }

            total -= arquivo.Length;
            arquivo.Delete();
            apagados++;
        }

        return apagados;
    }
}

/// <summary>Roda a retenção ao iniciar e depois a cada seis horas.</summary>
internal sealed class ZeladorDeRegistrosServico(
    string pasta,
    ILogger<ZeladorDeRegistrosServico> log,
    TimeSpan? primeiraEspera = null,
    TimeSpan? intervalo = null) : BackgroundService
{
    private readonly TimeSpan _primeiraEspera = primeiraEspera ?? TimeSpan.FromMinutes(2);
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
            try
            {
                var apagados = ZeladorDeRegistros.Limpar(pasta, DateTimeOffset.Now);
                if (apagados > 0)
                {
                    Log.Apagados(log, apagados, null);
                }
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                // Falha de limpeza não derruba o serviço; o próximo ciclo tenta de novo.
                Log.Falhou(log, erro.Message, erro);
            }

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

    private static class Log
    {
        public static readonly Action<ILogger, int, Exception?> Apagados = LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, "RetencaoDeRegistros"),
            "Retenção de registros: {Apagados} arquivo(s) antigo(s) apagado(s).");

        private static readonly Action<ILogger, string, Exception?> FalhouDefinido = LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, "RetencaoDeRegistrosFalhou"),
            "Não foi possível aplicar a retenção de registros: {Motivo}");

        public static void Falhou(ILogger log, string motivo, Exception erro) => FalhouDefinido(log, motivo, erro);
    }
}
