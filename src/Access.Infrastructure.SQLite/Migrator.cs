using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Aplica as migrações versionadas, em ordem, uma única vez cada.
/// </summary>
/// <remarks>
/// Migração é SQL puro, embutido no assembly: é auditável por quem não programa em C#,
/// e o que roda em produção é exatamente o que está no repositório.
/// </remarks>
public sealed class Migrator
{
    private const string PrefixoDoRecurso = "Access.Infrastructure.SQLite.Migrations.";

    private readonly SqliteConnectionFactory _fabrica;

    public Migrator(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Migrações disponíveis, na ordem de aplicação.</summary>
    public static IReadOnlyList<string> Disponiveis() =>
        typeof(Migrator).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(PrefixoDoRecurso, StringComparison.Ordinal))
            .Select(n => n[PrefixoDoRecurso.Length..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Aplica o que faltar. Seguro para chamar em toda inicialização: migração já
    /// aplicada é ignorada.
    /// </summary>
    /// <returns>Nomes das migrações efetivamente aplicadas nesta chamada.</returns>
    public IReadOnlyList<string> Aplicar()
    {
        using var conexao = _fabrica.Abrir();

        SqliteConnectionFactory.Executar(
            conexao,
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                nome       TEXT NOT NULL PRIMARY KEY,
                aplicada_em TEXT NOT NULL
            );
            """);

        var jaAplicadas = new HashSet<string>(StringComparer.Ordinal);
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "SELECT nome FROM schema_version;";
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                jaAplicadas.Add(leitor.GetString(0));
            }
        }

        var aplicadas = new List<string>();

        foreach (var nome in Disponiveis())
        {
            if (jaAplicadas.Contains(nome))
            {
                continue;
            }

            var sql = LerRecurso(nome);

            // Cada migração é uma transação: ou aplica inteira, ou não aplica.
            using var transacao = conexao.BeginTransaction();

            using (var comando = conexao.CreateCommand())
            {
                comando.Transaction = transacao;
                comando.CommandText = sql;
                comando.ExecuteNonQuery();
            }

            using (var registro = conexao.CreateCommand())
            {
                registro.Transaction = transacao;
                registro.CommandText = "INSERT INTO schema_version (nome, aplicada_em) VALUES ($nome, $em);";
                registro.Parameters.AddWithValue("$nome", nome);
                registro.Parameters.AddWithValue(
                    "$em",
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                registro.ExecuteNonQuery();
            }

            transacao.Commit();
            aplicadas.Add(nome);
        }

        return aplicadas;
    }

    private static string LerRecurso(string nome)
    {
        var caminho = PrefixoDoRecurso + nome;
        using var fluxo = typeof(Migrator).Assembly.GetManifestResourceStream(caminho)
            ?? throw new InvalidOperationException($"Migração não encontrada no assembly: {caminho}");
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
