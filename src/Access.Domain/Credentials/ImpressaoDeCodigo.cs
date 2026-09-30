using System.Security.Cryptography;
using System.Text;

namespace Access.Domain.Credentials;

/// <summary>
/// Impressão de um código de cartão: HMAC-SHA256 do código normalizado, com uma chave que
/// não está na base. Permite achar o histórico de um cartão sem guardar o número nele.
/// </summary>
/// <remarks>
/// <para>
/// HMAC, e não um hash simples, porque o espaço dos códigos é pequeno: um cartão tem de 4 a
/// 16 dígitos, e um SHA-256 puro de 10 dígitos se desfaz testando os 10^10 valores em
/// minutos. Com a chave guardada fora da base (DPAPI no escopo da máquina, como o segredo
/// da nuvem — ADR-0014), quem copia o arquivo da base não consegue fazer o mesmo teste
/// (docs/34 §5.1 e §6; docs/34-anexos/03 §1.5 e §5.4).
/// </para>
/// <para>
/// A entrada é o código <b>já normalizado</b> pelo perfil do provedor
/// (<see cref="PerfisDeLeitura.Normalizar"/>): o mesmo cartão lido na catraca e digitado no
/// cadastro precisa dar a mesma impressão.
/// </para>
/// <para>
/// A chave nunca aparece em <see cref="ToString"/>, em log nem na base; o
/// <see cref="IdDaChave"/> é derivado dela por SHA-256 e só diz <i>qual</i> chave calculou a
/// impressão, para que uma troca de chave (por evento, no expurgo da Etapa B.9) não exija
/// reescrever a trilha, que é só-INSERT.
/// </para>
/// </remarks>
public sealed class ImpressaoDeCodigo
{
    /// <summary>Prefixo gravado antes dos 64 dígitos hexadecimais.</summary>
    public const string Prefixo = "hmac-sha256:";

    /// <summary>Tamanho mínimo da chave, em bytes: o do próprio SHA-256.</summary>
    public const int TamanhoMinimoDaChave = 32;

    private readonly byte[] _chave;

    /// <param name="chave">Chave secreta; pelo menos <see cref="TamanhoMinimoDaChave"/> bytes.</param>
    public ImpressaoDeCodigo(ReadOnlySpan<byte> chave)
    {
        if (chave.Length < TamanhoMinimoDaChave)
        {
            throw new ArgumentException(
                $"A chave da impressão precisa de pelo menos {TamanhoMinimoDaChave} bytes.", nameof(chave));
        }

        _chave = chave.ToArray();

        // Rótulo fixo na frente: o identificador não é o SHA-256 "cru" da chave, que
        // coincidiria com qualquer outro uso ingênuo do mesmo material.
        var rotulo = "ConexaoTopdata:id-da-chave-da-impressao:"u8;
        var material = new byte[rotulo.Length + _chave.Length];
        rotulo.CopyTo(material);
        _chave.CopyTo(material, rotulo.Length);
        IdDaChave = "k1:" + Convert.ToHexStringLower(SHA256.HashData(material))[..16];
    }

    /// <summary>Qual chave calculou a impressão. Não revela a chave.</summary>
    public string IdDaChave { get; }

    /// <summary>A impressão de um código normalizado: <c>hmac-sha256:</c> + 64 hexadecimais.</summary>
    /// <param name="codigoNormalizado">O código depois de <see cref="PerfisDeLeitura.Normalizar"/>.</param>
    public string De(string codigoNormalizado)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoNormalizado);
        var mac = HMACSHA256.HashData(_chave, Encoding.UTF8.GetBytes(codigoNormalizado));
        return Prefixo + Convert.ToHexStringLower(mac);
    }

    /// <summary>Só o identificador da chave; nunca a chave.</summary>
    public override string ToString() => $"impressao:{IdDaChave}";
}
