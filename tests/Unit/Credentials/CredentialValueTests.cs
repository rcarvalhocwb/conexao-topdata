using Access.Domain.Credentials;

namespace Unit.Tests.Credentials;

public sealed class CredentialValueTests
{
    /// <summary>
    /// A regressão mais cara do projeto: perder zeros à esquerda faz cartão válido ser
    /// recusado no portão. Ver docs/ADR/ADR-0008.
    /// </summary>
    [Theory]
    [InlineData("0001234")]
    [InlineData("00000001")]
    [InlineData("0")]
    [InlineData("000")]
    public void Preserva_zeros_a_esquerda(string bruto)
    {
        var credencial = CredentialValue.Create(bruto, CredentialNormalization.Raw);

        Assert.Equal(bruto, credencial.Normalized);
        Assert.Equal(bruto, credencial.Raw);
    }

    [Fact]
    public void Cartoes_que_so_diferem_nos_zeros_a_esquerda_sao_credenciais_distintas()
    {
        var comZeros = CredentialValue.Create("0001234", CredentialNormalization.Raw);
        var semZeros = CredentialValue.Create("1234", CredentialNormalization.Raw);

        Assert.NotEqual(comZeros, semZeros);
    }

    [Fact]
    public void Completa_com_zeros_a_esquerda_quando_o_perfil_define_comprimento()
    {
        var perfil = new CredentialNormalization("wiegand-8", padLeftTo: 8);

        var credencial = CredentialValue.Create("1234", perfil);

        Assert.Equal("00001234", credencial.Normalized);
        Assert.Equal("1234", credencial.Raw);
    }

    [Fact]
    public void Nunca_trunca_valor_maior_que_o_comprimento_do_perfil()
    {
        var perfil = new CredentialNormalization("wiegand-8", padLeftTo: 8);

        var credencial = CredentialValue.Create("1234567890", perfil);

        Assert.Equal("1234567890", credencial.Normalized);
    }

    [Fact]
    public void ToString_mascara_o_numero()
    {
        var credencial = CredentialValue.Create("0001234567", CredentialNormalization.Raw);

        var texto = credencial.ToString();

        Assert.DoesNotContain("0001234567", texto, StringComparison.Ordinal);
        Assert.Contains("****", texto, StringComparison.Ordinal);
        Assert.Contains("10", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_de_credencial_curta_nao_revela_nada()
    {
        var credencial = CredentialValue.Create("1234", CredentialNormalization.Raw);

        var texto = credencial.ToString();

        Assert.DoesNotContain("1234", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Compara_por_valor_normalizado_e_perfil()
    {
        var perfil = new CredentialNormalization("p1");
        var outroPerfil = new CredentialNormalization("p2");

        Assert.Equal(
            CredentialValue.Create("123456", perfil),
            CredentialValue.Create("123456", perfil));

        Assert.NotEqual(
            CredentialValue.Create("123456", perfil),
            CredentialValue.Create("123456", outroPerfil));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Recusa_credencial_vazia(string bruto) =>
        Assert.Throws<ArgumentException>(() => CredentialValue.Create(bruto, CredentialNormalization.Raw));

    [Fact]
    public void Aplica_maiusculas_para_leitores_que_entregam_letras()
    {
        var perfil = new CredentialNormalization("qr-ascii", upperCase: true);

        var credencial = CredentialValue.Create("abc123xyz", perfil);

        Assert.Equal("ABC123XYZ", credencial.Normalized);
    }

    [Fact]
    public void Valida_comprimentos_aceitos_quando_o_equipamento_usa_digitos_variaveis()
    {
        var perfil = new CredentialNormalization(
            "abatrack-variavel",
            allowedLengths: new HashSet<int> { 6, 8, 10 });

        Assert.True(perfil.IsLengthAccepted("123456"));
        Assert.True(perfil.IsLengthAccepted("1234567890"));
        Assert.False(perfil.IsLengthAccepted("1234567"));
    }

    [Fact]
    public void Sem_lista_de_comprimentos_aceita_qualquer_um()
    {
        var perfil = new CredentialNormalization("livre");

        Assert.True(perfil.IsLengthAccepted("1"));
        Assert.True(perfil.IsLengthAccepted("1234567890123456"));
    }
}
