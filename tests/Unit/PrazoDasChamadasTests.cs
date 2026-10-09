using Contracts;
using Contracts.Edge.V1;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Unit.Tests;

/// <summary>
/// Achado E6-2 do docs/41: as chamadas do painel não tinham prazo, e um serviço que aceitava a conexão
/// sem responder deixava o painel "Operacional" para sempre. Toda chamada do painel sai com prazo.
/// </summary>
public sealed class PrazoDasChamadasTests
{
    private static readonly DateTime Agora = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Invocador falso: guarda as opções de cada chamada e responde vazio.</summary>
    private sealed class InvocadorQueGuarda : CallInvoker
    {
        public List<(string Metodo, CallOptions Opcoes)> Chamadas { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Chamadas.Add((method.Name, options));
            var resposta = Activator.CreateInstance<TResponse>();
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(resposta), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Leitura_comando_e_pacote_saem_cada_um_com_o_seu_prazo()
    {
        var invocador = new InvocadorQueGuarda();
        var cliente = new EdgeControl.EdgeControlClient(invocador.Intercept(new PrazoDasChamadas(() => Agora)));

        await cliente.ObterEstadoAsync(new ObterEstadoRequest());
        await cliente.ReenviarCartasMortasAsync(new ReenviarCartasMortasRequest());
        await cliente.ObterPacoteDeDiagnosticoAsync(new ObterPacoteDeDiagnosticoRequest());

        Assert.Equal(Agora + PrazoDasChamadas.Leitura, invocador.Chamadas[0].Opcoes.Deadline);
        Assert.Equal(Agora + PrazoDasChamadas.Comando, invocador.Chamadas[1].Opcoes.Deadline);
        Assert.Equal(Agora + PrazoDasChamadas.PacoteDeDiagnostico, invocador.Chamadas[2].Opcoes.Deadline);
    }

    [Fact]
    public async Task Chamada_com_prazo_proprio_fica_com_ele()
    {
        var invocador = new InvocadorQueGuarda();
        var cliente = new EdgeControl.EdgeControlClient(invocador.Intercept(new PrazoDasChamadas(() => Agora)));
        var proprio = Agora.AddMinutes(3);

        await cliente.ObterEstadoAsync(new ObterEstadoRequest(), deadline: proprio);

        Assert.Equal(proprio, invocador.Chamadas.Single().Opcoes.Deadline);
    }

    [Theory]
    [InlineData("ObterEstado", 5)]
    [InlineData("ListarAcessos", 5)]
    [InlineData("ConsultarCodigo", 5)]
    [InlineData("GravarConfiguracao", 15)]
    [InlineData("LiberarManualmente", 15)]
    [InlineData("ObterPacoteDeDiagnostico", 60)]
    public void Prazo_pelo_nome_do_metodo(string metodo, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), PrazoDasChamadas.PrazoPara(metodo));
}
