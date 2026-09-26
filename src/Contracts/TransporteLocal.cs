using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Contracts;

/// <summary>
/// Transporte do IPC local. Nunca abre porta TCP.
/// </summary>
/// <remarks>
/// <para>
/// Em Windows usa <b>named pipe</b>, que é o transporte de produção: a ACL do pipe
/// restringe quem conecta, e nenhuma porta fica exposta — nem para a rede, nem para
/// outro processo qualquer da máquina. A porta TCP da máquina é escassa e disputada
/// pelos próprios workers (ADR-0021); gastar uma com IPC seria desperdício e risco.
/// </para>
/// <para>
/// Fora de Windows usa <b>socket de domínio Unix</b>, com a mesma propriedade de ser
/// local e protegido por permissão de arquivo. Serve para desenvolvimento e para a CI
/// Linux, onde o contrato é exercitado de ponta a ponta.
/// </para>
/// <para>Ver docs/ADR/ADR-0004-ipc-grpc-named-pipes.md</para>
/// </remarks>
public static class TransporteLocal
{
    /// <summary>Nome padrão do canal.</summary>
    public const string NomePadrao = "conexao-topdata-edge";

    /// <summary>Cabeçalho que carrega o token de sessão.</summary>
    public const string CabecalhoDoToken = "x-edge-token";

    /// <summary>Espera máxima para estabelecer a conexão local.</summary>
    /// <remarks>
    /// <para>
    /// Existe por um motivo concreto: <c>NamedPipeClientStream.ConnectAsync</c> sem
    /// limite espera <b>para sempre</b> o servidor aparecer. Se o serviço não estiver em
    /// execução, o painel travaria em silêncio em vez de mostrar "Sem resposta do serviço
    /// local" — justamente o caso que ele foi feito para atender.
    /// </para>
    /// <para>
    /// O socket de domínio Unix falha na hora quando o arquivo não existe, e foi por isso
    /// que a CI Linux passou verde enquanto a do Windows ficava pendurada. Os dois
    /// caminhos agora usam o mesmo limite, para que a plataforma de teste e a de produção
    /// se comportem igual.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan TempoLimiteDeConexao = TimeSpan.FromSeconds(5);

    /// <summary>Verdadeiro quando o transporte de produção (named pipe) está disponível.</summary>
    public static bool UsaNamedPipe => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Endereço do canal: nome do pipe no Windows, caminho de socket no resto.
    /// </summary>
    public static string EnderecoPadrao(string nome = NomePadrao) =>
        UsaNamedPipe ? nome : Path.Combine(Path.GetTempPath(), $"{nome}.sock");

    /// <summary>Configura o Kestrel para escutar no transporte local.</summary>
    public static void Escutar(KestrelServerOptions opcoes, string endereco)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentException.ThrowIfNullOrWhiteSpace(endereco);

        if (UsaNamedPipe)
        {
            opcoes.ListenNamedPipe(endereco, o => o.Protocols = HttpProtocols.Http2);
            return;
        }

        // Socket órfão de uma execução anterior impede o bind.
        if (File.Exists(endereco))
        {
            File.Delete(endereco);
        }

        opcoes.ListenUnixSocket(endereco, o => o.Protocols = HttpProtocols.Http2);
    }

    /// <summary>
    /// Cria um canal cliente para o transporte local.
    /// </summary>
    /// <remarks>
    /// O endereço <c>http://localhost</c> é apenas a autoridade exigida pelo HTTP/2:
    /// nenhuma conexão TCP é aberta, porque o <c>ConnectCallback</c> entrega o pipe ou
    /// o socket de domínio.
    /// </remarks>
    public static GrpcChannel CriarCanal(string endereco, string? token = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endereco);

        var manipulador = new SocketsHttpHandler
        {
            ConnectCallback = (contexto, cancelamento) => new ValueTask<Stream>(
                ConectarAsync(
                    UsaNamedPipe ? AbrirPipeAsync : AbrirSocketAsync,
                    endereco,
                    TempoLimiteDeConexao,
                    cancelamento)),

            // Cada canal fala com um serviço local; não há pool a manter quente.
            EnableMultipleHttp2Connections = false,
        };

        var opcoes = new GrpcChannelOptions { HttpHandler = manipulador };

        if (!string.IsNullOrEmpty(token))
        {
            opcoes.Credentials = Grpc.Core.ChannelCredentials.Create(
                Grpc.Core.ChannelCredentials.Insecure,
                Grpc.Core.CallCredentials.FromInterceptor((_, metadados) =>
                {
                    metadados.Add(CabecalhoDoToken, token);
                    return Task.CompletedTask;
                }));

            opcoes.UnsafeUseInsecureChannelCallCredentials = true;
        }

        return GrpcChannel.ForAddress("http://localhost", opcoes);
    }

    /// <summary>
    /// Conecta com espera limitada.
    /// </summary>
    /// <remarks>
    /// Estourar o limite vira <see cref="IOException"/>, que o gRPC traduz para
    /// <c>Unavailable</c> — ou seja, chega à tela como estado, não como travamento.
    /// O cancelamento de fora continua sendo cancelamento: fechar a janela durante uma
    /// atualização não é falha de serviço.
    /// </remarks>
    internal static async Task<Stream> ConectarAsync(
        Func<string, CancellationToken, Task<Stream>> abrir,
        string endereco,
        TimeSpan limite,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(abrir);

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        prazo.CancelAfter(limite);

        try
        {
            return await abrir(endereco, prazo.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancelamento.IsCancellationRequested)
        {
            throw new IOException(
                $"O serviço local não respondeu em {limite.TotalSeconds:F0}s ({endereco}). " +
                "Verifique se o Edge.Supervisor está em execução.");
        }
    }

    private static async Task<Stream> AbrirPipeAsync(string endereco, CancellationToken cancelamento)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            endereco,
            PipeDirection.InOut,
            PipeOptions.WriteThrough | PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(cancelamento).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<Stream> AbrirSocketAsync(string endereco, CancellationToken cancelamento)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endereco), cancelamento)
                .ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
