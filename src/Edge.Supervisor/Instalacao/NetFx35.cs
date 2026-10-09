namespace Edge.Supervisor.Instalacao;

/// <summary>
/// Como habilitar o .NET Framework 3.5, de que a EasyInner.dll precisa (sem ele, retorno 8).
/// </summary>
/// <remarks>
/// Achado E10-8 do docs/41: o DISM rodava só pelo Windows Update. Num PC de evento sem internet o
/// recurso não era habilitado. Com a mídia do Windows (o ISO montado ou o pendrive de instalação, da
/// mesma versão do Windows do PC), a pasta <c>sources\sxs</c> tem o pacote, e o DISM instala dela sem
/// internet (<c>/Source</c> e <c>/LimitAccess</c>, documentados pela Microsoft para o NetFx3).
/// </remarks>
public static class NetFx35
{
    /// <summary>Começo do nome do pacote do .NET 3.5 na pasta <c>sources\sxs</c> da mídia do Windows.</summary>
    public const string PacoteNaMidia = "microsoft-windows-netfx3-ondemand-package";

    /// <summary>Argumentos do DISM. Sem pasta, usa o Windows Update; com pasta, só a mídia.</summary>
    /// <param name="pastaDaMidia">A pasta <c>sources\sxs</c> da mídia do Windows, ou nulo.</param>
    public static string ArgumentosDoDism(string? pastaDaMidia = null)
    {
        const string Base = "/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart";
        if (string.IsNullOrWhiteSpace(pastaDaMidia))
        {
            return Base;
        }

        var pasta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pastaDaMidia));
        if (pasta.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("Caminho com aspas não é aceito.", nameof(pastaDaMidia));
        }

        return $"{Base} /Source:\"{pasta}\" /LimitAccess";
    }

    /// <summary>
    /// O que impede usar a pasta como origem: vazio quando ela existe e tem o pacote do .NET 3.5.
    /// </summary>
    /// <param name="pastaDaMidia">A pasta escolhida.</param>
    public static string ProblemaNaPasta(string pastaDaMidia)
    {
        if (string.IsNullOrWhiteSpace(pastaDaMidia) || !Directory.Exists(pastaDaMidia))
        {
            return "A pasta não existe.";
        }

        var temPacote = Directory.EnumerateFiles(pastaDaMidia, "*.cab")
            .Any(f => Path.GetFileName(f).StartsWith(PacoteNaMidia, StringComparison.OrdinalIgnoreCase));

        return temPacote
            ? string.Empty
            : "Esta pasta não tem o pacote do .NET 3.5. Escolha a pasta sources\\sxs da mídia de instalação do Windows " +
              "(o ISO montado ou o pendrive), da mesma versão do Windows deste PC.";
    }
}
