using System.Globalization;

namespace Access.Infrastructure.SQLite;

/// <summary>O último uso liberado do mesmo ingresso, antes da tentativa explicada.</summary>
public sealed record UsoAnteriorLido(string DeviceId, DateTimeOffset Em, bool Girou);

/// <summary>
/// Uma tentativa e o que a base sabe em volta dela, para o "Por quê?" (Etapa I.2 do docs/36).
/// Sem código, máscara nem identificador do ingresso: a explicação não precisa deles.
/// </summary>
/// <param name="DeviceId">A catraca (<c>inner-N</c>).</param>
/// <param name="Em">Quando foi decidida.</param>
/// <param name="Liberou">Liberada (consumida) ou negada.</param>
/// <param name="Girou">Teve giro confirmado.</param>
/// <param name="Motivo">O motivo gravado (<c>reason</c>).</param>
/// <param name="Origem">A origem bruta da leitura (migração 010); nula antes dela.</param>
/// <param name="UltimoUso">O último uso liberado do mesmo ingresso antes desta tentativa.</param>
/// <param name="IntervaloDeReusoSegundos">O intervalo de reuso do canal de venda; 0 se não houver.</param>
public sealed record TentativaParaExplicar(
    string DeviceId,
    DateTimeOffset Em,
    bool Liberou,
    bool Girou,
    string Motivo,
    int? Origem,
    UsoAnteriorLido? UltimoUso,
    int IntervaloDeReusoSegundos);

/// <summary>
/// Lê, sob pedido, o contexto de uma tentativa para o "Por que negou" (Etapa I.2 do docs/36;
/// docs/36-anexos/02 IN-06). Só leitura; duas consultas curtas por pedido do operador.
/// </summary>
/// <remarks>
/// <para>
/// LGPD (docs/36-anexos/02 §6): nada aqui seleciona coluna com o código do ingresso. O uso
/// anterior é achado pelo identificador local do ingresso (um UUID), que fica dentro desta
/// classe e não vai para o contrato.
/// </para>
/// <para>
/// Não há índice por ingresso em <c>ticket_use_attempt</c>: a segunda consulta varre as
/// tentativas anteriores. É de propósito — índice novo custaria em cada decisão (a escrita da
/// tentativa é o passo do giro), e isto roda só quando o operador clica "Por quê?".
/// </para>
/// </remarks>
public sealed class ContextoDasNegativas
{
    private readonly SqliteConnectionFactory _fabrica;

    public ContextoDasNegativas(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>A tentativa e o contexto dela; nulo se não existe.</summary>
    public TentativaParaExplicar? Ler(Guid tentativa)
    {
        using var conexao = _fabrica.Abrir();

        long sequencia;
        string? ingresso;
        TentativaParaExplicar lida;

        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                SELECT a.rowid, a.device_id, a.at, a.outcome, a.reason, a.reader_origin,
                       a.passage_confirmed_at, a.ticket_id, COALESCE(p.reuse_interval_seconds, 0)
                FROM ticket_use_attempt a
                LEFT JOIN ticket_provider p ON p.id = a.provider_id
                WHERE a.id = $id;
                """;
            comando.Parameters.AddWithValue("$id", tentativa.ToString());
            using var leitor = comando.ExecuteReader();

            if (!leitor.Read())
            {
                return null;
            }

            sequencia = leitor.GetInt64(0);
            ingresso = leitor.IsDBNull(7) ? null : leitor.GetString(7);
            lida = new TentativaParaExplicar(
                leitor.GetString(1),
                Data(leitor.GetString(2)),
                string.Equals(leitor.GetString(3), "consumido", StringComparison.Ordinal),
                !leitor.IsDBNull(6),
                leitor.GetString(4),
                leitor.IsDBNull(5) ? null : leitor.GetInt32(5),
                null,
                leitor.GetInt32(8));
        }

        if (ingresso is null)
        {
            return lida;
        }

        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                SELECT device_id, at, passage_confirmed_at
                FROM ticket_use_attempt
                WHERE ticket_id = $ingresso AND outcome = 'consumido' AND rowid < $sequencia
                ORDER BY rowid DESC
                LIMIT 1;
                """;
            comando.Parameters.AddWithValue("$ingresso", ingresso);
            comando.Parameters.AddWithValue("$sequencia", sequencia);
            using var leitor = comando.ExecuteReader();

            if (leitor.Read())
            {
                lida = lida with { UltimoUso = new UsoAnteriorLido(leitor.GetString(0), Data(leitor.GetString(1)), !leitor.IsDBNull(2)) };
            }
        }

        return lida;
    }

    private static DateTimeOffset Data(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
