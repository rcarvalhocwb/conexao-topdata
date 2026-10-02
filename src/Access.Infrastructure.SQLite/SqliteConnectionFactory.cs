using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Abre conexões com as PRAGMAs que este produto exige.
/// </summary>
/// <remarks>
/// As PRAGMAs de conexão (<c>foreign_keys</c>, <c>synchronous</c>, <c>busy_timeout</c>)
/// <b>não</b> são persistentes: precisam ser aplicadas a cada conexão. Só
/// <c>journal_mode=WAL</c> fica gravado no arquivo. Esquecer isso é como não tê-las.
/// Ver docs/ADR/ADR-0003-sqlite-wal-outbox.md
/// </remarks>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private int _walConfigurado;

    public SqliteConnectionFactory(string caminhoDoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoDoArquivo);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = caminhoDoArquivo,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        }.ToString();

        CaminhoDoArquivo = caminhoDoArquivo;
    }

    public string CaminhoDoArquivo { get; }

    /// <summary>Abre e configura uma conexão.</summary>
    public SqliteConnection Abrir()
    {
        var conexao = new SqliteConnection(_connectionString);
        conexao.Open();

        // WAL é propriedade do arquivo: basta uma vez, mas é barato garantir.
        if (Interlocked.Exchange(ref _walConfigurado, 1) == 0)
        {
            Executar(conexao, "PRAGMA journal_mode = WAL;");
        }

        // Estas precisam ser aplicadas em TODA conexão.
        Executar(conexao, "PRAGMA foreign_keys = ON;");

        // FULL custa latência de disco. É deliberado: um acesso confirmado
        // localmente não pode se perder num corte de energia (CA-02).
        Executar(conexao, "PRAGMA synchronous = FULL;");

        // Sem isso, escrita concorrente devolve SQLITE_BUSY imediatamente.
        Executar(conexao, "PRAGMA busy_timeout = 5000;");

        return conexao;
    }

    /// <summary>Verifica a integridade do banco. Deve devolver <c>ok</c>.</summary>
    public string VerificarIntegridade()
    {
        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "PRAGMA integrity_check;";
        return comando.ExecuteScalar() as string ?? "desconhecido";
    }

    internal static void Executar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    internal static T? Escalar<T>(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        var valor = comando.ExecuteScalar();
        return valor is null or DBNull ? default : (T)Convert.ChangeType(valor, typeof(T), provider: null);
    }
}
