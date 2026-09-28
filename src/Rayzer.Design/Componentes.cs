using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

namespace Rayzer.Design;

/// <summary>O tom semântico de uma situação. A cor sai do tema; o texto sai de quem usa.</summary>
public enum Tom
{
    /// <summary>Parado, desligado, sem dado.</summary>
    Neutro,

    /// <summary>Autorizado, online, sincronizado.</summary>
    Sucesso,

    /// <summary>Precisa de atenção, ainda sem parar a operação.</summary>
    Atencao,

    /// <summary>Negado, offline, exige ação.</summary>
    Perigo,

    /// <summary>Informação, sincronizando.</summary>
    Info,

    /// <summary>Cor da marca.</summary>
    Marca,
}

/// <summary>
/// RayzerStatus: situação sempre como símbolo + texto + cor — nunca só cor.
/// </summary>
/// <remarks>
/// Símbolos padrão por tom: ✓ sucesso · × perigo · ! atenção · ↻ info · ● marca · ○ neutro.
/// Quem sabe mais (catraca online é ●, não ✓) informa <see cref="Glifo"/>.
/// </remarks>
public class RayzerStatus : Control
{
    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(Tom), typeof(RayzerStatus), new PropertyMetadata(Tom.Neutro, AtualizarGlifo));

    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(RayzerStatus), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlifoProperty = DependencyProperty.Register(
        nameof(Glifo), typeof(string), typeof(RayzerStatus), new PropertyMetadata(null, AtualizarGlifo));

    /// <summary>Com fundo tingido, em forma de pílula.</summary>
    public static readonly DependencyProperty PilulaProperty = DependencyProperty.Register(
        nameof(Pilula), typeof(bool), typeof(RayzerStatus), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey GlifoEfetivoKey = DependencyProperty.RegisterReadOnly(
        nameof(GlifoEfetivo), typeof(string), typeof(RayzerStatus), new PropertyMetadata("○"));

    public static readonly DependencyProperty GlifoEfetivoProperty = GlifoEfetivoKey.DependencyProperty;

    static RayzerStatus() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerStatus), new FrameworkPropertyMetadata(typeof(RayzerStatus)));

    public Tom Tom { get => (Tom)GetValue(TomProperty); set => SetValue(TomProperty, value); }

    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }

    public string? Glifo { get => (string?)GetValue(GlifoProperty); set => SetValue(GlifoProperty, value); }

    public bool Pilula { get => (bool)GetValue(PilulaProperty); set => SetValue(PilulaProperty, value); }

    public string GlifoEfetivo => (string)GetValue(GlifoEfetivoProperty);

    /// <summary>Símbolo padrão de cada tom.</summary>
    public static string GlifoPadrao(Tom tom) => tom switch
    {
        Tom.Sucesso => "✓",
        Tom.Perigo => "×",
        Tom.Atencao => "!",
        Tom.Info => "↻",
        Tom.Marca => "●",
        _ => "○",
    };

    private static void AtualizarGlifo(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var status = (RayzerStatus)d;
        status.SetValue(GlifoEfetivoKey, string.IsNullOrEmpty(status.Glifo) ? GlifoPadrao(status.Tom) : status.Glifo);
    }
}

/// <summary>RayzerMetricCard: um número do evento, com rótulo, ícone e contexto.</summary>
public class RayzerMetricCard : Control
{
    public static readonly DependencyProperty RotuloProperty = DependencyProperty.Register(
        nameof(Rotulo), typeof(string), typeof(RayzerMetricCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValorProperty = DependencyProperty.Register(
        nameof(Valor), typeof(object), typeof(RayzerMetricCard), new PropertyMetadata(null));

    public static readonly DependencyProperty DetalheProperty = DependencyProperty.Register(
        nameof(Detalhe), typeof(string), typeof(RayzerMetricCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(
        nameof(Icone), typeof(string), typeof(RayzerMetricCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(Tom), typeof(RayzerMetricCard), new PropertyMetadata(Tom.Neutro));

    static RayzerMetricCard() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerMetricCard), new FrameworkPropertyMetadata(typeof(RayzerMetricCard)));

    public string Rotulo { get => (string)GetValue(RotuloProperty); set => SetValue(RotuloProperty, value); }

    public object? Valor { get => GetValue(ValorProperty); set => SetValue(ValorProperty, value); }

    public string Detalhe { get => (string)GetValue(DetalheProperty); set => SetValue(DetalheProperty, value); }

    public string Icone { get => (string)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }

    public Tom Tom { get => (Tom)GetValue(TomProperty); set => SetValue(TomProperty, value); }
}

/// <summary>
/// RayzerAlert: STATUS → EXCEÇÃO → CONTEXTO → AÇÃO. O título diz o que aconteceu, o texto
/// dá o contexto, e o conteúdo (botões) é a ação.
/// </summary>
public class RayzerAlert : ContentControl
{
    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(Tom), typeof(RayzerAlert), new PropertyMetadata(Tom.Info, AtualizarGlifo));

    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(RayzerAlert), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(RayzerAlert), new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey GlifoKey = DependencyProperty.RegisterReadOnly(
        nameof(Glifo), typeof(string), typeof(RayzerAlert), new PropertyMetadata("↻"));

    public static readonly DependencyProperty GlifoProperty = GlifoKey.DependencyProperty;

    static RayzerAlert() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerAlert), new FrameworkPropertyMetadata(typeof(RayzerAlert)));

    public Tom Tom { get => (Tom)GetValue(TomProperty); set => SetValue(TomProperty, value); }

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }

    public string Glifo => (string)GetValue(GlifoProperty);

    private static void AtualizarGlifo(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.SetValue(GlifoKey, RayzerStatus.GlifoPadrao((Tom)e.NewValue));
}

