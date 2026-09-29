using System.Globalization;
using System.Text.Json;
using Access.Domain.Credentials;
using Access.Domain.Tempo;
using Access.Domain.Ticketing;

namespace Sync.Connectors.Rest;

/// <summary>
/// Lê o webhook que a <b>Compre no Zet</b> envia de fato — compra (<c>CP</c>) e estorno
/// (<c>ES</c>) —, conforme o formato documentado pela integração existente e conferido
/// contra os 27.641 webhooks reais da edição 2025 (análise em docs/30).
/// </summary>
/// <remarks>
/// <para>
/// Diferente do <see cref="TradutorDoContratoV1"/>, este formato não é nosso: é o que a Zet
/// já produz. O tradutor lê só o que a catraca precisa — voucher, data do evento, tipo e
/// situação — e <b>não lê</b> nome, e-mail, telefone nem CPF do comprador (minimização).
/// </para>
/// <para>
/// Regras, todas tiradas dos dados reais:
/// </para>
/// <list type="bullet">
///   <item>Referência = <c>eventTicketCodes[].id</c>, o mesmo na compra e no estorno — é o
///   que faz o estorno achar o ingresso. Reenvio da mesma compra (1.254 na edição 2025)
///   atualiza a mesma linha, sem duplicar.</item>
///   <item>Estorno cancela, e cancelamento de ingresso de site é definitivo: uma compra
///   reenviada depois do estorno não reativa (regra do repositório).</item>
///   <item>Só o evento configurado entra. A conta da Zet tem outros eventos (inclusive de
///   teste); entrega de outro evento devolve lista vazia.</item>
///   <item>O voucher é texto (13 dígitos na edição 2025). Voucher em número JSON é recusado:
///   número destrói zeros à esquerda (ADR-0008).</item>
///   <item>Validade: o <b>dia de operação</b> da data do ingresso, no horário de Brasília —
///   de <c>inicioDoDia</c> da data até <c>inicioDoDia</c> do dia seguinte. A sessão
///   ("19h15") não restringe a entrada: <c>A_CONFIRMAR</c> com a organização se um ingresso
///   de uma sessão pode entrar em outra do mesmo dia.</item>
///   <item>Categoria = <c>eventsValues.description</c> com espaços normalizados ("Inteira",
///   "Meia-entrada", "Solidário + 1kg…"). A reunião em tipos para os relatórios é do
///   cadastro de tipos (fase 3), não daqui.</item>
///   <item>Setor fica vazio: o "sector" da Zet é o produto ("Ingresso"), não um setor físico
///   para conferir contra o portão.</item>
/// </list>
/// </remarks>
public sealed class TradutorDaZet : ITradutorDeIngresso
{
    /// <summary>Início padrão do dia de operação (docs/25 §6: proposta 06:00, a confirmar).</summary>
    public static readonly TimeSpan InicioDoDiaPadrao = TimeSpan.FromHours(6);

    private readonly long _eventoId;
    private readonly TimeSpan _inicioDoDia;
    private readonly TimeZoneInfo _fuso;
    private readonly CredentialNormalization _normalizacao;

