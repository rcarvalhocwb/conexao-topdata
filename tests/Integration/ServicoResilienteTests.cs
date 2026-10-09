using Access.Infrastructure.SQLite;
using Edge.Supervisor;
using Microsoft.Extensions.Logging;

namespace Integration.Tests;

/// <summary>
/// Achado E2-02 do docs/41: uma exceção num serviço em segundo plano (a cópia de segurança com o disco
/// cheio, por exemplo) parava o host inteiro, encerrava todos os workers, e o Windows não religava o
/// serviço. Agora os laços registram a falha e recomeçam, e um grupo que não sobe não impede os outros.
/// </summary>
public sealed class ServicoResilienteTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    public ServicoResilienteTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        // No Windows, a conexão que ficou no pool segura o arquivo e o Delete falha.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
            // Sobra de arquivo temporário não reprova o teste.
        }
    }

    [Fact]
    public async Task Laco_que_falha_e_registrado_e_recomeca_em_vez_de_escapar()
    {
        var log = new RegistroDeTeste();
        var chamadas = 0;

        await LacoResiliente.RodarAsync(
            "laço de teste",
            _ =>
            {
                chamadas++;
                return chamadas == 1
                    ? throw new InvalidOperationException("disco cheio")
                    : Task.CompletedTask;
            },
            log,
            CancellationToken.None,
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(2, chamadas);
        var linha = Assert.Single(log.Linhas);
        Assert.Contains("laço de teste falhou", linha, StringComparison.Ordinal);
        Assert.Contains("disco cheio", linha, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Laco_para_sem_erro_quando_o_servico_para()
    {
        using var parar = new CancellationTokenSource();
        var log = new RegistroDeTeste();

        var laco = LacoResiliente.RodarAsync(
            "laço de teste",
            async c => await Task.Delay(Timeout.Infinite, c),
            log,
            parar.Token);
        await parar.CancelAsync();
        await laco;

        Assert.Empty(log.Linhas);
    }

    [Fact]
    public void Copia_com_a_base_ilegivel_nao_lanca_para_o_agendador_e_nao_deixa_arquivo()
    {
        // Um arquivo que não é SQLite: a abertura lança SqliteException, o tipo que antes escapava.
        var banco = Path.Combine(_pasta, "acesso.db");
        File.WriteAllText(banco, "isto não é uma base SQLite, é só texto para a abertura falhar");
        var copias = Path.Combine(_pasta, "copias");
        var log = new RegistroDeTeste<AgendadorDeCopias>();
        var agendador = new AgendadorDeCopias(new CopiaDeSeguranca(new SqliteConnectionFactory(banco), copias), log);

        var erro = Record.Exception(agendador.Executar);

        Assert.Null(erro);
        Assert.Contains(log.Linhas, l => l.Contains("SQLite", StringComparison.OrdinalIgnoreCase)
                                         || l.Contains("database", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Directory.EnumerateFiles(copias));
    }

    [Fact]
    public void Grupo_que_nao_sobe_nao_impede_os_outros_e_nao_e_iniciado_duas_vezes()
    {
        var quebrado = new WorkerQueNaoSobe("grupo-a", 3570);
        var bom = new WorkerContado("grupo-b", 3571);
        var supervisor = new WorkerSupervisor([quebrado, bom]);

        var acoes = supervisor.Iniciar();

        Assert.Equal(1, bom.Iniciadas);
        Assert.Contains(acoes, a => a.Worker == "grupo-a" && a.Situacao == SituacaoDoWorker.Morto
                                    && a.Acao.Contains("não iniciou", StringComparison.Ordinal));
        Assert.Contains(acoes, a => a.Worker == "grupo-b" && a.Acao == "iniciado");

        // O laço de supervisão pode recomeçar (LacoResiliente) e chamar Iniciar de novo.
        Assert.Empty(supervisor.Iniciar());
        Assert.Equal(1, bom.Iniciadas);
    }

    [Fact]
    public void Registro_em_arquivo_recebe_so_avisos_e_erros_e_com_o_redator()
    {
        var linhas = new List<string>();
        using var provedor = new ProvedorDeRegistroEmArquivo(linhas.Add);
        var log = provedor.CreateLogger("Edge.Supervisor.AgendadorDeCopias");

        log.Log(LogLevel.Information, default, "rotina", null, (s, _) => s);
        log.Log(LogLevel.Warning, default, "cópia falhou para o cartão 12345678901234", null, (s, _) => s);

        var linha = Assert.Single(linhas);
        Assert.StartsWith("AVISO AgendadorDeCopias: cópia falhou", linha, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678901234", linha, StringComparison.Ordinal);
    }

    private class RegistroDeTeste : ILogger
    {
        public List<string> Linhas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add(formatter(state, exception));
    }

    private sealed class RegistroDeTeste<T> : RegistroDeTeste, ILogger<T>;

    private sealed class WorkerQueNaoSobe(string nome, int porta) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = [1];

        public bool EstaVivo => false;

        public bool EstaSaudavel => false;

        public string Diagnostico => "não subiu";

        public void Iniciar() => throw new System.ComponentModel.Win32Exception(5, "Acesso negado");

        public void Matar()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class WorkerContado(string nome, int porta) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = [2];

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => EstaVivo;

        public string Diagnostico => "ok";

        public int Iniciadas { get; private set; }

        public void Iniciar()
        {
            EstaVivo = true;
            Iniciadas++;
        }

        public void Matar() => EstaVivo = false;

        public void Dispose()
        {
        }
    }
}
