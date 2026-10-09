using Access.Inteligencia;

namespace Unit.Tests.Inteligencia;

/// <summary>
/// As funções puras da fundação da camada inteligente (Etapa I.0 do docs/36): a chave, o
/// orçamento e a saúde do Analisador. Sem relógio de verdade: todo instante entra pelo teste
/// (docs/36-anexos/02 §7, item 1).
/// </summary>
public sealed class FundacaoDaCamadaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("sim", false)]
    [InlineData("true", false)]
    [InlineData(" 1", false)]
    [InlineData("1", true)]
    public void So_o_valor_1_liga_a_camada(string? valor, bool ligada) =>
        Assert.Equal(ligada, ChavesDaInteligencia.EstaLigada(valor));

    [Fact]
    public void A_chave_tem_o_nome_do_desenho() =>
        Assert.Equal("inteligencia.ligada", ChavesDaInteligencia.Ligada);

    [Fact]
    public void O_orcamento_do_ciclo_curto_e_50_ms_e_o_limite_nao_estoura()
    {
        var orcamento = OrcamentoDoCiclo.CicloCurto;

        Assert.Equal(TimeSpan.FromMilliseconds(50), orcamento.Limite);
        Assert.False(orcamento.Estourou(TimeSpan.FromMilliseconds(50)));
        Assert.True(orcamento.Estourou(TimeSpan.FromMilliseconds(50.001)));
    }

    [Fact]
    public void A_saude_conta_ciclos_estouros_e_falhas_e_guarda_so_o_tipo_do_erro()
    {
        var saude = SituacaoDoAnalisador.Inicial(OrcamentoDoCiclo.CicloCurto);
        Assert.False(saude.Ligada);
        Assert.Null(saude.UltimoCicloEm);

        saude = saude.ComCiclo(Agora, TimeSpan.FromMilliseconds(3), 5, estourou: false);
        saude = saude.ComCiclo(Agora.AddSeconds(1), TimeSpan.FromMilliseconds(80), 2, estourou: true);
        saude = saude.ComFalha(Agora.AddSeconds(2), TimeSpan.FromMilliseconds(1), new TimeoutException("código 1000000001"), estourou: false);

        Assert.Equal(2, saude.Ciclos);
        Assert.Equal(1, saude.Estouros);
        Assert.Equal(1, saude.Falhas);
        Assert.Equal(7, saude.TentativasLidas);
        Assert.Equal(Agora.AddSeconds(2), saude.UltimoCicloEm);
        Assert.Equal(nameof(TimeoutException), saude.UltimoErro);
        Assert.DoesNotContain("1000000001", saude.ToString(), StringComparison.Ordinal);
    }
}
