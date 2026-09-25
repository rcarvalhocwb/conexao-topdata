using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Sync.Ingestao;

namespace Sync.Connectors.Rest.Painel;

/// <summary>
/// Baixa do painel na nuvem os cartões da bilheteria autorizados a passar.
/// </summary>
/// <remarks>
/// <para>
/// É o sentido "desce" da ADR-0023: a nuvem é dona do cadastro, a borda é dona do fato.
/// O que vem daqui é <b>quais cartões valem e de que categoria são</b>; quantas vezes cada
/// um já passou é contado na borda e nunca é sobrescrito por esta leitura.
/// </para>
/// <para>
/// O formato está descrito em docs/22-sistema-supabase-atual.md, seção 8. Resumo: pede
/// com <c>device_id</c>, <c>last_sync_at</c> e <c>full_sync</c>; recebe <c>cards</c>,
/// <c>removed_cards</c> e <c>sync_timestamp</c>. O <c>sync_timestamp</c> é o relógio
/// <b>do servidor</b>, e é ele que vira o cursor — o relógio do PC não entra na conta.
/// </para>
/// <para>
/// <b>O servidor devolve tudo numa resposta só, sem paginação.</b> O Supabase costuma
/// limitar uma consulta a 1.000 linhas. Se a resposta vier com esse tamanho ou mais, a
/// lista pode ter sido cortada sem aviso; aí esta fonte avisa e <b>não avança o cursor</b>,
/// para não dar por sincronizado o que pode ter ficado de fora.
/// </para>
/// </remarks>
public sealed class FonteDeCartoesDoPainel : IFonteDeIngressos
{
    /// <summary>
    /// Usos de um cartão sem limite no painel (<c>max_uses</c> nulo). O controle, aí, é
    /// físico: a urna recolhe o cartão e o intervalo de reuso impede o repasse pela grade.
    /// </summary>
    public const int SemLimiteDeUsos = int.MaxValue;

    private readonly HttpClient _http;
    private readonly string _dispositivo;
    private readonly CredentialNormalization _normalizacao;
    private readonly string _caminho;
    private readonly int _limiteDeLinhas;
    private readonly Action<int, string>? _aoRecusarCartao;
    private readonly Action<int>? _aoSuspeitarDeCorte;

    /// <param name="http">Cliente já apontado para a base das funções; a credencial vem dele.</param>
    /// <param name="provedor">Provedor local que recebe os cartões (a bilheteria).</param>
    /// <param name="dispositivo">Identificador desta borda no painel (<c>device_id</c>).</param>
    /// <param name="normalizacao">Perfil do leitor — o mesmo que a catraca usa.</param>
    /// <param name="caminho">Caminho da função, relativo à base.</param>
    /// <param name="limiteDeLinhas">Tamanho a partir do qual a resposta pode ter sido cortada.</param>
    /// <param name="aoRecusarCartao">
    /// Posição e motivo de um cartão que não pôde ser lido. O motivo nunca traz o número.
    /// </param>
    /// <param name="aoSuspeitarDeCorte">Quantos cartões vieram, quando a lista pode estar cortada.</param>
    public FonteDeCartoesDoPainel(
        HttpClient http,
        string provedor,
        string dispositivo,
        CredentialNormalization normalizacao,
        string caminho = "middleware-sync-cards",
        int limiteDeLinhas = 1000,
        Action<int, string>? aoRecusarCartao = null,
        Action<int>? aoSuspeitarDeCorte = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(provedor);
        ArgumentException.ThrowIfNullOrWhiteSpace(dispositivo);
        ArgumentNullException.ThrowIfNull(normalizacao);
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        ArgumentOutOfRangeException.ThrowIfLessThan(limiteDeLinhas, 1);

        _http = http;
        Provedor = provedor;
        _dispositivo = dispositivo;
        _normalizacao = normalizacao;
        _caminho = caminho;
        _limiteDeLinhas = limiteDeLinhas;
        _aoRecusarCartao = aoRecusarCartao;
        _aoSuspeitarDeCorte = aoSuspeitarDeCorte;
    }

    /// <inheritdoc />
    public string Provedor { get; }