/// <summary>
/// RayzerEmptyState: a lista vazia explica por que está vazia. O sistema nunca parece
/// quebrado só porque ainda não há dado.
/// </summary>
public class RayzerEmptyState : Control
{
    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(
        nameof(Icone), typeof(string), typeof(RayzerEmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(RayzerEmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextoProperty = DependencyProperty.Register(
        nameof(Texto), typeof(string), typeof(RayzerEmptyState), new PropertyMetadata(string.Empty));

    static RayzerEmptyState() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerEmptyState), new FrameworkPropertyMetadata(typeof(RayzerEmptyState)));

    public string Icone { get => (string)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Texto { get => (string)GetValue(TextoProperty); set => SetValue(TextoProperty, value); }
}

/// <summary>Como a marca aparece.</summary>
public enum VarianteDoLogo
{
    /// <summary>Símbolo, empresa e produto lado a lado.</summary>
    Horizontal,

    /// <summary>Só o símbolo (barra recolhida, ícone).</summary>
    Simbolo,
}

/// <summary>
/// RayzerLogo: o símbolo Rayzer (um X em que o fluxo atravessa a passagem) com o nome da
/// empresa e do produto. Serve a qualquer produto Rayzer: muda só <see cref="Produto"/>.
/// </summary>
public class RayzerLogo : Control
{
    public static readonly DependencyProperty ProdutoProperty = DependencyProperty.Register(
        nameof(Produto), typeof(string), typeof(RayzerLogo), new PropertyMetadata(Marca.Produto));

    public static readonly DependencyProperty EmpresaProperty = DependencyProperty.Register(
        nameof(Empresa), typeof(string), typeof(RayzerLogo), new PropertyMetadata("RAYZER"));

    public static readonly DependencyProperty VarianteProperty = DependencyProperty.Register(
        nameof(Variante), typeof(VarianteDoLogo), typeof(RayzerLogo), new PropertyMetadata(VarianteDoLogo.Horizontal));

    public static readonly DependencyProperty TamanhoDoSimboloProperty = DependencyProperty.Register(
        nameof(TamanhoDoSimbolo), typeof(double), typeof(RayzerLogo), new PropertyMetadata(32.0));

    static RayzerLogo() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerLogo), new FrameworkPropertyMetadata(typeof(RayzerLogo)));

    public string Produto { get => (string)GetValue(ProdutoProperty); set => SetValue(ProdutoProperty, value); }

    public string Empresa { get => (string)GetValue(EmpresaProperty); set => SetValue(EmpresaProperty, value); }

    public VarianteDoLogo Variante { get => (VarianteDoLogo)GetValue(VarianteProperty); set => SetValue(VarianteProperty, value); }

    public double TamanhoDoSimbolo { get => (double)GetValue(TamanhoDoSimboloProperty); set => SetValue(TamanhoDoSimboloProperty, value); }
}

/// <summary>
/// RayzerFlowBar: a barra de carregamento. Um traço atravessa a linha — o movimento da
/// marca é passagem, não giro. Some quando não há nada carregando, e fica parada (cheia,
/// discreta) quando o Windows pede menos animação.
/// </summary>
public class RayzerFlowBar : Control
{
    public static readonly DependencyProperty AtivaProperty = DependencyProperty.Register(
        nameof(Ativa), typeof(bool), typeof(RayzerFlowBar), new PropertyMetadata(false, (d, _) => ((RayzerFlowBar)d).Atualizar()));

    private FrameworkElement? _traco;
    private TranslateTransform? _deslocamento;

    static RayzerFlowBar() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerFlowBar), new FrameworkPropertyMetadata(typeof(RayzerFlowBar)));

    public RayzerFlowBar() => SizeChanged += (_, _) => Atualizar();

    public bool Ativa { get => (bool)GetValue(AtivaProperty); set => SetValue(AtivaProperty, value); }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _traco = GetTemplateChild("PART_Traco") as FrameworkElement;
        _deslocamento = new TranslateTransform();

        if (_traco is not null)
        {
            _traco.RenderTransform = _deslocamento;
        }

        Atualizar();
    }

    private void Atualizar()
    {
        if (_traco is null || _deslocamento is null)
        {
            return;
        }

        _deslocamento.BeginAnimation(TranslateTransform.XProperty, null);
        _traco.Visibility = Ativa ? Visibility.Visible : Visibility.Hidden;

        if (!Ativa || ActualWidth <= 0)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            _traco.Width = ActualWidth;
            _deslocamento.X = 0;
            return;
        }

        _traco.Width = ActualWidth * 0.3;
        var travessia = new DoubleAnimation(-_traco.Width, ActualWidth, new Duration(TimeSpan.FromSeconds(1.4)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        _deslocamento.BeginAnimation(TranslateTransform.XProperty, travessia);
    }
}

/// <summary>A arquitetura de marca: empresa corporativa e produto.</summary>
public static class Marca
{
    /// <summary>Marca corporativa.</summary>
    public const string Empresa = "Rayzer";

    /// <summary>Razão social.</summary>
    public const string RazaoSocial = "Rayzer Serviços e Tecnologia LTDA";

    /// <summary>Produto: controle de acesso.</summary>
    public const string Produto = "XAcess";

    /// <summary>Nome completo do produto, como aparece em títulos e no instalador.</summary>
    public const string NomeDoProduto = Empresa + " " + Produto;

    /// <summary>Assinatura curta de autoria.</summary>
    public const string Autoria = "Desenvolvido sob demanda por " + RazaoSocial;

    /// <summary>O princípio que orienta a experiência.</summary>
    public const string Principio = "Access is a flow, not a door.";
}
