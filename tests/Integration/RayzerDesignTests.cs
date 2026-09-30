using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Integration.Tests;

/// <summary>
/// O Rayzer Design System como contrato: temas completos, contraste WCAG 2.2 AA, nenhuma
/// chave inexistente e nenhuma cor solta nas telas.
/// </summary>
/// <remarks>
/// Recurso dinâmico com nome errado não é erro de compilação nem de execução no WPF: a
/// cor simplesmente não aparece. Estes testes são a rede para isso.
/// </remarks>
public sealed partial class RayzerDesignTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    public static TheoryData<string> Temas() => ["Claro", "Escuro"];

    [Fact]
    public void Os_tres_temas_definem_exatamente_as_mesmas_chaves()
    {
        var claro = Chaves(Arquivo("src/Rayzer.Design/Temas/Claro.xaml"));
        var escuro = Chaves(Arquivo("src/Rayzer.Design/Temas/Escuro.xaml"));
        var contraste = Chaves(Arquivo("src/Rayzer.Design/Temas/AltoContraste.xaml"));

        Assert.NotEmpty(claro);
        Assert.Equal(claro.Order(StringComparer.Ordinal), escuro.Order(StringComparer.Ordinal));
        Assert.Equal(claro.Order(StringComparer.Ordinal), contraste.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A mesma chave em dois dicionários faz o último vencer: "Rayzer.Text.Secondary" era
    /// cor no tema e estilo nos controles, e o WPF entregou o estilo onde esperava a cor —
    /// o painel caiu ao abrir. Achado pelo autoteste do CI.
    /// </summary>
    [Fact]
    public void Nenhuma_chave_se_repete_entre_os_dicionarios()
    {
        var vistas = new Dictionary<string, string>(StringComparer.Ordinal);
        var repetidas = new List<string>();

        foreach (var dicionario in new[] { "Tokens.xaml", "Temas/Claro.xaml", "Controles.xaml", "Themes/Generic.xaml" })
        {
            foreach (var chave in Chaves(Arquivo($"src/Rayzer.Design/{dicionario}")))
            {
                if (!vistas.TryAdd(chave, dicionario))
                {
                    repetidas.Add($"{chave} ({vistas[chave]} e {dicionario})");
                }
            }
        }

        Assert.True(repetidas.Count == 0, $"Chaves repetidas: {string.Join(", ", repetidas)}");
    }

    [Fact]
    public void Os_tokens_semanticos_pedidos_pela_marca_existem()
    {
        var chaves = Chaves(Arquivo("src/Rayzer.Design/Temas/Claro.xaml"));

        foreach (var token in new[]
        {
            "Rayzer.Brand.Primary", "Rayzer.Brand.Secondary", "Rayzer.Brand.Accent", "Rayzer.Background",
            "Rayzer.Surface", "Rayzer.Surface.Elevated", "Rayzer.Border", "Rayzer.Text.Primary", "Rayzer.Text.Secondary",
            "Rayzer.Success", "Rayzer.Warning", "Rayzer.Danger", "Rayzer.Info", "Rayzer.Access.Granted",
            "Rayzer.Access.Denied", "Rayzer.Device.Online", "Rayzer.Device.Offline", "Rayzer.Device.Warning",
            "Rayzer.Syncing",
        })
        {
            Assert.Contains(token, chaves);
        }

        var tokens = Chaves(Arquivo("src/Rayzer.Design/Tokens.xaml"));
        foreach (var token in new[]
        {
            "Rayzer.Radius.Sm", "Rayzer.Radius.Md", "Rayzer.Radius.Lg", "Rayzer.Shadow.Sm", "Rayzer.Shadow.Md",
            "Rayzer.Shadow.Lg", "Rayzer.Space.4", "Rayzer.FontSize.Body", "Rayzer.Motion.Fast", "Rayzer.Breakpoint.Compact",
        })
        {
            Assert.Contains(token, tokens);
        }
    }

    [Theory]
    [MemberData(nameof(Temas))]
    public void Texto_e_situacao_passam_no_contraste_WCAG_AA(string tema)
    {
        var cor = Cores(Arquivo($"src/Rayzer.Design/Temas/{tema}.xaml"));
        // Hover incluído: a linha da tabela sob o mouse continua mostrando a situação.
        var superficies = new[] { "Rayzer.Background", "Rayzer.Surface", "Rayzer.Surface.Elevated", "Rayzer.Surface.Sunken", "Rayzer.Surface.Hover" };
        var falhas = new List<string>();

        void Exigir(string frente, string fundo, double minimo)
        {
            var razao = Contraste(cor[frente], cor[fundo]);
            if (razao < minimo)
            {
                falhas.Add(string.Create(CultureInfo.InvariantCulture, $"{frente} sobre {fundo}: {razao:F2} (mínimo {minimo})"));
            }
        }

        // Texto (4,5:1).
        foreach (var texto in new[]
        {
            "Rayzer.Text.Primary", "Rayzer.Text.Secondary", "Rayzer.Brand.Primary.Foreground", "Rayzer.Success",
            "Rayzer.Warning", "Rayzer.Danger", "Rayzer.Info", "Rayzer.Neutral", "Rayzer.Access.Granted",
            "Rayzer.Access.Denied", "Rayzer.Device.Online", "Rayzer.Device.Offline", "Rayzer.Device.Warning", "Rayzer.Syncing",
        })
        {
            foreach (var fundo in superficies)
            {
                Exigir(texto, fundo, 4.5);
            }
        }

        // Situação sobre o próprio fundo tingido (pílulas, alertas).
        foreach (var (frente, fundo) in new[]
        {
            ("Rayzer.Success", "Rayzer.Success.Subtle"), ("Rayzer.Warning", "Rayzer.Warning.Subtle"),
            ("Rayzer.Danger", "Rayzer.Danger.Subtle"), ("Rayzer.Info", "Rayzer.Info.Subtle"),
            ("Rayzer.Neutral", "Rayzer.Neutral.Subtle"), ("Rayzer.Brand.Primary.Foreground", "Rayzer.Brand.Primary.Subtle"),
            ("Rayzer.Text.Primary", "Rayzer.Surface.Hover"), ("Rayzer.Text.Primary", "Rayzer.Surface.Selected"),
        })
        {
            Exigir(frente, fundo, 4.5);
        }

        // Botão principal e barra lateral.
        Exigir("Rayzer.Text.OnBrand", "Rayzer.Brand.Primary", 4.5);
        Exigir("Rayzer.Text.OnBrand", "Rayzer.Brand.Primary.Hover", 4.5);
        Exigir("Rayzer.Text.OnBrand", "Rayzer.Brand.Primary.Pressed", 4.5);
        Exigir("Rayzer.Nav.Text", "Rayzer.Nav.Background", 4.5);
        Exigir("Rayzer.Nav.Text", "Rayzer.Nav.Item.Hover", 4.5);
        Exigir("Rayzer.Nav.Text.Active", "Rayzer.Nav.Item.Active", 4.5);
        Exigir("Rayzer.Nav.Text.Muted", "Rayzer.Nav.Background", 4.5);

        // Componentes de interface (3:1): borda de campo, foco, indicador, símbolo da marca.
        Exigir("Rayzer.Input.Border", "Rayzer.Input.Background", 3);
        Exigir("Rayzer.Focus", "Rayzer.Surface", 3);
        Exigir("Rayzer.Focus", "Rayzer.Background", 3);
        Exigir("Rayzer.Nav.Indicator", "Rayzer.Nav.Item.Active", 3);
        Exigir("Rayzer.Logo.Flow", "Rayzer.Nav.Background", 3);
        Exigir("Rayzer.Logo.Gate", "Rayzer.Nav.Background", 3);

        Assert.True(falhas.Count == 0, $"Tema {tema}: {string.Join("; ", falhas)}");
    }

    /// <summary>
    /// Controle sem estilo Rayzer cai no visual padrão do Windows, que não conhece o tema: o
    /// "Demonstração / Ao vivo" do gêmeo saía preto sobre o fundo escuro e a lista de peças
    /// saía branca (docs/34 §7.1, P1 e P2). Todo tipo de controle interativo usado nas telas
    /// precisa de estilo implícito (sem chave) em Controles.xaml; tipo novo que o teste não
    /// conhece reprova até ser classificado aqui.
    /// </summary>
    [Fact]
    public void Todo_controle_interativo_das_telas_tem_estilo_Rayzer_implicito()
    {
        // Controles que o operador clica, digita ou seleciona.
        var interativos = new HashSet<string>(StringComparer.Ordinal)
        {
            "Button", "RepeatButton", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "PasswordBox", "RichTextBox",
            "ComboBox", "ComboBoxItem", "ListBox", "ListBoxItem", "ListView", "ListViewItem", "TreeView", "TreeViewItem",
            "DataGrid", "DataGridRow", "DataGridCell", "DataGridColumnHeader", "DatePicker", "DatePickerTextBox", "Calendar",
            "TabControl", "TabItem", "Slider", "ScrollBar", "Menu", "MenuItem", "ContextMenu", "Expander", "GridSplitter",
            "Hyperlink", "ToolTip",
        };

        // Painéis, texto, desenho e declarações: não têm estado de interação próprio.
        var naoInterativos = new HashSet<string>(StringComparer.Ordinal)
        {
            "UserControl", "Window", "Grid", "StackPanel", "DockPanel", "WrapPanel", "UniformGrid", "Canvas", "Border",
            "ScrollViewer", "ContentControl", "ContentPresenter", "ItemsControl", "TextBlock", "Run", "LineBreak", "Span",
            "Bold", "Italic", "Image", "Viewbox", "Viewport3D", "Rectangle", "Ellipse", "Path", "ColumnDefinition",
            "RowDefinition", "DataGridTextColumn", "DataGridTemplateColumn", "DataTemplate", "ItemsPanelTemplate",
            "ControlTemplate", "Style", "Setter", "Trigger", "DataTrigger", "MultiDataTrigger", "Condition", "KeyBinding",
            "ResourceDictionary",
        };

        // Quem usa um controle de itens usa também o contêiner que ele gera.
        var implicitos = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ListBox"] = ["ListBoxItem", "ScrollBar"],
            ["ComboBox"] = ["ComboBoxItem"],
            ["DataGrid"] = ["DataGridRow", "DataGridCell", "DataGridColumnHeader", "ScrollBar"],
            ["DatePicker"] = ["DatePickerTextBox"],
            ["TabControl"] = ["TabItem"],
            ["ScrollViewer"] = ["ScrollBar"],
        };

        var comEstilo = XDocument.Load(Arquivo("src/Rayzer.Design/Controles.xaml")).Root!.Elements()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is null)
            .Select(e => (string)e.Attribute("TargetType")!)
            .ToHashSet(StringComparer.Ordinal);

        var usados = new SortedSet<string>(StringComparer.Ordinal);
        var desconhecidos = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var xaml in Directory.EnumerateFiles(Raiz("src/Desktop.App/Telas"), "*.xaml").Append(Arquivo("src/Desktop.App/JanelaPrincipal.xaml")))
        {
            foreach (var elemento in XDocument.Load(xaml).Descendants().Where(e => e.Name.Namespace == Wpf && !e.Name.LocalName.Contains('.', StringComparison.Ordinal)))
            {
                var tipo = elemento.Name.LocalName;
                if (interativos.Contains(tipo))
                {
                    usados.Add(tipo);
                    usados.UnionWith(implicitos.GetValueOrDefault(tipo, []));
                }
                else if (naoInterativos.Contains(tipo))
                {
                    usados.UnionWith(implicitos.GetValueOrDefault(tipo, []));
                }
                else
                {
                    desconhecidos.Add($"{Path.GetFileName(xaml)}: {tipo}");
                }
            }
        }

        Assert.True(desconhecidos.Count == 0, $"Tipos que o teste não sabe classificar (interativo ou não): {string.Join(", ", desconhecidos)}");
        Assert.Contains("RadioButton", usados);
        Assert.Contains("ListBoxItem", usados);

        var semEstilo = usados.Where(t => !comEstilo.Contains(t)).ToList();
        Assert.True(semEstilo.Count == 0, $"Usados nas telas sem estilo Rayzer implícito: {string.Join(", ", semEstilo)} (usados: {string.Join(", ", usados)})");
    }

    /// <summary>
    /// O texto do botão de opção e dos itens da lista, nas cores que os próprios estilos usam
    /// (lidas de Controles.xaml), passa 4,5:1 nos dois temas: normal, sob o mouse e
    /// selecionado. O aro e o ponto do botão de opção passam 3:1 (componente de interface).
    /// </summary>
    [Theory]
    [MemberData(nameof(Temas))]
    public void RadioButton_e_lista_passam_no_contraste_WCAG_AA(string tema)
    {
        var cor = Cores(Arquivo($"src/Rayzer.Design/Temas/{tema}.xaml"));
        var controles = XDocument.Load(Arquivo("src/Rayzer.Design/Controles.xaml")).Root!;
        var falhas = new List<string>();

        void Exigir(string contexto, string frente, string fundo, double minimo)
        {
            var razao = Contraste(cor[frente], cor[fundo]);
            if (razao < minimo)
            {
                falhas.Add(string.Create(CultureInfo.InvariantCulture, $"{contexto}: {frente} sobre {fundo}: {razao:F2} (mínimo {minimo})"));
            }
        }

        // Botão de opção: não tem fundo próprio; fica sobre cartão, página ou superfície elevada.
        var radio = EstiloImplicito(controles, "RadioButton");
        var textoDoRadio = Token(radio, "Foreground");
        foreach (var fundo in new[] { "Rayzer.Surface", "Rayzer.Background", "Rayzer.Surface.Elevated" })
        {
            Exigir("RadioButton", textoDoRadio, fundo, 4.5);
        }

        var aroMarcado = Token(radio, "Stroke", "IsChecked", "True", "Aro");
        var fundoDoAro = Token(radio, "Fill", alvo: "Aro");
        Exigir("RadioButton (aro)", Token(radio, "Stroke", alvo: "Aro"), fundoDoAro, 3);
        Exigir("RadioButton (marcado)", aroMarcado, fundoDoAro, 3);
        Exigir("RadioButton (ponto)", Token(radio, "Fill", alvo: "Ponto"), fundoDoAro, 3);
        Exigir("RadioButton (aro sobre o cartão)", aroMarcado, "Rayzer.Surface", 3);

        // Lista: o item herda o fundo da lista; sob o mouse e selecionado, tem fundo próprio.
        var lista = EstiloImplicito(controles, "ListBox");
        var item = EstiloImplicito(controles, "ListBoxItem");
        var fundoDaLista = Token(lista, "Background");
        var textoDoItem = Token(item, "Foreground");
        Exigir("ListBox", Token(lista, "Foreground"), fundoDaLista, 4.5);
        Exigir("ListBoxItem", textoDoItem, fundoDaLista, 4.5);
        Exigir("ListBoxItem (mouse)", textoDoItem, Token(item, "Background", "IsMouseOver", "True", "Fundo"), 4.5);
        Exigir("ListBoxItem (selecionado)", Token(item, "Foreground", "IsSelected", "True"), Token(item, "Background", "IsSelected", "True", "Fundo"), 4.5);
        Exigir("ListBoxItem (foco)", Token(item, "BorderBrush", "IsKeyboardFocused", "True", "Fundo"), fundoDaLista, 3);
        Exigir("ListBox (borda)", Token(lista, "BorderBrush"), fundoDaLista, 3);

        Assert.True(falhas.Count == 0, $"Tema {tema}: {string.Join("; ", falhas)}");
    }

    /// <summary>
    /// Desabilitado tem cor própria, não opacidade: a 50 %, o botão fantasma continuava azul e
    /// parecia link, e o principal continuava azul no tema escuro (docs/34 §7.1, P12).
    /// </summary>
    /// <remarks>
    /// O critério do estudo (anexo 04, §6.2, item 3) é ≥ 3:1 de diferença de luminância entre
    /// habilitado e desabilitado. Ele vale como está para o texto do botão secundário e para o
    /// preenchimento do principal. No fantasma não dá: o azul de link e um cinza 3:1 mais claro
    /// ou mais escuro deixariam o rótulo quase invisível sobre o cartão. Lá, o que muda é a
    /// cor: o texto desabilitado é cinza (saturação baixa), nunca o azul de link, e ainda se
    /// distingue em luminância (≥ 2:1). Em todos, o rótulo desabilitado continua legível
    /// (≥ 2:1 sobre o próprio fundo) — o operador lê o que não está disponível.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Temas))]
    public void Botao_desabilitado_tem_cor_propria_e_nao_parece_habilitado(string tema)
    {
        var cor = Cores(Arquivo($"src/Rayzer.Design/Temas/{tema}.xaml"));
        var raiz = XDocument.Load(Arquivo("src/Rayzer.Design/Controles.xaml")).Root!;
        var secundario = EstiloImplicito(raiz, "Button");
        var principal = EstiloComChave(raiz, "Rayzer.Button.Primary");
        var fantasma = EstiloComChave(raiz, "Rayzer.Button.Ghost");

        foreach (var estilo in new[] { secundario, principal, fantasma })
        {
            // Nenhum botão fica "desabilitado" só por transparência.
            Assert.DoesNotContain(
                Setters(estilo, "IsEnabled", "False"),
                s => (string?)s.Attribute("Property") == "Opacity");
        }

        var texto = Token(secundario, "Foreground", "IsEnabled", "False");
        var fundo = Token(secundario, "Background", "IsEnabled", "False");
        Assert.Equal("Rayzer.Text.Disabled", texto);
        Assert.Equal("Rayzer.Surface.Disabled", fundo);
        Assert.Equal(texto, Token(principal, "Foreground", "IsEnabled", "False"));
        Assert.Equal(fundo, Token(principal, "Background", "IsEnabled", "False"));
        Assert.Equal(texto, Token(fantasma, "Foreground", "IsEnabled", "False"));

        // Secundário: o texto. Principal: o preenchimento azul vira cinza.
        Assert.True(Contraste(cor[Token(secundario, "Foreground")], cor[texto]) >= 3, $"{tema}: texto do secundário");
        Assert.True(Contraste(cor[Token(principal, "Background")], cor[fundo]) >= 3, $"{tema}: preenchimento do principal");

        // Fantasma: sai o azul de link, entra o cinza.
        var link = cor[Token(fantasma, "Foreground")];
        Assert.True(Saturacao(link) >= 0.8, $"{tema}: o fantasma habilitado deveria ser o azul de link");
        Assert.True(Saturacao(cor[texto]) <= 0.3, $"{tema}: texto desabilitado com cor de link ({cor[texto]})");
        Assert.True(Contraste(link, cor[texto]) >= 2, $"{tema}: fantasma habilitado e desabilitado com a mesma luminância");

        // Legível: o rótulo desabilitado sobre o próprio fundo e sobre o cartão.
        Assert.True(Contraste(cor[texto], cor[fundo]) >= 2, $"{tema}: rótulo desabilitado ilegível");
        Assert.True(Contraste(cor[texto], cor["Rayzer.Surface"]) >= 2, $"{tema}: rótulo desabilitado ilegível no cartão");
    }

    [Fact]
    public void Toda_chave_Rayzer_usada_nas_telas_existe()
    {
        var definidas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dicionario in new[] { "Tokens.xaml", "Temas/Claro.xaml", "Controles.xaml" })
        {
            definidas.UnionWith(Chaves(Arquivo($"src/Rayzer.Design/{dicionario}")));
        }

        var faltando = new List<string>();
        foreach (var xaml in ArquivosXaml())
        {
            foreach (Match m in Referencia().Matches(File.ReadAllText(xaml)))
            {
                if (!definidas.Contains(m.Groups[1].Value))
                {
                    faltando.Add($"{Path.GetFileName(xaml)}: {m.Groups[1].Value}");
                }
            }
        }

        Assert.True(faltando.Count == 0, $"Chaves Rayzer inexistentes: {string.Join(", ", faltando.Distinct())}");
    }

    [Fact]
    public void Nenhuma_tela_tem_cor_solta()
    {
        // Cor só nos temas do Rayzer.Design. Tela, janela e componente usam as chaves.
        var soltas = new List<string>();
        foreach (var xaml in ArquivosXaml().Where(a => !a.Contains($"Rayzer.Design{Path.DirectorySeparatorChar}Temas", StringComparison.Ordinal)
                                                         && !a.EndsWith("Tokens.xaml", StringComparison.Ordinal)))
        {
            var texto = File.ReadAllText(xaml);
            foreach (Match m in CorLiteral().Matches(texto))
            {
                soltas.Add($"{Path.GetFileName(xaml)}: {m.Value}");
            }

            if (texto.Contains("SystemColors.", StringComparison.Ordinal))
            {
                soltas.Add($"{Path.GetFileName(xaml)}: SystemColors");
            }
        }

        foreach (var codigo in Directory.EnumerateFiles(Raiz("src"), "*.cs", SearchOption.AllDirectories)
                     .Where(c => (c.Contains("Desktop.App", StringComparison.Ordinal) || c.Contains("Edge.Configurador", StringComparison.Ordinal))
                                 && !c.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !c.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var texto = File.ReadAllText(codigo);
            foreach (Match m in CorEmCodigo().Matches(texto))
            {
                soltas.Add($"{Path.GetFileName(codigo)}: {m.Value}");
            }
        }

        Assert.True(soltas.Count == 0, $"Cores fora dos tokens: {string.Join(", ", soltas)}");
    }

    [GeneratedRegex(@"(?:DynamicResource|StaticResource)\s+(Rayzer\.[A-Za-z0-9.]+)")]
    private static partial Regex Referencia();

    [GeneratedRegex(@"=""#[0-9A-Fa-f]{3,8}""|>#[0-9A-Fa-f]{6,8}<")]
    private static partial Regex CorLiteral();

    [GeneratedRegex(@"Brushes\.[A-Z][A-Za-z]+|Color\.FromRgb|Colors\.[A-Z][A-Za-z]+|SystemColors\.[A-Za-z]+")]
    private static partial Regex CorEmCodigo();

    private static IEnumerable<string> ArquivosXaml() =>
        Directory.EnumerateFiles(Raiz("src"), "*.xaml", SearchOption.AllDirectories)
            .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !a.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static HashSet<string> Chaves(string arquivo) =>
        [.. XDocument.Load(arquivo).Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).OfType<string>()];

    private static Dictionary<string, string> Cores(string arquivo) =>
        XDocument.Load(arquivo).Root!.Elements()
            .Where(e => e.Attribute(X + "Key") is not null && e.Attribute("Color") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => (string)e.Attribute("Color")!, StringComparer.Ordinal);

    private static XElement EstiloImplicito(XElement raiz, string tipo) =>
        raiz.Elements().Single(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is null && (string?)e.Attribute("TargetType") == tipo);

    private static XElement EstiloComChave(XElement raiz, string chave) =>
        raiz.Elements().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(X + "Key") == chave);

    /// <summary>
    /// Os Setter do estilo: sem gatilho, os do próprio estilo e os atributos do modelo (por
    /// nome); com gatilho, os de dentro do Trigger com aquela propriedade e valor (do estilo
    /// ou do ControlTemplate).
    /// </summary>
    private static IEnumerable<XElement> Setters(XElement estilo, string? gatilho, string? valor) =>
        gatilho is null
            ? estilo.Elements().Where(e => e.Name.LocalName == "Setter")
            : estilo.Descendants().Where(e => e.Name.LocalName == "Trigger"
                                             && (string?)e.Attribute("Property") == gatilho
                                             && (string?)e.Attribute("Value") == valor)
                .SelectMany(t => t.Elements().Where(e => e.Name.LocalName == "Setter"));

    /// <summary>A chave Rayzer que o estilo põe numa propriedade (de um elemento do modelo, com <paramref name="alvo"/>).</summary>
    private static string Token(XElement estilo, string propriedade, string? gatilho = null, string? valor = null, string? alvo = null)
    {
        string? bruto;
        if (gatilho is null && alvo is not null)
        {
            // Sem gatilho, o valor está no atributo do próprio elemento nomeado do modelo.
            var elemento = estilo.Descendants().Single(e => (string?)e.Attribute(X + "Name") == alvo);
            bruto = (string?)elemento.Attribute(propriedade);
        }
        else
        {
            bruto = Setters(estilo, gatilho, valor)
                .Where(s => (string?)s.Attribute("Property") == propriedade && (string?)s.Attribute("TargetName") == alvo)
                .Select(s => (string?)s.Attribute("Value"))
                .SingleOrDefault();
        }

        var m = Referencia().Match(bruto ?? string.Empty);
        Assert.True(m.Success, $"{propriedade} ({gatilho}={valor}, {alvo}) não usa uma chave Rayzer: '{bruto}'");
        return m.Groups[1].Value;
    }

    /// <summary>Saturação HSL (0 a 1): cinza perto de 0, azul de link perto de 1.</summary>
    private static double Saturacao(string hex)
    {
        var h = hex.TrimStart('#');
        h = h.Length == 8 ? h[2..] : h;
        var canais = Enumerable.Range(0, 3).Select(i => int.Parse(h.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0).ToArray();
        var (max, min) = (canais.Max(), canais.Min());
        var luz = (max + min) / 2;
        return max == min ? 0 : (max - min) / (1 - Math.Abs((2 * luz) - 1));
    }

    private static double Contraste(string a, string b)
    {
        var (la, lb) = (Luminancia(a), Luminancia(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminancia(string hex)
    {
        static double Canal(string h, int i)
        {
            var c = int.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var h = hex.TrimStart('#');
        h = h.Length == 8 ? h[2..] : h;
        return (0.2126 * Canal(h, 0)) + (0.7152 * Canal(h, 2)) + (0.0722 * Canal(h, 4));
    }

    private static string Arquivo(string relativo) => Path.Combine(Raiz(), relativo.Replace('/', Path.DirectorySeparatorChar));

    private static string Raiz(string? sub = null)
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        return sub is null ? raiz.FullName : Path.Combine(raiz.FullName, sub);
    }
}
