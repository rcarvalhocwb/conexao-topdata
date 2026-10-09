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
            pessoas: _pessoas, parametrosDoCadastro: new ParametrosDoCadastro(_banco.Fabrica),
            importacaoDePessoas: new ImportacaoDePessoas(_banco.Fabrica, _pessoas));

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

    [Fact]
    public async Task Telas_cadastram_bloqueiam_dao_credencial_e_fecham_a_catraca()
    {
        var cliente = await Entrar("sup", "supervisor");

        // Parâmetros: tabela de horário pela tela, com faixas em texto.
        var parametros = new Desktop.ViewModels.ParametrosDoCadastroViewModel(cliente, () => Agora);
        await parametros.AtualizarAsync();
        Assert.Equal(9, parametros.Perfis.Count);
        parametros.HorarioNome = "Comercial";
        parametros.Dias[1].Faixas = "08:00-12:00, 13:00-18:00";
        await parametros.GravarHorario.ExecutarAsync();
        Assert.Contains("gravada", parametros.Mensagem, StringComparison.Ordinal);
        Assert.Equal("Seg 08:00-12:00, 13:00-18:00", Assert.Single(parametros.Horarios).Resumo);
        parametros.Dias[2].Faixas = "8h às 12h";
        await parametros.GravarHorario.ExecutarAsync();
        Assert.StartsWith("Terça:", parametros.Mensagem, StringComparison.Ordinal);

        // Pessoas: cadastra, dá crachá, bloqueia.
        var pessoas = new Desktop.ViewModels.PessoasViewModel(cliente, () => Agora);
        await pessoas.AtualizarAsync();
        pessoas.PerfilId = "aluno";
        pessoas.NomeCompleto = "Rafaela Souza";
        pessoas.Nascimento = "01/02/1995";
        await pessoas.Gravar.ExecutarAsync();
        Assert.True(pessoas.TemFicha, pessoas.Mensagem);
        Assert.Equal("ativo", pessoas.Situacao);

        pessoas.NovoCodigo = "77001234";
        await pessoas.AdicionarCredencial.ExecutarAsync();
        Assert.Equal(string.Empty, pessoas.NovoCodigo);
        var credencial = Assert.Single(pessoas.Credenciais);
        Assert.DoesNotContain("77001234", credencial.Codigo, StringComparison.Ordinal);

        Assert.False(pessoas.Bloquear.CanExecute(null));
        pessoas.Motivo = "Mensalidade em atraso";
        await pessoas.Bloquear.ExecutarAsync();
        Assert.Equal("bloqueado", pessoas.Situacao);
        Assert.Contains("bloqueada", pessoas.Mensagem, StringComparison.Ordinal);

        pessoas.Busca = "rafa";
        await pessoas.Buscar.ExecutarAsync();
        Assert.Equal("Bloqueada", Assert.Single(pessoas.Pessoas).Situacao);

        // Gerenciar catraca: fechar e reabrir com motivo.
        var gerenciar = new Desktop.ViewModels.GerenciarCatracaViewModel(cliente, () => Agora);
        gerenciar.Catraca = 2;
        Assert.False(gerenciar.FecharCatraca.CanExecute(null));
        gerenciar.MotivoDoFechamento = "Obra na entrada";
        await gerenciar.FecharCatraca.ExecutarAsync();
        Assert.True(gerenciar.EstaFechada, gerenciar.Mensagem);
        Assert.Contains("Usuária sup (sup)", gerenciar.SituacaoDoFechamento, StringComparison.Ordinal);
        gerenciar.MotivoDoFechamento = "Obra concluída";
        await gerenciar.AbrirCatraca.ExecutarAsync();
        Assert.False(gerenciar.EstaFechada);
    }

    [Fact]
    public async Task Tela_importa_planilha_com_previa_e_desfaz_o_lote()
    {
        var portaria = await Entrar("port", "portaria");
        var supervisor = await Entrar("sup", "supervisor");
        var arquivo = System.Text.Encoding.UTF8.GetBytes("Nome;Perfil;Crachá\r\nLia Prado;Aluno;880001\r\nRui Prado;Aluno;880002");

        // A portaria não importa (o papel não tem a permissão).
        Assert.Equal(StatusCode.PermissionDenied, await Codigo(() => portaria.PreverImportacaoDePessoasAsync(new ImportacaoDePessoasRequest
        {
            NomeDoArquivo = "p.csv", Conteudo = Google.Protobuf.ByteString.CopyFrom(arquivo),
        }).ResponseAsync));

        var tela = new Desktop.ViewModels.PessoasViewModel(supervisor, () => Agora);
        await tela.AtualizarAsync();
        Assert.False(tela.AplicarImportacao.CanExecute(null));

        await tela.PreverImportacaoAsync("p.csv", arquivo);
        Assert.True(tela.ImportacaoAplicavel, string.Join(" | ", tela.ProblemasDaImportacao));
        Assert.Empty(tela.Pessoas);

        await tela.AplicarImportacao.ExecutarAsync();
        Assert.Equal(2, tela.Pessoas.Count);
        tela.LoteSelecionado = Assert.Single(tela.Lotes);

        await tela.DesfazerImportacao.ExecutarAsync();
        Assert.Empty(tela.Pessoas);
        Assert.False(Assert.Single(tela.Lotes).PodeDesfazer);
    }

    [Theory]
    [InlineData("08:00-12:00, 13:00-18:00", true, 2)]
    [InlineData("", true, 0)]
    [InlineData("00:00-24:00", true, 1)]
    [InlineData("18:00-08:00", false, 0)]
    [InlineData("8h-12h", false, 0)]
    public void Faixas_de_horario_em_texto(string texto, bool valido, int quantas)
    {
        Assert.Equal(valido, Desktop.ViewModels.ParametrosDoCadastroViewModel.LerFaixas(texto, out var faixas));
        if (valido)
        {
            Assert.Equal(quantas, faixas.Count);
        }
    }
}
