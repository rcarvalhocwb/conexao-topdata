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
public class RayzerEmptyState : ContentControl
{
    /// <summary>Observação complementar, abaixo do texto.</summary>
    public static readonly DependencyProperty ObservacaoProperty = DependencyProperty.Register(
        nameof(Observacao), typeof(string), typeof(RayzerEmptyState), new PropertyMetadata(string.Empty));

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

    public string Observacao { get => (string)GetValue(ObservacaoProperty); set => SetValue(ObservacaoProperty, value); }
}

/// <summary>Como a marca aparece (brand board, "logo principal" e variações).</summary>
public enum VarianteDoLogo
{
    /// <summary>Símbolo + RAYZER X acess numa linha: barra lateral, cabeçalhos.</summary>
    Horizontal,

    /// <summary>Horizontal com a assinatura "CONTROLE DE ACESSO INTELIGENTE": telas de entrada.</summary>
    Completo,

    /// <summary>Só o símbolo: barra recolhida, ícone.</summary>
    Simbolo,

    /// <summary>Símbolo + "XAcess" em duas linhas curtas: espaços estreitos.</summary>
    Compacto,
}

/// <summary>
/// RayzerBrandMark: o X da marca. Duas faixas que se cruzam, com gradiente, realce vítreo
/// no alto e, na variante <see cref="Brilho"/>, o glow azul controlado.
/// </summary>
public class RayzerBrandMark : Control
{
    public static readonly DependencyProperty TamanhoProperty = DependencyProperty.Register(
        nameof(Tamanho), typeof(double), typeof(RayzerBrandMark), new PropertyMetadata(32.0));

    public static readonly DependencyProperty BrilhoProperty = DependencyProperty.Register(
        nameof(Brilho), typeof(bool), typeof(RayzerBrandMark), new PropertyMetadata(false));

    /// <summary>Uma cor só (a do Foreground): fundo claro, impressão, marca-d'água.</summary>
    public static readonly DependencyProperty MonocromaticoProperty = DependencyProperty.Register(
        nameof(Monocromatico), typeof(bool), typeof(RayzerBrandMark), new PropertyMetadata(false));

    static RayzerBrandMark() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerBrandMark), new FrameworkPropertyMetadata(typeof(RayzerBrandMark)));

    public double Tamanho { get => (double)GetValue(TamanhoProperty); set => SetValue(TamanhoProperty, value); }

    public bool Brilho { get => (bool)GetValue(BrilhoProperty); set => SetValue(BrilhoProperty, value); }

    public bool Monocromatico { get => (bool)GetValue(MonocromaticoProperty); set => SetValue(MonocromaticoProperty, value); }
}

/// <summary>
/// RayzerLogo: o símbolo com "RAYZER X acess". Serve a qualquer produto Rayzer: muda só
/// <see cref="Produto"/> (o texto depois do X).
/// </summary>
/// <remarks>
/// Área de respiro: metade da altura do símbolo em volta. Tamanho mínimo: símbolo de 16 px
/// (só símbolo) e de 24 px (horizontal).
/// </remarks>
public class RayzerLogo : Control
{
    public static readonly DependencyProperty ProdutoProperty = DependencyProperty.Register(
        nameof(Produto), typeof(string), typeof(RayzerLogo), new PropertyMetadata("acess"));

    public static readonly DependencyProperty EmpresaProperty = DependencyProperty.Register(
        nameof(Empresa), typeof(string), typeof(RayzerLogo), new PropertyMetadata("RAYZER"));

    public static readonly DependencyProperty AssinaturaProperty = DependencyProperty.Register(
        nameof(Assinatura), typeof(string), typeof(RayzerLogo), new PropertyMetadata("CONTROLE DE ACESSO INTELIGENTE"));

    public static readonly DependencyProperty VarianteProperty = DependencyProperty.Register(
        nameof(Variante), typeof(VarianteDoLogo), typeof(RayzerLogo), new PropertyMetadata(VarianteDoLogo.Horizontal));

    public static readonly DependencyProperty TamanhoDoSimboloProperty = DependencyProperty.Register(
        nameof(TamanhoDoSimbolo), typeof(double), typeof(RayzerLogo), new PropertyMetadata(32.0));

    public static readonly DependencyProperty BrilhoProperty = DependencyProperty.Register(
        nameof(Brilho), typeof(bool), typeof(RayzerLogo), new PropertyMetadata(false));

    public static readonly DependencyProperty MonocromaticoProperty = DependencyProperty.Register(
        nameof(Monocromatico), typeof(bool), typeof(RayzerLogo), new PropertyMetadata(false));

    static RayzerLogo() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerLogo), new FrameworkPropertyMetadata(typeof(RayzerLogo)));

    public string Produto { get => (string)GetValue(ProdutoProperty); set => SetValue(ProdutoProperty, value); }

    public string Empresa { get => (string)GetValue(EmpresaProperty); set => SetValue(EmpresaProperty, value); }

    public string Assinatura { get => (string)GetValue(AssinaturaProperty); set => SetValue(AssinaturaProperty, value); }

    public VarianteDoLogo Variante { get => (VarianteDoLogo)GetValue(VarianteProperty); set => SetValue(VarianteProperty, value); }

    public double TamanhoDoSimbolo { get => (double)GetValue(TamanhoDoSimboloProperty); set => SetValue(TamanhoDoSimboloProperty, value); }

    public bool Brilho { get => (bool)GetValue(BrilhoProperty); set => SetValue(BrilhoProperty, value); }

    public bool Monocromatico { get => (bool)GetValue(MonocromaticoProperty); set => SetValue(MonocromaticoProperty, value); }
}

