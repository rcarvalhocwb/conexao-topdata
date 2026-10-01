namespace Desktop.ViewModels.GemeoDigital;

/// <summary>O material de cada pedaço do desenho. A tela decide a cor e o brilho de cada um.</summary>
public enum Acabamento
{
    /// <summary>Metal com pintura industrial (coluna, base).</summary>
    Pintura,

    /// <summary>Plástico da tampa.</summary>
    TampaPlastica,

    /// <summary>Aço inox dos braços e do cubo.</summary>
    AcoInox,

    /// <summary>Teclas de borracha.</summary>
    Tecla,

    /// <summary>Molduras claras dos leitores.</summary>
    Moldura,

    /// <summary>Vidro escuro (janela do leitor de QR, tela do facial).</summary>
    VidroEscuro,

    /// <summary>A tela do display: recebe o texto.</summary>
    TelaDoDisplay,

    /// <summary>Pictograma de liberado: acende em verde.</summary>
    LuzDeLiberado,

    /// <summary>Pictograma de bloqueado: acende em vermelho.</summary>
    LuzDeBloqueado,

    /// <summary>Preto fosco: fendas e fundos.</summary>
    Fenda,

    /// <summary>Interior da urna, visível só com as peças separadas.</summary>
    InteriorDaUrna,

    Cartao,

    Celular,

    /// <summary>A tela do celular: recebe o desenho de um QR.</summary>
    TelaDoCelular,

    /// <summary>A pessoa genérica dos cenários. Nunca uma pessoa real.</summary>
    Pessoa,
}

/// <summary>Um pedaço do desenho, de uma peça, com o material e para onde vai na vista separada.</summary>
/// <param name="Peca">A peça a que pertence: é o que a escolha pelo mouse devolve.</param>
/// <param name="Nome">Nome do objeto. É o nome esperado num modelo feito no Blender.</param>
/// <param name="Malha">A geometria, em milímetros.</param>
/// <param name="Acabamento">O material.</param>
/// <param name="Separacao">Deslocamento na vista com as peças separadas.</param>
public sealed record ParteDoModelo(PecaDaCatraca Peca, string Nome, Malha Malha, Acabamento Acabamento, Ponto3 Separacao);

/// <summary>Objetos que aparecem nos cenários, fora da catraca.</summary>
public enum TipoDeAdereco
{
    /// <summary>Celular com um QR na tela, diante do leitor de QR.</summary>
    CelularNoQr,

    /// <summary>Cartão diante do leitor da frente.</summary>
    CartaoNaFrente,

    /// <summary>Cartão entrando na fenda da urna.</summary>
    CartaoNaUrna,

    /// <summary>Pessoa genérica que atravessa.</summary>
    Pessoa,
}

/// <summary>
/// Um objeto de cena. A malha está na posição final (lendo, ou parada diante da catraca);
/// <c>Aproximacao</c> é de onde ele vem, somado a ela.
/// </summary>
/// <param name="Tipo">Qual objeto.</param>
/// <param name="Partes">Os pedaços, cada um com o seu material.</param>
/// <param name="Aproximacao">Deslocamento no começo do movimento.</param>
/// <param name="Travessia">Para a pessoa: para onde ela vai ao passar.</param>
public sealed record AderecoDoModelo(
    TipoDeAdereco Tipo,
    IReadOnlyList<(Malha Malha, Acabamento Acabamento)> Partes,
    Ponto3 Aproximacao,
    Ponto3 Travessia);

/// <summary>A seta do sentido do giro, para o painel do mapa de giro.</summary>
/// <param name="Malha">O arco com a cabeça.</param>
/// <param name="Centro">O centro do arco, sobre o eixo dos braços.</param>
/// <param name="Inicio">Onde o arco começa.</param>
/// <param name="Ponta">A ponta da cabeça.</param>
public sealed record SetaDoGiroNoModelo(Malha Malha, Ponto3 Centro, Ponto3 Inicio, Ponto3 Ponta);

