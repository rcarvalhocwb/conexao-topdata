using Access.Application.Devices;
using Access.Domain.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// Mapa de giro (decisão D9, docs/34 §9): origem → função da DLL e rótulo. Padrão = hoje.
/// </summary>
public sealed class MapaDeGiroTests
{
    public static TheoryData<OrigemDoGiro> Origens() => [.. MapaDeGiro.Origens];

    [Theory]
    [MemberData(nameof(Origens))]
    public void Mapa_vazio_e_o_comportamento_de_hoje_para_toda_origem(OrigemDoGiro origem)
    {
        foreach (var funcao in Enum.GetValues<FuncaoDeLiberacao>())
        {
            var perfil = new GatePhysicalProfile(funcao);
            var giro = perfil.Resolver(origem);

            Assert.Equal(funcao, giro.Funcao);
            Assert.Equal(SentidoContado.Entrada, giro.ContaComo);
            Assert.Equal("Entrada liberada", giro.Texto);
            Assert.False(giro.DoMapa);
            Assert.Equal(perfil.LiberacaoDaEntrada, perfil.LiberacaoPara(origem));
        }
    }

    [Fact]
    public void Urna_pela_saida_contando_entrada()
    {
        var perfil = GatePhysicalProfile.Padrao with
        {
            MapaDeGiro = MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada)),
        };

        var urna = perfil.Resolver(OrigemDoGiro.Leitor2);
        Assert.Equal(GateDirection.Saida, urna.Direcao);
        Assert.Equal(SentidoContado.Entrada, urna.ContaComo);
        Assert.Equal("Entrada liberada", urna.Texto);
        Assert.True(urna.DoMapa);

        // A frente segue o perfil.
        Assert.Equal(GateDirection.Entrada, perfil.LiberacaoPara(OrigemDoGiro.Leitor1));
        Assert.False(perfil.Resolver(OrigemDoGiro.Leitor1).DoMapa);
    }

    [Fact]
    public void Regra_sem_funcao_segue_a_funcao_do_perfil_com_o_rotulo_da_regra()
    {
        var perfil = new GatePhysicalProfile(FuncaoDeLiberacao.EntradaInvertida)
        {
            MapaDeGiro = MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor1, new RegraDeGiro(null, SentidoContado.Saida, "Volte sempre")),
        };

        var giro = perfil.Resolver(OrigemDoGiro.Leitor1);
        Assert.Equal(FuncaoDeLiberacao.EntradaInvertida, giro.Funcao);
        Assert.Equal(SentidoContado.Saida, giro.ContaComo);
        Assert.Equal("Volte sempre", giro.Texto);
    }

    [Fact]
    public void Texto_padrao_sem_acento_pelo_rotulo()
    {
        Assert.Equal("Entrada liberada", MapaDeGiro.TextoPadrao(SentidoContado.Entrada));
        Assert.Equal("Saida liberada", MapaDeGiro.TextoPadrao(SentidoContado.Saida));
    }

    [Theory]
    [InlineData(KnownEventOrigin.Leitor1, OrigemDoGiro.Leitor1)]
    [InlineData(KnownEventOrigin.QrCode, OrigemDoGiro.Leitor1)]
    [InlineData(KnownEventOrigin.Leitor2, OrigemDoGiro.Leitor2)]
    [InlineData(KnownEventOrigin.Teclado, OrigemDoGiro.Teclado)]
    public void Leitura_vira_origem_do_mapa(KnownEventOrigin origem, OrigemDoGiro esperada) =>
        Assert.Equal(esperada, MapaDeGiro.DaLeitura(EventOrigin.From(origem)));

    [Theory]
    [InlineData(6)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(11)]
    public void Sinal_da_catraca_nao_e_origem_do_mapa(int bruta) =>
        Assert.Null(MapaDeGiro.DaLeitura(EventOrigin.FromRaw(bruta)));

    [Fact]
    public void Validar_recusa_funcao_rotulo_e_texto_fora_do_lugar()
    {
        var mapa = MapaDeGiro.Vazio
            .Com(OrigemDoGiro.Leitor1, new RegraDeGiro((FuncaoDeLiberacao)9, SentidoContado.Entrada))
            .Com(OrigemDoGiro.Leitor2, new RegraDeGiro(null, (SentidoContado)5))
            .Com(OrigemDoGiro.Teclado, new RegraDeGiro(null, SentidoContado.Entrada, new string('x', 33)))
            .Com(OrigemDoGiro.LiberacaoManual, new RegraDeGiro(null, SentidoContado.Entrada, "linha\nquebrada"));

        var problemas = mapa.Validar();
        Assert.Equal(4, problemas.Count);
        Assert.Contains(problemas, p => p.Contains("D5", StringComparison.Ordinal));

        var configuracao = PadroesDeFabrica.TopFit4 with { PerfilFisico = GatePhysicalProfile.Padrao with { MapaDeGiro = mapa } };
        Assert.Equal(4, configuracao.Validar().Count(p => p.StartsWith("Mapa de giro", StringComparison.Ordinal)));
    }

    [Fact]
    public void Texto_no_limite_do_display_e_aceito() =>
        Assert.Empty(MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor1, new RegraDeGiro(null, SentidoContado.Entrada, new string('x', 32))).Validar());

    [Fact]
    public void Montar_com_mapa_vazio_e_a_montagem_de_sempre()
    {
        var sempre = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma);
        var comVazio = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma, MapaDeGiro.Vazio);

        Assert.Equal(sempre.PerfilFisico, comVazio.PerfilFisico);
        Assert.Equal(VersaoDaConfiguracao.FormaCanonica(sempre), VersaoDaConfiguracao.FormaCanonica(comVazio));
        Assert.DoesNotContain("mapa_de_giro", VersaoDaConfiguracao.FormaCanonica(comVazio), StringComparison.Ordinal);
    }

    [Fact]
    public void Mapa_entra_na_versao_e_cada_regra_muda_a_versao()
    {
        var vazio = VersaoDaConfiguracao.Calcular(PadroesDeFabrica.TopFit4);
        var versoes = new HashSet<string>(StringComparer.Ordinal) { vazio };

        MapaDeGiro[] mapas =
        [
            MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada)),
            MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Saida)),
            MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(null, SentidoContado.Entrada)),
            MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor1, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada)),
            MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada, "Ola")),
        ];

        foreach (var mapa in mapas)
        {
            var configuracao = MontadorDaConfiguracao.Montar(
                PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma, mapa);
            Assert.True(versoes.Add(VersaoDaConfiguracao.Calcular(configuracao)), mapa.ToString());
        }
    }

    [Fact]
    public void Recuo_na_subida_descarta_o_mapa_com_a_camada_recusada()
    {
        var mapa = MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro((FuncaoDeLiberacao)9, SentidoContado.Entrada));

        var (configuracao, avisos) = ConfiguracaoComRecuo.NaSubida(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, 2, SobreposicoesDaCatraca.Nenhuma, mapa, []);

        Assert.True(configuracao.PerfilFisico.MapaDeGiro.EstaVazio);
        Assert.Contains(avisos, a => a.Contains("Mapa de giro", StringComparison.Ordinal));

        var (aplicar, problemas) = ConfiguracaoComRecuo.ParaAplicar(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, 2, SobreposicoesDaCatraca.Nenhuma, mapa, []);
        Assert.Null(aplicar);
        Assert.NotEmpty(problemas);
    }
}
