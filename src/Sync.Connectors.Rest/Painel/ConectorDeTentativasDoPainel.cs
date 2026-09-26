using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Access.Domain.Ticketing;
using Sync.Core;

namespace Sync.Connectors.Rest.Painel;

/// <summary>
/// Envia ao painel na nuvem as tentativas decididas na borda.
/// </summary>
/// <remarks>
/// <para>
/// É o sentido "sobe" da ADR-0023. A decisão já aconteceu aqui; o painel só registra.
/// Nada deste conector está no caminho da catraca.
/// </para>
/// <para>
/// O formato está descrito em docs/22-sistema-supabase-atual.md, seção 8. Três
/// características do servidor mandam no desenho:
/// </para>
/// <list type="bullet">
/// <item>
/// Um <c>device_id</c> por requisição, até 100 eventos. O lote é agrupado por
/// equipamento, e cada grupo vira uma requisição.
/// </item>
/// <item>
/// O servidor <b>descarta o <c>event_id</c></b> e reconhece repetição por equipamento +
/// cartão + horário. Por isso o horário vai sempre com a mesma grafia, em milissegundos:
/// reenviar depois de uma resposta perdida cai como repetido, e não como entrada nova.
/// </item>
/// <item>
/// A resposta é <b>200 mesmo com falha parcial</b>, e a lista <c>failed_events</c> diz
/// quais falharam. Ler só o código de situação daria por entregue o que não entrou.
/// </item>
/// </list>
/// <para>
/// A credencial não passa por aqui: quem põe o cabeçalho é o <see cref="HttpClient"/>.
/// </para>
/// </remarks>
public sealed class ConectorDeTentativasDoPainel : IConectorDeSincronizacao
{
    /// <summary>Limite do servidor por requisição.</summary>
    public const int MaximoPorRequisicao = 100;

    private readonly HttpClient _http;
    private readonly string _caminho;
    private readonly Func<string, string> _equipamentoNoPainel;

    /// <param name="http">Cliente já apontado para a base das funções.</param>
    /// <param name="nome">Nome do conector na outbox. O mesmo de <c>EspelhoDeTentativas</c>.</param>
    /// <param name="caminho">Caminho da função, relativo à base.</param>
    /// <param name="equipamentoNoPainel">
    /// Traduz o equipamento da borda para o <c>device_id</c> do painel. Sem tradução, vai
    /// o mesmo nome.
    /// </param>
    public ConectorDeTentativasDoPainel(
        HttpClient http,
        string nome = "painel-tentativas",
        string caminho = "middleware-sync-events",
        Func<string, string>? equipamentoNoPainel = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);

