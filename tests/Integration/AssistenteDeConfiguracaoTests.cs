using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Supervisor.Instalacao;

namespace Integration.Tests;

/// <summary>
/// A lógica do assistente de configuração: o que ele recusa, o que ele grava, e que o
/// serviço lê o que ele gravou.
/// </summary>
public sealed class AssistenteDeConfiguracaoTests : IDisposable
{
    private const string Segredo = "segredo-de-teste-com-mais-de-32-caracteres";

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    private string Arquivo => Path.Combine(_pasta, "workers.json");

    private string Worker
    {
        get
        {
            // O serviço confere que o executável existe.
            var caminho = Path.Combine(_pasta, "Worker", "Edge.Worker.X86.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, "x");
            return caminho;
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
            // Sobra de arquivo temporário não reprova o teste.
        }
    }

    private static DadosDaInstalacao Evento() => new()
    {
        Catracas = [new(1, "Entrada 1"), new(2, "Entrada 2"), new(5, "Saída")],
        NuvemLigada = true,
        BaseDaNuvem = "https://painel.invalid/functions/v1/",
        Dispositivo = "borda-01",
        Segredo = Segredo,
    };

    [Fact]
    public void Grava_o_que_o_servico_le_e_o_segredo_vai_para_o_cofre_e_nao_para_o_arquivo()
    {
        var cofre = new CofreEmMemoria();

        AssistenteDeConfiguracao.Gravar(Evento(), Arquivo, Worker, cofre);

        var texto = File.ReadAllText(Arquivo);
        Assert.DoesNotContain(Segredo, texto, StringComparison.Ordinal);
        Assert.Equal(Segredo, cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo));

        // O serviço lê e aceita exatamente o que o assistente gravou.
        var lida = ConfiguracaoDoSupervisor.Ler(Arquivo);
        Assert.Empty(lida.Validar());
        Assert.Equal([1, 2, 5], Assert.Single(lida.Grupos).Inners);
        Assert.Equal(3570, lida.Grupos[0].Porta);
        Assert.Equal("Saída", lida.NomesDasCatracas![5]);
        Assert.Equal("https://painel.invalid/functions/v1/", lida.Nuvem!.Base);
    }

    [Fact]
    public void Reabrir_o_assistente_traz_o_que_foi_gravado_sem_o_segredo()
    {
        AssistenteDeConfiguracao.Gravar(Evento(), Arquivo, Worker, new CofreEmMemoria());

        var dados = AssistenteDeConfiguracao.Carregar(Arquivo);

        Assert.Equal(Evento().Catracas, dados.Catracas);
        Assert.True(dados.NuvemLigada);
        Assert.Null(dados.Segredo);
    }

    [Fact]
    public void Regravar_sem_digitar_o_segredo_mantem_o_que_esta_no_cofre()
    {
        var cofre = new CofreEmMemoria();
        AssistenteDeConfiguracao.Gravar(Evento(), Arquivo, Worker, cofre);

        AssistenteDeConfiguracao.Gravar(Evento() with { Segredo = null, Porta = 3571 }, Arquivo, Worker, cofre);

        Assert.Equal(Segredo, cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo));
        Assert.Equal(3571, ConfiguracaoDoSupervisor.Ler(Arquivo).Grupos[0].Porta);
    }

    [Theory]
    [InlineData("sem catracas")]
    [InlineData("catraca repetida")]
    [InlineData("catraca sem nome")]
    [InlineData("porta baixa")]
    [InlineData("nuvem sem https")]
    [InlineData("segredo curto")]
    public void Recusa_antes_de_gravar(string caso)
    {
        var dados = caso switch
        {
            "sem catracas" => Evento() with { Catracas = [] },
            "catraca repetida" => Evento() with { Catracas = [new(1, "A"), new(1, "B")] },
            "catraca sem nome" => Evento() with { Catracas = [new(1, " ")] },
            "porta baixa" => Evento() with { Porta = 80 },
            "nuvem sem https" => Evento() with { BaseDaNuvem = "http://painel.invalid/functions/v1/" },
            "segredo curto" => Evento() with { Segredo = "curto" },
            _ => throw new ArgumentOutOfRangeException(nameof(caso)),
        };

        Assert.NotEmpty(AssistenteDeConfiguracao.Validar(dados));
        Assert.Throws<ArgumentException>(() => AssistenteDeConfiguracao.Gravar(dados, Arquivo, Worker, new CofreEmMemoria()));
        Assert.False(File.Exists(Arquivo));
    }

    [Fact]
    public void Sem_nuvem_nao_grava_secao_de_nuvem_nem_segredo()
    {
        var cofre = new CofreEmMemoria();

        AssistenteDeConfiguracao.Gravar(Evento() with { NuvemLigada = false }, Arquivo, Worker, cofre);

        Assert.Null(ConfiguracaoDoSupervisor.Ler(Arquivo).Nuvem);
        Assert.Null(cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo));
    }

    [Fact]
    public void Verificacao_do_ambiente_aponta_o_que_falta_em_portugues()
    {
        var itens = AssistenteDeConfiguracao.VerificarAmbiente(
            existeArquivo: _ => false,
            subpastas: pasta => pasta.Contains("AspNetCore", StringComparison.Ordinal) ? ["10.0.1"] : [],
            net35Instalado: false,
            pastaDoWorker: _pasta);

        Assert.Equal(false, itens.Single(i => i.Item.StartsWith("SDK da Topdata", StringComparison.Ordinal)).Ok);
        Assert.Equal(false, itens.Single(i => i.Item == ".NET Framework 3.5").Ok);
        Assert.Equal(true, itens.Single(i => i.Item.StartsWith("ASP.NET Core", StringComparison.Ordinal)).Ok);
        Assert.Contains("x86", itens.Single(i => i.Item.StartsWith(".NET 10 de 32", StringComparison.Ordinal)).Orientacao, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_painel_mostra_o_nome_dado_no_assistente()
    {
        var supervisor = new WorkerSupervisor([]);
        var servico = new EdgeControlService(
            new WorkerSupervisor([new CatracasFalsas()]),
            nomesDasCatracas: new Dictionary<int, string> { [1] = "Entrada 1" });

        var lista = await servico.ListarEquipamentos(new ListarEquipamentosRequest(), null!);

        Assert.Equal("Entrada 1", Assert.Single(lista.Equipamentos).NomeDoGate);
        Assert.Empty(supervisor.Workers);
    }

    private sealed class CatracasFalsas : IWorkerHost
    {
        public string Nome => "catracas";

        public int Porta => 3570;

        public IReadOnlyList<int> Inners { get; } = [1];

        public bool EstaVivo => true;

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar()
        {
        }

        public void Matar()
        {
        }

        public void Dispose()
        {
        }
    }
}
