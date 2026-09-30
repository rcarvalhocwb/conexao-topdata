using Access.Domain.Credentials;

namespace Unit.Tests.Credentials;

/// <summary>A impressão da trilha do cadastro (docs/35 B.1): HMAC, nunca o número, nunca a chave.</summary>
public sealed class ImpressaoDeCodigoTests
{
    private static readonly byte[] Chave = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];

    [Fact]
    public void Chave_curta_e_recusada()
    {
        Assert.Throws<ArgumentException>(() => new ImpressaoDeCodigo(new byte[31]));
    }

    [Fact]
    public void Mesmo_codigo_e_mesma_chave_dao_a_mesma_impressao()
    {
        var impressao = new ImpressaoDeCodigo(Chave);

        var valor = impressao.De("0000000101");

        Assert.Equal(valor, new ImpressaoDeCodigo(Chave).De("0000000101"));
        Assert.StartsWith(ImpressaoDeCodigo.Prefixo, valor, StringComparison.Ordinal);
        Assert.Equal(ImpressaoDeCodigo.Prefixo.Length + 64, valor.Length);
        Assert.Matches("^hmac-sha256:[0-9a-f]{64}$", valor);
        Assert.DoesNotContain("0000000101", valor, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_a_esquerda_muda_a_impressao()
    {
        var impressao = new ImpressaoDeCodigo(Chave);
        Assert.NotEqual(impressao.De("0000000101"), impressao.De("101"));
    }

    [Fact]
    public void Outra_chave_da_outra_impressao_e_outro_identificador()
    {
        var uma = new ImpressaoDeCodigo(Chave);
        var outra = new ImpressaoDeCodigo(new byte[32]);

        Assert.NotEqual(uma.De("99990000000101"), outra.De("99990000000101"));
        Assert.NotEqual(uma.IdDaChave, outra.IdDaChave);
    }

    [Fact]
    public void A_chave_nao_aparece_no_texto_nem_no_identificador()
    {
        var impressao = new ImpressaoDeCodigo(Chave);
        var hexa = Convert.ToHexString(Chave);

        foreach (var texto in new[] { impressao.ToString(), impressao.IdDaChave })
        {
            Assert.DoesNotContain(hexa, texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Convert.ToBase64String(Chave), texto, StringComparison.Ordinal);
        }

        Assert.Matches("^k1:[0-9a-f]{16}$", impressao.IdDaChave);
    }
}
