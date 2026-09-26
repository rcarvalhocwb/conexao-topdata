using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Access.Domain.Ticketing;
using Sync.Ingestao;

namespace Sync.Connectors.Rest;

/// <summary>
/// Traduz o corpo bruto de uma entrega do provedor em ingressos.
/// </summary>
/// <remarks>
/// <para>
/// <b>É a única peça que depende do formato de cada bilheteria.</b> O relé guarda bytes,
/// o laço cuida de cursor e retomada, e o destino cuida de idempotência e colisão. Aqui
/// mora o conhecimento de "o QR fica no campo tal".
/// </para>
/// <para>
/// Enquanto <see cref="Configurado"/> for falso, a fonte se recusa a ler — e o cursor
/// <b>não</b> avança. É deliberado: se ela lesse e não soubesse traduzir, as entregas
/// seriam consumidas e os ingressos sumiriam sem ninguém perceber.
/// </para>
/// </remarks>
public interface ITradutorDeIngresso
{
    /// <summary>Provedor a que este tradutor pertence.</summary>
    string Provedor { get; }

    /// <summary>
    /// Falso enquanto o formato do provedor não for conhecido.
    /// </summary>
    bool Configurado { get; }

    /// <summary>
    /// Converte uma entrega em ingressos. Uma entrega pode conter zero, um ou vários.
    /// </summary>
    /// <exception cref="FormatException">
    /// Quando <b>esta</b> entrega é ilegível, mas o tradutor sabe ler o formato em geral.
    /// A entrega é pulada e registrada; os bytes continuam no relé para reprocessar.
    /// </exception>
    IReadOnlyList<IngressoRecebido> Traduzir(byte[] corpo, string? tipoDeConteudo);
}

/// <summary>
/// Tradutor que ainda não existe, porque o formato do provedor não é conhecido.
/// </summary>
/// <remarks>
/// <c>A_CONFIRMAR_COM_ZET.</c> Falta a resposta das perguntas 7, 8 e 10 de
/// docs/17-questionario-de-integracao-bilheteria.md: o que exatamente vem no corpo do
/// webhook, se o QR é estável e se o código é único.
/// <para>
/// Ele existe declarado e desligado, em vez de adivinhado, porque um tradutor inventado
/// compila, passa em teste escrito contra a própria invenção, e falha no evento.
/// </para>
/// </remarks>
public sealed class TradutorPendente : ITradutorDeIngresso
{
    public TradutorPendente(string provedor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedor);
        Provedor = provedor;
    }

    /// <inheritdoc />
    public string Provedor { get; }

    /// <inheritdoc />
    public bool Configurado => false;

    /// <inheritdoc />
    public IReadOnlyList<IngressoRecebido> Traduzir(byte[] corpo, string? tipoDeConteudo) =>
        throw new NotSupportedException(
            $"O formato do webhook de '{Provedor}' ainda não foi confirmado. " +
            "Ver docs/17-questionario-de-integracao-bilheteria.md, perguntas 7, 8 e 10. " +
            "As entregas continuam guardadas no relé, em ordem, e nada se perde até isto ser preenchido.");
}

/// <summary>
/// Puxa entregas do relé de webhook e as transforma em ingressos.
/// </summary>
/// <remarks>
/// <para>
/// O provedor empurra para o relé, que fica na nuvem; a borda vem buscar aqui. A máquina
/// local nunca abre porta para a internet.
/// Ver docs/ADR/ADR-0022-rele-de-webhook.md
/// </para>
/// <para>
/// <b>Uma entrega ilegível não trava a fila.</b> Ela é pulada, contada e informada, e o
/// cursor segue — porque parar no primeiro payload estranho significa que nenhum ingresso
/// posterior entra, no meio de um evento. Os bytes ficam no relé, que é somente inserção,
/// e podem ser reprocessados depois.
/// </para>
/// </remarks>
public sealed class FonteDeRelay : IFonteDeIngressos
{
    private readonly HttpClient _http;
    private readonly ITradutorDeIngresso _tradutor;
    private readonly int _tamanhoDaPagina;
    private readonly Action<long, string>? _aoNaoConseguirLer;

