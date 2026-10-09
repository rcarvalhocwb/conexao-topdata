using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// A base da operação (<c>acesso.db</c>) aberta <b>só para leitura</b>: é por aqui, e só por aqui,
/// que o Analisador da camada inteligente a enxerga (Etapa I.0 do docs/36).
/// </summary>
/// <remarks>
/// <para>
/// Invariante I2 (docs/36-anexos/02 §3.1): o Analisador abre <c>acesso.db</c> com
/// <c>Mode=ReadOnly</c> e nunca segura transação de leitura. Cada consulta daqui é uma instrução
/// só, curta, numa conexão que abre e fecha — em WAL, leitor não bloqueia escritor, e a decisão do
/// giro, nos workers, não espera por nada que venha deste lado. <c>query_only</c> fecha a porta
/// uma segunda vez: mesmo que alguém troque o modo de abertura, escrever falha
/// (<c>NOVO-ARQ-IA-02</c>).
/// </para>
/// <para>
/// LGPD (docs/36-anexos/02 §6): nada aqui seleciona coluna com o código do ingresso (o QR
/// normalizado ou o bruto) — um teste de contrato reprova o nome delas neste arquivo. As consultas
/// olham <c>rowid</c> e a chave da camada; o resto entra nas etapas seguintes, sempre sem código.
/// </para>
/// </remarks>
public sealed class LeituraSomenteDaOperacao
{
    /// <summary>
    /// Quanto uma leitura espera. Em WAL, leitor só espera em recuperação ou checkpoint
    /// completo; se demorar mais que isto, o ciclo falha e o seguinte tenta.
    /// </summary>
    public const int EsperaPorTrava = 1000;

    public LeituraSomenteDaOperacao(string caminhoDoBanco)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoDoBanco);

        TextoDeConexao = new SqliteConnectionStringBuilder
        {
            DataSource = caminhoDoBanco,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,

            // A repetição automática do Microsoft.Data.Sqlite numa base presa vai até este prazo
            // (padrão: 30 s); o Analisador desiste antes e tenta no ciclo seguinte.
            DefaultTimeout = 2,
        }.ToString();
    }

    /// <summary>A cadeia de conexão; o teste confere o modo.</summary>
    public string TextoDeConexao { get; }

    /// <summary>Abre uma conexão só de leitura, com <c>query_only</c> ligado.</summary>
    public SqliteConnection Abrir()
    {
        var conexao = new SqliteConnection(TextoDeConexao);
        conexao.Open();
        SqliteConnectionFactory.Executar(conexao, "PRAGMA query_only = ON;");
        SqliteConnectionFactory.Executar(conexao, $"PRAGMA busy_timeout = {EsperaPorTrava.ToString(System.Globalization.CultureInfo.InvariantCulture)};");
        return conexao;
    }

    /// <summary>
    /// O valor de uma chave de <c>edge_setting</c>; nulo se não houver linha. Quem decide se liga é
    /// <c>ChavesDaInteligencia.EstaLigada</c>.
    /// </summary>
    public string? ValorDaChave(string chave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chave);

        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT value FROM edge_setting WHERE key = $chave;";
        comando.Parameters.AddWithValue("$chave", chave);
        return comando.ExecuteScalar() as string;
    }

    /// <summary>O <c>rowid</c> da última tentativa gravada; 0 se nenhuma.</summary>
    public long UltimaTentativa()
    {
        using var conexao = Abrir();
        return SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COALESCE(MAX(rowid), 0) FROM ticket_use_attempt;");
    }

    /// <summary>
    /// Quantas tentativas foram gravadas depois do cursor, e o novo cursor. Uma instrução só, pela
    /// chave primária implícita (<c>rowid</c>): O(linhas novas).
    /// </summary>
    public (long Novas, long Ultima) TentativasDepoisDe(long cursor)
    {
        using var conexao = Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*), COALESCE(MAX(rowid), $cursor) FROM ticket_use_attempt WHERE rowid > $cursor;";
        comando.Parameters.AddWithValue("$cursor", cursor);
        using var leitor = comando.ExecuteReader();
        leitor.Read();
        return (leitor.GetInt64(0), leitor.GetInt64(1));
    }
}
