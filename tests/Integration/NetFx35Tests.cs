using Edge.Supervisor.Instalacao;

namespace Integration.Tests;

/// <summary>
/// Achado E10-8 do docs/41: sem internet, o .NET 3.5 (de que a EasyInner.dll precisa) não era
/// habilitado. O assistente aceita a pasta <c>sources\sxs</c> da mídia do Windows.
/// </summary>
public sealed class NetFx35Tests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"), "sxs");

    public NetFx35Tests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_pasta)!, recursive: true);
        }
        catch (IOException)
        {
            // Sobra de arquivo temporário não reprova o teste.
        }
    }

    [Fact]
    public void Sem_pasta_usa_o_windows_update_como_antes()
    {
        Assert.Equal("/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart", NetFx35.ArgumentosDoDism());
    }

    [Fact]
    public void Com_a_pasta_da_midia_instala_dela_sem_internet()
    {
        var argumentos = NetFx35.ArgumentosDoDism(_pasta + Path.DirectorySeparatorChar);

        Assert.Equal($"/Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart /Source:\"{Path.GetFullPath(_pasta)}\" /LimitAccess", argumentos);
    }

    [Fact]
    public void Pasta_sem_o_pacote_do_net35_e_recusada_com_o_que_escolher()
    {
        Assert.Contains("sources\\sxs", NetFx35.ProblemaNaPasta(_pasta), StringComparison.Ordinal);
        Assert.Equal("A pasta não existe.", NetFx35.ProblemaNaPasta(Path.Combine(_pasta, "nao-existe")));

        File.WriteAllText(Path.Combine(_pasta, "microsoft-windows-netfx3-ondemand-package~31bf3856ad364e35~amd64~~.cab"), "x");
        Assert.Empty(NetFx35.ProblemaNaPasta(_pasta));
    }

    [Fact]
    public void Caminho_com_aspas_nao_entra_na_linha_de_comando()
    {
        Assert.Throws<ArgumentException>(() => NetFx35.ArgumentosDoDism("C:\\a\" /Outra"));
    }
}
