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
    private readonly Func<string?>? _entradaPadrao;
    private readonly IContencaoDeProcessos? _contencao;
    private readonly Func<IReadOnlyList<int>, DateTimeOffset?>? _ultimaNoticia;
    private readonly TimeSpan _toleranciaDoBatimento;
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
        IReadOnlyList<string>? argumentosExtras = null,
        Func<string?>? entradaPadrao = null,
        IContencaoDeProcessos? contencao = null,
        Func<IReadOnlyList<int>, DateTimeOffset?>? ultimaNoticia = null,
        TimeSpan? toleranciaDoBatimento = null)
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
        _entradaPadrao = entradaPadrao;
        _contencao = contencao;
        _ultimaNoticia = ultimaNoticia;
        _toleranciaDoBatimento = toleranciaDoBatimento ?? ToleranciaPadraoDoBatimento;
    }

    /// <summary>Silêncio máximo do worker na base antes de ele contar como travado.</summary>
    /// <remarks>
    /// O worker publica a situação das catracas a cada volta do laço, no máximo a cada 2 s
    /// (SessaoDeOperacao). Uma volta legítima pode demorar: com até 20 catracas fora do ar, cada
    /// teste de conexão bloqueia pelo tempo da DLL, que ainda não foi medido (T2, NOVO-LOAD-LOOP-01).
    /// 90 s fica bem acima disso e de uma base ocupada, sem matar um worker que só está lento; o
    /// painel já mostra "sem notícia" aos 15 s. Revisar com a medida da bancada.
    /// </remarks>
    public static readonly TimeSpan ToleranciaPadraoDoBatimento = TimeSpan.FromSeconds(90);

    public string Nome { get; }

    public int Porta { get; }

    public IReadOnlyList<int> Inners { get; }

    public bool EstaVivo => _processo is { HasExited: false };

    /// <summary>
    /// Saudável: processo de pé e dando notícia na base.
    /// </summary>
    /// <remarks>
    /// Achado E2-01 do docs/41: antes, "saudável" era só "processo vivo". Um worker travado
    /// dentro da DLL (ReceberDadosOnLine que não volta) parava todas as catracas do grupo e nunca
    /// era reiniciado. O batimento é a situação que o worker grava na base a cada 2 s (ADR-0024),
    /// lida por <c>ultimaNoticia</c>: sem notícia desta partida há mais que a tolerância, o worker
    /// está travado, e a supervisão o mata e sobe de novo. Base ocupada na leitura não conta como
    /// silêncio. Sem <c>ultimaNoticia</c> (testes, bancada), vale só o processo vivo.
    /// </remarks>
    public bool EstaSaudavel => EstaVivo && SilencioDoBatimento() is null;

    // Nulo quando o worker deu notícia dentro da tolerância (ou não há como saber).
    private TimeSpan? SilencioDoBatimento()
    {
        if (_ultimaNoticia is null || _iniciadoEm is not { } iniciado)
        {
            return null;
        }

        DateTimeOffset? noticia;
        try
        {
            noticia = _ultimaNoticia(Inners);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            return null;
        }

        // Notícia de antes desta subida é do processo anterior: conta a partir da subida.
        var referencia = noticia is { } n && n > iniciado ? n : iniciado;
        var silencio = _relogio() - referencia;
        return silencio > _toleranciaDoBatimento ? silencio : null;
    }

    public string Diagnostico
    {
        get
        {
            var idade = _iniciadoEm is { } quando
                ? string.Create(CultureInfo.InvariantCulture, $" · iniciado {(_relogio() - quando).TotalSeconds:F0}s atrás")
                : string.Empty;

            var silencio = EstaVivo && SilencioDoBatimento() is { } s
                ? string.Create(CultureInfo.InvariantCulture, $" · sem notícia na base há {s.TotalSeconds:F0}s")
                : string.Empty;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Nome} · porta {Porta} · {Inners.Count} equipamento(s) · {_ultimaSaida}{idade}{silencio}");
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

            // Segredo para o worker (a chave da impressão, Etapa A.9): pela entrada padrão,
            // nunca pela linha de comando, que qualquer processo da máquina lê.
            RedirectStandardInput = _entradaPadrao is not null,
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

        // O worker morre com o serviço (Job Object no Windows): sem isto, o serviço morto pelo
        // Gerenciador de Tarefas deixava o worker órfão gravando "em operação" na base (docs/29,
        // defeito de 01/10). Recusado, o worker roda assim mesmo — a vigia do pai (--pai) cobre —
        // e o motivo fica nas últimas linhas, para o diagnóstico.
        if (_contencao is not null && !_contencao.Conter(_processo))
        {
            Guardar($"[supervisor] worker fora da contenção ({_contencao.Descricao}).");
        }

        // A saída precisa ser LIDA enquanto o worker roda. Redirecionada e não lida, ela
        // enche o buffer do pipe (poucos KB) e o worker trava no próximo Console.WriteLine
        // — com a catraca parada e o processo "vivo". As últimas linhas ficam guardadas: são
        // a explicação de por que ele não subiu ou morreu.
        _processo.OutputDataReceived += (_, e) => Guardar(e.Data);
        _processo.ErrorDataReceived += (_, e) => Guardar(e.Data, erro: true);
        // O processo vai na closure: o campo pode já apontar para o worker seguinte quando o aviso de
        // saída deste chega (Matar + Iniciar na mesma ronda). Ler o ExitCode do novo, ainda vivo,
        // lançava InvalidOperationException numa thread do pool e derrubava o serviço inteiro.
        var processo = _processo;
        _processo.Exited += (_, _) => RegistrarSaida(processo);
        _processo.EnableRaisingEvents = true;
        _processo.BeginOutputReadLine();
        _processo.BeginErrorReadLine();

        if (_entradaPadrao is not null)
        {
            // Uma linha e fecha: o worker lê ao subir e não espera mais nada pela entrada.
            try
            {
                _processo.StandardInput.WriteLine(_entradaPadrao() ?? string.Empty);
                _processo.StandardInput.Close();
            }
            catch (IOException)
            {
                // O worker morreu antes de ler: a saída dele explica, e o supervisor reinicia.
            }
        }
    }

    public void Matar()
    {
        if (_processo is null || _processo.HasExited)
        {
            return;
        }

        try
        {
            _processo.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Saiu sozinho entre a conferência e o Kill.
        }

        // Kill só pede: o processo ainda aparece vivo por alguns instantes. Sem esperar, o Iniciar
        // que a supervisão chama logo em seguida achava o worker "vivo" e não subia outro, e a
        // catraca ficava parada até a ronda seguinte, gastando um reinício a mais (achado no teste
        // do batimento, E2-01 do docs/41).
        _processo.WaitForExit(TimeSpan.FromSeconds(5));
        _ultimaSaida = "morto pelo supervisor";
    }

    public void Dispose()
    {
        Matar();
        _processo?.Dispose();
        _processo = null;
    }

    /// <summary>Anota como o worker saiu. Roda numa thread do pool: nunca lança.</summary>
    internal void RegistrarSaida(Process processo)
    {
        int codigo;
        try
        {
            // Espera a leitura assíncrona terminar, senão a última linha de erro — a que
            // explica a saída — ainda não chegou.
            processo.WaitForExit();
            codigo = processo.ExitCode;
        }
        catch (Exception erro) when (erro is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            // Descartado pelo Dispose, ou sem código disponível: não há o que anotar.
            return;
        }

        // A saída de um worker anterior não sobrescreve a situação do atual.
        if (!ReferenceEquals(processo, _processo))
        {
            return;
        }

        var ultimoErro = _ultimoErro;
        _ultimaSaida = string.IsNullOrWhiteSpace(ultimoErro)
            ? $"encerrou com código {codigo}"
            : $"encerrou com código {codigo}: {ultimoErro}";
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
