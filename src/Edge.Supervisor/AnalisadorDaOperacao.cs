using System.Diagnostics;
using Access.Infrastructure.SQLite;
using Access.Inteligencia;
using Microsoft.Extensions.Hosting;

namespace Edge.Supervisor;

/// <summary>
/// Um ciclo do Analisador: o que ele lê e grava. Separado do laço para o teste de caos
/// (<c>NOVO-CHAOS-IA-01</c>) poder pôr no lugar um ciclo que lança ou que trava.
/// </summary>
public interface ICicloDoAnalisador
{
    /// <summary>A chave <see cref="ChavesDaInteligencia.Ligada"/> está ligada nesta instalação.</summary>
    bool Ligada();

    /// <summary>Faz um ciclo; devolve quantas tentativas novas leu. Pode lançar: o laço segura.</summary>
    long Executar(CancellationToken cancelamento);
}

/// <summary>
/// O ciclo de verdade da Etapa I.0: segue as tentativas novas de <c>acesso.db</c> pelo
/// <c>rowid</c>, só lendo, e registra o ciclo no caderno de <c>telemetria.db</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ainda não calcula nada: é a fundação por onde as próximas etapas passam (I.3 em diante, docs/36
/// §5), com as guardas já provadas — base da operação só para leitura (I2), escrita só em
/// <c>telemetria.db</c>, orçamento e falha contida.
/// </para>
/// <para>
/// Começa do fim, como o <see cref="AcompanhamentoDaOperacao"/>: na partida, o cursor vai para a
/// última tentativa já gravada. A reconstrução da última hora fica para quando houver o que
/// reconstruir (I.3, docs/36-anexos/02 §3.4, "Reinício do serviço").
/// </para>
/// </remarks>
public sealed class CicloSobreABase : ICicloDoAnalisador
{
    private readonly LeituraSomenteDaOperacao _leitura;
    private readonly FabricaDaTelemetria _telemetria;
    private readonly CadernoDoAnalisador _caderno;
    private readonly string? _sessao;
    private readonly TimeProvider _relogio;
    private readonly OrcamentoDoCiclo _orcamento;
    private bool _migrada;
    private long? _cursor;

    public CicloSobreABase(
        LeituraSomenteDaOperacao leitura,
        FabricaDaTelemetria telemetria,
        string? sessao = null,
        TimeProvider? relogio = null,
        OrcamentoDoCiclo? orcamento = null)
    {
        ArgumentNullException.ThrowIfNull(leitura);
        ArgumentNullException.ThrowIfNull(telemetria);
        _leitura = leitura;
        _telemetria = telemetria;
        _caderno = new CadernoDoAnalisador(telemetria);
        _sessao = sessao;
        _relogio = relogio ?? TimeProvider.System;
        _orcamento = orcamento ?? OrcamentoDoCiclo.CicloCurto;
    }

    public bool Ligada() => ChavesDaInteligencia.EstaLigada(_leitura.ValorDaChave(ChavesDaInteligencia.Ligada));

    public long Executar(CancellationToken cancelamento)
    {
        var inicio = _relogio.GetTimestamp();

        // O arquivo só nasce com a camada ligada. Se a migração falhar (arquivo preso por outro
        // processo, disco cheio), o ciclo falha, o laço conta, e o próximo tenta de novo.
        if (!_migrada)
        {
            new MigradorDaTelemetria(_telemetria).Aplicar();
            _migrada = true;
        }

        cancelamento.ThrowIfCancellationRequested();

        var (novas, ultima) = _cursor is { } cursor
            ? _leitura.TentativasDepoisDe(cursor)
            : (0L, _leitura.UltimaTentativa());

        var duracao = _relogio.GetElapsedTime(inicio);
        _caderno.Registrar(new CicloRegistrado(
            _sessao, _relogio.GetUtcNow(), duracao, novas, ultima, _orcamento.Estourou(duracao)));

        // O cursor só anda depois de o ciclo ficar registrado: um ciclo que falhou ao gravar não
        // "consome" as tentativas que leu, e o seguinte as conta de novo.
        _cursor = ultima;
        return novas;
    }
}

/// <summary>
/// O Analisador da camada inteligente (Etapa I.0 do docs/36): um <see cref="BackgroundService"/>
/// do serviço, de prioridade baixa e com orçamento por ciclo, que lê a base da operação só para
/// leitura e grava só em <c>telemetria.db</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nunca derruba o serviço nem toca no worker</b> (invariante I3, docs/36-anexos/02 §3.1;
/// <c>NOVO-CHAOS-IA-01</c>):
/// </para>
/// <list type="bullet">
/// <item>roda numa thread própria, <see cref="ThreadPriority.BelowNormal"/>, fora do pool que
/// atende o painel; uma exceção do ciclo vira contagem e tipo do erro no Diagnóstico, e o laço
/// segue;</item>
/// <item>um ciclo que trava prende só a thread dele: a parada do serviço não espera por ela (a
/// thread é de fundo e <see cref="ExecuteAsync"/> termina com o pedido de parada);</item>
/// <item>o worker não referencia nada disto (<c>NOVO-ARQ-IA-01</c>) e a base da operação é aberta
/// só para leitura (<c>NOVO-ARQ-IA-02</c>): não existe caminho para a camada mudar uma chamada à
/// catraca ou uma decisão.</item>
/// </list>
/// <para>
/// <b>Desligado por padrão</b> (chave <see cref="ChavesDaInteligencia.Ligada"/>, sem tela). A chave
/// é lida uma vez, na partida: desligada, o Analisador não roda — não lê a base, não cria
/// <c>telemetria.db</c>. Ligar é ensaio de bancada e pede reinício do serviço.
/// </para>
/// <para>
/// Orçamento (§3.4): um ciclo acima do orçamento conta um estouro e faz o seguinte ser pulado.
/// </para>
/// </remarks>
public sealed class AnalisadorDaOperacao : BackgroundService
{
    /// <summary>A cadência do ciclo curto (§3.4).</summary>
    public static readonly TimeSpan IntervaloPadrao = TimeSpan.FromSeconds(1);