    /// <param name="provedor">Identificador local do provedor (ex.: "zet").</param>
    /// <param name="eventoId">
    /// <c>data.event.id</c> do evento na Zet. Muda a cada edição (538 em 2025); vem da
    /// configuração, nunca do código.
    /// </param>
    /// <param name="inicioDoDia">Hora em que o dia de operação começa, no horário de Brasília.</param>
    /// <param name="normalizacao">Perfil do QR. O padrão só apara as pontas.</param>
    public TradutorDaZet(string provedor, long eventoId, TimeSpan? inicioDoDia = null, CredentialNormalization? normalizacao = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventoId);
        var inicio = inicioDoDia ?? InicioDoDiaPadrao;
        if (inicio < TimeSpan.Zero || inicio >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(inicioDoDia), "o início do dia precisa estar entre 00:00 e 23:59.");
        }

        Provedor = provedor;
        _eventoId = eventoId;
        _inicioDoDia = inicio;
        _fuso = HoraDeBrasilia.Fuso;
        _normalizacao = normalizacao ?? CredentialNormalization.Raw;
    }

    /// <inheritdoc />
    public string Provedor { get; }

    /// <inheritdoc />
    public bool Configurado => true;

    /// <inheritdoc />
    public IReadOnlyList<IngressoRecebido> Traduzir(byte[] corpo, string? tipoDeConteudo)
    {
        ArgumentNullException.ThrowIfNull(corpo);

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException erro)
        {
            throw new FormatException($"corpo não é JSON válido: {erro.Message}", erro);
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("o corpo precisa ser um objeto JSON.");
            }

            var cancelado = LerAcao(raiz);

            if (!raiz.TryGetProperty("data", out var dados) || dados.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("campo 'data' ausente ou não é um objeto.");
            }

            if (!DoEventoConfigurado(dados))
            {
                return [];
            }

            // Estorno sem ingressos existe (5 na edição 2025): não há o que cancelar.
            if (!dados.TryGetProperty("eventTicketCodes", out var lista) || lista.ValueKind == JsonValueKind.Null)
            {
                return cancelado ? [] : throw new FormatException("compra sem 'eventTicketCodes'.");
            }

            if (lista.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("'eventTicketCodes' não é uma lista.");
            }

            var ingressos = new List<IngressoRecebido>(lista.GetArrayLength());
            var posicao = 0;
            foreach (var item in lista.EnumerateArray())
            {
                ingressos.Add(Ler(item, posicao, cancelado));
                posicao++;
            }

            return ingressos;
        }
    }

    private static bool LerAcao(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("action", out var acao) || acao.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("campo 'action' ausente ou não é texto.");
        }

        return acao.GetString() switch
        {
            "CP" => false,
            "ES" => true,
            var outra => throw new FormatException(
                $"action '{outra}' não é reconhecida; a Zet envia 'CP' (compra) e 'ES' (estorno)."),
        };
    }

    private bool DoEventoConfigurado(JsonElement dados)
    {
        if (!dados.TryGetProperty("event", out var evento) || evento.ValueKind != JsonValueKind.Object
            || !evento.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            // Sem evento não dá para saber de quem é: nunca vira ingresso válido.
            return false;
        }

        return id.TryGetInt64(out var valor) && valor == _eventoId;
    }

    private IngressoRecebido Ler(JsonElement item, int posicao, bool cancelado)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"eventTicketCodes[{posicao}] não é um objeto.");
        }

        if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var idIngresso))
        {
            throw new FormatException($"eventTicketCodes[{posicao}].id ausente ou não numérico.");
        }

        var voucher = LerVoucher(item, posicao);
        var normalizado = _normalizacao.Apply(voucher);
        if (!_normalizacao.IsLengthAccepted(normalizado))
        {
            throw new FormatException(
                $"eventTicketCodes[{posicao}].voucher tem {normalizado.Length} caracteres, e o perfil " +
                $"'{_normalizacao.Name}' não aceita esse tamanho — a catraca não conseguiria ler.");
        }

        item.TryGetProperty("eventsValues", out var valores);
        var (de, ate) = Validade(valores, posicao, obrigatoria: !cancelado);

        return new IngressoRecebido(
            ProvedorId: Provedor,
            ReferenciaExterna: idIngresso.ToString(CultureInfo.InvariantCulture),
            QrBruto: voucher,
            QrNormalizado: normalizado,
            Setor: null,
            ValidoDe: de,
            ValidoAte: ate,
            UsosMaximos: 1,
            Cancelado: cancelado,
            Categoria: Categoria(valores));
    }

    private static string LerVoucher(JsonElement item, int posicao)
    {
        if (!item.TryGetProperty("voucher", out var voucher))
        {
            throw new FormatException($"eventTicketCodes[{posicao}].voucher ausente.");
        }

        if (voucher.ValueKind == JsonValueKind.Number)
        {
            throw new FormatException(
                $"eventTicketCodes[{posicao}].voucher veio como número JSON; precisa ser texto — " +
                "número destrói zeros à esquerda de forma irreversível. Ver ADR-0008.");
        }

        var valor = voucher.ValueKind == JsonValueKind.String ? voucher.GetString() : null;
        return string.IsNullOrWhiteSpace(valor)
            ? throw new FormatException($"eventTicketCodes[{posicao}].voucher vazio ou não é texto.")
            : valor;
    }

    private (DateTimeOffset? De, DateTimeOffset? Ate) Validade(JsonElement valores, int posicao, bool obrigatoria)
    {
        var inicio = DataDoEvento(valores, "startDate");
        if (inicio is null)
        {
            return obrigatoria
                ? throw new FormatException($"eventTicketCodes[{posicao}] sem eventsValues.eventsDates.startDate: não dá para saber o dia do ingresso.")
                : (null, null);
        }

        var fim = DataDoEvento(valores, "endDate") ?? inicio.Value;
        if (fim < inicio.Value)
        {
            fim = inicio.Value;
        }

        return (NoFuso(inicio.Value), NoFuso(fim.AddDays(1)));
    }

    /// <summary>A Zet manda a data do evento como meia-noite UTC ("2025-11-20T00:00:00.000Z"): vale o dia do calendário.</summary>
    private static DateOnly? DataDoEvento(JsonElement valores, string campo)
    {
        if (valores.ValueKind != JsonValueKind.Object
            || !valores.TryGetProperty("eventsDates", out var datas) || datas.ValueKind != JsonValueKind.Object
            || !datas.TryGetProperty(campo, out var data) || data.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var texto = data.GetString();
        return texto is { Length: >= 10 }
               && DateOnly.TryParseExact(texto.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            ? dia
            : throw new FormatException($"eventsDates.{campo} '{texto}' não é uma data.");
    }

    private DateTimeOffset NoFuso(DateOnly dia)
    {
        var local = dia.ToDateTime(TimeOnly.FromTimeSpan(_inicioDoDia), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _fuso.GetUtcOffset(local));
    }

    private static string? Categoria(JsonElement valores)
    {
        if (valores.ValueKind != JsonValueKind.Object
            || !valores.TryGetProperty("description", out var descricao) || descricao.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var texto = string.Join(' ', (descricao.GetString() ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return texto.Length == 0 ? null : texto;
    }
}
