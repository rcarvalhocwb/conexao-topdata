using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Integration.Tests;

/// <summary>
/// P0-01/P0-02 no painel: catraca simulada nunca passa por física, e os estados da nuvem não se
/// confundem entre si (credencial recusada não é "sem internet"; segredo ausente não é "não configurada").
/// </summary>
public sealed class SituacaoPorCategoriaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Simulacao_com_catracas_ativas_nao_diz_nenhuma_catraca_conectada()
    {
        var estado = EstadoDoPainel.De(new ObterEstadoResponse
        {
            Simulacao = true,
            Nivel = NivelDeDegradacao.T0Normal,
            EquipamentosConectados = 0,
            CatracasSimuladasAtivas = 2,
        }, Agora);

        Assert.DoesNotContain("Nenhuma catraca conectada", estado.Mensagem, StringComparison.Ordinal);
        Assert.StartsWith("MODO SIMULAÇÃO", estado.Mensagem, StringComparison.Ordinal);
        Assert.Equal(2, estado.CatracasSimuladasAtivas);
    }

    [Fact]
    public void Sem_nenhuma_catraca_ativa_continua_dizendo_nenhuma_catraca_conectada()
    {
        var estado = EstadoDoPainel.De(new ObterEstadoResponse
        {
            Nivel = NivelDeDegradacao.T0Normal,
            EquipamentosConectados = 0,
        }, Agora);

        Assert.Equal("Nenhuma catraca conectada — verifique a rede e a alimentação", estado.Mensagem);
    }

    [Fact]
    public void Catraca_simulada_leva_o_prefixo_e_nunca_aparece_como_fisica()
    {
        var (texto, _) = Textos.SituacaoDaCatraca(new Equipamento { Inner = 1, Simulacao = true, EmOperacao = true, Estado = "Polling" });

        Assert.StartsWith("Simulada · ", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Credencial_recusada_mostra_internet_disponivel_e_nuvem_com_problema()
    {
        var (texto, sinal) = Textos.Nuvem(SituacaoDaNuvem.FalhaDeAutenticacao, null, Agora);

        Assert.Contains("recusou a credencial", texto, StringComparison.Ordinal);
        Assert.Equal(Sinal.Problema, sinal);
        Assert.Equal("Internet: disponível", Textos.Internet(SituacaoDaInternet.Disponivel));
    }

    [Fact]
    public void Segredo_ausente_e_diferente_de_nao_configurada()
    {
        Assert.NotEqual(Textos.NuvemCurta(SituacaoDaNuvem.SegredoAusente), Textos.NuvemCurta(SituacaoDaNuvem.NaoConfigurada));
        Assert.Contains("segredo ausente", Textos.Nuvem(SituacaoDaNuvem.SegredoAusente, null, Agora).Texto, StringComparison.Ordinal);
    }
}
