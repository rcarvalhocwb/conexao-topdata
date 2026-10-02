using System.Globalization;
using System.Text.RegularExpressions;

namespace Shared.Observability;

/// <summary>
/// Remove dado sensível de qualquer texto que vá para log, métrica ou diagnóstico.
/// </summary>
/// <remarks>
/// <para>
/// A redação acontece <b>no serializador</b>, não em cada chamada de log. Confiar em
/// disciplina de programador para não logar um número de cartão é como confiar em
/// disciplina para não esquecer o <c>FecharPortaComunicacao</c>: funciona até o dia em
/// que não funciona, e aí o vazamento já está no disco do cliente.
/// Ver docs/03-arquitetura.md, seção 10, e docs/ADR/ADR-0014.
/// </para>
/// <para>
/// Cobre três famílias de dado, todas com origem documentada no projeto: números de
/// cartão e sequências longas de dígitos, fotos em Base64 (o leitor facial envia a foto
/// de quem não está cadastrado dentro do evento <c>sendlog</c>, ver docs/13) e valores
/// de campos com nome sensível.
/// </para>
/// </remarks>
public static partial class RedatorDeDadoSensivel
{
    /// <summary>Marca deixada no lugar do valor removido.</summary>
    public const string Marca = "[REDIGIDO]";

    /// <summary>Nomes de campo cujo valor nunca pode aparecer.</summary>
    public static IReadOnlySet<string> CamposSensiveis { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cartao", "card", "cardnumber", "credencial", "credential",
        "senha", "password", "pwd", "pin",
        "token", "secret", "apikey", "authorization",
        "template", "biometria", "biometric", "digital",
        "foto", "photo", "image", "face", "record",
        "cpf", "documento", "document",
        "nome", "name", "fullname",
    };

    /// <summary>Redige um texto livre.</summary>
    public static string Redigir(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto ?? string.Empty;
        }

        var resultado = FotoEmBase64().Replace(texto, Marca);
        resultado = SequenciaLongaDeDigitos().Replace(resultado, Marca);
        return resultado;
    }

    /// <summary>
    /// Redige o valor de um campo, considerando o nome dele.
    /// </summary>
    /// <remarks>
    /// Campo com nome sensível é removido inteiro, sem tentar adivinhar o formato:
    /// um nome próprio curto não casaria com nenhum padrão de valor.
    /// </remarks>
    public static string RedigirCampo(string nome, object? valor)
    {
        ArgumentNullException.ThrowIfNull(nome);

        if (CamposSensiveis.Contains(nome))
        {
            return Marca;
        }

        return valor switch
        {
            null => string.Empty,
            string texto => Redigir(texto),
            IFormattable formatavel => Redigir(formatavel.ToString(null, CultureInfo.InvariantCulture)),
            _ => Redigir(valor.ToString()),
        };
    }

    /// <summary>
    /// Verdadeiro quando o texto ainda contém algo que parece dado sensível.
    /// Usado pelo teste SEC-LOG-01.
    /// </summary>
    public static bool ParecemHaverDadosSensiveis(string? texto) =>
        !string.IsNullOrEmpty(texto)
        && (SequenciaLongaDeDigitos().IsMatch(texto) || FotoEmBase64().IsMatch(texto));

    /// <summary>
    /// Sequências isoladas de 6 ou mais dígitos: a faixa de cartões que o SDK aceita vai
    /// de 4 a 16 dígitos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O piso é 6 para não redigir porta TCP, número de Inner, versão de firmware ou
    /// duração em milissegundos — esses são diagnóstico útil, e um log que esconde tudo
    /// não serve para achar defeito.
    /// </para>
    /// <para>
    /// As âncoras excluem dígitos colados a <c>.</c>, <c>:</c>, <c>-</c> ou <c>T</c>,
    /// o que evita mutilar carimbo de tempo ISO-8601 (a fração de segundo tem 7 dígitos)
    /// e endereço IP. Sem isso, todo horário no log viraria <c>[REDIGIDO]</c>.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"(?<![\d.:T-])\d{6,}(?![\d.:-])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SequenciaLongaDeDigitos();

    /// <summary>
    /// Imagem embutida em Base64. O evento <c>sendlog</c> do leitor facial traz a foto
    /// de quem não está cadastrado neste formato — dado pessoal sensível entrando pelo
    /// caminho de evento.
    /// </summary>
    [GeneratedRegex(@"data:image/[a-zA-Z]+;base64,[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FotoEmBase64();
}
