using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Access.Infrastructure.SQLite;

namespace Edge.Supervisor.Atualizacoes;

/// <summary>
/// A ficha de uma versão estável (<c>versao.json</c>), publicada com a assinatura (<c>versao.json.sig</c>) na
/// release do GitHub (docs/60 §2.3). É ela que garante que o MSI baixado é nosso: o instalador não tem
/// certificado de assinatura de código.
/// </summary>
/// <param name="Produto">Sempre <c>rayzer-xacess</c>.</param>
/// <param name="Canal">O canal do instalador: <c>producao</c> ou <c>teste</c>.</param>
/// <param name="Versao">A versão (<c>0.1.N</c>).</param>
/// <param name="Msi">O nome do arquivo MSI na release.</param>
/// <param name="Sha256">O SHA-256 do MSI, em hexadecimal.</param>
/// <param name="Tamanho">O tamanho do MSI, em bytes.</param>
public sealed record FichaDaVersao(string Produto, string Canal, Version Versao, string Msi, string Sha256, long Tamanho)
{
    /// <summary>O nome do produto na ficha.</summary>
    public const string NomeDoProduto = "rayzer-xacess";

    /// <summary>O nome da ficha na release.</summary>
    public const string Arquivo = "versao.json";

    /// <summary>O nome da assinatura na release: base64 da assinatura ECDSA P-256 (IEEE P1363) dos bytes da ficha.</summary>
    public const string ArquivoDaAssinatura = "versao.json.sig";

    /// <summary>
    /// Confere a assinatura e lê a ficha. Nulo, com o motivo, se a assinatura não confere ou a ficha não serve.
    /// </summary>
    public static FichaDaVersao? Ler(byte[] ficha, string? assinaturaEmBase64, IReadOnlyList<string> chavesPublicas, out string? problema)
    {
        ArgumentNullException.ThrowIfNull(ficha);
        ArgumentNullException.ThrowIfNull(chavesPublicas);

        byte[] assinatura;
        try
        {
            assinatura = Convert.FromBase64String((assinaturaEmBase64 ?? string.Empty).Trim());
        }
        catch (FormatException)
        {
            assinatura = [];
        }

        if (assinatura.Length != 64 || !Licencas.AssinaturaConfere(ficha, assinatura, chavesPublicas))
        {
            problema = "a assinatura da ficha da versão não confere; nada foi baixado";
            return null;
        }

        Corpo? corpo;
        try
        {
            corpo = JsonSerializer.Deserialize<Corpo>(ficha);
        }
        catch (JsonException)
        {
            corpo = null;
        }

        if (corpo is null || corpo.Produto != NomeDoProduto || string.IsNullOrWhiteSpace(corpo.Canal)
            || !Version.TryParse(corpo.Versao, out var versao)
            || string.IsNullOrWhiteSpace(corpo.Msi) || corpo.Msi.IndexOfAny(['/', '\\', ':']) >= 0
            || !corpo.Msi.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            || corpo.Sha256 is not { Length: 64 } || !corpo.Sha256.All(Uri.IsHexDigit)
            || corpo.Tamanho <= 0)
        {
            problema = "a ficha da versão está incompleta";
            return null;
        }

        problema = null;
        return new FichaDaVersao(corpo.Produto, corpo.Canal, versao, corpo.Msi, corpo.Sha256.ToLowerInvariant(), corpo.Tamanho);
    }

    /// <summary>A ficha em JSON, como o CI gera (para testes e para a ferramenta).</summary>
    public string ParaJson() => JsonSerializer.Serialize(new Corpo
    {
        Produto = Produto,
        Canal = Canal,
        Versao = Versao.ToString(),
        Msi = Msi,
        Sha256 = Sha256,
        Tamanho = Tamanho,
    });

    /// <summary>A versão do programa, com três partes (<c>0.1.N</c>), como o CI grava.</summary>
    public static Version? VersaoDe(string? texto) =>
        Version.TryParse(texto, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;

    private sealed class Corpo
    {
        [JsonPropertyName("produto")]
        public string? Produto { get; set; }

        [JsonPropertyName("canal")]
        public string? Canal { get; set; }

        [JsonPropertyName("versao")]
        public string? Versao { get; set; }

        [JsonPropertyName("msi")]
        public string? Msi { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("tamanho")]
        public long Tamanho { get; set; }
    }
}
