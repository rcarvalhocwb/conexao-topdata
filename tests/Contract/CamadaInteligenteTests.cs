using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Contract.Tests;

/// <summary>
/// As guardas da camada inteligente (Etapa I do docs/36; desenho e invariantes em
/// docs/36-anexos/02 §3.1 e §7): <c>NOVO-ARQ-IA-01</c>, <c>NOVO-ARQ-IA-03</c> e
/// <c>NOVO-CTR-IA-01</c>.
/// </summary>
/// <remarks>
/// Verificado sobre os <c>.csproj</c>, o código-fonte e o <c>.proto</c>, como o
/// <see cref="ArquiteturaTests"/>: cobre também o que só compila no Windows.
/// </remarks>
public sealed partial class CamadaInteligenteTests
{
    private const string Inteligencia = "Access.Inteligencia";

    /// <summary>Quem pode referenciar a camada: o serviço e os projetos de teste.</summary>
    private static readonly string[] PodemUsarAInteligencia = ["Edge.Supervisor"];

    /// <summary>
    /// Os pontos de partida do processo do worker: tudo o que eles alcançam roda (ou pode rodar)
    /// no passo da decisão.
    /// </summary>
    private static readonly string[] RaizesDoWorker = ["Edge.Worker.X86", "Edge.Worker"];

    private static Dictionary<string, string> Projetos() =>
        Directory
            .EnumerateFiles(RepositorioDeMatriz.RaizDoRepositorio, "*.csproj", SearchOption.AllDirectories)
            .Where(c => !c.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            // Outros worktrees ficam em .claude/worktrees, dentro da raiz: não são este repositório.
            .Where(c => !Path.GetRelativePath(RepositorioDeMatriz.RaizDoRepositorio, c).StartsWith(".claude", StringComparison.Ordinal))
            .GroupBy(c => Path.GetFileNameWithoutExtension(c), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Length).First(), StringComparer.Ordinal);

