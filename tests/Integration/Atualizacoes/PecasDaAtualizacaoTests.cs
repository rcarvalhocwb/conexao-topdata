using System.Security.Cryptography;
using System.Text;
using Edge.Supervisor.Atualizacoes;
using Rayzer.Suporte;

namespace Integration.Tests.Atualizacoes;

/// <summary>As peças da atualização automática (docs/60 §2): ficha, quando instalar, script e guarda do SDK.</summary>
public sealed class PecasDaAtualizacaoTests : IDisposable
{
    private static readonly DateTimeOffset Meiodia = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TresDaManha = new(2026, 10, 16, 6, 0, 0, TimeSpan.Zero);

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "xacess-pecas-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, null, null)] // meio-dia, catraca acabou de ler
    [InlineData(2, null, null)] // 03:00 mas leitura há 2 min
    [InlineData(2, 31, "Madrugada")]
    [InlineData(3, 31, null)] // 05:00 em diante não
    public void Madrugada_so_com_as_catracas_paradas_ha_30_minutos(int caso, int? minutosParada, string? esperado)
    {
        var agora = caso switch
        {
            0 => Meiodia,
            3 => TresDaManha.AddHours(2),
            _ => TresDaManha,
        };
        var leitura = minutosParada is { } m ? agora.AddMinutes(-m) : agora.AddMinutes(-2);

        Assert.Equal(esperado, QuandoInstalar.Decidir(agora, leitura, null, licencaBloqueada: false)?.ToString());
    }

    [Fact]
    public void Painel_finalizado_instala_com_5_minutos_parada_e_vale_por_uma_hora()
    {
        Assert.Equal(MotivoDaInstalacao.SistemaFinalizado, QuandoInstalar.Decidir(Meiodia, Meiodia.AddMinutes(-6), Meiodia, false));
        Assert.Null(QuandoInstalar.Decidir(Meiodia, Meiodia.AddMinutes(-4), Meiodia, false));
        Assert.Null(QuandoInstalar.Decidir(Meiodia.AddMinutes(61), Meiodia.AddMinutes(-6), Meiodia, false));
        Assert.Equal(MotivoDaInstalacao.SistemaFinalizado, QuandoInstalar.Decidir(Meiodia, null, Meiodia, false));
        Assert.Null(QuandoInstalar.Decidir(TresDaManha, null, TresDaManha, licencaBloqueada: true));
    }

    [Fact]
    public void Ficha_assinada_pela_ferramenta_e_aceita_e_nome_de_msi_com_caminho_e_recusado()
    {
        var (privada, publica) = FerramentaDoSuporte.GerarChaves("senha-de-teste-forte");
        var ficha = new FichaDaVersao("rayzer-xacess", "producao", new Version(0, 1, 300), "RayzerXAcess-0.1.300.msi", new string('a', 64), 10);
        var bytes = Encoding.UTF8.GetBytes(ficha.ParaJson());

        var lida = FichaDaVersao.Ler(bytes, FerramentaDoSuporte.AssinarArquivo(privada, "senha-de-teste-forte", bytes), [publica], out var problema);
        Assert.Null(problema);
        Assert.Equal(ficha, lida);

        var ruim = Encoding.UTF8.GetBytes((ficha with { Msi = @"..\..\Windows\algo.msi" }).ParaJson());
        Assert.Null(FichaDaVersao.Ler(ruim, FerramentaDoSuporte.AssinarArquivo(privada, "senha-de-teste-forte", ruim), [publica], out problema));
        Assert.Contains("incompleta", problema, StringComparison.Ordinal);

        Assert.Null(FichaDaVersao.Ler(bytes, "nao-e-base64", [publica], out problema));
        Assert.Contains("assinatura", problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_roda_o_msiexec_em_silencio_grava_o_resultado_e_apaga_a_tarefa()
    {
        var script = InstaladorPorTarefaAgendada.Script(@"C:\dados\atualizacoes\0.1.300\RayzerXAcess-0.1.300.msi", @"C:\dados\atualizacoes\0.1.300", new Version(0, 1, 300));

        Assert.Contains(@"/i ""C:\dados\atualizacoes\0.1.300\RayzerXAcess-0.1.300.msi"" /qn /norestart /l*v", script, StringComparison.Ordinal);
        Assert.Contains("resultado.txt", script, StringComparison.Ordinal);
        Assert.Contains($"/Delete /TN \"{InstaladorPorTarefaAgendada.NomeDaTarefa}\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Guarda_do_SDK_repoe_o_que_a_atualizacao_apagou_e_ignora_nomes_com_caminho()
    {
        var catracas = Directory.CreateDirectory(Path.Combine(_pasta, "Worker")).FullName;
        var guarda = Path.Combine(_pasta, "sdk");
        File.WriteAllBytes(Path.Combine(catracas, GuardaDoSdk.DllPrincipal), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(catracas, "Apoio.dll"), [4, 5]);
        File.WriteAllLines(Path.Combine(catracas, GuardaDoSdk.Lista), ["Apoio.dll", @"..\fora.dll", ""]);

        Assert.Equal(2, GuardaDoSdk.Guardar(catracas, guarda));
        Assert.Equal(0, GuardaDoSdk.Guardar(catracas, guarda));

        // A atualização apagou a DLL que era do MSI antigo.
        File.Delete(Path.Combine(catracas, GuardaDoSdk.DllPrincipal));
        File.Delete(Path.Combine(catracas, "Apoio.dll"));

        Assert.Equal(2, GuardaDoSdk.Repor(catracas, guarda));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(catracas, GuardaDoSdk.DllPrincipal)));
        Assert.False(File.Exists(Path.Combine(_pasta, "fora.dll")));
        Assert.Equal(0, GuardaDoSdk.Repor(catracas, guarda));
    }

    [Fact]
    public void Sem_SDK_na_pasta_nada_e_guardado_nem_reposto()
    {
        var catracas = Directory.CreateDirectory(Path.Combine(_pasta, "Worker")).FullName;
        var guarda = Path.Combine(_pasta, "sdk");

        Assert.Equal(0, GuardaDoSdk.Guardar(catracas, guarda));
        Assert.Equal(0, GuardaDoSdk.Repor(catracas, guarda));
        Assert.False(Directory.Exists(guarda));
    }

    [Fact]
    public void Versao_do_programa_tem_tres_partes()
    {
        Assert.Equal(new Version(0, 1, 215), FichaDaVersao.VersaoDe("0.1.215"));
        Assert.Equal(new Version(1, 0, 0), FichaDaVersao.VersaoDe("1.0"));
        Assert.Null(FichaDaVersao.VersaoDe("teste"));
    }
}