    /// <summary>Cartões recusados desde que esta fonte foi criada.</summary>
    public long Recusados { get; private set; }

    /// <inheritdoc />
    /// <remarks>Cursor nulo pede a lista completa; senão, só o que mudou desde o cursor.</remarks>
    public async Task<PaginaDeIngressos> LerAsync(string? cursor, CancellationToken cancelamento)
    {
        var completa = string.IsNullOrEmpty(cursor);

        var pedido = completa
            ? (object)new { device_id = _dispositivo, full_sync = true }
            : new { device_id = _dispositivo, last_sync_at = cursor, full_sync = false };

        using var resposta = await _http.PostAsJsonAsync(_caminho, pedido, cancelamento).ConfigureAwait(false);

        if (!resposta.IsSuccessStatusCode)
        {
            // Lançar, e não devolver página vazia: página vazia com cursor nulo é "nada
            // novo", e uma falha não é isso. O laço registra e tenta de novo depois.
            throw new HttpRequestException(
                $"o painel recusou a leitura dos cartões: {ClassificacaoHttp.Detalhe(resposta.StatusCode)}",
                inner: null,
                resposta.StatusCode);
        }

        var corpo = await resposta.Content.ReadAsByteArrayAsync(cancelamento).ConfigureAwait(false);
        return Interpretar(corpo);
    }

