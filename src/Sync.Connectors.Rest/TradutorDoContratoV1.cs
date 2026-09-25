using System.Globalization;
using System.Text;
using System.Text.Json;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;

namespace Sync.Connectors.Rest;

/// <summary>
/// Lê o formato de webhook que <b>nós</b> especificamos, versão 1.
/// </summary>
/// <remarks>
/// <para>
/// Este tradutor não adivinha o formato de ninguém: ele implementa o contrato descrito em
/// docs/18-contrato-do-webhook.md, que é o que entregamos ao provedor para ele produzir.
/// Enquanto o provedor não confirmar que vai produzi-lo, o contrato é proposta — mas o
/// código já é código, e o teste já é teste.
/// </para>
/// <para>
/// <b>Ele é rigoroso de propósito.</b> Campo ausente, tipo errado ou versão desconhecida
/// viram recusa com mensagem, nunca um valor padrão silencioso: um campo mal interpretado
/// aqui é uma pessoa barrada na porta, e ninguém vai descobrir a causa no meio do evento.
/// A única tolerância é com <b>campos a mais</b>, que são ignorados — é o que permite ao
/// provedor evoluir o payload sem combinar conosco.
/// </para>
/// </remarks>
public sealed class TradutorDoContratoV1 : ITradutorDeIngresso
{
    /// <summary>Versão do contrato que este tradutor entende.</summary>
    public const int VersaoSuportada = 1;

    private readonly CredentialNormalization _normalizacao;

    /// <summary>
    /// </summary>
    /// <param name="provedor">Identificador local do provedor.</param>
    /// <param name="normalizacao">
    /// Perfil aplicado ao QR. O padrão só remove espaços das pontas — de propósito.
    /// Passar para maiúsculas destrói um QR em base64, e cortar zeros à esquerda destrói
    /// qualquer código numérico. Mudar isto exige a resposta da pergunta 9 de
    /// docs/17-questionario-de-integracao-bilheteria.md.
    /// </param>
    public TradutorDoContratoV1(string provedor, CredentialNormalization? normalizacao = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedor);
        Provedor = provedor;
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

            ConferirVersao(raiz);

            if (!raiz.TryGetProperty("ingressos", out var lista) || lista.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("campo 'ingressos' ausente ou não é uma lista.");
            }

            var ingressos = new List<IngressoRecebido>(lista.GetArrayLength());
            var posicao = 0;

            foreach (var item in lista.EnumerateArray())
            {
                ingressos.Add(Ler(item, posicao));
                posicao++;
            }

