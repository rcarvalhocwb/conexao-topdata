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
    public static IReadOnlyList<string> Disponiveis() => Recursos(PrefixoDoRecurso);

    /// <summary>
    /// Aplica o que faltar. Seguro para chamar em toda inicialização: migração já
    /// aplicada é ignorada.
    /// </summary>
    /// <returns>Nomes das migrações efetivamente aplicadas nesta chamada.</returns>
    public IReadOnlyList<string> Aplicar()
    {
        using var conexao = _fabrica.Abrir();
        return Aplicar(conexao, PrefixoDoRecurso);
    }

    /// <summary>Os recursos embutidos com o prefixo, sem ele, em ordem.</summary>
    internal static IReadOnlyList<string> Recursos(string prefixo) =>
        typeof(Migrator).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(prefixo, StringComparison.Ordinal))
            .Select(n => n[prefixo.Length..])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Aplica, na conexão, as migrações embutidas com o prefixo que ainda não estão em
    /// <c>schema_version</c>. É o mesmo mecanismo para <c>acesso.db</c> e para
    /// <c>telemetria.db</c> (Etapa I.0), cada arquivo com a sua pasta e a sua tabela de versão.
    /// </summary>
    internal static IReadOnlyList<string> Aplicar(SqliteConnection conexao, string prefixo)
    {
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

        foreach (var nome in Recursos(prefixo))
        {
            if (jaAplicadas.Contains(nome))
            {
                continue;
            }

            var sql = LerRecurso(prefixo + nome);

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

    private static string LerRecurso(string caminho)
    {
        using var fluxo = typeof(Migrator).Assembly.GetManifestResourceStream(caminho)
            ?? throw new InvalidOperationException($"Migração não encontrada no assembly: {caminho}");
        using var leitor = new StreamReader(fluxo);
        return leitor.ReadToEnd();
    }
}
