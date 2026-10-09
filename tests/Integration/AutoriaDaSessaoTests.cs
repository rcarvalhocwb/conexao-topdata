using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>Issue #7: a autoria vem da sessão conferida pelo serviço, nunca do campo legado do pedido.</summary>
public sealed class AutoriaDaSessaoTests : IAsyncLifetime, IDisposable
{
    private const string Qr = "AUTORIA-SINTETICA-1";
    private const string Motivo = "Pessoa não passou, conferido na portaria";
    private readonly DateTimeOffset _agora = DateTimeOffset.UtcNow;
    private readonly BancoTemporario _banco = new();
    private RepositorioDeIngressos _repositorio = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    private sealed class WorkerFalso : IWorkerHost
    {
        public string Nome => "setor-sintetico";
        public int Porta => 3570;
        public IReadOnlyList<int> Inners { get; } = [1, 2];
        public bool EstaVivo { get; private set; }
        public bool EstaSaudavel => true;
        public string Diagnostico => "ok";
        public void Iniciar() => EstaVivo = true;
        public void Matar() => EstaVivo = false;
        public void Dispose() => EstaVivo = false;
    }

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("sintetico", "Teste", "raw", ""), _agora);
        _repositorio.Ingerir([new IngressoRecebido("sintetico", "T1", Qr, Qr)], _agora);

        var usuarios = new UsuariosDoSistema(_banco.Fabrica, 100_000);
        usuarios.GarantirAdministradorPadrao(_agora);
        var sessoes = new SessoesDoPainel();
        var supervisor = new WorkerSupervisor([new WorkerFalso()]);
        supervisor.Iniciar();
        var servico = new EdgeControlService(supervisor, relogio: () => _agora,
            comandos: new FilaDeComandosSqlite(_banco.Fabrica),
            estornos: new EstornosDeUso(_banco.Fabrica), usuarios: usuarios, sessoes: sessoes);

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"autoria-{Guid.NewGuid():N}");
        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(servico);
        construtor.Services.AddGrpc(o =>
        {
            o.Interceptors.Add<InterceptadorDeToken>(_token);
            o.Interceptors.Add<InterceptadorDeSessao>(usuarios, sessoes, (Func<DateTimeOffset>)(() => _agora));
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

    private async Task<EdgeControl.EdgeControlClient> EntrarAsync(string nome = "Ana Sintética", string login = "ana.teste")
    {
        var (cliente, sessao) = Painel();
        var entrada = await cliente.EntrarAsync(new EntrarRequest { Login = UsuariosDoSistema.LoginPadrao, Senha = UsuariosDoSistema.SenhaPadrao });
        Assert.True(entrada.Aceito, entrada.Mensagem);
        sessao.Token = entrada.Sessao;
        var troca = await cliente.TrocarSenhaAsync(new TrocarSenhaRequest
        {
            SenhaAtual = UsuariosDoSistema.SenhaPadrao, SenhaNova = "senha-sintetica-123",
            NovoNome = nome, NovoLogin = login,
        });
        Assert.True(troca.Trocada, string.Join(" ", troca.Problemas));
        return cliente;
    }

    private Guid ConsumirSemGiro()
    {
        var (uso, tentativa) = _repositorio.TentarUsar(Qr, "portao-sintetico", "inner-1", _agora.AddMinutes(-3));
        Assert.True(uso.Liberou);
        return tentativa;
    }

    private string QuemEstornou()
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText = "SELECT refunded_by FROM ticket_use_refund;";
        return (string)sql.ExecuteScalar()!;
    }

    [Theory]
    [InlineData(TipoDeComando.AcertarRelogio)]
    [InlineData(TipoDeComando.MensagemTemporaria)]
    [InlineData(TipoDeComando.LiberacaoManual)]
    [InlineData(TipoDeComando.ReiniciarConexao)]
    [InlineData(TipoDeComando.AplicarConfiguracao)]
    public async Task Comando_ignora_autoria_digitada_e_registra_a_sessao(TipoDeComando tipo)
    {
        var cliente = await EntrarAsync();
        var resposta = await cliente.EnviarComandoAsync(new EnviarComandoRequest
        {
            Inner = tipo is TipoDeComando.AplicarConfiguracao ? 0 : 1,
            Tipo = tipo, Operador = "Outra pessoa (outro.login)", Texto = "Mensagem sintética", Motivo = Motivo,
        });
        Assert.True(resposta.Aceito, string.Join(" ", resposta.Problemas));
        var historico = await cliente.ListarComandosAsync(new ListarComandosRequest());
        Assert.Equal(tipo is TipoDeComando.AplicarConfiguracao ? 2 : 1, historico.Comandos.Count);
        Assert.All(historico.Comandos, c => Assert.Equal("Ana Sintética (ana.teste)", c.Operador));
    }

    [Fact]
    public async Task Estorno_ignora_autoria_digitada_e_registra_a_sessao()
    {
        var cliente = await EntrarAsync();
        var tentativa = ConsumirSemGiro();
        var resposta = await cliente.EstornarUsoAsync(new EstornarUsoRequest
        {
            TentativaId = tentativa.ToString(), Operador = "Outra pessoa (outro.login)", Motivo = Motivo,
        });
        Assert.True(resposta.Estornado, string.Join(" ", resposta.Problemas));
        Assert.Equal("Ana Sintética (ana.teste)", QuemEstornou());
    }

    [Fact]
    public async Task Nome_e_login_no_tamanho_maximo_ficam_inteiros_no_comando_e_no_estorno()
    {
        var nome = new string('N', 80);
        var login = new string('u', 40);
        var autoria = $"{nome} ({login})";
        var cliente = await EntrarAsync(nome, login);
        var comando = await cliente.EnviarComandoAsync(new EnviarComandoRequest { Inner = 1, Tipo = TipoDeComando.AcertarRelogio });
        Assert.True(comando.Aceito, string.Join(" ", comando.Problemas));
        Assert.Equal(autoria, Assert.Single((await cliente.ListarComandosAsync(new ListarComandosRequest())).Comandos).Operador);
        var estorno = await cliente.EstornarUsoAsync(new EstornarUsoRequest { TentativaId = ConsumirSemGiro().ToString(), Motivo = Motivo });
        Assert.True(estorno.Estornado, string.Join(" ", estorno.Problemas));
        Assert.Equal(autoria, QuemEstornou());
    }

    [Fact]
    public async Task Telas_de_comando_e_estorno_operam_com_a_sessao_sem_pedir_nome()
    {
        var cliente = await EntrarAsync();
        var comandos = new GerenciarCatracaViewModel(cliente, esperaPeloResultado: TimeSpan.Zero);
        await comandos.AtualizarAsync();
        Assert.True(comandos.AcertarRelogio.CanExecute(null));
        await comandos.AcertarRelogio.ExecutarAsync();
        Assert.Equal("Ana Sintética (ana.teste)", Assert.Single(comandos.Historico).Operador);

        ConsumirSemGiro();
        var estornos = new PainelDeUsosSemPassagem(cliente);
        await estornos.AtualizarAsync();
        estornos.Selecionada = Assert.Single(estornos.Linhas);
        estornos.Motivo = Motivo;
        await estornos.PrepararEstorno.ExecutarAsync();
        Assert.True(estornos.Confirmando);
        await estornos.ConfirmarEstorno.ExecutarAsync();
        Assert.Empty(estornos.Linhas);
        Assert.Equal("Ana Sintética (ana.teste)", QuemEstornou());
    }

    [Fact]
    public async Task Sem_sessao_nao_se_aceita_o_nome_digitado_como_autenticacao()
    {
        var (cliente, _) = Painel();
        var comando = await Assert.ThrowsAsync<RpcException>(() => cliente.EnviarComandoAsync(new EnviarComandoRequest
        {
            Inner = 1, Tipo = TipoDeComando.AcertarRelogio, Operador = "Ana Sintética (ana.teste)",
        }).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, comando.StatusCode);
        var estorno = await Assert.ThrowsAsync<RpcException>(() => cliente.EstornarUsoAsync(new EstornarUsoRequest
        {
            TentativaId = ConsumirSemGiro().ToString(), Operador = "Ana Sintética (ana.teste)", Motivo = Motivo,
        }).ResponseAsync);
        Assert.Equal(StatusCode.Unauthenticated, estorno.StatusCode);
    }

    [Fact]
    public async Task Usuario_sem_permissao_nao_comanda_nem_estorna_com_nome_forjado()
    {
        var administrador = await EntrarAsync();
        var usuario = await administrador.GravarUsuarioAsync(new GravarUsuarioRequest
        {
            Login = "consulta.teste", Nome = "Consulta Sintética", Ativo = true,
            Papeis = { "somente_leitura" }, SenhaInicial = "provisoria-sintetica-123",
        });
        Assert.True(usuario.Gravado, string.Join(" ", usuario.Problemas));
        var (cliente, sessao) = Painel();
        var entrada = await cliente.EntrarAsync(new EntrarRequest { Login = "consulta.teste", Senha = "provisoria-sintetica-123" });
        Assert.True(entrada.Aceito, entrada.Mensagem);
        sessao.Token = entrada.Sessao;
        Assert.True((await cliente.TrocarSenhaAsync(new TrocarSenhaRequest
        {
            SenhaAtual = "provisoria-sintetica-123", SenhaNova = "definitiva-sintetica-123",
        })).Trocada);

        var comando = await Assert.ThrowsAsync<RpcException>(() => cliente.EnviarComandoAsync(new EnviarComandoRequest
        {
            Inner = 1, Tipo = TipoDeComando.AcertarRelogio, Operador = "Ana Sintética (ana.teste)",
        }).ResponseAsync);
        Assert.Equal(StatusCode.PermissionDenied, comando.StatusCode);
        var estorno = await Assert.ThrowsAsync<RpcException>(() => cliente.EstornarUsoAsync(new EstornarUsoRequest
        {
            TentativaId = ConsumirSemGiro().ToString(), Operador = "Ana Sintética (ana.teste)", Motivo = Motivo,
        }).ResponseAsync);
        Assert.Equal(StatusCode.PermissionDenied, estorno.StatusCode);
        Assert.Empty((await administrador.ListarComandosAsync(new ListarComandosRequest())).Comandos);
        Assert.Single((await administrador.ListarUsosSemPassagemAsync(new ListarUsosSemPassagemRequest())).Usos);
    }
}
