using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Desktop.ViewModels;
using Desktop.ViewModels.GemeoDigital;

namespace Desktop.App.Telas;

/// <summary>
/// Tela "Gêmeo digital": copia a geometria da ViewModel para o 3D do WPF e desenha, a cada
/// quadro, o que a cena manda. Nenhuma regra mora aqui — só desenho, câmera e mouse.
/// </summary>
/// <remarks>
/// <para>
/// 3D nativo do WPF (Viewport3D), sem biblioteca a mais: roda na mesma máquina modesta que
/// o painel, e não acrescenta pacote para a auditoria de dependências (docs/33, seção 4).
/// </para>
/// <para>
/// O laço de desenho só existe enquanto a tela está aberta: sair da tela para o laço e o
/// fluxo ao vivo dela.
/// </para>
/// </remarks>
public partial class Gemeo : UserControl
{
    private const double Graus = Math.PI / 180;

    private readonly Dictionary<GeometryModel3D, PecaDaCatraca> _pecaDoModelo = [];
    private readonly Dictionary<PecaDaCatraca, List<(GeometryModel3D Modelo, Material Normal)>> _modelosDaPeca = [];
    private readonly Dictionary<PecaDaCatraca, TranslateTransform3D> _separacaoDaPeca = [];
    private readonly Dictionary<PecaDaCatraca, Ponto3> _deslocamentoDaPeca = [];
    private readonly Dictionary<TipoDeAdereco, (Model3DGroup Grupo, TranslateTransform3D Posicao, AderecoDoModelo Dados)> _aderecos = [];

