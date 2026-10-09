using Access.Application.Devices;

namespace Unit.Tests;

/// <summary>
/// Comportamento da trava de capacidade.
/// </summary>
/// <remarks>
/// O caso que motivou isto: montar a lista de um evento e só descobrir o estouro no envio,
/// que sobrescreve tudo e trava a catraca enquanto roda. No meio do pico, uma pista parada.
/// </remarks>
public sealed class LimitesDeCapacidadeTests
{
    [Fact]
    public void O_evento_de_doze_mil_cabe_na_producao_atual()
    {
        var avaliacao = LimitesDeCapacidade.AvaliarListaDeAcesso(
            12_000, PlacaDoEquipamento.ControleCatracaOuInnerAcesso, digitos: 10);

        Assert.True(avaliacao.Cabe);
        Assert.Equal(0, avaliacao.Excedente);
        Assert.Contains("Folga de 3000", avaliacao.Mensagem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Recusar não basta: a mensagem tem de dizer qual é a saída e de quem é a decisão.
    /// </summary>
    [Fact]
    public void Trinta_mil_nao_cabe_e_a_mensagem_aponta_a_saida()
    {
        var avaliacao = LimitesDeCapacidade.AvaliarListaDeAcesso(
            30_000, PlacaDoEquipamento.ControleCatracaOuInnerAcesso, digitos: 10);

        Assert.False(avaliacao.Cabe);
        Assert.Equal(15_000, avaliacao.Excedente);

        // A alternativa e o custo dela precisam estar na cara de quem lê.
        Assert.Contains("lista negra", avaliacao.Mensagem, StringComparison.Ordinal);
        Assert.Contains("deixar passar desconhecidos", avaliacao.Mensagem, StringComparison.Ordinal);
        Assert.Contains("B4", avaliacao.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Cartao_de_dezesseis_digitos_reduz_a_capacidade()
    {
        var comum = LimitesDeCapacidade.MaximoDeUsuarios(
            PlacaDoEquipamento.ControleCatracaOuInnerAcesso, 10);
        var longo = LimitesDeCapacidade.MaximoDeUsuarios(
            PlacaDoEquipamento.ControleCatracaOuInnerAcesso, 16);

        Assert.True(longo < comum, "16 dígitos ocupam mais espaço por registro.");
        Assert.Equal(14_900, longo);
    }

    [Fact]
    public void Na_placa_descontinuada_a_capacidade_cai_com_o_tamanho_do_cartao()
    {
        var curto = LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.InnerPlusOuInnerNet, 4);
        var longo = LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.InnerPlusOuInnerNet, 16);

        Assert.Equal(15_000, curto);
        Assert.Equal(5_000, longo);
    }

    /// <summary>
    /// Tamanho sem linha própria usa a faixa documentada imediatamente acima.
    /// </summary>
    /// <remarks>
    /// Arredondar para a faixa de baixo inventaria capacidade que a Topdata não publicou —
    /// e o preço de inventar aqui é lista truncada em silêncio no equipamento.
    /// </remarks>
    [Fact]
    public void Tamanho_intermediario_usa_a_faixa_documentada_acima()
    {
        // 5 dígitos não tem linha; a faixa de 6 é a próxima que cobre.
        Assert.Equal(
            LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.InnerPlusOuInnerNet, 6),
            LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.InnerPlusOuInnerNet, 5));
    }

    /// <summary>A placa antiga não publica valor para 16 dígitos.</summary>
    [Fact]
    public void Sem_valor_publicado_a_capacidade_e_zero_e_nada_cabe()
    {
        Assert.Equal(0, LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.InnerAntiga, 16));

        var avaliacao = LimitesDeCapacidade.AvaliarListaDeAcesso(
            1, PlacaDoEquipamento.InnerAntiga, digitos: 16);

        Assert.False(avaliacao.Cabe);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Tamanho_de_cartao_fora_da_faixa_e_recusado(int digitos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.ControleCatracaOuInnerAcesso, digitos));
    }

    [Fact]
    public void A_memoria_de_marcacoes_do_evento_de_doze_mil_cabe_no_pior_caso()
    {
        // 12.000 pessoas divididas em 4 pistas, 3 marcações por passagem.
        var avaliacao = LimitesDeCapacidade.AvaliarMarcacoes(3_000, marcacoesPorPassagem: 3);

        Assert.True(avaliacao.Cabe);
        Assert.Equal(9_000, avaliacao.Planejado);
    }

    [Fact]
    public void A_memoria_estoura_e_a_mensagem_diz_o_que_fazer()
    {
        var avaliacao = LimitesDeCapacidade.AvaliarMarcacoes(15_000, marcacoesPorPassagem: 3);

        Assert.False(avaliacao.Cabe);
        Assert.Contains("coleta precisa rodar continuamente", avaliacao.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Marcacoes_por_passagem_abaixo_de_um_nao_faz_sentido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LimitesDeCapacidade.AvaliarMarcacoes(1_000, marcacoesPorPassagem: 0));
    }
}
