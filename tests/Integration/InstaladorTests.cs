using System.Text.RegularExpressions;
using Edge.Worker;

namespace Integration.Tests;

/// <summary>
/// Trava o instalador de desenvolvimento contra o código.
/// </summary>
/// <remarks>
/// <para>
/// Script de instalação é o tipo de artefato que apodrece calado: ninguém roda até o dia
/// em que precisa, e aí ele reclama de uma coisa que o worker já não confere mais, ou
/// deixa de reclamar do que importa. Estes testes não executam PowerShell — a máquina de
/// build é Linux — mas conferem as propriedades que, se quebradas, só apareceriam na
/// bancada.
/// </para>
/// <para>
/// O que <b>não</b> está coberto aqui e só a bancada responde: se o script de fato roda
/// no PowerShell do Windows, se o ACL aplicado ao token é aceito pela máquina alvo e se o
/// <c>dotnet publish</c> com <c>win-x86</c> produz um executável que carrega a
/// EasyInner.dll (ensaio HIL-STACK-01, ver docs/12-decisao-de-stack.md).
/// </para>
/// </remarks>
public sealed class InstaladorTests
{
    private static readonly string PastaDoInstalador =
        Path.Combine(LocalizarRaiz(), "installer");

    [Theory]
    [InlineData("README.md")]
    [InlineData("publicar.ps1")]
    [InlineData("verificar-ambiente.ps1")]
    [InlineData("instalar-dev.ps1")]
    public void O_instalador_esta_completo(string arquivo)
    {
        var caminho = Path.Combine(PastaDoInstalador, arquivo);
        Assert.True(File.Exists(caminho), $"Faltando: {caminho}");
    }

    /// <summary>
    /// O script e o worker precisam reclamar das mesmas coisas, com os mesmos nomes.
    /// </summary>
    /// <remarks>
    /// Se divergirem, o operador conferiu o ambiente, viu "ok" e mesmo assim tomou
    /// retorno 8 — o pior resultado possível, porque destrói a confiança na checagem.
    /// </remarks>
    [Fact]
    public void O_verificador_de_ambiente_confere_os_mesmos_prerequisitos_do_worker()
    {
        var noCodigo = VerificadorDePreRequisitos.Verificar()
            .Select(p => p.Id)
            .ToHashSet(StringComparer.Ordinal);

        var noScript = IdsConferidosPeloScript();

        Assert.Equal(noCodigo.OrderBy(i => i, StringComparer.Ordinal), noScript.OrderBy(i => i, StringComparer.Ordinal));
    }

    /// <summary>
    /// A DLL é de 32 bits. Publicar o worker em x64 é uma falha silenciosa até a primeira
    /// chamada nativa.
    /// </summary>
    [Fact]
    public void O_worker_e_publicado_em_x86()
    {
        var script = File.ReadAllText(Path.Combine(PastaDoInstalador, "publicar.ps1"));

        var linhaDoWorker = script
            .Split('\n')
            .Single(l => l.Contains("Edge.Worker.X86", StringComparison.Ordinal) && l.Contains("Rid", StringComparison.Ordinal));

        Assert.Contains("win-x86", linhaDoWorker, StringComparison.Ordinal);
    }

    /// <summary>
    /// Todo componente que o instalador publica precisa gerar executável.
    /// </summary>
    /// <remarks>
    /// Escrito depois de um defeito real: <c>publicar.ps1</c> listava o Edge.Supervisor, que
    /// é biblioteca e não tem <c>OutputType</c>. A publicação "funcionava" e não produzia
    /// <c>.exe</c> nenhum, enquanto <c>instalar-dev.ps1</c> mandava executar
    /// <c>Edge.Supervisor.exe</c>. Só se descobre na bancada, tentando rodar.
    /// </remarks>
    [Fact]
    public void Tudo_que_o_instalador_publica_gera_executavel()
    {
        var script = File.ReadAllText(Path.Combine(PastaDoInstalador, "publicar.ps1"));

        var projetos = Regex
            .Matches(script, @"Projeto\s*=\s*'([^']+)'", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(projetos);

        var bibliotecas = new List<string>();

        foreach (var projeto in projetos)
        {
            var nome = projeto.Split('/')[^1];
            var csproj = Path.Combine(LocalizarRaiz(), projeto.Replace('/', Path.DirectorySeparatorChar), $"{nome}.csproj");

            Assert.True(File.Exists(csproj), $"projeto citado pelo instalador não existe: {csproj}");

            var conteudo = File.ReadAllText(csproj);

            if (!conteudo.Contains("<OutputType>Exe</OutputType>", StringComparison.Ordinal) &&
                !conteudo.Contains("<OutputType>WinExe</OutputType>", StringComparison.Ordinal))
            {
                bibliotecas.Add(nome);
            }
        }

        Assert.True(
            bibliotecas.Count == 0,
            "O instalador publica projeto que não gera executável: " +
            string.Join(", ", bibliotecas) +
            ". A publicação passaria sem produzir .exe, e a instrução de execução apontaria " +
            "para um arquivo inexistente.");
    }

    /// <summary>O token de sessão não pode chegar ao console nem ao log.</summary>
    /// <remarks>
    /// Console de instalação costuma ser copiado para chamado, print ou log de terminal.
    /// Um token impresso vale tanto quanto um token vazado.
    /// </remarks>
    [Fact]
    public void O_instalador_nunca_imprime_o_token()
    {
        var linhas = File.ReadAllLines(Path.Combine(PastaDoInstalador, "instalar-dev.ps1"));

        var vazamentos = linhas
            .Select((texto, numero) => (texto, numero: numero + 1))
            .Where(l => Regex.IsMatch(l.texto, @"(Write-Host|Write-Output|Write-Information|echo)\b", RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Where(l => Regex.IsMatch(l.texto, @"\$token\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            .ToList();

        Assert.True(
            vazamentos.Count == 0,
            "O token de sessão está sendo impresso em: " +
            string.Join(", ", vazamentos.Select(v => $"linha {v.numero}")));
    }

    /// <summary><c>Get-Random</c> não é fonte criptográfica.</summary>
    [Fact]
    public void O_token_vem_de_fonte_criptografica()
    {
        var script = File.ReadAllText(Path.Combine(PastaDoInstalador, "instalar-dev.ps1"));

        Assert.Contains("RandomNumberGenerator", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Random", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// O instalador precisa dizer, na cara, o que ainda não foi provado em hardware.
    /// </summary>
    [Fact]
    public void O_README_aponta_os_ensaios_pendentes_antes_do_hardware()
    {
        var texto = File.ReadAllText(Path.Combine(PastaDoInstalador, "README.md"));

        Assert.Contains("HIL-STACK-01", texto, StringComparison.Ordinal);
        Assert.Contains("EasyInner.cs", texto, StringComparison.Ordinal);
    }

    private static HashSet<string> IdsConferidosPeloScript()
    {
        var script = File.ReadAllText(Path.Combine(PastaDoInstalador, "verificar-ambiente.ps1"));

        var achados = Regex
            .Matches(script, @"Conferir\s+'([A-Z0-9_]+)'", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(achados);
        return achados;
    }

    private static string LocalizarRaiz()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        return raiz.FullName;
    }
}
