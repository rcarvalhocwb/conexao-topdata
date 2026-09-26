using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Contracts;

/// <summary>
/// Recusa chamadas sem o token de sessão.
/// </summary>
/// <remarks>
/// <para>
/// O named pipe já restringe quem conecta pela ACL, mas isso protege por identidade de
/// usuário, não por processo: qualquer coisa rodando com a mesma conta alcançaria o
/// serviço. O token é a segunda barreira, e é ela que impede outro programa na mesma
/// máquina de comandar catracas.
/// Ver docs/03-arquitetura.md, seção 11.
/// </para>
/// <para>
/// A comparação é de tempo fixo. Comparar segredo com <c>==</c> vaza, pelo tempo de
/// resposta, quantos caracteres iniciais estavam certos.
/// </para>
/// </remarks>
public sealed class InterceptadorDeToken : Interceptor
{
    private readonly byte[] _esperado;

    public InterceptadorDeToken(string tokenEsperado)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenEsperado);
        _esperado = Encoding.UTF8.GetBytes(tokenEsperado);
    }

    // Os nomes de parâmetro seguem a classe base do gRPC, em inglês: um override que
    // renomeia parâmetros quebra a chamada por nome de quem usar a biblioteca.
    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        Conferir(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        Conferir(context);
        return continuation(request, responseStream, context);
    }

    /// <summary>Gera um token de sessão com entropia adequada.</summary>
    public static string GerarToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private void Conferir(ServerCallContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var recebido = contexto.RequestHeaders.GetValue(TransporteLocal.CabecalhoDoToken);

        if (string.IsNullOrEmpty(recebido))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Chamada sem token de sessão. O serviço local só atende o aplicativo autorizado."));
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), _esperado))
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "Token de sessão inválido."));
        }
    }
}