        _http = http;
        Nome = nome;
        _caminho = caminho;
        _equipamentoNoPainel = equipamentoNoPainel ?? (d => d);
    }

    /// <inheritdoc />
    public string Nome { get; }

    /// <inheritdoc />
    public int TamanhoMaximoDoLote => MaximoPorRequisicao;

    /// <summary>
    /// Grafia do horário enviada ao painel: UTC, milissegundos, sufixo Z.
    /// </summary>
    /// <remarks>Fixa de propósito: é parte da chave de repetição do lado de lá.</remarks>
    public static string Horario(DateTimeOffset instante) =>
        instante.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RespostaDeItem>> EnviarAsync(
        IReadOnlyList<ItemDeSaida> lote,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(lote);

        var respostas = new List<RespostaDeItem>(lote.Count);
        var legiveis = new List<(ItemDeSaida Item, TentativaEspelhada Tentativa)>(lote.Count);

        foreach (var item in lote)
        {
            try
            {
                legiveis.Add((item, TentativaEspelhada.DeJson(item.PayloadJson)));
            }
            catch (FormatException)
            {
                // Não vai ficar legível repetindo. Carta morta, com o motivo — sem o
                // conteúdo, que tem o número do cartão.
                respostas.Add(new RespostaDeItem(item.Id, ResultadoDoEnvio.FalhaPermanente, "conteúdo da outbox ilegível"));
            }
        }

        foreach (var grupo in legiveis.GroupBy(l => _equipamentoNoPainel(l.Tentativa.Dispositivo), StringComparer.Ordinal))
        {
            cancelamento.ThrowIfCancellationRequested();
            respostas.AddRange(await EnviarGrupoAsync(grupo.Key, [.. grupo], cancelamento).ConfigureAwait(false));
        }

        return respostas;
    }

    private async Task<IEnumerable<RespostaDeItem>> EnviarGrupoAsync(
        string equipamento,
        IReadOnlyList<(ItemDeSaida Item, TentativaEspelhada Tentativa)> grupo,
        CancellationToken cancelamento)
    {
        var eventos = grupo.Select(g => Evento(g.Tentativa)).ToList();
        var pedido = new { device_id = equipamento, events = eventos, sync_reason = "batch" };

        HttpResponseMessage resposta;
        try
        {
            resposta = await _http.PostAsJsonAsync(_caminho, pedido, cancelamento).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Todos(grupo, ResultadoDoEnvio.FalhaTemporaria, "tempo de resposta esgotado");
        }
        catch (HttpRequestException erro)
        {
            return Todos(grupo, ResultadoDoEnvio.FalhaTemporaria, ClassificacaoHttp.Resumir(erro));
        }

        using (resposta)
        {
            if (!resposta.IsSuccessStatusCode)
            {
                var resultado = ClassificacaoHttp.ValeRepetir(resposta.StatusCode)
                    ? ResultadoDoEnvio.FalhaTemporaria
                    : ResultadoDoEnvio.FalhaPermanente;
                return Todos(grupo, resultado, ClassificacaoHttp.Detalhe(resposta.StatusCode));
            }

            var corpo = await resposta.Content.ReadAsByteArrayAsync(cancelamento).ConfigureAwait(false);
            var falhas = Falhas(corpo);

            if (falhas is null)
            {
                // 200 com corpo que não se entende: não dá para saber o que entrou.
                // Reenviar é seguro — o que já entrou volta como repetido.
                return Todos(grupo, ResultadoDoEnvio.FalhaTemporaria, "resposta do painel ilegível");
            }

            var reconhecidas = grupo.Count(g => falhas.Value.Lista.Contains((g.Tentativa.Codigo, Horario(g.Tentativa.Em))));

            if (reconhecidas != falhas.Value.Contagem)
            {
                // O servidor disse que N falharam e não conseguimos apontar quais. Dar
                // os outros por entregues seria perder evento em silêncio; reenviar o
                // grupo inteiro é seguro, porque o que entrou volta como repetido.
                return Todos(grupo, ResultadoDoEnvio.FalhaTemporaria, "falhas do painel não identificadas");
            }

            return grupo.Select(g =>
                falhas.Value.Lista.Contains((g.Tentativa.Codigo, Horario(g.Tentativa.Em)))
                    // O servidor gravou o motivo na tabela de rejeitados dele. Aqui vai
                    // só o fato, sem o texto do banco, que pode repetir o cartão.
                    ? new RespostaDeItem(g.Item.Id, ResultadoDoEnvio.FalhaPermanente, "recusado pelo painel")
                    : new RespostaDeItem(g.Item.Id, ResultadoDoEnvio.Aceito));
        }
    }

    private static Dictionary<string, object?> Evento(TentativaEspelhada t) => new()
    {
        ["event_id"] = t.Tentativa.ToString(),
        ["card_id"] = t.Codigo,
        ["occurred_at"] = Horario(t.Em),
        ["authorized"] = t.Liberado,
        ["reason"] = t.Motivo,
        ["admission_type"] = t.Categoria,
        ["extra"] = new Dictionary<string, object?>
        {
            ["origem"] = "borda",
            ["tentativa"] = t.Tentativa.ToString(),
            ["portao"] = t.Portao,
            ["provedor"] = t.Provedor,
            ["giro_confirmado"] = t.GiroEm is not null,
            ["giro_em"] = t.GiroEm is { } g ? Horario(g) : null,
        },
    };

    private static (HashSet<(string Cartao, string Horario)> Lista, int Contagem)? Falhas(byte[] corpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var raiz = documento.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("saved", out _)
                || !raiz.TryGetProperty("failed", out var quantas) || quantas.ValueKind != JsonValueKind.Number
                || !quantas.TryGetInt32(out var contagem))
            {
                return null;
            }

            var falhas = new HashSet<(string, string)>();

            if (raiz.TryGetProperty("failed_events", out var lista) && lista.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in lista.EnumerateArray())
                {
                    if (f.ValueKind == JsonValueKind.Object
                        && f.TryGetProperty("card_id", out var c) && c.ValueKind == JsonValueKind.String
                        && f.TryGetProperty("occurred_at", out var o) && o.ValueKind == JsonValueKind.String)
                    {
                        falhas.Add((c.GetString()!, o.GetString()!));
                    }
                }
            }

            return (falhas, contagem);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<RespostaDeItem> Todos(
        IEnumerable<(ItemDeSaida Item, TentativaEspelhada Tentativa)> grupo,
        ResultadoDoEnvio resultado,
        string erro) =>
        [.. grupo.Select(g => new RespostaDeItem(g.Item.Id, resultado, erro))];
}
