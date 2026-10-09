using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Contracts;

/// <summary>
/// Prazo padrão para as chamadas do painel ao serviço.
/// </summary>
/// <remarks>
/// <para>
/// Achado E6-2 do docs/41: as chamadas não tinham prazo. Com o serviço aceitando a conexão mas sem
/// responder (um handler preso, a base ocupada), a chamada nunca voltava: o painel continuava
/// "Operacional", com os números congelados, e a cada 2 s mais uma chamada se acumulava.
/// </para>
/// <para>
/// Com prazo, a chamada que estoura vira <c>DeadlineExceeded</c>, que o painel mostra como serviço sem
/// resposta. Leituras: 5 s. Comandos: 15 s. Pacote de diagnóstico (monta um zip): 60 s. Chamada que já
/// traz o próprio prazo fica com ele; o fluxo ao vivo (streaming) não tem prazo.
/// </para>
/// </remarks>
public sealed class PrazoDasChamadas : Interceptor
{
    public static readonly TimeSpan Leitura = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Comando = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan PacoteDeDiagnostico = TimeSpan.FromSeconds(60);

    private readonly Func<DateTime> _agoraUtc;

    public PrazoDasChamadas(Func<DateTime>? agoraUtc = null) => _agoraUtc = agoraUtc ?? (() => DateTime.UtcNow);

    /// <summary>O prazo de uma chamada pelo nome do método.</summary>
    public static TimeSpan PrazoPara(string metodo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metodo);

        if (metodo == "ObterPacoteDeDiagnostico")
        {
            return PacoteDeDiagnostico;
        }

        return metodo.StartsWith("Obter", StringComparison.Ordinal)
            || metodo.StartsWith("Listar", StringComparison.Ordinal)
            || metodo.StartsWith("Consultar", StringComparison.Ordinal)
            || metodo.StartsWith("Explicar", StringComparison.Ordinal)
                ? Leitura
                : Comando;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, ComPrazo(context));
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, ComPrazo(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> ComPrazo<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline is not null)
        {
            return context;
        }

        var opcoes = context.Options.WithDeadline(_agoraUtc() + PrazoPara(context.Method.Name));
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, opcoes);
    }
}