/// <summary>O desenho completo da catraca, pronto para a tela copiar.</summary>
public sealed class ModeloDaFit4
{
    internal ModeloDaFit4(
        IReadOnlyList<ParteDoModelo> partes,
        IReadOnlyList<AderecoDoModelo> aderecos,
        Ponto3 centroDoRotor,
        Ponto3 eixoDoRotor,
        Malha piso,
        Malha sombra)
    {
        Partes = partes;
        Aderecos = aderecos;
        CentroDoRotor = centroDoRotor;
        EixoDoRotor = eixoDoRotor;
        Piso = piso;
        Sombra = sombra;
    }

    public IReadOnlyList<ParteDoModelo> Partes { get; }

    public IReadOnlyList<AderecoDoModelo> Aderecos { get; }

    /// <summary>Centro do cubo dos braços: o pivô do giro.</summary>
    public Ponto3 CentroDoRotor { get; }

    /// <summary>
    /// Direção do eixo (unitária). Ângulo positivo, pela mão direita, leva o braço de cima
    /// para a frente (+Z); a entrada é o sentido negativo.
    /// </summary>
    public Ponto3 EixoDoRotor { get; }

    /// <summary>Piso, em volta da catraca.</summary>
    public Malha Piso { get; }

    /// <summary>Sombra de contato embaixo da base.</summary>
    public Malha Sombra { get; }

    /// <summary>O centro de uma peça, para a câmera mirar.</summary>
    public Ponto3 CentroDe(PecaDaCatraca peca)
    {
        var (min, max) = LimitesDe(peca);
        return Ponto3.Entre(min, max, 0.5);
    }

    /// <summary>O tamanho de uma peça (a diagonal da caixa que a envolve).</summary>
    public double TamanhoDe(PecaDaCatraca peca)
    {
        var (min, max) = LimitesDe(peca);
        return (max - min).Comprimento;
    }

    /// <summary>Todas as peças de uma vez: a caixa que envolve a catraca.</summary>
    public (Ponto3 Minimo, Ponto3 Maximo) Limites() => Unir(Partes.Where(p => p.Peca is not PecaDaCatraca.LeitorFacial));

    /// <summary>A geometria inteira em OBJ, um objeto por parte.</summary>
    public string ParaObj()
    {
        var texto = new System.Text.StringBuilder();
        var primeiro = 1;

        foreach (var parte in Partes)
        {
            texto.Append(parte.Malha.ParaObj($"{parte.Peca}.{parte.Nome}", primeiro));
            primeiro += parte.Malha.Posicoes.Count;
        }

        return texto.ToString();
    }

    private (Ponto3 Minimo, Ponto3 Maximo) LimitesDe(PecaDaCatraca peca)
    {
        var partes = Partes.Where(p => p.Peca == peca).ToList();
        return partes.Count == 0 ? (Ponto3.Zero, Ponto3.Zero) : Unir(partes);
    }

    private static (Ponto3 Minimo, Ponto3 Maximo) Unir(IEnumerable<ParteDoModelo> partes)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
        double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;

        foreach (var parte in partes)
        {
            var (a, b) = parte.Malha.Limites();
            x0 = Math.Min(x0, a.X);
            y0 = Math.Min(y0, a.Y);
            z0 = Math.Min(z0, a.Z);
            x1 = Math.Max(x1, b.X);
            y1 = Math.Max(y1, b.Y);
            z1 = Math.Max(z1, b.Z);
        }

        return x0 > x1 ? (Ponto3.Zero, Ponto3.Zero) : (new Ponto3(x0, y0, z0), new Ponto3(x1, y1, z1));
    }
}

