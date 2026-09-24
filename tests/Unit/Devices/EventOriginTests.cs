using Access.Domain.Devices;

namespace Unit.Tests.Devices;

public sealed class EventOriginTests
{
    [Theory]
    [InlineData(1, KnownEventOrigin.Teclado)]
    [InlineData(2, KnownEventOrigin.Leitor1)]
    [InlineData(3, KnownEventOrigin.Leitor2)]
    [InlineData(5, KnownEventOrigin.FimTempoAcionamento)]
    [InlineData(6, KnownEventOrigin.GiroConfirmado)]
    [InlineData(7, KnownEventOrigin.CartaoRecolhidoUrna)]
    [InlineData(20, KnownEventOrigin.UrnaCheia)]
    [InlineData(21, KnownEventOrigin.QrCode)]
    public void Reconhece_origens_documentadas(int raw, KnownEventOrigin esperada)
    {
        var origem = EventOrigin.FromRaw(raw);

        Assert.True(origem.IsKnown);
        Assert.Equal(esperada, origem.Known);
        Assert.Equal(raw, origem.Raw);
    }

    /// <summary>
    /// Estes valores não constam da tabela oficial do manual. Não são inválidos —
    /// são desconhecidos, e precisam sobreviver íntegros.
    /// </summary>
    [Theory]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(19)]
    public void Preserva_origens_ausentes_da_tabela_oficial(int raw)
    {
        var origem = EventOrigin.FromRaw(raw);

        Assert.False(origem.IsKnown);
        Assert.Null(origem.Known);
        Assert.Equal(raw, origem.Raw);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(255)]
    [InlineData(-1)]
    public void Nunca_lanca_para_valor_inesperado(int raw)
    {
        var origem = EventOrigin.FromRaw(raw);

        Assert.False(origem.IsKnown);
        Assert.Equal(raw, origem.Raw);
    }

    [Fact]
    public void Somente_a_origem_6_confirma_passagem_fisica()
    {
        Assert.True(EventOrigin.FromRaw(6).ConfirmaPassagemFisica);

        foreach (var raw in Enumerable.Range(-1, 260).Where(v => v != 6))
        {
            Assert.False(
                EventOrigin.FromRaw(raw).ConfirmaPassagemFisica,
                $"origem {raw} não pode confirmar passagem física");
        }
    }

    /// <summary>
    /// A origem 4 é o sensor de giro legado, declarado obsoleto pelo manual.
    /// Tratá-la como confirmação de passagem inflaria a contagem de público.
    /// </summary>
    [Fact]
    public void Sensor_de_catraca_legado_nao_confirma_passagem()
    {
        var origem = EventOrigin.From(KnownEventOrigin.SensorCatracaLegado);

        Assert.True(origem.IsKnown);
        Assert.False(origem.ConfirmaPassagemFisica);
    }

    [Fact]
    public void Origem_desconhecida_se_identifica_no_texto()
    {
        Assert.Contains("Desconhecida", EventOrigin.FromRaw(14).ToString(), StringComparison.Ordinal);
        Assert.Contains("14", EventOrigin.FromRaw(14).ToString(), StringComparison.Ordinal);
    }
}
