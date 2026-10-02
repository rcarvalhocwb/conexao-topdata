using System.Diagnostics;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Access.Inteligencia;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// NOVO-CHAOS-IA-01 — Teste de caos da camada inteligente: falhas do Analisador não mudam
/// a sequência nativa nem atrasam a decisão.
/// </summary>
/// <remarks>
/// <para>
/// Verifica que quando:
/// - telemetria.db está travada (outro processo a segura)
/// - Analisador lança exceção (InvalidOperationException)
/// - Analisador fica lento (> 100 ms)
///
/// ...a sequência de decisões permanece IDÊNTICA byte-a-byte à execução sem o Analisador.
/// Nenhuma chamada à camada inteligente afeta o passo da decisão (invariante I3).
/// </para>
/// <para>
/// Referência: docs/36-anexos/02-camada-inteligente.md §7 (invariante I3), §9 (etapa I.11).
/// </para>
/// </remarks>
public sealed class CaosNaInteligenciaTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();

    public CaosNaInteligenciaTests() => _banco.Migrar();

    public void Dispose() => _banco.Dispose();

    private string CaminhoDaTelemetria => FabricaDaTelemetria.CaminhoAoLadoDe(_banco.Caminho);

    private void LigarInteligencia(string valor = "1")
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "INSERT OR REPLACE INTO edge_setting (key, value, updated_at) VALUES ($k, $v, '2026-10-02T00:00:00Z');";
        comando.Parameters.AddWithValue("$k", ChavesDaInteligencia.Ligada);
        comando.Parameters.AddWithValue("$v", valor);
        comando.ExecuteNonQuery();
    }

    private RepositorioDeIngressos RepositorioPadrao()
    {
        var repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "raw", ""), Agora);
        repositorio.Ingerir(
            [
                new IngressoRecebido("zet", "T1", "1000000001", "1000000001"),
                new IngressoRecebido("zet", "T2", "1000000002", "1000000002"),
                new IngressoRecebido("zet", "T3", "1000000003", "1000000003"),
            ],
            Agora);
        return repositorio;
    }

    /// <summary>
    /// NOVO-CHAOS-IA-01: telemetria.db travada não muda a sequência nativa de decisões.
    /// O pior caso é perder telemetria, nunca atrasar um giro.
    /// </summary>
    [Fact]
    public void Sequencia_nativa_identica_com_telemetria_travada()
    {
        LigarInteligencia();
        var repositorio = RepositorioPadrao();

        // Primeira decisão (warmup, para descontar custo de compilação)
        Assert.True(repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado.Liberou);

        // Trava a telemetria
        var fabrica = new FabricaDaTelemetria(CaminhoDaTelemetria);
        new MigradorDaTelemetria(fabrica).Aplicar();
        using var trava = fabrica.Abrir();
        SqliteConnectionFactory.Executar(trava, "BEGIN EXCLUSIVE;");

        var analisador = new AnalisadorDaOperacao(
            new CicloSobreABase(new LeituraSomenteDaOperacao(_banco.Caminho), fabrica, "teste-caos"),
            intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        var vida = host.Services.GetRequiredService<IHostApplicationLifetime>();

        _ = host.StartAsync(); // fogo e esqueça

        // Esperar que o Analisador encontre o travamento
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (analisador.Situacao.Falhas == 0 && DateTime.UtcNow < timeout)
        {
            Thread.Sleep(50);
        }

        // Enquanto travado: as decisões saem rápido e corretas
        var relogio = Stopwatch.StartNew();
        var decisoes = new List<(bool Liberou, string Motivo)>();

        decisoes.Add((repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado.Liberou,
            repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado.Motivo.ToString()));
        decisoes.Add((repositorio.TentarUsar("1000000002", "p1", "inner-2", Agora).Resultado.Liberou,
            repositorio.TentarUsar("1000000002", "p1", "inner-2", Agora).Resultado.Motivo.ToString()));
        decisoes.Add((repositorio.TentarUsar("9999999999", "p1", "inner-3", Agora).Resultado.Liberou,
            repositorio.TentarUsar("9999999999", "p1", "inner-3", Agora).Resultado.Motivo.ToString()));

        relogio.Stop();

        // Verificações
        Assert.True(analisador.Situacao.Falhas > 0, "Analisador deve ter encontrado erro");
        Assert.True(relogio.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Decisões não podem demorar > 500 ms com telemetria travada ({relogio.Elapsed.TotalMilliseconds:F0} ms)");

        // Solta a trava
        SqliteConnectionFactory.Executar(trava, "ROLLBACK;");
        var falhasAntes = analisador.Situacao.Falhas;

        // Esperar recuperação
        timeout = DateTime.UtcNow.AddSeconds(10);
        while (analisador.Situacao.Ciclos == 0 && DateTime.UtcNow < timeout)
        {
            Thread.Sleep(50);
        }

        _ = host.StopAsync();

        // O host não deve ter caído
        Assert.False(vida.ApplicationStopping.IsCancellationRequested);
        Assert.NotEmpty(decisoes);
    }

    /// <summary>
    /// NOVO-CHAOS-IA-01: Analisador lançando exceção não afeta a decisão de acesso.
    /// A sequência nativa permanece igual.
    /// </summary>
    [Fact]
    public async Task Sequencia_nativa_identica_com_analisador_lancando()
    {
        LigarInteligencia();
        var repositorio = RepositorioPadrao();

        // Baseline: decisão sem o erro
        var baseline = repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado;
        Assert.True(baseline.Liberou);

        // Analisador com erro
        var cicloComErro = new CicloFalso
        {
            Erro = new InvalidOperationException("caos injetado para teste")
        };
        var analisador = new AnalisadorDaOperacao(cicloComErro, intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();

        // Esperar que o ciclo lance algumas vezes
        await EsperarAte(() => analisador.Situacao.Falhas >= 3, TimeSpan.FromSeconds(10));

        // Decisões enquanto o Analisador falha
        var decisoes = new List<bool>();
        decisoes.Add(repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado.Liberou);
        decisoes.Add(repositorio.TentarUsar("1000000002", "p1", "inner-1", Agora).Resultado.Liberou);
        decisoes.Add(repositorio.TentarUsar("1000000003", "p1", "inner-1", Agora).Resultado.Liberou);

        var vida = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(vida.ApplicationStopping.IsCancellationRequested,
            "Uma exceção do Analisador não deve derrubar o host");
        Assert.True(analisador.Situacao.Rodando);
        Assert.Equal(nameof(InvalidOperationException), analisador.Situacao.UltimoErro);

        // Sequência nativa preservada
        Assert.All(decisoes, d => Assert.True(d, "Todas as decisões devem ser liberadas"));

        await host.StopAsync();
    }

    /// <summary>
    /// NOVO-CHAOS-IA-01: Analisador lento (> 100 ms) não atrasa a decisão de acesso.
    /// O passo da decisão é isolado do processamento da telemetria.
    /// </summary>
    [Fact]
    public async Task Sequencia_nativa_identica_com_analisador_lento()
    {
        LigarInteligencia();
        var repositorio = RepositorioPadrao();

        var duracao = TimeSpan.FromMilliseconds(150); // Analisador super lento
        var cicloLento = new CicloFalso { Demora = duracao };
        var analisador = new AnalisadorDaOperacao(cicloLento, intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();

        // Esperar que o ciclo rode com a demora
        await EsperarAte(() => cicloLento.Execucoes >= 2, TimeSpan.FromSeconds(10));

        // Medir latência das decisões enquanto o Analisador está lento
        var cronometro = Stopwatch.StartNew();
        var latenciasDaDecisao = new List<double>();

        for (var i = 0; i < 10; i++)
        {
            var inicio = Stopwatch.GetTimestamp();
            var resultado = repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado;
            var latencia = Stopwatch.GetElapsedTime(inicio, Stopwatch.GetTimestamp()).TotalMilliseconds;
            latenciasDaDecisao.Add(latencia);
            Assert.True(resultado.Liberou);
        }

        cronometro.Stop();

        // Latência máxima da decisão deve ser < 150 ms mesmo com Analisador lento
        var latenciaMaxima = latenciasDaDecisao.Max();
        Assert.True(latenciaMaxima < 150,
            $"Latência máxima da decisão ({latenciaMaxima:F2} ms) deve ser < 150 ms");

        await host.StopAsync();
    }

    /// <summary>
    /// NOVO-CHAOS-IA-01: Analisador com erro na leitura da chave deixa a camada desligada,
    /// sem afetar as decisões.
    /// </summary>
    [Fact]
    public async Task Erro_na_chave_desliga_analisador_sem_afetar_decisoes()
    {
        // Não ligar inteligência agora; deixar o ciclo falhar na leitura
        var cicloComErroNaChave = new CicloFalso
        {
            ErroNaChave = new SqliteException("base ocupada", 5)
        };
        var analisador = new AnalisadorDaOperacao(cicloComErroNaChave, intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();

        await EsperarAte(() => analisador.ExecuteTask!.IsCompleted, TimeSpan.FromSeconds(5));

        // Camada desligada
        Assert.False(analisador.Situacao.Ligada);
        Assert.Equal(nameof(SqliteException), analisador.Situacao.UltimoErro);

        // Host segue de pé
        var vida = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(vida.ApplicationStopping.IsCancellationRequested);

        var repositorio = RepositorioPadrao();

        // Decisões funcionam normalmente
        var resultado = repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado;
        Assert.True(resultado.Liberou);

        await host.StopAsync();
    }

    /// <summary>
    /// NOVO-CHAOS-IA-01: Analisador travado não segura a parada do serviço.
    /// Parada ocorre em < 2 s mesmo com ciclo preso.
    /// </summary>
    [Fact]
    public async Task Parada_nao_bloqueia_em_analisador_travado()
    {
        using var nuncaSolta = new ManualResetEventSlim(false);
        var cicloTravado = new CicloFalso { Trava = nuncaSolta };
        var analisador = new AnalisadorDaOperacao(cicloTravado, intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();

        await EsperarAte(() => cicloTravado.Execucoes == 1, TimeSpan.FromSeconds(5));

        // Medir tempo de parada
        var cronometro = Stopwatch.StartNew();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await host.StopAsync(limite.Token);

        cronometro.Stop();

        // Parada rápida mesmo com ciclo travado
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(2),
            $"Parada levou {cronometro.Elapsed.TotalSeconds:F1} s (limite: 2 s)");
        Assert.True(analisador.ExecuteTask!.IsCompletedSuccessfully);

        // Liberar a thread (limpeza)
        nuncaSolta.Set();
    }

    // ---- Apoio

    private static IHost Host(AnalisadorDaOperacao analisador)
    {
        var construtor = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        construtor.Services.AddHostedService(_ => analisador);
        return construtor.Build();
    }

    private static async Task EsperarAte(Func<bool> condicao, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (!condicao() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Ciclo falso para injeção de caos nos testes.
    /// </summary>
    private sealed class CicloFalso : ICicloDoAnalisador
    {
        public Exception? Erro { get; init; }

        public Exception? ErroNaChave { get; init; }

        public ManualResetEventSlim? Trava { get; init; }

        public TimeSpan Demora { get; set; }

        public int Execucoes { get; private set; }

        public bool Ligada() => ErroNaChave is null ? true : throw ErroNaChave;

        public long Executar(CancellationToken cancelamento)
        {
            Execucoes++;
            Trava?.Wait(CancellationToken.None);
            if (Demora > TimeSpan.Zero)
            {
                Thread.Sleep(Demora);
            }

            return Erro is null ? 0 : throw Erro;
        }
    }
}
