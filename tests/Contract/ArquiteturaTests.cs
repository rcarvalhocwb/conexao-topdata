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

        var permitidas = new[] { "Contracts", "Desktop.ViewModels", "Shared.Observability" };

        var indevidas = ReferenciasDe(projeto.Caminho)
            .Where(r => !permitidas.Contains(r, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            indevidas.Count == 0,
            $"Desktop.App referencia indevidamente: {string.Join(", ", indevidas)}. " +
            "A interface fala só com o serviço local, pelo IPC.");
    }

    /// <summary>
    /// As ViewModels não podem alcançar domínio, infraestrutura nem worker: é o que as
    /// mantém testáveis em qualquer plataforma e impede a interface de virar um segundo
    /// lugar onde regra de acesso mora.
    /// </summary>
    [Fact]
    public void As_viewmodels_so_conhecem_o_contrato()
    {
        var projeto = Projetos().FirstOrDefault(p => p.Nome == "Desktop.ViewModels");
        if (projeto.Caminho is null)
        {
            return;
        }

        var permitidas = new[] { "Contracts", "Shared.Observability" };

        var indevidas = ReferenciasDe(projeto.Caminho)
            .Where(r => !permitidas.Contains(r, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            indevidas.Count == 0,
            $"Desktop.ViewModels referencia indevidamente: {string.Join(", ", indevidas)}");
    }

    /// <summary>
    /// O caminho de decisão não alcança a sincronização.
    /// </summary>
    /// <remarks>
    /// A regra existe para que ninguém consiga, num prazo apertado, "só chamar a nuvem
    /// para conferir" no meio de uma decisão de acesso. Um giro de catraca nunca pode
    /// depender de rede. O que a decisão faz é gravar uma linha na outbox, na mesma
    /// transação — e quem a drena vive do outro lado desta fronteira.
    /// Ver docs/15-integracao-e-sincronizacao.md e docs/ADR/ADR-0002-local-first.md
    /// </remarks>
    [Fact]
    public void O_caminho_de_decisao_nao_alcanca_a_sincronizacao()
    {
        var violacoes = new List<string>();

        foreach (var nome in new[] { "Access.Domain", "Access.Application" })
        {
            var projeto = Projetos().FirstOrDefault(p => p.Nome == nome);
            if (projeto.Caminho is null)
            {
                continue;
            }

            violacoes.AddRange(
                ReferenciasDe(projeto.Caminho)
                    .Where(r => r.StartsWith("Sync.", StringComparison.Ordinal))
                    .Select(r => $"{nome} -> {r}"));
        }

        Assert.True(
            violacoes.Count == 0,
            $"Caminho de decisão alcançando sincronização: {string.Join("; ", violacoes)}");
    }

    /// <summary>
    /// <c>Sync.Core</c> define política pura: ordem, repetição e cartas mortas.
    /// </summary>
    /// <remarks>
    /// Sem referência nenhuma, ela é testável em milissegundos contra uma fila em
    /// memória. No dia em que passar a depender de SQLite ou de HTTP, a suíte que hoje
    /// roda em 120 ms passa a precisar de banco e de rede — e deixa de ser executada.
    /// </remarks>
    [Fact]
    public void A_politica_de_sincronizacao_nao_depende_de_infraestrutura()
    {
        var projeto = Projetos().FirstOrDefault(p => p.Nome == "Sync.Core");
        if (projeto.Caminho is null)
        {
            return;
        }

        var referencias = ReferenciasDe(projeto.Caminho).ToList();

        Assert.True(
            referencias.Count == 0,
            $"Sync.Core passou a referenciar: {string.Join(", ", referencias)}. " +
            "Ela define as portas; quem tem banco, rede e relógio implementa.");
    }

    /// <summary>
    /// Cada projeto de sincronização conhece só o que precisa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ingestão conhece o domínio, porque é dele o ingresso que ela traz. O conector
    /// HTTP conhece o contrato de saída, e mais nada. Nenhum dos dois conhece SQLite.
    /// </para>
    /// <para>
    /// O dia em que o conector alcançar o banco, deixa de ser possível trocá-lo por outro
    /// sem mexer em persistência — e o dia em que a ingestão alcançar HTTP, deixa de ser
    /// possível testar a retomada de cursor sem servidor.
    /// </para>
    /// </remarks>
    [Fact]
    public void Os_projetos_de_sincronizacao_so_conhecem_o_que_precisam()
    {
        var permitido = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Sync.Core"] = [],
            ["Sync.Ingestao"] = ["Access.Domain"],
            ["Sync.Connectors.Rest"] = ["Sync.Core"],
        };

        var violacoes = new List<string>();

        foreach (var (nome, permitidas) in permitido)
        {
            var projeto = Projetos().FirstOrDefault(p => p.Nome == nome);
            if (projeto.Caminho is null)
            {
                continue;
            }

            violacoes.AddRange(
                ReferenciasDe(projeto.Caminho)
                    .Where(r => !permitidas.Contains(r, StringComparer.Ordinal))
                    .Select(r => $"{nome} -> {r}"));
        }

        Assert.True(
            violacoes.Count == 0,
            $"Projeto de sincronização com referência indevida: {string.Join("; ", violacoes)}");
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
