using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>O supervisor rodando um processo de verdade.</summary>
public sealed class ProcessoDeWorkerTests
{
    /// <summary>
    /// A saída do worker é redirecionada para o supervisor. Se ninguém a ler, o buffer do
    /// pipe enche em poucos KB e o worker trava no próximo Console.WriteLine — com a
    /// catraca parada e o processo ainda "vivo" para quem olha de fora.
    /// </summary>
    [Fact]
    public void Worker_que_escreve_muito_nao_trava_e_as_ultimas_linhas_ficam_guardadas()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        var marcador = Path.Combine(pasta, "terminou-de-escrever");

        using var worker = new ProcessoDeWorker(
            "tagarela", 3570, [1], ExecutavelDaCobaia(), argumentosExtras: ["--tagarela", marcador]);

        try
        {
            worker.Iniciar();

            var terminou = SpinWait.SpinUntil(() => File.Exists(marcador), TimeSpan.FromSeconds(60));

            Assert.True(terminou, "o worker travou escrevendo: a saída redirecionada não está sendo lida.");
            Assert.True(worker.EstaVivo);

            var linhas = worker.UltimasLinhas();
            Assert.True(linhas.Count <= ProcessoDeWorker.LinhasGuardadas);
            Assert.NotEmpty(linhas);
        }
        finally
        {
            worker.Matar();

            try
            {
                Directory.Delete(pasta, recursive: true);
            }
            catch (IOException)
            {
                // Sobra de arquivo temporário não reprova o teste.
            }
        }
    }

    private static string ExecutavelDaCobaia()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);

        var nome = OperatingSystem.IsWindows() ? "CrashProbe.exe" : "CrashProbe";
        var candidatos = Directory
            .EnumerateFiles(Path.Combine(raiz.FullName, "tests", "CrashProbe", "bin"), nome, SearchOption.AllDirectories)
            .Where(c => File.Exists(Path.Combine(Path.GetDirectoryName(c)!, "CrashProbe.runtimeconfig.json")))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        Assert.True(candidatos.Count > 0, "o executável do CrashProbe não foi construído.");
        return candidatos[0];
    }
}