/// <summary>
/// Desenha a TopFit 4 por código, a partir da <see cref="EspecificacaoDaFit4"/>.
/// </summary>
/// <remarks>
/// <para>
/// Eixos: X da coluna para os braços, Y para cima, Z para a frente (o lado de quem chega e
/// vê o painel). Unidade: milímetro.
/// </para>
/// <para>
/// O mecanismo é um tripé: o eixo do cubo desce inclinado para fora, e os três braços saem
/// num cone em volta dele, de modo que um fica sempre na horizontal, fechando a passagem.
/// Um terço de volta abaixa esse braço e levanta o próximo.
/// </para>
/// <para>
/// É um desenho ilustrativo feito de caixas e cilindros, fiel na disposição das peças
/// (fotografias de referência em docs/gemeo-digital/), não nas medidas finas. Pode ser
/// trocado por um modelo do Blender com os mesmos nomes de objeto sem mexer na tela.
/// </para>
/// </remarks>
public static class GeometriaFit4
{
    /// <summary>
    /// A seta do mapa de giro (D9, docs/34 §9): um arco em volta do eixo dos braços, na frente do
    /// cubo, no sentido em que o braço gira com a função escolhida.
    /// </summary>
    /// <remarks>
    /// O sentido segue a mesma convenção da cena (<see cref="CenaDaCatraca.Sinal"/>): ângulo
    /// negativo em volta do eixo é a entrada. Que a função escolhida gire mesmo para esse lado
    /// nesta instalação é a conferência de comissionamento (NOVO-HIL-DIR-11); a seta mostra o
    /// previsto pelo nome da função.
    /// </remarks>
    /// <param name="modelo">O desenho montado.</param>
    /// <param name="sentido">O sentido do braço.</param>
    public static SetaDoGiroNoModelo SetaDoGiro(ModeloDaFit4 modelo, SentidoDoGiro sentido)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        var eixo = modelo.EixoDoRotor.Normalizado();
        var cima = new Ponto3(0, 1, 0);
        var u = (cima - (eixo * Ponto3.Escalar(cima, eixo))).Normalizado();
        var v = Ponto3.Vetorial(eixo, u);
        var centro = modelo.CentroDoRotor + (eixo * 70);
        const double raio = 120;
        const double meia = 7;
        const int fatias = 16;
        var sinal = CenaDaCatraca.Sinal(sentido);
        var inicio = -sinal * Math.PI / 3;
        var fim = sinal * Math.PI / 3;
        var ponta = fim + (sinal * 0.35);

        Ponto3 P(double angulo, double r) => centro + (((u * Math.Cos(angulo)) + (v * Math.Sin(angulo))) * r);

        var malha = new Malha();
        for (var i = 0; i < fatias; i++)
        {
            var a0 = inicio + ((fim - inicio) * i / fatias);
            var a1 = inicio + ((fim - inicio) * (i + 1) / fatias);
            malha.Quad(P(a0, raio - meia), P(a0, raio + meia), P(a1, raio + meia), P(a1, raio - meia), eixo);
        }

        // A cabeça: um triângulo (quadrilátero com dois cantos iguais) apontando no sentido do giro.
        malha.Quad(P(fim, raio - (meia * 2.6)), P(fim, raio + (meia * 2.6)), P(ponta, raio), P(ponta, raio), eixo);

