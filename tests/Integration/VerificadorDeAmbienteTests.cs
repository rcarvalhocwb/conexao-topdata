using System.Diagnostics;
using System.Text;

namespace Integration.Tests;

/// <summary>
/// Executa o verificador em estruturas temporárias, inclusive o ZIP que o CI entrega.
/// As consultas ao Windows são dublês e a DLL é vazia: isto não ensaia a EasyInner.
/// </summary>
public sealed class VerificadorDeAmbienteTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    [FactSoNoWindows]
    public async Task Encontra_o_worker_na_instalacao_no_pacote_e_no_repositorio()
    {
        foreach (var formato in new[] { "instalado", "bancada", "repositorio" })
        {
            var (script, worker) = Preparar(formato);
            CriarWorker(worker);

            var (codigo, saida) = await Executar(script);

            Assert.True(codigo == 0, $"{formato}: {saida}");
            Assert.Contains("ARQUITETURA_X86: O programa das catracas é de 32 bits", saida, StringComparison.Ordinal);
            Assert.Contains(Path.Combine(worker, "EasyInner.dll"), saida, StringComparison.Ordinal);
            Assert.DoesNotContain("não encontrado", saida, StringComparison.Ordinal);
        }
    }

    [FactSoNoWindows]
    public async Task Pacote_incompleto_aponta_para_o_worker_do_zip_e_pede_extracao_completa()
    {
        var (script, worker) = Preparar("bancada");
        // O ZIP tem o roteiro, mas perdeu a pasta do worker ao ser extraído.
        Directory.Delete(worker);

        var (codigo, saida) = await Executar(script);

        Assert.Equal(1, codigo);
        Assert.Contains(Path.Combine(worker, "Edge.Worker.X86.exe"), saida, StringComparison.Ordinal);
        Assert.Contains("Extraia novamente o pacote-da-bancada inteiro", saida, StringComparison.Ordinal);
        Assert.DoesNotContain("publicar.ps1", saida, StringComparison.Ordinal);
    }

    [FactSoNoWindows]
    public async Task Worker_x64_no_zip_e_reprovado()
    {
        var (script, worker) = Preparar("bancada");
        CriarWorker(worker, maquina: 0x8664);

        var (codigo, saida) = await Executar(script);

        Assert.Equal(1, codigo);
        Assert.Contains("máquina PE 0x8664", saida, StringComparison.Ordinal);
        Assert.Contains("ARQUITETURA_X86", saida, StringComparison.Ordinal);
        Assert.DoesNotContain("não encontrado", saida, StringComparison.Ordinal);
    }

    [FactSoNoWindows]
    public async Task Instalacao_tem_precedencia_sobre_uma_pasta_de_bancada_ao_lado()
    {
        var (script, worker) = Preparar("instalado");
        CriarWorker(worker, maquina: 0x8664);
        CriarWorker(Path.Combine(Path.GetDirectoryName(script)!, "Edge.Worker.X86"));

        var (codigo, saida) = await Executar(script);

        // Não pode aprovar outro executável enquanto o instalado continua incompatível.
        Assert.Equal(1, codigo);
        Assert.Contains("máquina PE 0x8664", saida, StringComparison.Ordinal);
        Assert.Contains("Reinstale o Rayzer XAcess pelo Setup", saida, StringComparison.Ordinal);
    }

    [FactSoNoWindows]
    public async Task Sem_sdk_o_pacote_orienta_a_obter_a_dll_sem_exigir_o_assistente()
    {
        var (script, worker) = Preparar("bancada");
        CriarWorker(worker);
        File.Delete(Path.Combine(worker, "EasyInner.dll"));

        var (codigo, saida) = await Executar(script);

        Assert.Equal(1, codigo);
        Assert.Contains("EasyInner.dll não encontrada", saida, StringComparison.Ordinal);
        Assert.Contains("O pacote público não contém o SDK", saida, StringComparison.Ordinal);
        Assert.DoesNotContain("No Assistente de configuração", saida, StringComparison.Ordinal);
    }

    private (string Script, string Worker) Preparar(string formato)
    {
        var raiz = Path.Combine(_pasta, formato);
        var pastaDoScript = formato == "repositorio" ? Path.Combine(raiz, "installer") : raiz;
        var worker = formato switch
        {
            "instalado" => Path.Combine(raiz, "Worker"),
            "bancada" => Path.Combine(raiz, "Edge.Worker.X86"),
            _ => Path.Combine(raiz, "artifacts", "Edge.Worker.X86"),
        };
        Directory.CreateDirectory(pastaDoScript);
        Directory.CreateDirectory(worker);
        var script = Path.Combine(pastaDoScript, "verificar-ambiente.ps1");
        File.Copy(Path.Combine(LocalizarRaiz(), "installer", "verificar-ambiente.ps1"), script);
        if (formato == "bancada")
        {
            File.WriteAllText(Path.Combine(raiz, "21-roteiro-da-bancada.md"), "Roteiro fictício para testar a estrutura do ZIP.");
        }

        return (script, worker);
    }

    private static void CriarWorker(string pasta, ushort maquina = 0x014C)
    {
        Directory.CreateDirectory(pasta);
        // Cabeçalho sintético: não é um executável utilizável e nunca será executado.
        using (var arquivo = File.Create(Path.Combine(pasta, "Edge.Worker.X86.exe")))
        using (var escritor = new BinaryWriter(arquivo))
        {
            escritor.Write((ushort)0x5A4D);
            arquivo.Position = 0x3C;
            escritor.Write(0x80);
            arquivo.Position = 0x80;
            escritor.Write(0x00004550);
            escritor.Write(maquina);
        }

        File.WriteAllText(Path.Combine(pasta, "EasyInner.dll"), "");
    }

    private async Task<(int Codigo, string Saida)> Executar(string script)
    {
        var wrapper = Path.Combine(Path.GetDirectoryName(script)!, "executar-teste.ps1");
        var sistemaFicticio = Path.Combine(_pasta, "windows-ficticio");
        Directory.CreateDirectory(sistemaFicticio);
        // PowerShell 5.1 exige BOM para ler os textos em português como UTF-8.
        File.WriteAllText(wrapper, """
            param([string]$Verificador, [string]$SistemaFicticio)
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
            $env:SystemRoot = $SistemaFicticio
            function Get-ItemProperty { @{ Install = 1 } }
            function Get-NetTCPConnection { @() }
            $global:LASTEXITCODE = 0
            & $Verificador -Porta 3570
            exit $LASTEXITCODE
            """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var inicio = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argumento in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", wrapper, "-Verificador", script, "-SistemaFicticio", sistemaFicticio })
        {
            inicio.ArgumentList.Add(argumento);
        }

        using var processo = Process.Start(inicio)!;
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erro = processo.StandardError.ReadToEndAsync();
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await processo.WaitForExitAsync(prazo.Token);
        }
        catch (OperationCanceledException)
        {
            processo.Kill(entireProcessTree: true);
            throw;
        }

        return (processo.ExitCode, await saida + await erro);
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

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }
}
