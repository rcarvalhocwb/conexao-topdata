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

    /// <summary>
    /// A lista de perfis é uma só, e a leitura usa <c>raw</c> até a parametrização por
    /// catraca (docs/35, Etapa A) — com ele, a decisão compara o mesmo texto de sempre.
    /// </summary>
    [Fact]
    public void Os_perfis_conhecidos_sao_tres_e_a_leitura_usa_raw()
    {
        Assert.Equal(["mifare-catraca4", "qr-catraca4", "raw"], PerfisDeLeitura.Todos.Keys.Order(StringComparer.Ordinal));
        Assert.Same(CredentialNormalization.Raw, PerfisDeLeitura.DaLeitura);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_codigo_nao_ha_o_que_normalizar(string? codigo)
    {
        foreach (var perfil in PerfisDeLeitura.Todos.Values)
        {
            Assert.Null(PerfisDeLeitura.Normalizar(codigo, perfil));
        }
    }

    [Fact]
    public void Com_o_perfil_raw_normalizar_e_so_tirar_espacos_nas_pontas()
    {
        // É o que a decisão fazia antes (RawCardData.Trim()); nada muda com o perfil padrão.
        Assert.Equal("0000000101", PerfisDeLeitura.Normalizar(" 0000000101 ", CredentialNormalization.Raw));
        Assert.Equal("00ab12CD", PerfisDeLeitura.Normalizar("00ab12CD", CredentialNormalization.Raw));
    }

    /// <summary>ADR-0008: nenhum perfil tira zero à esquerda, nem transforma em número.</summary>
    [Theory]
    [InlineData("0000000101")]
    [InlineData("0099994567")]
    [InlineData("00123")]
    [InlineData("000099990000000101")]
    public void Nenhum_perfil_remove_zeros_a_esquerda(string codigo)
    {
        foreach (var perfil in PerfisDeLeitura.Todos.Values)
        {
            var normalizado = PerfisDeLeitura.Normalizar(codigo, perfil)!;

            Assert.EndsWith(codigo, normalizado, StringComparison.Ordinal);
            Assert.True(normalizado.Length >= codigo.Length, perfil.Name);
        }
    }

    [Fact]
    public void Dez_digitos_e_exatamente_o_tamanho_de_um_identificador_de_4_bytes()
    {
        // A conta que justifica comprar cartão Mifare de 4 bytes.
        Assert.Equal(10, uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        Assert.True(Math.Pow(2, 56).ToString("F0", System.Globalization.CultureInfo.InvariantCulture).Length > 10);
    }
}
