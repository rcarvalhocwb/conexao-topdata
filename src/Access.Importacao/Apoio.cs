using System.Security.Cryptography;
using System.Text;

namespace Access.Importacao;

/// <summary>Limites da importação (docs/34-anexos/03 §3.8).</summary>
public static class LimitesDaImportacao
{
    /// <summary>Tamanho máximo do arquivo: 100 mil linhas de ~100 bytes cabem com folga.</summary>
    public const long BytesDoArquivo = 20L * 1024 * 1024;

    /// <summary>Tamanho máximo do .xlsx descompactado (proteção contra bomba de compressão).</summary>
    public const long BytesDescompactados = 100L * 1024 * 1024;

    /// <summary>Máximo de entradas no pacote .xlsx.</summary>
    public const int EntradasDoPacote = 1000;

    /// <summary>Acima disto, a prévia avisa.</summary>
    public const int LinhasParaAviso = 100_000;

    /// <summary>Acima disto, o arquivo é recusado.</summary>
    public const int LinhasMaximas = 200_000;

    /// <summary>Colunas lidas por linha; o modelo tem 8.</summary>
    public const int ColunasMaximas = 64;
}

internal static class Apoio
{
    /// <summary>Lê até <paramref name="limite"/> bytes; nulo quando o arquivo passa disso.</summary>
    public static byte[]? LerAteOLimite(Stream arquivo, long limite)
    {
        ArgumentNullException.ThrowIfNull(arquivo);

        using var memoria = new MemoryStream();
        var bloco = new byte[81920];
        int lidos;
        while ((lidos = arquivo.Read(bloco, 0, bloco.Length)) > 0)
        {
            if (memoria.Length + lidos > limite)
            {
                return null;
            }

            memoria.Write(bloco, 0, lidos);
        }

        return memoria.ToArray();
    }

    public static string Sha256(byte[] conteudo) => Convert.ToHexStringLower(SHA256.HashData(conteudo));

    /// <summary>Nome de coluna ou de aba para comparar: sem caixa, sem acento, sem espaço nas pontas.</summary>
    public static string Chave(string nome)
    {
        var texto = new StringBuilder(nome.Length);
        foreach (var c in nome.Trim().ToLowerInvariant())
        {
            texto.Append(c switch
            {
                'á' or 'à' or 'â' or 'ã' or 'ä' => 'a',
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'í' or 'ì' or 'î' or 'ï' => 'i',
                'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
                'ú' or 'ù' or 'û' or 'ü' => 'u',
                'ç' => 'c',
                _ => c,
            });
        }

        return texto.ToString();
    }
}
