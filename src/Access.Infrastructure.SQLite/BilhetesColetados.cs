using System.Globalization;
using Access.Application.Devices;
using Access.Domain.Credentials;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Um bilhete gravado, como a base o guarda: sem o código.</summary>
/// <param name="Id">Identificador (UUID v7).</param>
/// <param name="Inner">Catraca.</param>
/// <param name="Tipo">Tipo como veio (tipos-bilhete.csv).</param>
/// <param name="Repetido">Tipo 128 gravado por não ter o original na base.</param>
/// <param name="MarcadoEm">Relógio da catraca, ao minuto; nulo = a catraca devolveu data inválida.</param>
/// <param name="CodigoMascarado">Máscara do código.</param>
/// <param name="Impressao">HMAC do código (<see cref="ImpressaoDeCodigo"/>).</param>
/// <param name="ColetaId">A coleta que o trouxe.</param>
/// <param name="Ordem">Posição nessa coleta.</param>
/// <param name="ColetadoEm">Relógio da borda.</param>
public sealed record RegistroDeBilhete(
    Guid Id,
    int Inner,
    byte Tipo,
    bool Repetido,
    DateTimeOffset? MarcadoEm,
    string CodigoMascarado,
    string Impressao,
    Guid ColetaId,
    int Ordem,
    DateTimeOffset ColetadoEm);

/// <summary>
/// Os bilhetes coletados da memória da catraca, tabela <c>collected_ticket</c> (migração 015,
/// Etapa A.9 do docs/35).
/// </summary>
/// <remarks>
/// <para>
/// Quem grava é o worker, um bilhete por vez, antes de pedir o próximo à catraca (R-68). Cada
/// gravação é uma transação: voltar sem exceção é bilhete durável.
/// </para>
/// <para>
/// Deduplicação (o bilhete da DLL não tem sequência nem reinício, EI-039): o mesmo conteúdo
/// — catraca, minuto, tipo, impressão — grava uma vez (índice único). O tipo 128 ("já
/// retornado em coleta anterior", manual 5.2.2) procura o original pela catraca, minuto e
/// impressão, de qualquer tipo; achou, é repetido; não achou, grava o 128 como veio, porque é
/// a única cópia daquela marcação (o worker caiu antes de gravar o original). Que o 128 traga
/// a data e o código do original é <c>A_CONFIRMAR_COM_TOPDATA</c> (NOVO-INT-REC-07, docs/21 §6F).
/// </para>
/// <para>
/// O código nunca chega à base: vira máscara (<see cref="CredentialValue.Mascarar"/>) e
/// impressão HMAC com chave fora da base (<see cref="ImpressaoDeCodigo"/>, Etapa B.1).
/// </para>
/// </remarks>
public sealed class BilhetesColetados : IGravadorDeBilhetes
{
    private readonly SqliteConnectionFactory _fabrica;
    private readonly ImpressaoDeCodigo _impressao;

