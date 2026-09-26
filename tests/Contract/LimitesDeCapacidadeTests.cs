using System.Globalization;
using System.Text.RegularExpressions;
using Access.Application.Devices;

namespace Contract.Tests;

/// <summary>
/// Amarra <see cref="LimitesDeCapacidade"/> à tabela publicada pela Topdata.
/// </summary>
/// <remarks>
/// Os tetos são dado de fábrica, não escolha nossa. Se alguém "ajustar" uma constante para
/// fazer um caso caber, o build reprova — que é exatamente o momento em que se quer ser
/// interrompido, e não no envio da lista com a catraca travada.
/// </remarks>
public sealed class LimitesDeCapacidadeTests
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Linhas =
        RepositorioDeMatriz.Ler("limites-de-capacidade.csv");

    [Fact]
    public void A_producao_atual_segue_a_matriz()
    {
        var linha = Linha(l =>
            l["limite"] == "LISTA_ACESSO" &&
            l["escopo"].Contains("producao atual", StringComparison.Ordinal));

        var publicado = int.Parse(linha["valor"], CultureInfo.InvariantCulture);

        // "qualquer quantidade de digitos" — vale para todo tamanho, menos a exceção de 16.
        foreach (var digitos in (int[])[1, 4, 8, 10, 14, 15])
        {
            Assert.Equal(
                publicado,
                LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.ControleCatracaOuInnerAcesso, digitos));
        }
    }

    [Fact]
    public void A_excecao_de_dezesseis_digitos_segue_a_matriz()
    {
        var linha = Linha(l =>
            l["limite"] == "LISTA_ACESSO" &&
            l["condicao"].Contains("16 digitos", StringComparison.Ordinal));

        var publicado = int.Parse(linha["valor"], CultureInfo.InvariantCulture);

        Assert.Equal(
            publicado,
            LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.ControleCatracaOuInnerAcesso, 16));
    }

    [Fact]
    public void As_placas_descontinuadas_seguem_a_tabela_por_digito()
    {
        var linha = Linha(l =>
            l["limite"] == "LISTA_ACESSO" &&
            l["escopo"].Contains("Inner Plus", StringComparison.Ordinal));

        var pares = Regex
            .Matches(linha["condicao"], @"(\d+)d=(\d+)", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(m => (
                Digitos: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Maximo: int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();

        Assert.NotEmpty(pares);

        foreach (var (digitos, maximo) in pares)
        {
            Assert.Equal(
                maximo,
                LimitesDeCapacidade.MaximoDeUsuarios(PlacaDoEquipamento.InnerPlusOuInnerNet, digitos));
        }
    }

    [Fact]
    public void As_marcacoes_e_os_equipamentos_por_dll_seguem_a_matriz()
    {
        var marcacoes = Linha(l => l["limite"] == "MARCACOES");
        Assert.Equal(
            LimitesDeCapacidade.MarcacoesPorEquipamento,
            int.Parse(marcacoes["valor"], CultureInfo.InvariantCulture));

        var porDll = Linha(l => l["limite"] == "EQUIPAMENTOS_POR_DLL");
        Assert.Equal(
            LimitesDeCapacidade.EquipamentosPorInstanciaDaDll,
            int.Parse(porDll["valor"], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// O número de marcações que uma passagem gera continua sem confirmação da Topdata.
    /// </summary>
    /// <remarks>
    /// Este teste guarda a lacuna: se alguém marcar a linha como confirmada sem ter a
    /// resposta, ou apagar a linha, o build avisa. Planejar memória com um número inventado
    /// é como o off-line de um evento falha em silêncio.
    /// </remarks>
    [Fact]
    public void A_lacuna_das_marcacoes_por_passagem_continua_registrada()
    {
        var linha = Linha(l => l["limite"] == "MARCACOES_POR_PASSAGEM");

        Assert.Equal("A_CONFIRMAR_COM_TOPDATA", linha["selo"]);
    }

    private static IReadOnlyDictionary<string, string> Linha(
        Func<IReadOnlyDictionary<string, string>, bool> criterio)
    {
        var achadas = Linhas.Where(criterio).ToList();

        Assert.True(
            achadas.Count == 1,
            $"esperava exatamente uma linha na matriz; achei {achadas.Count}. " +
            "A matriz é a fonte da verdade — se ela mudou de forma, o teste precisa saber.");

        return achadas[0];
    }
}

/// <summary>
/// Amarra o inventário da DLL ao que o produto realmente declara.
/// </summary>
/// <remarks>
/// O inventário é a resposta a "quantas funções existem e quais usamos". Sem esta trava
/// ele viraria um retrato de um dia, e a diferença entre 38 declaradas e 265 existentes é
/// justamente o que não se quer perder de vista.
/// </remarks>
public sealed class InventarioDaDllTests
{
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Inventario =
        RepositorioDeMatriz.Ler("inventario-completo-dll.csv");

    /// <summary>
    /// A superfície real é 265, e não as 775 linhas da tabela de exportação.
    /// </summary>
    /// <remarks>
    /// 510 daquelas linhas são wrappers JNI — a mesma API exposta para Java, com nome
    /// decorado. Contá-las triplicava o tamanho aparente do SDK, e eu cheguei a relatar o
    /// número inflado antes de conferir.
    /// </remarks>
    [Fact]
    public void O_inventario_cobre_a_api_real_e_nao_os_wrappers_java()
    {
        Assert.Equal(265, Inventario.Count);
        Assert.DoesNotContain(Inventario, f => f["funcao"].StartsWith("_Java_", StringComparison.Ordinal));
    }

    /// <summary>
    /// Toda declaração do produto precisa existir na DLL.
    /// </summary>
    /// <remarks>
    /// As 229 declarações não foram digitadas: <c>tools/gerar-interop.py</c> as emite a
    /// partir do SDK e confere cada nome contra a tabela de exportação do próprio binário.
    /// Este teste é a rede embaixo disso — declarar um símbolo que a DLL não exporta falha
    /// no carregamento, e não num lugar que ajude a entender.
    /// </remarks>
    [Fact]
    public void Toda_declaracao_do_produto_existe_na_dll()
    {
        var noInventario = Inventario.Select(f => f["funcao"]).ToHashSet(StringComparer.Ordinal);

        var declaradas = LerDeclaracoes("EasyInnerNative.cs")
            .Concat(LerDeclaracoes("EasyInnerGerada.cs"))
            .ToList();

        Assert.True(declaradas.Count >= 200, $"esperava a superfície quase inteira; achei {declaradas.Count}.");

        var fantasmas = declaradas.Where(d => !noInventario.Contains(d)).ToList();

        Assert.True(
            fantasmas.Count == 0,
            "Declarações que não existem na DLL: " + string.Join(", ", fantasmas));
    }

    /// <summary>O arquivo gerado não pode ser editado à mão: a próxima geração o sobrescreve.</summary>
    [Fact]
    public void O_arquivo_gerado_se_identifica_como_gerado()
    {
        var texto = File.ReadAllText(CaminhoDoInterop("EasyInnerGerada.cs"));

        Assert.StartsWith("// <auto-generated>", texto, StringComparison.Ordinal);
        Assert.Contains("tools/gerar-interop.py", texto, StringComparison.Ordinal);
    }

    private static string CaminhoDoInterop(string arquivo) =>
        Path.Combine(RepositorioDeMatriz.RaizDoRepositorio, "src", "Topdata.EasyInner.Interop", arquivo);

    private static IEnumerable<string> LerDeclaracoes(string arquivo) =>
        System.Text.RegularExpressions.Regex
            .Matches(
                File.ReadAllText(CaminhoDoInterop(arquivo)),
                @"internal static extern [\w\[\]]+ (\w+)\(",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value);

    [Fact]
    public void Toda_funcao_declarada_no_produto_esta_marcada_como_tal()
    {
        var declaradas = Inventario.Where(f => f["situacao"] == "DECLARADA_NO_PRODUTO").ToList();

        Assert.NotEmpty(declaradas);

        // Declarar o que não existe na DLL é o erro que corrompe memória em vez de lançar.
        Assert.All(declaradas, f => Assert.NotEqual("SEM ASSINATURA PUBLICADA", f["assinatura_oficial"]));
    }

    /// <summary>
    /// Nada pode ser declarado sem assinatura de fonte primária.
    /// </summary>
    [Fact]
    public void Nenhuma_funcao_sem_assinatura_foi_declarada()
    {
        var inventadas = Inventario
            .Where(f => f["situacao"] == "DECLARADA_NO_PRODUTO" && f["fonte"] != "SDK 6.0.2.0 EasyInner.cs")
            .Select(f => f["funcao"])
            .ToList();

        Assert.True(
            inventadas.Count == 0,
            "Funções declaradas sem assinatura de fonte primária: " + string.Join(", ", inventadas) +
            ". Deduzir assinatura de P/Invoke não dá exceção, dá corrupção de memória.");
    }
}
