using Contracts.Edge.V1;
using Desktop.ViewModels;
using Desktop.ViewModels.GemeoDigital;
using TipoDeSinal = Desktop.ViewModels.GemeoDigital.TipoDeSinal;

namespace Unit.Tests.Gemeo;

/// <summary>Display, especificação, cenários, catálogo e tradução do fluxo ao vivo.</summary>
public sealed class GemeoDigitalTests
{
    private static TimeSpan S(double segundos) => TimeSpan.FromSeconds(segundos);

    // --- Display 2 x 16 ---

    [Fact]
    public void Display_quebra_aos_16_caracteres_sem_olhar_palavra()
    {
        Assert.Equal(("Aproxime o ingre", "sso             "), Display2x16.Formatar("Aproxime o ingresso"));
    }

    [Fact]
    public void Display_corta_depois_de_32_e_troca_quebra_de_linha_por_espaco()
    {
        var (l1, l2) = Display2x16.Formatar("12345678901234567890123456789012XYZ");
        Assert.Equal(("1234567890123456", "7890123456789012"), (l1, l2));

        Assert.Equal("Portao B        ", Display2x16.Formatar("Portao\nB").Linha1);
        Assert.Equal((new string(' ', 16), new string(' ', 16)), Display2x16.Formatar(null));
    }

    [Fact]
    public void Display_avisa_palavra_cortada_e_diz_quantos_espacos_por()
    {
        var aviso = Assert.Single(Display2x16.Avisos("Aproxime o ingresso"));
        Assert.Contains("\"ingresso\"", aviso, StringComparison.Ordinal);
        Assert.Contains("5 espaço(s)", aviso, StringComparison.Ordinal);

        // Com os espaços sugeridos, a palavra começa na linha de baixo e o aviso some.
        Assert.Empty(Display2x16.Avisos("Aproxime o      ingresso"));
        Assert.Equal("ingresso        ", Display2x16.Formatar("Aproxime o      ingresso").Linha2);
    }

    [Fact]
    public void Display_avisa_acento_e_texto_longo()
    {
        var avisos = Display2x16.Avisos("Bem-vindo à festa de São João do bairro");
        Assert.Contains(avisos, a => a.Contains("acento", StringComparison.Ordinal));
        Assert.Contains(avisos, a => a.Contains("só os 32", StringComparison.Ordinal));
    }

    // --- Especificação ---

    [Fact]
    public void Especificacao_embutida_e_valida_e_diz_que_as_medidas_sao_a_confirmar()
    {
        var e = EspecificacaoDaFit4.Padrao;

        Assert.Empty(e.Validar());
        Assert.Equal("Topdata", e.Equipamento.Fabricante);
        Assert.Equal(3, e.Rotor.Bracos);
        Assert.Equal((2, 16), (e.Display.Linhas, e.Display.Colunas));
        Assert.True(e.MedidasAConfirmar);
    }

    [Fact]
    public void Especificacao_recusa_display_que_nao_cabe_a_mensagem_da_catraca()
    {
        var ruim = EspecificacaoDaFit4.Padrao with { Display = new DisplayDaFit4 { Linhas = 4, Colunas = 20 } };
        Assert.Contains(ruim.Validar(), p => p.Contains("32", StringComparison.Ordinal));
    }

