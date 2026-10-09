using System.Globalization;
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
    private readonly Lock _trava = new();
    private bool _encerrado;
    private readonly TimeSpan _duracaoDaQuarentena;

    /// <summary>Quanto tempo um grupo fica isolado antes de uma nova tentativa automática.</summary>
    /// <remarks>
    /// Achado E2-03 do docs/41: a quarentena não tinha saída; só reiniciar o serviço, o que exige
    /// administrador, tirava um grupo dela. Agora ela termina sozinha depois deste tempo (e de novo
    /// entra, se o grupo continuar caindo), e o operador pode antecipar pelo painel.
    /// </remarks>
    public static readonly TimeSpan DuracaoPadraoDaQuarentena = TimeSpan.FromMinutes(15);

    public WorkerSupervisor(
        IEnumerable<IWorkerHost> workers,
        Func<DateTimeOffset>? relogio = null,
        BackoffComJitter? backoff = null,
        int reiniciosMaximos = 5,
        TimeSpan? janelaDeReinicios = null,
        TimeSpan? duracaoDaQuarentena = null)
    {
        ArgumentNullException.ThrowIfNull(workers);

        // Lista vazia é permitida: é o serviço recém-instalado, ainda sem configuração. Ele
        // sobe, atende o painel e diz o que falta, em vez de cair na subida.
        _workers = [.. workers];

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
        _duracaoDaQuarentena = duracaoDaQuarentena ?? DuracaoPadraoDaQuarentena;

        foreach (var worker in _workers)
        {
            _estados[worker.Nome] = new EstadoDeSupervisao();
        }
    }

    public IReadOnlyList<IWorkerHost> Workers => _workers;

    /// <summary>Situação atual de cada worker.</summary>
    public IReadOnlyDictionary<string, SituacaoDoWorker> Situacoes =>
        _workers.ToDictionary(w => w.Nome, Situacao, StringComparer.Ordinal);

    /// <summary>Sobe os workers que ainda não foram iniciados.</summary>
    /// <remarks>
    /// Um grupo que não sobe (o executável bloqueado pelo antivírus ou preso numa atualização, por
    /// exemplo) não impede os outros: ele fica como "morto", e a supervisão tenta de novo com backoff.
    /// Antes, a exceção saía daqui, nenhum grupo seguinte subia e o laço de supervisão morria (achado
    /// E2-02 do docs/41). Chamar de novo não sobe um worker duas vezes.
    /// </remarks>
    /// <returns>O que aconteceu com cada worker iniciado agora.</returns>
    public IReadOnlyList<AcaoDeSupervisao> Iniciar()
    {
        lock (_trava)
        {
            if (_encerrado)
            {
                return [];
            }

            var acoes = new List<AcaoDeSupervisao>(_workers.Count);

            foreach (var worker in _workers)
            {
                var estado = _estados[worker.Nome];
                if (estado.IniciadoEm is not null)
                {
                    continue;
                }

                estado.IniciadoEm = _relogio();

                try
                {
                    worker.Iniciar();
                    acoes.Add(new AcaoDeSupervisao(worker.Nome, Situacao(worker), "iniciado"));
                }
                catch (Exception erro) when (erro is not OutOfMemoryException)
                {
                    acoes.Add(new AcaoDeSupervisao(
                        worker.Nome,
                        SituacaoDoWorker.Morto,
                        $"não iniciou ({erro.Message}); a supervisão tenta de novo"));
                }
            }

            return acoes;
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
        lock (_trava)
        {
            return _encerrado ? [] : SupervisionarTodos();
        }
    }

    /// <summary>
    /// Mata todos os workers e não sobe mais nenhum. O serviço chama ao parar.
    /// </summary>
    /// <remarks>
    /// No Windows o processo filho não morre com o pai: sem isto, parar o serviço deixava o
    /// programa das catracas rodando sozinho — segurando os arquivos na atualização e a
    /// porta TCP na próxima partida.
    /// </remarks>
    public void Encerrar()
    {
        lock (_trava)
        {
            _encerrado = true;

            foreach (var worker in _workers)
            {
                try
                {
                    worker.Matar();
                }
                catch (Exception erro) when (erro is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Já tinha saído: nada a matar.
                }
            }
        }
    }

    private List<AcaoDeSupervisao> SupervisionarTodos()
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
                // Um grupo que explode ao ser inspecionado não pode levar os outros junto. A falha
                // conta como um reinício que não deu certo: com espera, e quarentena só se repetir
                // (antes, a primeira exceção já isolava o grupo; achado E2-03).
                acoes.Add(FalhaAoSupervisionar(worker, agora, erro));
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

    /// <summary>
    /// Tira o grupo da quarentena agora, a pedido do operador: a próxima ronda tenta subir o worker.
    /// </summary>
    /// <returns>Falso se o grupo não existe ou não estava em quarentena.</returns>
    public bool TentarDeNovo(string nomeDoWorker)
    {
        lock (_trava)
        {
            if (!_estados.TryGetValue(nomeDoWorker, out var estado) || !estado.EmQuarentena)
            {
                return false;
            }

            SairDaQuarentena(estado);
            return true;
        }
    }

    private static void SairDaQuarentena(EstadoDeSupervisao estado)
    {
        estado.EmQuarentena = false;
        estado.QuarentenaDesde = null;
        estado.Reinicios.Clear();
        estado.ReiniciarApos = null;
    }

    private static void EntrarEmQuarentena(EstadoDeSupervisao estado, DateTimeOffset agora)
    {
        estado.EmQuarentena = true;
        estado.QuarentenaDesde = agora;
    }

    private AcaoDeSupervisao FalhaAoSupervisionar(IWorkerHost worker, DateTimeOffset agora, Exception erro)
    {
        var estado = _estados[worker.Nome];

        if (estado.EmQuarentena)
        {
            return new AcaoDeSupervisao(worker.Nome, SituacaoDoWorker.Quarentena, $"em quarentena: {erro.Message}");
        }

        if (estado.ReiniciarApos is { } quando && agora < quando)
        {
            return new AcaoDeSupervisao(worker.Nome, SituacaoDoWorker.Morto, $"falha ao supervisionar ({erro.Message}); aguardando backoff");
        }

        estado.Reinicios.RemoveAll(r => agora - r > _janelaDeReinicios);
        estado.Reinicios.Add(agora);

        if (estado.Reinicios.Count >= _reiniciosMaximos)
        {
            EntrarEmQuarentena(estado, agora);
            return new AcaoDeSupervisao(
                worker.Nome,
                SituacaoDoWorker.Quarentena,
                $"falha ao supervisionar repetida ({erro.Message}); grupo isolado por {_duracaoDaQuarentena.TotalMinutes:F0} min");
        }

        estado.ReiniciarApos = agora + _backoff.Para(estado.Reinicios.Count);
        return new AcaoDeSupervisao(worker.Nome, SituacaoDoWorker.Morto, $"falha ao supervisionar ({erro.Message}); nova tentativa com espera");
    }

    private AcaoDeSupervisao Supervisionar(IWorkerHost worker, DateTimeOffset agora)
    {
        var estado = _estados[worker.Nome];

        if (estado.EmQuarentena && estado.QuarentenaDesde is { } desde && agora - desde >= _duracaoDaQuarentena)
        {
            SairDaQuarentena(estado);
            worker.Matar();
            worker.Iniciar();
            estado.IniciadoEm = agora;
            estado.Reinicios.Add(agora);
            estado.ReiniciarApos = agora + _backoff.Para(estado.Reinicios.Count);
            return new AcaoDeSupervisao(
                worker.Nome,
                Situacao(worker),
                $"fim da quarentena de {_duracaoDaQuarentena.TotalMinutes:F0} min: nova tentativa");
        }

        var situacao = Situacao(worker);

        if (situacao is SituacaoDoWorker.Quarentena)
        {
            var volta = estado.QuarentenaDesde is { } d ? d + _duracaoDaQuarentena : agora;
            return new AcaoDeSupervisao(
                worker.Nome,
                situacao,
                string.Create(CultureInfo.InvariantCulture, $"em quarentena; nova tentativa automática às {volta.ToLocalTime():HH:mm}"));
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
            EntrarEmQuarentena(estado, agora);
            return new AcaoDeSupervisao(
                worker.Nome,
                SituacaoDoWorker.Quarentena,
                $"reiniciou {estado.Reinicios.Count} vezes em {_janelaDeReinicios.TotalMinutes:F0} min — " +
                $"reinício não resolve, isolado por {_duracaoDaQuarentena.TotalMinutes:F0} min para diagnóstico");
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

        public DateTimeOffset? QuarentenaDesde { get; set; }

        public List<DateTimeOffset> Reinicios { get; } = [];
    }
}
