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
}