    private PaginaDeIngressos Interpretar(byte[] corpo)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException erro)
        {
            throw new FormatException($"resposta do painel não é JSON válido: {erro.Message}", erro);
        }

        using (documento)
        {
            var raiz = documento.RootElement;

            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("cards", out var cartoes) || cartoes.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("resposta do painel sem a lista 'cards'.");
            }

            if (!raiz.TryGetProperty("sync_timestamp", out var marca) || marca.ValueKind != JsonValueKind.String
                || !DataComFuso(marca.GetString(), out _))
            {
                // Sem a marca do servidor não há cursor confiável. Seguir com o relógio do
                // PC faria a próxima leitura perder o que mudou no intervalo entre os dois.
                throw new FormatException("resposta do painel sem 'sync_timestamp' com fuso.");
            }

            var itens = new List<IngressoRecebido>(cartoes.GetArrayLength());
            var posicao = 0;

            foreach (var cartao in cartoes.EnumerateArray())
            {
                if (TentarLerCartao(cartao, posicao, out var ingresso))
                {
                    itens.Add(ingresso);
                }

                posicao++;
            }

            if (raiz.TryGetProperty("removed_cards", out var removidos) && removidos.ValueKind == JsonValueKind.Array)
            {
                var r = 0;
                foreach (var removido in removidos.EnumerateArray())
                {
                    if (TentarLerNumero(removido, $"removed_cards[{r}]", r, out var bruto, out var normalizado))
                    {
                        itens.Add(new IngressoRecebido(
                            Provedor, bruto, bruto, normalizado, UsosMaximos: 1, Cancelado: true));
                    }

                    r++;
                }
            }

            var recebidos = cartoes.GetArrayLength();

            if (recebidos >= _limiteDeLinhas)
            {
                _aoSuspeitarDeCorte?.Invoke(recebidos);
                return new PaginaDeIngressos(itens, ProximoCursor: null, TemMais: false);
            }

            return new PaginaDeIngressos(itens, marca.GetString(), TemMais: false);
        }
    }

    private bool TentarLerCartao(JsonElement cartao, int posicao, out IngressoRecebido ingresso)
    {
        ingresso = null!;
        var onde = $"cards[{posicao}]";

        if (cartao.ValueKind != JsonValueKind.Object)
        {
            return Recusar(posicao, $"{onde} não é um objeto.");
        }

        if (!cartao.TryGetProperty("card_number", out var numero)
            || !TentarLerNumero(numero, $"{onde}.card_number", posicao, out var bruto, out var normalizado))
        {
            return false;
        }

        if (!Data(cartao, "valid_from", out var de) || !Data(cartao, "valid_until", out var ate))
        {
            return Recusar(posicao, $"{onde} tem validade que não é data ISO 8601 com fuso.");
        }

        if (!Usos(cartao, out var usos))
        {
            return Recusar(posicao, $"{onde} tem max_uses ou times_used que não é inteiro não negativo.");
        }

        // Só cartão ativo vem nesta lista; "active": false aqui seria contradição do
        // servidor, e na dúvida vale o lado seguro.
        var ativo = !cartao.TryGetProperty("active", out var a) || a.ValueKind != JsonValueKind.False;

        ingresso = new IngressoRecebido(
            ProvedorId: Provedor,
            ReferenciaExterna: bruto,
            QrBruto: bruto,
            QrNormalizado: normalizado,
            ValidoDe: de,
            ValidoAte: ate,
            // A base local exige pelo menos um uso por cadastro. Cartão que já gastou
            // tudo na nuvem entra cancelado: gravar 0 violaria a restrição e derrubaria
            // o lote inteiro de cartões; gravar 1 daria uma entrada a mais.
            UsosMaximos: Math.Max(usos, 1),
            Cancelado: !ativo || usos == 0,
            Categoria: Texto(cartao, "admission_type"));

        return true;
    }

    private bool TentarLerNumero(JsonElement valor, string onde, int posicao, out string bruto, out string normalizado)
    {
        bruto = normalizado = string.Empty;

        if (valor.ValueKind == JsonValueKind.Number)
        {
            // Número JSON já perdeu os zeros à esquerda antes de chegar aqui. Ver ADR-0008.
            return Recusar(posicao, $"{onde} veio como número JSON; o número do cartão precisa ser texto.");
        }

        if (valor.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(valor.GetString()))
        {
            return Recusar(posicao, $"{onde} ausente ou vazio.");
        }

        bruto = valor.GetString()!.Trim();
        normalizado = _normalizacao.Apply(bruto);

        if (!_normalizacao.IsLengthAccepted(normalizado))
        {
            // O motivo diz o tamanho, nunca o número: isto vai para log.
            return Recusar(posicao,
                $"{onde} tem {normalizado.Length} caracteres depois de normalizado, e o perfil " +
                $"'{_normalizacao.Name}' não aceita esse tamanho — a catraca não o leria assim.");
        }

        return true;
    }

    private bool Recusar(int posicao, string motivo)
    {
        Recusados++;
        _aoRecusarCartao?.Invoke(posicao, motivo);
        return false;
    }

    private static string? Texto(JsonElement item, string campo) =>
        item.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static bool Data(JsonElement item, string campo, out DateTimeOffset? data)
    {
        data = null;

        if (!item.TryGetProperty(campo, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (v.ValueKind != JsonValueKind.String || !DataComFuso(v.GetString(), out var lida))
        {
            return false;
        }

        data = lida;
        return true;
    }

    private static bool DataComFuso(string? texto, out DateTimeOffset data)
    {
        data = default;

        if (string.IsNullOrWhiteSpace(texto)
            || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out data))
        {
            return false;
        }

        // Data sem fuso é ambígua, e a ambiguidade aparece como cartão recusado três
        // horas antes ou depois.
        var t = texto.IndexOf('T', StringComparison.Ordinal);
        return texto.EndsWith('Z') || texto.EndsWith('z')
            || (t >= 0 && texto.AsSpan(t).IndexOfAny('+', '-') >= 0);
    }

    private static bool Usos(JsonElement item, out int usos)
    {
        usos = SemLimiteDeUsos;

        if (!item.TryGetProperty("max_uses", out var maximo) || maximo.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (maximo.ValueKind != JsonValueKind.Number || !maximo.TryGetInt32(out var max) || max < 0)
        {
            return false;
        }

        var feitos = 0;
        if (item.TryGetProperty("times_used", out var t) && t.ValueKind != JsonValueKind.Null
            && (t.ValueKind != JsonValueKind.Number || !t.TryGetInt32(out feitos) || feitos < 0))
        {
            return false;
        }

        // O painel não soma usos a partir dos eventos (docs/22, seção 8), então o que
        // estiver em times_used veio de antes ou de ajuste manual. Descontar é o lado
        // seguro: no pior caso nega cedo, nunca deixa passar a mais.
        usos = Math.Max(max - feitos, 0);
        return true;
    }
}
