using Access.Domain.Credentials;
using Shared.Observability;

namespace Unit.Tests.Observabilidade;

/// <summary>
/// SEC-LOG-01 — nenhum dado sensível pode chegar ao log.
/// </summary>
public sealed class RedacaoTests
{
    [Theory]
    [InlineData("cartao 0001234567 lido no leitor 1")]
    [InlineData("credencial 123456")]
    [InlineData("usuario 9876543210987654")]
    public void Redige_numeros_com_cara_de_cartao(string texto)
    {
        var redigido = RedatorDeDadoSensivel.Redigir(texto);

        Assert.Contains(RedatorDeDadoSensivel.Marca, redigido, StringComparison.Ordinal);
        Assert.False(RedatorDeDadoSensivel.ParecemHaverDadosSensiveis(redigido));
    }

    /// <summary>
    /// Um log que esconde tudo não serve para achar defeito. Porta, número de Inner,
    /// versão e duração precisam sobreviver.
    /// </summary>
    [Theory]
    [InlineData("porta 3570 aberta")]
    [InlineData("inner 8 em Polling")]
    [InlineData("latencia 1234 ms")]
    [InlineData("firmware 4.2.0 linha 16")]
    [InlineData("ip 192.168.0.10 respondeu")]
    public void Preserva_o_que_e_diagnostico(string texto) =>
        Assert.Equal(texto, RedatorDeDadoSensivel.Redigir(texto));

    /// <summary>
    /// A fração de segundo de um carimbo ISO-8601 tem 7 dígitos. Sem âncoras, todo
    /// horário no log viraria [REDIGIDO] — e o log perderia a serventia.
    /// </summary>
    [Theory]
    [InlineData("2026-09-24T19:00:00.0000000+00:00")]
    [InlineData("evento em 2026-09-24T19:00:00.0000000Z no inner 8")]
    public void Nao_mutila_carimbo_de_tempo(string texto) =>
        Assert.Equal(texto, RedatorDeDadoSensivel.Redigir(texto));

    /// <summary>
    /// O leitor facial manda a foto de quem NÃO está cadastrado dentro do evento
    /// sendlog, em Base64. Dado pessoal sensível entrando pelo caminho de evento.
    /// Ver docs/13-sdk-facial.md, seção 5.
    /// </summary>
    [Fact]
    public void Redige_foto_em_base64_do_leitor_facial()
    {
        const string evento =
            """{"cmd":"sendlog","enrollid":99999999,"image":"data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD"}""";

        var redigido = RedatorDeDadoSensivel.Redigir(evento);

        Assert.DoesNotContain("/9j/4AAQSkZJRgABAQAAAQABAAD", redigido, StringComparison.Ordinal);
        Assert.Contains(RedatorDeDadoSensivel.Marca, redigido, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cartao")]
    [InlineData("Cartao")]
    [InlineData("password")]
    [InlineData("template")]
    [InlineData("image")]
    [InlineData("nome")]
    [InlineData("cpf")]
    public void Campo_com_nome_sensivel_e_removido_inteiro(string nome) =>
        Assert.Equal(RedatorDeDadoSensivel.Marca, RedatorDeDadoSensivel.RedigirCampo(nome, "qualquer valor"));

    /// <summary>
    /// Um nome próprio curto não casaria com nenhum padrão de valor. É por isso que o
    /// nome do campo também decide.
    /// </summary>
    [Fact]
    public void Nome_de_pessoa_e_removido_pelo_nome_do_campo()
    {
        Assert.Equal(RedatorDeDadoSensivel.Marca, RedatorDeDadoSensivel.RedigirCampo("nome", "Ana"));
        Assert.Equal("Ana", RedatorDeDadoSensivel.RedigirCampo("estado", "Ana"));
    }

    [Fact]
    public void Campo_comum_mantem_o_valor()
    {
        Assert.Equal("Polling", RedatorDeDadoSensivel.RedigirCampo("estado", "Polling"));
        Assert.Equal("8", RedatorDeDadoSensivel.RedigirCampo("inner", 8));
    }

    [Fact]
    public void Campo_comum_com_valor_sensivel_ainda_e_redigido()
    {
        var redigido = RedatorDeDadoSensivel.RedigirCampo("detalhe", "leu 0001234567");

        Assert.Contains(RedatorDeDadoSensivel.Marca, redigido, StringComparison.Ordinal);
    }

    /// <summary>
    /// A credencial já se mascara sozinha, então nem chega ao redator em texto claro.
    /// Duas barreiras, não uma.
    /// </summary>
    [Fact]
    public void Credencial_logada_por_engano_nao_vaza()
    {
        var credencial = CredentialValue.Create("0001234567", CredentialNormalization.Raw);

        var texto = RedatorDeDadoSensivel.Redigir($"decidindo para {credencial}");

        Assert.DoesNotContain("0001234567", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Texto_vazio_nao_quebra()
    {
        Assert.Equal(string.Empty, RedatorDeDadoSensivel.Redigir(null));
        Assert.Equal(string.Empty, RedatorDeDadoSensivel.Redigir(string.Empty));
    }
}
