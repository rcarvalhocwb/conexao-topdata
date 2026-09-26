using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edge.Supervisor;

/// <summary>Um grupo de equipamentos atendido por um worker.</summary>
/// <param name="Nome">Nome do grupo, normalmente a área física.</param>
/// <param name="Porta">Porta TCP deste worker. Uma por worker (ADR-0021).</param>
/// <param name="Inners">Equipamentos atendidos.</param>
/// <param name="Executavel">Caminho do hospedeiro x86.</param>
public sealed record GrupoConfigurado(string Nome, int Porta, IReadOnlyList<int> Inners, string Executavel);

/// <summary>Configuração do serviço local.</summary>
/// <param name="Endereco">Nome do named pipe (Windows) ou caminho do socket.</param>
/// <param name="Grupos">Grupos a supervisionar.</param>
/// <param name="Banco">Caminho da base local; vazio usa o padrão, ver <see cref="CaminhoDoBanco"/>.</param>
/// <param name="Nuvem">Painel na nuvem; ausente, a borda opera sem sincronizar.</param>
/// <param name="NomesDasCatracas">Nome de cada catraca no painel, pelo número do Inner.</param>
public sealed record ConfiguracaoDoSupervisor(
    string? Endereco,
    IReadOnlyList<GrupoConfigurado> Grupos,
    string? Banco = null,
    ConfiguracaoDaNuvem? Nuvem = null,
    IReadOnlyDictionary<int, string>? NomesDasCatracas = null)
{
    /// <summary>
    /// Caminho relativo do executável é relativo à pasta do serviço, e não à pasta atual:
    /// um serviço do Windows roda com a pasta atual em System32.
    /// </summary>
    public static string ResolverExecutavel(string executavel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executavel);
        return Path.IsPathRooted(executavel)
            ? executavel
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, executavel));
    }

    /// <summary>Pasta de dados: base, registros e segredos.</summary>
    public string PastaDeDados => Path.GetDirectoryName(Path.GetFullPath(CaminhoDoBanco))!;

    /// <summary>
    /// Base local compartilhada pelo serviço e pelos workers (ADR-0024). Sem valor, fica
    /// em <c>%ProgramData%\ConexaoTopdata\acesso.db</c>, que sobrevive a reinstalação.
    /// </summary>
    public string CaminhoDoBanco => string.IsNullOrWhiteSpace(Banco)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ConexaoTopdata",
            "acesso.db")
        : Banco;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lê a configuração de um arquivo.</summary>
    /// <exception cref="InvalidDataException">Arquivo vazio ou malformado.</exception>
    public static ConfiguracaoDoSupervisor Ler(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);

        var texto = File.ReadAllText(caminho);

        var config = JsonSerializer.Deserialize<ConfiguracaoDoSupervisor>(texto, Opcoes)
            ?? throw new InvalidDataException($"{caminho} não descreve uma configuração.");

        return config;
    }

    /// <summary>Problemas que impedem o serviço de subir. Lista vazia quando está válida.</summary>
    /// <remarks>
    /// Reclamar aqui, na partida, é muito melhor do que descobrir com a fila formada. Os
    /// mesmos conflitos são recusados pelo supervisor; a diferença é que aqui a mensagem
    /// diz o que corrigir no arquivo.
    /// </remarks>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        if (Grupos.Count == 0)
        {
            problemas.Add(
                "Nenhum grupo configurado. Um serviço sem worker não tem o que supervisionar — " +
                "descreva ao menos um grupo com nome, porta e equipamentos.");
        }

        foreach (var repetida in Grupos.GroupBy(g => g.Porta).Where(g => g.Count() > 1))
        {
            problemas.Add(
                $"Porta {repetida.Key} repetida entre grupos ({string.Join(", ", repetida.Select(g => g.Nome))}). " +
                "Cada worker precisa da sua, e a catraca aponta para ela.");
        }

        foreach (var repetido in Grupos.SelectMany(g => g.Inners).GroupBy(i => i).Where(g => g.Count() > 1))
        {
            problemas.Add($"Equipamento {repetido.Key} aparece em mais de um grupo.");
        }

        if (Nuvem is not null)
        {
            problemas.AddRange(Nuvem.Validar());
        }

        foreach (var grupo in Grupos.Where(g => !File.Exists(ResolverExecutavel(g.Executavel))))
        {
            problemas.Add($"Grupo {grupo.Nome}: executável não encontrado em {grupo.Executavel}.");
        }

        return problemas;
    }
}
