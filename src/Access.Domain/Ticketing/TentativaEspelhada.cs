using System.Text.Json;
using System.Text.Json.Serialization;

namespace Access.Domain.Ticketing;

/// <summary>
/// Uma tentativa de uso, no formato neutro em que a borda a guarda na outbox para
/// espelhar num sistema de fora (o painel na nuvem).
/// </summary>
/// <remarks>
/// <para>
/// <b>O formato é nosso, e não o do destino.</b> A base local não sabe como a nuvem
/// quer o evento; quem traduz é o conector, na hora de enviar. Assim trocar de painel
/// não mexe na base, e o que foi enviado continua reproduzível a partir do que está
/// gravado.
/// </para>
/// <para>
/// <see cref="GiroEm"/> começa nulo e é preenchido quando a catraca confirma o giro
/// (origem 6), enquanto o item ainda não saiu. Ver ADR-0023.
/// </para>
/// </remarks>
/// <param name="Versao">Versão deste formato. Hoje, 1.</param>
/// <param name="Tentativa">Identificador único da tentativa na borda.</param>
/// <param name="Codigo">Código lido, já normalizado. Sempre texto.</param>
/// <param name="Dispositivo">Equipamento que leu.</param>
/// <param name="Portao">Portão.</param>
/// <param name="Em">Instante da leitura.</param>
/// <param name="Liberado">Se a borda liberou.</param>
/// <param name="Motivo">O <see cref="MotivoDoUso"/>, por nome.</param>
/// <param name="Provedor">Dono do código, quando conhecido.</param>
/// <param name="Categoria">Categoria no instante da tentativa (meia, inteira...).</param>
/// <param name="GiroEm">Quando a catraca confirmou o giro; nulo se não confirmou.</param>
public sealed record TentativaEspelhada(
    [property: JsonPropertyName("versao")] int Versao,
    [property: JsonPropertyName("tentativa")] Guid Tentativa,
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("dispositivo")] string Dispositivo,
    [property: JsonPropertyName("portao")] string Portao,
    [property: JsonPropertyName("em")] DateTimeOffset Em,
    [property: JsonPropertyName("liberado")] bool Liberado,
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("provedor")] string? Provedor,
    [property: JsonPropertyName("categoria")] string? Categoria,
    [property: JsonPropertyName("giroEm")] DateTimeOffset? GiroEm)
{
    /// <summary>Versão atual do formato.</summary>
    public const int VersaoAtual = 1;

    /// <summary>Tipo gravado na coluna <c>aggregate_type</c> da outbox.</summary>
    public const string TipoDoAgregado = "tentativa";

    /// <summary>Serializa no formato gravado na outbox.</summary>
    public string ParaJson() => JsonSerializer.Serialize(this);

    /// <summary>Lê o formato gravado na outbox.</summary>
    /// <exception cref="FormatException">Conteúdo ilegível ou de versão desconhecida.</exception>
    public static TentativaEspelhada DeJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        TentativaEspelhada? lida;
        try
        {
            lida = JsonSerializer.Deserialize<TentativaEspelhada>(json);
        }
        catch (JsonException erro)
        {
            throw new FormatException($"tentativa espelhada ilegível: {erro.Message}", erro);
        }

        if (lida is null || lida.Versao != VersaoAtual || string.IsNullOrWhiteSpace(lida.Codigo)
            || string.IsNullOrWhiteSpace(lida.Dispositivo))
        {
            throw new FormatException("tentativa espelhada incompleta ou de versão desconhecida.");
        }

        return lida;
    }
}
