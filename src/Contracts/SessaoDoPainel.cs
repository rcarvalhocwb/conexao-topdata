using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Contracts;

/// <summary>
/// A sessão do usuário logado no painel (ADR-0026): guarda o token devolvido por <c>Entrar</c> e o põe
/// no cabeçalho <see cref="TransporteLocal.CabecalhoDaSessao"/> de toda chamada.
/// </summary>
public sealed class SessaoDoPainel : Interceptor
{
    private volatile string? _token;

    /// <summary>O token da sessão; nulo antes de entrar ou depois de sair.</summary>
    public string? Token
    {
        get => _token;
        set => _token = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, ComSessao(context));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, ComSessao(context));
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        return continuation(request, ComSessao(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> ComSessao<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> contexto)
        where TRequest : class
        where TResponse : class
    {
        if (_token is not { } token)
        {
            return contexto;
        }

        var cabecalhos = contexto.Options.Headers ?? [];
        cabecalhos.Add(TransporteLocal.CabecalhoDaSessao, token);
        return new ClientInterceptorContext<TRequest, TResponse>(contexto.Method, contexto.Host, contexto.Options.WithHeaders(cabecalhos));
    }
}