        return new SetaDoGiroNoModelo(malha, centro, P(inicio, raio), P(ponta, raio));
    }

    /// <summary>Monta o desenho completo.</summary>
    public static ModeloDaFit4 Montar(EspecificacaoDaFit4 especificacao)
    {
        ArgumentNullException.ThrowIfNull(especificacao);

        var m = especificacao.MedidasMm;
        var partes = new List<ParteDoModelo>();

        var hx = m.LarguraDoCorpo / 2;
        var hz = m.ProfundidadeDoCorpo / 2;
        var topo = m.AlturaDaTampa;
        var yCabeca = topo - m.AlturaDaCabeca;

        var sobeATampa = new Ponto3(0, 260, 0);
        var saiORotor = new Ponto3(260, 0, 0);

        void Parte(PecaDaCatraca peca, string nome, Malha malha, Acabamento acabamento, Ponto3 separacao) =>
            partes.Add(new ParteDoModelo(peca, nome, malha, acabamento, separacao));

        // --- Base e coluna ---
        Parte(PecaDaCatraca.Base, "Base",
            new Malha().Caixa(new Ponto3(-hx - 10, 0, -hz - 55), new Ponto3(hx + 10, 10, hz + 55)),
            Acabamento.Pintura, Ponto3.Zero);

        var cx = hx * 0.72;
        var cz = hz * 0.72;
        Parte(PecaDaCatraca.Coluna, "Coluna",
            new Malha().Caixa(new Ponto3(-cx, 10, -cz), new Ponto3(cx, yCabeca, cz)),
            Acabamento.Pintura, Ponto3.Zero);

        // --- Cabeça: perfil com a frente inclinada, extrudado na largura ---
        var yFrente = yCabeca + (m.AlturaDaCabeca * 0.58);
        var zTopoFrente = -hz + (m.ProfundidadeDoCorpo * 0.12);
        (double Z, double Y)[] perfil =
        [
            (-hz, yCabeca),
            (hz - 12, yCabeca),
            (hz, yCabeca + 12),
            (hz, yFrente),
            (zTopoFrente, topo),
            (-hz, topo),
        ];
        Parte(PecaDaCatraca.Tampa, "Tampa", new Malha().PrismaDePerfil(perfil, -hx, hx), Acabamento.TampaPlastica, sobeATampa);

        // O painel inclinado: s ao longo de X (direita de quem olha), t subindo a rampa.
        var origemDoPainel = new Ponto3(0, yFrente, hz);
        var subida = new Ponto3(0, topo - yFrente, zTopoFrente - hz);
        var comprimentoDoPainel = subida.Comprimento;
        var p = new Ponto3(1, 0, 0);
        var q = subida.Normalizado();
        var n = Ponto3.Vetorial(p, q);

        Ponto3 NoPainel(double s, double t, double acima) => origemDoPainel + (p * s) + (q * t) + (n * acima);

        Malha Placa(double s, double t, double largura, double altura, double espessura, double acima = 0) =>
            new Malha().CaixaOrientada(NoPainel(s, t, acima + (espessura / 2)), p, q, n, largura / 2, altura / 2, espessura / 2);

        var sobeComATampa = sobeATampa + (n * 40);

        // --- Display: moldura e tela, junto ao alto da rampa ---
        var tDisplay = comprimentoDoPainel - 30;
        Parte(PecaDaCatraca.Display, "DisplayMoldura", Placa(-25, tDisplay, 196, 44, 3), Acabamento.Fenda, sobeComATampa);
        var meioDaTela = NoPainel(-25, tDisplay, 3.2);
        Parte(PecaDaCatraca.Display, "DisplayTela",
            new Malha().Quad(
                meioDaTela - (p * 90) - (q * 17),
                meioDaTela + (p * 90) - (q * 17),
                meioDaTela + (p * 90) + (q * 17),
                meioDaTela - (p * 90) + (q * 17),
                n),
            Acabamento.TelaDoDisplay, sobeComATampa);

        // --- Teclado 4 x 4, à esquerda de quem olha ---
        if (especificacao.Pecas.Teclado)
        {
            const double sTeclado = -75, tTeclado = 118, passo = 23;
            Parte(PecaDaCatraca.Teclado, "TecladoBase", Placa(sTeclado, tTeclado, 98, 98, 2.5), Acabamento.Fenda, sobeComATampa);

            var teclas = new Malha();
            for (var linha = 0; linha < 4; linha++)
            {
                for (var coluna = 0; coluna < 4; coluna++)
                {
                    teclas.Juntar(Placa(sTeclado + ((coluna - 1.5) * passo), tTeclado + ((1.5 - linha) * passo), 18, 18, 4, 2.5));
                }
            }

            Parte(PecaDaCatraca.Teclado, "Teclas", teclas, Acabamento.Tecla, sobeComATampa);
        }

        // --- Leitor de QR, à direita ---
        const double sQr = 78, tQr = 120;
        if (especificacao.Pecas.LeitorQr)
        {
            Parte(PecaDaCatraca.LeitorQr, "QrMoldura", Placa(sQr, tQr, 84, 76, 3), Acabamento.Moldura, sobeComATampa);
            Parte(PecaDaCatraca.LeitorQr, "QrJanela", Placa(sQr, tQr, 64, 56, 1, 3), Acabamento.VidroEscuro, sobeComATampa);
        }

        // --- Leitor da frente (proximidade), no centro da faixa de baixo ---
        const double sProx = -12, tProx = 32;
        if (especificacao.Pecas.LeitorDeProximidade)
        {
            Parte(PecaDaCatraca.LeitorDeProximidade, "ProxPlaca", Placa(sProx, tProx, 58, 38, 2), Acabamento.Moldura, sobeComATampa);
            Parte(PecaDaCatraca.LeitorDeProximidade, "ProxSimbolo", Placa(sProx, tProx, 30, 18, 0.8, 2), Acabamento.Fenda, sobeComATampa);
        }

        // --- Urna: fenda na faixa de baixo e, dentro da coluna, o depósito ---
        const double sUrna = 80, tUrna = 30;
        if (especificacao.Pecas.Urna)
        {
            Parte(PecaDaCatraca.Urna, "UrnaMoldura", Placa(sUrna, tUrna, 104, 24, 3), Acabamento.Moldura, sobeComATampa);
            Parte(PecaDaCatraca.Urna, "UrnaFenda", Placa(sUrna, tUrna, 88, 6, 0.6, 3), Acabamento.Fenda, sobeComATampa);
            Parte(PecaDaCatraca.Urna, "UrnaDeposito",
                new Malha().Caixa(new Ponto3(-cx + 15, yCabeca - 330, -cz + 15), new Ponto3(cx - 15, yCabeca - 20, cz - 15)),
                Acabamento.InteriorDaUrna, new Ponto3(-340, 0, 0));
        }

        // --- Sinais luminosos na face da frente da cabeça ---
        var ySinal = yCabeca + ((12 + (yFrente - yCabeca)) / 2);
        var frente = new Ponto3(0, 0, 1);
        var cima = new Ponto3(0, 1, 0);
        var sinalLiberado = new Ponto3(55, ySinal, hz);
        var sinalBloqueado = new Ponto3(-55, ySinal, hz);

        Parte(PecaDaCatraca.SinalLiberado, "LiberadoFundo",
            new Malha().CaixaOrientada(sinalLiberado + (frente * 1.5), p, cima, frente, 20, 20, 1.5), Acabamento.Fenda, sobeATampa);
        var seta = new Malha()
            .CaixaOrientada(sinalLiberado + new Ponto3(0, -5, 3.4), p, cima, frente, 3.5, 8, 0.4)
            .Quad(
                sinalLiberado + new Ponto3(-11, 3, 3.8),
                sinalLiberado + new Ponto3(11, 3, 3.8),
                sinalLiberado + new Ponto3(0, 15, 3.8),
                sinalLiberado + new Ponto3(0, 15, 3.8),
                frente);
        Parte(PecaDaCatraca.SinalLiberado, "LiberadoSeta", seta, Acabamento.LuzDeLiberado, sobeATampa);

        Parte(PecaDaCatraca.SinalBloqueado, "BloqueadoFundo",
            new Malha().CaixaOrientada(sinalBloqueado + (frente * 1.5), p, cima, frente, 20, 20, 1.5), Acabamento.Fenda, sobeATampa);
        var c45 = Math.Sqrt(0.5);
        var xis = new Malha()
            .CaixaOrientada(sinalBloqueado + (frente * 3.4), new Ponto3(c45, c45, 0), new Ponto3(-c45, c45, 0), frente, 12, 2.5, 0.4)
            .CaixaOrientada(sinalBloqueado + (frente * 3.4), new Ponto3(c45, -c45, 0), new Ponto3(c45, c45, 0), frente, 12, 2.5, 0.4);
        Parte(PecaDaCatraca.SinalBloqueado, "BloqueadoXis", xis, Acabamento.LuzDeBloqueado, sobeATampa);

        // --- Rotor: flange presa na cabeça, cubo e três braços num cone em volta do eixo ---
        var yEixo = m.AlturaDoEixo;
        Parte(PecaDaCatraca.Tampa, "Flange",
            new Malha().Cilindro(new Ponto3(hx - 2, yEixo, 0), new Ponto3(hx + 14, yEixo, 0), 64, 32),
            Acabamento.Pintura, sobeATampa);

        var inclinacao = especificacao.Rotor.InclinacaoDoEixoGraus * Math.PI / 180;
        var eixo = new Ponto3(Math.Cos(inclinacao), -Math.Sin(inclinacao), 0);
        var centroDoRotor = new Ponto3(hx + 14 + 46, yEixo, 0);

        // O braço 0 é o horizontal (+X). u e v completam a base em volta do eixo, com
        // eixo × u = v, de modo que girar pela mão direita em volta do eixo vai de u a v.
        var u = new Ponto3(Math.Sin(inclinacao), Math.Cos(inclinacao), 0);
        var v = Ponto3.Vetorial(eixo, u);
        var aberturaDoCone = inclinacao;

        var cubo = new Malha()
            .Cilindro(centroDoRotor - (eixo * 42), centroDoRotor + (eixo * 34), 48, 36)
            .Cilindro(centroDoRotor + (eixo * 34), centroDoRotor + (eixo * 44), 36, 36);
        Parte(PecaDaCatraca.Rotor, "Cubo", cubo, Acabamento.AcoInox, saiORotor);

        var raioDoBraco = m.DiametroDoBraco / 2;
        for (var k = 0; k < especificacao.Rotor.Bracos; k++)
        {
            var phi = 2 * Math.PI * k / especificacao.Rotor.Bracos;
            var direcao = ((eixo * Math.Cos(aberturaDoCone)) + (((u * Math.Cos(phi)) + (v * Math.Sin(phi))) * Math.Sin(aberturaDoCone))).Normalizado();
            var ponta = centroDoRotor + (direcao * m.ComprimentoDoBraco);
            var braco = new Malha()
                .Cilindro(centroDoRotor + (direcao * 30), ponta, raioDoBraco, 24, tampas: false)
                .Esfera(ponta, raioDoBraco, 16, 8);
            Parte(PecaDaCatraca.Rotor, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Braco{k + 1:00}"), braco, Acabamento.AcoInox, saiORotor);
        }

        // --- Leitor facial (só na variante que o tem; a tela decide se mostra) ---
        {
            var inclinacaoDaTela = 15 * Math.PI / 180;
            var normalDaTela = new Ponto3(0, Math.Sin(inclinacaoDaTela), Math.Cos(inclinacaoDaTela));
            var altoDaTela = new Ponto3(0, Math.Cos(inclinacaoDaTela), -Math.Sin(inclinacaoDaTela));
            const double meiaLargura = 66, meiaAltura = 116, meiaEspessura = 12;
            var centroDaTela = new Ponto3(-85, m.AlturaComLeitorFacial - (meiaAltura * Math.Cos(inclinacaoDaTela)) - 4, -55);
            var baseDaHaste = new Ponto3(-85, topo, -62);
            var altoDaHaste = new Ponto3(-85, Math.Max(topo + 20, centroDaTela.Y - (meiaAltura * 0.6)), -62);

            var facial = new Malha()
                .Cilindro(baseDaHaste, altoDaHaste, 16, 20)
                .Cilindro(baseDaHaste, baseDaHaste + new Ponto3(0, 8, 0), 34, 24)
                .CaixaOrientada(centroDaTela, p, altoDaTela, normalDaTela, meiaLargura, meiaAltura, meiaEspessura);
            var separacaoDoFacial = new Ponto3(0, 420, -60);
            Parte(PecaDaCatraca.LeitorFacial, "FacialCorpo", facial, Acabamento.Fenda, separacaoDoFacial);
            Parte(PecaDaCatraca.LeitorFacial, "FacialTela",
                new Malha().CaixaOrientada(centroDaTela + (normalDaTela * (meiaEspessura + 0.4)), p, altoDaTela, normalDaTela, meiaLargura - 8, meiaAltura - 14, 0.3),
                Acabamento.VidroEscuro, separacaoDoFacial);
        }

        // --- Objetos de cena ---
        var aderecos = new List<AderecoDoModelo>();

        if (especificacao.Pecas.LeitorQr)
        {
            var centroDoCelular = NoPainel(sQr, tQr, 60);
            aderecos.Add(new AderecoDoModelo(
                TipoDeAdereco.CelularNoQr,
                [
                    (new Malha().CaixaOrientada(centroDoCelular, p, q, n, 36, 72, 4), Acabamento.Celular),
                    (new Malha().CaixaOrientada(centroDoCelular - (n * 4.3), p, q, n, 32, 64, 0.3), Acabamento.TelaDoCelular),
                ],
                (n * 300) + (p * 120),
                Ponto3.Zero));
        }

        if (especificacao.Pecas.LeitorDeProximidade)
        {
            aderecos.Add(new AderecoDoModelo(
                TipoDeAdereco.CartaoNaFrente,
                [(new Malha().CaixaOrientada(NoPainel(sProx, tProx, 24), p, q, n, 43, 27, 0.5), Acabamento.Cartao)],
                (n * 280) - (p * 60),
                Ponto3.Zero));
        }

        if (especificacao.Pecas.Urna)
        {
            // Em pé sobre a fenda, com 20 mm já dentro dela.
            aderecos.Add(new AderecoDoModelo(
                TipoDeAdereco.CartaoNaUrna,
                [(new Malha().CaixaOrientada(NoPainel(sUrna, tUrna, 27 - 20 + 3), p, n, -q, 43, 27, 0.5), Acabamento.Cartao)],
                n * 150,
                Ponto3.Zero));
        }

        // A pessoa para no meio da passagem, diante do painel, e atravessa para trás (-Z).
        var meioDaPassagem = hx + 60 + (m.ComprimentoDoBraco * 0.55);
        var pessoa = new Malha()
            .Cilindro(new Ponto3(meioDaPassagem, 0, 330), new Ponto3(meioDaPassagem, 1420, 330), 150, 28)
            .Esfera(new Ponto3(meioDaPassagem, 1560, 330), 105, 20, 12);
        aderecos.Add(new AderecoDoModelo(
            TipoDeAdereco.Pessoa,
            [(pessoa, Acabamento.Pessoa)],
            new Ponto3(0, 0, 900),
            new Ponto3(0, 0, -950)));

        var piso = new Malha().Quad(
            new Ponto3(-1600, 0, 1600),
            new Ponto3(1600, 0, 1600),
            new Ponto3(1600, 0, -1600),
            new Ponto3(-1600, 0, -1600),
            new Ponto3(0, 1, 0));

        var sombra = new Malha().Quad(
            new Ponto3(-hx - 170, 0.8, hz + 200),
            new Ponto3(hx + 170, 0.8, hz + 200),
            new Ponto3(hx + 170, 0.8, -hz - 200),
            new Ponto3(-hx - 170, 0.8, -hz - 200),
            new Ponto3(0, 1, 0));

        return new ModeloDaFit4(partes, aderecos, centroDoRotor, eixo, piso, sombra);
    }
}
