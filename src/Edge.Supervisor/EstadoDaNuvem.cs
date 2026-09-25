namespace Edge.Supervisor;

/// <summary>O que o painel precisa saber da sincronização com a nuvem.</summary>
/// <remarks>
/// Atualizado pelo laço de sincronização; lido pelo painel. Sem internet é o regime normal
/// de um evento (ADR-0017), então isto informa — nunca bloqueia nada.
/// </remarks>
public sealed class EstadoDaNuvem
{
    /// <summary>Sem sucesso há mais que isto, o painel mostra "sem internet".</summary>
    public static readonly TimeSpan ConsideradaFora = TimeSpan.FromMinutes(2);

    private long _ultimoSucessoTicks;

    /// <summary>Se a nuvem está configurada nesta máquina.</summary>
    public bool Configurada { get; set; }

    /// <summary>Última vez que a nuvem respondeu com sucesso.</summary>
    public DateTimeOffset? UltimoSucesso
    {
        get
        {
            var ticks = Interlocked.Read(ref _ultimoSucessoTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Última falha, para o painel explicar. Nunca contém segredo nem corpo de resposta.</summary>
    public string? UltimaFalha { get; private set; }

    public void RegistrarSucesso(DateTimeOffset quando)
    {
        Interlocked.Exchange(ref _ultimoSucessoTicks, quando.UtcTicks);
        UltimaFalha = null;
    }

    public void RegistrarFalha(string motivo) => UltimaFalha = motivo;
}
