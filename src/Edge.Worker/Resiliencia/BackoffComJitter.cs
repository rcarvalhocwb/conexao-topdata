namespace Edge.Worker.Resiliencia;

/// <summary>
/// Espera exponencial com jitter, para reconexão.
/// </summary>
/// <remarks>
/// O jitter não é refinamento: sem ele, dezenas de catracas que caíram juntas — porque
/// o switch reiniciou — voltam a tentar exatamente no mesmo instante, repetidamente.
/// Ver docs/03-arquitetura.md, seção 5.
/// </remarks>
public sealed class BackoffComJitter
{
    private readonly TimeSpan _inicial;
    private readonly TimeSpan _maximo;
    private readonly double _fatorDeJitter;
    private readonly Func<double> _aleatorio;

    public BackoffComJitter(
        TimeSpan? inicial = null,
        TimeSpan? maximo = null,
        double fatorDeJitter = 0.3,
        Func<double>? aleatorio = null)
    {
        if (fatorDeJitter is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fatorDeJitter), fatorDeJitter, "O jitter vai de 0 a 1.");
        }

        _inicial = inicial ?? TimeSpan.FromSeconds(1);
        _maximo = maximo ?? TimeSpan.FromMinutes(2);
        _fatorDeJitter = fatorDeJitter;
        _aleatorio = aleatorio ?? Random.Shared.NextDouble;
    }

    /// <summary>Espera para a tentativa informada, começando em 1.</summary>
    public TimeSpan Para(int tentativa)
    {
        if (tentativa < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tentativa), tentativa, "A primeira tentativa é 1.");
        }

        // Limita o expoente antes de multiplicar: 2^1000 estoura muito antes do teto.
        var expoente = Math.Min(tentativa - 1, 30);
        var bruto = _inicial * Math.Pow(2, expoente);
        var limitado = bruto > _maximo ? _maximo : bruto;

        // Jitter simétrico: pode adiantar ou atrasar, nunca ficar negativo.
        var desvio = (_aleatorio() * 2 - 1) * _fatorDeJitter;
        var comJitter = limitado * (1 + desvio);

        return comJitter < TimeSpan.Zero ? TimeSpan.Zero : comJitter;
    }
}
