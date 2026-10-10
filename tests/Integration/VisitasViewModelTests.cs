using Contracts.Edge.V1;
using Desktop.ViewModels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Integration.Tests;

public sealed class VisitasViewModelTests
{
    [Fact]
    public async Task Resposta_de_outra_sessao_nao_restaura_dados_na_agenda()
    {
        var invocador = new RespostaPendente();
        var tela = new VisitasViewModel(new EdgeControl.EdgeControlClient(invocador), () => VisitasTests.Agora);
        var carregando = tela.AtualizarAsync();
        tela.Nome = "Nome digitado pela sessão anterior";
        tela.Documento = "RG-FICTICIO-001";
        tela.Codigo = "77001234";
        // Outra sessão, inclusive com as mesmas permissões, não herda dados nem respostas da anterior.
        tela.DefinirPermissoes(true, true, true, true);
        invocador.Lista.SetResult(new ListarVisitasResponse
        {
            Visitas = { new VisitaDoCadastro
            {
                Id = "anterior", Nome = "Rafaela Souza", Anfitriao = "Beatriz Lima", Situacao = "agendada",
                De = Timestamp.FromDateTimeOffset(VisitasTests.Agora), Ate = Timestamp.FromDateTimeOffset(VisitasTests.Agora.AddHours(1)),
            } },
        });
        await carregando;
        Assert.Empty(tela.Visitas); Assert.Empty(tela.Anfitrioes); Assert.Null(tela.Selecionada);
        Assert.Equal(string.Empty, tela.Nome); Assert.Equal(string.Empty, tela.Documento); Assert.Equal(string.Empty, tela.Codigo);
        tela.DefinirPermissoes(false, false, false, false);
        Assert.False(tela.Atualizar.CanExecute(null)); Assert.False(tela.Agendar.CanExecute(null));
        Assert.False(tela.Receber.CanExecute(null)); Assert.False(tela.Encerrar.CanExecute(null));
    }

    private sealed class RespostaPendente : CallInvoker
    {
        public TaskCompletionSource<ListarVisitasResponse> Lista { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
            string? host, CallOptions options, TRequest request)
        {
            var resposta = method.Name == "ListarVisitas"
                ? Converter<TResponse>()
                : Task.FromResult((TResponse)(object)new BuscarAnfitrioesDeVisitaResponse());
            return new AsyncUnaryCall<TResponse>(resposta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        private async Task<TResponse> Converter<TResponse>() => (TResponse)(object)await Lista.Task;

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
