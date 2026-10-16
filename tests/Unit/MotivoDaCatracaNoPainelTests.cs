using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Unit.Tests;

/// <summary>
/// Plano de reparo, Fase 1.4 (docs/61): quando o programa das catracas não abre a porta, a barra do painel
/// diz o motivo (ex.: a Inner.dll que falta), e não só "Nenhuma catraca conectada".
/// </summary>
public sealed class MotivoDaCatracaNoPainelTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static ObterEstadoResponse SemCatraca() => new()
    {
        Nivel = NivelDeDegradacao.T0Normal,
        EquipamentosConectados = 0,
        EquipamentosCadastrados = 1,
        CatracasSimuladasAtivas = 0,
    };

    [Fact]
    public void Sem_motivo_a_barra_segue_com_a_mensagem_de_antes()
    {
        var estado = EstadoDoPainel.De(SemCatraca(), Agora);

        Assert.Equal("Nenhuma catraca conectada — verifique a rede e a alimentação", estado.Mensagem);
        Assert.Equal(SaudeDoPainel.Acao, estado.Saude);
    }

    [Fact]
    public void Com_motivo_a_barra_mostra_a_causa_e_continua_em_acao()
    {
        var resposta = SemCatraca();
        resposta.MotivoDaCatraca = "Retorno 11: falta a Inner.dll (DLL de apoio da versão 6.x).";

        var estado = EstadoDoPainel.De(resposta, Agora);

        Assert.Equal(SaudeDoPainel.Acao, estado.Saude);
        Assert.Contains("programa das catracas não está atendendo", estado.Mensagem, StringComparison.Ordinal);
        Assert.Contains("falta a Inner.dll", estado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Motivo_so_com_espacos_nao_entra_na_barra()
    {
        var resposta = SemCatraca();
        resposta.MotivoDaCatraca = "   ";

        Assert.Equal("Nenhuma catraca conectada — verifique a rede e a alimentação", EstadoDoPainel.De(resposta, Agora).Mensagem);
    }
}
