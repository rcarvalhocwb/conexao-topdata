using System.Globalization;

namespace Access.Infrastructure.SQLite;

/// <summary>Uma leitura pedida pelo painel no modo simulação.</summary>
public sealed record LeituraPedida(long Id, int Inner, string Codigo, bool NaUrna, bool Girar);

/// <summary>
/// A fila entre o painel (que pede a leitura, pelo serviço) e o worker em modo simulação
/// (que a entrega ao simulador).
/// </summary>
public sealed class LeiturasSimuladas
{
    private readonly SqliteConnectionFactory _fabrica;

    public LeiturasSimuladas(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Pede uma leitura.</summary>
    public long Pedir(int inner, string codigo, bool naUrna, bool girar, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentOutOfRangeException.ThrowIfLessThan(inner, 1);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO simulated_read (inner_number, code, at_urn, turn, created_at)
            VALUES ($inner, $codigo, $urna, $girar, $em)
            RETURNING id;
            """;
        comando.Parameters.AddWithValue("$inner", inner);
        comando.Parameters.AddWithValue("$codigo", codigo.Trim());
        comando.Parameters.AddWithValue("$urna", naUrna ? 1 : 0);
        comando.Parameters.AddWithValue("$girar", girar ? 1 : 0);
        comando.Parameters.AddWithValue("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Retira, em ordem, as leituras pendentes destas catracas.</summary>
    /// <remarks>Retirar marca como tomada na mesma transação: cada leitura vai para um worker só, uma vez só.</remarks>
    public IReadOnlyList<LeituraPedida> Retirar(IReadOnlyCollection<int> inners, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(inners);

        if (inners.Count == 0)
        {
            return [];
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var lista = new List<LeituraPedida>();
        var marcadores = string.Join(", ", inners.Select((_, i) => $"$i{i}"));

        using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText =
                $"""
                UPDATE simulated_read SET taken_at = $em
                WHERE taken_at IS NULL AND inner_number IN ({marcadores})
                RETURNING id, inner_number, code, at_urn, turn;
                """;
            comando.Parameters.AddWithValue("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            var i = 0;
            foreach (var inner in inners)
            {
                comando.Parameters.AddWithValue($"$i{i++}", inner);
            }

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new LeituraPedida(
                    leitor.GetInt64(0),
                    leitor.GetInt32(1),
                    leitor.GetString(2),
                    leitor.GetInt64(3) == 1,
                    leitor.GetInt64(4) == 1));
            }
        }

        transacao.Commit();
        return [.. lista.OrderBy(l => l.Id)];
    }
}
