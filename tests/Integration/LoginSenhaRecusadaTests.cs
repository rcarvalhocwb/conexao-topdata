using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>
/// Entrada no painel depois de uma senha recusada. A caixa de senha continua com o texto digitado; o
/// botão Entrar não pode ficar desabilitado só porque o ViewModel zerou a senha.
/// </summary>
public sealed class LoginSenhaRecusadaTests : IAsyncLifetime, IDisposable
{
    private const int IteracoesDeTeste = Access.Infrastructure.SQLite.UsuariosDoSistema.Iteracoes;
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        var usuarios = new UsuariosDoSistema(_banco.Fabrica, IteracoesDeTeste);
        usuarios.GarantirAdministradorPadrao(Agora);
        var sessoes = new SessoesDoPainel();
        var servico = new EdgeControlService(new WorkerSupervisor([]), semConfiguracao: true, usuarios: usuarios, sessoes: sessoes);

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"login-recusada-{Guid.NewGuid():N}");

        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(servico);
        construtor.Services.AddGrpc(o =>
        {
            o.Interceptors.Add<InterceptadorDeToken>(_token);
            o.Interceptors.Add<InterceptadorDeSessao>(usuarios, sessoes, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow));
        });

        _servidor = construtor.Build();
        _servidor.MapGrpcService<EdgeControlService>();
        await _servidor.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_servidor is not null)
        {
            await _servidor.StopAsync();
            await _servidor.DisposeAsync();
        }

        if (!TransporteLocal.UsaNamedPipe && File.Exists(_endereco))
        {
            File.Delete(_endereco);
        }
    }

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Login_com_a_senha_padrao_com_iteracoes_de_producao_nao_falha_no_servico()
    {
        var sessao = new SessaoDoPainel();
        var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
        var tela = new SessaoDoUsuarioViewModel(cliente, sessao);
        await tela.IniciarAsync();

        tela.Login = "admin";
        tela.Senha = "xacess";
        await tela.Entrar.ExecutarAsync();

        Assert.True(tela.PedindoTroca, tela.Mensagem);
    }

    [Fact]
    public async Task Senha_recusada_mantem_o_botao_entrar_disponivel_para_nova_tentativa()
    {
        var sessao = new SessaoDoPainel();
        var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
        var tela = new SessaoDoUsuarioViewModel(cliente, sessao);
        await tela.IniciarAsync();

        tela.Login = "admin";
        tela.Senha = "senha-errada";
        await tela.Entrar.ExecutarAsync();

        Assert.False(tela.Logado);
        Assert.True(tela.Entrar.CanExecute(null),
            "a caixa de senha ainda mostra o texto digitado: o botão Entrar precisa continuar disponível");
    }
}
