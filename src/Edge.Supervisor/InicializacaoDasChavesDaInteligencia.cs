using System.Globalization;
using Access.Inteligencia;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Inicializa as chaves técnicas da camada inteligente (Etapa I.11 do docs/36) com os valores
/// padrão na primeira partida após a migração que as cria.
/// </summary>
/// <remarks>
/// Após I.10 passar nos testes (<c>NOVO-SOAK-IA-24H</c>, <c>NOVO-REST-IA-01</c>), a camada
/// fica ligada por padrão. Esta classe insere as chaves com valor "1" se não existirem ainda.
/// </remarks>
public sealed class InicializacaoDasChavesDaInteligencia
{
    private readonly SqliteConnectionFactory _fabrica;

    public InicializacaoDasChavesDaInteligencia(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>
    /// Insere as chaves de inteligência com seus valores padrão se não existirem.
    /// Seguro em toda partida (idempotente).
    /// </summary>
    public void GarantirChavesComValoresPadrao()
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var agora = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        // A camada ligada por padrão (I.11)
        GarantirChave(conexao, transacao, ChavesDaInteligencia.Ligada, ChavesDaInteligencia.ValorQueLiga, agora);

        // O coletor do worker ligado por padrão (I.11)
        GarantirChave(conexao, transacao, ChavesDaInteligencia.ColetorLigado, ChavesDaInteligencia.ValorQueLiga, agora);

        transacao.Commit();
    }

    private static void GarantirChave(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string chave,
        string valor,
        string agora)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO edge_setting (key, value, updated_at, updated_by)
            VALUES ($chave, $valor, $agora, 'sistema')
            ON CONFLICT(key) DO NOTHING;
            """;

        comando.Parameters.AddWithValue("$chave", chave);
        comando.Parameters.AddWithValue("$valor", valor);
        comando.Parameters.AddWithValue("$agora", agora);

        comando.ExecuteNonQuery();
    }
}
