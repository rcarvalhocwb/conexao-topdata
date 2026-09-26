using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Relay.Ingressos;

/// <summary>Uma entrega recebida do provedor, exatamente como chegou.</summary>
/// <param name="Seq">Número de sequência. É o cursor.</param>
/// <param name="RecebidaEm">Quando o relé recebeu.</param>
/// <param name="TipoDeConteudo">Cabeçalho <c>Content-Type</c> original.</param>
/// <param name="CorpoBase64">Corpo bruto, em base64.</param>
/// <param name="CabecalhosJson">Cabeçalhos preservados, menos os de autorização.</param>
public sealed record Entrega(
    long Seq,
    string RecebidaEm,
    string? TipoDeConteudo,
    string CorpoBase64,
    string CabecalhosJson);

/// <summary>
/// Guarda o que o provedor entrega, sem interpretar.
/// </summary>
/// <remarks>
/// <para>
/// <b>O relé não sabe o que é um ingresso.</b> Ele guarda bytes, atribui um número de
/// sequência e devolve em ordem. Isso não é preguiça: é o que faz com que uma mudança no
/// formato do payload — que vai acontecer, e sem aviso — não derrube o recebimento. Quem
/// interpreta é a borda, e se ela errar, os bytes continuam aqui para reprocessar.
/// </para>
/// <para>
/// A tabela é <b>somente inserção</b>, com gatilhos que proíbem alteração e remoção. É a
/// prova de que o provedor entregou, e vale numa discussão de prestação de contas.
/// </para>
/// </remarks>
public sealed class ArmazenamentoDeEntregas
{
    private readonly string _conexao;

    public ArmazenamentoDeEntregas(string caminhoDoArquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoDoArquivo);

        _conexao = new SqliteConnectionStringBuilder
        {
            DataSource = caminhoDoArquivo,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
    }

    /// <summary>Cria o esquema. Seguro para chamar em toda inicialização.</summary>
    public void Preparar()
    {
        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            CREATE TABLE IF NOT EXISTS delivery (
                seq          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                received_at  TEXT    NOT NULL,
                content_type TEXT    NULL,
                body         BLOB    NOT NULL,
                headers_json TEXT    NOT NULL
            );

            CREATE TRIGGER IF NOT EXISTS trg_delivery_sem_update
            BEFORE UPDATE ON delivery
            BEGIN
                SELECT RAISE(ABORT, 'delivery e somente insercao: UPDATE proibido');
            END;

            CREATE TRIGGER IF NOT EXISTS trg_delivery_sem_delete
            BEFORE DELETE ON delivery
            BEGIN
                SELECT RAISE(ABORT, 'delivery e somente insercao: DELETE proibido');
            END;
            """;
        comando.ExecuteNonQuery();
    }

    /// <summary>Grava uma entrega e devolve o número de sequência atribuído.</summary>
    public long Gravar(
        byte[] corpo,
        string? tipoDeConteudo,
        IReadOnlyDictionary<string, string> cabecalhos,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(corpo);
        ArgumentNullException.ThrowIfNull(cabecalhos);

        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO delivery (received_at, content_type, body, headers_json)
            VALUES ($em, $tipo, $corpo, $cabecalhos);
            SELECT last_insert_rowid();
            """;
        comando.Parameters.AddWithValue("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$tipo", (object?)tipoDeConteudo ?? DBNull.Value);
        comando.Parameters.AddWithValue("$corpo", corpo);
        comando.Parameters.AddWithValue("$cabecalhos", JsonSerializer.Serialize(cabecalhos));

        return Convert.ToInt64(comando.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    /// <summary>Lê as entregas a partir de um cursor, em ordem de chegada.</summary>
    public IReadOnlyList<Entrega> Desde(long cursor, int limite)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);

        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT seq, received_at, content_type, body, headers_json
            FROM delivery
            WHERE seq > $cursor
            ORDER BY seq ASC
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$cursor", cursor);
        comando.Parameters.AddWithValue("$limite", limite);

        var entregas = new List<Entrega>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            var corpo = (byte[])leitor["body"];
            entregas.Add(new Entrega(
                leitor.GetInt64(0),
                leitor.GetString(1),
                leitor.IsDBNull(2) ? null : leitor.GetString(2),
                Convert.ToBase64String(corpo),
                leitor.GetString(4)));
        }

        return entregas;
    }

    /// <summary>Maior sequência gravada. Zero quando não há nada.</summary>
    public long UltimaSequencia()
    {
        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COALESCE(MAX(seq), 0) FROM delivery;";
        return Convert.ToInt64(comando.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private SqliteConnection Abrir()
    {
        var conexao = new SqliteConnection(_conexao);
        conexao.Open();

        using (var pragma = conexao.CreateCommand())
        {
            // WAL para que a leitura da borda não trave o recebimento do provedor, e
            // FULL porque perder uma entrega é perder um ingresso que ninguém vai
            // reenviar.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        return conexao;
    }
}