    /// <summary>
    /// </summary>
    /// <param name="http">Cliente já autenticado no relé. Este tipo não vê a credencial.</param>
    /// <param name="tradutor">Quem sabe ler o formato do provedor.</param>
    /// <param name="tamanhoDaPagina">Quantas entregas pedir por vez.</param>
    /// <param name="aoNaoConseguirLer">
    /// Chamado para cada entrega ilegível, com a sequência e o motivo. É por aqui que o
    /// operador descobre que existe algo a reprocessar.
    /// </param>
    public FonteDeRelay(
        HttpClient http,
        ITradutorDeIngresso tradutor,
        int tamanhoDaPagina = 200,
        Action<long, string>? aoNaoConseguirLer = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tradutor);
        ArgumentOutOfRangeException.ThrowIfLessThan(tamanhoDaPagina, 1);

        _http = http;
        _tradutor = tradutor;
        _tamanhoDaPagina = tamanhoDaPagina;
        _aoNaoConseguirLer = aoNaoConseguirLer;
    }

    /// <inheritdoc />
    public string Provedor => _tradutor.Provedor;

    /// <summary>Quantas entregas foram puladas por não serem legíveis.</summary>
    public long Ilegiveis { get; private set; }

    /// <inheritdoc />
    public async Task<PaginaDeIngressos> LerAsync(string? cursor, CancellationToken cancelamento)
    {
        if (!_tradutor.Configurado)
        {
            // Antes de consumir qualquer entrega. Se lesse aqui, o laço avançaria o
            // cursor e os ingressos sumiriam sem ninguém perceber.
            throw new InvalidOperationException(
                $"O tradutor de '{Provedor}' não está configurado; a ingestão não avança de propósito. " +
                "Nada se perde: as entregas continuam no relé, em ordem.");
        }

        var desde = long.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : 0L;

        var resposta = await _http
            .GetFromJsonAsync<RespostaDoRele>(
                FormattableString.Invariant($"entregas?desde={desde}&limite={_tamanhoDaPagina}"),
                cancelamento)
            .ConfigureAwait(false);

        if (resposta is null || resposta.Entregas.Count == 0)
        {
            return PaginaDeIngressos.Vazia;
        }

        var ingressos = new List<IngressoRecebido>();

        foreach (var entrega in resposta.Entregas)
        {
            cancelamento.ThrowIfCancellationRequested();

            byte[] corpo;
            try
            {
                corpo = Convert.FromBase64String(entrega.CorpoBase64);
            }
            catch (FormatException erro)
            {
                Pular(entrega.Seq, $"corpo não é base64 válido: {erro.Message}");
                continue;
            }

            try
            {
                ingressos.AddRange(_tradutor.Traduzir(corpo, entrega.TipoDeConteudo));
            }
            catch (FormatException erro)
            {
                Pular(entrega.Seq, erro.Message);
            }
        }

        return new PaginaDeIngressos(
            ingressos,
            resposta.UltimoSeq.ToString(CultureInfo.InvariantCulture),
            resposta.TemMais);
    }

    private void Pular(long seq, string motivo)
    {
        Ilegiveis++;
        _aoNaoConseguirLer?.Invoke(seq, motivo);
    }

    private sealed record RespostaDoRele
    {
        [JsonPropertyName("entregas")]
        public IReadOnlyList<EntregaDoRele> Entregas { get; init; } = [];

        [JsonPropertyName("ultimoSeq")]
        public long UltimoSeq { get; init; }

        [JsonPropertyName("temMais")]
        public bool TemMais { get; init; }
    }

    private sealed record EntregaDoRele
    {
        [JsonPropertyName("seq")]
        public long Seq { get; init; }

        [JsonPropertyName("tipoDeConteudo")]
        public string? TipoDeConteudo { get; init; }

        [JsonPropertyName("corpoBase64")]
        public string CorpoBase64 { get; init; } = string.Empty;
    }
}
