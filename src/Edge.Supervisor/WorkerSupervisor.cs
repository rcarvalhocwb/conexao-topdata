using Edge.Worker.Resiliencia;

namespace Edge.Supervisor;

/// <summary>O que o supervisor fez com um worker numa passagem.</summary>
/// <param name="Worker">Nome do grupo.</param>
/// <param name="Situacao">Situação observada.</param>
/// <param name="Acao">O que foi feito, em português, para o log do operador.</param>
public sealed record AcaoDeSupervisao(string Worker, SituacaoDoWorker Situacao, string Acao);

/// <summary>
/// Mantém os workers de pé, isolando a falha de um grupo dos demais.
/// </summary>
/// <remarks>
/// <para>
/// É aqui que mora o isolamento real do produto. Dentro de um worker, uma catraca
/// travada bloqueia as outras — a DLL é sequencial. O que impede isso de virar uma
/// parada geral é o supervisor matar e recriar aquele worker, sem tocar nos demais.
/// Ver docs/ADR/ADR-0005-particionamento-por-worker.md
/// </para>
/// <para>
/// Reinício não é ilimitado: um worker que morre repetidamente tem um problema que
/// reinício não resolve — DLL ausente, arquitetura errada, porta ocupada. Depois do
/// limite ele vai para quarentena e o operador é avisado, em vez de o sistema entrar
/// num laço de reinícios que esconde a causa.
/// </para>
/// </remarks>
public sealed class WorkerSupervisor
{
    private readonly Dictionary<string, EstadoDeSupervisao> _estados = [];
    private readonly List<IWorkerHost> _workers;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly BackoffComJitter _backoff;
    private readonly int _reiniciosMaximos;
    private readonly TimeSpan _janelaDeReinicios;

    public WorkerSupervisor(
        IEnumerable<IWorkerHost> workers,
        Func<DateTimeOffset>? relogio = null,
        BackoffComJitter? backoff = null,
        int reiniciosMaximos = 5,
        TimeSpan? janelaDeReinicios = null)
    {
        ArgumentNullException.ThrowIfNull(workers);

        _workers = [.. workers];

        if (_workers.Count == 0)
        {
            throw new ArgumentException("Não há workers para supervisionar.", nameof(workers));
        }

        var portasRepetidas = _workers.GroupBy(w => w.Porta).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (portasRepetidas.Count > 0)
        {
            throw new ArgumentException(
                $"Porta TCP repetida entre workers: {string.Join(", ", portasRepetidas)}. " +
                "Cada worker precisa da sua, e a catraca aponta para ela. Ver docs/ADR/ADR-0021.",
                nameof(workers));
        }

        var innersRepetidos = _workers
            .SelectMany(w => w.Inners)
            .GroupBy(i => i)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (innersRepetidos.Count > 0)
        {
            throw new ArgumentException(
                $"Equipamento atribuído a mais de um worker: {string.Join(", ", innersRepetidos)}.",
                nameof(workers));
        }

        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
        _backoff = backoff ?? new BackoffComJitter(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(1));
        _reiniciosMaximos = reiniciosMaximos;
        _janelaDeReinicios = janelaDeReinicios ?? TimeSpan.FromMinutes(5);

        foreach (var worker in _workers)
        {
            _estados[worker.Nome] = new EstadoDeSupervisao();
        }
    }

    public IReadOnlyList<IWorkerHost> Workers => _workers;

    /// <summary>Situação atual de cada worker.</summary>
    public IReadOnlyDictionary<string, SituacaoDoWorker> Situacoes =>
        _workers.ToDictionary(w => w.Nome, Situacao, StringComparer.Ordinal);

    /// <summary>Sobe todos os workers.</summary>
    public void Iniciar()
    {
        foreach (var worker in _workers)
        {
            worker.Iniciar();
            _estados[worker.Nome].IniciadoEm = _relogio();
        }
    }

