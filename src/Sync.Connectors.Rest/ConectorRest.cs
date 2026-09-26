using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Sync.Core;

namespace Sync.Connectors.Rest;

/// <summary>Como este conector fala com um destino HTTP.</summary>
/// <param name="Nome">Nome de roteamento, igual ao gravado na outbox.</param>
/// <param name="Caminho">
/// Caminho relativo à base do <see cref="HttpClient"/>. Fica aqui, e não no código,
/// porque muda de provedor para provedor.
/// </param>
/// <param name="TamanhoMaximoDoLote">
/// Quantos itens o drenador pode entregar de uma vez. O envio continua sendo um por
/// requisição — é o limite do destino que manda.
/// </param>
/// <param name="CabecalhoDeIdempotencia">
/// Cabeçalho onde vai a chave de idempotência. <c>Idempotency-Key</c> é o mais comum,
/// mas cada API escolhe o seu.
/// </param>
/// <param name="StatusDeDuplicado">
/// Situações em que o destino diz "já tenho este". Contam como sucesso.
/// <c>409 Conflict</c> é o mais comum.
/// </param>
public sealed record ConfiguracaoDoConectorRest(
    string Nome,
    string Caminho,
    int TamanhoMaximoDoLote = 100,
    string CabecalhoDeIdempotencia = "Idempotency-Key",
    IReadOnlySet<HttpStatusCode>? StatusDeDuplicado = null)
{
    /// <summary>Situações tratadas como "o destino já tinha".</summary>
    public IReadOnlySet<HttpStatusCode> Duplicados =>
        StatusDeDuplicado ?? new HashSet<HttpStatusCode> { HttpStatusCode.Conflict };
}

/// <summary>
/// Entrega itens da outbox a um destino HTTP, um por requisição.
/// </summary>
/// <remarks>
/// <para>
/// <b>Este conector não conhece credencial nenhuma.</b> Autenticação é responsabilidade
/// do <see cref="HttpClient"/> que lhe entregam — um <c>DelegatingHandler</c> no ponto de
/// composição põe o cabeçalho. Isso não é elegância: é o que garante que um segredo não
/// pode vazar por aqui, porque ele nunca passa por aqui.
/// </para>
/// <para>
/// O corpo enviado é <b>exatamente</b> o <c>payload_json</c> da outbox, sem reescrita. Se
/// o destino espera outro formato, quem monta o conteúdo é quem enfileira — mudar a forma
/// no meio do caminho tornaria impossível reproduzir o que foi enviado a partir do que
/// está gravado.
/// </para>
/// <para>
/// <b>O que este conector decide é uma coisa só: se vale repetir.</b> Todo o resto —
/// ordem, espera crescente, cartas mortas — é do drenador.
/// </para>
/// </remarks>
public sealed class ConectorRest : IConectorDeSincronizacao
{
    private readonly HttpClient _http;
    private readonly ConfiguracaoDoConectorRest _configuracao;

    public ConectorRest(HttpClient http, ConfiguracaoDoConectorRest configuracao)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuracao.Nome);
        ArgumentOutOfRangeException.ThrowIfLessThan(configuracao.TamanhoMaximoDoLote, 1);

        _http = http;
        _configuracao = configuracao;
    }

    /// <inheritdoc />
    public string Nome => _configuracao.Nome;

    /// <inheritdoc />
    public int TamanhoMaximoDoLote => _configuracao.TamanhoMaximoDoLote;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RespostaDeItem>> EnviarAsync(
        IReadOnlyList<ItemDeSaida> lote,
        CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(lote);

        var respostas = new List<RespostaDeItem>(lote.Count);

        foreach (var item in lote)
        {
            cancelamento.ThrowIfCancellationRequested();
            respostas.Add(await EnviarUmAsync(item, cancelamento).ConfigureAwait(false));
        }

        return respostas;
    }

    private async Task<RespostaDeItem> EnviarUmAsync(ItemDeSaida item, CancellationToken cancelamento)
    {
        try
        {
            using var requisicao = new HttpRequestMessage(HttpMethod.Post, _configuracao.Caminho)
            {
                Content = new StringContent(item.PayloadJson, Encoding.UTF8),
            };

            requisicao.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8",
            };

            // A chave de idempotência é o que permite repetir sem duplicar do outro lado.
            // Sem ela, uma resposta perdida na volta vira um registro a mais no sistema do
            // provedor — e ninguém descobre até a conciliação não fechar.
            requisicao.Headers.TryAddWithoutValidation(_configuracao.CabecalhoDeIdempotencia, item.ChaveDeIdempotencia);

            using var resposta = await _http.SendAsync(requisicao, cancelamento).ConfigureAwait(false);

            return Classificar(item.Id, resposta);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            // Parada ordenada: não é veredito sobre o item.
            throw;
        }
        catch (TaskCanceledException)
        {
            // Tempo esgotado do HttpClient, sem cancelamento pedido. Vale repetir.
            return new RespostaDeItem(item.Id, ResultadoDoEnvio.FalhaTemporaria, "tempo de resposta esgotado");
        }
        catch (HttpRequestException erro)
        {
            // DNS, recusa de conexão, TLS, rede fora. Sempre vale repetir.
            return new RespostaDeItem(item.Id, ResultadoDoEnvio.FalhaTemporaria, Resumir(erro));
        }
    }

    private RespostaDeItem Classificar(string id, HttpResponseMessage resposta)
    {
        var status = resposta.StatusCode;

        if (resposta.IsSuccessStatusCode)
        {
            return new RespostaDeItem(id, ResultadoDoEnvio.Aceito);
        }

        if (_configuracao.Duplicados.Contains(status))
        {
            // O destino já tinha. É sucesso: o caminho normal depois de uma queda no meio
            // do envio.
            return new RespostaDeItem(id, ResultadoDoEnvio.Duplicado, Detalhe(status));
        }

        if (ClassificacaoHttp.ValeRepetir(status))
        {
            return new RespostaDeItem(id, ResultadoDoEnvio.FalhaTemporaria, Detalhe(status));
        }

        // Os demais 4xx são recusa de conteúdo ou de permissão: repetir não muda nada, e
        // martelar um 401 durante horas só atrasa a fila inteira.
        return new RespostaDeItem(id, ResultadoDoEnvio.FalhaPermanente, Detalhe(status));
    }

    private static string Detalhe(HttpStatusCode status) => ClassificacaoHttp.Detalhe(status);

    private static string Resumir(Exception erro) => ClassificacaoHttp.Resumir(erro);
}

/// <summary>Regras de HTTP comuns aos conectores deste projeto.</summary>
internal static class ClassificacaoHttp
{
    /// <summary>
    /// Sobrecarga, indisponibilidade e tempo esgotado do lado deles: repetir resolve.
    /// Os demais 4xx são recusa de conteúdo ou de permissão, e repetir não muda nada.
    /// </summary>
    public static bool ValeRepetir(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.Locked
            or (HttpStatusCode)425   // Too Early
        || (int)status >= 500;

    // O corpo da resposta NÃO entra no detalhe: ele pode conter o ingresso de volta, e
    // esse texto vai para o banco e para a tela. O código de situação já diz o que
    // precisa ser dito.
    public static string Detalhe(HttpStatusCode status) =>
        string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)status} {status}");

    public static string Resumir(Exception erro) =>
        string.Create(CultureInfo.InvariantCulture, $"{erro.GetType().Name}: {erro.Message}");
}
