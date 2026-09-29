using Contracts.Edge.V1;
using Desktop.ViewModels;
using Desktop.ViewModels.GemeoDigital;

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

    [Fact]
    public void Toda_peca_tem_ficha()
    {
        foreach (var peca in Enum.GetValues<PecaDaCatraca>())
        {
            Assert.Equal(peca, CatalogoDaFit4.De(peca).Peca);
        }

        Assert.Equal(Enum.GetValues<PecaDaCatraca>().Length, CatalogoDaFit4.Pecas.Count);
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