    /// <summary>
    /// Uma passagem de supervisão: observa cada worker e age no que precisa.
    /// </summary>
    /// <remarks>
    /// A falha de um worker nunca interrompe a passagem pelos outros — é isso que
    /// mantém os demais grupos operando.
    /// </remarks>
    public IReadOnlyList<AcaoDeSupervisao> Supervisionar()
    {
        var acoes = new List<AcaoDeSupervisao>(_workers.Count);
        var agora = _relogio();

        foreach (var worker in _workers)
        {
            try
            {
                acoes.Add(Supervisionar(worker, agora));
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                // Um grupo que explode ao ser inspecionado não pode levar os outros
                // junto. Vai para quarentena e o operador é avisado.
                _estados[worker.Nome].EmQuarentena = true;
                acoes.Add(new AcaoDeSupervisao(
                    worker.Nome,
                    SituacaoDoWorker.Quarentena,
                    $"falha ao supervisionar, grupo isolado: {erro.Message}"));
            }
        }

        return acoes;
    }

    /// <summary>Situação de um worker, já considerando a quarentena.</summary>
    public SituacaoDoWorker Situacao(IWorkerHost worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        var estado = _estados[worker.Nome];

        if (estado.EmQuarentena)
        {
            return SituacaoDoWorker.Quarentena;
        }

        if (estado.IniciadoEm is null)
        {
            return SituacaoDoWorker.Parado;
        }

        if (!worker.EstaVivo)
        {
            return SituacaoDoWorker.Morto;
        }

        return worker.EstaSaudavel ? SituacaoDoWorker.Saudavel : SituacaoDoWorker.SemBatimento;
    }

    /// <summary>Quantas vezes um worker foi reiniciado.</summary>
    public int Reinicios(string nomeDoWorker) => _estados[nomeDoWorker].Reinicios.Count;

    private AcaoDeSupervisao Supervisionar(IWorkerHost worker, DateTimeOffset agora)
    {
        var estado = _estados[worker.Nome];
        var situacao = Situacao(worker);

        if (situacao is SituacaoDoWorker.Quarentena)
        {
            return new AcaoDeSupervisao(worker.Nome, situacao, "em quarentena — exige ação humana");
        }

        if (situacao is SituacaoDoWorker.Saudavel)
        {
            return new AcaoDeSupervisao(worker.Nome, situacao, "ok");
        }

        if (estado.ReiniciarApos is { } quando && agora < quando)
        {
            return new AcaoDeSupervisao(worker.Nome, situacao, "aguardando backoff para reiniciar");
        }

        // Descarta reinícios fora da janela: um worker que reiniciou 4 vezes ao longo de
        // uma semana não é o mesmo caso de um que reiniciou 4 vezes em 5 minutos.
        estado.Reinicios.RemoveAll(r => agora - r > _janelaDeReinicios);

        if (estado.Reinicios.Count >= _reiniciosMaximos)
        {
            estado.EmQuarentena = true;
            return new AcaoDeSupervisao(
                worker.Nome,
                SituacaoDoWorker.Quarentena,
                $"reiniciou {estado.Reinicios.Count} vezes em {_janelaDeReinicios.TotalMinutes:F0} min — " +
                "reinício não resolve, isolado para diagnóstico");
        }

        var motivo = situacao is SituacaoDoWorker.SemBatimento
            ? $"laço travado ({worker.Diagnostico})"
            : "processo morto";

        // Matar antes de subir: com a DLL travada, pedir parada não adianta.
        worker.Matar();
        worker.Iniciar();

        estado.Reinicios.Add(agora);
        estado.IniciadoEm = agora;
        estado.ReiniciarApos = agora + _backoff.Para(estado.Reinicios.Count);

        return new AcaoDeSupervisao(
            worker.Nome,
            situacao,
            $"reiniciado ({motivo}); reinício {estado.Reinicios.Count} de {_reiniciosMaximos}");
    }

    private sealed class EstadoDeSupervisao
    {
        public DateTimeOffset? IniciadoEm { get; set; }

        public DateTimeOffset? ReiniciarApos { get; set; }

        public bool EmQuarentena { get; set; }

        public List<DateTimeOffset> Reinicios { get; } = [];
    }
}
