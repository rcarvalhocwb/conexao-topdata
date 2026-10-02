namespace Edge.Worker;

/// <summary>
/// Batimento do laço de comunicação, observado pelo supervisor.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque <c>ReceberDadosOnLine</c> travado <b>não lança</b>: a thread
/// simplesmente não volta. Nenhum <c>try/catch</c> pega isso. O que pega é a ausência
/// de batimento — e a ação é matar o worker, não esperar.
/// </para>
/// <para>
/// <b>Importante e frequentemente mal entendido:</b> dentro de um worker, uma catraca
/// travada <b>bloqueia</b> as outras, porque a DLL exige thread única. O isolamento é
/// entre workers, não entre equipamentos do mesmo worker. É isso que torna o
/// particionamento por afinidade física (ADR-0005) uma decisão de operação, e não de
/// desempenho.
/// </para>
/// </remarks>
public sealed class Watchdog
{
    private readonly TimeSpan _tolerancia;
    private readonly Func<DateTimeOffset> _relogio;
    private long _ultimoBatimentoTicks;
    private string _ultimaAtividade = "iniciando";

    public Watchdog(TimeSpan? tolerancia = null, Func<DateTimeOffset>? relogio = null)
    {
        _tolerancia = tolerancia ?? TimeSpan.FromSeconds(30);
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
        _ultimoBatimentoTicks = _relogio().UtcTicks;
    }

    /// <summary>Tolerância antes de considerar o laço travado.</summary>
    public TimeSpan Tolerancia => _tolerancia;

    /// <summary>Último batimento registrado.</summary>
    public DateTimeOffset UltimoBatimento =>
        new(Interlocked.Read(ref _ultimoBatimentoTicks), TimeSpan.Zero);

    /// <summary>O que o laço estava fazendo no último batimento.</summary>
    public string UltimaAtividade => Volatile.Read(ref _ultimaAtividade);

    /// <summary>Tempo desde o último batimento.</summary>
    public TimeSpan TempoSemBatimento => _relogio() - UltimoBatimento;

    /// <summary>
    /// Verdadeiro enquanto o laço deu sinal de vida dentro da tolerância.
    /// </summary>
    public bool EstaSaudavel => TempoSemBatimento <= _tolerancia;

    /// <summary>Registra sinal de vida. Chamado pelo laço a cada passo.</summary>
    /// <param name="atividade">
    /// O que está sendo feito, para que o diagnóstico diga <i>onde</i> travou.
    /// </param>
    public void Bater(string atividade)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atividade);
        Volatile.Write(ref _ultimaAtividade, atividade);
        Interlocked.Exchange(ref _ultimoBatimentoTicks, _relogio().UtcTicks);
    }

    /// <summary>Diagnóstico legível, para log e para o pacote de suporte.</summary>
    public string Diagnostico() =>
        EstaSaudavel
            ? $"saudável (último batimento há {TempoSemBatimento.TotalSeconds:F1}s em '{UltimaAtividade}')"
            : $"SEM BATIMENTO há {TempoSemBatimento.TotalSeconds:F1}s, travado em '{UltimaAtividade}' " +
              $"(tolerância {_tolerancia.TotalSeconds:F0}s)";
}
