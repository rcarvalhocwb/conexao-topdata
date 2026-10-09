namespace Access.Domain.Credentials;

/// <summary>
/// Regra de normalização de credencial, específica do padrão de leitura da instalação.
/// </summary>
/// <remarks>
/// <para>
/// Deliberadamente <b>não existe</b> opção de remover zeros à esquerda. Essa é a causa
/// clássica de "cartão válido recusado" no dia do evento, e não é uma escolha que o
/// operador deva poder fazer. Ver docs/ADR/ADR-0008.
/// </para>
/// <para>
/// O padrão de leitura e a quantidade de dígitos vêm da configuração enviada ao
/// equipamento (<c>DefinirPadraoCartao</c>, <c>DefinirQuantidadeDigitosCartao</c>,
/// <c>InserirQuantidadeDigitoVariavel</c>) — ver docs/11-capacidades-do-sdk.md.
/// </para>
/// </remarks>
public sealed class CredentialNormalization
{
    /// <summary>Perfil que não altera nada além de remover espaços nas pontas.</summary>
    public static CredentialNormalization Raw { get; } = new("raw");

    public CredentialNormalization(
        string name,
        int? padLeftTo = null,
        bool upperCase = false,
        IReadOnlySet<int>? allowedLengths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (padLeftTo is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(padLeftTo), padLeftTo, "Comprimento deve ser positivo.");
        }

        Name = name;
        PadLeftTo = padLeftTo;
        UpperCase = upperCase;
        AllowedLengths = allowedLengths;
    }

    /// <summary>Nome do perfil, registrado junto com a credencial.</summary>
    public string Name { get; }

    /// <summary>
    /// Completa com zeros à esquerda até este comprimento, quando o leitor entrega o
    /// valor sem os zeros. Nunca remove.
    /// </summary>
    public int? PadLeftTo { get; }

    /// <summary>
    /// Converte para maiúsculas. Necessário em leitores TTL/Serial ASCII e QR Code,
    /// que podem entregar letras.
    /// </summary>
    public bool UpperCase { get; }

    /// <summary>
    /// Comprimentos aceitos, quando o equipamento está configurado com quantidade
    /// variável de dígitos. Vazio significa "qualquer comprimento".
    /// </summary>
    public IReadOnlySet<int>? AllowedLengths { get; }

    /// <summary>Aplica a normalização.</summary>
    public string Apply(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var value = raw.Trim();

        if (UpperCase)
        {
            value = value.ToUpperInvariant();
        }

        if (PadLeftTo is { } width && value.Length < width)
        {
            value = value.PadLeft(width, '0');
        }

        return value;
    }

    /// <summary>
    /// Verdadeiro quando o comprimento do valor normalizado é aceito por este perfil.
    /// </summary>
    public bool IsLengthAccepted(string normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        return AllowedLengths is null || AllowedLengths.Count == 0 || AllowedLengths.Contains(normalized.Length);
    }
}
