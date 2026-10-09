namespace Desktop.ViewModels.GemeoDigital;

/// <summary>Um ponto, ou um vetor, em milímetros. Y para cima.</summary>
public readonly record struct Ponto3(double X, double Y, double Z)
{
    public static Ponto3 Zero { get; } = new(0, 0, 0);

    public double Comprimento => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    public static Ponto3 operator +(Ponto3 a, Ponto3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Ponto3 operator -(Ponto3 a, Ponto3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Ponto3 operator -(Ponto3 a) => new(-a.X, -a.Y, -a.Z);

    public static Ponto3 operator *(Ponto3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    public static Ponto3 operator *(double k, Ponto3 a) => a * k;

    public static Ponto3 operator /(Ponto3 a, double k) => new(a.X / k, a.Y / k, a.Z / k);

    // Nomes em inglês de propósito: são os que a regra CA2225 espera como alternativa aos
    // operadores, para linguagens sem sobrecarga de operador.
    public static Ponto3 Add(Ponto3 a, Ponto3 b) => a + b;

    public static Ponto3 Subtract(Ponto3 a, Ponto3 b) => a - b;

    public static Ponto3 Negate(Ponto3 a) => -a;

    public static Ponto3 Multiply(Ponto3 a, double k) => a * k;

    public static Ponto3 Divide(Ponto3 a, double k) => a / k;

    /// <summary>Produto escalar.</summary>
    public static double Escalar(Ponto3 a, Ponto3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>Produto vetorial (mão direita).</summary>
    public static Ponto3 Vetorial(Ponto3 a, Ponto3 b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    /// <summary>Interpolação linear: 0 dá <paramref name="a"/>, 1 dá <paramref name="b"/>.</summary>
    public static Ponto3 Entre(Ponto3 a, Ponto3 b, double t) => a + ((b - a) * t);

    /// <summary>O mesmo vetor com comprimento 1. O vetor nulo continua nulo.</summary>
    public Ponto3 Normalizado()
    {
        var c = Comprimento;
        return c < 1e-12 ? Zero : this / c;
    }
}

/// <summary>Coordenada de textura: (0, 0) no canto superior esquerdo, como no WPF.</summary>
public readonly record struct Uv(double U, double V);

/// <summary>
/// Malha de triângulos neutra, sem WPF: posições, normais, coordenadas de textura e índices.
/// </summary>
/// <remarks>
/// Fica fora do WPF para que a geometria da catraca seja testada na CI Linux e possa ser
/// exportada para conferência visual (OBJ). A tela só copia os números para um
/// <c>MeshGeometry3D</c>. Faces na ordem anti-horária vistas de fora, como o WPF espera.
/// </remarks>
public sealed class Malha
{
    private readonly List<Ponto3> _posicoes = [];
    private readonly List<Ponto3> _normais = [];
    private readonly List<Uv> _uvs = [];
    private readonly List<int> _indices = [];

    public IReadOnlyList<Ponto3> Posicoes => _posicoes;

    public IReadOnlyList<Ponto3> Normais => _normais;

    public IReadOnlyList<Uv> Uvs => _uvs;

    public IReadOnlyList<int> Indices => _indices;

    public int Triangulos => _indices.Count / 3;

    /// <summary>O ponto médio da caixa que envolve a malha.</summary>
    public Ponto3 Centro
    {
        get
        {
            var (min, max) = Limites();
            return Ponto3.Entre(min, max, 0.5);
        }
    }

    /// <summary>Menor e maior coordenada em cada eixo.</summary>
    public (Ponto3 Minimo, Ponto3 Maximo) Limites()
    {
        if (_posicoes.Count == 0)
        {
            return (Ponto3.Zero, Ponto3.Zero);
        }

        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
        double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;

        foreach (var p in _posicoes)
        {
            x0 = Math.Min(x0, p.X);
            y0 = Math.Min(y0, p.Y);
            z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X);
            y1 = Math.Max(y1, p.Y);
            z1 = Math.Max(z1, p.Z);
        }

        return (new Ponto3(x0, y0, z0), new Ponto3(x1, y1, z1));
    }

    /// <summary>
    /// Um quadrilátero plano. A face é virada para o lado de <paramref name="fora"/>, seja
    /// qual for a ordem em que os cantos vieram.
    /// </summary>
    /// <remarks>
    /// Cantos na ordem do contorno. A textura vai de (0, 1) em <paramref name="a"/> a (1, 0) em
    /// <paramref name="c"/>: com <paramref name="a"/> embaixo à esquerda, o texto fica de pé.
    /// </remarks>
    public Malha Quad(Ponto3 a, Ponto3 b, Ponto3 c, Ponto3 d, Ponto3 fora)
    {
        var normal = Ponto3.Vetorial(b - a, c - a).Normalizado();
        if (Ponto3.Escalar(normal, fora) < 0)
        {
            // Mesma face, contorno invertido: a textura continua de pé.
            (b, d) = (d, b);
            normal = -normal;
            var i0 = Vertice(a, normal, new Uv(0, 1));
            var i1 = Vertice(b, normal, new Uv(0, 0));
            var i2 = Vertice(c, normal, new Uv(1, 0));
            var i3 = Vertice(d, normal, new Uv(1, 1));
            Triangulo(i0, i1, i2);
            Triangulo(i0, i2, i3);
            return this;
        }

        var j0 = Vertice(a, normal, new Uv(0, 1));
        var j1 = Vertice(b, normal, new Uv(1, 1));
        var j2 = Vertice(c, normal, new Uv(1, 0));
        var j3 = Vertice(d, normal, new Uv(0, 0));
        Triangulo(j0, j1, j2);
        Triangulo(j0, j2, j3);
        return this;
    }

    /// <summary>
    /// Caixa com eixos quaisquer (ortonormais, <paramref name="u"/> × <paramref name="v"/> =
    /// <paramref name="w"/>), dada pelo centro e pelas meias medidas.
    /// </summary>
    public Malha CaixaOrientada(Ponto3 centro, Ponto3 u, Ponto3 v, Ponto3 w, double meiaU, double meiaV, double meiaW)
    {
        Face(centro, w, meiaW, u, meiaU, v, meiaV);
        Face(centro, -w, meiaW, v, meiaV, u, meiaU);
        Face(centro, u, meiaU, v, meiaV, w, meiaW);
        Face(centro, -u, meiaU, w, meiaW, v, meiaV);
        Face(centro, v, meiaV, w, meiaW, u, meiaU);
        Face(centro, -v, meiaV, u, meiaU, w, meiaW);
        return this;
    }

    /// <summary>Caixa alinhada aos eixos.</summary>
    public Malha Caixa(Ponto3 minimo, Ponto3 maximo)
    {
        var meia = (maximo - minimo) * 0.5;
        return CaixaOrientada(
            Ponto3.Entre(minimo, maximo, 0.5),
            new Ponto3(1, 0, 0),
            new Ponto3(0, 1, 0),
            new Ponto3(0, 0, 1),
            Math.Abs(meia.X),
            Math.Abs(meia.Y),
            Math.Abs(meia.Z));
    }

    /// <summary>Cilindro entre dois pontos, lados suaves e tampas planas.</summary>
    public Malha Cilindro(Ponto3 inicio, Ponto3 fim, double raio, int lados = 24, bool tampas = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lados, 3);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(raio);

        var eixo = (fim - inicio).Normalizado();
        var (u, v) = Perpendiculares(eixo);

        var anelDeBaixo = new int[lados + 1];
        var anelDeCima = new int[lados + 1];

        for (var i = 0; i <= lados; i++)
        {
            var angulo = 2 * Math.PI * i / lados;
            var direcao = (u * Math.Cos(angulo)) + (v * Math.Sin(angulo));
            var fracao = (double)i / lados;
            anelDeBaixo[i] = Vertice(inicio + (direcao * raio), direcao, new Uv(fracao, 1));
            anelDeCima[i] = Vertice(fim + (direcao * raio), direcao, new Uv(fracao, 0));
        }

        for (var i = 0; i < lados; i++)
        {
            Triangulo(anelDeBaixo[i], anelDeBaixo[i + 1], anelDeCima[i + 1]);
            Triangulo(anelDeBaixo[i], anelDeCima[i + 1], anelDeCima[i]);
        }

        if (tampas)
        {
            Tampa(fim, eixo, u, v, raio, lados);
            Tampa(inicio, -eixo, u, v, raio, lados);
        }

        return this;
    }

    /// <summary>Esfera com normais suaves.</summary>
    public Malha Esfera(Ponto3 centro, double raio, int fatias = 20, int aneis = 12)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fatias, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(aneis, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(raio);

        var inicio = _posicoes.Count;

        for (var a = 0; a <= aneis; a++)
        {
            var theta = Math.PI * a / aneis;
            for (var f = 0; f <= fatias; f++)
            {
                var phi = 2 * Math.PI * f / fatias;
                var n = new Ponto3(Math.Sin(theta) * Math.Cos(phi), Math.Cos(theta), Math.Sin(theta) * Math.Sin(phi));
                Vertice(centro + (n * raio), n, new Uv((double)f / fatias, (double)a / aneis));
            }
        }

        var largura = fatias + 1;
        for (var a = 0; a < aneis; a++)
        {
            for (var f = 0; f < fatias; f++)
            {
                var i0 = inicio + (a * largura) + f;
                var i1 = i0 + 1;
                var i2 = i0 + largura;
                var i3 = i2 + 1;
                TrianguloVoltadoPara(i0, i2, i1, centro);
                TrianguloVoltadoPara(i1, i2, i3, centro);
            }
        }

        return this;
    }

    /// <summary>
    /// Extrusão de um perfil convexo do plano YZ (pares Z, Y) entre <paramref name="x0"/> e
    /// <paramref name="x1"/>. É assim que a cabeça inclinada da catraca é desenhada.
    /// </summary>
    public Malha PrismaDePerfil(IReadOnlyList<(double Z, double Y)> perfil, double x0, double x1)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        ArgumentOutOfRangeException.ThrowIfLessThan(perfil.Count, 3);

        double somaZ = 0, somaY = 0;
        foreach (var (z, y) in perfil)
        {
            somaZ += z;
            somaY += y;
        }

        var meioX = (x0 + x1) / 2;
        var centro = new Ponto3(meioX, somaY / perfil.Count, somaZ / perfil.Count);

        for (var i = 0; i < perfil.Count; i++)
        {
            var (za, ya) = perfil[i];
            var (zb, yb) = perfil[(i + 1) % perfil.Count];
            var a0 = new Ponto3(x0, ya, za);
            var b0 = new Ponto3(x0, yb, zb);
            var b1 = new Ponto3(x1, yb, zb);
            var a1 = new Ponto3(x1, ya, za);
            var meio = Ponto3.Entre(a0, b1, 0.5);
            Quad(a0, b0, b1, a1, meio - centro);
        }

        TampaDoPerfil(perfil, x1, new Ponto3(1, 0, 0));
        TampaDoPerfil(perfil, x0, new Ponto3(-1, 0, 0));
        return this;
    }

    /// <summary>Acrescenta outra malha a esta.</summary>
    public Malha Juntar(Malha outra)
    {
        ArgumentNullException.ThrowIfNull(outra);

        var deslocamento = _posicoes.Count;
        _posicoes.AddRange(outra._posicoes);
        _normais.AddRange(outra._normais);
        _uvs.AddRange(outra._uvs);
        foreach (var i in outra._indices)
        {
            _indices.Add(i + deslocamento);
        }

        return this;
    }

    /// <summary>Texto no formato Wavefront OBJ, para conferir a geometria fora do programa.</summary>
    public string ParaObj(string nome, int primeiroVertice = 1)
    {
        var texto = new System.Text.StringBuilder();
        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        texto.Append("o ").AppendLine(nome);

        foreach (var p in _posicoes)
        {
            texto.AppendLine(string.Create(cultura, $"v {p.X:0.###} {p.Y:0.###} {p.Z:0.###}"));
        }

        for (var i = 0; i < _indices.Count; i += 3)
        {
            texto.AppendLine(string.Create(
                cultura,
                $"f {_indices[i] + primeiroVertice} {_indices[i + 1] + primeiroVertice} {_indices[i + 2] + primeiroVertice}"));
        }

        return texto.ToString();
    }

    /// <summary>Dois vetores unitários perpendiculares ao eixo e entre si (u × v = eixo).</summary>
    internal static (Ponto3 U, Ponto3 V) Perpendiculares(Ponto3 eixo)
    {
        var referencia = Math.Abs(eixo.Y) < 0.9 ? new Ponto3(0, 1, 0) : new Ponto3(1, 0, 0);
        var u = Ponto3.Vetorial(referencia, eixo).Normalizado();
        var v = Ponto3.Vetorial(eixo, u).Normalizado();
        return (u, v);
    }

    private int Vertice(Ponto3 posicao, Ponto3 normal, Uv uv)
    {
        _posicoes.Add(posicao);
        _normais.Add(normal);
        _uvs.Add(uv);
        return _posicoes.Count - 1;
    }

    private void Triangulo(int a, int b, int c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    // Vira o triângulo para fora de um ponto interno (esfera: o centro).
    private void TrianguloVoltadoPara(int a, int b, int c, Ponto3 interno)
    {
        var pa = _posicoes[a];
        var normal = Ponto3.Vetorial(_posicoes[b] - pa, _posicoes[c] - pa);
        var fora = Ponto3.Entre(pa, Ponto3.Entre(_posicoes[b], _posicoes[c], 0.5), 0.5) - interno;

        if (Ponto3.Escalar(normal, fora) < 0)
        {
            Triangulo(a, c, b);
        }
        else
        {
            Triangulo(a, b, c);
        }
    }

    // Uma face da caixa: normal n, eixos p e q no plano, com p × q = n.
    private void Face(Ponto3 centro, Ponto3 n, double meiaN, Ponto3 p, double meiaP, Ponto3 q, double meiaQ)
    {
        var c = centro + (n * meiaN);
        Quad(
            c - (p * meiaP) - (q * meiaQ),
            c + (p * meiaP) - (q * meiaQ),
            c + (p * meiaP) + (q * meiaQ),
            c - (p * meiaP) + (q * meiaQ),
            n);
    }

    private void Tampa(Ponto3 centro, Ponto3 normal, Ponto3 u, Ponto3 v, double raio, int lados)
    {
        var meio = Vertice(centro, normal, new Uv(0.5, 0.5));
        var anel = new int[lados + 1];

        for (var i = 0; i <= lados; i++)
        {
            var angulo = 2 * Math.PI * i / lados;
            var direcao = (u * Math.Cos(angulo)) + (v * Math.Sin(angulo));
            anel[i] = Vertice(centro + (direcao * raio), normal, new Uv(0.5 + (0.5 * Math.Cos(angulo)), 0.5 - (0.5 * Math.Sin(angulo))));
        }

        for (var i = 0; i < lados; i++)
        {
            var pa = _posicoes[anel[i]];
            var pb = _posicoes[anel[i + 1]];
            var n = Ponto3.Vetorial(pa - centro, pb - centro);
            if (Ponto3.Escalar(n, normal) >= 0)
            {
                Triangulo(meio, anel[i], anel[i + 1]);
            }
            else
            {
                Triangulo(meio, anel[i + 1], anel[i]);
            }
        }
    }

    private void TampaDoPerfil(IReadOnlyList<(double Z, double Y)> perfil, double x, Ponto3 normal)
    {
        var indices = new int[perfil.Count];
        for (var i = 0; i < perfil.Count; i++)
        {
            var (z, y) = perfil[i];
            indices[i] = Vertice(new Ponto3(x, y, z), normal, new Uv(0, 0));
        }

        // Perfil convexo: leque a partir do primeiro ponto.
        for (var i = 1; i < perfil.Count - 1; i++)
        {
            var p0 = _posicoes[indices[0]];
            var n = Ponto3.Vetorial(_posicoes[indices[i]] - p0, _posicoes[indices[i + 1]] - p0);
            if (Ponto3.Escalar(n, normal) >= 0)
            {
                Triangulo(indices[0], indices[i], indices[i + 1]);
            }
            else
            {
                Triangulo(indices[0], indices[i + 1], indices[i]);
            }
        }
    }
}
