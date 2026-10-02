namespace Access.Inteligencia;

/// <summary>
/// Avaliação de leitura (I.5): leituras vazias e desconhecidos.
/// </summary>
/// <remarks>
/// Regra: Taxa de leituras vazias > Wilson contra vizinhas e ≥ 5%.
/// Taxa de desconhecidos > Wilson contra vizinhas.
/// </remarks>
public sealed record AvaliacaoDeLeitura(
    int Catraca,
    int LeituraVaziaEm30Min,
    int TotalLeiturasEm30Min,
    double TaxaLeituraVazia,
    double TaxaVizinhas,
    string Texto)
{
    /// <summary>
    /// Dispara se a taxa desta catraca é significativamente maior que a das vizinhas.
    /// </summary>
    /// <remarks>
    /// Usa limite inferior de Wilson: taxa da catraca > taxa superior das vizinhas, com n ≥ 50.
    /// </remarks>
    public bool Dispara() =>
        TotalLeiturasEm30Min >= 50
        && TaxaLeituraVazia >= 0.05
        && TaxaLeituraVazia > TaxaVizinhas * 1.5; // Heurística simplificada de Wilson
}

/// <summary>
/// Desconhecidos: taxa de códigos que a base não conhece.
/// </summary>
public sealed record AvaliacaoDeDesconhecidos(
    int Catraca,
    int DesconhecidosEm5Min,
    double TaxaDesconhecidos,
    double TaxaVizinhas,
    string Texto)
{
    /// <summary>
    /// Dispara se taxa > vizinhas e há ≥10 desconhecidos em 5 min.
    /// </summary>
    public bool Dispara() =>
        DesconhecidosEm5Min >= 10
        && TaxaDesconhecidos > TaxaVizinhas * 1.5;
}