    public BilhetesColetados(SqliteConnectionFactory fabrica, ImpressaoDeCodigo impressao)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(impressao);
        _fabrica = fabrica;
        _impressao = impressao;
    }

    /// <inheritdoc />
    public DesfechoDaGravacaoDoBilhete Gravar(BilheteColetado bilhete)
    {
        ArgumentNullException.ThrowIfNull(bilhete);

        var b = bilhete.Bilhete;
        var marcadoEm = Minuto(b.Quando);
        var impressao = _impressao.De(b.Cartao);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        if (b.Repetido && TemOriginal(conexao, transacao, bilhete.Inner, marcadoEm, impressao))
        {
            transacao.Commit();
            return DesfechoDaGravacaoDoBilhete.Repetido;
        }

        using var sql = conexao.CreateCommand();
        sql.Transaction = transacao;

        // ON CONFLICT sem alvo só cobre unicidade; CHECK e NOT NULL continuam recusando (com
        // OR IGNORE, um bilhete fora do formato sumiria em silêncio).
        sql.CommandText =
            """
            INSERT INTO collected_ticket
                (id, inner_number, raw_type, repeated, marked_at, code_mask, code_hmac, code_key_id,
                 collection_id, collection_seq, collected_at)
            VALUES ($id, $inner, $tipo, $repetido, $marcado, $mascara, $impressao, $chave,
                    $coleta, $ordem, $coletado)
            ON CONFLICT DO NOTHING;
            """;
        sql.Parameters.AddWithValue("$id", Guid.CreateVersion7(bilhete.ColetadoEm).ToString());
        sql.Parameters.AddWithValue("$inner", bilhete.Inner);
        sql.Parameters.AddWithValue("$tipo", (int)b.Tipo);
        sql.Parameters.AddWithValue("$repetido", b.Repetido ? 1 : 0);
        sql.Parameters.AddWithValue("$marcado", (object?)marcadoEm ?? DBNull.Value);
        sql.Parameters.AddWithValue("$mascara", CredentialValue.Mascarar(b.Cartao));
        sql.Parameters.AddWithValue("$impressao", impressao);
        sql.Parameters.AddWithValue("$chave", _impressao.IdDaChave);
        sql.Parameters.AddWithValue("$coleta", bilhete.ColetaId.ToString());
        sql.Parameters.AddWithValue("$ordem", bilhete.Ordem);
        sql.Parameters.AddWithValue("$coletado", Iso(bilhete.ColetadoEm));
        var gravados = sql.ExecuteNonQuery();

        transacao.Commit();
        return gravados == 1 ? DesfechoDaGravacaoDoBilhete.Gravado : DesfechoDaGravacaoDoBilhete.Repetido;
    }

    /// <summary>Os bilhetes de uma catraca (ou de todas), na ordem em que foram coletados.</summary>
    public IReadOnlyList<RegistroDeBilhete> Listar(int? inner = null, int limite = 1000)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            SELECT id, inner_number, raw_type, repeated, marked_at, code_mask, code_hmac,
                   collection_id, collection_seq, collected_at
            FROM collected_ticket
            WHERE $inner IS NULL OR inner_number = $inner
            ORDER BY collected_at, collection_seq
            LIMIT $limite;
            """;
        sql.Parameters.AddWithValue("$inner", (object?)inner ?? DBNull.Value);
        sql.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 100_000));

        var lista = new List<RegistroDeBilhete>();
        using var leitor = sql.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new RegistroDeBilhete(
                Guid.Parse(leitor.GetString(0)),
                leitor.GetInt32(1),
                (byte)leitor.GetInt32(2),
                leitor.GetInt32(3) == 1,
                leitor.IsDBNull(4) ? null : DateTimeOffset.Parse(leitor.GetString(4) + ":00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                leitor.GetString(5),
                leitor.GetString(6),
                Guid.Parse(leitor.GetString(7)),
                leitor.GetInt32(8),
                DateTimeOffset.Parse(leitor.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    private static bool TemOriginal(SqliteConnection conexao, SqliteTransaction transacao, int inner, string? marcadoEm, string impressao)
    {
        using var sql = conexao.CreateCommand();
        sql.Transaction = transacao;
        sql.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM collected_ticket
                WHERE inner_number = $inner AND ifnull(marked_at, '') = $marcado AND code_hmac = $impressao);
            """;
        sql.Parameters.AddWithValue("$inner", inner);
        sql.Parameters.AddWithValue("$marcado", marcadoEm ?? string.Empty);
        sql.Parameters.AddWithValue("$impressao", impressao);
        return Convert.ToInt64(sql.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    // O bilhete não tem segundos (EI-039): guarda até o minuto, em UTC. Data inválida da
    // catraca chega como MinValue (adapter) e fica nula, sem derrubar a gravação.
    private static string? Minuto(DateTimeOffset quando) =>
        quando == DateTimeOffset.MinValue
            ? null
            : quando.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
