namespace Edge.Worker.Resiliencia;

/// <summary>Estado do disjuntor.</summary>
public enum EstadoDoDisjuntor
{
    /// <summary>Passando: chamadas seguem normalmente.</summary>
    Fechado,

    /// <summary>Aberto: chamadas são recusadas de imediato.</summary>
    Aberto,

    /// <summary>Testando: uma chamada é deixada passar para sondar a recuperação.</summary>
    MeioAberto,
}

/// <summary>
/// Disjuntor por equipamento.
/// </summary>
/// <remarks>
/// Existe para que uma catraca com defeito pare de consumir o tempo da thread de
/// comunicação. Como a DLL é sequencial, cada tentativa contra um equipamento morto é
/// tempo roubado dos que estão funcionando.
/// </remarks>
public sealed class CircuitBreaker
{
    private readonly int _falhasParaAbrir;
    private readonly TimeSpan _tempoAberto;
    private readonly Func<DateTimeOffset> _relogio;

    private int _falhasSeguidas;
    private DateTimeOffset? _abertoDesde;

    public CircuitBreaker(int falhasParaAbrir = 5, TimeSpan? tempoAberto = null, Func<DateTimeOffset>? relogio = null)
    {
        if (falhasParaAbrir < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(falhasParaAbrir), falhasParaAbrir, "Precisa ser ao menos 1.");
        }

        _falhasParaAbrir = falhasParaAbrir;
        _tempoAberto = tempoAberto ?? TimeSpan.FromSeconds(30);
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    public EstadoDoDisjuntor Estado
    {
        get
        {
            if (_abertoDesde is null)
            {
                return EstadoDoDisjuntor.Fechado;
            }

            return _relogio() - _abertoDesde.Value >= _tempoAberto
                ? EstadoDoDisjuntor.MeioAberto
                : EstadoDoDisjuntor.Aberto;
        }
    }

    public int FalhasSeguidas => _falhasSeguidas;

    /// <summary>Verdadeiro quando vale a pena tentar de novo.</summary>
    public bool PermitePassar => Estado is EstadoDoDisjuntor.Fechado or EstadoDoDisjuntor.MeioAberto;

    public void RegistrarSucesso()
    {
        _falhasSeguidas = 0;
        _abertoDesde = null;
    }

    public void RegistrarFalha()
    {
        _falhasSeguidas++;

        if (_falhasSeguidas >= _falhasParaAbrir)
        {
            _abertoDesde = _relogio();
        }
    }
}
