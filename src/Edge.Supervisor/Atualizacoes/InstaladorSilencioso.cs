using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Edge.Supervisor.Atualizacoes;

/// <summary>Quem roda o MSI baixado. No Windows, uma tarefa agendada como SYSTEM; nos testes, um dublê.</summary>
public interface IInstaladorDoWindows
{
    /// <summary>Manda instalar o MSI em silêncio, fora do processo do serviço. Lança se não conseguiu agendar.</summary>
    void Instalar(string msi, string pastaDeTrabalho, Version versao);
}

/// <summary>
/// Roda <c>msiexec /i … /qn /norestart</c> por uma tarefa agendada do Windows, como SYSTEM (docs/60 §2.3).
/// </summary>
/// <remarks>
/// <para>
/// Não pode ser um processo filho do serviço: o MSI para o serviço no meio da instalação. A tarefa agendada
/// roda à parte, num script em <c>instalar.cmd</c> (o <c>/TR</c> do <c>schtasks</c> não comporta a linha
/// toda), grava o código de saída e o registro do <c>msiexec</c>, e apaga a si mesma.
/// </para>
/// <para>
/// O MSI já fecha o painel e o assistente e sobe o serviço de novo numa atualização (ConexaoTopdata.wxs).
/// </para>
/// </remarks>
public sealed class InstaladorPorTarefaAgendada : IInstaladorDoWindows
{
    /// <summary>O nome da tarefa agendada.</summary>
    public const string NomeDaTarefa = "Rayzer XAcess - atualizacao";

    /// <inheritdoc />
    public void Instalar(string msi, string pastaDeTrabalho, Version versao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msi);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDeTrabalho);
        ArgumentNullException.ThrowIfNull(versao);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("A instalação automática só roda no Windows.");
        }

        Directory.CreateDirectory(pastaDeTrabalho);
        var script = Path.Combine(pastaDeTrabalho, "instalar.cmd");
        File.WriteAllText(script, Script(msi, pastaDeTrabalho, versao), Encoding.ASCII);

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var schtasks = Path.Combine(system32, "schtasks.exe");

        // Uma vez, numa data passada: só roda pelo /Run logo abaixo. /F troca uma tarefa velha do mesmo nome.
        Rodar(schtasks, $"/Create /TN \"{NomeDaTarefa}\" /RU SYSTEM /RL HIGHEST /SC ONCE /SD 01/01/2000 /ST 00:00 /F /TR \"\\\"{script}\\\"\"");
        Rodar(schtasks, $"/Run /TN \"{NomeDaTarefa}\"");
    }

    /// <summary>
    /// O script que a tarefa roda. Os caminhos vêm de nós (pasta de dados do serviço), nunca do usuário nem da
    /// rede: o nome do MSI passou pela ficha assinada e não tem barra nem dois-pontos.
    /// </summary>
    public static string Script(string msi, string pastaDeTrabalho, Version versao)
    {
        ArgumentNullException.ThrowIfNull(versao);
        var registro = Path.Combine(pastaDeTrabalho, "msiexec.log");
        var resultado = Path.Combine(pastaDeTrabalho, "resultado.txt");
        var msiexec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");

        var texto = new StringBuilder();
        texto.AppendLine("@echo off");
        texto.AppendLine(CultureInfo.InvariantCulture, $"rem Rayzer XAcess: atualização automática para {versao} (docs/60).");
        texto.AppendLine(CultureInfo.InvariantCulture, $"\"{msiexec}\" /i \"{msi}\" /qn /norestart /l*v \"{registro}\"");
        texto.AppendLine(CultureInfo.InvariantCulture, $"echo %ERRORLEVEL% > \"{resultado}\"");
        texto.AppendLine(CultureInfo.InvariantCulture, $"schtasks /Delete /TN \"{NomeDaTarefa}\" /F > nul 2>&1");
        return texto.ToString();
    }

    private static void Rodar(string programa, string argumentos)
    {
        using var processo = Process.Start(new ProcessStartInfo(programa, argumentos)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException($"não foi possível rodar {Path.GetFileName(programa)}");

        processo.StandardOutput.ReadToEnd();
        var erro = processo.StandardError.ReadToEnd();
        if (!processo.WaitForExit(30_000) || processo.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(programa)} falhou: {erro.Trim()}");
        }
    }
}
