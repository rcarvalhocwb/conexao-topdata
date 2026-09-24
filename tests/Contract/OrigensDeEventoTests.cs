using Access.Domain.Devices;

namespace Contract.Tests;

/// <summary>
/// Mantém <see cref="KnownEventOrigin"/> e <c>origens-evento.csv</c> em acordo.
/// </summary>
/// <remarks>
/// Se alguém acrescentar uma origem no código sem registrá-la na matriz — ou ao
/// contrário — o build quebra. É assim que a matriz deixa de ser documentação e vira
/// trava. Ver docs/02-matriz-compatibilidade.md.
/// </remarks>
public sealed class OrigensDeEventoTests
{
    private const string Arquivo = "origens-evento.csv";
    private const string MarcadorDeLacuna = "LACUNA";

    /// <summary>Origem 6 — a única que comprova giro, segundo o manual.</summary>
    private static readonly int[] OrigensQueConfirmamPassagem = [6];

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Linhas => RepositorioDeMatriz.Ler(Arquivo);

    [Fact]
    public void Toda_origem_documentada_na_matriz_existe_no_enum()
    {
        var ausentes = new List<string>();

        foreach (var linha in Linhas)
        {
            var enumOficial = linha["enum_oficial"];
            if (string.Equals(enumOficial, MarcadorDeLacuna, StringComparison.Ordinal))
            {
                continue;
            }

            var valor = RepositorioDeMatriz.ParaInteiro(linha["valor"]);
            if (!Enum.IsDefined(typeof(KnownEventOrigin), valor))
            {
                ausentes.Add($"{valor} ({enumOficial})");
            }
        }

        Assert.True(
            ausentes.Count == 0,
            $"Origens presentes em {Arquivo} e ausentes de KnownEventOrigin: {string.Join(", ", ausentes)}");
    }

    [Fact]
    public void Toda_origem_do_enum_esta_registrada_na_matriz()
    {
        var naMatriz = Linhas
            .Where(l => !string.Equals(l["enum_oficial"], MarcadorDeLacuna, StringComparison.Ordinal))
            .Select(l => RepositorioDeMatriz.ParaInteiro(l["valor"]))
            .ToHashSet();

        var naoRegistradas = Enum.GetValues<KnownEventOrigin>()
            .Select(v => (int)v)
            .Where(v => !naMatriz.Contains(v))
            .ToList();

        Assert.True(
            naoRegistradas.Count == 0,
            $"Origens no enum e ausentes de {Arquivo}: {string.Join(", ", naoRegistradas)}. " +
            "Toda origem tratada em código precisa de registro na matriz, com fonte.");
    }

    /// <summary>
    /// As lacunas da matriz precisam continuar sendo lacunas no código. Se alguém
    /// "resolver" a origem 14 por dedução, este teste avisa.
    /// </summary>
    [Fact]
    public void Lacunas_da_matriz_nao_podem_virar_valores_conhecidos()
    {
        var invadidas = Linhas
            .Where(l => string.Equals(l["enum_oficial"], MarcadorDeLacuna, StringComparison.Ordinal))
            .Select(l => RepositorioDeMatriz.ParaInteiro(l["valor"]))
            .Where(v => Enum.IsDefined(typeof(KnownEventOrigin), v))
            .ToList();

        Assert.True(
            invadidas.Count == 0,
            $"Estas origens estão marcadas como LACUNA em {Arquivo} mas foram definidas no enum: " +
            $"{string.Join(", ", invadidas)}. Preencher uma lacuna exige fonte primária, no mesmo commit.");
    }

    [Fact]
    public void Apenas_a_origem_6_e_marcada_como_confirmacao_de_passagem_na_matriz()
    {
        var confirmam = Linhas
            .Where(l => string.Equals(l["confirma_passagem_fisica"], "SIM", StringComparison.OrdinalIgnoreCase))
            .Select(l => RepositorioDeMatriz.ParaInteiro(l["valor"]))
            .ToList();

        Assert.Equal(OrigensQueConfirmamPassagem, confirmam);
    }

    /// <summary>
    /// O código e a matriz precisam concordar sobre o que prova uma passagem física.
    /// Divergir aqui significa contar público errado.
    /// </summary>
    [Fact]
    public void Matriz_e_dominio_concordam_sobre_confirmacao_de_passagem()
    {
        foreach (var linha in Linhas)
        {
            var valor = RepositorioDeMatriz.ParaInteiro(linha["valor"]);
            var matrizConfirma = string.Equals(linha["confirma_passagem_fisica"], "SIM", StringComparison.OrdinalIgnoreCase);

            Assert.Equal(matrizConfirma, EventOrigin.FromRaw(valor).ConfirmaPassagemFisica);
        }
    }

    [Fact]
    public void Toda_linha_declara_procedencia()
    {
        var semSelo = Linhas
            .Where(l => string.IsNullOrWhiteSpace(l["selo"]) || string.IsNullOrWhiteSpace(l["fonte"]))
            .Select(l => l["valor"])
            .ToList();

        Assert.True(
            semSelo.Count == 0,
            $"Linhas sem selo de procedência ou sem fonte: {string.Join(", ", semSelo)}");
    }
}
