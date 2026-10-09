using System.Security.Cryptography;
using System.Text;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Google.Protobuf;

namespace Integration.Tests;

/// <summary>
/// docs/43 P5: importação de pessoas por planilha. A prévia aponta cada linha ruim com a mesma regra da
/// ficha; aplicar grava tudo ou nada; desfazer apaga o lote e as passagens ficam sem a pessoa.
/// </summary>
public sealed class ImportacaoDePessoasTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private const string Cabecalho = "Nome;Perfil;Tipo de documento;Documento;Empresa;Sala;Catracas;Crachá;Vale até";

    private readonly BancoTemporario _banco = new();
    private readonly EdgeControlService _servico;
    private readonly RepositorioDeIngressos _ingressos;

    public ImportacaoDePessoasTests()
    {
        _banco.Migrar();
        var cadastro = new CadastroDePessoas(_banco.Fabrica, new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave)));
        _servico = new EdgeControlService(
            new WorkerSupervisor([]), relogio: () => Agora, semConfiguracao: true,
            pessoas: cadastro, parametrosDoCadastro: new ParametrosDoCadastro(_banco.Fabrica),
            importacaoDePessoas: new ImportacaoDePessoas(_banco.Fabrica, cadastro));
        _ingressos = new RepositorioDeIngressos(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private static ImportacaoDePessoasRequest Csv(params string[] linhas) => new()
    {
        NomeDoArquivo = "pessoas.csv",
        Conteudo = ByteString.CopyFrom(Encoding.UTF8.GetBytes(string.Join("\r\n", linhas))),
    };

    private Task<Contracts.Edge.V1.ResultadoDaImportacaoDePessoas> Prever(ImportacaoDePessoasRequest pedido) => _servico.PreverImportacaoDePessoas(pedido, null!);

    private Task<Contracts.Edge.V1.ResultadoDaImportacaoDePessoas> Importar(ImportacaoDePessoasRequest pedido) => _servico.ImportarPessoas(pedido, null!);

    [Fact]
    public async Task Previa_aponta_cada_linha_ruim_e_nao_grava_nada()
    {
        var pedido = Csv(
            Cabecalho,
            "Ana Lima;colaborador;cpf;529.982.247-25;Acme;101;1;5001;",
            "Bruno Reis;Inexistente;;;;;;5002;",
            "Carla Dias;colaborador;cpf;111.111.111-11;Acme;;;5003;",
            "Davi Melo;Prestador;;;Beta;;;5001;",
            ";colaborador;;;;;;;");

        var previa = await Prever(pedido);

        Assert.False(previa.Aplicavel);
        var problemas = previa.Problemas.Select(p => (p.Linha, p.Problema)).ToList();
        Assert.Contains((3, "Perfil \"Inexistente\" não existe ou está inativo."), problemas);
        Assert.Contains((4, "CPF inválido (confira os dígitos)."), problemas);
        Assert.Contains(problemas, p => p.Linha == 5 && p.Problema.Contains("linha 2", StringComparison.Ordinal));   // crachá repetido no arquivo
        Assert.Contains(problemas, p => p.Linha == 5 && p.Problema.Contains("Documento", StringComparison.Ordinal)); // prestador exige documento
        Assert.Contains((6, "Falta o nome."), problemas);

        // Nada gravado: nem pessoa, nem empresa criada, nem lote.
        Assert.Empty((await _servico.BuscarPessoasAsync()).Pessoas);
        Assert.Empty((await _servico.ObterParametrosDoCadastro(new ObterParametrosDoCadastroRequest(), null!)).Empresas);

        // Aplicar com problema também não grava.
        var aplicada = await Importar(pedido);
        Assert.False(aplicada.Aplicada);
        Assert.Empty((await _servico.BuscarPessoasAsync()).Pessoas);
    }

    [Fact]
    public async Task Aplicar_grava_tudo_o_cracha_vale_na_catraca_e_desfazer_apaga_o_lote()
    {
        var pedido = Csv(
            Cabecalho,
            "Ana Lima;colaborador;cpf;529.982.247-25;Acme;101;1;5001;",
            "Bruno Reis;Morador;;;Acme;102;;5002;",
            "Carla Dias;aluno;;;;;;;31/12/2026");

        var previa = await Prever(pedido);
        Assert.True(previa.Aplicavel, string.Join(" | ", previa.Problemas.Select(p => $"{p.Linha}: {p.Problema}")));
        Assert.Equal(3, previa.Pessoas);
        Assert.Equal(2, previa.Credenciais);
        Assert.Equal(1, previa.EmpresasNovas);
        Assert.Equal(2, previa.SalasNovas);

        var aplicada = await Importar(pedido);
        Assert.True(aplicada.Aplicada);
        Assert.Equal(3, (await _servico.BuscarPessoasAsync()).Pessoas.Count);

        // O crachá importado decide na catraca, com as catracas da linha.
        Assert.True(_ingressos.TentarUsar("5001", "portao-1", "inner-1", Agora).Resultado.Liberou);
        Assert.Equal(MotivoDoUso.PortaoNaoPermitido, _ingressos.TentarUsar("5001", "portao-2", "inner-2", Agora).Resultado.Motivo);

        // O mesmo arquivo de novo: tudo repetido, nada entra.
        var repetida = await Importar(pedido);
        Assert.False(repetida.Aplicada);
        Assert.Contains(repetida.Problemas, p => p.Problema == "Já existe uma pessoa com este documento.");

        var lote = Assert.Single((await _servico.ListarImportacoesDePessoas(new ListarImportacoesDePessoasRequest(), null!)).Lotes);
        Assert.Equal(3, lote.Pessoas);
        var desfeita = await _servico.DesfazerImportacaoDePessoas(new DesfazerImportacaoDePessoasRequest { LoteId = lote.Id }, null!);
        Assert.True(desfeita.Gravado, string.Join(" ", desfeita.Problemas));

        Assert.Empty((await _servico.BuscarPessoasAsync()).Pessoas);
        Assert.Empty((await _servico.ObterParametrosDoCadastro(new ObterParametrosDoCadastroRequest(), null!)).Empresas);
        Assert.Equal(MotivoDoUso.Desconhecido, _ingressos.TentarUsar("5001", "portao-1", "inner-1", Agora).Resultado.Motivo);
        Assert.False((await _servico.DesfazerImportacaoDePessoas(new DesfazerImportacaoDePessoasRequest { LoteId = lote.Id }, null!)).Gravado);
    }

    [Fact]
    public async Task Coluna_de_saude_recusa_o_arquivo_inteiro()
    {
        var previa = await Prever(Csv("Nome;Perfil;Alergias", "Ana Lima;aluno;Amendoim"));

        Assert.False(previa.Aplicavel);
        Assert.Contains("saúde", Assert.Single(previa.Problemas).Problema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cinco_mil_linhas_entram_de_uma_vez()
    {
        var linhas = new List<string> { "Nome;Perfil;Empresa;Crachá" };
        linhas.AddRange(Enumerable.Range(0, 5000).Select(i => $"Pessoa Numero {i:D4};colaborador;Acme;{900_000 + i}"));

        var aplicada = await Importar(Csv([.. linhas]));

        Assert.True(aplicada.Aplicada, string.Join(" | ", aplicada.Problemas.Take(5).Select(p => $"{p.Linha}: {p.Problema}")));
        Assert.Equal(5000, aplicada.Pessoas);
        Assert.True(_ingressos.TentarUsar("904999", "portao-1", "inner-1", Agora).Resultado.Liberou);
    }
}

internal static class BuscaDoServico
{
    public static async Task<BuscarPessoasResponse> BuscarPessoasAsync(this EdgeControlService servico) =>
        await servico.BuscarPessoas(new BuscarPessoasRequest { Limite = 100 }, null!);
}

/// <summary>O modelo que vai no instalador passa na prévia: só exemplos fictícios, colunas do modelo.</summary>
public sealed class ModeloDePessoasTests : IDisposable
{
    private readonly BancoTemporario _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task O_modelo_de_pessoas_passa_na_previa_com_todas_as_colunas()
    {
        _banco.Migrar();
        var cadastro = new CadastroDePessoas(_banco.Fabrica, new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave)));
        var servico = new EdgeControlService(
            new WorkerSupervisor([]), semConfiguracao: true, pessoas: cadastro,
            importacaoDePessoas: new ImportacaoDePessoas(_banco.Fabrica, cadastro));
        var caminho = Path.Combine(RepositorioDoModelo(), "installer", "modelos", "modelo-pessoas.csv");

        var previa = await servico.PreverImportacaoDePessoas(
            new ImportacaoDePessoasRequest { NomeDoArquivo = "modelo-pessoas.csv", Conteudo = ByteString.CopyFrom(File.ReadAllBytes(caminho)) }, null!);

        Assert.True(previa.Aplicavel, string.Join(" | ", previa.Problemas.Select(p => $"{p.Linha}: {p.Problema}")));
        Assert.Empty(previa.Avisos);
        Assert.Equal(4, previa.Pessoas);
        Assert.Equal(Access.Importacao.PlanilhaDePessoas.Colunas.Count, File.ReadLines(caminho).First().Split(';').Length);
        Assert.All(File.ReadLines(caminho).Skip(1), l => Assert.Contains("exemplo fictício", l, StringComparison.Ordinal));
    }

    private static string RepositorioDoModelo()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !Directory.Exists(Path.Combine(pasta.FullName, "installer", "modelos")))
        {
            pasta = pasta.Parent;
        }

        return pasta?.FullName ?? throw new InvalidOperationException("Pasta installer/modelos não encontrada.");
    }
}
