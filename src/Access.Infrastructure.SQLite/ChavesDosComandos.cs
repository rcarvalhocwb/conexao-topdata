using Access.Application.Devices;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// As chaves técnicas dos comandos novos da Etapa A.8 do docs/35, lidas de <c>edge_setting</c>
/// pelo serviço a cada pedido.
/// </summary>
/// <remarks>
/// <para>
/// Uma chave por comando (<see cref="ComandoDeCatraca.ChaveTecnica"/>), todas desligadas: sem
/// linha na tabela, nada liga. Só o valor <c>1</c> liga; qualquer outro, inclusive ilegível,
/// é desligada — comando de catraca não liga por engano. Não têm tela: liga quem faz o ensaio
/// do docs/21 §6E, na base, como as chaves do §6C.
/// </para>
/// <para>
/// Fica fora de <see cref="ConfiguracoesDaBorda"/> de propósito: aquelas são parâmetros que o
/// worker manda à catraca ao subir; estas só decidem se o serviço aceita um pedido do painel.
/// </para>
/// </remarks>
public sealed class ChavesDosComandos
{
    private readonly SqliteConnectionFactory _fabrica;

    public ChavesDosComandos(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>A chave do tipo está ligada. Tipo sem chave (fase 4b) responde falso.</summary>
    public bool Ligada(TipoDeComando tipo)
    {
        if (ComandoDeCatraca.ChaveTecnica(tipo) is not { } chave)
        {
            return false;
        }

        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText = "SELECT value FROM edge_setting WHERE key = $chave;";
        sql.Parameters.AddWithValue("$chave", chave);
        return sql.ExecuteScalar() is string valor && string.Equals(valor, "1", StringComparison.Ordinal);
    }
}
