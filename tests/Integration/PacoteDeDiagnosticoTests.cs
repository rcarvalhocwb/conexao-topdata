using System.IO.Compression;
using System.Text;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// O pacote de diagnóstico (A07; pergunta respondida: registros, versão, pré-requisitos e configuração
/// sem segredo). O zip nunca pode levar banco, token, cofre, cópia de segurança nem número de cartão.
/// </summary>
public sealed class PacoteDeDiagnosticoTests : IDisposable
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    public PacoteDeDiagnosticoTests()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "registros"));
        File.WriteAllText(Path.Combine(_pasta, "registros", "servico-hoje.log"),
            "catraca 1 atendeu\ncartão 0001234567890123 lido no leitor 1\nlinha normal");
        var antigo = Path.Combine(_pasta, "registros", "servico-antigo.log");
        File.WriteAllText(antigo, "registro muito antigo");
        File.SetLastWriteTimeUtc(antigo, Agora.AddDays(-30).UtcDateTime);
        File.WriteAllText(Path.Combine(_pasta, "workers.json"),
            """{"nome":"catracas","porta":3570,"senha":"segredo-x","grupos":[{"nome":"setor-a","token":"abc","inners":[1]}],"nuvem":{"segredo":"s3"}}""");
        File.WriteAllBytes(Path.Combine(_pasta, "acesso.db"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(_pasta, "token"), "token-da-sessao");
        Directory.CreateDirectory(Path.Combine(_pasta, "copias"));
        File.WriteAllBytes(Path.Combine(_pasta, "copias", "acesso-1.db"), [5, 6]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static Dictionary<string, string> Ler(byte[] zip)
    {
        using var memoria = new MemoryStream(zip);
        using var arquivo = new ZipArchive(memoria, ZipArchiveMode.Read);
        return arquivo.Entries.ToDictionary(
            e => e.FullName,
            e =>
            {
                using var leitor = new StreamReader(e.Open(), Encoding.UTF8);
                return leitor.ReadToEnd();
            });
    }

    [Fact]
    public void O_zip_traz_o_que_o_suporte_pede()
    {
        var zip = MontadorDoPacoteDeDiagnostico.Montar(_pasta, "0.1.151", ["Pré-requisitos:", "  DLL: ok"], Agora);

        var entradas = Ler(zip);
        Assert.Contains("diagnostico.txt", entradas.Keys);
        Assert.Contains("registros/servico-hoje.log", entradas.Keys);
        Assert.Contains("workers.json", entradas.Keys);
        Assert.Contains("Versão: 0.1.151", entradas["diagnostico.txt"], StringComparison.Ordinal);
        Assert.Contains("DLL: ok", entradas["diagnostico.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public void O_zip_nao_traz_banco_token_copias_nem_registro_antigo()
    {
        var entradas = Ler(MontadorDoPacoteDeDiagnostico.Montar(_pasta, "x", [], Agora));

        Assert.DoesNotContain(entradas.Keys, k => k.Contains("acesso", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entradas.Keys, k => k.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entradas.Keys, k => k.Contains("copias", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entradas.Keys, k => k.Contains("antigo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Numero_de_cartao_nos_registros_nao_sai_no_zip()
    {
        var entradas = Ler(MontadorDoPacoteDeDiagnostico.Montar(_pasta, "x", [], Agora));

        var log = entradas["registros/servico-hoje.log"];
        Assert.DoesNotContain("0001234567890123", log, StringComparison.Ordinal);
        Assert.Contains("catraca 1 atendeu", log, StringComparison.Ordinal);
        Assert.Contains("linha normal", log, StringComparison.Ordinal);
    }

    [Fact]
    public void A_configuracao_sai_sem_segredo_mas_com_o_que_o_suporte_precisa()
    {
        var entradas = Ler(MontadorDoPacoteDeDiagnostico.Montar(_pasta, "x", [], Agora));

        var configuracao = entradas["workers.json"];
        Assert.DoesNotContain("segredo-x", configuracao, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", configuracao, StringComparison.Ordinal);
        Assert.DoesNotContain("s3", configuracao, StringComparison.Ordinal);
        Assert.DoesNotContain("senha", configuracao, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", configuracao, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"porta\": 3570", configuracao, StringComparison.Ordinal);
        Assert.Contains("setor-a", configuracao, StringComparison.Ordinal);
    }

    [Fact]
    public void Pasta_sem_registros_ainda_monta_o_diagnostico()
    {
        var vazia = Path.Combine(_pasta, "vazia");
        Directory.CreateDirectory(vazia);

        var entradas = Ler(MontadorDoPacoteDeDiagnostico.Montar(vazia, "x", ["linha"], Agora));

        Assert.Contains("diagnostico.txt", entradas.Keys);
        Assert.DoesNotContain(entradas.Keys, k => k.StartsWith("registros/", StringComparison.Ordinal));
    }
}
