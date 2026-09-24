using System.Diagnostics;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// CHAOS-KILL-01 — nenhum acesso confirmado localmente se perde num corte abrupto.
/// </summary>
/// <remarks>
/// O processo é morto de verdade, com SIGKILL: sem finalizadores, sem flush de saída,
/// sem despedida. Simular a queda dentro do próprio teste provaria outra coisa.
/// Critério CA-02 de docs/00-entendimento-e-escopo.md.
/// </remarks>
public sealed class QuedaAbruptaTests
{
    private const int QuantidadeDeAcessos = 200;

    [Fact]
    public async Task Nenhum_acesso_se_perde_quando_o_processo_leva_kill()
    {
        using var banco = new BancoTemporario();
        var cobaia = LocalizarCobaia();

        using var processo = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { cobaia, banco.Caminho, QuantidadeDeAcessos.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        Assert.True(processo.Start(), "não foi possível iniciar o processo-cobaia");

        // Espera o sinal de que os acessos já foram commitados.
        var pronto = await EsperarPeloSinalAsync(processo, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
        Assert.True(pronto, "o processo-cobaia não sinalizou que terminou de gravar");

        // SIGKILL. Não há chance de limpeza.
        processo.Kill(entireProcessTree: true);
        await processo.WaitForExitAsync().ConfigureAwait(true);

        // Abre do zero, como faria o supervisor ao reiniciar o worker.
        var fabrica = new SqliteConnectionFactory(banco.Caminho);
        var diario = new AccessJournal(fabrica);

        Assert.Equal("ok", fabrica.VerificarIntegridade());
        Assert.Equal(QuantidadeDeAcessos, diario.ContarEventos("catraca-08"));

        // A outbox precisa ter acompanhado: é a mesma transação.
        Assert.Equal(QuantidadeDeAcessos, ContarOutbox(fabrica));
        Assert.Equal(QuantidadeDeAcessos, ContarDecisoes(fabrica));
    }

    /// <summary>
    /// Depois da queda, o worker reinicia e reprocessa os eventos que já tinha lido.
    /// Nada pode duplicar.
    /// </summary>
    [Fact]
    public async Task Reprocessar_apos_a_queda_nao_duplica_acessos()
    {
        using var banco = new BancoTemporario();
        var cobaia = LocalizarCobaia();

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            using var processo = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    ArgumentList = { cobaia, banco.Caminho, QuantidadeDeAcessos.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };

            processo.Start();
            Assert.True(
                await EsperarPeloSinalAsync(processo, TimeSpan.FromMinutes(2)).ConfigureAwait(true),
                $"tentativa {tentativa}: o processo-cobaia não sinalizou");

            processo.Kill(entireProcessTree: true);
            await processo.WaitForExitAsync().ConfigureAwait(true);
        }

        var fabrica = new SqliteConnectionFactory(banco.Caminho);

        Assert.Equal("ok", fabrica.VerificarIntegridade());
        Assert.Equal(QuantidadeDeAcessos, new AccessJournal(fabrica).ContarEventos("catraca-08"));
        Assert.Equal(QuantidadeDeAcessos, ContarOutbox(fabrica));
    }

    private static async Task<bool> EsperarPeloSinalAsync(Process processo, TimeSpan limite)
    {
        using var cancelamento = new CancellationTokenSource(limite);
        try
        {
            while (await processo.StandardOutput.ReadLineAsync(cancelamento.Token).ConfigureAwait(false) is { } linha)
            {
                if (linha.Contains("PRONTO", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Estourou o tempo: devolve falso e o teste reprova com mensagem própria.
        }

        return false;
    }

    private static long ContarOutbox(SqliteConnectionFactory fabrica)
    {
        using var conexao = fabrica.Abrir();
        return SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COUNT(*) FROM outbox;");
    }

    private static long ContarDecisoes(SqliteConnectionFactory fabrica)
    {
        using var conexao = fabrica.Abrir();
        return SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COUNT(*) FROM access_decision;");
    }

    private static string LocalizarCobaia()
    {
        // O CrashProbe é construído junto, pela referência no .csproj.
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        var raiz = baseDir;
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);

        var pastaBin = Path.Combine(raiz.FullName, "tests", "CrashProbe", "bin");
        Assert.True(Directory.Exists(pastaBin), $"CrashProbe ainda não foi construído: {pastaBin} não existe.");

        // Só serve o binário real: o assembly de referência em obj/ref não roda, e um
        // executável sem runtimeconfig.json seria tratado como self-contained.
        var candidatos = Directory
            .EnumerateFiles(pastaBin, "CrashProbe.dll", SearchOption.AllDirectories)
            .Where(c => File.Exists(Path.ChangeExtension(c, ".runtimeconfig.json")))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        Assert.True(candidatos.Count > 0, $"CrashProbe.dll executável não encontrado em {pastaBin}.");
        return candidatos[0];
    }
}
