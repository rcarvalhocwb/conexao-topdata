using Access.Infrastructure.SQLite;
using Access.Application.Ingressos;
using Edge.Worker.Bancada;
using Edge.Worker.Operacao;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Achado E1-03 do docs/41: com as catracas fora do ar, nenhum passo chama a DLL e a volta termina
/// na hora. O laço girava sem pausa (um núcleo a 100%) e gravava uma linha por volta e por catraca,
/// num arquivo de registro sem teto, no mesmo disco da base local.
/// </summary>
public sealed class LacoOciosoTests : IDisposable
{
    private readonly BancoTemporario _banco = new();

    public LacoOciosoTests() => _banco.Migrar();

    public void Dispose() => _banco.Dispose();

    private SessaoDeOperacao Sessao(InnerSimulator simulador, List<string> registro, Func<DateTimeOffset> relogio) =>
        new(
            simulador, [1, 2], ConfiguracaoDeBancada.TopFit4(), new DecisorDeIngresso(new RepositorioDeIngressos(_banco.Fabrica)),
            registro.Add,
            _ => { },
            relogio: relogio);

    [Fact]
    public void Com_todas_as_catracas_fora_do_ar_o_laco_pausa_e_nao_repete_a_mesma_linha()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.Zero);
        using var simulador = new InnerSimulator(() => agora);
        simulador.Dispositivo(1).Desconectado = true;
        simulador.Dispositivo(2).Desconectado = true;
        var registro = new List<string>();
        var sessao = Sessao(simulador, registro, () => agora);
        sessao.Iniciar(3570);

        // A primeira volta tenta conectar e falha: dali em diante, as duas esperam o backoff.
        sessao.UmaVolta();
        var pausa = sessao.PausaSugerida();
        Assert.NotNull(pausa);
        Assert.True(pausa > TimeSpan.Zero && pausa <= SessaoDeOperacao.PausaMaxima);

        var linhasAntes = registro.Count;
        for (var i = 0; i < 500; i++)
        {
            sessao.UmaVolta();
        }

        // 500 voltas esperando o backoff: no máximo uma linha por catraca, não mil.
        Assert.True(registro.Count - linhasAntes <= 2, $"{registro.Count - linhasAntes} linhas em 500 voltas ociosas");
    }

    [Fact]
    public void Catraca_atendendo_nao_pausa_o_laco()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.Zero);
        using var simulador = new InnerSimulator(() => agora);
        var registro = new List<string>();
        var sessao = Sessao(simulador, registro, () => agora);
        sessao.Iniciar(3570);

        for (var i = 0; i < 30; i++)
        {
            sessao.UmaVolta();
        }

        Assert.Null(sessao.PausaSugerida());
    }

    [Fact]
    public void Registro_do_dia_para_de_crescer_no_teto_e_diz_por_que()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));
        var registro = new RegistroEmArquivo(pasta, "worker-teste", limiteDiario: 1024);

        for (var i = 0; i < 200; i++)
        {
            registro.Escrever($"linha {i} que se repete por causa de uma falha");
        }

        var arquivo = Directory.EnumerateFiles(pasta).Single();
        var conteudo = File.ReadAllText(arquivo);
        Assert.True(new FileInfo(arquivo).Length < 1024 + 256);
        Assert.Contains("atingiu o teto", conteudo, StringComparison.Ordinal);
        Assert.True(registro.Perdidas > 150);

        Directory.Delete(pasta, recursive: true);
    }
}
