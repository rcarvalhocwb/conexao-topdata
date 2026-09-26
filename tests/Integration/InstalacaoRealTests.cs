using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>O que muda quando o serviço roda instalado, e não num terminal de desenvolvedor.</summary>
public sealed class InstalacaoRealTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
            // Sobra de arquivo temporário (ou pasta nunca criada) não reprova o teste.
        }
    }

    [Fact]
    public void O_token_e_gerado_na_primeira_subida_e_reaproveitado_nas_seguintes()
    {
        Assert.Null(Environment.GetEnvironmentVariable("EDGE_TOKEN"));
        var arquivo = Path.Combine(_pasta, "token");

        var primeiro = SegurancaLocal.GarantirToken(arquivo);
        var segundo = SegurancaLocal.GarantirToken(arquivo);

        // Trocar a cada subida desconectaria o painel à toa.
        Assert.Equal(primeiro, segundo);
        Assert.True(Convert.FromBase64String(primeiro).Length >= 32);
        Assert.Equal(primeiro, Contracts.InstalacaoLocal.LerToken(arquivo));
    }

    [Fact]
    public void Caminho_relativo_do_worker_e_relativo_a_pasta_do_servico_e_nao_a_pasta_atual()
    {
        // Como serviço do Windows, a pasta atual é System32.
        var resolvido = ConfiguracaoDoSupervisor.ResolverExecutavel(Path.Combine("..", "Worker", "Edge.Worker.X86.exe"));

        Assert.True(Path.IsPathRooted(resolvido));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Worker", "Edge.Worker.X86.exe")),
            resolvido);

        var absoluto = Path.Combine(_pasta, "Edge.Worker.X86.exe");
        Assert.Equal(absoluto, ConfiguracaoDoSupervisor.ResolverExecutavel(absoluto));
    }

    [Fact]
    public async Task Servico_sem_configuracao_faz_o_painel_pedir_o_assistente()
    {
        var supervisor = new WorkerSupervisor([]);
        var servico = new EdgeControlService(supervisor, semConfiguracao: true);

        var estado = EstadoDoPainel.De(await servico.ObterEstado(new ObterEstadoRequest(), null!), DateTimeOffset.UtcNow);

        Assert.Equal(SaudeDoPainel.Acao, estado.Saude);
        Assert.Equal("Instalação ainda não configurada — abra o Assistente de configuração", estado.Mensagem);
    }
}
