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

/// <summary>
/// Como a marca corporativa aparece (brand board, "Variações e aplicações da marca RAYZER X").
/// </summary>
public enum VarianteDoLogo
{
    /// <summary>RAYZER + X numa linha (com o descritor, se <see cref="RayzerLogo.Descritor"/>).</summary>
    Horizontal,

    /// <summary>X em cima, RAYZER e o descritor embaixo.</summary>
    Vertical,

    /// <summary>Só o X: barra recolhida, favicon, espaços mínimos.</summary>
    Simbolo,
}

/// <summary>Como o produto aparece (brand board, "Variações e aplicações do produto XAcess").</summary>
public enum VarianteDoProduto
{
    /// <summary>Xacess com a tagline "CONTROLE DE ACESSO INTELIGENTE".</summary>
    Principal,

    /// <summary>Xacess com o endosso "by RAYZER X".</summary>
    ComAssinatura,

    /// <summary>Só Xacess: cabeçalhos estreitos.</summary>
    Compacto,

    /// <summary>O ícone do produto (quadro arredondado com o X).</summary>
    Icone,
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
/// A marca corporativa RAYZER X — Rayzer Serviços e Tecnologia LTDA. Letreiro em vetor
/// (Rayzer.Letreiro.*, gerado por tools/gerar-marca.py); a altura do controle define o
/// tamanho e a largura acompanha a proporção. Não é o produto: para o XAcess use
/// <see cref="XAcessLogo"/>.
/// </summary>
public class RayzerLogo : Control
{
    public static readonly DependencyProperty VarianteProperty = DependencyProperty.Register(
        nameof(Variante), typeof(VarianteDoLogo), typeof(RayzerLogo), new PropertyMetadata(VarianteDoLogo.Horizontal));

    public static readonly DependencyProperty DescritorProperty = DependencyProperty.Register(
        nameof(Descritor), typeof(bool), typeof(RayzerLogo), new PropertyMetadata(true));

    public static readonly DependencyProperty CorDeApoioProperty = DependencyProperty.Register(
        nameof(CorDeApoio), typeof(Brush), typeof(RayzerLogo), new PropertyMetadata(null));

    public static readonly DependencyProperty BrilhoProperty = DependencyProperty.Register(
        nameof(Brilho), typeof(bool), typeof(RayzerLogo), new PropertyMetadata(false));

    public static readonly DependencyProperty MonocromaticoProperty = DependencyProperty.Register(
        nameof(Monocromatico), typeof(bool), typeof(RayzerLogo), new PropertyMetadata(false));

    static RayzerLogo() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerLogo), new FrameworkPropertyMetadata(typeof(RayzerLogo)));

    public VarianteDoLogo Variante { get => (VarianteDoLogo)GetValue(VarianteProperty); set => SetValue(VarianteProperty, value); }

    /// <summary>Mostra "SERVIÇOS E TECNOLOGIA LTDA" sob o RAYZER (só a partir de ~40 px de altura).</summary>
    public bool Descritor { get => (bool)GetValue(DescritorProperty); set => SetValue(DescritorProperty, value); }

    /// <summary>Cor do descritor.</summary>
    public Brush? CorDeApoio { get => (Brush?)GetValue(CorDeApoioProperty); set => SetValue(CorDeApoioProperty, value); }

    public bool Brilho { get => (bool)GetValue(BrilhoProperty); set => SetValue(BrilhoProperty, value); }

    public bool Monocromatico { get => (bool)GetValue(MonocromaticoProperty); set => SetValue(MonocromaticoProperty, value); }
}

/// <summary>
/// O produto XAcess — controle de acesso inteligente, by RAYZER X. O X da marca abre o nome
/// ("Xacess"); a altura do controle define o tamanho.
/// </summary>
public class XAcessLogo : Control
{
    public static readonly DependencyProperty VarianteProperty = DependencyProperty.Register(
        nameof(Variante), typeof(VarianteDoProduto), typeof(XAcessLogo), new PropertyMetadata(VarianteDoProduto.Principal));

