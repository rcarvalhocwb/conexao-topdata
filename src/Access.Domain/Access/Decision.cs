namespace Access.Domain.Access;

/// <summary>Resultado de uma decisão de acesso.</summary>
public enum DecisionOutcome
{
    /// <summary>Acesso liberado.</summary>
    Allowed,

    /// <summary>Acesso negado.</summary>
    Denied,

    /// <summary>Exige conferência humana antes de liberar.</summary>
    Review,

    /// <summary>
    /// Decidido por regra de contingência, porque a decisão normal não pôde ser tomada
    /// dentro do tempo-limite.
    /// </summary>
    OfflineFallback,
}

/// <summary>
/// Nível de degradação em que a decisão foi tomada.
/// Ver docs/ADR/ADR-0017-niveis-de-degradacao.md
/// </summary>
public enum DegradationTier
{
    /// <summary>Tudo no ar.</summary>
    T0Normal,

    /// <summary>Sem internet. Regime normal de um evento; a borda decide tudo.</summary>
    T1SemInternet,

    /// <summary>Borda indisponível; o equipamento decide pela lista local.</summary>
    T2ListaLocal,

    /// <summary>Equipamento isolado; vale a política de falha do portão.</summary>
    T3Isolado,
}

/// <summary>
/// Código estável de motivo. Entra em relatório, integração e auditoria, então
/// <b>não muda por conveniência de texto</b> — a mensagem amigável é separada.
/// </summary>
public readonly record struct ReasonCode
{
    public ReasonCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(ReasonCode code) => code.Value;
}

/// <summary>
/// Passo da avaliação de regras, para que a negação possa ser <b>explicada</b>.
/// </summary>
/// <param name="Rule">Nome da regra avaliada.</param>
/// <param name="Passed">Se a regra foi satisfeita.</param>
/// <param name="Detail">Detalhe legível, sem dado sensível.</param>
public sealed record RuleTrace(string Rule, bool Passed, string? Detail = null);

/// <summary>
/// Decisão de acesso. É o contrato entre o motor local e todo o resto.
/// </summary>
/// <remarks>
/// A mensagem exibida ao operador <b>não</b> faz parte da decisão: o mesmo
/// <see cref="Reason"/> vira textos diferentes na tela, no relatório e na integração.
/// Ver docs/03-arquitetura.md, seção 6.
/// </remarks>
public sealed record Decision(
    DecisionOutcome Outcome,
    ReasonCode Reason,
    DegradationTier Tier,
    TimeSpan Elapsed,
    IReadOnlyList<RuleTrace> Trace)
{
    /// <summary>Verdadeiro quando o comando de liberação deve ser emitido.</summary>
    public bool ShouldRelease => Outcome is DecisionOutcome.Allowed;
}