    [Fact]
    public void Especificacao_ilegivel_vira_erro_com_motivo()
    {
        var erro = Assert.Throws<FormatException>(() => EspecificacaoDaFit4.Ler("{ \"versao\": 1 "));
        Assert.Contains("ilegível", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Especificacao_exige_cor_valida_para_todo_acabamento()
    {
        var e = EspecificacaoDaFit4.Padrao;
        var semInox = e with
        {
            Aparencia = e.Aparencia with { Acabamentos = e.Aparencia.Acabamentos.Where(kv => kv.Key != "AcoInox").ToDictionary() },
        };

        Assert.Contains(semInox.Validar(), p => p.Contains("AcoInox", StringComparison.Ordinal));
        Assert.Equal(new CorRgba(0x90, 0, 0, 0), CorRgba.Ler("#90000000"));
        Assert.Equal(new CorRgba(0xFF, 0x3A, 0x3E, 0x46), CorRgba.Ler("#3A3E46"));
        Assert.Throws<FormatException>(() => CorRgba.Ler("azul"));
    }

    // --- Catálogo ---

    [Fact]
    public void O_que_o_sistema_nao_faz_aparece_como_aguardando_confirmacao()
    {
        var urna = CatalogoDaFit4.De(PecaDaCatraca.Urna);
        Assert.Equal(
            SituacaoDaFuncao.AguardandoConfirmacao,
            urna.Funcoes.Single(f => f.Nome.StartsWith("Recolher", StringComparison.Ordinal)).Situacao);

        var rotor = CatalogoDaFit4.De(PecaDaCatraca.Rotor);
        Assert.Equal(
            SituacaoDaFuncao.AguardandoConfirmacao,
            rotor.Funcoes.Single(f => f.Nome.Contains("dois sentidos", StringComparison.Ordinal)).Situacao);

        Assert.All(CatalogoDaFit4.De(PecaDaCatraca.LeitorFacial).Funcoes, f => Assert.Equal(SituacaoDaFuncao.ForaDoEscopo, f.Situacao));
    }

    /// <summary>
    /// P16 (docs/34 §7.1): as luzes verde e vermelha comandadas pelo sistema só existem na
    /// Linha 3; na TopFit 4 (Linha 4) o aviso é o display. Enquanto a matriz disser isso, os
    /// sinais do gêmeo não são "Disponível", a ficha diz que o desenho só marca o momento, e
    /// nenhum cenário conta que a catraca acende a luz.
    /// </summary>
    [Fact]
    public void Sinais_luminosos_nao_sao_disponiveis_sem_confirmacao_da_linha_4()
    {
        var matriz = File.ReadAllText(Path.Combine(Raiz(), "docs", "02-matriz-compatibilidade.md"));
        Assert.Contains("LEDs verde e vermelho só existem na Linha 3", matriz, StringComparison.Ordinal);

        foreach (var peca in new[] { PecaDaCatraca.SinalLiberado, PecaDaCatraca.SinalBloqueado })
        {
            var ficha = CatalogoDaFit4.De(peca);
            Assert.NotEmpty(ficha.Funcoes);
            Assert.All(ficha.Funcoes, f =>
            {
                Assert.Equal(SituacaoDaFuncao.AguardandoConfirmacao, f.Situacao);
                Assert.Equal("Aguardando confirmação", f.Selo);
                Assert.Contains("display", f.Explicacao, StringComparison.Ordinal);
                Assert.Contains("Linha 3", f.Explicacao, StringComparison.Ordinal);
                Assert.Contains("marca o momento", f.Explicacao, StringComparison.Ordinal);
            });
        }

        foreach (var passo in Roteiros.Todos.SelectMany(r => r.Passos))
        {
            // O display é o aviso principal: nenhum passo põe o sinal em destaque como se ele
            // fosse a resposta da catraca, e a cor só aparece dita como marca do desenho.
            Assert.NotEqual(PecaDaCatraca.SinalLiberado, passo.Destaque);
            Assert.NotEqual(PecaDaCatraca.SinalBloqueado, passo.Destaque);
            if (passo.Narracao.Contains("verde", StringComparison.OrdinalIgnoreCase)
                || passo.Narracao.Contains("vermelh", StringComparison.OrdinalIgnoreCase)
                || passo.Narracao.Contains("sinal", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("no desenho", passo.Narracao, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Toda_peca_tem_ficha()
    {
        foreach (var peca in Enum.GetValues<PecaDaCatraca>())
        {
            Assert.Equal(peca, CatalogoDaFit4.De(peca).Peca);
        }

        Assert.Equal(Enum.GetValues<PecaDaCatraca>().Length, CatalogoDaFit4.Pecas.Count);
    }

    // --- Configuração por peça (docs/33 §9) ---

    /// <summary>
    /// O gêmeo é a porta principal da configuração: todo campo que a catraca pode sobrepor ao
    /// evento aparece no painel de alguma peça, e toda origem do mapa de giro também.
    /// </summary>
    [Fact]
    public void Todo_campo_da_catraca_e_toda_origem_do_giro_tem_uma_peca()
    {
        foreach (var campo in Enum.GetValues<CampoDaCatraca>().Where(c => c is not CampoDaCatraca.NaoEspecificado))
        {
            Assert.NotEmpty(ConfiguracaoPorPeca.PecasDoCampo(campo));
        }

        foreach (var origem in Enum.GetValues<OrigemDoGiro>().Where(o => o is not OrigemDoGiro.NaoEspecificado))
        {
            Assert.Contains(PecaDaCatraca.Rotor, ConfiguracaoPorPeca.PecasDaOrigem(origem));
        }

        Assert.Equal([PecaDaCatraca.Rotor, PecaDaCatraca.Urna], ConfiguracaoPorPeca.PecasDaOrigem(OrigemDoGiro.Leitor2).Order());
        Assert.Equal([CampoDaCatraca.OperacaoDoLeitor2], ConfiguracaoPorPeca.De(PecaDaCatraca.Urna).Campos);
        Assert.Equal(OrigemDoGiro.Leitor2, ConfiguracaoPorPeca.De(PecaDaCatraca.Urna).OrigemDoGiro);
        Assert.True(ConfiguracaoPorPeca.De(PecaDaCatraca.Coluna).MostraRele2);
        Assert.Contains(ComandoDaPeca.MensagemTemporaria, ConfiguracaoPorPeca.De(PecaDaCatraca.Display).Comandos);
    }

    /// <summary>Peça sem parâmetro tem painel vazio: só a ficha, e "nada a configurar".</summary>
    [Fact]
    public void Peca_sem_parametro_nao_tem_nada_a_configurar()
    {
        foreach (var peca in new[] { PecaDaCatraca.Base, PecaDaCatraca.Tampa, PecaDaCatraca.Teclado, PecaDaCatraca.SinalLiberado, PecaDaCatraca.SinalBloqueado, PecaDaCatraca.LeitorFacial })
        {
            Assert.True(ConfiguracaoPorPeca.De(peca).NadaAConfigurar, peca.ToString());
        }

        foreach (var peca in new[] { PecaDaCatraca.Rotor, PecaDaCatraca.Display, PecaDaCatraca.LeitorQr, PecaDaCatraca.Urna, PecaDaCatraca.Coluna })
        {
            Assert.False(ConfiguracaoPorPeca.De(peca).NadaAConfigurar, peca.ToString());
        }
    }

    /// <summary>
    /// As duas marcações de uma peça ficam perto dela, uma ao lado da outra, sem se cobrir, e têm
    /// formas diferentes (bola e cubo), para não depender só da cor.
    /// </summary>
    [Fact]
    public void Marcacoes_da_peca_ficam_perto_dela_lado_a_lado_e_com_formas_diferentes()
    {
        var modelo = GeometriaFit4.Montar(EspecificacaoDaFit4.Padrao);

        foreach (var peca in new[] { PecaDaCatraca.Rotor, PecaDaCatraca.Display, PecaDaCatraca.LeitorQr, PecaDaCatraca.Urna, PecaDaCatraca.Coluna })
        {
            var naoSalva = GeometriaFit4.MarcaDaPeca(modelo, peca, TipoDeMarca.AlteracaoNaoSalva);
            var diferente = GeometriaFit4.MarcaDaPeca(modelo, peca, TipoDeMarca.DiferenteDoEvento);

            Assert.True(naoSalva.Triangulos > 12, $"{peca}: a bola tem mais faces que o cubo");
            Assert.Equal(12, diferente.Triangulos);

            var (a0, a1) = naoSalva.Limites();
            var (b0, b1) = diferente.Limites();
            Assert.True(a1.X <= b0.X || b1.X <= a0.X, $"{peca}: as marcações se cobrem");

            var distancia = (naoSalva.Centro - modelo.CentroDe(peca)).Comprimento;
            Assert.True(distancia < Math.Max(500, modelo.TamanhoDe(peca)), $"{peca}: a marcação ficou longe da peça ({distancia:F0} mm)");
        }
    }

    // --- Cenários ---

    [Fact]
    public void Todo_cenario_tem_passos_em_ordem_e_termina_com_a_catraca_livre()
    {
        foreach (var roteiro in Roteiros.Todos)
        {
            Assert.NotEmpty(roteiro.Passos);
            Assert.Equal(roteiro.Passos.OrderBy(p => p.Em), roteiro.Passos);

            var cena = new CenaDaCatraca("Aproxime o ingresso");
            var execucao = new ExecucaoDeRoteiro(roteiro, TimeSpan.Zero);

            for (var t = TimeSpan.Zero; t <= roteiro.Duracao + S(4); t += TimeSpan.FromMilliseconds(100))
            {
                foreach (var passo in execucao.Avancar(t))
                {
                    if (passo.Sinal is { } sinal)
                    {
                        Assert.True(cena.Aplicar(sinal, t), $"{roteiro.Nome}: \"{passo.Narracao}\" não coube em {cena.Estado}");
                    }
                }

                cena.Quadro(t);
            }

            Assert.True(execucao.Terminou);
            Assert.Equal(EstadoDaCena.Livre, cena.Estado);
        }
    }

    [Fact]
    public void Cenarios_contam_o_que_devem_contar()
    {
        Assert.Equal((1, 1, 0), Rodar(Roteiros.QrValido));
        Assert.Equal((0, 0, 1), Rodar(Roteiros.CodigoDesconhecido));
        Assert.Equal((0, 1, 0), Rodar(Roteiros.LiberadoSemGiro));
        Assert.Equal((1, 1, 0), Rodar(Roteiros.CartaoNaUrna));

        static (int Giros, int Liberacoes, int Negacoes) Rodar(RoteiroDeDemonstracao roteiro)
        {
            var cena = new CenaDaCatraca("x");
            var execucao = new ExecucaoDeRoteiro(roteiro, TimeSpan.Zero);
            foreach (var passo in execucao.Avancar(TimeSpan.FromHours(1)))
            {
                cena.Avancar(passo.Em);
                if (passo.Sinal is { } s)
                {
                    cena.Aplicar(s, passo.Em);
                }
            }

            cena.Avancar(TimeSpan.FromHours(1));
            return (cena.Giros, cena.Liberacoes, cena.Negacoes);
        }
    }

    [Fact]
    public void Codigos_de_teste_existem_no_arquivo_do_modo_simulacao()
    {
        var arquivo = File.ReadAllText(Path.Combine(Raiz(), "installer", "simulacao.exemplo.json"));

        foreach (var roteiro in Roteiros.Todos.Where(r => r.CodigoDeTeste is not null && r.CodigoDeTeste != "9999999999"))
        {
            Assert.Contains($"\"{roteiro.CodigoDeTeste}\"", arquivo, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"9999999999\"", arquivo, StringComparison.Ordinal);
    }

    // --- Fluxo ao vivo ---

    [Fact]
    public void Evento_liberado_com_giro_vira_leitura_liberacao_e_giro()
    {
        var sinais = TraducaoAoVivo.Sinais(new EventoDeAcesso
        {
            Inner = 1,
            Resultado = ResultadoDoAcesso.Permitido,
            PassagemConfirmada = true,
            OrigemBruta = 21,
        });

        Assert.Equal(
            new[] { TipoDeSinal.CredencialApresentada, TipoDeSinal.AcessoLiberado, TipoDeSinal.GiroConfirmado },
            sinais.Select(s => s.Tipo));
        Assert.Equal(LeitorDaCena.Qr, sinais[0].Leitor);
    }

    [Fact]
    public void Evento_sem_origem_nao_inventa_o_leitor()
    {
        var sinais = TraducaoAoVivo.Sinais(new EventoDeAcesso { Resultado = ResultadoDoAcesso.Negado });

        Assert.Equal(LeitorDaCena.Nenhum, sinais[0].Leitor);
        Assert.Equal(TipoDeSinal.AcessoNegado, sinais[^1].Tipo);

        var foraDaUrna = TraducaoAoVivo.Sinais(new EventoDeAcesso { Resultado = ResultadoDoAcesso.Negado, Motivo = "ForaDaUrna" });
        Assert.Equal(LeitorDaCena.CartaoNaFrente, foraDaUrna[0].Leitor);
    }

    [Fact]
    public void Evento_em_revisao_mostra_a_leitura_sem_desfecho()
    {
        var sinais = TraducaoAoVivo.Sinais(new EventoDeAcesso { Resultado = ResultadoDoAcesso.EmRevisao });
        Assert.Equal(new[] { TipoDeSinal.CredencialApresentada }, sinais.Select(s => s.Tipo));
    }

    private static string Raiz()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "ConexaoTopdata.slnx")))
        {
            pasta = pasta.Parent;
        }

        Assert.NotNull(pasta);
        return pasta.FullName;
    }
}
