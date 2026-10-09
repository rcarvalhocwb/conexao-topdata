using System.Security.Cryptography;
using System.Text;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Cifra por campo dos dados pessoais do cadastro local (docs/43 §7.2, ADR-0026).
/// </summary>
/// <remarks>
/// <para>
/// AES-256-GCM com nonce aleatório de 12 bytes e etiqueta de 16. O identificador do registro entra
/// como dado associado: o cifrado de uma pessoa, copiado para outra, não decifra. Formato:
/// <c>versão (1 byte) ‖ nonce ‖ cifrado ‖ etiqueta</c>.
/// </para>
/// <para>
/// A busca por nome e a detecção de documento repetido usam HMAC-SHA256 com uma subchave derivada,
/// nunca o texto em claro: a base acha "Silva" sem guardar "Silva".
/// </para>
/// <para>
/// A chave vem do cofre do serviço (DPAPI da máquina). O worker não precisa dela: a decisão da
/// catraca não lê nome nem documento.
/// </para>
/// </remarks>
public sealed class CifraDeDadosPessoais
{
    /// <summary>Tamanho da chave, em bytes.</summary>
    public const int TamanhoDaChave = 32;

    private const byte Versao = 1;
    private const int TamanhoDoNonce = 12;
    private const int TamanhoDaEtiqueta = 16;
    private const int MinimoDoComeco = 3;

    private readonly byte[] _chave;
    private readonly byte[] _chaveDaBusca;

    /// <param name="chave">Chave de <see cref="TamanhoDaChave"/> bytes.</param>
    public CifraDeDadosPessoais(ReadOnlySpan<byte> chave)
    {
        if (chave.Length != TamanhoDaChave)
        {
            throw new ArgumentException($"A chave dos dados pessoais precisa de {TamanhoDaChave} bytes.", nameof(chave));
        }

        _chave = chave.ToArray();
        _chaveDaBusca = HMACSHA256.HashData(_chave, "ConexaoTopdata:busca-de-pessoas:v1"u8);
    }

    /// <summary>Cifra um campo; nulo ou vazio fica nulo.</summary>
    /// <param name="texto">O valor em claro.</param>
    /// <param name="registro">O id do registro (dado associado).</param>
    public byte[]? Cifrar(string? texto, string registro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registro);
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        var claro = Encoding.UTF8.GetBytes(texto);
        var saida = new byte[1 + TamanhoDoNonce + claro.Length + TamanhoDaEtiqueta];
        saida[0] = Versao;
        var nonce = saida.AsSpan(1, TamanhoDoNonce);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_chave, TamanhoDaEtiqueta);
        aes.Encrypt(
            nonce,
            claro,
            saida.AsSpan(1 + TamanhoDoNonce, claro.Length),
            saida.AsSpan(1 + TamanhoDoNonce + claro.Length, TamanhoDaEtiqueta),
            Associado(registro));
        return saida;
    }

    /// <summary>Decifra um campo; nulo fica nulo.</summary>
    /// <param name="cifrado">O valor gravado.</param>
    /// <param name="registro">O id do registro (dado associado).</param>
    /// <exception cref="CryptographicException">Chave errada, registro trocado ou valor adulterado.</exception>
    public string? Decifrar(byte[]? cifrado, string registro)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registro);
        if (cifrado is null)
        {
            return null;
        }

        if (cifrado.Length < 1 + TamanhoDoNonce + TamanhoDaEtiqueta || cifrado[0] != Versao)
        {
            throw new CryptographicException("Dado pessoal em formato desconhecido.");
        }

        var tamanho = cifrado.Length - 1 - TamanhoDoNonce - TamanhoDaEtiqueta;
        var claro = new byte[tamanho];
        using var aes = new AesGcm(_chave, TamanhoDaEtiqueta);
        aes.Decrypt(
            cifrado.AsSpan(1, TamanhoDoNonce),
            cifrado.AsSpan(1 + TamanhoDoNonce, tamanho),
            cifrado.AsSpan(1 + TamanhoDoNonce + tamanho, TamanhoDaEtiqueta),
            claro,
            Associado(registro));
        return Encoding.UTF8.GetString(claro);
    }

    /// <summary>
    /// Os termos de busca de um nome, separados por espaço: sem acento e em minúsculas, a impressão de cada
    /// parte inteira e de cada começo dela com 3 letras ou mais. Assim "joaq" acha "Joaquina".
    /// </summary>
    public string TermosDeBusca(string? nome) =>
        string.Join(' ', Partes(nome).SelectMany(Comecos).Select(Impressao).Distinct(StringComparer.Ordinal));

    /// <summary>As impressões que uma busca precisa achar (todas as partes digitadas).</summary>
    public IReadOnlyList<string> TermosDaConsulta(string? consulta) => [.. Partes(consulta).Select(Impressao).Distinct(StringComparer.Ordinal)];

    /// <summary>A impressão do documento (só letras e números, em maiúsculas, com o tipo); nula sem documento.</summary>
    public string? ImpressaoDoDocumento(string? tipo, string? numero)
    {
        var limpo = new string((numero ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return limpo.Length == 0 ? null : "doc:" + Impressao($"{tipo ?? "outro"}|{limpo}");
    }

    private static IEnumerable<string> Comecos(string parte)
    {
        for (var tamanho = Math.Min(MinimoDoComeco, parte.Length); tamanho <= parte.Length; tamanho++)
        {
            yield return parte[..tamanho];
        }
    }

    private string Impressao(string texto) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_chaveDaBusca, Encoding.UTF8.GetBytes(texto)))[..20];

    private static IEnumerable<string> Partes(string? texto) =>
        SemAcento(texto ?? string.Empty)
            .ToLowerInvariant()
            .Split([' ', '-', '.', ',', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 2);

    // Tabela própria, e não string.Normalize: o serviço roda com InvariantGlobalization, em que a
    // normalização não decompõe acentos. Cobre Latin-1 e Latin Extended-A (nomes em português, espanhol,
    // francês, alemão, italiano e do leste europeu).
    private const string ComAcento = "ÀÁÂÃÄÅàáâãäåĀāĂăĄąÇçĆćĈĉĊċČčĎďĐđÈÉÊËèéêëĒēĔĕĖėĘęĚěĜĝĞğĠġĢģĤĥĦħÌÍÎÏìíîïĨĩĪīĬĭĮįİıĴĵĶķĹĺĻļĽľĿŀŁłÑñŃńŅņŇňÒÓÔÕÖØòóôõöøŌōŎŏŐőŔŕŖŗŘřŚśŜŝŞşŠšŢţŤťŦŧÙÚÛÜùúûüŨũŪūŬŭŮůŰűŲųŴŵÝýÿŶŷŸŹźŻżŽž";
    private const string SemAcentoNenhum = "AAAAAAaaaaaaAaAaAaCcCcCcCcCcDdDdEEEEeeeeEeEeEeEeEeGgGgGgGgHhHhIIIIiiiiIiIiIiIiIiJjKkLlLlLlLlLlNnNnNnNnOOOOOOooooooOoOoOoRrRrRrSsSsSsSsTtTtTtUUUUuuuuUuUuUuUuUuUuWwYyyYyYZzZzZz";

    private static string SemAcento(string texto)
    {
        var saida = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            var i = ComAcento.IndexOf(c, StringComparison.Ordinal);
            saida.Append(i >= 0 ? SemAcentoNenhum[i] : c);
        }

        return saida.ToString();
    }

    private static byte[] Associado(string registro) => Encoding.UTF8.GetBytes("pessoa:" + registro);
}
