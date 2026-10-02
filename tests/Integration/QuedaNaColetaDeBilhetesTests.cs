using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Access.Domain.Credentials;
using Access.Infrastructure.SQLite;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Etapa A.9 do docs/35 (CHAOS-REC-01 simulado): o worker morre com SIGKILL entre a coleta e a
/// gravação de um bilhete, ou entre a gravação e o próximo pedido, e volta. Nenhum bilhete
/// perdido, nenhum duplicado.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo padrão de <see cref="QuedaAbruptaTests"/>: um processo de verdade (a cobaia do
/// <c>CrashProbe</c>, modo <c>--bilhetes</c>) com o laço de verdade (<c>DevicePump</c>) e a base
/// de verdade, morto sem despedida. A catraca simulada guarda a memória num arquivo, porque a
/// catraca não morre com o worker.
/// </para>
/// <para>
/// A garantia "nenhum perdido" depende de a catraca devolver de novo o bilhete que entregou e
/// não teve confirmação — é o que o tipo 128 ("já retornado em coleta anterior", manual 5.2.2)
/// sugere, e é <c>A_CONFIRMAR_COM_TOPDATA</c> (NOVO-INT-REC-07). Se a DLL apagar o bilhete na
/// hora em que o devolve (leitura literal de FUN:40), a queda exatamente entre a coleta e a
/// gravação perde aquele bilhete e só ele: o laço nunca tem mais de um fora da base. Os dois
/// comportamentos estão aqui, cada um com o que garante.
/// </para>
/// </remarks>
public sealed class QuedaNaColetaDeBilhetesTests
{
    private const int Quantidade = 20;
    private const int PontoDaQueda = 7;

    private static readonly ImpressaoDeCodigo Impressao = new(Enumerable.Repeat((byte)7, 32).ToArray());