    private readonly ICicloDoAnalisador _ciclo;
    private readonly TimeProvider _relogio;
    private readonly TimeSpan _intervalo;
    private readonly OrcamentoDoCiclo _orcamento;
    private readonly object _publicacao = new();
    private SituacaoDoAnalisador _situacao;
    private bool _pularProximo;

    public AnalisadorDaOperacao(
        ICicloDoAnalisador ciclo,
        TimeProvider? relogio = null,
        TimeSpan? intervalo = null,
        OrcamentoDoCiclo? orcamento = null)
    {
        ArgumentNullException.ThrowIfNull(ciclo);
        _ciclo = ciclo;
        _relogio = relogio ?? TimeProvider.System;
        _intervalo = intervalo ?? IntervaloPadrao;
        _orcamento = orcamento ?? OrcamentoDoCiclo.CicloCurto;
        _situacao = SituacaoDoAnalisador.Inicial(_orcamento);
    }

    /// <summary>A última situação publicada pelo laço; lida pelo Diagnóstico.</summary>
    public SituacaoDoAnalisador Situacao => Volatile.Read(ref _situacao);

    /// <summary>
    /// Confere a chave. Lê uma vez, na partida; falha ao ler conta como desligada (e o tipo do erro
    /// aparece no Diagnóstico): na dúvida, a camada não roda.
    /// </summary>
    public bool ConferirChave()
    {
        bool ligada;
        try
        {
            ligada = _ciclo.Ligada();
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            Publicar(s => s.ComFalha(_relogio.GetUtcNow(), TimeSpan.Zero, erro, estourou: false) with { Ligada = false });
            return false;
        }

        Publicar(s => s with { Ligada = ligada });
        return ligada;
    }

    /// <summary>
    /// Uma volta do laço: faz um ciclo, ou pula este se o anterior estourou o orçamento. Nunca
    /// lança (a não ser o cancelamento pedido).
    /// </summary>
    public void UmaVolta(CancellationToken cancelamento = default)
    {
        if (_pularProximo)
        {
            _pularProximo = false;
            Publicar(s => s with { Pulados = s.Pulados + 1 });
            return;
        }

        var inicio = _relogio.GetTimestamp();

        try
        {
            var novas = _ciclo.Executar(cancelamento);
            var duracao = _relogio.GetElapsedTime(inicio);
            var estourou = _orcamento.Estourou(duracao);
            _pularProximo = estourou;
            Publicar(s => s.ComCiclo(_relogio.GetUtcNow(), duracao, novas, estourou));
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            var duracao = _relogio.GetElapsedTime(inicio);
            var estourou = _orcamento.Estourou(duracao);
            _pularProximo = estourou;
            Publicar(s => s.ComFalha(_relogio.GetUtcNow(), duracao, erro, estourou));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fim = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Laco(stoppingToken);
            }
            finally
            {
                fim.TrySetResult();
            }
        })
        {
            IsBackground = true,
            Name = "Analisador da operação",
        };

        try
        {
            thread.Priority = ThreadPriority.BelowNormal;
        }
        catch (Exception erro) when (erro is ThreadStateException or PlatformNotSupportedException)
        {
            // Sem prioridade baixa o laço roda igual; o orçamento continua valendo.
        }

        thread.Start();

        // Um ciclo travado não segura a parada do serviço: a espera acaba com o pedido de parada,
        // e a thread de fundo morre com o processo.
        await Task.WhenAny(fim.Task, Task.Delay(Timeout.Infinite, stoppingToken)).ConfigureAwait(false);
    }

    private void Laco(CancellationToken parar)
    {
        try
        {
            if (!ConferirChave())
            {
                return;
            }

            Publicar(s => s with { Rodando = true });

            while (!parar.IsCancellationRequested)
            {
                UmaVolta(parar);
                parar.WaitHandle.WaitOne(_intervalo);
            }
        }
        catch (OperationCanceledException) when (parar.IsCancellationRequested)
        {
            // Parada pedida no meio de um ciclo.
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Nada pode sair desta thread: uma exceção solta numa thread própria derruba o
            // processo inteiro — e o processo é o serviço que atende o painel.
            Debug.WriteLine($"Analisador parou: {erro.GetType().Name}");
            Publicar(s => s.ComFalha(_relogio.GetUtcNow(), TimeSpan.Zero, erro, estourou: false));
        }
        finally
        {
            Publicar(s => s with { Rodando = false });
        }
    }

    private void Publicar(Func<SituacaoDoAnalisador, SituacaoDoAnalisador> mudar)
    {
        lock (_publicacao)
        {
            Volatile.Write(ref _situacao, mudar(_situacao));
        }
    }
}
