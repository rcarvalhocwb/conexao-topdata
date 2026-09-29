using Desktop.ViewModels.GemeoDigital;

namespace Unit.Tests.Gemeo;

/// <summary>
/// A geometria da TopFit 4 desenhada por código. A tela WPF só copia estes números, então
/// é aqui que se garante que o desenho faz sentido: faces para fora, braço na horizontal,
/// giro no sentido certo.
/// </summary>
public sealed class GeometriaFit4Tests
{
    private static readonly ModeloDaFit4 Modelo = GeometriaFit4.Montar(EspecificacaoDaFit4.Padrao);

    // Rotação de Rodrigues, pela mão direita.
    private static Ponto3 Girar(Ponto3 v, Ponto3 eixo, double graus)
    {
        var t = graus * Math.PI / 180;
        return (v * Math.Cos(t)) + (Ponto3.Vetorial(eixo, v) * Math.Sin(t)) + (eixo * (Ponto3.Escalar(eixo, v) * (1 - Math.Cos(t))));
    }

    private static Ponto3 DirecaoDoBraco(string nome)
    {
        // O ponto mais distante do cubo é a ponta da bola na ponta do braço.
        var braco = Modelo.Partes.Single(p => p.Nome == nome).Malha;
        var ponta = braco.Posicoes.MaxBy(p => (p - Modelo.CentroDoRotor).Comprimento);
        return (ponta - Modelo.CentroDoRotor).Normalizado();
    }

    [Fact]
    public void Toda_peca_tem_desenho()
    {
        foreach (var peca in Enum.GetValues<PecaDaCatraca>())
        {
            Assert.Contains(Modelo.Partes, p => p.Peca == peca);
        }
    }

