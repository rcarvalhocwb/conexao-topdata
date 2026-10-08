using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Cópia de segurança consistente de <c>acesso.db</c>, verificada e com retenção.
/// </summary>
/// <remarks>
/// <para>
/// A base é a fonte da prestação de contas: perdê-la no meio do evento é perder o que foi
/// liberado. Copiar o arquivo com o WAL aberto não serve (a cópia pode pegar o banco no meio de
/// uma escrita). <c>VACUUM INTO</c> gera um arquivo novo, íntegro, a partir de uma leitura
/// consistente, sem parar a operação.
/// </para>
/// <para>
/// Toda cópia é conferida antes de contar como cópia: <c>integrity_check</c> e a mesma quantidade
/// de tabelas do original. Cópia que não passa é apagada, para ninguém restaurar um arquivo
/// corrompido achando que é o backup.
/// </para>
/// </remarks>
public sealed class CopiaDeSeguranca
{
    private const string Prefixo = "acesso-";
    private const string Extensao = ".db";

    private readonly SqliteConnectionFactory _fabrica;

    public CopiaDeSeguranca(SqliteConnectionFactory fabrica, string pastaDeCopias)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDeCopias);
        _fabrica = fabrica;
        PastaDeCopias = pastaDeCopias;
    }

    public string PastaDeCopias { get; }

    /// <summary>Cria uma cópia agora e devolve o caminho do arquivo, já verificado.</summary>
    /// <exception cref="InvalidOperationException">A cópia não passou na verificação e foi apagada.</exception>
    public string Criar(DateTimeOffset agora)
    {
        Directory.CreateDirectory(PastaDeCopias);

        var destino = Path.Combine(
            PastaDeCopias,
            $"{Prefixo}{agora.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}{Extensao}");

        if (File.Exists(destino))
        {
            throw new InvalidOperationException($"Já existe uma cópia com este nome: {destino}.");
        }

        using (var conexao = _fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            // O caminho não pode ser parâmetro em VACUUM INTO; ele vem de um nome que nós geramos,
            // sem entrada do usuário, e as aspas simples são dobradas por garantia.
            comando.CommandText = $"VACUUM INTO '{destino.Replace("'", "''", StringComparison.Ordinal)}';";
            comando.ExecuteNonQuery();
        }

        var problema = Conferir(destino);
        if (problema is not null)
        {
            File.Delete(destino);
            throw new InvalidOperationException($"A cópia não passou na verificação ({problema}) e foi apagada.");
        }

        return destino;
    }

    /// <summary>Apaga as cópias mais antigas e mantém só as <paramref name="manter"/> mais recentes.</summary>
    /// <returns>Quantas cópias foram apagadas.</returns>
    public int Limpar(int manter)
    {
        if (manter < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(manter), "É preciso manter pelo menos uma cópia.");
        }

        if (!Directory.Exists(PastaDeCopias))
        {
            return 0;
        }

        var antigas = Directory.EnumerateFiles(PastaDeCopias, $"{Prefixo}*{Extensao}")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(manter)
            .ToList();

        foreach (var arquivo in antigas)
        {
            File.Delete(arquivo);
        }

        return antigas.Count;
    }

    /// <summary>Lista as cópias existentes, da mais recente para a mais antiga.</summary>
    public IReadOnlyList<string> Listar()
    {
        if (!Directory.Exists(PastaDeCopias))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(PastaDeCopias, $"{Prefixo}*{Extensao}")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)];
    }

    /// <summary>Verifica um arquivo de cópia: integridade e o mesmo número de tabelas do original.</summary>
    /// <returns>Nulo se passou; senão, o motivo.</returns>
    private string? Conferir(string arquivo)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = arquivo,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        using var copia = new SqliteConnection(cs);
        copia.Open();

        var integridade = Escalar(copia, "PRAGMA integrity_check;")?.ToString();
        if (!string.Equals(integridade, "ok", StringComparison.Ordinal))
        {
            return $"integrity_check: {integridade ?? "sem resposta"}";
        }

        using (var conexao = _fabrica.Abrir())
        {
            var tabelasOriginal = Escalar(conexao, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';");
            var tabelasCopia = Escalar(copia, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';");
            if (Convert.ToInt64(tabelasOriginal, CultureInfo.InvariantCulture) != Convert.ToInt64(tabelasCopia, CultureInfo.InvariantCulture))
            {
                return "número de tabelas diferente do original";
            }
        }

        return null;
    }

    private static object? Escalar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return comando.ExecuteScalar();
    }
}
