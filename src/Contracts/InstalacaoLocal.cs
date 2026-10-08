namespace Contracts;

/// <summary>
/// Onde a instalação guarda o que é da máquina: configuração, token do painel, base local,
/// registros e segredos.
/// </summary>
/// <remarks>
/// <c>%ProgramData%\ConexaoTopdata</c>, e não a pasta do programa: sobrevive a atualização
/// e reinstalação, e não exige gravar em <c>Program Files</c>.
/// </remarks>
public static class InstalacaoLocal
{
    /// <summary>Pasta de dados da instalação.</summary>
    public static string PastaDeDados { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ConexaoTopdata");

    /// <summary>Configuração do serviço: catracas, base e nuvem.</summary>
    public static string ArquivoDeConfiguracao => Path.Combine(PastaDeDados, "workers.json");

    /// <summary>Token de sessão entre o painel e o serviço.</summary>
    public static string ArquivoDoToken => Path.Combine(PastaDeDados, "token");

    /// <summary>Pasta do cofre de segredos.</summary>
    public static string PastaDosSegredos => Path.Combine(PastaDeDados, "segredos");

    /// <summary>
    /// Verdadeiro quando o arquivo do token existe mas esta conta não pode lê-lo. Isso acontece
    /// quando o operador não está no grupo <c>ConexaoTopdata Operadores</c>, e é o único caso em
    /// que o painel pode afirmar a causa com certeza. Arquivo ausente não conta.
    /// </summary>
    public static bool TokenIlegivel(string? arquivo = null)
    {
        var caminho = arquivo ?? ArquivoDoToken;

        return File.Exists(caminho) && LeituraNegada(caminho);
    }

    private static bool LeituraNegada(string caminho)
    {
        try
        {
            using (File.OpenRead(caminho))
            {
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// O token do painel: da variável <c>EDGE_TOKEN</c> (desenvolvimento) ou do arquivo que
    /// o serviço gera ao subir. Nulo se nenhum dos dois existe ainda.
    /// </summary>
    public static string? LerToken(string? arquivo = null)
    {
        var doAmbiente = Environment.GetEnvironmentVariable("EDGE_TOKEN");
        if (!string.IsNullOrWhiteSpace(doAmbiente))
        {
            return doAmbiente.Trim();
        }

        var caminho = arquivo ?? ArquivoDoToken;

        try
        {
            return File.Exists(caminho) ? File.ReadAllText(caminho).Trim() : null;
        }
        catch (UnauthorizedAccessException)
        {
            // Usuário sem permissão de leitura: o painel mostra "sem resposta do serviço".
            return null;
        }
    }
}