            return ingressos;
        }
    }

    private static void ConferirVersao(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("versao", out var versao) || versao.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException("campo 'versao' ausente ou não numérico.");
        }

        var valor = versao.GetInt32();

        if (valor != VersaoSuportada)
        {
            // Nunca tentar ler "mais ou menos". Uma versão desconhecida pode ter mudado o
            // significado de um campo sem mudar o nome, e é exatamente aí que se aceita
            // um ingresso que devia ser recusado.
            throw new FormatException(
                $"versão {valor} do contrato não é suportada; este tradutor lê a {VersaoSuportada}. " +
                "Ver docs/18-contrato-do-webhook.md");
        }
    }

    private IngressoRecebido Ler(JsonElement item, int posicao)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"ingressos[{posicao}] não é um objeto.");
        }

        var referencia = Texto(item, "referencia", posicao, obrigatorio: true)!;
        var qr = LerQr(item, posicao);
        var situacao = Texto(item, "situacao", posicao, obrigatorio: true)!;

        var cancelado = situacao switch
        {
            "valido" => false,
            "cancelado" => true,
            _ => throw new FormatException(
                $"ingressos[{posicao}].situacao = '{situacao}' não é reconhecido; use 'valido' ou 'cancelado'."),
        };

        var normalizado = _normalizacao.Apply(qr);

        if (!_normalizacao.IsLengthAccepted(normalizado))
        {
            // Recusar aqui é recusar dias antes do evento, quando o provedor ainda pode
            // corrigir. Aceitar é descobrir na porta que a catraca não lê este ingresso.
            throw new FormatException(
                $"ingressos[{posicao}].qr tem {normalizado.Length} caracteres, e o perfil " +
                $"'{_normalizacao.Name}' não aceita esse tamanho — a catraca não conseguiria ler. " +
                "Ver docs/20-leitores-qr-e-cartao-mifare.md");
        }

        return new IngressoRecebido(
            ProvedorId: Provedor,
            ReferenciaExterna: referencia,
            QrBruto: qr,
            QrNormalizado: normalizado,
            Setor: Texto(item, "setor", posicao, obrigatorio: false),
            ValidoDe: Data(item, "validoDe", posicao),
            ValidoAte: Data(item, "validoAte", posicao),
            UsosMaximos: Usos(item, posicao),
            Cancelado: cancelado,
            Categoria: Texto(item, "categoria", posicao, obrigatorio: false));
    }

    private static string LerQr(JsonElement item, int posicao)
    {
        if (!item.TryGetProperty("qr", out var qr))
        {
            throw new FormatException($"ingressos[{posicao}].qr ausente.");
        }

        if (qr.ValueKind == JsonValueKind.Number)
        {
            // A recusa mais importante deste arquivo. Se o QR chegou como número JSON, os
            // zeros à esquerda já foram destruídos antes de chegar aqui — '0081AC' virou
            // outra coisa, ou nem seria número. Aceitar "convertendo para texto" produz um
            // código que não existe, e a pessoa é recusada na porta sem explicação.
            throw new FormatException(
                $"ingressos[{posicao}].qr veio como número JSON. O QR precisa ser uma string: " +
                "número destrói zeros à esquerda de forma irreversível. Ver ADR-0008.");
        }

        if (qr.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"ingressos[{posicao}].qr precisa ser uma string.");
        }

        var valor = qr.GetString();

        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new FormatException($"ingressos[{posicao}].qr está vazio.");
        }

        return valor;
    }

    private static string? Texto(JsonElement item, string campo, int posicao, bool obrigatorio)
    {
        if (!item.TryGetProperty(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
        {
            return obrigatorio
                ? throw new FormatException($"ingressos[{posicao}].{campo} ausente.")
                : null;
        }

        if (valor.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"ingressos[{posicao}].{campo} precisa ser uma string.");
        }

        var texto = valor.GetString();

        if (obrigatorio && string.IsNullOrWhiteSpace(texto))
        {
            throw new FormatException($"ingressos[{posicao}].{campo} está vazio.");
        }

        return string.IsNullOrWhiteSpace(texto) ? null : texto;
    }

    private static DateTimeOffset? Data(JsonElement item, string campo, int posicao)
    {
        var texto = Texto(item, campo, posicao, obrigatorio: false);

        if (texto is null)
        {
            return null;
        }

        // Exige deslocamento explícito. "2026-11-14T18:00:00" sem fuso é ambíguo, e a
        // ambiguidade aparece como ingresso recusado uma hora antes ou depois.
        if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            || !texto.Contains('Z', StringComparison.OrdinalIgnoreCase) && !ContemDeslocamento(texto))
        {
            throw new FormatException(
                $"ingressos[{posicao}].{campo} = '{texto}' não é uma data ISO 8601 com fuso. " +
                "Exemplo: 2026-11-14T18:00:00-03:00");
        }

        return data;
    }

    private static bool ContemDeslocamento(string texto)
    {
        // Procura + ou - depois da parte da hora, não no início nem nas datas.
        var t = texto.IndexOf('T', StringComparison.Ordinal);
        return t >= 0 && texto.AsSpan(t).IndexOfAny('+', '-') >= 0;
    }

    private static int Usos(JsonElement item, int posicao)
    {
        if (!item.TryGetProperty("usos", out var usos) || usos.ValueKind == JsonValueKind.Null)
        {
            return 1;
        }

        if (usos.ValueKind != JsonValueKind.Number || !usos.TryGetInt32(out var valor) || valor < 1)
        {
            throw new FormatException($"ingressos[{posicao}].usos precisa ser um inteiro maior ou igual a 1.");
        }

        return valor;
    }

    /// <summary>Exemplo canônico do contrato, usado na documentação e nos testes.</summary>
    internal static byte[] Exemplo() => Encoding.UTF8.GetBytes(
        """
        {
          "versao": 1,
          "id": "entrega-9f3c2a10",
          "emitidoEm": "2026-11-14T20:31:07-03:00",
          "ingressos": [
            {
              "referencia": "ZET-8842179",
              "qr": "0081443290",
              "setor": "pista",
              "validoDe": "2026-11-14T18:00:00-03:00",
              "validoAte": "2026-11-15T02:00:00-03:00",
              "usos": 1,
              "categoria": "inteira",
              "situacao": "valido"
            }
          ]
        }
        """);
}
