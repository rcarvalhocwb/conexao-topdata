using System.Globalization;
using Microsoft.Extensions.Logging;
using Shared.Observability;

namespace Edge.Supervisor;

/// <summary>
/// Leva os avisos e erros do <see cref="ILogger"/> para o registro do serviço em arquivo
/// (<c>registros\servico-AAAA-MM-DD.log</c>), o mesmo que o suporte abre pelos runbooks.
/// </summary>
/// <remarks>
/// Achados E2-05 e E10-6 do docs/41: rodando como serviço não há console, e as falhas dos laços em
/// segundo plano (cópia, retenção, supervisão) iam só para o console e o Visualizador de Eventos.
/// Só Warning ou acima, e com o redator aplicado, para o arquivo não virar ruído nem guardar dado
/// sensível.
/// </remarks>
public sealed class ProvedorDeRegistroEmArquivo(Action<string> escrever, LogLevel minimo = LogLevel.Warning)
    : ILoggerProvider
{
    private readonly Action<string> _escrever = escrever ?? throw new ArgumentNullException(nameof(escrever));
    private readonly LogLevel _minimo = minimo;

    public ILogger CreateLogger(string categoryName) => new Registrador(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Registrador(ProvedorDeRegistroEmArquivo provedor, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provedor._minimo;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            var origem = categoria[(categoria.LastIndexOf('.') + 1)..];
            var texto = formatter(state, exception);
            if (exception is not null)
            {
                texto += string.Create(CultureInfo.InvariantCulture, $" [{exception.GetType().Name}: {exception.Message}]");
            }

            try
            {
                provedor._escrever(RedatorDeDadoSensivel.Redigir(
                    string.Create(CultureInfo.InvariantCulture, $"{Nivel(logLevel)} {origem}: {texto}")));
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
            {
                // O registro em arquivo é auxiliar: disco cheio ou sem permissão não pode derrubar quem registrou.
            }
        }

        private static string Nivel(LogLevel nivel) => nivel switch
        {
            LogLevel.Critical => "CRÍTICO",
            LogLevel.Error => "ERRO",
            LogLevel.Warning => "AVISO",
            _ => nivel.ToString(),
        };
    }
}
