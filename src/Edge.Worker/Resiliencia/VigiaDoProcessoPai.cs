using System.ComponentModel;
using System.Diagnostics;

namespace Edge.Worker.Resiliencia;

/// <summary>
/// O worker morre com o serviço: confere de tempos em tempos se o processo que o subiu ainda
/// existe e, se não existe, encerra.
/// </summary>
/// <remarks>
/// <para>
/// Defeito relatado pelo dono do produto (01/10, docs/29): o serviço morreu sem passar pela
/// parada limpa (Gerenciador de Tarefas, queda), os workers simulados ficaram órfãos e
/// continuaram gravando na base "catraca em operação" — e o serviço seguinte, em modo real,
/// mostrou três catracas "Atendendo" sem nenhuma na rede.
/// </para>
/// <para>
/// No Windows o serviço também põe cada worker num Job Object que o mata quando o serviço
/// some (<c>ContencaoDosWorkers</c>, no supervisor). Esta vigia é a segunda defesa: vale em
/// qualquer sistema e cobre o worker que escapou do Job Object (atribuído depois de subir,
/// ou Job Object recusado pelo Windows).
/// </para>
/// <para>
/// Encerrar primeiro pede a parada limpa (o laço termina a volta e o registro fecha). Se o
/// worker estiver preso numa chamada bloqueante da DLL e não sair em
/// <see cref="Tolerancia"/>, encerra à força — como o supervisor faria (ADR-0001).
/// </para>
/// <para>
/// Determinística: <see cref="Conferir"/> é um passo, com relógio injetável, para ser testada
/// sem esperar tempo passar; <see cref="Iniciar"/> só a faz girar.
/// </para>
/// </remarks>
public sealed class VigiaDoProcessoPai : IDisposable
{
    private readonly Func<bool> _paiVivo;
    private readonly Action _encerrar;
    private readonly Action _encerrarAForca;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly Lock _trava = new();
    private DateTimeOffset? _paiSumiuEm;
    private bool _forcado;
    private Timer? _timer;

    /// <param name="paiVivo">Verdadeiro enquanto o processo pai existe.</param>
    /// <param name="encerrar">Parada limpa do worker (cancelar o laço).</param>
    /// <param name="encerrarAForca">Saída imediata do processo, se a limpa não bastou.</param>
    /// <param name="intervalo">Entre uma conferência e outra. Padrão: 2 s, o ritmo da supervisão.</param>
    /// <param name="tolerancia">Quanto esperar a parada limpa antes de forçar. Padrão: 10 s.</param>
    /// <param name="relogio">Relógio.</param>
    public VigiaDoProcessoPai(
        Func<bool> paiVivo,
        Action encerrar,
        Action encerrarAForca,
        TimeSpan? intervalo = null,
        TimeSpan? tolerancia = null,
        Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(paiVivo);
        ArgumentNullException.ThrowIfNull(encerrar);
        ArgumentNullException.ThrowIfNull(encerrarAForca);

        _paiVivo = paiVivo;
        _encerrar = encerrar;
        _encerrarAForca = encerrarAForca;
        Intervalo = intervalo ?? TimeSpan.FromSeconds(2);
        Tolerancia = tolerancia ?? TimeSpan.FromSeconds(10);
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Entre uma conferência e outra.</summary>
    public TimeSpan Intervalo { get; }

    /// <summary>Quanto a parada limpa tem antes de o processo sair à força.</summary>
    public TimeSpan Tolerancia { get; }

    /// <summary>Verdadeiro depois que o pai sumiu e o encerramento foi pedido.</summary>
    public bool PaiSumiu
    {
        get
        {
            lock (_trava)
            {
                return _paiSumiuEm is not null;
            }
        }
    }

    /// <summary>
    /// Uma conferência. Pai vivo: nada. Pai sumido: pede a parada limpa (uma vez só) e, se ela
    /// passar da tolerância, a saída à força (uma vez só).
    /// </summary>
    /// <returns>Verdadeiro se o pai continua vivo.</returns>
    public bool Conferir()
    {
        lock (_trava)
        {
            var agora = _relogio();

            if (_paiSumiuEm is { } desde)
            {
                if (!_forcado && agora - desde >= Tolerancia)
                {
                    _forcado = true;
                    _encerrarAForca();
                }

                return false;
            }

            if (_paiVivo())
            {
                return true;
            }

            _paiSumiuEm = agora;
            _encerrar();
            return false;
        }
    }

    /// <summary>Começa a conferir a cada <see cref="Intervalo"/>, numa thread do sistema.</summary>
    public void Iniciar()
    {
        lock (_trava)
        {
            _timer ??= new Timer(_ => Conferir(), null, Intervalo, Intervalo);
        }
    }

    public void Dispose()
    {
        lock (_trava)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>
    /// "O processo <paramref name="pid"/> ainda existe?", à prova de PID reaproveitado: guarda a
    /// hora de início do pai agora e, depois, um processo com o mesmo PID e outra hora de início
    /// é outro programa — o pai morreu.
    /// </summary>
    /// <remarks>
    /// Sem permissão para consultar o processo, responde "vivo": na dúvida, o worker que atende
    /// a catraca não se mata. O Job Object, no Windows, continua valendo.
    /// </remarks>
    public static Func<bool> PaiPorPid(int pid)
    {
        DateTime? inicio;

        try
        {
            using var processo = Process.GetProcessById(pid);
            inicio = HoraDeInicio(processo);
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException)
        {
            // Já não existe quando o worker subiu.
            return () => false;
        }

        return () =>
        {
            try
            {
                using var processo = Process.GetProcessById(pid);

                if (processo.HasExited)
                {
                    return false;
                }

                return inicio is not { } esperado || HoraDeInicio(processo) is not { } atual || atual == esperado;
            }
            catch (Exception erro) when (erro is ArgumentException or InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return true;
            }
        };
    }

    private static DateTime? HoraDeInicio(Process processo)
    {
        try
        {
            return processo.StartTime;
        }
        catch (Exception erro) when (erro is Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}