    private readonly Model3DGroup _catraca = new();
    private readonly Model3DGroup _facial = new();
    private readonly Model3DGroup _depositoDaUrna = new();
    private readonly TranslateTransform3D _separacaoDoDeposito = new();
    private readonly AxisAngleRotation3D _rotacaoDoRotor = new();
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 34, NearPlaneDistance = 20, FarPlaneDistance = 40000 };

    private GemeoDigitalViewModel? _vm;

    // As cores do objeto vêm da especificação (fit4.json): é uma catraca, não um controle
    // da interface. O realce e as luzes acesas vêm do tema, pelas chaves Rayzer.
    private AparenciaDaFit4? _aparencia;
    // Para o fluxo ao vivo. Guardado como ação, e não como o CancellationTokenSource: ele
    // vive e morre com a tela aberta (Loaded/Unloaded), não com o objeto.
    private Action? _pararAoVivo;
    private GeometryModel3D? _telaDoDisplay;
    private GeometryModel3D? _setaDeLiberado;
    private GeometryModel3D? _xisDeBloqueado;
    private GeometryModel3D? _molduraDaUrna;
    private Material? _materialDaUrnaNormal;
    private Material? _materialDaUrnaDesligada;
    private string _textoDoDisplay = "\u0000";
    private bool _luzDoDisplay;
    private bool? _liberadoAceso;
    private bool? _bloqueadoAceso;
    private (bool Cheia, bool Ligada)? _situacaoDaUrna;
    private (PecaDaCatraca? Selecionada, PecaDaCatraca? Apontada, PecaDaCatraca? Destaque) _realce;
    private bool _facialNaCena;
    private bool _depositoNaCena;
    private Ponto3 _deslocamentoDoDeposito;

    // Câmera orbital: onde está (alvo, distância, ângulos) e para onde vai, suavizado.
    private Ponto3 _alvo;
    private Ponto3 _alvoDesejado;
    private double _distancia;
    private double _distanciaDesejada;
    private double _rumo;
    private double _rumoDesejado;
    private double _elevacao;
    private double _elevacaoDesejada;
    private double _separacao;
    private TimeSpan _ultimoQuadro;

    private Point _inicioDoArrasto;
    private bool _arrastando;
    private bool _moveuNoArrasto;

    public Gemeo()
    {
        InitializeComponent();

        Vista3D.Camera = _camera;
        DataContextChanged += (_, e) => Ligar(e.NewValue as GemeoDigitalViewModel);
        Loaded += (_, _) => Abrir();
        Unloaded += (_, _) => Fechar();

        Palco.MouseLeftButtonDown += AoApertar;
        Palco.MouseMove += AoMover;
        Palco.MouseLeftButtonUp += AoSoltar;
        Palco.MouseWheel += AoRolar;
        Palco.MouseLeave += (_, _) => _vm?.Apontar(null);
        Palco.KeyDown += AoTeclar;
    }

    private void Ligar(GemeoDigitalViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= AoMudarNaViewModel;
        }

        _vm = vm;
        if (vm is null)
        {
            return;
        }

        vm.PropertyChanged += AoMudarNaViewModel;
        _aparencia = vm.Especificacao.Aparencia;
        Montar(vm.Modelo);
        IrPara(vm.Vista, imediato: true);
    }

    private void Abrir()
    {
        CompositionTarget.Rendering += AoDesenhar;

        if (_vm is { } vm && _pararAoVivo is null)
        {
            var cancelamento = new CancellationTokenSource();
            _ = vm.AcompanharAsync(acao => Dispatcher.BeginInvoke(acao), cancelamento.Token);
            _pararAoVivo = () =>
            {
                cancelamento.Cancel();
                cancelamento.Dispose();
            };
        }
    }

    private void Fechar()
    {
        CompositionTarget.Rendering -= AoDesenhar;
        _pararAoVivo?.Invoke();
        _pararAoVivo = null;
    }

    private void AoMudarNaViewModel(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(GemeoDigitalViewModel.VersaoDaVista):
                IrPara(vm.Vista, imediato: false);
                break;
            case nameof(GemeoDigitalViewModel.VersaoDoFoco) when vm.PecaSelecionada is { } ficha:
                Focar(ficha.Peca);
                break;
            default:
                break;
        }
    }

    // ------------------------------------------------------------------ montagem

    private void Montar(ModeloDaFit4 modelo)
    {
        _pecaDoModelo.Clear();
        _modelosDaPeca.Clear();
        _separacaoDaPeca.Clear();
        _deslocamentoDaPeca.Clear();
        _aderecos.Clear();
        _catraca.Children.Clear();
        _facial.Children.Clear();
        _depositoDaUrna.Children.Clear();
        _facialNaCena = false;
        _depositoNaCena = false;

        var grupos = new Dictionary<PecaDaCatraca, Model3DGroup>();

        foreach (var parte in modelo.Partes)
        {
            if (!grupos.TryGetValue(parte.Peca, out var grupo))
            {
                grupo = new Model3DGroup();
                var separacao = new TranslateTransform3D();
                _separacaoDaPeca[parte.Peca] = separacao;
                _deslocamentoDaPeca[parte.Peca] = parte.Separacao;

                if (parte.Peca is PecaDaCatraca.Rotor)
                {
                    var giro = new RotateTransform3D(_rotacaoDoRotor, Ponto(modelo.CentroDoRotor));
                    _rotacaoDoRotor.Axis = Vetor(modelo.EixoDoRotor);
                    grupo.Transform = new Transform3DGroup { Children = { giro, separacao } };
                }
                else
                {
                    grupo.Transform = separacao;
                }

                grupos[parte.Peca] = grupo;

                if (parte.Peca is PecaDaCatraca.LeitorFacial)
                {
                    _facial.Children.Add(grupo);
                }
                else
                {
                    _catraca.Children.Add(grupo);
                }
            }

            var material = Material(parte.Acabamento);
            var geometria = new GeometryModel3D(Malha(parte.Malha), material) { BackMaterial = material };
            _pecaDoModelo[geometria] = parte.Peca;

            if (!_modelosDaPeca.TryGetValue(parte.Peca, out var lista))
            {
                lista = [];
                _modelosDaPeca[parte.Peca] = lista;
            }

            lista.Add((geometria, material));

            switch (parte.Nome)
            {
                case "DisplayTela":
                    _telaDoDisplay = geometria;
                    break;
                case "LiberadoSeta":
                    _setaDeLiberado = geometria;
                    break;
                case "BloqueadoXis":
                    _xisDeBloqueado = geometria;
                    break;
                case "UrnaMoldura":
                    _molduraDaUrna = geometria;
                    _materialDaUrnaNormal = material;
                    _materialDaUrnaDesligada = Material(Acabamento.Fenda);
                    break;
                default:
                    break;
            }

            // O depósito da urna fica dentro da coluna: só aparece com as peças separadas.
            if (parte.Nome == "UrnaDeposito")
            {
                _depositoDaUrna.Children.Add(geometria);
                _depositoDaUrna.Transform = _separacaoDoDeposito;
                _deslocamentoDoDeposito = parte.Separacao;
            }
            else
            {
                grupo.Children.Add(geometria);
            }
        }

        var luzes = Aparencia.Cena;
        var cena = new Model3DGroup();
        cena.Children.Add(new AmbientLight(Cor(luzes.LuzAmbiente)));
        cena.Children.Add(new DirectionalLight(Cor(luzes.LuzPrincipal), new Vector3D(-0.45, -0.8, -0.55)));
        cena.Children.Add(new DirectionalLight(Cor(luzes.LuzDeRecorte), new Vector3D(0.6, -0.2, 0.7)));
        cena.Children.Add(new DirectionalLight(Cor(luzes.LuzDeBaixo), new Vector3D(0.2, 0.9, -0.3)));

        var piso = new DiffuseMaterial(new SolidColorBrush(Cor(luzes.Piso)));
        piso.Freeze();
        cena.Children.Add(new GeometryModel3D(Malha(modelo.Piso), piso));

        // Sombra de contato: um degradê escuro embaixo da base, sem custo de sombra real.
        var corDaSombra = Cor(luzes.Sombra);
        var sombra = new RadialGradientBrush(corDaSombra, corDaSombra with { A = 0 });
        sombra.Freeze();
        var materialDaSombra = new DiffuseMaterial(sombra);
        materialDaSombra.Freeze();
        cena.Children.Add(new GeometryModel3D(Malha(modelo.Sombra), materialDaSombra));

        cena.Children.Add(_catraca);

        foreach (var adereco in modelo.Aderecos)
        {
            var grupo = new Model3DGroup();
            foreach (var (malha, acabamento) in adereco.Partes)
            {
                var material = Material(acabamento);
                grupo.Children.Add(new GeometryModel3D(Malha(malha), material) { BackMaterial = material });
            }

            var posicao = new TranslateTransform3D();
            grupo.Transform = posicao;
            _aderecos[adereco.Tipo] = (grupo, posicao, adereco);
        }

        // Transparentes por último: é a ordem em que o WPF os mistura certo.
        foreach (var (grupo, _, _) in _aderecos.Values.OrderBy(a => a.Dados.Tipo is TipoDeAdereco.Pessoa ? 1 : 0))
        {
            cena.Children.Add(grupo);
        }

        Vista3D.Children.Clear();
        Vista3D.Children.Add(new ModelVisual3D { Content = cena });

        _textoDoDisplay = "\u0000";
        _liberadoAceso = null;
        _bloqueadoAceso = null;
        _situacaoDaUrna = null;
        _realce = default;
    }

    private static MeshGeometry3D Malha(Malha m)
    {
        var positions = new Point3DCollection(m.Posicoes.Count);
        var normals = new Vector3DCollection(m.Normais.Count);
        var uvs = new PointCollection(m.Uvs.Count);
        var indices = new Int32Collection(m.Indices.Count);

        foreach (var p in m.Posicoes)
        {
            positions.Add(Ponto(p));
        }

        foreach (var n in m.Normais)
        {
            normals.Add(Vetor(n));
        }

        foreach (var uv in m.Uvs)
        {
            uvs.Add(new Point(uv.U, uv.V));
        }

        foreach (var i in m.Indices)
        {
            indices.Add(i);
        }

        var malha = new MeshGeometry3D { Positions = positions, Normals = normals, TextureCoordinates = uvs, TriangleIndices = indices };
        malha.Freeze();
        return malha;
    }

    private static Point3D Ponto(Ponto3 p) => new(p.X, p.Y, p.Z);

    private static Vector3D Vetor(Ponto3 p) => new(p.X, p.Y, p.Z);

    private AparenciaDaFit4 Aparencia => _aparencia ?? EspecificacaoDaFit4.Padrao.Aparencia;

    private static Color Cor(string hex)
    {
        var c = CorRgba.Ler(hex);
        return Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    /// <summary>A cor de uma chave do tema Rayzer (muda com claro, escuro e alto contraste).</summary>
    private Color CorDoTema(string chave) =>
        TryFindResource(chave) is SolidColorBrush pincel ? pincel.Color : Cor(Aparencia.De(Acabamento.Fenda).Cor);

    private Material Material(Acabamento acabamento)
    {
        var a = Aparencia.De(acabamento);
        Material material = acabamento switch
        {
            Acabamento.TelaDoCelular => TelaComQr(Cor(Aparencia.Cena.QrClaro), Cor(Aparencia.Cena.QrEscuro)),
            Acabamento.Pessoa => new DiffuseMaterial(new SolidColorBrush(Cor(a.Cor))),
            _ => Fosco(Cor(a.Cor), Cor(a.Brilho), a.Potencia),
        };

        material.Freeze();
        return material;
    }

    private static MaterialGroup Fosco(Color cor, Color brilho, double potencia)
    {
        var grupo = new MaterialGroup();
        grupo.Children.Add(new DiffuseMaterial(new SolidColorBrush(cor)));
        grupo.Children.Add(new SpecularMaterial(new SolidColorBrush(brilho), potencia));
        return grupo;
    }

    /// <summary>Um desenho de QR genérico: parece um QR, não é o código de ninguém.</summary>
    private static MaterialGroup TelaComQr(Color claro, Color escuro)
    {
        var fundo = new SolidColorBrush(claro);
        var tinta = new SolidColorBrush(escuro);
        var desenho = new DrawingGroup();
        using (var dc = desenho.Open())
        {
            dc.DrawRectangle(fundo, null, new Rect(0, 0, 32, 64));
            var semente = 7;

            for (var y = 0; y < 21; y++)
            {
                for (var x = 0; x < 21; x++)
                {
                    semente = ((semente * 1103515245) + 12345) & 0x7FFFFFFF;
                    if (ModuloDoQr(x, y, semente))
                    {
                        dc.DrawRectangle(tinta, null, new Rect(3 + (x * 1.24), 19 + (y * 1.24), 1.25, 1.25));
                    }
                }
            }
        }

        var pincel = new DrawingBrush(desenho) { Stretch = Stretch.Fill };
        var grupo = new MaterialGroup();
        grupo.Children.Add(new DiffuseMaterial(pincel));
        grupo.Children.Add(new EmissiveMaterial(pincel));
        return grupo;
    }

    // Os três quadrados de canto de um QR (borda e miolo), e o resto pseudoaleatório.
    private static bool ModuloDoQr(int x, int y, int semente)
    {
        foreach (var (cx, cy) in new[] { (0, 0), (14, 0), (0, 14) })
        {
            var (dx, dy) = (x - cx, y - cy);
            if (dx is >= 0 and < 7 && dy is >= 0 and < 7)
            {
                var borda = dx is 0 or 6 || dy is 0 or 6;
                var miolo = dx is >= 2 and <= 4 && dy is >= 2 and <= 4;
                return borda || miolo;
            }

            if (dx is >= -1 and < 8 && dy is >= -1 and < 8)
            {
                return false; // a margem clara em volta do quadrado
            }
        }

        return (semente >> 8) % 2 == 0;
    }

    // ------------------------------------------------------------------ quadro a quadro

    private void AoDesenhar(object? sender, EventArgs e)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        // Rendering pode vir mais de uma vez por quadro: só o primeiro de cada quadro conta.
        var agora = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
        if (agora == _ultimoQuadro)
        {
            return;
        }

        var dt = Math.Clamp((agora - _ultimoQuadro).TotalSeconds, 0, 0.1);
        _ultimoQuadro = agora;

        var quadro = vm.Quadro();

        _rotacaoDoRotor.Angle = quadro.AnguloDoRotor;
        AtualizarDisplay(quadro);
        AtualizarLuzes(quadro);
        AtualizarUrna(quadro, vm.UrnaLigada);
        AtualizarAderecos(quadro);
        AtualizarSeparacao(vm.PecasSeparadas, dt);
        AtualizarFacial(vm.MostrarLeitorFacial);
        AtualizarRealce(vm.PecaSelecionada?.Peca, vm.PecaApontada?.Peca, vm.PecaEmDestaque);
        AtualizarCamera(dt);
    }

    private void AtualizarDisplay(QuadroDaCena quadro)
    {
        var texto = quadro.Linha1 + "\n" + quadro.Linha2;
        if (_telaDoDisplay is null || (texto == _textoDoDisplay && quadro.LuzDeFundoDoDisplay == _luzDoDisplay))
        {
            return;
        }

        _textoDoDisplay = texto;
        _luzDoDisplay = quadro.LuzDeFundoDoDisplay;

        // O texto sai do próprio display (textura emissiva), não flutua sobre ele.
        var luzes = Aparencia.Cena;
        var fundo = Cor(quadro.LuzDeFundoDoDisplay ? luzes.DisplayAceso : luzes.DisplayApagado);
        var tinta = Cor(quadro.LuzDeFundoDoDisplay ? luzes.TextoAceso : luzes.TextoApagado);
        var imagem = RenderizarDisplay(quadro.Linha1, quadro.Linha2, fundo, tinta);

        var grupo = new MaterialGroup();
        grupo.Children.Add(new DiffuseMaterial(new ImageBrush(imagem)));
        if (quadro.LuzDeFundoDoDisplay)
        {
            grupo.Children.Add(new EmissiveMaterial(new ImageBrush(imagem) { Opacity = 0.85 }));
        }

        grupo.Freeze();
        _telaDoDisplay.Material = grupo;
    }

    private RenderTargetBitmap RenderizarDisplay(string linha1, string linha2, Color fundo, Color tinta)
    {
        const int largura = 360, altura = 68;
        var dpi = VisualTreeHelper.GetDpi(this);
        var fonte = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var pincel = new SolidColorBrush(tinta);
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(fundo), null, new Rect(0, 0, largura, altura));
            var passo = (largura - 16) / 16.0;

            for (var linha = 0; linha < 2; linha++)
            {
                var texto = linha == 0 ? linha1 : linha2;
                for (var i = 0; i < texto.Length && i < 16; i++)
                {
                    // Uma célula por caractere, como no display de verdade.
                    var celula = new Rect(8 + (i * passo), 6 + (linha * 30), passo - 2, 26);
                    dc.DrawRectangle(new SolidColorBrush(tinta with { A = 0x22 }), null, celula);
                    var letra = new FormattedText(
                        texto[i].ToString(CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        fonte,
                        22,
                        pincel,
                        dpi.PixelsPerDip);
                    dc.DrawText(letra, new Point(celula.X + ((celula.Width - letra.Width) / 2), celula.Y));
                }
            }
        }

        var bitmap = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void AtualizarLuzes(QuadroDaCena quadro)
    {
        if (_setaDeLiberado is not null && _liberadoAceso != quadro.LuzDeLiberado)
        {
            _liberadoAceso = quadro.LuzDeLiberado;
            _setaDeLiberado.Material = quadro.LuzDeLiberado
                ? Aceso(CorDoTema("Rayzer.Access.Granted"))
                : Base(PecaDaCatraca.SinalLiberado, _setaDeLiberado);
        }

        if (_xisDeBloqueado is not null && _bloqueadoAceso != quadro.LuzDeBloqueado)
        {
            _bloqueadoAceso = quadro.LuzDeBloqueado;
            _xisDeBloqueado.Material = quadro.LuzDeBloqueado
                ? Aceso(CorDoTema("Rayzer.Access.Denied"))
                : Base(PecaDaCatraca.SinalBloqueado, _xisDeBloqueado);
        }
    }

    private void AtualizarUrna(QuadroDaCena quadro, bool ligada)
    {
        if (_molduraDaUrna is null || _situacaoDaUrna == (quadro.UrnaCheia, ligada))
        {
            return;
        }

        _situacaoDaUrna = (quadro.UrnaCheia, ligada);
        _molduraDaUrna.Material = quadro.UrnaCheia
            ? Aceso(CorDoTema("Rayzer.Warning"))
            : ligada ? _materialDaUrnaNormal : _materialDaUrnaDesligada;
    }

    private void AtualizarAderecos(QuadroDaCena quadro)
    {
        foreach (var (_, posicao, dados) in _aderecos.Values)
        {
            var visivel = dados.Tipo switch
            {
                TipoDeAdereco.CelularNoQr => quadro.AderecoVisivel && quadro.Leitor is LeitorDaCena.Qr,
                TipoDeAdereco.CartaoNaFrente => quadro.AderecoVisivel && quadro.Leitor is LeitorDaCena.CartaoNaFrente,
                TipoDeAdereco.CartaoNaUrna => quadro.AderecoVisivel && quadro.Leitor is LeitorDaCena.CartaoNaUrna,
                _ => quadro.PessoaVisivel,
            };

            if (!visivel)
            {
                // Longe e fora de vista: tirar e pôr na cena a cada quadro custaria mais.
                posicao.OffsetY = -100000;
                continue;
            }

            var longe = dados.Aproximacao * (1 - quadro.Aproximacao);
            var andou = dados.Travessia * quadro.Travessia;
            var total = longe + andou;
            posicao.OffsetX = total.X;
            posicao.OffsetY = total.Y;
            posicao.OffsetZ = total.Z;
        }
    }

    private void AtualizarSeparacao(bool separadas, double dt)
    {
        var alvo = separadas ? 1.0 : 0.0;
        if (Math.Abs(_separacao - alvo) < 1e-4 && _separacaoDaPeca.Count > 0 && _depositoNaCena == separadas)
        {
            return;
        }

        _separacao = Aproximar(_separacao, alvo, dt, 6);
        foreach (var (peca, transformacao) in _separacaoDaPeca)
        {
            var d = _deslocamentoDaPeca[peca] * _separacao;
            transformacao.OffsetX = d.X;
            transformacao.OffsetY = d.Y;
            transformacao.OffsetZ = d.Z;
        }

        var deposito = _deslocamentoDoDeposito * _separacao;
        _separacaoDoDeposito.OffsetX = deposito.X;
        _separacaoDoDeposito.OffsetY = deposito.Y;
        _separacaoDoDeposito.OffsetZ = deposito.Z;

        var mostrarDeposito = _separacao > 0.05;
        if (mostrarDeposito != _depositoNaCena)
        {
            _depositoNaCena = mostrarDeposito;
            if (mostrarDeposito)
            {
                _catraca.Children.Add(_depositoDaUrna);
            }
            else
            {
                _catraca.Children.Remove(_depositoDaUrna);
            }
        }
    }

    private void AtualizarFacial(bool mostrar)
    {
        if (mostrar == _facialNaCena)
        {
            return;
        }

        _facialNaCena = mostrar;
        if (mostrar)
        {
            _catraca.Children.Add(_facial);
        }
        else
        {
            _catraca.Children.Remove(_facial);
        }
    }

    private void AtualizarRealce(PecaDaCatraca? selecionada, PecaDaCatraca? apontada, PecaDaCatraca? destaque)
    {
        var realce = (selecionada, apontada, destaque);
        if (realce == _realce)
        {
            return;
        }

        var antes = new[] { _realce.Selecionada, _realce.Apontada, _realce.Destaque };
        _realce = realce;

        foreach (var peca in antes.Concat(new[] { selecionada, apontada, destaque }).OfType<PecaDaCatraca>().Distinct())
        {
            if (!_modelosDaPeca.TryGetValue(peca, out var modelos))
            {
                continue;
            }

            var intensidade = peca == apontada ? 0.30 : peca == selecionada ? 0.22 : peca == destaque ? 0.18 : 0;

            foreach (var (modelo, normal) in modelos)
            {
                // As luzes e o display têm material próprio, trocado pelo quadro.
                if (ReferenceEquals(modelo, _telaDoDisplay)
                    || (ReferenceEquals(modelo, _setaDeLiberado) && _liberadoAceso == true)
                    || (ReferenceEquals(modelo, _xisDeBloqueado) && _bloqueadoAceso == true)
                    || (ReferenceEquals(modelo, _molduraDaUrna) && _situacaoDaUrna != (false, true)))
                {
                    continue;
                }

                modelo.Material = intensidade > 0 ? Realcado(normal, CorDoTema("Rayzer.Brand.Cyan"), intensidade) : normal;
            }
        }
    }

    private Material Base(PecaDaCatraca peca, GeometryModel3D modelo) =>
        _modelosDaPeca.TryGetValue(peca, out var lista) && lista.FirstOrDefault(m => ReferenceEquals(m.Modelo, modelo)) is { Normal: { } normal }
            ? normal
            : Material(Acabamento.Fenda);

    private static MaterialGroup Aceso(Color cor)
    {
        var grupo = new MaterialGroup();
        grupo.Children.Add(new DiffuseMaterial(new SolidColorBrush(cor)));
        grupo.Children.Add(new EmissiveMaterial(new SolidColorBrush(cor)));
        grupo.Freeze();
        return grupo;
    }

    private static MaterialGroup Realcado(Material normal, Color destaque, double intensidade)
    {
        var brilho = destaque with { A = (byte)(255 * intensidade) };
        var grupo = new MaterialGroup();
        grupo.Children.Add(normal);
        grupo.Children.Add(new EmissiveMaterial(new SolidColorBrush(brilho)));
        grupo.Freeze();
        return grupo;
    }

    // ------------------------------------------------------------------ câmera

    private void IrPara(VistaDaCamera vista, bool imediato)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var (min, max) = vm.Modelo.Limites();
        var centro = new Ponto3((min.X + max.X) / 2, (min.Y + max.Y) * 0.45, (min.Z + max.Z) / 2);
        var painel = vm.Modelo.CentroDe(PecaDaCatraca.Display);

        (_alvoDesejado, _distanciaDesejada, _rumoDesejado, _elevacaoDesejada) = vista switch
        {
            VistaDaCamera.Frente => (centro, 3000.0, 0.0, 8.0),
            VistaDaCamera.Painel => (painel with { Y = painel.Y - 60 }, 1100.0, 0.0, 40.0),
            VistaDaCamera.Bracos => (centro, 3000.0, 90.0, 10.0),
            VistaDaCamera.Tras => (centro, 3000.0, 180.0, 12.0),
            VistaDaCamera.Cima => (centro, 3000.0, 20.0, 80.0),
            _ => (centro, 3200.0, 38.0, 18.0),
        };

        // O rumo vai pelo caminho mais curto.
        _rumoDesejado = _rumo + NormalizarAngulo(_rumoDesejado - _rumo);

        if (imediato)
        {
            (_alvo, _distancia, _rumo, _elevacao) = (_alvoDesejado, _distanciaDesejada, _rumoDesejado, _elevacaoDesejada);
            AplicarCamera();
        }
    }

    private void Focar(PecaDaCatraca peca)
    {
        if (_vm is not { } vm)
        {
            return;
        }

        var deslocamento = _deslocamentoDaPeca.TryGetValue(peca, out var d) ? d * _separacao : Ponto3.Zero;
        _alvoDesejado = vm.Modelo.CentroDe(peca) + deslocamento;
        _distanciaDesejada = Math.Clamp(vm.Modelo.TamanhoDe(peca) * 3.2, 700, 3200);

        // Peças do painel: olhar de frente e um pouco de cima, para ler o que está escrito.
        if (peca is PecaDaCatraca.Display or PecaDaCatraca.Teclado or PecaDaCatraca.LeitorQr
            or PecaDaCatraca.LeitorDeProximidade or PecaDaCatraca.Urna or PecaDaCatraca.SinalLiberado or PecaDaCatraca.SinalBloqueado)
        {
            _rumoDesejado = _rumo + NormalizarAngulo(10 - _rumo);
            _elevacaoDesejada = 32;
        }
        else if (peca is PecaDaCatraca.Rotor)
        {
            _rumoDesejado = _rumo + NormalizarAngulo(55 - _rumo);
            _elevacaoDesejada = 18;
        }
    }

    private void AtualizarCamera(double dt)
    {
        _alvo = new Ponto3(
            Aproximar(_alvo.X, _alvoDesejado.X, dt, 5),
            Aproximar(_alvo.Y, _alvoDesejado.Y, dt, 5),
            Aproximar(_alvo.Z, _alvoDesejado.Z, dt, 5));
        _distancia = Aproximar(_distancia, _distanciaDesejada, dt, 5);
        _rumo = Aproximar(_rumo, _rumoDesejado, dt, 5);
        _elevacao = Aproximar(_elevacao, _elevacaoDesejada, dt, 5);
        AplicarCamera();
    }

    private void AplicarCamera()
    {
        var rumo = _rumo * Graus;
        var elevacao = _elevacao * Graus;
        var direcao = new Ponto3(Math.Cos(elevacao) * Math.Sin(rumo), Math.Sin(elevacao), Math.Cos(elevacao) * Math.Cos(rumo));
        var posicao = _alvo + (direcao * _distancia);

        _camera.Position = Ponto(posicao);
        _camera.LookDirection = Vetor(-direcao * _distancia);
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    // Movimento curto e sem quique: aproximação exponencial, independente da taxa de quadros.
    private static double Aproximar(double atual, double alvo, double dt, double rapidez)
    {
        var resto = alvo - atual;
        return Math.Abs(resto) < 1e-3 ? alvo : atual + (resto * (1 - Math.Exp(-rapidez * dt)));
    }

    private static double NormalizarAngulo(double graus)
    {
        var a = graus % 360;
        return a switch
        {
            > 180 => a - 360,
            < -180 => a + 360,
            _ => a,
        };
    }

    // ------------------------------------------------------------------ mouse e teclado

    private void AoApertar(object sender, MouseButtonEventArgs e)
    {
        Palco.Focus();

        if (e.ClickCount == 2 && Escolher(e.GetPosition(Vista3D)) is { } peca)
        {
            Focar(peca);
            e.Handled = true;
            return;
        }

        _inicioDoArrasto = e.GetPosition(Palco);
        _arrastando = true;
        _moveuNoArrasto = false;
        Palco.CaptureMouse();
    }

    private void AoMover(object sender, MouseEventArgs e)
    {
        var ponto = e.GetPosition(Palco);

        if (_arrastando)
        {
            var delta = ponto - _inicioDoArrasto;
            if (delta.Length > 3)
            {
                _moveuNoArrasto = true;
            }

            _inicioDoArrasto = ponto;
            _rumoDesejado -= delta.X * 0.4;
            _elevacaoDesejada = Math.Clamp(_elevacaoDesejada + (delta.Y * 0.3), -5, 85);
            return;
        }

        _vm?.Apontar(PecaSob(e.GetPosition(Vista3D)));
        Etiqueta.Margin = new Thickness(ponto.X + 14, ponto.Y + 10, 0, 0);
    }

    private void AoSoltar(object sender, MouseButtonEventArgs e)
    {
        if (!_arrastando)
        {
            return;
        }

        _arrastando = false;
        Palco.ReleaseMouseCapture();

        if (!_moveuNoArrasto)
        {
            Escolher(e.GetPosition(Vista3D));
        }
    }

    private void AoRolar(object sender, MouseWheelEventArgs e)
    {
        _distanciaDesejada = Math.Clamp(_distanciaDesejada * Math.Pow(0.9, e.Delta / 120.0), 400, 7000);
        e.Handled = true;
    }

    // Setas giram, + e - aproximam: o desenho também anda sem mouse.
    private void AoTeclar(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _rumoDesejado += 15;
                break;
            case Key.Right:
                _rumoDesejado -= 15;
                break;
            case Key.Up:
                _elevacaoDesejada = Math.Clamp(_elevacaoDesejada + 10, -5, 85);
                break;
            case Key.Down:
                _elevacaoDesejada = Math.Clamp(_elevacaoDesejada - 10, -5, 85);
                break;
            case Key.Add or Key.OemPlus:
                _distanciaDesejada = Math.Clamp(_distanciaDesejada * 0.85, 400, 7000);
                break;
            case Key.Subtract or Key.OemMinus:
                _distanciaDesejada = Math.Clamp(_distanciaDesejada / 0.85, 400, 7000);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private PecaDaCatraca? Escolher(Point ponto)
    {
        var peca = PecaSob(ponto);
        if (peca is { } p)
        {
            _vm?.Escolher(p);
        }

        return peca;
    }

    /// <summary>A peça sob um ponto da tela, pelo raio que sai da câmera (não por regiões fixas).</summary>
    private PecaDaCatraca? PecaSob(Point ponto)
    {
        PecaDaCatraca? achada = null;

        VisualTreeHelper.HitTest(
            Vista3D,
            null,
            resultado =>
            {
                if (resultado is RayMeshGeometry3DHitTestResult malha
                    && malha.ModelHit is GeometryModel3D modelo
                    && _pecaDoModelo.TryGetValue(modelo, out var peca))
                {
                    achada = peca;
                    return HitTestResultBehavior.Stop;
                }

                // Piso, sombra ou objeto de cena: segue procurando atrás dele.
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(ponto));

        return achada;
    }
}