    [Theory]
    [InlineData("--cair-antes-de-gravar")]
    [InlineData("--cair-depois-de-gravar")]
    public async Task Queda_entre_coleta_e_gravacao_nao_perde_nem_duplica_bilhete(string pontoDaQueda)
    {
        using var banco = new BancoTemporario();
        var memoria = Path.Combine(Path.GetDirectoryName(banco.Caminho)!, "catraca-1.json");

        await RodarAteOPronto(banco.Caminho, memoria, pontoDaQueda, PontoDaQueda.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(true);
        var noMeio = Gravados(banco);
        Assert.Equal(pontoDaQueda is "--cair-antes-de-gravar" ? PontoDaQueda - 1 : PontoDaQueda, noMeio.Count);

        // O worker volta (o supervisor o reinicia) e o operador pede a coleta de novo.
        await RodarAteOPronto(banco.Caminho, memoria).ConfigureAwait(true);

        var fabrica = new SqliteConnectionFactory(banco.Caminho);
        Assert.Equal("ok", fabrica.VerificarIntegridade());

        var gravados = Gravados(banco);
        Assert.Equal(Quantidade, gravados.Count);
        Assert.Equal(
            Enumerable.Range(1, Quantidade).Select(i => Impressao.De(Codigo(i))).Order(StringComparer.Ordinal),
            gravados.Select(g => g.Impressao).Order(StringComparer.Ordinal));

        // A catraca terminou vazia: tudo o que ela tinha está na base, uma vez.
        var restante = JsonSerializer.Deserialize<MemoriaDeBilhetes>(await File.ReadAllTextAsync(memoria).ConfigureAwait(true))!;
        Assert.Empty(restante.NaMemoria);
        Assert.Null(restante.AConfirmar);

        var doPontoDaQueda = Assert.Single(gravados, g => g.Impressao == Impressao.De(Codigo(PontoDaQueda)));
        if (pontoDaQueda is "--cair-antes-de-gravar")
        {
            // O original nunca chegou à base: a cópia que a catraca devolveu (128) é que fica,
            // sem o tipo de origem (limitação registrada no docs/34 §11).
            Assert.True(doPontoDaQueda.Repetido);
        }
        else
        {
            Assert.DoesNotContain(gravados, g => g.Repetido);
        }
    }

    /// <summary>
    /// Se a catraca apagar o bilhete ao devolvê-lo (FUN:40 ao pé da letra), a queda entre coleta e
    /// gravação perde aquele um — e nunca mais de um, porque o laço não pede o próximo antes de
    /// gravar. É a razão de a chave ficar desligada até NOVO-INT-REC-07.
    /// </summary>
    [Fact]
    public async Task Se_a_catraca_apaga_ao_devolver_a_queda_expoe_no_maximo_um_bilhete()
    {
        using var banco = new BancoTemporario();
        var memoria = Path.Combine(Path.GetDirectoryName(banco.Caminho)!, "catraca-1.json");

        await RodarAteOPronto(banco.Caminho, memoria, "--cair-antes-de-gravar", PontoDaQueda.ToString(CultureInfo.InvariantCulture), "--remove-ao-devolver").ConfigureAwait(true);
        await RodarAteOPronto(banco.Caminho, memoria, "--remove-ao-devolver").ConfigureAwait(true);

        var gravados = Gravados(banco);
        Assert.Equal(Quantidade - 1, gravados.Count);
        Assert.DoesNotContain(gravados, g => g.Impressao == Impressao.De(Codigo(PontoDaQueda)));
        Assert.Equal(gravados.Count, gravados.Select(g => g.Impressao).Distinct(StringComparer.Ordinal).Count());
    }

    private static string Codigo(int i) => string.Create(CultureInfo.InvariantCulture, $"9999{i:D10}");

    private static List<RegistroDeBilhete> Gravados(BancoTemporario banco) =>
        [.. new BilhetesColetados(new SqliteConnectionFactory(banco.Caminho), Impressao).Listar(1)];

    /// <summary>Roda a cobaia até ela dizer "PRONTO" e a mata com SIGKILL.</summary>
    private static async Task RodarAteOPronto(string banco, string memoria, params string[] extras)
    {
        using var processo = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        processo.StartInfo.ArgumentList.Add(LocalizarCobaia());
        processo.StartInfo.ArgumentList.Add("--bilhetes");
        processo.StartInfo.ArgumentList.Add(banco);
        processo.StartInfo.ArgumentList.Add(memoria);
        processo.StartInfo.ArgumentList.Add(Quantidade.ToString(CultureInfo.InvariantCulture));
        foreach (var extra in extras)
        {
            processo.StartInfo.ArgumentList.Add(extra);
        }

        var erros = new StringBuilder();
        Assert.True(processo.Start(), "não foi possível iniciar o processo-cobaia");
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

        var saida = new StringBuilder();
        var pronto = false;
        using (var limite = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
        {
            try
            {
                while (await processo.StandardOutput.ReadLineAsync(limite.Token).ConfigureAwait(false) is { } linha)
                {
                    saida.AppendLine(linha);
                    if (linha.Contains("PRONTO", StringComparison.Ordinal))
                    {
                        pronto = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Estourou o tempo: reprova abaixo com o que a cobaia disse.
            }
        }

        // SIGKILL. Sem limpeza.
        processo.Kill(entireProcessTree: true);
        await processo.WaitForExitAsync().ConfigureAwait(false);

        string texto;
        lock (erros)
        {
            texto = erros.ToString();
        }

        Assert.True(pronto, $"a cobaia não sinalizou PRONTO; saída: {saida}; erros: {texto}");
    }

    private static string LocalizarCobaia()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        var pastaBin = Path.Combine(raiz.FullName, "tests", "CrashProbe", "bin");
        Assert.True(Directory.Exists(pastaBin), $"CrashProbe ainda não foi construído: {pastaBin} não existe.");

        var candidatos = Directory
            .EnumerateFiles(pastaBin, "CrashProbe.dll", SearchOption.AllDirectories)
            .Where(c => File.Exists(Path.ChangeExtension(c, ".runtimeconfig.json")))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        Assert.True(candidatos.Count > 0, $"CrashProbe.dll executável não encontrado em {pastaBin}.");
        return candidatos[0];
    }
}
