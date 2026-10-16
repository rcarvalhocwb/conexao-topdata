using Access.Application.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// Etapa A.4 do docs/35: camada da catraca recusada cai no padrão dela na subida, e faz o
/// "Aplicar agora" falhar; problema de leitura na subida é só aviso, no aplicar é falha.
/// </summary>
/// <remarks>
/// A camada inválida guarda um valor fora da faixa (leitor 1 = 9), como uma linha gravada por
/// uma versão antiga ou à mão. Até 15/10/2026 o caso era "sem leitor 2" contra um relé 2
/// configurado; desde o docs/61 o montador desliga o relé 2 junto com o leitor da fenda, e essa
/// combinação não quebra mais. Dados sintéticos.
/// </remarks>
public sealed class ConfiguracaoComRecuoTests
{
    private static DeviceConfiguration ComReleDois => PadroesDeFabrica.TopFit4;

    private static readonly SobreposicoesDaCatraca SemLeitor2 = new()
    {
        OperacaoDoLeitor1 = 9,
        MensagemPadrao = "Portao sintetico 2",
    };

    private static readonly SobreposicoesDaCatraca Propria = new()
    {
        TempoDoAcionamento1 = 7,
        MensagemPadrao = "Portao sintetico 2",
    };

    [Fact]
    public void Na_subida_a_camada_valida_vale_sem_aviso()
    {
        var fabrica = PadroesDeFabrica.TopFit4;

        var (configuracao, avisos) = ConfiguracaoComRecuo.NaSubida(fabrica, SobreposicoesDoEvento.Nenhuma, 2, Propria, []);

        Assert.Empty(avisos);
        Assert.Equal(MontadorDaConfiguracao.Montar(fabrica, SobreposicoesDoEvento.Nenhuma, Propria), configuracao);
        Assert.Equal("Portao sintetico 2", configuracao.MensagemPadrao);
    }

    [Fact]
    public void Na_subida_a_camada_recusada_cai_no_padrao_da_catraca_com_aviso()
    {
        var evento = new SobreposicoesDoEvento { MensagemPadrao = "Evento sintetico" };
        var fabrica = ComReleDois;

        var (configuracao, avisos) = ConfiguracaoComRecuo.NaSubida(fabrica, evento, 2, SemLeitor2, []);

        Assert.Empty(configuracao.Validar());
        Assert.Equal(MontadorDaConfiguracao.Montar(fabrica, evento, SobreposicoesDaCatraca.Nenhuma), configuracao);
        Assert.Equal("Evento sintetico", configuracao.MensagemPadrao);
        var aviso = Assert.Single(avisos);
        Assert.StartsWith("Catraca 2: configuração própria recusada, subindo com o padrão dela", aviso, StringComparison.Ordinal);
        Assert.Contains("OperacaoDoLeitor1 deve estar entre 0 e 4", aviso, StringComparison.Ordinal);
    }

    [Fact]
    public void Na_subida_o_problema_de_leitura_e_so_aviso_e_o_resto_da_camada_vale()
    {
        var ilegivel = "Catraca 2: reader_type ilegível (42); herda do evento.";

        var (configuracao, avisos) = ConfiguracaoComRecuo.NaSubida(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, 2, Propria, [ilegivel]);

        Assert.Equal([ilegivel], avisos);
        Assert.Equal("Portao sintetico 2", configuracao.MensagemPadrao);
        Assert.Equal((byte)7, configuracao.TempoDoAcionamento1);
    }

    [Fact]
    public void Sem_camada_e_identico_ao_montador_com_a_camada_vazia()
    {
        var evento = new SobreposicoesDoEvento { TipoDeLeitor = 5, LeitorDaUrna = false, MensagemPadrao = "Evento sintetico" };

        var fabrica = PadroesDeFabrica.TopFit4;

        var (subida, avisos) = ConfiguracaoComRecuo.NaSubida(fabrica, evento, 1, SobreposicoesDaCatraca.Nenhuma, []);
        var (aplicar, problemas) = ConfiguracaoComRecuo.ParaAplicar(fabrica, evento, 1, SobreposicoesDaCatraca.Nenhuma, []);

        var hoje = MontadorDaConfiguracao.Montar(fabrica, evento, SobreposicoesDaCatraca.Nenhuma);
        Assert.Empty(avisos);
        Assert.Empty(problemas);
        Assert.Equal(hoje, subida);
        Assert.Equal(hoje, aplicar);
    }

    [Fact]
    public void Aplicar_com_a_camada_recusada_nao_devolve_configuracao()
    {
        var (configuracao, problemas) = ConfiguracaoComRecuo.ParaAplicar(
            ComReleDois, SobreposicoesDoEvento.Nenhuma, 2, SemLeitor2, []);

        Assert.Null(configuracao);
        var problema = Assert.Single(problemas);
        Assert.StartsWith("Catraca 2: OperacaoDoLeitor1 deve estar entre 0 e 4", problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Aplicar_com_problema_de_leitura_falha_mesmo_com_o_resultado_valido()
    {
        var ilegivel = "Catraca 2: updated_at ilegível.";

        var (configuracao, problemas) = ConfiguracaoComRecuo.ParaAplicar(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, 2, Propria, [ilegivel]);

        Assert.Null(configuracao);
        Assert.Equal([ilegivel], problemas);
    }
}
