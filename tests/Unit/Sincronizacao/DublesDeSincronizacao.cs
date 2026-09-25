using Sync.Core;

namespace Unit.Tests.Sincronizacao;

/// <summary>Relógio que só anda quando o teste mandar.</summary>
internal sealed class RelogioDeTeste(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _agora = inicio;

    public override DateTimeOffset GetUtcNow() => _agora;

    public void Avancar(TimeSpan quanto) => _agora += quanto;
}

/// <summary>
/// A outbox em memória, com a mesma semântica da de SQLite.
/// </summary>
/// <remarks>
/// Serve para testar a política de drenagem sem banco. As regras que ela reproduz de
/// propósito: ordem por prioridade e depois por chegada, item adiado invisível até a
/// hora, e item em cartas mortas fora da fila mas preservado.
/// </remarks>
internal sealed class FilaEmMemoria : IFilaDeSaida
{
    private readonly List<Linha> _linhas = [];

    public List<(string Id, string Erro)> CartasMortas { get; } = [];

    public List<(string Id, int Tentativas, DateTimeOffset Proxima)> Adiamentos { get; } = [];

    public int Consultas { get; private set; }

    public void Enfileirar(
        string id,
        string conector,
        int prioridade,
        DateTimeOffset criadoEm,
        int tentativas = 0,
        DateTimeOffset? proximaTentativaEm = null)
    {
        _linhas.Add(new Linha
        {
            Id = id,
            Conector = conector,
            Prioridade = prioridade,
            CriadoEm = criadoEm,
            Tentativas = tentativas,
            ProximaTentativaEm = proximaTentativaEm,
        });
    }

    public IReadOnlyList<string> Pendentes() =>
        [.. _linhas.Where(l => l.EnviadoEm is null).Select(l => l.Id)];

    public IReadOnlyList<string> Enviados() =>
        [.. _linhas.Where(l => l.EnviadoEm is not null).Select(l => l.Id)];

    public Task<IReadOnlyList<string>> ConectoresComPendenciaAsync(
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        IReadOnlyList<string> conectores =
        [
            .. _linhas
                .Where(l => Vencido(l, agora))
                .GroupBy(l => l.Conector, StringComparer.Ordinal)
                .OrderBy(g => g.Min(l => l.Prioridade))
                .ThenBy(g => g.Min(l => l.CriadoEm))
                .Select(g => g.Key),
        ];

        return Task.FromResult(conectores);
    }

    public Task<IReadOnlyList<ItemDeSaida>> ProximosAsync(
        string conector,
        int limite,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        Consultas++;

        IReadOnlyList<ItemDeSaida> itens =
        [
            .. _linhas
                .Where(l => Vencido(l, agora) && string.Equals(l.Conector, conector, StringComparison.Ordinal))
                .OrderBy(l => l.Prioridade)
                .ThenBy(l => l.CriadoEm)
                .Take(limite)
                .Select(l => new ItemDeSaida(
                    l.Id,
                    l.Conector,
                    "teste",
                    l.Id,
                    "{}",
                    l.Prioridade,
                    $"chave-{l.Id}",
                    l.Tentativas,
                    l.CriadoEm)),
        ];

        return Task.FromResult(itens);
    }

    public Task MarcarEnviadosAsync(
        IReadOnlyCollection<string> ids,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        foreach (var linha in _linhas.Where(l => ids.Contains(l.Id)))
        {
            linha.EnviadoEm = agora;
        }

        return Task.CompletedTask;
    }

    public Task AdiarAsync(
        string id,
        int tentativas,
        DateTimeOffset proximaTentativaEm,
        string erro,
        CancellationToken cancelamento)
    {
        var linha = _linhas.Single(l => l.Id == id);
        linha.Tentativas = tentativas;
        linha.ProximaTentativaEm = proximaTentativaEm;
        Adiamentos.Add((id, tentativas, proximaTentativaEm));
        return Task.CompletedTask;
    }

    public Task MoverParaCartasMortasAsync(
        string id,
        string erro,
        DateTimeOffset agora,
        CancellationToken cancelamento)
    {
        _linhas.RemoveAll(l => l.Id == id);
        CartasMortas.Add((id, erro));
        return Task.CompletedTask;
    }

    private static bool Vencido(Linha l, DateTimeOffset agora) =>
        l.EnviadoEm is null && (l.ProximaTentativaEm is null || l.ProximaTentativaEm <= agora);

    private sealed class Linha
    {
        public required string Id { get; init; }

        public required string Conector { get; init; }

        public required int Prioridade { get; init; }

        public required DateTimeOffset CriadoEm { get; init; }

        public int Tentativas { get; set; }

        public DateTimeOffset? ProximaTentativaEm { get; set; }

        public DateTimeOffset? EnviadoEm { get; set; }
    }
}

/// <summary>Conector programável: o teste diz o que cada item deve responder.</summary>
internal sealed class ConectorFalso(
    string nome,
    Func<ItemDeSaida, ResultadoDoEnvio> veredito,
    int tamanhoMaximoDoLote = 100) : IConectorDeSincronizacao
{
    public string Nome { get; } = nome;

    public int TamanhoMaximoDoLote { get; } = tamanhoMaximoDoLote;

    public List<IReadOnlyList<ItemDeSaida>> Lotes { get; } = [];

    /// <summary>Quando definido, a chamada lança em vez de responder.</summary>
    public Func<Exception>? Explodir { get; init; }

    /// <summary>Quando verdadeiro, responde só o primeiro item do lote.</summary>
    public bool ResponderSoOPrimeiro { get; init; }

    public Task<IReadOnlyList<RespostaDeItem>> EnviarAsync(
        IReadOnlyList<ItemDeSaida> lote,
        CancellationToken cancelamento)
    {
        Lotes.Add(lote);

        if (Explodir is not null)
        {
            throw Explodir();
        }

        var considerados = ResponderSoOPrimeiro ? lote.Take(1) : lote;

        IReadOnlyList<RespostaDeItem> respostas =
        [
            .. considerados.Select(i => new RespostaDeItem(i.Id, veredito(i), "detalhe de teste")),
        ];

        return Task.FromResult(respostas);
    }
}
