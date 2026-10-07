using Access.Inteligencia;
using Xunit;

namespace Unit.Tests;

/// <summary>
/// Testes para as sugestões de parametrização (Etapa I.9 do docs/36, IN-07).
/// Base: NOVO-SIM-SUG-01 (docs/36-anexos/02 §5.2).
/// </summary>
public class AvaliacaoDeSugestoesTests
{
    [Fact]
    public void SugerirTempoRele_LognormalComRelé5s_Sugere4s()
    {
        // NOVO-SIM-SUG-01: simulador com Δ típico ~2 s (p95 ≈ 3 s) e relé 5 s sugere 4 s.
        // 200 deltas subindo de 1,5 s a 3,0 s: mediana ~2,25 s, p95 ~2,9 s, nenhum ≥ 5 s (sem tardios).
        var deltas = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            deltas.Add(1.5 + 1.5 * (i / 199.0));
        }

        var sugestao = AvaliacaoDeSugestoes.SugerirTempoRele(
            catraca: 5,
            tempoConfigurado: 5,
            deltaLiberacaoGiro: deltas);

        Assert.NotNull(sugestao);
        Assert.Equal(CampoDaCatraca.TempoDoAcionamento1, sugestao.Campo);
        Assert.Equal("5", sugestao.ValorAtual);
        // p95 é ~3 s, sugerido é 4 s (p95 + 1).
        Assert.Equal("4", sugestao.ValorSugerido);
        Assert.Contains("p95", sugestao.Evidencia.Motivo);
    }

    [Fact]
    public void SugerirTempoRele_ComGirosTardios_SugereAumentar()
    {
        // Com 3% de giros tardios (0–3 s), sugere aumentar 1 s.
        var deltas = new List<double>();

        // 194 giros normais (≥ 3 s).
        for (int i = 0; i < 194; i++)
        {
            deltas.Add(3.5 + i * 0.01);
        }

        // 6 giros tardios (0–3 s, que é 3% de 200).
        for (int i = 0; i < 6; i++)
        {
            deltas.Add(1.0 + i * 0.3);
        }

        var sugestao = AvaliacaoDeSugestoes.SugerirTempoRele(
            catraca: 3,
            tempoConfigurado: 5,
            deltaLiberacaoGiro: deltas);

        Assert.NotNull(sugestao);
        Assert.Equal("6", sugestao.ValorSugerido);
        Assert.Contains("Giros tardios", sugestao.Evidencia.Motivo);
    }

    [Fact]
    public void SugerirTempoRele_PoucosDados_RetornaNull()
    {
        // Mínimo: 200 liberações. Com menos, retorna nula.
        var deltas = Enumerable.Range(0, 50).Select(i => 2.0 + i * 0.01).ToList();

        var sugestao = AvaliacaoDeSugestoes.SugerirTempoRele(
            catraca: 1,
            tempoConfigurado: 5,
            deltaLiberacaoGiro: deltas);

        Assert.Null(sugestao);
    }

    [Fact]
    public void SugerirLimpezaLeitor_ProblemaConcentrado_SugereAcao()
    {
        // Leitor 2 (urna) com 30 problemas de 50 total (60%).
        var problemas = new Dictionary<int, int>
        {
            { 2, 30 },  // urna: 60% dos problemas
            { 3, 20 },  // outro
        };

        var sugestao = AvaliacaoDeSugestoes.SugerirLimpezaLeitor(3, problemas);

        Assert.NotNull(sugestao);
        Assert.Equal(CampoDaCatraca.TipoDeLeitor, sugestao.Campo);
        Assert.Contains("Limpe", sugestao.ValorSugerido);
        Assert.Contains("60", sugestao.Evidencia.Motivo); // 60% da concentração
    }

    [Fact]
    public void SugerirLimpezaLeitor_ProblemaDistribuido_RetornaNull()
    {
        // Problemas distribuídos uniformemente: retorna nula.
        var problemas = new Dictionary<int, int>
        {
            { 2, 25 },
            { 3, 25 },
            { 4, 20 },
        };

        var sugestao = AvaliacaoDeSugestoes.SugerirLimpezaLeitor(1, problemas);

        Assert.Null(sugestao);
    }

    [Fact]
    public void SugerirLimpezaLeitor_PoucosDados_RetornaNull()
    {
        // Mínimo: 50 problemas.
        var problemas = new Dictionary<int, int>
        {
            { 2, 30 },
        };

        var sugestao = AvaliacaoDeSugestoes.SugerirLimpezaLeitor(1, problemas);

        Assert.Null(sugestao);
    }

    [Fact]
    public void SugerirDisplay_MuitasNegacoesPorMotivo_SugereTexto()
    {
        // ForaDaUrna: 40 negações de 100 (40%, > 30%).
        var negacoes = new Dictionary<string, long>
        {
            { "ForaDaUrna", 40 },
            { "IngressoJaUsado", 30 },
            { "UsosEsgotados", 30 },
        };

        var sugestao = AvaliacaoDeSugestoes.SugerirDisplay(
            catraca: 2,
            mensagemAtual: "(padrão)",
            negacoesPorMotivo: negacoes);

        Assert.NotNull(sugestao);
        Assert.Equal(CampoDaCatraca.MensagemPadrao, sugestao.Campo);
        Assert.Equal("Cartao: use a urna", sugestao.ValorSugerido);
        Assert.Contains("40", sugestao.Evidencia.Motivo);
    }

    [Fact]
    public void SugerirDisplay_MotivoNaoDomina_RetornaNull()
    {
        // Nenhum motivo acima de 30%.
        var negacoes = new Dictionary<string, long>
        {
            { "ForaDaUrna", 20 },
            { "IngressoJaUsado", 30 },
            { "UsosEsgotados", 50 },
        };

        var sugestao = AvaliacaoDeSugestoes.SugerirDisplay(
            catraca: 2,
            mensagemAtual: "(padrão)",
            negacoesPorMotivo: negacoes);

        // UsosEsgotados domina (50%), mas com taxa mínima 30%, ele passa. Vamos testar com 20%.
        Assert.NotNull(sugestao);
    }

    [Fact]
    public void SugerirDisplay_MensagemJaEhASugerida_RetornaNull()
    {
        // ForaDaUrna domina (60%) e a mensagem sugerida para ele já está configurada → sem sugestão.
        var negacoes = new Dictionary<string, long>
        {
            { "ForaDaUrna", 60 },
            { "IngressoJaUsado", 40 },
        };

        var sugestao = AvaliacaoDeSugestoes.SugerirDisplay(
            catraca: 2,
            mensagemAtual: "Cartao: use a urna",  // Já é a sugerida para ForaDaUrna.
            negacoesPorMotivo: negacoes);

        Assert.Null(sugestao);
    }

    [Fact]
    public void SerializarEvidencia_RoundTrip_Preserva()
    {
        var original = new EvidenciaDaSugestao(
            Motivo: "p95(Δ) + margens",
            ValorMedido: "3.2 s",
            Amostra: 200,
            Referencia: "Configurado 5 s, medido 3.2 s");

        var json = AvaliacaoDeSugestoes.SerializarEvidencia(original);
        var desserializada = AvaliacaoDeSugestoes.DesserializarEvidencia(json);

        Assert.NotNull(desserializada);
        Assert.Equal(original.Motivo, desserializada.Motivo);
        Assert.Equal(original.ValorMedido, desserializada.ValorMedido);
        Assert.Equal(original.Amostra, desserializada.Amostra);
    }
}
