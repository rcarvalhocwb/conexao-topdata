using System.Globalization;
using Access.Infrastructure.SQLite;

namespace Edge.Supervisor.Atualizacoes;

/// <summary>A última leitura de qualquer catraca, para a atualização só instalar com as catracas paradas.</summary>
public static class AtividadeDasCatracas
{
    /// <summary>A última tentativa de uso gravada; nulo se nunca houve ou se a base não respondeu.</summary>
    /// <remarks>Base ocupada não pode virar "parada": na dúvida, devolve agora (não instala).</remarks>
    public static DateTimeOffset? UltimaLeitura(SqliteConnectionFactory fabrica, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        try
        {
            using var conexao = fabrica.Abrir();
            using var comando = conexao.CreateCommand();
            comando.CommandText = "SELECT MAX(at) FROM ticket_use_attempt;";
            return comando.ExecuteScalar() is string texto
                   && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var quando)
                ? quando
                : null;
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return agora;
        }
    }
}
