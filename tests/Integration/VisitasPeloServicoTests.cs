using System.Security.Cryptography;
using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>P6: login e permissão reais no IPC, máscaras nas respostas e ViewModel contra o serviço.</summary>
public sealed class VisitasPeloServicoTests : IAsyncLifetime, IDisposable
{
    private readonly BancoTemporario _banco = new();
    private UsuariosDoSistema _usuarios = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;
    private string _host = string.Empty;
    private DateTimeOffset _agora = VisitasTests.Agora;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _usuarios = new UsuariosDoSistema(_banco.Fabrica, 100_000);
        _usuarios.GarantirAdministradorPadrao(_agora);
        var cifra = new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(32));
        var pessoas = new CadastroDePessoas(_banco.Fabrica, cifra);
        _host = pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Beatriz Lima" }, _agora).Id!;
        var sessoes = new SessoesDoPainel();
        var servico = new EdgeControlService(new WorkerSupervisor([]), relogio: () => _agora, semConfiguracao: true,
            usuarios: _usuarios, sessoes: sessoes, visitas: new Visitas(_banco.Fabrica, cifra));
        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"visitas-{Guid.NewGuid():N}");
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
        if (_servidor is not null) { await _servidor.StopAsync(); await _servidor.DisposeAsync(); }
        if (!TransporteLocal.UsaNamedPipe && File.Exists(_endereco)) { File.Delete(_endereco); }
    }

    public void Dispose() => _banco.Dispose();

    private EdgeControl.EdgeControlClient Cliente(SessaoDoPainel sessao) => new(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));

    private async Task<(EdgeControl.EdgeControlClient Cliente, SessaoDoPainel Sessao)> Entrar(string login, string papel)
    {
        var admin = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
        Assert.Empty(_usuarios.Gravar(admin, null, login, "Operador " + login, true, [papel], "provisoria-1", _agora).Problemas);
        var sessao = new SessaoDoPainel();
        var cliente = Cliente(sessao);
        sessao.Token = (await cliente.EntrarAsync(new EntrarRequest { Login = login, Senha = "provisoria-1" })).Sessao;
        Assert.True((await cliente.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "provisoria-1", SenhaNova = "senha-de-" + login })).Trocada);
        return (cliente, sessao);
    }

    private AgendarVisitaRequest Pedido() => new()
    {
        Nome = "Rafaela Souza", AnfitriaoId = _host, Motivo = "Reunião de trabalho",
        De = Timestamp.FromDateTimeOffset(_agora), Ate = Timestamp.FromDateTimeOffset(_agora.AddHours(1)),
    };

    [Fact]
    public async Task Portaria_agenda_recebe_ve_so_mascaras_e_encerra_com_autoria_da_sessao()
    {
        var (portaria, _) = await Entrar("recepcao", "portaria");
        Assert.Single((await portaria.BuscarAnfitrioesDeVisitaAsync(new BuscarAnfitrioesDeVisitaRequest { Texto = "beat" })).Anfitrioes);
        var r = await portaria.AgendarVisitaAsync(Pedido());
        Assert.True(r.Gravado, string.Join(" ", r.Problemas));
        Assert.True((await portaria.ReceberVisitaAsync(new ReceberVisitaRequest
        {
            Id = r.Id, TipoDoDocumento = "rg", Documento = "RG-FICTICIO-001", DocumentoConferido = true,
            TipoDoCodigo = "cartao", Codigo = "77001234",
        })).Gravado);
        var visita = Assert.Single((await portaria.ListarVisitasAsync(new ListarVisitasRequest())).Visitas);
        Assert.Equal("Rafaela Souza", visita.Nome);
        Assert.Equal("em_visita", visita.Situacao);
        Assert.DoesNotContain("77001234", visita.CodigoMascarado, StringComparison.Ordinal);
        Assert.DoesNotContain("RG-FICTICIO-001", visita.DocumentoMascarado, StringComparison.Ordinal);
        Assert.True((await portaria.EncerrarVisitaAsync(new EncerrarVisitaRequest { Id = r.Id })).Gravado);
        using var c = _banco.Fabrica.Abrir();
        using var s = c.CreateCommand();
        s.CommandText = "SELECT DISTINCT actor_id FROM person_event WHERE action LIKE 'visita.%';";
        Assert.Equal(_usuarios.Listar().Single(u => u.Login == "recepcao").Id, s.ExecuteScalar());
    }

    [Fact]
    public async Task Todas_as_rpcs_rejeitam_sem_sessao_e_sem_a_permissao_propria()
    {
        var (leitor, _) = await Entrar("leitor", "somente_leitura");
        var semSessao = Cliente(new SessaoDoPainel());
        foreach (var cliente in new[] { leitor, semSessao })
        {
            var esperado = ReferenceEquals(cliente, leitor) ? StatusCode.PermissionDenied : StatusCode.Unauthenticated;
            await Recusada(() => cliente.ListarVisitasAsync(new ListarVisitasRequest()).ResponseAsync, esperado);
            await Recusada(() => cliente.BuscarAnfitrioesDeVisitaAsync(new BuscarAnfitrioesDeVisitaRequest()).ResponseAsync, esperado);
            await Recusada(() => cliente.AgendarVisitaAsync(Pedido()).ResponseAsync, esperado);
            await Recusada(() => cliente.ReceberVisitaAsync(new ReceberVisitaRequest()).ResponseAsync, esperado);
            await Recusada(() => cliente.EncerrarVisitaAsync(new EncerrarVisitaRequest()).ResponseAsync, esperado);
        }

        var admin = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
        var (papel, problemas) = _usuarios.GravarPapel(admin, null, "Anfitrião", null, [Permissoes.VisitasVer, Permissoes.VisitasAgendar], _agora);
        Assert.Empty(problemas);
        var (anfitriao, _) = await Entrar("host", papel!);
        Assert.True((await anfitriao.AgendarVisitaAsync(Pedido())).Gravado);
        await Recusada(() => anfitriao.ReceberVisitaAsync(new ReceberVisitaRequest()).ResponseAsync, StatusCode.PermissionDenied);
        await Recusada(() => anfitriao.EncerrarVisitaAsync(new EncerrarVisitaRequest()).ResponseAsync, StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task Viewmodel_percorre_o_fluxo_real_com_horario_de_brasilia_e_limpa_dados_ao_sair()
    {
        var (cliente, canal) = await Entrar("recepcao", "portaria");
        var sessao = new SessaoDoUsuarioViewModel(cliente, canal);
        await sessao.IniciarAsync();
        var janela = new JanelaViewModel(cliente, () => _agora, sessao);
        var tela = janela.Telas.OfType<VisitasViewModel>().Single();
        Assert.Contains(tela, janela.TelasDoMenu);
        await tela.AtualizarAsync();
        tela.Nome = "Rafaela Souza"; tela.AnfitriaoId = _host; tela.Motivo = "Reunião de trabalho";
        tela.DataDe = new DateTime(2026, 11, 16); tela.DataAte = tela.DataDe; tela.HoraDe = "09:00"; tela.HoraAte = "10:00";
        await tela.Agendar.ExecutarAsync();
        Assert.Equal("Agendada", Assert.Single(tela.Visitas).Situacao);
        Assert.Contains("09:00", tela.Selecionada!.Janela, StringComparison.Ordinal);
        tela.Documento = "RG-FICTICIO-001"; tela.Codigo = "77001234";
        Assert.False(tela.Receber.CanExecute(null));
        tela.DocumentoConferido = true;
        await tela.Receber.ExecutarAsync();
        Assert.Equal("Em visita", tela.Selecionada.Situacao);
        Assert.Equal(string.Empty, tela.Documento); Assert.Equal(string.Empty, tela.Codigo);
        Assert.False(tela.Encerrar.CanExecute(null));
        tela.SaidaConfirmada = true;
        await tela.Encerrar.ExecutarAsync();
        Assert.Equal("Encerrada", tela.Selecionada.Situacao);
        await sessao.Sair.ExecutarAsync();
        Assert.Empty(tela.Visitas); Assert.Empty(tela.Anfitrioes); Assert.Null(tela.Selecionada);
        Assert.DoesNotContain(tela, janela.TelasDoMenu);
        Assert.False(tela.Agendar.CanExecute(null)); Assert.False(tela.Receber.CanExecute(null));
    }

    private static async Task Recusada(Func<Task> chamada, StatusCode esperado)
    {
        var erro = await Assert.ThrowsAsync<RpcException>(chamada);
        Assert.Equal(esperado, erro.StatusCode);
    }
}
