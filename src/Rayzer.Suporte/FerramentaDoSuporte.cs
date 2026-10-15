using System.Security.Cryptography;
using Access.Infrastructure.SQLite;

namespace Rayzer.Suporte;

/// <summary>O que a ferramenta faz, sem console: gerar as chaves e assinar um pedido (docs/58).</summary>
public static class FerramentaDoSuporte
{
    // PBKDF2 com SHA-256 protege o arquivo da chave privada com a senha do desenvolvedor.
    private static readonly PbeParameters Protecao = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000);

    /// <summary>Gera um par ECDSA P-256: a privada em PEM cifrada com a senha, a pública em base64.</summary>
    public static (string PrivadaPem, string PublicaBase64) GerarChaves(string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senha);

        using var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (chave.ExportEncryptedPkcs8PrivateKeyPem(senha.AsSpan(), Protecao),
                Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo()));
    }

    /// <summary>Assina um pedido. Devolve até quando ele vale e a resposta, ou nulo se o pedido não é válido.</summary>
    public static (DateTimeOffset ValidoAte, string Resposta)? Assinar(string privadaPem, string senha, string pedido)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privadaPem);
        ArgumentNullException.ThrowIfNull(senha);

        var bytes = RecuperacaoPeloSuporte.DoTexto(pedido, RecuperacaoPeloSuporte.BytesDoPedido);
        if (RecuperacaoPeloSuporte.ValidoAte(bytes) is not { } ate)
        {
            return null;
        }

        using var chave = ECDsa.Create();
        chave.ImportFromEncryptedPem(privadaPem.AsSpan(), senha.AsSpan());
        return (ate, RecuperacaoPeloSuporte.ParaTexto(RecuperacaoPeloSuporte.Assinar(bytes!, chave)));
    }

    /// <summary>Emite uma licença (docs/60 §1) assinada com a chave do suporte.</summary>
    /// <param name="privadaPem">A chave privada do suporte, cifrada.</param>
    /// <param name="senha">A senha da chave.</param>
    /// <param name="cliente">Para quem.</param>
    /// <param name="instalacao">O identificador da instalação (Diagnóstico) ou <c>*</c>.</param>
    /// <param name="venceEm">Último dia de validade.</param>
    /// <param name="agora">Agora (vai na licença e no número).</param>
    public static string EmitirLicenca(string privadaPem, string senha, string cliente, string instalacao, DateOnly venceEm, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privadaPem);
        ArgumentNullException.ThrowIfNull(senha);
        ArgumentException.ThrowIfNullOrWhiteSpace(cliente);
        ArgumentException.ThrowIfNullOrWhiteSpace(instalacao);

        var numero = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"L-{agora.UtcDateTime:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}");

        using var chave = ECDsa.Create();
        chave.ImportFromEncryptedPem(privadaPem.AsSpan(), senha.AsSpan());
        return Licencas.Emitir(new DadosDaLicenca(numero, cliente.Trim(), instalacao.Trim(), venceEm, agora), chave);
    }

    /// <summary>
    /// Assina os bytes de um arquivo (a ficha da versão, docs/60 §2.4): base64 da assinatura ECDSA P-256 IEEE P1363.
    /// </summary>
    public static string AssinarArquivo(string privadaPem, string senha, byte[] conteudo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privadaPem);
        ArgumentNullException.ThrowIfNull(senha);
        ArgumentNullException.ThrowIfNull(conteudo);

        using var chave = ECDsa.Create();
        chave.ImportFromEncryptedPem(privadaPem.AsSpan(), senha.AsSpan());
        return Convert.ToBase64String(chave.SignData(conteudo, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>A chave pública (base64) de uma chave privada cifrada: para conferir qual chave está no segredo.</summary>
    public static string PublicaDe(string privadaPem, string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privadaPem);
        ArgumentNullException.ThrowIfNull(senha);

        using var chave = ECDsa.Create();
        chave.ImportFromEncryptedPem(privadaPem.AsSpan(), senha.AsSpan());
        return Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo());
    }
}
