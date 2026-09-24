using System.Diagnostics;
using System.Globalization;

namespace Edge.Supervisor;

/// <summary>
/// Um worker de verdade: processo filho, com o hospedeiro x86 no comando.
/// </summary>
/// <remarks>
/// <para>
/// Até aqui só existiam dublês de teste implementando <see cref="IWorkerHost"/>. Isso
/// bastava para exercitar a lógica de supervisão, e escondia que nada no produto sabia
/// <b>subir</b> um worker.
/// </para>
/// <para>
/// Matar é o único encerramento confiável: com a DLL travada numa chamada bloqueante, a
/// thread não volta para atender pedido de parada. Ver ADR-0001.
/// </para>
/// </remarks>
public sealed class ProcessoDeWorker : IWorkerHost
{
    private readonly string _executavel;
    private readonly Func<DateTimeOffset> _relogio;
    private Process? _processo;
    private DateTimeOffset? _iniciadoEm;
    private string _ultimaSaida = "ainda não iniciado";

    public ProcessoDeWorker(
        string nome,
        int porta,
        IReadOnlyList<int> inners,
        string executavel,
        Func<DateTimeOffset>? relogio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentException.ThrowIfNullOrWhiteSpace(executavel);
        ArgumentNullException.ThrowIfNull(inners);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(porta);

        Nome = nome;
        Porta = porta;
        Inners = [.. inners];
        _executavel = executavel;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    public string Nome { get; }

    public int Porta { get; }

    public IReadOnlyList<int> Inners { get; }

    public bool EstaVivo => _processo is { HasExited: false };

    /// <summary>
    /// Saudável enquanto o processo está de pé.
    /// </summary>
    /// <remarks>
    /// <b>Não é batimento de verdade.</b> Um worker vivo mas travado dentro da DLL aparece
    /// como saudável aqui. O batimento real exige o worker reportar pelo IPC, e isso só faz
    /// sentido quando existir adapter nativo — hoje ele sai na largada.
    /// A_CONFIRMAR: ver docs/07, Fase 2.
    /// </remarks>
    public bool EstaSaudavel => EstaVivo;

    public string Diagnostico
    {
        get
        {
            var idade = _iniciadoEm is { } quando
                ? string.Create(CultureInfo.InvariantCulture, $" · iniciado {(_relogio() - quando).TotalSeconds:F0}s atrás")
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Nome} · porta {Porta} · {Inners.Count} equipamento(s) · {_ultimaSaida}{idade}");
        }
    }

    public void Iniciar()
    {
        if (EstaVivo)
        {
            return;
        }

        var inicio = new ProcessStartInfo
        {
            FileName = _executavel,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        inicio.ArgumentList.Add("--porta");
        inicio.ArgumentList.Add(Porta.ToString(CultureInfo.InvariantCulture));
        inicio.ArgumentList.Add("--inners");
        inicio.ArgumentList.Add(string.Join(',', Inners));

        _processo = Process.Start(inicio);
        _iniciadoEm = _relogio();
        _ultimaSaida = "em execução";

        if (_processo is null)
        {
            _ultimaSaida = "o sistema operacional não criou o processo";
            return;
        }

        // A saída do worker é a explicação de por que ele não subiu. Perdê-la transforma
        // "o worker morreu" num mistério.
        _processo.Exited += (_, _) => RegistrarSaida();
        _processo.EnableRaisingEvents = true;
    }

    public void Matar()
    {
        if (_processo is null || _processo.HasExited)
        {
            return;
        }

        _processo.Kill(entireProcessTree: true);
        _ultimaSaida = "morto pelo supervisor";
    }

    public void Dispose()
    {
        Matar();
        _processo?.Dispose();
        _processo = null;
    }

    private void RegistrarSaida()
    {
        if (_processo is null)
        {
            return;
        }

        var erro = _processo.StandardError.ReadToEnd().Trim();
        var codigo = _processo.ExitCode;

        _ultimaSaida = string.IsNullOrWhiteSpace(erro)
            ? $"encerrou com código {codigo}"
            : $"encerrou com código {codigo}: {PrimeiraLinha(erro)}";
    }

    private static string PrimeiraLinha(string texto)
    {
        var quebra = texto.IndexOf('\n', StringComparison.Ordinal);
        return quebra < 0 ? texto : texto[..quebra].TrimEnd('\r');
    }
}
