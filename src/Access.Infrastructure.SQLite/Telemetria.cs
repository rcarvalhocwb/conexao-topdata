using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Abre conexões com <c>telemetria.db</c>, o arquivo da camada inteligente (Etapa I.0 do docs/36;
/// docs/36-anexos/02 §3.3).
/// </summary>
/// <remarks>
/// <para>
/// Arquivo próprio, separado de <c>acesso.db</c>: o SQLite tem um escritor por arquivo, e a
/// telemetria não pode disputar o escritor com a decisão do giro. Travada, ela perde telemetria;
/// nunca atrasa uma catraca.
/// </para>
/// <para>
/// <c>synchronous = NORMAL</c>, e não <c>FULL</c> como em <c>acesso.db</c>
/// (<see cref="SqliteConnectionFactory"/>): em WAL isso não corrompe o arquivo e pode perder as
/// últimas transações num corte de energia — aceitável para telemetria, nunca para prestação de
/// contas. A espera por trava é curta (<see cref="EsperaPorTrava"/>): um arquivo preso por outro
/// processo faz o ciclo do Analisador falhar logo, e não segurar a thread dele por 5 s.
/// </para>
/// </remarks>
public sealed class FabricaDaTelemetria
{
    /// <summary>Quanto uma escrita espera por uma trava antes de desistir.</summary>
    public const int EsperaPorTrava = 250;

    /// <summary>Nome do arquivo, ao lado de <c>acesso.db</c>.</summary>
    public const string NomeDoArquivo = "telemetria.db";

    private readonly string _textoDeConexao;
    private int _walConfigurado;

    public FabricaDaTelemetria(string caminhoDoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoDoArquivo);

        _textoDeConexao = new SqliteConnectionStringBuilder
        {
            DataSource = caminhoDoArquivo,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,

            // O Microsoft.Data.Sqlite repete, sozinho, a instrução que achou o arquivo preso, até
            // este prazo (padrão: 30 s). Para telemetria, 1 s basta: preso, o ciclo falha e conta.
            DefaultTimeout = 1,
        }.ToString();

        CaminhoDoArquivo = caminhoDoArquivo;
    }

    public string CaminhoDoArquivo { get; }

    /// <summary>O arquivo de telemetria que fica ao lado da base da operação.</summary>
    public static string CaminhoAoLadoDe(string caminhoDoBanco)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoDoBanco);
        var pasta = Path.GetDirectoryName(Path.GetFullPath(caminhoDoBanco)) ?? ".";
        return Path.Combine(pasta, NomeDoArquivo);
    }

    /// <summary>Abre e configura uma conexão.</summary>
    public SqliteConnection Abrir()
    {
        var conexao = new SqliteConnection(_textoDeConexao);
        conexao.Open();

        // A espera vem antes de tudo: até o journal_mode pode esbarrar numa trava.
        SqliteConnectionFactory.Executar(conexao, string.Create(CultureInfo.InvariantCulture, $"PRAGMA busy_timeout = {EsperaPorTrava};"));

        if (Interlocked.Exchange(ref _walConfigurado, 1) == 0)
        {
            SqliteConnectionFactory.Executar(conexao, "PRAGMA journal_mode = WAL;");
        }

        SqliteConnectionFactory.Executar(conexao, "PRAGMA synchronous = NORMAL;");
        return conexao;
    }
}

/// <summary>
/// Aplica as migrações de <c>telemetria.db</c> (pasta <c>MigracoesDaTelemetria/</c>, prefixo
/// <c>T</c>), com o mesmo mecanismo do <see cref="Migrator"/> e numeração que não colide com a de
/// <c>acesso.db</c>.
/// </summary>
public sealed class MigradorDaTelemetria
{
    private const string PrefixoDoRecurso = "Access.Infrastructure.SQLite.MigracoesDaTelemetria.";

    private readonly FabricaDaTelemetria _fabrica;

