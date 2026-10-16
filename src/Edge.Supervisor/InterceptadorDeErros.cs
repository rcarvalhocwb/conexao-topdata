using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Edge.Supervisor;

/// <summary>
/// Registra no arquivo do serviço a exceção que vira "erro interno" no painel. Sem isso, a tela só mostra
/// "O serviço recusou o pedido (Internal)" e o motivo fica perdido.
/// </summary>
/// <remarks>
/// Grava o tipo da exceção, a mensagem e a primeira linha da pilha, e o nome do método chamado. Nunca grava
/// o conteúdo da requisição: a senha de login e o código de um cartão não podem entrar no registro.
/// </remarks>
public sealed class InterceptadorDeErros(Action<string> registrar) : Interceptor
{
    /// <summary>A linha que vai ao registro para uma exceção, sem dados da requisição.</summary>
    public static string Descrever(string metodo, Exception erro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metodo);
        ArgumentNullException.ThrowIfNull(erro);
        var primeiraLinha = erro.StackTrace?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "sem pilha";
        return $"erro interno em {metodo}: {erro.GetType().FullName}: {erro.Message} ({primeiraLinha})";
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (RpcException)
        {
            // Recusa esperada (permissão, sessão, validação): já tem a sua mensagem, não é falha.
            throw;
        }
        catch (Exception erro)
        {
            registrar(Descrever(context.Method, erro));
            throw;
        }
    }
}
