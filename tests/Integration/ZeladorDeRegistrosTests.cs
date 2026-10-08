using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Retenção dos registros do serviço (pergunta A06: 30 dias e teto de 200 MB). Os arquivos têm
/// datas forjadas, então o teste não depende do relógio.
/// </summary>
public sealed class ZeladorDeRegistrosTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    public ZeladorDeRegistrosTests() => Directory.CreateDirectory(_pasta);

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

    private string Arquivo(string nome, DateTimeOffset quando, int tamanho = 10)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, new byte[tamanho]);
        File.SetLastWriteTimeUtc(caminho, quando.UtcDateTime);
        return caminho;
    }

    [Fact]
    public void Apaga_o_que_tem_mais_de_30_dias_e_mantem_o_recente()
    {
        var antigo = Arquivo("servico-2026-08-01.log", Agora.AddDays(-45));
        var recente = Arquivo("servico-2026-10-01.log", Agora.AddDays(-7));

        var apagados = ZeladorDeRegistros.Limpar(_pasta, Agora);

        Assert.Equal(1, apagados);
        Assert.False(File.Exists(antigo));
        Assert.True(File.Exists(recente));
    }

    [Fact]
    public void Acima_do_teto_apaga_do_mais_antigo_ate_caber()
    {
        var metade = ZeladorDeRegistros.TetoEmBytes / 2 + 1024;
        var primeiro = Arquivo("servico-a.log", Agora.AddDays(-10), (int)Math.Min(metade, int.MaxValue));
        var segundo = Arquivo("servico-b.log", Agora.AddDays(-9), (int)Math.Min(metade, int.MaxValue));
        var terceiro = Arquivo("servico-c.log", Agora.AddDays(-8), (int)Math.Min(metade, int.MaxValue));

        ZeladorDeRegistros.Limpar(_pasta, Agora);

        // Do mais antigo para o mais novo, até caber no teto: sai o primeiro e o segundo; fica o terceiro.
        Assert.False(File.Exists(primeiro));
        Assert.False(File.Exists(segundo));
        Assert.True(File.Exists(terceiro));
        var sobrou = Directory.EnumerateFiles(_pasta, "*.log").Sum(f => new FileInfo(f).Length);
        Assert.True(sobrou <= ZeladorDeRegistros.TetoEmBytes);
    }

    [Fact]
    public void Nunca_apaga_o_que_foi_escrito_nas_ultimas_24_horas()
    {
        var hoje = Arquivo("servico-hoje.log", Agora.AddHours(-2), 1024);

        ZeladorDeRegistros.Limpar(_pasta, Agora);

        Assert.True(File.Exists(hoje));
    }

    [Fact]
    public void Pasta_inexistente_nao_falha()
    {
        Assert.Equal(0, ZeladorDeRegistros.Limpar(Path.Combine(_pasta, "nao-existe"), Agora));
    }

    [Fact]
    public void So_mexe_em_arquivos_de_registro()
    {
        var outro = Path.Combine(_pasta, "workers.json");
        File.WriteAllText(outro, "{}");
        File.SetLastWriteTimeUtc(outro, Agora.AddDays(-400).UtcDateTime);

        ZeladorDeRegistros.Limpar(_pasta, Agora);

        Assert.True(File.Exists(outro));
    }
}