    public MigradorDaTelemetria(FabricaDaTelemetria fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Migrações de telemetria disponíveis, na ordem de aplicação.</summary>
    public static IReadOnlyList<string> Disponiveis() => Migrator.Recursos(PrefixoDoRecurso);

    /// <summary>Aplica o que faltar. Seguro em toda partida.</summary>
    public IReadOnlyList<string> Aplicar()
    {
        using var conexao = _fabrica.Abrir();

        // Toda tabela de telemetria.db é STRICT, inclusive a de versão (em acesso.db ela é anterior
        // a essa regra). Criada aqui primeiro, o CREATE ... IF NOT EXISTS do migrador não faz nada.
        SqliteConnectionFactory.Executar(
            conexao,
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                nome        TEXT NOT NULL PRIMARY KEY,
                aplicada_em TEXT NOT NULL
            ) STRICT;
            """);

        return Migrator.Aplicar(conexao, PrefixoDoRecurso);
    }
}

/// <summary>Um ciclo do Analisador que terminou bem, como vai para <c>analyzer_cycle</c>.</summary>
/// <param name="Sessao">A partida do serviço (migração 016); nula em teste.</param>
/// <param name="TerminouEm">Quando terminou.</param>
/// <param name="Duracao">Quanto durou.</param>
/// <param name="TentativasNovas">Tentativas novas lidas da base.</param>
/// <param name="UltimaTentativa">O cursor (<c>rowid</c>) em <c>ticket_use_attempt</c> depois do ciclo.</param>
/// <param name="Estourou">Passou do orçamento.</param>
public sealed record CicloRegistrado(
    string? Sessao,
    DateTimeOffset TerminouEm,
    TimeSpan Duracao,
    long TentativasNovas,
    long UltimaTentativa,
    bool Estourou);

/// <summary>
/// O caderno do Analisador em <c>telemetria.db</c> (T001). Só o Analisador escreve aqui.
/// </summary>
public sealed class CadernoDoAnalisador
{
    /// <summary>Ciclos guardados; os mais antigos saem. Uma hora de ciclos de 1 s.</summary>
    public const int CiclosGuardados = 3600;

    private readonly FabricaDaTelemetria _fabrica;

    public CadernoDoAnalisador(FabricaDaTelemetria fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Grava o ciclo e apaga o que passou de <see cref="CiclosGuardados"/>.</summary>
    public void Registrar(CicloRegistrado ciclo)
    {
        ArgumentNullException.ThrowIfNull(ciclo);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText =
                """
                INSERT INTO analyzer_cycle (session_id, finished_at, duration_ms, new_attempts, last_attempt_rowid, over_budget)
                VALUES ($sessao, $em, $duracao, $novas, $ultima, $estourou);
                """;
            comando.Parameters.AddWithValue("$sessao", (object?)ciclo.Sessao ?? DBNull.Value);
            comando.Parameters.AddWithValue("$em", ciclo.TerminouEm.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            comando.Parameters.AddWithValue("$duracao", (long)Math.Max(0, ciclo.Duracao.TotalMilliseconds));
            comando.Parameters.AddWithValue("$novas", ciclo.TentativasNovas);
            comando.Parameters.AddWithValue("$ultima", ciclo.UltimaTentativa);
            comando.Parameters.AddWithValue("$estourou", ciclo.Estourou ? 1 : 0);
            comando.ExecuteNonQuery();
        }

        using (var faxina = conexao.CreateCommand())
        {
            faxina.Transaction = transacao;
            faxina.CommandText = "DELETE FROM analyzer_cycle WHERE id <= (SELECT MAX(id) FROM analyzer_cycle) - $guardados;";
            faxina.Parameters.AddWithValue("$guardados", CiclosGuardados);
            faxina.ExecuteNonQuery();
        }

        transacao.Commit();
    }

    /// <summary>Quantos ciclos estão gravados.</summary>
    public long Contar()
    {
        using var conexao = _fabrica.Abrir();
        return SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COUNT(*) FROM analyzer_cycle;");
    }
}
