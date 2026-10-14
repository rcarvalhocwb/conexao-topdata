using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Recuperação da senha de um administrador pelo suporte Rayzer, por desafio e resposta (docs/58).
/// </summary>
/// <remarks>
/// <para>
/// <b>Não é senha mestra.</b> Não existe senha guardada que abra o sistema. Quem esqueceu pede, na
/// tela de login, um <b>código de pedido</b>: vale só para esta instalação, para este login e por
/// <see cref="Validade"/>. O desenvolvedor assina o pedido com a chave privada dele (que nunca está
/// no repositório nem no instalador) e devolve o <b>código de resposta</b>. O serviço confere a
/// assinatura com as chaves públicas de <see cref="ChavesDoSuporte"/>, e só então grava a senha nova
/// que a própria pessoa escolheu. Tudo fica na trilha de usuários (<c>app_user_event</c>).
/// </para>
/// <para>
/// O pedido tem 36 bytes: a marca <c>XAR1</c>, 16 bytes aleatórios, o fim da validade (segundos Unix)
/// e 8 bytes do SHA-256 de instalação + login. A resposta é a assinatura ECDSA P-256 com SHA-256,
/// no formato IEEE P1363 (64 bytes). Os dois viajam como texto base32 (Crockford), em grupos de 4.
/// </para>
/// </remarks>
public static class RecuperacaoPeloSuporte
{
    /// <summary>Quanto tempo um pedido vale.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    private const int TamanhoDoPedido = 36;
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly byte[] Marca = "XAR1"u8.ToArray();

    /// <summary>Há pelo menos uma chave pública do suporte nesta versão.</summary>
    public static bool Configurada => ChavesDoSuporte.Publicas.Count > 0;

    /// <summary>Cria um pedido para esta instalação e este login, válido até <c>agora + Validade</c>.</summary>
    public static byte[] Pedido(string instalacao, string login, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instalacao);
        ArgumentException.ThrowIfNullOrWhiteSpace(login);

        var pedido = new byte[TamanhoDoPedido];
        Marca.CopyTo(pedido, 0);
        RandomNumberGenerator.Fill(pedido.AsSpan(4, 16));
        BinaryPrimitives.WriteInt64BigEndian(pedido.AsSpan(20, 8), (agora + Validade).ToUnixTimeSeconds());
        Impressao(instalacao, login).CopyTo(pedido, 28);
        return pedido;
    }

    /// <summary>Até quando o pedido vale; nulo se não é um pedido desta versão.</summary>
    public static DateTimeOffset? ValidoAte(byte[]? pedido) =>
        pedido is { Length: TamanhoDoPedido } && pedido.AsSpan(0, 4).SequenceEqual(Marca)
            ? DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(pedido.AsSpan(20, 8)))
            : null;

    /// <summary>
    /// Confere a resposta: o pedido é desta instalação e deste login, ainda vale, e a assinatura é de
    /// uma das chaves públicas do suporte.
    /// </summary>
    public static bool Confere(
        byte[] pedido,
        byte[]? assinatura,
        string instalacao,
        string login,
        DateTimeOffset agora,
        IEnumerable<string>? chavesPublicas = null)
    {
        ArgumentNullException.ThrowIfNull(pedido);

        if (assinatura is not { Length: 64 } || ValidoAte(pedido) is not { } ate || agora > ate
            || !pedido.AsSpan(28, 8).SequenceEqual(Impressao(instalacao, login)))
        {
            return false;
        }

        foreach (var chave in chavesPublicas ?? ChavesDoSuporte.Publicas)
        {
            using var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(chave), out _);
            }
            catch (Exception erro) when (erro is FormatException or CryptographicException)
            {
                continue;
            }

            if (ecdsa.VerifyData(pedido, assinatura, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Assina um pedido (ferramenta do desenvolvedor; o serviço nunca tem a chave privada).</summary>
    public static byte[] Assinar(byte[] pedido, ECDsa chavePrivada)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(chavePrivada);
        return chavePrivada.SignData(pedido, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Bytes em texto base32 (Crockford), em grupos de 4 separados por hífen.</summary>
    public static string ParaTexto(byte[] dados)
    {
        ArgumentNullException.ThrowIfNull(dados);

        var texto = new StringBuilder();
        var bits = 0;
        var acumulado = 0;
        foreach (var b in dados)
        {
            acumulado = (acumulado << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                texto.Append(Alfabeto[(acumulado >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            texto.Append(Alfabeto[(acumulado << (5 - bits)) & 31]);
        }

        return string.Join('-', texto.ToString().Chunk(4).Select(g => new string(g)));
    }

    /// <summary>
    /// O texto de volta em bytes. Tolera espaço, hífen, minúscula e as confusões de leitura (O→0,
    /// I e L→1). Nulo se houver letra que não existe no alfabeto.
    /// </summary>
    public static byte[]? DoTexto(string? texto, int tamanho)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var bytes = new List<byte>();
        var bits = 0;
        var acumulado = 0;
        foreach (var bruto in texto.ToUpperInvariant())
        {
            if (bruto is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            var c = bruto switch { 'O' => '0', 'I' or 'L' => '1', _ => bruto };
            var valor = Alfabeto.IndexOf(c, StringComparison.Ordinal);
            if (valor < 0)
            {
                return null;
            }

            acumulado = ((acumulado << 5) | valor) & 0xFFFF;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(acumulado >> (bits - 8)));
                bits -= 8;
            }
        }

        return bytes.Count >= tamanho ? [.. bytes.Take(tamanho)] : null;
    }

    /// <summary>O tamanho do pedido, em bytes (para <see cref="DoTexto"/>).</summary>
    public static int BytesDoPedido => TamanhoDoPedido;

    /// <summary>O tamanho da resposta, em bytes (para <see cref="DoTexto"/>).</summary>
    public static int BytesDaResposta => 64;

    private static byte[] Impressao(string instalacao, string login) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{instalacao}\n{login.Trim().ToLowerInvariant()}"))[..8];
}
