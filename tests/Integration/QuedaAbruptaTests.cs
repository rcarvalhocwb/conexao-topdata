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

        var erros = new System.Text.StringBuilder();
        Assert.True(processo.Start(), "não foi possível iniciar o processo-cobaia");
        LerErros(processo, erros);

        // Espera o sinal de que os acessos já foram commitados.
        var pronto = await EsperarPeloSinalAsync(processo, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
        Assert.True(pronto, $"o processo-cobaia não sinalizou que terminou de gravar{Situacao(processo, erros)}");

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

            var erros = new System.Text.StringBuilder();
            processo.Start();
            LerErros(processo, erros);
            Assert.True(
                await EsperarPeloSinalAsync(processo, TimeSpan.FromMinutes(2)).ConfigureAwait(true),
                $"tentativa {tentativa}: o processo-cobaia não sinalizou{Situacao(processo, erros)}");

            processo.Kill(entireProcessTree: true);
            await processo.WaitForExitAsync().ConfigureAwait(true);
        }

        var fabrica = new SqliteConnectionFactory(banco.Caminho);

        Assert.Equal("ok", fabrica.VerificarIntegridade());
        Assert.Equal(QuantidadeDeAcessos, new AccessJournal(fabrica).ContarEventos("catraca-08"));
        Assert.Equal(QuantidadeDeAcessos, ContarOutbox(fabrica));
    }

    /// <summary>
    /// Achado E9-1 do docs/41: os dois testes acima exercitam o <c>AccessJournal</c>, que a produção
    /// não usa. Este mata, no meio das gravações, a cobaia que passa ingressos pelo caminho real
    /// (<c>TentarUsar</c> e <c>ConfirmarPassagemFisica</c>, com o espelho da nuvem ligado), e confere
    /// que nada confirmado se perdeu e nada ficou pela metade: cada uso consumido tem exatamente uma
    /// tentativa e um item na outbox, e nenhum ingresso foi usado duas vezes.
    /// </summary>
    [Fact]
    public async Task No_caminho_real_o_kill_no_meio_nao_perde_passagem_confirmada_nem_deixa_uso_pela_metade()
    {
        const int Total = 400;
        using var banco = new BancoTemporario();
        var cobaia = LocalizarCobaia();

        using var processo = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { cobaia, "--ingressos", banco.Caminho, Total.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        var erros = new System.Text.StringBuilder();
        Assert.True(processo.Start(), "não foi possível iniciar o processo-cobaia");
        LerErros(processo, erros);

        // Mata assim que a cobaia confirmar a 60ª passagem: ela segue gravando enquanto o Kill chega,
        // então o corte cai no meio de uma gravação qualquer.
        var confirmadas = await EsperarConfirmacoesAsync(processo, 60, TimeSpan.FromMinutes(2)).ConfigureAwait(true);
        Assert.True(confirmadas >= 60, $"a cobaia confirmou só {confirmadas}{Situacao(processo, erros)}");
        processo.Kill(entireProcessTree: true);
        await processo.WaitForExitAsync().ConfigureAwait(true);

        var fabrica = new SqliteConnectionFactory(banco.Caminho);
        Assert.Equal("ok", await IntegridadeDepoisDaQuedaAsync(fabrica).ConfigureAwait(true));

        using var conexao = fabrica.Abrir();
        long Contar(string sql) => SqliteConnectionFactory.Escalar<long>(conexao, sql);

        var usados = Contar("SELECT COUNT(*) FROM ticket WHERE used_count > 0;");
        var tentativas = Contar("SELECT COUNT(*) FROM ticket_use_attempt WHERE outcome = 'consumido';");
        var comGiro = Contar("SELECT COUNT(*) FROM ticket_use_attempt WHERE outcome = 'consumido' AND passage_confirmed_at IS NOT NULL;");
        var naOutbox = Contar("SELECT COUNT(*) FROM outbox WHERE connector = 'painel-tentativas';");

        // Tudo o que a cobaia confirmou antes do Kill está na base, com o giro.
        Assert.True(comGiro >= confirmadas, $"{comGiro} passagens com giro na base, {confirmadas} confirmadas antes do Kill");

        // Nada pela metade: cada uso consumido tem a sua tentativa, e o espelho acompanhou.
        Assert.Equal(usados, tentativas);
        Assert.Equal(tentativas, naOutbox);
        Assert.Equal(0L, Contar("SELECT COUNT(*) FROM ticket WHERE used_count > 1;"));
        Assert.Equal(0L, Contar(
            """
            SELECT COUNT(*) FROM ticket t
            WHERE t.used_count <> (SELECT COUNT(*) FROM ticket_use_attempt a WHERE a.ticket_id = t.id AND a.outcome = 'consumido');
            """));

        // E no máximo uma passagem ficou sem o giro gravado: a que estava entre as duas gravações.
        Assert.InRange(tentativas - comGiro, 0, 1);
    }

    /// <summary>
    /// A integridade da base logo depois do Kill no meio de uma gravação.
    /// </summary>
    /// <remarks>
    /// No Windows, as travas de arquivo de um processo morto são soltas pelo sistema "num tempo que
    /// depende dos recursos disponíveis" (documentação do LockFileEx). Abrir no mesmo instante pode dar
    /// SQLITE_IOERR ou SQLITE_BUSY enquanto o sistema solta as travas do -shm. Tenta por até 15 s, só
    /// nesses dois erros; qualquer outro, ou a base não abrir depois disso, reprova.
    /// </remarks>
    private static async Task<string> IntegridadeDepoisDaQuedaAsync(SqliteConnectionFactory fabrica)
    {
        var limite = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return fabrica.VerificarIntegridade();
            }
            catch (Microsoft.Data.Sqlite.SqliteException erro)
                when (erro.SqliteErrorCode is 10 or 5 && limite.Elapsed < TimeSpan.FromSeconds(15))
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> EsperarConfirmacoesAsync(Process processo, int alvo, TimeSpan limite)
    {
        using var cancelamento = new CancellationTokenSource(limite);
        var ultima = 0;
        try
        {
            while (await processo.StandardOutput.ReadLineAsync(cancelamento.Token).ConfigureAwait(false) is { } linha)
            {
                if (linha.StartsWith("OK ", StringComparison.Ordinal)
                    && int.TryParse(linha.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    ultima = n;
                    if (n >= alvo)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Estourou o tempo: devolve o que viu e o teste reprova com mensagem própria.
        }

        return ultima;
    }

    /// <summary>
    /// Esvazia a saída de erro enquanto o processo roda. Redirecionada e nunca lida, ela
    /// enche o pipe e trava a cobaia antes do "PRONTO" — o teste reprovava por tempo sem
    /// dizer por quê.
    /// </summary>
    private static void LerErros(Process processo, System.Text.StringBuilder erros)
    {
        processo.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } linha)
            {
                lock (erros)
                {
                    erros.AppendLine(linha);
                }
            }
        };
        processo.BeginErrorReadLine();
    }

    private static string Situacao(Process processo, System.Text.StringBuilder erros)
    {
        string texto;
        lock (erros)
        {
            texto = erros.ToString();
        }

        var saiu = processo.HasExited ? $" (saiu com código {processo.ExitCode})" : " (ainda rodando)";
        return string.IsNullOrWhiteSpace(texto) ? saiu : $"{saiu}; erros: {texto[^Math.Min(texto.Length, 2000)..]}";
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
