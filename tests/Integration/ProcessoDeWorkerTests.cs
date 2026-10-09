using System.Diagnostics;
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

    /// <summary>
    /// Achado E2-01 do docs/41: um worker vivo mas travado (a DLL presa numa chamada) contava como
    /// saudável, porque saudável era só "processo vivo", e nunca era reiniciado. Aqui a cobaia fica de
    /// pé sem gravar nada na base, como um worker travado; passada a tolerância, a supervisão a mata e
    /// sobe outra.
    /// </summary>
    [Fact]
    public void Worker_vivo_que_para_de_dar_noticia_na_base_e_morto_e_reiniciado()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.Zero);
        DateTimeOffset? noticia = null;

        using var worker = new ProcessoDeWorker(
            "travado", 3570, [1], ExecutavelDaCobaia(),
            relogio: () => agora,
            argumentosExtras: ["--esperar"],
            ultimaNoticia: _ => noticia,
            toleranciaDoBatimento: TimeSpan.FromSeconds(90));
        var supervisor = new WorkerSupervisor([worker], () => agora);

        try
        {
            supervisor.Iniciar();
            Assert.True(worker.EstaVivo);

            // Logo depois de subir, ainda sem notícia: dentro da tolerância, saudável.
            agora += TimeSpan.FromSeconds(60);
            Assert.True(worker.EstaSaudavel);

            // Dando notícia, segue saudável mesmo depois da tolerância contada da subida.
            noticia = agora;
            agora += TimeSpan.FromSeconds(60);
            Assert.True(worker.EstaSaudavel);

            // Silêncio na base acima da tolerância: travado.
            agora += TimeSpan.FromSeconds(31);
            Assert.False(worker.EstaSaudavel);
            Assert.Equal(SituacaoDoWorker.SemBatimento, supervisor.Situacao(worker));
            Assert.Contains("sem notícia na base há 91s", worker.Diagnostico, StringComparison.Ordinal);

            var acao = Assert.Single(supervisor.Supervisionar());
            Assert.Contains("laço travado", acao.Acao, StringComparison.Ordinal);
            Assert.Equal(1, supervisor.Reinicios("travado"));
            Assert.True(worker.EstaVivo);

            // O processo novo conta a tolerância da subida dele, não da notícia velha.
            Assert.True(worker.EstaSaudavel);
        }
        finally
        {
            worker.Matar();
        }
    }

    /// <summary>Base ocupada na hora de ler o batimento não é silêncio: não mata ninguém.</summary>
    [Fact]
    public void Base_ocupada_ao_ler_o_batimento_nao_conta_como_worker_travado()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.Zero);

        using var worker = new ProcessoDeWorker(
            "ocupado", 3571, [2], ExecutavelDaCobaia(),
            relogio: () => agora,
            argumentosExtras: ["--esperar"],
            ultimaNoticia: _ => throw new InvalidOperationException("database is locked"));

        try
        {
            worker.Iniciar();
            agora += TimeSpan.FromMinutes(10);

            Assert.True(worker.EstaSaudavel);
        }
        finally
        {
            worker.Matar();
        }
    }

    /// <summary>
    /// O aviso de saída de um worker antigo chega depois de o novo já ter subido (Matar + Iniciar na
    /// mesma ronda). Antes, o aviso lia o ExitCode do processo novo, ainda vivo, e a
    /// InvalidOperationException numa thread do pool derrubava o serviço (visto no CI Linux, 09/10).
    /// </summary>
    [Fact]
    public void Aviso_de_saida_do_worker_antigo_nao_derruba_nem_sobrescreve_o_novo()
    {
        using var worker = new ProcessoDeWorker("reinicio", 3572, [3], ExecutavelDaCobaia(), argumentosExtras: ["--esperar"]);
        using var antigo = Process.Start(new ProcessStartInfo(ExecutavelDaCobaia(), "--esperar") { UseShellExecute = false })!;

        try
        {
            worker.Iniciar();

            // O antigo ainda está vivo: ler o código dele lançaria; o aviso não pode lançar.
            var registro = Record.Exception(() =>
            {
                antigo.Kill();
                worker.RegistrarSaida(antigo);
            });

            Assert.Null(registro);
            Assert.True(worker.EstaVivo);
            Assert.DoesNotContain("encerrou", worker.Diagnostico, StringComparison.Ordinal);
        }
        finally
        {
            worker.Matar();
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
