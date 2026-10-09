using Contracts;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Integration.Tests;

/// <summary>
/// IPC local de ponta a ponta: servidor, transporte e autenticação.
/// </summary>
/// <remarks>
/// Roda sobre named pipe no Windows e sobre socket de domínio Unix no resto — os dois
/// locais, nenhum abrindo porta TCP. Ver docs/ADR/ADR-0004.
/// </remarks>
public sealed class IpcLocalTests : IAsyncLifetime
{
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    /// <summary>Dublê de worker, só para dar conteúdo ao serviço.</summary>
    private sealed class WorkerDeTeste(string nome, int porta, params int[] inners) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => true;

        public string Diagnostico => "saudável";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    public async Task InitializeAsync()
    {
        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"teste-{Guid.NewGuid():N}");

        var supervisor = new WorkerSupervisor(
        [
            new WorkerDeTeste("setor-A", 3570, 1, 2),
            new WorkerDeTeste("setor-B", 3571, 3),
        ]);
        supervisor.Iniciar();

        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(opcoes => TransporteLocal.Escutar(opcoes, _endereco));
        construtor.Services.AddSingleton(supervisor);
        construtor.Services.AddSingleton<EdgeControlService>();
        construtor.Services.AddGrpc(o => o.Interceptors.Add<InterceptadorDeToken>(_token));

        _servidor = construtor.Build();
        _servidor.MapGrpcService<EdgeControlService>();

        await _servidor.StartAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        if (_servidor is not null)
        {
            await _servidor.StopAsync().ConfigureAwait(true);
            await _servidor.DisposeAsync().ConfigureAwait(true);
        }

        if (!TransporteLocal.UsaNamedPipe && File.Exists(_endereco))
        {
            File.Delete(_endereco);
        }
    }

    [Fact]
    public async Task Aplicativo_autorizado_obtem_o_estado()
    {
        using var canal = TransporteLocal.CriarCanal(_endereco, _token);
        var cliente = new EdgeControl.EdgeControlClient(canal);

        var estado = await cliente.ObterEstadoAsync(new ObterEstadoRequest()).ConfigureAwait(true);

        Assert.Equal(2, estado.WorkersAtivos);
        Assert.Equal(3, estado.EquipamentosConectados);
        Assert.False(string.IsNullOrWhiteSpace(estado.Versao));
    }

    /// <summary>
    /// Sem internet é o regime normal de um evento. O painel precisa mostrar isso como
    /// estado, não como falha.
    /// </summary>
    [Fact]
    public async Task Estado_reporta_operacao_sem_internet_como_nivel_T1()
    {
        using var canal = TransporteLocal.CriarCanal(_endereco, _token);
        var cliente = new EdgeControl.EdgeControlClient(canal);

        var estado = await cliente.ObterEstadoAsync(new ObterEstadoRequest()).ConfigureAwait(true);

        Assert.Equal(NivelDeDegradacao.T1SemInternet, estado.Nivel);
        Assert.False(estado.InternetDisponivel);
    }

    /// <summary>
    /// O pipe já restringe por identidade de usuário, mas isso não separa processos.
    /// O token é o que impede outro programa da mesma conta de comandar catracas.
    /// </summary>
    [Fact]
    public async Task Chamada_sem_token_e_recusada()
    {
        using var canal = TransporteLocal.CriarCanal(_endereco, token: null);
        var cliente = new EdgeControl.EdgeControlClient(canal);

        var erro = await Assert.ThrowsAsync<RpcException>(
            async () => await cliente.ObterEstadoAsync(new ObterEstadoRequest()).ConfigureAwait(true))
            .ConfigureAwait(true);

        Assert.Equal(StatusCode.Unauthenticated, erro.StatusCode);
    }

    [Fact]
    public async Task Chamada_com_token_errado_e_recusada()
    {
        using var canal = TransporteLocal.CriarCanal(_endereco, InterceptadorDeToken.GerarToken());
        var cliente = new EdgeControl.EdgeControlClient(canal);

        var erro = await Assert.ThrowsAsync<RpcException>(
            async () => await cliente.ObterEstadoAsync(new ObterEstadoRequest()).ConfigureAwait(true))
            .ConfigureAwait(true);

        Assert.Equal(StatusCode.Unauthenticated, erro.StatusCode);
    }

    [Fact]
    public async Task Lista_os_equipamentos_com_a_porta_de_cada_worker()
    {
        using var canal = TransporteLocal.CriarCanal(_endereco, _token);
        var cliente = new EdgeControl.EdgeControlClient(canal);

        var lista = await cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest()).ConfigureAwait(true);

        Assert.Equal(3, lista.Equipamentos.Count);
        Assert.Equal(3570, lista.Equipamentos.Single(e => e.Inner == 1).Porta);
        Assert.Equal(3571, lista.Equipamentos.Single(e => e.Inner == 3).Porta);
    }

    /// <summary>A interface recebe eventos por fluxo; nunca faz polling.</summary>
    [Fact]
    public async Task Acompanha_eventos_ao_vivo()
    {
        using var canal = TransporteLocal.CriarCanal(_endereco, _token);
        var cliente = new EdgeControl.EdgeControlClient(canal);

        using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var fluxo = cliente.AcompanharEventos(new AcompanharEventosRequest(), cancellationToken: cancelamento.Token);

        var servico = _servidor!.Services.GetRequiredService<EdgeControlService>();
        await servico.Eventos.Writer.WriteAsync(
            new EventoDeAcesso
            {
                EventoId = "evt-1",
                Inner = 1,
                OrigemBruta = 2,
                OrigemConhecida = "Leitor1",
                CredencialMascarada = "cred:****67(10)",
                Resultado = ResultadoDoAcesso.Permitido,
                Motivo = "AUTORIZADO",
                MensagemAoOperador = "Acesso liberado",
                PassagemConfirmada = false,
                CorrelationId = "corr-1",
            },
            cancelamento.Token).ConfigureAwait(true);

        Assert.True(await fluxo.ResponseStream.MoveNext(cancelamento.Token).ConfigureAwait(true));

        var recebido = fluxo.ResponseStream.Current;
        Assert.Equal("evt-1", recebido.EventoId);
        Assert.Equal("cred:****67(10)", recebido.CredencialMascarada);

        // Autorizado não é o mesmo que passou. Ver ADR-0007.
        Assert.False(recebido.PassagemConfirmada);
    }

    /// <summary>
    /// O transporte é local por construção: no Unix é um arquivo de socket; no Windows,
    /// um named pipe. Nenhum dos dois é endereço de rede.
    /// </summary>
    [Fact]
    public void O_transporte_nao_abre_porta_tcp()
    {
        if (TransporteLocal.UsaNamedPipe)
        {
            Assert.DoesNotContain(":", _endereco, StringComparison.Ordinal);
            return;
        }

        Assert.True(File.Exists(_endereco), $"socket de domínio Unix não encontrado: {_endereco}");
        Assert.EndsWith(".sock", _endereco, StringComparison.Ordinal);
    }
}
