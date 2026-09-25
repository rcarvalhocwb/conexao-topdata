using System.Text;
using Access.Domain.Credentials;
using Sync.Connectors.Rest;

namespace Unit.Tests.Credentials;

/// <summary>
/// O que a Catraca 4 consegue ler, aplicado na entrada do sistema.
/// </summary>
/// <remarks>
/// Um código que a catraca não lê precisa ser recusado quando entra, dias antes do evento
/// — não descoberto na porta. Ver docs/20-leitores-qr-e-cartao-mifare.md
/// </remarks>
public sealed class PerfisDeLeituraTests
{
    private static readonly TradutorDoContratoV1 Tradutor = new("zet", PerfisDeLeitura.QrCatraca4);

    private static byte[] Entrega(string qr) => Encoding.UTF8.GetBytes(
        $$"""{"versao":1,"ingressos":[{"referencia":"ZET-1","qr":"{{qr}}","situacao":"valido"}]}""");

    [Theory]
    [InlineData("1234")]
    [InlineData("0081443290")]
    [InlineData("1234567890123456")]
    public void QR_de_4_a_16_caracteres_passa(string qr)
    {
        var ingresso = Assert.Single(Tradutor.Traduzir(Entrega(qr), "application/json"));

        Assert.Equal(qr, ingresso.QrNormalizado);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345678901234567")]
    public void QR_fora_de_4_a_16_e_recusado_na_entrada(string qr)
    {
        var erro = Assert.Throws<FormatException>(() => Tradutor.Traduzir(Entrega(qr), "application/json"));

        Assert.Contains($"tem {qr.Length} caracteres", erro.Message, StringComparison.Ordinal);
        Assert.Contains("docs/20", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Um_identificador_uuid_como_qr_e_recusado()
    {
        // É o formato mais comum de identificador em sistema web — e tem 36 caracteres.
        // Se o Zet gerar o QR assim, nenhum ingresso online passa na catraca.
        var uuid = Guid.NewGuid().ToString();

        Assert.Throws<FormatException>(() => Tradutor.Traduzir(Entrega(uuid), "application/json"));
    }

    [Fact]
    public void O_perfil_de_QR_nao_mexe_no_conteudo()
    {
        Assert.Equal("00AB12cd", PerfisDeLeitura.QrCatraca4.Apply("00AB12cd"));
    }

    [Fact]
    public void Mifare_sempre_tem_10_digitos_e_zero_a_esquerda_e_restaurado()
    {
        // Um leitor de mesa que entregue o número sem os zeros precisa casar com a catraca,
        // que entrega os 10 dígitos.
        var perfil = PerfisDeLeitura.MifareCatraca4;

        Assert.Equal("0078234567", perfil.Apply("78234567"));
        Assert.Equal("0078234567", perfil.Apply("0078234567"));
        Assert.True(perfil.IsLengthAccepted(perfil.Apply("78234567")));
    }

    [Fact]
    public void Mifare_com_mais_de_10_digitos_nao_e_aceito()
    {
        // Um identificador de 7 bytes vira até 17 dígitos. Não cabe no que a catraca
        // entrega, e aceitar aqui cadastraria um cartão que a porta nunca vai reconhecer.
        var perfil = PerfisDeLeitura.MifareCatraca4;

        Assert.False(perfil.IsLengthAccepted(perfil.Apply("12345678901234")));
    }

    [Fact]
    public void Dez_digitos_e_exatamente_o_tamanho_de_um_identificador_de_4_bytes()
    {
        // A conta que justifica comprar cartão Mifare de 4 bytes.
        Assert.Equal(10, uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        Assert.True(Math.Pow(2, 56).ToString("F0", System.Globalization.CultureInfo.InvariantCulture).Length > 10);
    }
}
