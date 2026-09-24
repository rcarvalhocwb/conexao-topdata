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
