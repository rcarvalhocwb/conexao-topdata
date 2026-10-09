namespace Integration.Tests;

/// <summary>
/// Teste que só tem sentido no Windows (Job Object, ACL do pipe, Toolhelp32). Fora dele aparece como
/// "ignorado", com o motivo, e não como "passou" (achado E9-8 do docs/41: o retorno silencioso contava
/// como aprovado no Linux, sem provar nada).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactSoNoWindowsAttribute : FactAttribute
{
    public FactSoNoWindowsAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Só roda no Windows; o CI Windows executa e publica o resultado.";
        }
    }
}
