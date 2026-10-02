using System.Globalization;

namespace Contract.Tests;

/// <summary>
/// Lê os CSVs de <c>docs/compatibility-matrix/</c>, que são a fonte da verdade sobre o
/// que o SDK oferece.
/// </summary>
/// <remarks>
/// A matriz não é documentação decorativa: ela é conferida contra o código a cada build.
/// Ver docs/02-matriz-compatibilidade.md.
/// </remarks>
internal static class RepositorioDeMatriz
{
    private const string MarcadorDaRaiz = "ConexaoTopdata.slnx";

    internal static string RaizDoRepositorio { get; } = LocalizarRaiz();

    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> Ler(string nomeDoArquivo)
    {
        var caminho = Path.Combine(RaizDoRepositorio, "docs", "compatibility-matrix", nomeDoArquivo);

        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException(
                $"Matriz de compatibilidade não encontrada: {caminho}. " +
                "Ela é obrigatória — sem matriz não há como validar as chamadas nativas.",
                caminho);
        }

        var linhas = File.ReadAllLines(caminho);
        if (linhas.Length < 2)
        {
            throw new InvalidDataException($"{nomeDoArquivo} está vazio ou só tem cabeçalho.");
        }

        var cabecalho = linhas[0].Split(';');
        var registros = new List<IReadOnlyDictionary<string, string>>(linhas.Length - 1);

        for (var i = 1; i < linhas.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(linhas[i]))
            {
                continue;
            }

            var campos = linhas[i].Split(';');
            if (campos.Length != cabecalho.Length)
            {
                throw new InvalidDataException(
                    $"{nomeDoArquivo}, linha {i + 1}: {campos.Length} colunas, esperado {cabecalho.Length}. " +
                    "Provavelmente há um ';' dentro de um campo de texto.");
            }

            var registro = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < cabecalho.Length; c++)
            {
                registro[cabecalho[c]] = campos[c].Trim();
            }

            registros.Add(registro);
        }

        return registros;
    }

    internal static int ParaInteiro(string valor) =>
        int.Parse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static string LocalizarRaiz()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (diretorio is not null)
        {
            if (File.Exists(Path.Combine(diretorio.FullName, MarcadorDaRaiz)))
            {
                return diretorio.FullName;
            }

            diretorio = diretorio.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Não foi possível localizar a raiz do repositório (procurando por {MarcadorDaRaiz}).");
    }
}
