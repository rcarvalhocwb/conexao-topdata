using System.Security.Cryptography;
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
/// docs/43 P3: o cadastro de pessoas pelo serviço, com login. Cada RPC confere a permissão; o código da
/// credencial nunca volta em claro; documento e contato voltam mascarados para quem não tem
/// <c>pessoas.ver_dados</c>, e gravar a ficha com a máscara não apaga o dado guardado.
/// </summary>
public sealed class PessoasPeloServicoTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private UsuariosDoSistema _usuarios = null!;
    private CadastroDePessoas _pessoas = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _usuarios = new UsuariosDoSistema(_banco.Fabrica, 100_000);
        _usuarios.GarantirAdministradorPadrao(Agora);
        _pessoas = new CadastroDePessoas(_banco.Fabrica, new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave)));

        var sessoes = new SessoesDoPainel();
        var servico = new EdgeControlService(
            new WorkerSupervisor([]), relogio: () => Agora, semConfiguracao: true, usuarios: _usuarios, sessoes: sessoes,
            pessoas: _pessoas, parametrosDoCadastro: new ParametrosDoCadastro(_banco.Fabrica));

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"pessoas-{Guid.NewGuid():N}");
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

    private async Task<EdgeControl.EdgeControlClient> Entrar(string login, string papel)
    {
        var admin = _usuarios.Listar().Single(u => u.Login == UsuariosDoSistema.LoginPadrao).Id;
        var (_, problemas) = _usuarios.Gravar(admin, null, login, "Usuária " + login, true, [papel], "provisoria-1", Agora);
        Assert.Empty(problemas);
        var sessao = new SessaoDoPainel();
        var cliente = new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(_endereco, _token, sessao));
        sessao.Token = (await cliente.EntrarAsync(new EntrarRequest { Login = login, Senha = "provisoria-1" })).Sessao;
        Assert.True((await cliente.TrocarSenhaAsync(new TrocarSenhaRequest { SenhaAtual = "provisoria-1", SenhaNova = "senha-de-" + login })).Trocada);
        return cliente;
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

    [Fact]
    public async Task Portaria_cadastra_ve_mascarado_e_nao_apaga_o_que_nao_ve()
    {
        var supervisor = await Entrar("sup", "supervisor");
        var portaria = await Entrar("port", "portaria");

        var gravada = await supervisor.GravarPessoaAsync(new GravarPessoaRequest
        {
            Pessoa = new PessoaDoCadastro
            {
                PerfilId = "aluno", NomeCompleto = "Beatriz Lima", TipoDoDocumento = "cpf", Documento = "529.982.247-25",
                Telefone = "11 98888-7777", Nascimento = "1990-05-01",
            },
        });
        Assert.True(gravada.Gravado, string.Join(" ", gravada.Problemas));
        Assert.True((await portaria.AdicionarCredencialAsync(new AdicionarCredencialRequest { PessoaId = gravada.Id, Tipo = "cartao", Codigo = "0012345678" })).Gravado);

        var vistaPelaPortaria = await portaria.ObterPessoaAsync(new ObterPessoaRequest { Id = gravada.Id });
        Assert.False(vistaPelaPortaria.DadosCompletos);
        Assert.Equal("Beatriz Lima", vistaPelaPortaria.Pessoa.NomeCompleto);
        Assert.Equal("************25", vistaPelaPortaria.Pessoa.Documento);
        Assert.Equal(string.Empty, vistaPelaPortaria.Pessoa.Nascimento);
        Assert.DoesNotContain("12345678", vistaPelaPortaria.Credenciais.Single().CodigoMascarado, StringComparison.Ordinal);

        // A portaria muda a observação e devolve a ficha com as máscaras: nada do que ela não vê se perde.
        vistaPelaPortaria.Pessoa.Observacao = "Turma da noite";
        Assert.True((await portaria.GravarPessoaAsync(new GravarPessoaRequest { Pessoa = vistaPelaPortaria.Pessoa })).Gravado);

        var vistaPeloSupervisor = await supervisor.ObterPessoaAsync(new ObterPessoaRequest { Id = gravada.Id });
        Assert.True(vistaPeloSupervisor.DadosCompletos);
        Assert.Equal("529.982.247-25", vistaPeloSupervisor.Pessoa.Documento);
        Assert.Equal("11 98888-7777", vistaPeloSupervisor.Pessoa.Telefone);
        Assert.Equal("1990-05-01", vistaPeloSupervisor.Pessoa.Nascimento);
        Assert.Equal("Turma da noite", vistaPeloSupervisor.Pessoa.Observacao);

        // A busca pelo nome e pelo código achados pelo serviço.
        Assert.Single((await portaria.BuscarPessoasAsync(new BuscarPessoasRequest { Texto = "beatriz" })).Pessoas);
        Assert.Single((await portaria.BuscarPessoasAsync(new BuscarPessoasRequest { Texto = "0012345678" })).Pessoas);
    }

    [Fact]
    public async Task Cada_acao_exige_a_permissao_do_papel()
    {
        var leitura = await Entrar("leitor", "somente_leitura");
        var portaria = await Entrar("port", "portaria");
        var supervisor = await Entrar("sup", "supervisor");

        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => leitura.BuscarPessoasAsync(new BuscarPessoasRequest()).ResponseAsync));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => leitura.GravarPessoaAsync(new GravarPessoaRequest()).ResponseAsync));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => portaria.GravarEmpresaAsync(new GravarEmpresaRequest()).ResponseAsync));
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => portaria.FecharCatracaAsync(new FecharCatracaRequest { Inner = 1, Motivo = "Teste de permissão" }).ResponseAsync));

        // Os parâmetros não têm dado pessoal: quem está logado lê.
        var parametros = await leitura.ObterParametrosDoCadastroAsync(new ObterParametrosDoCadastroRequest());
        Assert.Equal(9, parametros.Perfis.Count);

        var fechada = await supervisor.FecharCatracaAsync(new FecharCatracaRequest { Inner = 3, Motivo = "Manutenção do braço" });
        Assert.True(fechada.Gravado, string.Join(" ", fechada.Problemas));
        var lista = await leitura.ListarCatracasFechadasAsync(new ListarCatracasFechadasRequest());
        Assert.Equal("Usuária sup (sup)", lista.Catracas.Single().Por);

        var empresa = await supervisor.GravarEmpresaAsync(new GravarEmpresaRequest { Empresa = new EmpresaDoCadastro { Nome = "Acme", Cnpj = "11.222.333/0001-81", Ativa = true } });
        Assert.True(empresa.Gravado, string.Join(" ", empresa.Problemas));
        var horario = await supervisor.GravarHorarioAsync(new GravarHorarioRequest
        {
            Horario = new HorarioDoCadastro { Nome = "Turnos", Faixas = { new FaixaDoHorario { Dia = 1, Inicio = 0, Fim = 480 }, new FaixaDoHorario { Dia = 1, Inicio = 480, Fim = 960 }, new FaixaDoHorario { Dia = 1, Inicio = 960, Fim = 1440 } } },
        });
        Assert.True(horario.Gravado);
        Assert.Single(horario.Avisos);
    }
}
