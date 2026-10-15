using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Grpc.Core;

namespace Integration.Tests;

/// <summary>
/// Os RPCs do cadastro de cartões (migração 026) pelo serviço. Nenhuma resposta traz o código do cartão
/// em claro: a lista mostra máscara, e o cadastro recebe o identificador da tentativa.
/// </summary>
public sealed class CadastroDeCartoesPeloServicoTests : IDisposable
{
    private const string Balcao = "balcao-local";
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repo;
    private readonly EdgeControlService _servico;

    public CadastroDeCartoesPeloServicoTests()
    {
        _banco.Migrar();
        _repo = new RepositorioDeIngressos(_banco.Fabrica);
        _repo.RegistrarProvedor(new ProvedorDeIngresso(Balcao, "Bilheteria local", "raw", "", Reutilizavel: true), Agora);
        _servico = new EdgeControlService(new WorkerSupervisor([]), relogio: () => Agora, semConfiguracao: true, cartoes: _repo);
    }

    public void Dispose() => _banco.Dispose();

    private static AbrirSessaoDeCadastroRequest Abrir(string provedor = Balcao, string categoria = "INTEIRA", string lote = "Evento 1") =>
        new() { ProvedorId = provedor, Categoria = categoria, Lote = lote, Usos = 1 };

    [Fact]
    public async Task Sessao_abre_consulta_e_fecha_com_a_contagem()
    {
        var aberta = await _servico.AbrirSessaoDeCadastro(Abrir(), null!);
        Assert.Equal(ResultadoDaSessaoDeCadastro.Aberta, aberta.Resultado);
        Assert.Equal("INTEIRA", aberta.Sessao.Categoria);
        Assert.Equal("Evento 1", aberta.Sessao.Lote);

        var consulta = await _servico.ObterSessaoDeCadastro(new ObterSessaoDeCadastroRequest(), null!);
        Assert.Equal(ResultadoDaSessaoDeCadastro.Aberta, consulta.Resultado);

        _repo.TentarUsar("A1B2C3D4", "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor2);

        var fechada = await _servico.FecharSessaoDeCadastro(new FecharSessaoDeCadastroRequest(), null!);
        Assert.Equal(ResultadoDaSessaoDeCadastro.Fechada, fechada.Resultado);
        Assert.Equal(1, fechada.Sessao.Cadastrados);

        var depois = await _servico.ObterSessaoDeCadastro(new ObterSessaoDeCadastroRequest(), null!);
        Assert.Equal(ResultadoDaSessaoDeCadastro.NenhumaAberta, depois.Resultado);
    }

    [Fact]
    public async Task Abrir_com_dados_invalidos_ou_provedor_vazio_recusa()
    {
        var semProvedor = await _servico.AbrirSessaoDeCadastro(Abrir(provedor: ""), null!);
        Assert.Equal(ResultadoDaSessaoDeCadastro.DadosInvalidos, semProvedor.Resultado);

        var categoriaRuim = await _servico.AbrirSessaoDeCadastro(Abrir(categoria: "inteira"), null!);
        Assert.Equal(ResultadoDaSessaoDeCadastro.DadosInvalidos, categoriaRuim.Resultado);
    }

    [Fact]
    public async Task Nenhuma_resposta_traz_o_codigo_em_claro()
    {
        const string codigo = "7F3E9A21";
        _repo.TentarUsar(codigo, "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor1);

        var lista = await _servico.ListarLeiturasDesconhecidas(new ListarLeiturasDesconhecidasRequest { Limite = 10 }, null!);

        var cartao = Assert.Single(lista.Cartoes);
        Assert.Equal(1, cartao.Vezes);
        Assert.DoesNotContain(codigo, cartao.CodigoMascarado, StringComparison.Ordinal);
        Assert.DoesNotContain(codigo, lista.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(codigo, (await _servico.AbrirSessaoDeCadastro(Abrir(), null!)).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cadastrar_recusado_pelo_identificador_da_tentativa()
    {
        _repo.TentarUsar("99887766", "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor1);
        var tentativa = (await _servico.ListarLeiturasDesconhecidas(new ListarLeiturasDesconhecidasRequest(), null!)).Cartoes[0].TentativaId;

        var resposta = await _servico.CadastrarLeituraDesconhecida(new CadastrarLeituraDesconhecidaRequest
        {
            TentativaId = tentativa,
            ProvedorId = Balcao,
            Categoria = "MEIA",
            Lote = "Meia lote 2",
            Usos = 1,
        }, null!);

        Assert.Equal(ResultadoDoRegistro.Cadastrado, resposta.Resultado);
        Assert.Empty((await _servico.ListarLeiturasDesconhecidas(new ListarLeiturasDesconhecidasRequest(), null!)).Cartoes);
    }

    [Fact]
    public async Task Cadastrar_com_identificador_invalido_recusa_sem_gravar()
    {
        var resposta = await _servico.CadastrarLeituraDesconhecida(new CadastrarLeituraDesconhecidaRequest
        {
            TentativaId = "nao-e-um-guid",
            ProvedorId = Balcao,
            Categoria = "MEIA",
            Lote = "Lote",
            Usos = 1,
        }, null!);

        Assert.Equal(ResultadoDoRegistro.DadosInvalidos, resposta.Resultado);
    }

    [Fact]
    public async Task Sem_o_repositorio_de_cartoes_a_instalacao_recusa_com_indisponivel()
    {
        var servico = new EdgeControlService(new WorkerSupervisor([]), relogio: () => Agora, semConfiguracao: true);

        var erro = await Assert.ThrowsAsync<RpcException>(() => servico.AbrirSessaoDeCadastro(Abrir(), null!));
        Assert.Equal(StatusCode.Unimplemented, erro.StatusCode);
    }
}