    public static readonly DependencyProperty CorDeApoioProperty = DependencyProperty.Register(
        nameof(CorDeApoio), typeof(Brush), typeof(XAcessLogo), new PropertyMetadata(null));

    public static readonly DependencyProperty BrilhoProperty = DependencyProperty.Register(
        nameof(Brilho), typeof(bool), typeof(XAcessLogo), new PropertyMetadata(false));

    public static readonly DependencyProperty MonocromaticoProperty = DependencyProperty.Register(
        nameof(Monocromatico), typeof(bool), typeof(XAcessLogo), new PropertyMetadata(false));

    static XAcessLogo() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(XAcessLogo), new FrameworkPropertyMetadata(typeof(XAcessLogo)));

    public VarianteDoProduto Variante { get => (VarianteDoProduto)GetValue(VarianteProperty); set => SetValue(VarianteProperty, value); }

    /// <summary>Cor da tagline e do "by".</summary>
    public Brush? CorDeApoio { get => (Brush?)GetValue(CorDeApoioProperty); set => SetValue(CorDeApoioProperty, value); }

    public bool Brilho { get => (bool)GetValue(BrilhoProperty); set => SetValue(BrilhoProperty, value); }

    public bool Monocromatico { get => (bool)GetValue(MonocromaticoProperty); set => SetValue(MonocromaticoProperty, value); }
}

/// <summary>
/// Ícone de aplicativo (brand board, "Ícone e favicon"): quadro arredondado em azul profundo,
/// borda Azul Principal e o X; com <see cref="ComNome"/>, o "acess" do produto embaixo.
/// A largura define o tamanho.
/// </summary>
public class RayzerAppIcon : Control
{
    public static readonly DependencyProperty ComNomeProperty = DependencyProperty.Register(
        nameof(ComNome), typeof(bool), typeof(RayzerAppIcon), new PropertyMetadata(false));

    static RayzerAppIcon() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerAppIcon), new FrameworkPropertyMetadata(typeof(RayzerAppIcon)));

    public bool ComNome { get => (bool)GetValue(ComNomeProperty); set => SetValue(ComNomeProperty, value); }
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

/// <summary>
/// Abertura (brand board, "Animação de inicialização"): fluxos se aproximam (0–0,6 s),
/// interseção e pulso (0,6–1,4 s), o X se completa (1,4–1,8 s), marca e produto
/// (1,8–2,2 s); some sozinha em 2,9 s. Nunca bloqueia: não recebe clique nem foco, e com as
/// animações do Windows desligadas (movimento reduzido) nem aparece.
/// </summary>
public class RayzerAbertura : Control
{
    /// <summary>Tempo total, do primeiro quadro até sumir.</summary>
    public static readonly TimeSpan Duracao = TimeSpan.FromSeconds(2.9);

    static RayzerAbertura() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RayzerAbertura), new FrameworkPropertyMetadata(typeof(RayzerAbertura)));

    /// <summary>Não mostra a abertura (captura de telas, testes).</summary>
    public static bool Desligada { get; set; }

    /// <summary>Para a animação no quadro final, visível (captura da própria abertura).</summary>
    public static bool SomenteQuadroFinal { get; set; }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (!SomenteQuadroFinal && (Desligada || !SystemParameters.ClientAreaAnimation))
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        if (Template?.Resources["Sequencia"] is not Storyboard modelo)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var sequencia = modelo.Clone();
        if (SomenteQuadroFinal)
        {
            sequencia.Begin(this, Template, isControllable: true);
            sequencia.SeekAlignedToLastTick(this, TimeSpan.FromSeconds(2.5), TimeSeekOrigin.BeginTime);
            sequencia.Pause(this);
            return;
        }

        sequencia.Completed += (_, _) => Visibility = Visibility.Collapsed;
        sequencia.Begin(this, Template);
    }
}
