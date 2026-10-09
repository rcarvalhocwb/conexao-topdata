using System.Reflection;
using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>
/// ADR-0026: login próprio. O sistema instala com um administrador padrão que só pode trocar a senha;
/// depois, cada RPC exige a permissão do papel de quem chama, e é o serviço que confere.
/// </summary>
public sealed class LoginEUsuariosTests : IAsyncLifetime, IDisposable
{
    private const int IteracoesDeTeste = 100_000;
    private static readonly DateTimeOffset Agora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private UsuariosDoSistema _usuarios = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _usuarios = new UsuariosDoSistema(_banco.Fabrica, IteracoesDeTeste);
        _usuarios.GarantirAdministradorPadrao(Agora);

        var sessoes = new SessoesDoPainel();
        var servico = new EdgeControlService(new WorkerSupervisor([]), semConfiguracao: true, usuarios: _usuarios, sessoes: sessoes);

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"login-{Guid.NewGuid():N}");
        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(servico);
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

    public void Dispose() => _banco.Dispose();

    private (EdgeControl.EdgeControlClient Cliente, SessaoDoPainel Sessao) Painel()
    {
        var sessao = new SessaoDoPainel();
        return (new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao)), sessao);
    }

    private static async Task<StatusCode> Codigo(Func<Task> chamada)
    {
        try
        {
            await chamada();
            return StatusCode.OK;
        }
        catch (RpcException erro)
        {
            return erro.StatusCode;
        }
    }

    // ------------------------------------------------------------------ pelo serviço, de ponta a ponta

    [Fact]
    public async Task Primeiro_acesso_so_troca_a_senha_e_depois_o_administrador_real_opera_e_cria_usuarios()
    {
        var (painel, sessao) = Painel();

        // Sem sessão: só o estado geral (bandeja) e entrar.
        await painel.ObterEstadoAsync(new ObterEstadoRequest());
        Assert.Equal(StatusCode.Unauthenticated, await Codigo(() => painel.ListarEquipamentosAsync(new ListarEquipamentosRequest()).ResponseAsync));

        var entrada = await painel.EntrarAsync(new EntrarRequest { Login = UsuariosDoSistema.LoginPadrao, Senha = UsuariosDoSistema.SenhaPadrao });
        Assert.True(entrada.Aceito, entrada.Mensagem);
        Assert.True(entrada.Usuario.TrocarSenha);
        sessao.Token = entrada.Sessao;

        // Com a senha padrão, nada além da troca.
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => painel.ListarEquipamentosAsync(new ListarEquipamentosRequest()).ResponseAsync));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => painel.ListarUsuariosAsync(new ListarUsuariosRequest()).ResponseAsync));

        var troca = await painel.TrocarSenhaAsync(new TrocarSenhaRequest
        {
            SenhaAtual = UsuariosDoSistema.SenhaPadrao,
            SenhaNova = "senha-do-dono-2026",
            NovoLogin = "rodrigo",
            NovoNome = "Rodrigo (administrador)",
        });
        Assert.True(troca.Trocada, string.Join(" ", troca.Problemas));

        var depois = await painel.ObterSessaoAsync(new ObterSessaoRequest());
        Assert.False(depois.TrocarSenha);
        Assert.Equal("rodrigo", depois.Login);
        Assert.Contains(Permissoes.UsuariosGerenciar, depois.Permissoes);
        await painel.ListarEquipamentosAsync(new ListarEquipamentosRequest());

        // A senha padrão não entra mais.
        var (outro, _) = Painel();
        Assert.False((await outro.EntrarAsync(new EntrarRequest { Login = UsuariosDoSistema.LoginPadrao, Senha = UsuariosDoSistema.SenhaPadrao })).Aceito);

        // O administrador cria um porteiro.
        var criado = await painel.GravarUsuarioAsync(new GravarUsuarioRequest
        {
            Login = "porteiro.ana",
            Nome = "Ana da portaria",
            Ativo = true,
            Papeis = { "portaria" },
            SenhaInicial = "provisoria-123",
        });
        Assert.True(criado.Gravado, string.Join(" ", criado.Problemas));

        // O porteiro troca a senha provisória e só faz o que o papel permite.
        var (porteiro, sessaoDoPorteiro) = Painel();
        var entradaDoPorteiro = await porteiro.EntrarAsync(new EntrarRequest { Login = "porteiro.ana", Senha = "provisoria-123" });
        Assert.True(entradaDoPorteiro.Aceito);
        Assert.True(entradaDoPorteiro.Usuario.TrocarSenha);
        sessaoDoPorteiro.Token = entradaDoPorteiro.Sessao;
        Assert.True((await porteiro.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "provisoria-123", SenhaNova = "senha-da-ana-1" })).Trocada);

        await porteiro.ListarEquipamentosAsync(new ListarEquipamentosRequest());
        await porteiro.ConsultarCodigoAsync(new ConsultarCodigoRequest { Codigo = "1234" });
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => porteiro.GravarConfiguracaoAsync(new GravarConfiguracaoRequest()).ResponseAsync));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => porteiro.ListarUsuariosAsync(new ListarUsuariosRequest()).ResponseAsync));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => porteiro.EstornarUsoAsync(new EstornarUsoRequest()).ResponseAsync));

        // Desativado, a sessão aberta dele acaba na hora.
        var desativado = await painel.GravarUsuarioAsync(new GravarUsuarioRequest
        {
            Id = criado.Id, Login = "porteiro.ana", Nome = "Ana da portaria", Ativo = false, Papeis = { "portaria" },
        });
        Assert.True(desativado.Gravado, string.Join(" ", desativado.Problemas));
        Assert.Equal(StatusCode.Unauthenticated, await Codigo(() => porteiro.ListarEquipamentosAsync(new ListarEquipamentosRequest()).ResponseAsync));

        // Sair encerra a sessão.
        await painel.SairAsync(new SairRequest());
        Assert.Equal(StatusCode.Unauthenticated, await Codigo(() => painel.ListarEquipamentosAsync(new ListarEquipamentosRequest()).ResponseAsync));
    }

    [Fact]
    public async Task Papel_criado_pelo_administrador_vale_na_hora()
    {
        var (painel, sessao) = Painel();
        sessao.Token = (await painel.EntrarAsync(new EntrarRequest { Login = "admin", Senha = "xacess" })).Sessao;
        await painel.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "xacess", SenhaNova = "outra-senha-1" });

        var papeis = await painel.ListarPapeisAsync(new ListarPapeisRequest());
        Assert.Equal(Permissoes.Todas.Count, papeis.Catalogo.Count);
        Assert.Contains(papeis.Papeis, p => p.Id == "administrador" && p.DoSistema);

        var papel = await painel.GravarPapelAsync(new GravarPapelRequest { Nome = "Relatórios", Permissoes = { Permissoes.RelatoriosVer } });
        Assert.True(papel.Gravado, string.Join(" ", papel.Problemas));
        var usuario = await painel.GravarUsuarioAsync(new GravarUsuarioRequest
        {
            Login = "contador", Nome = "Contador", Ativo = true, Papeis = { papel.Id }, SenhaInicial = "provisoria-9",
        });
        Assert.True(usuario.Gravado);

        var (contador, sessaoDoContador) = Painel();
        sessaoDoContador.Token = (await contador.EntrarAsync(new EntrarRequest { Login = "contador", Senha = "provisoria-9" })).Sessao;
        await contador.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "provisoria-9", SenhaNova = "contador-2026" });

        await contador.ObterPrestacaoDeContasAsync(new ObterPrestacaoDeContasRequest());
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => contador.ListarAcessosAsync(new ListarAcessosRequest()).ResponseAsync));

        // Tirar a permissão do papel vale na chamada seguinte, sem novo login.
        Assert.True((await painel.GravarPapelAsync(new GravarPapelRequest { Id = papel.Id, Nome = "Relatórios", Permissoes = { Permissoes.OperacaoVer } })).Gravado);
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => contador.ObterPrestacaoDeContasAsync(new ObterPrestacaoDeContasRequest()).ResponseAsync));
    }

    [Fact]
    public void Toda_rpc_do_contrato_tem_permissao_definida()
    {
        var rpcs = typeof(EdgeControl.EdgeControlBase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsVirtual)
            .Select(m => m.Name)
            .ToList();

        Assert.True(rpcs.Count > 30, $"só {rpcs.Count} RPCs");
        Assert.All(rpcs, r => Assert.True(InterceptadorDeSessao.Exigida.ContainsKey(r), $"{r} sem permissão definida"));
        Assert.All(InterceptadorDeSessao.Exigida.Values.OfType<string>(), p => Assert.True(Permissoes.Existe(p), p));
    }

    // ------------------------------------------------------------------ regras da base

    [Fact]
    public void Administrador_padrao_nasce_uma_vez_com_troca_obrigatoria()
    {
        Assert.False(_usuarios.GarantirAdministradorPadrao(Agora.AddDays(1)));
        var admin = Assert.Single(_usuarios.Listar());
        Assert.Equal("admin", admin.Login);
        Assert.True(admin.TrocarSenha);
        Assert.Equal(["administrador"], admin.Papeis);
    }

    [Fact]
    public void Cinco_erros_bloqueiam_por_cinco_minutos_mesmo_com_a_senha_certa()
    {
        for (var i = 0; i < UsuariosDoSistema.ErrosAteBloquear - 1; i++)
        {
            Assert.Equal(ResultadoDaEntrada.Recusada, _usuarios.Entrar("admin", "errada", Agora).Resultado);
        }

        Assert.Equal(ResultadoDaEntrada.Bloqueado, _usuarios.Entrar("admin", "errada", Agora).Resultado);
        Assert.Equal(ResultadoDaEntrada.Bloqueado, _usuarios.Entrar("admin", "xacess", Agora.AddMinutes(4)).Resultado);
        Assert.Equal(ResultadoDaEntrada.Entrou, _usuarios.Entrar("admin", "xacess", Agora.AddMinutes(5)).Resultado);
    }

    [Fact]
    public void Troca_exige_a_senha_atual_tamanho_minimo_e_nao_aceita_continuar_com_a_padrao()
    {
        var id = _usuarios.Listar()[0].Id;

        Assert.Contains("não confere", Assert.Single(_usuarios.TrocarSenha(id, "errada", "nova-senha-1", null, null, Agora)), StringComparison.Ordinal);
        Assert.Contains("pelo menos 8", Assert.Single(_usuarios.TrocarSenha(id, "xacess", "curta", null, null, Agora)), StringComparison.Ordinal);
        Assert.Contains(_usuarios.TrocarSenha(id, "xacess", "xacess", null, null, Agora), p => p.Contains("padrão", StringComparison.Ordinal));
        Assert.Empty(_usuarios.TrocarSenha(id, "xacess", "nova-senha-1", null, null, Agora));
        Assert.False(_usuarios.Obter(id)!.TrocarSenha);
        Assert.Contains("diferente da atual", Assert.Single(_usuarios.TrocarSenha(id, "nova-senha-1", "nova-senha-1", null, null, Agora)), StringComparison.Ordinal);
    }

    [Fact]
    public void O_ultimo_administrador_ativo_nao_pode_ser_desativado_nem_perder_o_papel()
    {
        var admin = _usuarios.Listar()[0];

        var (_, desativar) = _usuarios.Gravar(admin.Id, admin.Id, "admin", "Administrador", false, ["administrador"], null, Agora);
        var (_, semPapel) = _usuarios.Gravar(admin.Id, admin.Id, "admin", "Administrador", true, ["portaria"], null, Agora);

        Assert.Contains("último administrador", Assert.Single(desativar), StringComparison.Ordinal);
        Assert.Contains("último administrador", Assert.Single(semPapel), StringComparison.Ordinal);
    }

    [Fact]
    public void Papel_administrador_sempre_gerencia_usuarios_e_papel_do_sistema_nao_se_apaga()
    {
        var admin = _usuarios.Listar()[0].Id;
        var (_, problemas) = _usuarios.GravarPapel(admin, "administrador", "Administrador", null, [Permissoes.OperacaoVer], Agora);
        Assert.Contains("sempre gerencia usuários", Assert.Single(problemas), StringComparison.Ordinal);

        using var conexao = _banco.Fabrica.Abrir();
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            SqliteConnectionFactory.Executar(conexao, "DELETE FROM app_role WHERE id = 'portaria';"));
    }

    [Fact]
    public void A_trilha_e_a_base_nunca_guardam_a_senha()
    {
        var admin = _usuarios.Listar()[0].Id;
        _usuarios.Entrar("admin", "senha-errada-qualquer", Agora);
        _usuarios.TrocarSenha(admin, "xacess", "uma-senha-secreta", null, null, Agora);

        using var conexao = _banco.Fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText = "SELECT COALESCE(detail, '') || COALESCE(target, '') || action FROM app_user_event;";
        using var leitor = sql.ExecuteReader();
        while (leitor.Read())
        {
            var linha = leitor.GetString(0);
            Assert.DoesNotContain("senha-errada-qualquer", linha, StringComparison.Ordinal);
            Assert.DoesNotContain("uma-senha-secreta", linha, StringComparison.Ordinal);
            Assert.DoesNotContain("xacess", linha, StringComparison.Ordinal);
        }
    }
}
