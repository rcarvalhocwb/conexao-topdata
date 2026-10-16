using System.Security.Cryptography;
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
/// Recuperação da senha de administrador pelo suporte Rayzer (docs/58), pelo serviço e pela tela de
/// login, sem sessão: pedido, resposta assinada pela chave do suporte, senha nova escolhida pela pessoa,
/// trilha. Sem a chave certa, nada muda. As chaves são geradas no teste.
/// </summary>
public sealed class RecuperacaoPeloSuporteServicoTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly ECDsa _chaveDoSuporte = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private UsuariosDoSistema _usuarios = null!;
    private EdgeControlService _servico = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _usuarios = new UsuariosDoSistema(_banco.Fabrica, 100_000);
        _usuarios.GarantirAdministradorPadrao(Agora);

        var sessoes = new SessoesDoPainel();
        _servico = new EdgeControlService(
            new WorkerSupervisor([]),
            relogio: () => Agora,
            configuracoes: new ConfiguracoesDaBorda(_banco.Fabrica),
            semConfiguracao: true,
            usuarios: _usuarios,
            sessoes: sessoes)
        {
            ChavesPublicasDoSuporte = [Convert.ToBase64String(_chaveDoSuporte.ExportSubjectPublicKeyInfo())],
        };

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"recuperacao-{Guid.NewGuid():N}");
        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(_servico);
        construtor.Services.AddGrpc(o =>
        {
            o.Interceptors.Add<InterceptadorDeToken>(_token);
            o.Interceptors.Add<InterceptadorDeSessao>(_usuarios, sessoes, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow));
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

    public void Dispose()
    {
        _chaveDoSuporte.Dispose();
        _banco.Dispose();
    }

    private string Assinar(string pedido) =>
        RecuperacaoPeloSuporte.ParaTexto(RecuperacaoPeloSuporte.Assinar(
            RecuperacaoPeloSuporte.DoTexto(pedido, RecuperacaoPeloSuporte.BytesDoPedido)!, _chaveDoSuporte));

    private string Trilha()
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT group_concat(action || ':' || COALESCE(actor_id, ''), ',') FROM app_user_event;";
        return Convert.ToString(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    [Fact]
    public async Task Pela_tela_de_login_o_administrador_recupera_com_a_resposta_do_suporte_e_entra()
    {
        var sessao = new SessaoDoPainel();
        var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
        var login = new SessaoDoUsuarioViewModel(cliente, sessao) { Login = UsuariosDoSistema.LoginPadrao };

        await login.AlternarAjudaDaSenha.ExecutarAsync();
        await login.PedirRecuperacao.ExecutarAsync();
        Assert.True(login.MostrandoAjudaDaSenha);
        Assert.NotEmpty(login.PedidoDeRecuperacao);

        login.RespostaDoSuporte = Assinar(login.PedidoDeRecuperacao);
        login.SenhaRecuperada = login.ConfirmacaoRecuperada = "Nova-Senha-123";
        await login.ConfirmarRecuperacao.ExecutarAsync();

        Assert.Equal(string.Empty, login.MensagemDaRecuperacao);
        Assert.Contains("Entre com a senha nova", login.Mensagem, StringComparison.Ordinal);
        var (resultado, usuario) = _usuarios.Entrar(UsuariosDoSistema.LoginPadrao, "Nova-Senha-123", Agora);
        Assert.Equal(ResultadoDaEntrada.Entrou, resultado);
        Assert.False(usuario!.TrocarSenha);
        Assert.Contains($"senha_recuperada_pelo_suporte:{UsuariosDoSistema.AutorDoSuporte}", Trilha(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resposta_de_outra_chave_nao_muda_a_senha_e_cinco_erros_anulam_o_pedido()
    {
        var pedido = await _servico.PedirRecuperacaoPeloSuporte(new PedirRecuperacaoPeloSuporteRequest { Login = "admin" }, null!);
        using var outra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var falsa = RecuperacaoPeloSuporte.ParaTexto(RecuperacaoPeloSuporte.Assinar(
            RecuperacaoPeloSuporte.DoTexto(pedido.Pedido, RecuperacaoPeloSuporte.BytesDoPedido)!, outra));

        for (var i = 0; i < 5; i++)
        {
            var r = await _servico.ConfirmarRecuperacaoPeloSuporte(
                new ConfirmarRecuperacaoPeloSuporteRequest { Login = "admin", Resposta = falsa, SenhaNova = "Nova-Senha-123" }, null!);
            Assert.False(r.Gravada);
        }

        // Depois de cinco erros, nem a resposta certa serve: é preciso pedir de novo.
        var certa = await _servico.ConfirmarRecuperacaoPeloSuporte(
            new ConfirmarRecuperacaoPeloSuporteRequest { Login = "admin", Resposta = Assinar(pedido.Pedido), SenhaNova = "Nova-Senha-123" }, null!);
        Assert.False(certa.Gravada);
        Assert.Equal(ResultadoDaEntrada.Entrou, _usuarios.Entrar(UsuariosDoSistema.LoginPadrao, UsuariosDoSistema.SenhaPadrao, Agora).Resultado);
        Assert.DoesNotContain("senha_recuperada_pelo_suporte", Trilha(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pedido_de_um_login_nao_serve_para_outro()
    {
        var pedido = await _servico.PedirRecuperacaoPeloSuporte(new PedirRecuperacaoPeloSuporteRequest { Login = "admin" }, null!);
        await _servico.PedirRecuperacaoPeloSuporte(new PedirRecuperacaoPeloSuporteRequest { Login = "outro" }, null!);

        var r = await _servico.ConfirmarRecuperacaoPeloSuporte(
            new ConfirmarRecuperacaoPeloSuporteRequest { Login = "outro", Resposta = Assinar(pedido.Pedido), SenhaNova = "Nova-Senha-123" }, null!);

        Assert.False(r.Gravada);
    }

    [Fact]
    public async Task Sem_chave_do_suporte_na_versao_a_recuperacao_fica_desligada_e_diz_o_caminho()
    {
        var semChave = new EdgeControlService(
            new WorkerSupervisor([]),
            configuracoes: new ConfiguracoesDaBorda(_banco.Fabrica),
            semConfiguracao: true,
            usuarios: _usuarios,
            sessoes: new SessoesDoPainel())
        {
            ChavesPublicasDoSuporte = [],
        };

        var pedido = await semChave.PedirRecuperacaoPeloSuporte(new PedirRecuperacaoPeloSuporteRequest { Login = "admin" }, null!);

        Assert.False(pedido.Disponivel);
        Assert.Contains("outro administrador", pedido.Motivo, StringComparison.Ordinal);
    }
}