    [Fact]
    public void Nomes_dos_objetos_sao_unicos_para_poder_trocar_por_um_modelo_do_blender()
    {
        var nomes = Modelo.Partes.Select(p => p.Nome).ToList();
        Assert.Equal(nomes.Count, nomes.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Braco01", nomes);
        Assert.Contains("DisplayTela", nomes);
    }

    [Fact]
    public void Malhas_sao_consistentes()
    {
        foreach (var parte in Modelo.Partes)
        {
            var m = parte.Malha;
            Assert.True(m.Triangulos > 0, parte.Nome);
            Assert.Equal(0, m.Indices.Count % 3);
            Assert.Equal(m.Posicoes.Count, m.Normais.Count);
            Assert.Equal(m.Posicoes.Count, m.Uvs.Count);
            Assert.All(m.Indices, i => Assert.InRange(i, 0, m.Posicoes.Count - 1));
        }
    }

    [Fact]
    public void Faces_da_coluna_e_da_tampa_estao_viradas_para_fora()
    {
        foreach (var nome in new[] { "Coluna", "Tampa", "Base" })
        {
            var m = Modelo.Partes.Single(p => p.Nome == nome).Malha;
            var centro = m.Centro;

            for (var i = 0; i < m.Indices.Count; i += 3)
            {
                var a = m.Posicoes[m.Indices[i]];
                var b = m.Posicoes[m.Indices[i + 1]];
                var c = m.Posicoes[m.Indices[i + 2]];
                var normal = Ponto3.Vetorial(b - a, c - a);
                if (normal.Comprimento < 1e-9)
                {
                    continue;
                }

                var meio = (a + b + c) / 3;
                Assert.True(Ponto3.Escalar(normal, meio - centro) > 0, $"{nome}: triângulo {i / 3} virado para dentro");
            }
        }
    }

    [Fact]
    public void Um_braco_fica_na_horizontal_fechando_a_passagem()
    {
        var braco = DirecaoDoBraco("Braco01");

        Assert.Equal(1, braco.X, 3);
        Assert.Equal(0, braco.Y, 3);
        Assert.Equal(0, braco.Z, 3);
    }

    [Fact]
    public void Os_outros_dois_bracos_ficam_para_baixo_um_para_cada_lado()
    {
        var b2 = DirecaoDoBraco("Braco02");
        var b3 = DirecaoDoBraco("Braco03");

        Assert.True(b2.Y < -0.5 && b3.Y < -0.5);
        Assert.True(b2.Z * b3.Z < 0, "um braço para a frente e outro para trás");
    }

    [Fact]
    public void Um_terco_de_volta_leva_cada_braco_ao_lugar_do_proximo()
    {
        var eixo = Modelo.EixoDoRotor;
        var bracos = new[] { DirecaoDoBraco("Braco01"), DirecaoDoBraco("Braco02"), DirecaoDoBraco("Braco03") };

        foreach (var braco in bracos)
        {
            var girado = Girar(braco, eixo, CenaDaCatraca.PassoDoRotor);
            // A bola da ponta é facetada: a direção medida tem erro de uns 0,01.
            Assert.Contains(bracos, outro => (outro - girado).Comprimento < 0.03);
        }
    }

    [Fact]
    public void Na_entrada_o_braco_de_cima_vai_para_tras_como_quem_empurra_vindo_da_frente()
    {
        var braco = DirecaoDoBraco("Braco01");
        var sentido = CenaDaCatraca.Sinal(SentidoDoGiro.Entrada);

        var poucoDepois = Girar(braco, Modelo.EixoDoRotor, sentido * 10);

        Assert.True(poucoDepois.Z < 0, "a entrada empurra o braço de +Z (frente) para -Z");
    }

    [Fact]
    public void Texto_do_display_fica_de_pe_para_quem_esta_na_frente()
    {
        var tela = Modelo.Partes.Single(p => p.Nome == "DisplayTela").Malha;
        var baixoEsquerda = tela.Posicoes[Enumerable.Range(0, tela.Uvs.Count).Single(i => tela.Uvs[i] == new Uv(0, 1))];
        var altoDireita = tela.Posicoes[Enumerable.Range(0, tela.Uvs.Count).Single(i => tela.Uvs[i] == new Uv(1, 0))];

        // Quem está na frente (+Z) olhando a catraca vê +X à direita e o alto da rampa em cima.
        Assert.True(altoDireita.X > baixoEsquerda.X);
        Assert.True(altoDireita.Y > baixoEsquerda.Y);
        Assert.True(tela.Normais[0].Z > 0 && tela.Normais[0].Y > 0, "a tela olha para a frente e para cima");
    }

    [Fact]
    public void Proporcoes_seguem_a_especificacao()
    {
        var m = EspecificacaoDaFit4.Padrao.MedidasMm;
        var (min, max) = Modelo.Limites();

        Assert.Equal(0, min.Y, 3);
        Assert.Equal(m.AlturaDaTampa, max.Y, 3);
        Assert.InRange(max.X - Modelo.CentroDoRotor.X, m.ComprimentoDoBraco - 1, m.ComprimentoDoBraco + m.DiametroDoBraco);
        Assert.Equal(m.AlturaDoEixo, Modelo.CentroDoRotor.Y, 0);
    }

    [Fact]
    public void Leitor_facial_chega_a_altura_da_variante_facial()
    {
        var m = EspecificacaoDaFit4.Padrao.MedidasMm;
        var facial = Modelo.Partes.Where(p => p.Peca == PecaDaCatraca.LeitorFacial).Select(p => p.Malha.Limites().Maximo.Y).Max();

        Assert.InRange(facial, m.AlturaComLeitorFacial - 20, m.AlturaComLeitorFacial + 5);
    }

    [Fact]
    public void Cada_adereco_da_variante_existe_e_a_pessoa_atravessa_para_tras()
    {
        Assert.Contains(Modelo.Aderecos, a => a.Tipo == TipoDeAdereco.CelularNoQr);
        Assert.Contains(Modelo.Aderecos, a => a.Tipo == TipoDeAdereco.CartaoNaFrente);
        Assert.Contains(Modelo.Aderecos, a => a.Tipo == TipoDeAdereco.CartaoNaUrna);

        var pessoa = Modelo.Aderecos.Single(a => a.Tipo == TipoDeAdereco.Pessoa);
        Assert.True(pessoa.Aproximacao.Z > 0 && pessoa.Travessia.Z < 0);
    }

    [Fact]
    public void Exporta_obj_com_um_objeto_por_parte()
    {
        var obj = Modelo.ParaObj();
        var objetos = obj.Split('\n').Count(l => l.StartsWith("o ", StringComparison.Ordinal));

        Assert.Equal(Modelo.Partes.Count, objetos);
        Assert.Contains("o Rotor.Braco01", obj, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_urna_a_variante_nao_desenha_a_urna()
    {
        var semUrna = EspecificacaoDaFit4.Padrao with
        {
            Pecas = EspecificacaoDaFit4.Padrao.Pecas with { Urna = false },
        };

        var modelo = GeometriaFit4.Montar(semUrna);

        Assert.DoesNotContain(modelo.Partes, p => p.Peca == PecaDaCatraca.Urna);
        Assert.DoesNotContain(modelo.Aderecos, a => a.Tipo == TipoDeAdereco.CartaoNaUrna);
    }
}
