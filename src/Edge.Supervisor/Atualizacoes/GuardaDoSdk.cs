namespace Edge.Supervisor.Atualizacoes;

/// <summary>
/// Guarda uma cópia do SDK da Topdata fora da pasta do programa e a repõe se sumir (docs/60 §2.1).
/// </summary>
/// <remarks>
/// <para>
/// O instalador público não leva o SDK. A DLL chega pelo assistente ("Localizar EasyInner.dll") ou pelo
/// instalador da Topdata, ou veio dentro de um MSI do workflow privado. Neste último caso a DLL é do MSI, e
/// a atualização pelo MSI público a apagaria: as catracas parariam. Por isso a cópia em
/// <c>%ProgramData%\ConexaoTopdata\sdk</c>, reposta antes de as catracas subirem.
/// </para>
/// <para>
/// Os arquivos do SDK são a <c>EasyInner.dll</c> e os listados em <c>sdk-topdata.txt</c> na pasta das
/// catracas, se existir (um nome por linha, sem caminho).
/// </para>
/// </remarks>
public static class GuardaDoSdk
{
    /// <summary>A DLL principal do SDK.</summary>
    public const string DllPrincipal = "EasyInner.dll";

    /// <summary>A lista opcional dos outros arquivos do SDK.</summary>
    public const string Lista = "sdk-topdata.txt";

    /// <summary>Copia o SDK da pasta das catracas para a guarda, se estiver lá. Devolve quantos arquivos guardou.</summary>
    public static int Guardar(string pastaDasCatracas, string pastaDaGuarda)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDasCatracas);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDaGuarda);

        if (!File.Exists(Path.Combine(pastaDasCatracas, DllPrincipal)))
        {
            return 0;
        }

        Directory.CreateDirectory(pastaDaGuarda);
        var guardados = 0;
        foreach (var nome in Arquivos(pastaDasCatracas))
        {
            var origem = Path.Combine(pastaDasCatracas, nome);
            var destino = Path.Combine(pastaDaGuarda, nome);
            if (File.Exists(origem) && !Iguais(origem, destino))
            {
                File.Copy(origem, destino, overwrite: true);
                guardados++;
            }
        }

        if (File.Exists(Path.Combine(pastaDasCatracas, Lista)))
        {
            File.Copy(Path.Combine(pastaDasCatracas, Lista), Path.Combine(pastaDaGuarda, Lista), overwrite: true);
        }

        return guardados;
    }

    /// <summary>Repõe na pasta das catracas o que falta do SDK guardado. Devolve quantos arquivos repôs.</summary>
    public static int Repor(string pastaDasCatracas, string pastaDaGuarda)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDasCatracas);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDaGuarda);

        if (!File.Exists(Path.Combine(pastaDaGuarda, DllPrincipal)) || !Directory.Exists(pastaDasCatracas))
        {
            return 0;
        }

        var repostos = 0;
        foreach (var nome in Arquivos(pastaDaGuarda))
        {
            var origem = Path.Combine(pastaDaGuarda, nome);
            var destino = Path.Combine(pastaDasCatracas, nome);
            if (File.Exists(origem) && !File.Exists(destino))
            {
                File.Copy(origem, destino);
                repostos++;
            }
        }

        return repostos;
    }

    private static IEnumerable<string> Arquivos(string pasta)
    {
        yield return DllPrincipal;

        var lista = Path.Combine(pasta, Lista);
        if (!File.Exists(lista))
        {
            yield break;
        }

        foreach (var linha in File.ReadAllLines(lista))
        {
            var nome = linha.Trim();

            // Só nomes de arquivo: nada de caminho nem de subir de pasta.
            if (nome.Length > 0 && nome == Path.GetFileName(nome) && nome != ".." && !nome.Equals(DllPrincipal, StringComparison.OrdinalIgnoreCase))
            {
                yield return nome;
            }
        }
    }

    private static bool Iguais(string a, string b)
    {
        if (!File.Exists(b))
        {
            return false;
        }

        var (ia, ib) = (new FileInfo(a), new FileInfo(b));
        return ia.Length == ib.Length && ia.LastWriteTimeUtc == ib.LastWriteTimeUtc;
    }
}
