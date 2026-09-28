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
        var superficies = new[] { "Rayzer.Background", "Rayzer.Surface", "Rayzer.Surface.Elevated", "Rayzer.Surface.Sunken" };
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
