namespace Access.Inteligencia;

/// <summary>
/// Avaliação do laço do worker (I.5): volta lenta.
/// </summary>
/// <remarks>
/// Regra: p95 da volta ≥ 1,5 s (3× o limiteDeEspera = 500 ms).
/// </remarks>
public sealed record AvaliacaoDoLaco(
    string Worker,
    double P95LoopMs,
    int VoltasEm1Min,
    string Texto)
{
    /// <summary>
    /// Dispara se p95 ≥ 1500 ms.
    /// </summary>
    public bool Dispara() => P95LoopMs >= 1500.0;
}

/// <summary>
/// A9 — Liberou e não girou em série (I.5): 3 liberações seguidas sem giro.
/// </summary>
public sealed record AvaliacaoDeLiberacaoSemGiro(
    int Catraca,
    int LiberacoesSemGiroSeguidas,
    string Texto)
{
    /// <summary>
    /// Dispara se há 3 liberações seguidas sem giro.
    /// </summary>
    public bool Dispara() => LiberacoesSemGiroSeguidas >= 3;
}

/// <summary>
/// A10 — Giro sem pedido (I.5): origem 6 orfã ou correlação em múltiplas catracas.
/// </summary>
public sealed record AvaliacaoDeGiroSemPedido(
    int Catraca,
    int GirosSemPedidoEm1Hora,
    IReadOnlyList<int> CatracasComGiroSemPedidoEm10s,
    string Texto)
{
    /// <summary>
    /// Dispara se ≥3 giros sem pedido em 1 hora, ou ≥2 catracas em 10s (correlação).
    /// </summary>
    public bool Dispara() =>
        GirosSemPedidoEm1Hora >= 3
        || CatracasComGiroSemPedidoEm10s.Count >= 2;
}

/// <summary>
/// A12 — Queda simultânea (I.5): ≥2 catracas caíram no mesmo minuto.
/// </summary>
public sealed record AvaliacaoDeQuedaSimultanea(
    IReadOnlyList<int> CatracasQueQuairam,
    string Texto)
{
    /// <summary>
    /// Dispara se ≥2 catracas caíram.
    /// </summary>
    public bool Dispara() => CatracasQueQuairam.Count >= 2;
}

/// <summary>
/// A13 — Liberação recusada (I.5): a catraca devolveu retorno ≠ 0.
/// </summary>
public sealed record AvaliacaoDeLiberacaoRecusada(
    int Catraca,
    int LiberacoesRecusadasEm10Min,
    int UltimoRetornoDaCatraca,
    string Texto)
{
    /// <summary>
    /// Dispara se ≥2 liberações recusadas em 10 min.
    /// </summary>
    public bool Dispara() => LiberacoesRecusadasEm10Min >= 2;
}
