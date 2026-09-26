using System.Globalization;
using Sync.Ingestao;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// A marca de retomada da ingestão, durável.
/// </summary>
/// <remarks>
/// Guardada no mesmo banco e no mesmo disco do resto: um cursor que se perde num reinício
/// faz a ingestão recomeçar do zero — o que é correto, porque o destino é idempotente,
/// mas custa uma releitura inteira no pior momento possível.
/// </remarks>
public sealed class CursoresSqlite : IArmazenamentoDeCursor
{
    private readonly SqliteConnectionFactory _fabrica;

    public CursoresSqlite(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <inheritdoc />
    public string? Ler(string conector, string fluxo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conector);
        ArgumentException.ThrowIfNullOrWhiteSpace(fluxo);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT cursor FROM sync_cursor WHERE connector = $c AND stream = $f;";
        comando.Parameters.AddWithValue("$c", conector);
        comando.Parameters.AddWithValue("$f", fluxo);
        return comando.ExecuteScalar() as string;
    }

    /// <inheritdoc />
    public void Gravar(string conector, string fluxo, string cursor, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conector);
        ArgumentException.ThrowIfNullOrWhiteSpace(fluxo);
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO sync_cursor (connector, stream, cursor, updated_at)
            VALUES ($c, $f, $cursor, $em)
            ON CONFLICT (connector, stream) DO UPDATE SET
                cursor = excluded.cursor,
                updated_at = excluded.updated_at;
            """;
        comando.Parameters.AddWithValue("$c", conector);
        comando.Parameters.AddWithValue("$f", fluxo);
        comando.Parameters.AddWithValue("$cursor", cursor);
        comando.Parameters.AddWithValue("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void Apagar(string conector, string fluxo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conector);
        ArgumentException.ThrowIfNullOrWhiteSpace(fluxo);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "DELETE FROM sync_cursor WHERE connector = $c AND stream = $f;";
        comando.Parameters.AddWithValue("$c", conector);
        comando.Parameters.AddWithValue("$f", fluxo);
        comando.ExecuteNonQuery();
    }
}
