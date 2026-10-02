using System.Text.RegularExpressions;
using Access.Application.Devices;

namespace Contract.Tests;

/// <summary>
/// Mantém <see cref="RetornosDocumentados"/> e a coluna <c>retornos_documentados</c> de
/// <c>funcoes-easyinner.csv</c> em acordo, nas duas direções.
/// </summary>
/// <remarks>
/// Um retorno por função que não esteja na matriz é retorno inventado; um que esteja na
/// matriz e não no código volta a cair em "desconhecido" (defeito F6, docs/34 §2; ADR-0018).
/// Os retornos gerais (0, 1 e 8) valem para toda função e não entram na tabela.
/// </remarks>
public sealed partial class RetornosDocumentadosTests
{
    private static readonly int[] RetornosGerais = [0, 1, 8];

    [GeneratedRegex(@"(\d+)(?:-(\d+))?")]
    private static partial Regex Numeros();

    /// <summary>Números da coluna, com as faixas ("4-6") expandidas.</summary>
    private static HashSet<int> RetornosDaLinha(string texto) =>
        [.. Numeros().Matches(texto).SelectMany(m =>
        {
            var de = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var ate = m.Groups[2].Success
                ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                : de;
            return Enumerable.Range(de, ate - de + 1);
        })];

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Linhas =>
        RepositorioDeMatriz.Ler("funcoes-easyinner.csv");

    [Fact]
    public void Todo_retorno_da_tabela_esta_na_matriz_para_aquela_funcao()
    {
        var inventados = new List<string>();

        foreach (var (funcao, retorno) in RetornosDocumentados.Pares)
        {
            var linha = Linhas.SingleOrDefault(l => l["funcao"] == funcao);
            var documentado = RetornosDocumentados.Consultar(funcao, retorno)!;

            if (linha is null
                || linha["id"] != documentado.Fonte
                || !RetornosDaLinha(linha["retornos_documentados"]).Contains(retorno))
            {
                inventados.Add($"{funcao} {retorno}");
            }
        }

        Assert.True(inventados.Count == 0, "Retornos sem linha na matriz FUN: " + string.Join(", ", inventados));
    }

    [Fact]
    public void Todo_retorno_especifico_da_matriz_esta_na_tabela()
    {
        var faltando = new List<string>();

        foreach (var linha in Linhas)
        {
            foreach (var retorno in RetornosDaLinha(linha["retornos_documentados"]).Except(RetornosGerais))
            {
                if (RetornosDocumentados.Consultar(linha["funcao"], retorno) is null)
                {
                    faltando.Add($"{linha["id"]} {linha["funcao"]} {retorno}");
                }
            }
        }

        Assert.True(faltando.Count == 0, "Retornos da matriz sem tratamento por função: " + string.Join(", ", faltando));
    }
}
