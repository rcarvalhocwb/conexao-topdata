namespace Edge.Supervisor.Atualizacoes;

/// <summary>
/// O canal do instalador desta máquina, que o MSI grava em <c>HKLM\Software\Rayzer\XAcess\Canal</c>
/// (docs/60 §2.2): <c>producao</c> ou <c>teste</c>. Vazio se não há (instalação anterior a esta versão,
/// ou fora do Windows): a atualização automática fica desligada, que é o lado seguro.
/// </summary>
public static class CanalDoInstalador
{
    /// <summary>A chave do registro.</summary>
    public const string Chave = @"Software\Rayzer\XAcess";

    /// <summary>O canal, ou vazio.</summary>
    public static string Ler()
    {
        if (!OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        try
        {
            using var chave = Microsoft.Win32.RegistryKey
                .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)
                .OpenSubKey(Chave);
            return (chave?.GetValue("Canal") as string)?.Trim().ToLowerInvariant() ?? string.Empty;
        }
        catch (Exception erro) when (erro is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return string.Empty;
        }
    }
}
