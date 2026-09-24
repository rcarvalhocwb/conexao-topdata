using System.Xml.Linq;
using Access.Domain.Devices;

namespace Contract.Tests;

/// <summary>
/// ARCH-01 — impede que a EasyInner.dll vaze para fora do worker.
/// </summary>
/// <remarks>
/// <para>
/// O isolamento da DLL em processo x86 separado só vale se nada mais a alcançar. Sem
/// este teste, a regra apodrece no primeiro prazo apertado.
/// Ver docs/ADR/ADR-0001-isolar-easyinner-em-processo-x86.md
/// </para>
/// <para>
/// A verificação é feita sobre os arquivos <c>.csproj</c>, não sobre os assemblies
/// compilados: assim ela também cobre os projetos que só compilam no Windows
/// (worker x86 e aplicativo WPF), que não são construídos na CI Linux.
/// </para>
/// </remarks>
public sealed class ArquiteturaTests
{
    /// <summary>Projetos que ninguém, além do worker, pode referenciar.</summary>
    private static readonly string[] ProjetosDeInterop =
    [
        "Topdata.EasyInner.Interop",
        "Topdata.EasyInner.Adapter",
    ];

    /// <summary>Quem tem permissão de tocar no interop da DLL.</summary>
    private static readonly string[] PodemUsarInterop =
    [
        "Edge.Worker.X86",
        "Topdata.EasyInner.Adapter",
        "HardwareInLoop.Tests",
    ];

    private static List<(string Nome, string Caminho)> Projetos() =>
        Directory
            .EnumerateFiles(RepositorioDeMatriz.RaizDoRepositorio, "*.csproj", SearchOption.AllDirectories)
            .Where(c => !c.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(c => (Nome: Path.GetFileNameWithoutExtension(c), Caminho: c))
            .OrderBy(p => p.Nome, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> ReferenciasDe(string caminhoDoProjeto) =>
        XDocument.Load(caminhoDoProjeto)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')));

    [Fact]
    public void Somente_o_worker_referencia_o_interop_da_dll()
    {
        var violacoes = new List<string>();

        foreach (var (nome, caminho) in Projetos())
        {
            if (PodemUsarInterop.Contains(nome, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var referencia in ReferenciasDe(caminho))
            {
                if (ProjetosDeInterop.Contains(referencia, StringComparer.Ordinal))
                {
                    violacoes.Add($"{nome} -> {referencia}");
                }
            }
        }

        Assert.True(
            violacoes.Count == 0,
            "Projetos referenciando o interop da EasyInner sem autorização: " +
            string.Join("; ", violacoes) +
            ". Ver docs/ADR/ADR-0001.");
    }

    /// <summary>
    /// O domínio não referencia projeto nenhum. É isso que permite rodar as regras de
    /// acesso em CI Linux, sem Windows e sem hardware.
    /// </summary>
    [Fact]
    public void O_dominio_nao_referencia_nada()
    {
        var projeto = Projetos().Single(p => p.Nome == "Access.Domain");

        var referencias = ReferenciasDe(projeto.Caminho).ToList();

        Assert.True(
            referencias.Count == 0,
            $"Access.Domain passou a referenciar: {string.Join(", ", referencias)}. " +
            "O domínio precisa continuar independente de infraestrutura.");
    }

    /// <summary>
    /// Complementa a verificação por .csproj: no assembly já compilado, o domínio só
    /// pode depender da biblioteca padrão.
    /// </summary>
    [Fact]
    public void O_assembly_do_dominio_so_depende_da_biblioteca_padrao()
    {
        var permitidos = new[] { "System", "netstandard", "mscorlib" };

        var externas = typeof(EventOrigin).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !permitidos.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            externas.Count == 0,
            $"Access.Domain passou a depender de: {string.Join(", ", externas)}");
    }

    /// <summary>
    /// A interface gráfica conversa com o serviço por IPC. Se ela alcançar o domínio de
    /// aplicação ou o interop diretamente, o processo x64 passa a carregar o que
    /// deveria estar isolado no worker.
    /// </summary>
    [Fact]
    public void A_interface_grafica_so_conhece_os_contratos()
    {
        var projeto = Projetos().FirstOrDefault(p => p.Nome == "Desktop.App");
        if (projeto.Caminho is null)
        {
            return; // Ainda não criado nesta fase.
        }

        var permitidas = new[] { "Contracts", "Shared.Observability" };

        var indevidas = ReferenciasDe(projeto.Caminho)
            .Where(r => !permitidas.Contains(r, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            indevidas.Count == 0,
            $"Desktop.App referencia indevidamente: {string.Join(", ", indevidas)}");
    }

    [Fact]
    public void Nenhum_projeto_de_producao_referencia_projeto_de_teste()
    {
        var violacoes = new List<string>();

        foreach (var (nome, caminho) in Projetos())
        {
            if (nome.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var referencia in ReferenciasDe(caminho).Where(r => r.EndsWith(".Tests", StringComparison.Ordinal)))
            {
                violacoes.Add($"{nome} -> {referencia}");
            }
        }

        Assert.True(violacoes.Count == 0, $"Produção referenciando teste: {string.Join("; ", violacoes)}");
    }
}
