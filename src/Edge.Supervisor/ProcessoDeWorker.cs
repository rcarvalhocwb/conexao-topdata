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
    private readonly IReadOnlyList<string> _argumentosExtras;
    private readonly Queue<string> _ultimasLinhas = new();
    private readonly Lock _travaDasLinhas = new();

    /// <summary>Quantas linhas da saída do worker ficam guardadas para diagnóstico.</summary>
    public const int LinhasGuardadas = 50;

    public ProcessoDeWorker(
        string nome,
        int porta,
        IReadOnlyList<int> inners,
        string executavel,
        Func<DateTimeOffset>? relogio = null,
        IReadOnlyList<string>? argumentosExtras = null)
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
        _argumentosExtras = argumentosExtras ?? [];
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

        foreach (var argumento in _argumentosExtras)
        {
            inicio.ArgumentList.Add(argumento);
        }

        _ultimoErro = null;
        _processo = Process.Start(inicio);
        _iniciadoEm = _relogio();
        _ultimaSaida = "em execução";

        if (_processo is null)
        {
            _ultimaSaida = "o sistema operacional não criou o processo";
            return;
        }

        // A saída precisa ser LIDA enquanto o worker roda. Redirecionada e não lida, ela
        // enche o buffer do pipe (poucos KB) e o worker trava no próximo Console.WriteLine
        // — com a catraca parada e o processo "vivo". As últimas linhas ficam guardadas: são
        // a explicação de por que ele não subiu ou morreu.
        _processo.OutputDataReceived += (_, e) => Guardar(e.Data);
        _processo.ErrorDataReceived += (_, e) => Guardar(e.Data, erro: true);
        _processo.Exited += (_, _) => RegistrarSaida();
        _processo.EnableRaisingEvents = true;
        _processo.BeginOutputReadLine();
        _processo.BeginErrorReadLine();
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

        // Espera a leitura assíncrona terminar, senão a última linha de erro — a que
        // explica a saída — ainda não chegou.
        _processo.WaitForExit();
        var codigo = _processo.ExitCode;
        var erro = _ultimoErro;

        _ultimaSaida = string.IsNullOrWhiteSpace(erro)
            ? $"encerrou com código {codigo}"
            : $"encerrou com código {codigo}: {erro}";
    }

    /// <summary>As últimas linhas que o worker escreveu, da mais antiga à mais nova.</summary>
    public IReadOnlyList<string> UltimasLinhas()
    {
        lock (_travaDasLinhas)
        {
            return [.. _ultimasLinhas];
        }
    }

    private string? _ultimoErro;

    private void Guardar(string? linha, bool erro = false)
    {
        if (linha is null)
        {
            return;
        }

        lock (_travaDasLinhas)
        {
            if (erro && _ultimoErro is null && !string.IsNullOrWhiteSpace(linha))
            {
                // A primeira linha de erro costuma ser a causa; as seguintes, consequência.
                _ultimoErro = linha.Trim();
            }

            _ultimasLinhas.Enqueue(linha);
            while (_ultimasLinhas.Count > LinhasGuardadas)
            {
                _ultimasLinhas.Dequeue();
            }
        }
    }
}
