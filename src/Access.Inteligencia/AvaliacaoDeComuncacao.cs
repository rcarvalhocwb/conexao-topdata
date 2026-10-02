namespace Access.Inteligencia;

/// <summary>
/// A6 — Comunicação instável (I.5): reconexões, erros de recepção, latência.
/// </summary>
/// <remarks>
/// Regra: ≥3 reconexões em 15 min, ou erros de recepção com z ≥ 4, ou p95 da latência
/// de recepção ≥ 3× a mediana das vizinhas.
/// </remarks>
public sealed record AvaliacaoDeComunicacao(
    int Catraca,
    int ReconexoesEm15Min,
    int ErrosDeRecepcaoEm15Min,
    double ZDoErro,
    double P95LatenciaRecepção,
    double MedianaLatenciaVizinhas,
    string Texto)
{
    /// <summary>
    /// Dispara se há ≥3 reconexões, ou z ≥ 4, ou latência muito alta.
    /// </summary>
    public bool Dispara() =>
        ReconexoesEm15Min >= 3
        || ZDoErro >= 4.0
        || (MedianaLatenciaVizinhas > 0 && P95LatenciaRecepção >= MedianaLatenciaVizinhas * 3);
}

/// <summary>
/// Detecção de comunicação: reconexões e erros de recepção.
/// </summary>
public static class DetectorDeComunicacao
{
    /// <summary>
    /// Calcula o z robusto (z = (x - mediana) / (1,4826 * MAD)) para erros.
    /// </summary>
    public static double CalcularZRobusto(int errosObservados, double mediaErros, double mad)
    {
        if (mad <= 1)
            mad = 1; // MAD mínimo de 1
        return (errosObservados - mediaErros) / (1.4826 * mad);
    }
}