/// <summary>StatusPill: RayzerStatus já em forma de pílula (Operacional, Online, Simulação…).</summary>
public class RayzerStatusPill : RayzerStatus
{
    static RayzerStatusPill()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerStatusPill), new FrameworkPropertyMetadata(typeof(RayzerStatusPill)));
        PilulaProperty.OverrideMetadata(typeof(RayzerStatusPill), new PropertyMetadata(true));
    }
}

/// <summary>
/// StatusCard: bloco da barra operacional — ícone tingido, título e descrição curta, com a
/// linha de energia na cor da situação.
/// </summary>
public class RayzerStatusCard : Control
{
    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(
        nameof(Icone), typeof(string), typeof(RayzerStatusCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(RayzerStatusCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescricaoProperty = DependencyProperty.Register(
        nameof(Descricao), typeof(string), typeof(RayzerStatusCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(Tom), typeof(RayzerStatusCard), new PropertyMetadata(Tom.Neutro));

    static RayzerStatusCard() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerStatusCard), new FrameworkPropertyMetadata(typeof(RayzerStatusCard)));

    public string Icone { get => (string)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Descricao { get => (string)GetValue(DescricaoProperty); set => SetValue(DescricaoProperty, value); }

    public Tom Tom { get => (Tom)GetValue(TomProperty); set => SetValue(TomProperty, value); }
}

/// <summary>PageHeader: título da página, descrição curta e ação opcional à direita (o conteúdo).</summary>
public class RayzerPageHeader : ContentControl
{
    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(RayzerPageHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescricaoProperty = DependencyProperty.Register(
        nameof(Descricao), typeof(string), typeof(RayzerPageHeader), new PropertyMetadata(string.Empty));

    static RayzerPageHeader() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerPageHeader), new FrameworkPropertyMetadata(typeof(RayzerPageHeader)));

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Descricao { get => (string)GetValue(DescricaoProperty); set => SetValue(DescricaoProperty, value); }
}

/// <summary>SectionHeader: título de seção ("Catracas", "Acessos em tempo real"), subtítulo e ação.</summary>
public class RayzerSectionHeader : ContentControl
{
    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(RayzerSectionHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtituloProperty = DependencyProperty.Register(
        nameof(Subtitulo), typeof(string), typeof(RayzerSectionHeader), new PropertyMetadata(string.Empty));

    static RayzerSectionHeader() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerSectionHeader), new FrameworkPropertyMetadata(typeof(RayzerSectionHeader)));

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Subtitulo { get => (string)GetValue(SubtituloProperty); set => SetValue(SubtituloProperty, value); }
}

/// <summary>
/// DataPanel: cartão de dados com cabeçalho (título, subtítulo), barra de ferramentas
/// opcional e o conteúdo (normalmente uma DataTable e o seu EmptyState).
/// </summary>
public class RayzerDataPanel : ContentControl
{
    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(
        nameof(Titulo), typeof(string), typeof(RayzerDataPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtituloProperty = DependencyProperty.Register(
        nameof(Subtitulo), typeof(string), typeof(RayzerDataPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FerramentasProperty = DependencyProperty.Register(
        nameof(Ferramentas), typeof(object), typeof(RayzerDataPanel), new PropertyMetadata(null));

    static RayzerDataPanel() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerDataPanel), new FrameworkPropertyMetadata(typeof(RayzerDataPanel)));

    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }

    public string Subtitulo { get => (string)GetValue(SubtituloProperty); set => SetValue(SubtituloProperty, value); }

    public object? Ferramentas { get => GetValue(FerramentasProperty); set => SetValue(FerramentasProperty, value); }
}

/// <summary>MetaItem: ícone, rótulo e valor — a linha técnica do cartão de catraca.</summary>
public class RayzerMetaItem : Control
{
    public static readonly DependencyProperty IconeProperty = DependencyProperty.Register(
        nameof(Icone), typeof(string), typeof(RayzerMetaItem), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RotuloProperty = DependencyProperty.Register(
        nameof(Rotulo), typeof(string), typeof(RayzerMetaItem), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValorProperty = DependencyProperty.Register(
        nameof(Valor), typeof(object), typeof(RayzerMetaItem), new PropertyMetadata(null));

    static RayzerMetaItem() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerMetaItem), new FrameworkPropertyMetadata(typeof(RayzerMetaItem)));

    public string Icone { get => (string)GetValue(IconeProperty); set => SetValue(IconeProperty, value); }

    public string Rotulo { get => (string)GetValue(RotuloProperty); set => SetValue(RotuloProperty, value); }

    public object? Valor { get => GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
}

/// <summary>
/// A miniatura da catraca (tripé com leitor), desenhada em vetor. A luz do leitor acende na
/// cor da situação. É ilustração, não foto do modelo.
/// </summary>
public class RayzerTurnstileGlyph : Control
{
    public static readonly DependencyProperty TomProperty = DependencyProperty.Register(
        nameof(Tom), typeof(Tom), typeof(RayzerTurnstileGlyph), new PropertyMetadata(Tom.Neutro));

    static RayzerTurnstileGlyph() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerTurnstileGlyph), new FrameworkPropertyMetadata(typeof(RayzerTurnstileGlyph)));

    public Tom Tom { get => (Tom)GetValue(TomProperty); set => SetValue(TomProperty, value); }
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