    private static List<string> ReferenciasDe(string caminhoDoProjeto) =>
        [.. XDocument.Load(caminhoDoProjeto)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))];

    private static string Arquivo(params string[] partes) =>
        File.ReadAllText(Path.Combine([RepositorioDeMatriz.RaizDoRepositorio, .. partes]));

    // ------------------------------------------------------------------ NOVO-ARQ-IA-01

    /// <summary>
    /// <c>NOVO-ARQ-IA-01</c> (invariante I1): nada da camada roda no passo da decisão. O worker não
    /// alcança <c>Access.Inteligencia</c> nem direta nem indiretamente — por nenhum caminho de
    /// referências.
    /// </summary>
    [Fact]
    public void NOVO_ARQ_IA_01_o_worker_nao_alcanca_a_camada_inteligente()
    {
        var projetos = Projetos();
        var alcancados = new HashSet<string>(StringComparer.Ordinal);
        var fila = new Queue<string>(RaizesDoWorker.Where(projetos.ContainsKey));

        Assert.NotEmpty(fila);

        while (fila.Count > 0)
        {
            var nome = fila.Dequeue();
            if (!alcancados.Add(nome) || !projetos.TryGetValue(nome, out var caminho))
            {
                continue;
            }

            foreach (var referencia in ReferenciasDe(caminho))
            {
                fila.Enqueue(referencia);
            }
        }

        Assert.Contains("Access.Application", alcancados);
        Assert.DoesNotContain(Inteligencia, alcancados);
        Assert.DoesNotContain("Edge.Supervisor", alcancados);
    }

    /// <summary>Só o serviço (e os testes) referenciam a camada.</summary>
    [Fact]
    public void NOVO_ARQ_IA_01_so_o_servico_referencia_a_camada_inteligente()
    {
        var indevidos = Projetos()
            .Where(p => !p.Key.EndsWith(".Tests", StringComparison.Ordinal) && !PodemUsarAInteligencia.Contains(p.Key))
            .Where(p => ReferenciasDe(p.Value).Contains(Inteligencia))
            .Select(p => p.Key)
            .ToList();

        Assert.True(indevidos.Count == 0, $"Referenciam {Inteligencia} sem autorização: {string.Join(", ", indevidos)}");
    }

    /// <summary>
    /// A camada é pura: só a biblioteca padrão e, no máximo, o domínio (onde moram os motivos de
    /// negação e a hora de Brasília). Nenhum pacote NuGet — padrão de <c>Access.Importacao</c>.
    /// </summary>
    [Fact]
    public void NOVO_ARQ_IA_01_a_camada_so_conhece_o_dominio_e_nenhum_pacote()
    {
        var caminho = Projetos()[Inteligencia];

        var indevidas = ReferenciasDe(caminho).Where(r => r != "Access.Domain").ToList();
        var pacotes = XDocument.Load(caminho).Descendants("PackageReference").ToList();

        Assert.True(indevidas.Count == 0, $"{Inteligencia} referencia indevidamente: {string.Join(", ", indevidas)}");
        Assert.True(pacotes.Count == 0, $"{Inteligencia} não pode ter pacote NuGet.");
    }

    /// <summary>No assembly compilado: biblioteca padrão e o domínio, mais nada.</summary>
    [Fact]
    public void NOVO_ARQ_IA_01_o_assembly_da_camada_so_depende_da_biblioteca_padrao_e_do_dominio()
    {
        var permitidos = new[] { "System", "netstandard", "mscorlib", "Access.Domain" };

        var externas = typeof(Access.Inteligencia.ChavesDaInteligencia).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !permitidos.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(externas.Count == 0, $"{Inteligencia} passou a depender de: {string.Join(", ", externas)}");
    }

    // ------------------------------------------------------------------ NOVO-ARQ-IA-03

    /// <summary>
    /// <c>NOVO-ARQ-IA-03</c> (invariante I7): nenhum código da camada escreve em tabela da operação
    /// — <c>device_config</c>, <c>ticket</c>, <c>operator_command</c> nem nenhuma outra de
    /// <c>acesso.db</c>. Varre as instruções SQL de todo arquivo da camada: a camada pura não tem
    /// SQL; o Analisador e a leitura só para leitura não escrevem; a telemetria escreve só nas
    /// tabelas dela.
    /// </summary>
    [Fact]
    public void NOVO_ARQ_IA_03_a_camada_nao_escreve_em_tabela_da_operacao()
    {
        // A camada pura: nenhuma instrução SQL.
        var pura = Directory.EnumerateFiles(Path.Combine(RepositorioDeMatriz.RaizDoRepositorio, "src", Inteligencia), "*.cs", SearchOption.AllDirectories)
            .Where(c => !c.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(c => InstrucaoSql().IsMatch(File.ReadAllText(c)))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(pura.Count == 0, $"SQL na camada pura: {string.Join(", ", pura)}");

        // Quem lê acesso.db pela camada: nenhuma escrita, nenhuma coluna de código.
        foreach (var arquivo in ArquivosQueLeemAOperacao)
        {
            var texto = Arquivo(arquivo);
            Assert.DoesNotMatch(Escrita(), texto);
            Assert.DoesNotContain("qr_normalized", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("qr_raw", texto, StringComparison.Ordinal);
        }

        // Quem escreve: só nas tabelas de telemetria.db.
        var tabelasDaTelemetria = new HashSet<string>(StringComparer.Ordinal) { "analyzer_cycle", "schema_version" };
        var forasDaTelemetria = new List<string>();
        foreach (var arquivo in ArquivosQueEscrevemATelemetria())
        {
            foreach (Match m in Escrita().Matches(Arquivo(arquivo)))
            {
                if (!tabelasDaTelemetria.Contains(m.Groups["tabela"].Value))
                {
                    forasDaTelemetria.Add($"{arquivo[^1]}: {m.Value}");
                }
            }
        }

        Assert.True(forasDaTelemetria.Count == 0, $"A camada escreve fora de telemetria.db: {string.Join("; ", forasDaTelemetria)}");
    }

    /// <summary>Os arquivos pelos quais a camada lê a base da operação, só para leitura.</summary>
    private static readonly string[][] ArquivosQueLeemAOperacao =
    [
        ["src", "Access.Infrastructure.SQLite", "LeituraSomenteDaOperacao.cs"],
        ["src", "Edge.Supervisor", "AnalisadorDaOperacao.cs"],
    ];

    private static IEnumerable<string[]> ArquivosQueEscrevemATelemetria()
    {
        yield return ["src", "Access.Infrastructure.SQLite", "Telemetria.cs"];

        foreach (var sql in Directory.EnumerateFiles(
            Path.Combine(RepositorioDeMatriz.RaizDoRepositorio, "src", "Access.Infrastructure.SQLite", "MigracoesDaTelemetria"), "*.sql"))
        {
            yield return ["src", "Access.Infrastructure.SQLite", "MigracoesDaTelemetria", Path.GetFileName(sql)];
        }
    }

    /// <summary>A telemetria tem migrações próprias, prefixo T, numeração que não colide com a de acesso.db.</summary>
    [Fact]
    public void As_migracoes_da_telemetria_sao_numeradas_com_T()
    {
        var pasta = Path.Combine(RepositorioDeMatriz.RaizDoRepositorio, "src", "Access.Infrastructure.SQLite", "MigracoesDaTelemetria");
        var nomes = Directory.EnumerateFiles(pasta, "*.sql").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(nomes);
        Assert.All(nomes, n => Assert.Matches(NomeDeMigracaoDaTelemetria(), n!));
        Assert.StartsWith("T001_", nomes[0], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ NOVO-CTR-IA-01

    /// <summary>
    /// <c>NOVO-CTR-IA-01</c> (docs/36-anexos/02 §3.6): o contrato estendido pela camada continua
    /// sem palavra proibida — inclusive as três armadilhas: "imagem" contém <c>image</c>,
    /// "discard"/"scorecard" contêm <c>card</c>, e "credencial" não vira nome de campo.
    /// </summary>
    [Theory]
    [MemberData(nameof(MensagensDaCamada))]
    public void NOVO_CTR_IA_01_mensagem_da_camada_nao_tem_palavra_proibida(string mensagem)
    {
        var corpo = CorpoDaMensagem(mensagem);

        foreach (var proibida in new[] { "cartao", "card", "credencial", "senha", "password", "template", "foto", "image", "nome_do_titular", "pessoa" })
        {
            Assert.DoesNotContain(proibida, corpo, StringComparison.OrdinalIgnoreCase);
        }

        // Nenhuma devolve código, máscara, impressão HMAC nem ticket_id: a tela não precisa deles.
        foreach (var proibida in new[] { "codigo", "mascar", "impressao", "hmac", "ticket" })
        {
            Assert.DoesNotContain(proibida, corpo, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>As mensagens que a camada inteligente acrescentou ao contrato.</summary>
    public static TheoryData<string> MensagensDaCamada() => ["SaudeDoAnalisador"];

    /// <summary>O Diagnóstico leva a saúde do Analisador num número de campo novo.</summary>
    [Fact]
    public void NOVO_CTR_IA_01_o_diagnostico_ganha_a_saude_do_analisador_num_numero_novo()
    {
        var corpo = CorpoDaMensagem("Diagnostico");

        Assert.Contains("string versao = 1;", corpo, StringComparison.Ordinal);
        Assert.Contains("string pasta_de_dados = 2;", corpo, StringComparison.Ordinal);
        Assert.Contains("repeated DiagnosticoDeWorker workers = 3;", corpo, StringComparison.Ordinal);
        Assert.Contains("SaudeDoAnalisador analisador = 4;", corpo, StringComparison.Ordinal);
    }

    private static string CorpoDaMensagem(string nome)
    {
        var proto = Arquivo("src", "Contracts", "Protos", "edge_control.proto");
        var m = Regex.Match(proto, $@"message {nome} \{{(?<corpo>[^}}]*)\}}", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(m.Success, $"Mensagem {nome} não encontrada no contrato.");

        // Comentários contam: um comentário com palavra proibida também reprova o teste do contrato.
        return m.Groups["corpo"].Value;
    }

    [GeneratedRegex(@"\b(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|REPLACE)\s")]
    private static partial Regex InstrucaoSql();

    [GeneratedRegex(@"\b(INSERT(\s+OR\s+\w+)?\s+INTO|UPDATE|DELETE\s+FROM|REPLACE\s+INTO|CREATE\s+TABLE(\s+IF\s+NOT\s+EXISTS)?|CREATE\s+INDEX\s+\w+\s+ON|ALTER\s+TABLE|DROP\s+TABLE)\s+(?<tabela>\w+)")]
    private static partial Regex Escrita();

    [GeneratedRegex(@"^T\d{3}_[a-z0-9_]+\.sql$")]
    private static partial Regex NomeDeMigracaoDaTelemetria();
}
