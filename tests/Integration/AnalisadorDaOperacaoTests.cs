using System.Diagnostics;
using System.Text.RegularExpressions;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Access.Inteligencia;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Integration.Tests;

/// <summary>
/// Etapa I.0 do docs/36: a fundação da camada inteligente — o Analisador, a base da operação só
/// para leitura, <c>telemetria.db</c> e a chave desligada. Desenho e invariantes em
/// docs/36-anexos/02 §3; provas da §7 (<c>NOVO-ARQ-IA-02</c>, <c>NOVO-CHAOS-IA-01</c>).
/// </summary>
public sealed partial class AnalisadorDaOperacaoTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();

    public AnalisadorDaOperacaoTests() => _banco.Migrar();

    public void Dispose() => _banco.Dispose();

    private string CaminhoDaTelemetria => FabricaDaTelemetria.CaminhoAoLadoDe(_banco.Caminho);

    private void Ligar(string valor = "1")
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "INSERT OR REPLACE INTO edge_setting (key, value, updated_at) VALUES ($k, $v, '2026-10-02T00:00:00Z');";
        comando.Parameters.AddWithValue("$k", ChavesDaInteligencia.Ligada);
        comando.Parameters.AddWithValue("$v", valor);
        comando.ExecuteNonQuery();
    }

    private CicloSobreABase CicloReal(TimeProvider? relogio = null) =>
        new(new LeituraSomenteDaOperacao(_banco.Caminho), new FabricaDaTelemetria(CaminhoDaTelemetria), "sessao-teste", relogio);

    private RepositorioDeIngressos Repositorio()
    {
        var repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "raw", ""), Agora);
        repositorio.Ingerir(
            [
                new IngressoRecebido("zet", "T1", "1000000001", "1000000001"),
                new IngressoRecebido("zet", "T2", "1000000002", "1000000002"),
            ],
            Agora);
        return repositorio;
    }

    // ---------------------------------------------------------------- chave desligada

    [Fact]
    public void Sem_a_chave_o_analisador_esta_desligado()
    {
        Assert.False(CicloReal().Ligada());
        Ligar("sim");
        Assert.False(CicloReal().Ligada());
        Ligar("1");
        Assert.True(CicloReal().Ligada());
    }

    [Fact]
    public async Task Desligado_o_analisador_nao_roda_nem_cria_a_telemetria()
    {
        var analisador = new AnalisadorDaOperacao(CicloReal(), intervalo: TimeSpan.FromMilliseconds(10));

        await analisador.StartAsync(CancellationToken.None);
        await EsperarAte(() => analisador.ExecuteTask!.IsCompleted, TimeSpan.FromSeconds(5));
        await analisador.StopAsync(CancellationToken.None);

        var situacao = analisador.Situacao;
        Assert.False(situacao.Ligada);
        Assert.False(situacao.Rodando);
        Assert.Equal(0, situacao.Ciclos);
        Assert.False(File.Exists(CaminhoDaTelemetria), "Desligada, a camada não cria telemetria.db.");
    }

    // ---------------------------------------------------------------- NOVO-ARQ-IA-02

    [Fact]
    public void NOVO_ARQ_IA_02_a_base_da_operacao_e_aberta_so_para_leitura()
    {
        var leitura = new LeituraSomenteDaOperacao(_banco.Caminho);

        Assert.Equal(SqliteOpenMode.ReadOnly, new SqliteConnectionStringBuilder(leitura.TextoDeConexao).Mode);

        using var conexao = leitura.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "INSERT INTO edge_setting (key, value, updated_at) VALUES ('x', '1', 'y');";

        var erro = Assert.Throws<SqliteException>(() => comando.ExecuteNonQuery());
        Assert.Contains("readonly", erro.Message.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NOVO_ARQ_IA_02_o_ciclo_le_a_operacao_e_grava_so_na_telemetria()
    {
        Ligar();
        var repositorio = Repositorio();
        var esquemaAntes = Esquema(_banco.Fabrica.Abrir());

        var relogio = new RelogioManual(Agora);
        var ciclo = CicloReal(relogio);

        Assert.Equal(0, ciclo.Executar(CancellationToken.None)); // começa do fim
        repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora);
        repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora);
        repositorio.TentarUsar("9999999999", "p1", "inner-2", Agora);
        relogio.Agora = Agora.AddSeconds(1);
        Assert.Equal(3, ciclo.Executar(CancellationToken.None));
        Assert.Equal(0, ciclo.Executar(CancellationToken.None));

        // acesso.db: nenhuma tabela nova, nenhuma linha da camada.
        Assert.Equal(esquemaAntes, Esquema(_banco.Fabrica.Abrir()));

        // telemetria.db: os ciclos, com o relógio injetado.
        var telemetria = new FabricaDaTelemetria(CaminhoDaTelemetria);
        Assert.Equal(3, new CadernoDoAnalisador(telemetria).Contar());
        using var conexao = telemetria.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT finished_at, new_attempts, session_id FROM analyzer_cycle ORDER BY id DESC LIMIT 1 OFFSET 1;";
        using var leitor = comando.ExecuteReader();
        Assert.True(leitor.Read());
        Assert.Equal("2026-10-02T22:00:01.0000000+00:00", leitor.GetString(0));
        Assert.Equal(3, leitor.GetInt64(1));
        Assert.Equal("sessao-teste", leitor.GetString(2));
    }

    [Fact]
    public void A_telemetria_e_strict_e_nao_tem_coluna_para_codigo_nem_nome()
    {
        var fabrica = new FabricaDaTelemetria(CaminhoDaTelemetria);
        var aplicadas = new MigradorDaTelemetria(fabrica).Aplicar();
        Assert.Equal("T001_caderno_do_analisador.sql", aplicadas[0]);
        Assert.Empty(new MigradorDaTelemetria(fabrica).Aplicar());

        using var conexao = fabrica.Abrir();
        var tabelas = new List<(string Nome, string Sql)>();
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                tabelas.Add((leitor.GetString(0), leitor.GetString(1)));
            }
        }

        Assert.Contains(tabelas, t => t.Nome == "analyzer_cycle");
        Assert.All(tabelas, t => Assert.Matches(StrictNoFim(), t.Sql));

        // schema_version guarda o nome do ARQUIVO de migração ("T001_..."), não de pessoa.
        foreach (var (nome, _) in tabelas.Where(t => t.Nome != "schema_version"))
        {
            using var comando = conexao.CreateCommand();
            comando.CommandText = $"SELECT name FROM pragma_table_info('{nome}');";
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                Assert.DoesNotMatch(ColunaProibida(), leitor.GetString(0));
            }
        }

        Assert.Equal("wal", SqliteConnectionFactory.Escalar<string>(conexao, "PRAGMA journal_mode;"));
        Assert.Equal(1L, SqliteConnectionFactory.Escalar<long>(conexao, "PRAGMA synchronous;")); // NORMAL
    }

    [Fact]
    public async Task Ligado_o_analisador_roda_numa_thread_de_prioridade_baixa_e_conta_os_ciclos()
    {
        Ligar();
        var relogio = new RelogioManual(Agora);
        var analisador = new AnalisadorDaOperacao(CicloReal(relogio), relogio, TimeSpan.FromMilliseconds(5),
            new OrcamentoDoCiclo(TimeSpan.FromSeconds(10)));

        await analisador.StartAsync(CancellationToken.None);
        await EsperarAte(() => analisador.Situacao.Ciclos >= 3, TimeSpan.FromSeconds(10));
        await analisador.StopAsync(CancellationToken.None);

        var situacao = analisador.Situacao;
        Assert.True(situacao.Ligada);
        Assert.False(situacao.Rodando);
        Assert.Equal(0, situacao.Falhas);
        Assert.Equal(Agora, situacao.UltimoCicloEm);
        Assert.True(File.Exists(CaminhoDaTelemetria));
    }

    // ---------------------------------------------------------------- orçamento

    [Fact]
    public void Ciclo_acima_do_orcamento_conta_estouro_e_pula_o_seguinte()
    {
        var ciclo = new CicloFalso { Demora = TimeSpan.FromMilliseconds(30) };
        var analisador = new AnalisadorDaOperacao(ciclo, orcamento: new OrcamentoDoCiclo(TimeSpan.FromMilliseconds(5)));

        analisador.UmaVolta();
        analisador.UmaVolta(); // pulada
        ciclo.Demora = TimeSpan.Zero;
        analisador.UmaVolta();

        var situacao = analisador.Situacao;
        Assert.Equal(2, ciclo.Execucoes);
        Assert.Equal(2, situacao.Ciclos);
        Assert.Equal(1, situacao.Estouros);
        Assert.Equal(1, situacao.Pulados);
    }

    // ---------------------------------------------------------------- NOVO-CHAOS-IA-01

    [Fact]
    public async Task NOVO_CHAOS_IA_01_analisador_que_lanca_nao_derruba_o_servico()
    {
        var ciclo = new CicloFalso { Erro = new InvalidOperationException("mensagem que nunca deve aparecer") };
        var analisador = new AnalisadorDaOperacao(ciclo, intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();
        await EsperarAte(() => analisador.Situacao.Falhas >= 5, TimeSpan.FromSeconds(10));

        var vida = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(vida.ApplicationStopping.IsCancellationRequested, "Uma exceção do Analisador não pode parar o serviço.");
        Assert.True(analisador.Situacao.Rodando);
        Assert.Equal(nameof(InvalidOperationException), analisador.Situacao.UltimoErro);

        // O Diagnóstico leva o tipo do erro, nunca a mensagem.
        var saude = EdgeControlService.SaudeDoAnalisadorPara(analisador.Situacao);
        Assert.DoesNotContain("mensagem", saude.ToString(), StringComparison.Ordinal);

        await host.StopAsync();
    }

    [Fact]
    public async Task NOVO_CHAOS_IA_01_analisador_travado_nao_segura_a_parada_do_servico()
    {
        using var nuncaSolta = new ManualResetEventSlim(false);
        var ciclo = new CicloFalso { Trava = nuncaSolta };
        var analisador = new AnalisadorDaOperacao(ciclo, intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();
        await EsperarAte(() => ciclo.Execucoes == 1, TimeSpan.FromSeconds(5));

        var relogio = Stopwatch.StartNew();
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await host.StopAsync(limite.Token);

        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(2), $"A parada esperou o ciclo travado ({relogio.Elapsed}).");
        Assert.True(analisador.ExecuteTask!.IsCompletedSuccessfully);
        nuncaSolta.Set(); // só para não deixar a thread presa depois do teste
    }

    [Fact]
    public async Task NOVO_CHAOS_IA_01_falha_ao_ler_a_chave_deixa_a_camada_desligada()
    {
        var analisador = new AnalisadorDaOperacao(new CicloFalso { ErroNaChave = new SqliteException("base ocupada", 5) },
            intervalo: TimeSpan.FromMilliseconds(5));

        using var host = Host(analisador);
        await host.StartAsync();
        await EsperarAte(() => analisador.ExecuteTask!.IsCompleted, TimeSpan.FromSeconds(5));

        Assert.False(analisador.Situacao.Ligada);
        Assert.Equal(nameof(SqliteException), analisador.Situacao.UltimoErro);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await host.StopAsync();
    }

    /// <summary>
    /// <c>telemetria.db</c> presa por outro processo: o ciclo do Analisador falha (e conta), e a
    /// decisão de acesso, em <c>acesso.db</c>, segue igual e rápida — o pior caso é perder
    /// telemetria (docs/36-anexos/02 §3.3).
    /// </summary>
    [Fact]
    public async Task NOVO_CHAOS_IA_01_telemetria_presa_nao_muda_nem_atrasa_a_decisao()
    {
        Ligar();
        var repositorio = Repositorio();

        // Uma decisão antes, sem o Analisador: a primeira do processo paga a compilação do caminho
        // e a primeira abertura do arquivo (medido: ~10x uma decisão normal). Sem isto o teste
        // media esse custo, e não o efeito da telemetria presa, e falhava no Windows do CI.
        Assert.True(repositorio.TentarUsar("1000000002", "p1", "inner-1", Agora).Resultado.Liberou);

        // Cria o arquivo e o prende, como outro processo faria.
        var fabrica = new FabricaDaTelemetria(CaminhoDaTelemetria);
        new MigradorDaTelemetria(fabrica).Aplicar();
        using var trava = fabrica.Abrir();
        SqliteConnectionFactory.Executar(trava, "BEGIN EXCLUSIVE;");

        var analisador = new AnalisadorDaOperacao(CicloReal(), intervalo: TimeSpan.FromMilliseconds(5));
        using var host = Host(analisador);
        await host.StartAsync();
        await EsperarAte(() => analisador.Situacao.Falhas >= 2, TimeSpan.FromSeconds(15));

        // Enquanto o Analisador bate na trava: as decisões dão o mesmo resultado de sempre.
        var relogio = Stopwatch.StartNew();
        var primeira = repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado;
        var segunda = repositorio.TentarUsar("1000000001", "p1", "inner-1", Agora).Resultado;
        var desconhecido = repositorio.TentarUsar("9999999999", "p1", "inner-1", Agora).Resultado;
        relogio.Stop();

        Assert.True(primeira.Liberou);
        Assert.Equal(MotivoDoUso.UsosEsgotados, segunda.Motivo);
        Assert.Equal(MotivoDoUso.Desconhecido, desconhecido.Motivo);
        // A garantia real de que a camada não entra no passo da decisão é estrutural (o worker não
        // alcança Access.Inteligencia — NOVO_ARQ_IA_01). Aqui só confirmamos que a decisão não trava
        // na telemetria presa: se travasse, esperaria o busy_timeout do acesso.db (5 s) por operação.
        // O teto é 2 s (bem acima da variação do runner de CI, ~0,6 s, e bem abaixo de um bloqueio real).
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(2), $"Decisões levaram {relogio.Elapsed}.");
        Assert.Equal(nameof(SqliteException), analisador.Situacao.UltimoErro);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);

        // Solta a trava: o Analisador volta sozinho, sem ninguém reiniciar nada.
        SqliteConnectionFactory.Executar(trava, "ROLLBACK;");
        var falhas = analisador.Situacao.Falhas;
        await EsperarAte(() => analisador.Situacao.Ciclos >= 2 && analisador.Situacao.Falhas == falhas, TimeSpan.FromSeconds(10));
        await host.StopAsync();
    }

    // ---------------------------------------------------------------- Diagnóstico

    [Fact]
    public void Diagnostico_mostra_a_saude_do_analisador()
    {
        var desligada = Textos.SaudeDoAnalisador(EdgeControlService.SaudeDoAnalisadorPara(null), Agora);
        Assert.Equal("Desligada nesta instalação", desligada.Resumo);
        Assert.Equal(Sinal.Neutro, desligada.Sinal);

        var situacao = SituacaoDoAnalisador.Inicial(OrcamentoDoCiclo.CicloCurto) with { Ligada = true, Rodando = true };
        situacao = situacao.ComCiclo(Agora.AddSeconds(-2), TimeSpan.FromMilliseconds(3), 7, estourou: false);
        var saude = EdgeControlService.SaudeDoAnalisadorPara(situacao);

        Assert.True(saude.Ligado);
        Assert.Equal(3, saude.DuracaoDoUltimoCicloMs);
        Assert.Equal(50, saude.OrcamentoMs);
        Assert.Equal(7, saude.TentativasLidas);

        var (resumo, sinal, linhas) = Textos.SaudeDoAnalisador(saude, Agora);
        Assert.Equal("Funcionando", resumo);
        Assert.Equal(Sinal.Bom, sinal);
        Assert.Contains(linhas, l => l.Rotulo == "Último ciclo" && l.Valor == "há 2 s");
        Assert.Contains(linhas, l => l.Rotulo == "Duração do último ciclo" && l.Valor == "3 ms (orçamento: 50 ms)");

        var comErro = EdgeControlService.SaudeDoAnalisadorPara(situacao.ComFalha(Agora, TimeSpan.Zero, new IOException(), estourou: false));
        Assert.Equal("Funcionando, com erros", Textos.SaudeDoAnalisador(comErro, Agora).Resumo);
    }

    // ---------------------------------------------------------------- apoio

    private static IHost Host(AnalisadorDaOperacao analisador)
    {
        var construtor = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        construtor.Services.AddHostedService(_ => analisador);
        return construtor.Build();
    }

    private static string Esquema(SqliteConnection conexao)
    {
        using (conexao)
        {
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT group_concat(name, ',') FROM (SELECT name FROM sqlite_master ORDER BY name);";
            return (string)comando.ExecuteScalar()!;
        }
    }

    private static async Task EsperarAte(Func<bool> condicao, TimeSpan limite)
    {
        var relogio = Stopwatch.StartNew();
        while (!condicao())
        {
            Assert.True(relogio.Elapsed < limite, "A condição não aconteceu a tempo.");
            await Task.Delay(10);
        }
    }

    [GeneratedRegex(@"\)\s*STRICT\s*$")]
    private static partial Regex StrictNoFim();

    // Casa o termo proibido só como palavra inteira do nome (snake_case), não como pedaço de outra:
    // "dimensionamento_catracas" tem "name" dentro, mas não guarda nome; "codigo_titular" guardaria.
    [GeneratedRegex(@"(^|_)(qr|code|codigo|card|cartao|nome|name|mask|holder|titular|operador|operator)(_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ColunaProibida();

    private sealed class RelogioManual(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;

        public override DateTimeOffset GetUtcNow() => Agora;
    }

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
            Trava?.Wait(CancellationToken.None); // trava de verdade: nem o cancelamento a solta
            if (Demora > TimeSpan.Zero)
            {
                Thread.Sleep(Demora);
            }

            return Erro is null ? 0 : throw Erro;
        }
    }
}
