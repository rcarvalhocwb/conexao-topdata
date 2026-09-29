using System.Xml.Linq;

namespace Integration.Tests;

/// <summary>
/// A arquitetura de marca do brand board como contrato: a empresa (RAYZER X — Rayzer
/// Serviços e Tecnologia) e o produto (XAcess) são marcas separadas; paleta e tipografia
/// são as do board; o desktop e a web desenham o mesmo letreiro.
/// </summary>
public sealed partial class RayzerDesignTests
{
    /// <summary>Paleta do brand board (seção 06), no tema escuro — a expressão principal.</summary>
    [Theory]
    [InlineData("Rayzer.Brand.Primary", "#0066FF")]
    [InlineData("Rayzer.Brand.Cyan", "#00D5FF")]
    [InlineData("Rayzer.Text.Primary", "#F4F7FB")]
    [InlineData("Rayzer.Text.Secondary", "#8A9BB0")]
    [InlineData("Rayzer.Background", "#0B1A33")]
    [InlineData("Rayzer.Surface", "#111827")]
    [InlineData("Rayzer.Success", "#32D583")]
    [InlineData("Rayzer.Warning", "#F5A524")]
    [InlineData("Rayzer.Danger", "#F05252")]
    public void O_tema_escuro_usa_a_paleta_do_brand_board(string chave, string cor)
    {
        var cores = Cores(Arquivo("src/Rayzer.Design/Temas/Escuro.xaml"));

        Assert.Equal(cor, cores[chave], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// RayzerLogo é a empresa, XAcessLogo é o produto: o letreiro "acess" nunca entra no
    /// logo corporativo, e o produto sempre abre com o X e traz o endosso "by RAYZER X".
    /// </summary>
    [Fact]
    public void A_marca_corporativa_e_o_produto_sao_logos_separados()
    {
        var generic = File.ReadAllText(Arquivo("src/Rayzer.Design/Themes/Generic.xaml"));
        var corporativo = Estilo(generic, "RayzerLogo");
        var produto = Estilo(generic, "XAcessLogo");

        Assert.Contains("Rayzer.Letreiro.Rayzer", corporativo);
        Assert.Contains("Rayzer.Letreiro.Descritor", corporativo);
        Assert.DoesNotContain("Rayzer.Letreiro.Acess", corporativo);
        Assert.DoesNotContain("Rayzer.Letreiro.Tagline", corporativo);

        Assert.Contains("Rayzer.Letreiro.Acess", produto);
        Assert.Contains("Rayzer.Letreiro.Tagline", produto);
        Assert.Contains("Rayzer.Letreiro.By", produto);
        Assert.Contains("local:RayzerBrandMark", produto);
    }

    /// <summary>
    /// Os letreiros saem de tools/gerar-marca.py para o XAML e para o React: se alguém
    /// edita um lado à mão, o desktop e a web passam a desenhar marcas diferentes.
    /// </summary>
    [Fact]
    public void Desktop_e_web_desenham_o_mesmo_letreiro()
    {
        var tokens = XDocument.Load(Arquivo("src/Rayzer.Design/Tokens.xaml")).Root!.Elements()
            .Where(e => ((string?)e.Attribute(X + "Key"))?.StartsWith("Rayzer.Letreiro.", StringComparison.Ordinal) == true)
            .ToDictionary(e => ((string)e.Attribute(X + "Key")!)["Rayzer.Letreiro.".Length..], e => e.Value.Trim(), StringComparer.Ordinal);
        var ts = File.ReadAllText(Arquivo("web/rayzer-ui/src/components/brand/letreiros.ts"));

        Assert.Equal(["Acess", "By", "Descritor", "Rayzer", "Tagline"], tokens.Keys.Order(StringComparer.Ordinal));
        foreach (var (nome, caminho) in tokens)
        {
            Assert.False(string.IsNullOrWhiteSpace(caminho), nome);
            Assert.Contains($"\"{nome}\": {{\n    \"d\": \"{caminho}\"", ts.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Tipografia do board (seção 07): Sora na interface, Inter no suporte, JetBrains Mono nos
    /// dados — embutidas, com a licença OFL ao lado, e com a fonte do sistema de reserva.
    /// </summary>
    [Fact]
    public void As_fontes_do_brand_board_estao_embutidas_com_licenca()
    {
        var pasta = Arquivo("src/Rayzer.Design/Fontes");
        foreach (var (familia, arquivo) in new[] { ("Sora", "Sora"), ("Inter", "Inter"), ("JetBrains Mono", "JetBrainsMono") })
        {
            Assert.NotEmpty(Directory.GetFiles(pasta, $"{arquivo}-*.ttf"));
            Assert.Contains("SIL Open Font License", File.ReadAllText(Path.Combine(pasta, $"{arquivo}-OFL.txt")), StringComparison.Ordinal);
            Assert.Contains($"/Rayzer.Design;component/Fontes/#{familia},", File.ReadAllText(Arquivo("src/Rayzer.Design/Tokens.xaml")), StringComparison.Ordinal);
        }

        Assert.Contains(@"<Resource Include=""Fontes\*.ttf"" />", File.ReadAllText(Arquivo("src/Rayzer.Design/Rayzer.Design.csproj")), StringComparison.Ordinal);
    }

    private static string Estilo(string xaml, string tipo)
    {
        var inicio = xaml.IndexOf($"<Style TargetType=\"{{x:Type local:{tipo}}}\">", StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"Estilo de {tipo} não encontrado.");
        var fim = xaml.IndexOf("</Style>", inicio, StringComparison.Ordinal);
        return xaml[inicio..fim];
    }
}
