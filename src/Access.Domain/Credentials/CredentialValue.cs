namespace Access.Domain.Credentials;

/// <summary>
/// Identificador de credencial. <b>Sempre texto</b>, nunca número.
/// </summary>
/// <remarks>
/// <para>
/// Converter para inteiro destrói informação de forma irreversível: <c>0001234</c> vira
/// <c>1234</c>, e o cartão passa a ser recusado — ou, pior, a colidir com outro.
/// Ver docs/ADR/ADR-0008-credencial-como-string.md
/// </para>
/// <para>
/// O <see cref="ToString"/> devolve <b>mascarado</b>. Isso torna o caminho seguro o
/// caminho padrão: logar uma credencial por engano não vaza o número.
/// Para o valor real, use <see cref="Normalized"/> explicitamente.
/// </para>
/// </remarks>
public sealed class CredentialValue : IEquatable<CredentialValue>
{
    private CredentialValue(string raw, string normalized, string profile)
    {
        Raw = raw;
        Normalized = normalized;
        Profile = profile;
    }

    /// <summary>Valor exatamente como veio do equipamento, preservado para auditoria.</summary>
    public string Raw { get; }

    /// <summary>Valor após a normalização do perfil. É por ele que se compara.</summary>
    public string Normalized { get; }

    /// <summary>Nome do perfil de normalização aplicado.</summary>
    public string Profile { get; }

    /// <summary>Quantidade de caracteres do valor normalizado.</summary>
    public int Length => Normalized.Length;

    /// <summary>
    /// Cria a partir de um valor bruto, aplicando o perfil informado.
    /// </summary>
    /// <exception cref="ArgumentException">Se o valor bruto for nulo ou vazio.</exception>
    public static CredentialValue Create(string raw, CredentialNormalization normalization)
    {
        ArgumentNullException.ThrowIfNull(normalization);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("Credencial vazia não é uma credencial.", nameof(raw));
        }

        return new CredentialValue(raw, normalization.Apply(raw), normalization.Name);
    }

    public bool Equals(CredentialValue? other) =>
        other is not null
        && string.Equals(Normalized, other.Normalized, StringComparison.Ordinal)
        && string.Equals(Profile, other.Profile, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CredentialValue);

    public override int GetHashCode() => HashCode.Combine(Normalized, Profile);

    /// <summary>
    /// Representação <b>mascarada</b>, segura para log: mostra no máximo os dois últimos
    /// caracteres e o comprimento.
    /// </summary>
    public override string ToString() => Mask(Normalized);

    /// <summary>
    /// Máscara para qualquer código lido — tela de operador, log, painel. Mesma regra de
    /// <see cref="ToString"/>: no máximo os dois últimos caracteres e o comprimento.
    /// </summary>
    public static string Mascarar(string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        return Mask(valor);
    }

    internal static string Mask(string value)
    {
        if (value.Length <= 4)
        {
            return $"cred:****({value.Length})";
        }

        return $"cred:****{value[^2..]}({value.Length})";
    }
}
