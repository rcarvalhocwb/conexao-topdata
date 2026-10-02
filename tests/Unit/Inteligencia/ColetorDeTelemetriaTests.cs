using System.Diagnostics;
using Access.Infrastructure.SQLite;
using Edge.Worker;
using Xunit;

namespace Tests.Unit.Inteligencia;

/// <summary>Testes para o coletor mínimo (Etapa I.1): anel limitado, enfileiramento O(1), nunca bloqueia.</summary>
public sealed class ColetorDeTelemetriaTests : IDisposable
{
    private readonly string _caminhoTemporario;
    private readonly FabricaDaTelemetria _fabrica;

    public ColetorDeTelemetriaTests()
    {
        _caminhoTemporario = Path.Combine(Path.GetTempPath(), $"test-telemetria-{Guid.NewGuid()}.db");
        _fabrica = new FabricaDaTelemetria(_caminhoTemporario);

        // Aplica as migrações
        var migrador = new MigradorDaTelemetria(_fabrica);
        migrador.Aplicar();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_caminhoTemporario))
            {
                File.Delete(_caminhoTemporario);
            }
        }
        catch { }
    }

    [Fact]
    public void Coletor_Nulo_NuncaBloqueiaOuEscreve()
    {
        var coletor = ColetorDeTelemetria.Nulo;

        // Todas as operações devem ser instantâneas
        var cronometro = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            coletor.Enfileirar(new SinalDaOperacao(
                Guid.NewGuid().ToString(),
                1,
                TipoDeSinal.Origem,
                OrigemBruta: 2));
            coletor.ContarErroDeRecepcao(1);
            coletor.ContarLeituraVazia(1);
            coletor.ContarOrigemDesconhecida(1);
            coletor.RegistrarLatenciaDecisao(1, 10);
            coletor.RegistrarLatenciaRecepcao(1, 5);
            coletor.ContarVoltaDoLaco(1);
        }
        cronometro.Stop();

        // 10k operações devem ser < 10 ms (verificando overhead zero)
        Assert.True(cronometro.ElapsedMilliseconds < 100, $"Coletor nulo levou {cronometro.ElapsedMilliseconds} ms");

        var (desc, disc) = coletor.Descarregar("session-test", "worker-test");
        Assert.Equal(0, desc);
        Assert.Equal(0, disc);
    }

    [Fact]
    public void AelIntensoComSubstituicao_NuncaBloqueiaEContaDescartes()
    {
        var coletor = new ColetorDeTelemetriaReal(_fabrica);
        var sinaisEnfieirados = new List<string>();

        // Enfileira mais que a capacidade do anel (4096)
        var cronometro = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var id = $"sinal-{i:D5}";
            sinaisEnfieirados.Add(id);
            coletor.Enfileirar(new SinalDaOperacao(
                id,
                (i % 20) + 1,    // Catracas 1-20
                TipoDeSinal.Origem,
                OrigemBruta: i % 256));
        }
        cronometro.Stop();

        // Nunca deve bloquear: 10k enfileiramentos em < 50 ms
        Assert.True(cronometro.ElapsedMilliseconds < 50, $"Enfileiramento levou {cronometro.ElapsedMilliseconds} ms");

        // Descarrega o que conseguiu guardar
        var (desc, disc) = coletor.Descarregar("session-1", "worker-1");

        // Deve ter descartado os primeiros 6000 (10000 - 4096)
        Assert.Equal(4096, desc);
        Assert.Equal(6000, disc);  // Os antigos foram substituídos
    }

    [Fact]
    public void ContadoresAtomicos_SemCorrida()
    {
        var coletor = new ColetorDeTelemetriaReal(_fabrica);
        var tarefas = new List<Task>();

        // 10 threads incrementando contadores em paralelo
        for (int t = 0; t < 10; t++)
        {
            tarefas.Add(Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    coletor.ContarErroDeRecepcao(1);
                    coletor.ContarLeituraVazia(2);
                    coletor.RegistrarLatenciaDecisao(3, 10 + i % 100);
                }
            }));
        }

        Task.WaitAll(tarefas.ToArray());

        // Descarrega e verifica se os contadores estão corretos
        var (desc, disc) = coletor.Descarregar("session-test", "worker-test");

        // Todos devem estar gravados (nenhum sinal foi enfieirado)
        Assert.Equal(0, desc);
        Assert.Equal(0, disc);
    }

    [Fact]
    public void DescarregaSinaisEContadoresEmHistogramas()
    {
        var coletor = new ColetorDeTelemetriaReal(_fabrica);

        // Enfileira alguns sinais
        for (int i = 0; i < 100; i++)
        {
            coletor.Enfileirar(new SinalDaOperacao(
                $"sinal-{i:D3}",
                1,
                TipoDeSinal.Origem,
                OrigemBruta: 2 + (i % 5),
                Complemento: i % 256));
        }

        // Registra latências (para histograma)
        for (int i = 0; i < 50; i++)
        {
            coletor.RegistrarLatenciaDecisao(1, 5 + i * 2);
            coletor.RegistrarLatenciaRecepcao(1, 3 + i);
            coletor.RegistrarLatenciaVolta(1, 10 + i * 5);
        }

        coletor.ContarErroDeRecepcao(1);
        coletor.ContarErroDeRecepcao(1);
        coletor.ContarLeituraVazia(1);
        coletor.ContarOrigemDesconhecida(1);
        coletor.ContarVoltaDoLaco(1);
        coletor.ContarDecisao(1);
        coletor.ContarReconexao(1);
        coletor.RegistrarSegundoEmOperacao(1);
        coletor.RegistrarDesvioDoRelogio(1, 10);

        // Descarrega
        var (desc, disc) = coletor.Descarregar("session-1", "worker-1");

        Assert.Equal(100, desc);
        Assert.Equal(0, disc);

        // Verifica se foram gravados em device_signal
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM device_signal WHERE inner_number = 1 AND kind = 'origem';";
        var contagemSinais = (long)comando.ExecuteScalar()!;
        Assert.Equal(100, contagemSinais);

        // Verifica health_minute
        comando.CommandText = "SELECT COUNT(*) FROM health_minute WHERE inner_number = 1;";
        var contagemSaude = (long)comando.ExecuteScalar()!;
        Assert.Equal(1, contagemSaude);

        // Verifica coluna recv_errors (deve ser 2)
        comando.CommandText = "SELECT recv_errors FROM health_minute WHERE inner_number = 1;";
        var erros = (long)comando.ExecuteScalar()!;
        Assert.Equal(2, erros);

        // Verifica histograma (JSON)
        comando.CommandText = "SELECT decision_ms_hist FROM health_minute WHERE inner_number = 1;";
        var json = (string)comando.ExecuteScalar()!;
        Assert.NotNull(json);
        Assert.Contains("\"", json);  // Tem JSON válido
    }

    [Fact]
    public void AelCheioEDescarta_SemBloqueio()
    {
        var coletor = new ColetorDeTelemetriaReal(_fabrica);

        // Preenche completamente o anel
        var sinaisPrimeiros = new List<string>();
        for (int i = 0; i < 4096; i++)
        {
            var id = $"sinal-{i:D5}";
            sinaisPrimeiros.Add(id);
            coletor.Enfileirar(new SinalDaOperacao(id, 1, TipoDeSinal.Origem));
        }

        // Agora tenta enfileirar mais (deve descartar os antigos)
        var cronometro = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            coletor.Enfileirar(new SinalDaOperacao($"sinal-novo-{i}", 1, TipoDeSinal.Origem));
        }
        cronometro.Stop();

        // Nunca deve bloquear
        Assert.True(cronometro.ElapsedMilliseconds < 10, $"Descarte levou {cronometro.ElapsedMilliseconds} ms");

        // Descarrega
        var (desc, disc) = coletor.Descarregar("session-1", "worker-1");

        // 4096 do anel + 1000 novos, mas só 4096 cabem
        Assert.Equal(4096, desc);
        // 1000 descartados (os novos substituíram alguns dos primeiros)
        Assert.Equal(1000, disc);
    }

    [Fact]
    public void HistogramaDeBaldes_Registra_E_Serializa()
    {
        var hist = new HistogramaDeBaldes();

        // Registra vários valores em diferentes baldes
        for (int i = 0; i < 100; i++)
        {
            hist.Registrar(i);      // 0-99 ms
        }

        for (int i = 0; i < 50; i++)
        {
            hist.Registrar(500 + i * 10);  // 500+ ms
        }

        for (int i = 0; i < 30; i++)
        {
            hist.Registrar(2000 + i * 100);  // 2000+ ms
        }

        Assert.Equal(180, hist.Total());

        // Verifica percentis
        var p50 = hist.ObterPercentil(50);
        var p95 = hist.ObterPercentil(95);
        var p99 = hist.ObterPercentil(99);

        Assert.True(p50 > 0 && p50 <= 100);
        Assert.True(p95 > p50);
        Assert.True(p99 > p95);

        // Serializa e desserializa
        var json = hist.SerializarParaJson();
        Assert.NotNull(json);
        Assert.Contains("{", json);

        var hist2 = HistogramaDeBaldes.DessSerializarDeJson(json);
        Assert.Equal(hist.Total(), hist2.Total());
    }

    [Fact]
    public void MultiplosDescarregamentosNoMesmoMinuto_Somam()
    {
        var coletor = new ColetorDeTelemetriaReal(_fabrica);

        // Primeiro descarregamento
        coletor.ContarErroDeRecepcao(1);
        coletor.ContarErroDeRecepcao(1);
        coletor.RegistrarLatenciaDecisao(1, 10);
        var (desc1, disc1) = coletor.Descarregar("session-1", "worker-1");

        // Segundo descarregamento no mesmo minuto
        coletor.ContarErroDeRecepcao(1);
        coletor.RegistrarLatenciaDecisao(1, 20);
        var (desc2, disc2) = coletor.Descarregar("session-1", "worker-1");

        // Verifica que foram agregados (INSERT OR REPLACE)
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT recv_errors FROM health_minute WHERE inner_number = 1;";
        var erros = (long)comando.ExecuteScalar()!;

        // Deve ter 3 (2 + 1 do segundo descarregamento), não 2
        Assert.Equal(3, erros);
    }
}
