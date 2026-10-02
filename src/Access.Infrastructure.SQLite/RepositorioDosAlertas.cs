using System.Globalization;
using Access.Inteligencia;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Persiste e recupera alertas em <c>telemetria.db</c>.
/// </summary>
public sealed class RepositorioDosAlertas
{
    private readonly FabricaDaTelemetria _fabrica;

    public RepositorioDosAlertas(FabricaDaTelemetria fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>
    /// Grava um novo alerta ou atualiza um existente (mesma regra + catraca/portão).
    /// </summary>
    public void Gravar(Alerta alerta)
    {
        ArgumentNullException.ThrowIfNull(alerta);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText =
            """
            INSERT INTO alert (id, regra, session_id, inner_number, portao, nivel, texto, conta,
                               aberto_em, atualizado_em, fechado_em, ciente_por, ciente_em,
                               versao_dos_parametros, simulacao, elegivel_a_rele)
            VALUES ($id, $regra, $sessao, $inner, $portao, $nivel, $texto, $conta,
                    $aberto, $atualizado, $fechado, $ciente_por, $ciente_em,
                    $versao, $simulacao, $rele)
            ON CONFLICT(regra, inner_number, portao) DO UPDATE SET
                nivel = excluded.nivel,
                texto = excluded.texto,
                conta = excluded.conta,
                atualizado_em = excluded.atualizado_em,
                fechado_em = excluded.fechado_em,
                ciente_por = excluded.ciente_por,
                ciente_em = excluded.ciente_em
            WHERE NEW.fechado_em IS NULL OR old.fechado_em IS NULL;
            """;

        comando.Parameters.AddWithValue("$id", alerta.Id);
        comando.Parameters.AddWithValue("$regra", alerta.Regra.ToString().ToLowerInvariant().Replace("nao_especificado", ""));
        comando.Parameters.AddWithValue("$sessao", (object?)alerta.Id ?? DBNull.Value);
        comando.Parameters.AddWithValue("$inner", (object?)alerta.InnerNumber ?? DBNull.Value);
        comando.Parameters.AddWithValue("$portao", (object?)alerta.Portao ?? DBNull.Value);
        comando.Parameters.AddWithValue("$nivel", alerta.Nivel.ToString().ToLowerInvariant().Replace("nao_especificado", ""));
        comando.Parameters.AddWithValue("$texto", alerta.Texto);
        comando.Parameters.AddWithValue("$conta", alerta.Conta);
        comando.Parameters.AddWithValue("$aberto", alerta.AbertaEm.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$atualizado", alerta.AtualizadaEm.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$fechado", alerta.FechadaEm?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        comando.Parameters.AddWithValue("$ciente_por", (object?)alerta.CientePor ?? DBNull.Value);
        comando.Parameters.AddWithValue("$ciente_em", alerta.CienteEm?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        comando.Parameters.AddWithValue("$versao", alerta.VersaoDosParametros);
        comando.Parameters.AddWithValue("$simulacao", alerta.Simulacao ? 1 : 0);
        comando.Parameters.AddWithValue("$rele", alerta.ElegiavelARele ? 1 : 0);

        comando.ExecuteNonQuery();
    }

    /// <summary>Recupera alertas abertos.</summary>
    public IReadOnlyList<Alerta> ListarAbertos()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText =
            """
            SELECT id, regra, inner_number, portao, nivel, texto, conta,
                   aberto_em, atualizado_em, fechado_em, ciente_por, ciente_em,
                   versao_dos_parametros, simulacao, elegivel_a_rele
            FROM alert
            WHERE fechado_em IS NULL
            ORDER BY aberto_em DESC;
            """;

        var alertas = new List<Alerta>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            alertas.Add(LerAlerta(leitor));
        }

        return alertas;
    }

    /// <summary>Marca um alerta como ciente por um operador.</summary>
    public void MarcarComoCiente(string alertaId, string nomeDoOperador, DateTimeOffset em)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alertaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(nomeDoOperador);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText =
            """
            UPDATE alert
            SET ciente_por = $operador, ciente_em = $em
            WHERE id = $id AND ciente_em IS NULL;
            """;

        comando.Parameters.AddWithValue("$id", alertaId);
        comando.Parameters.AddWithValue("$operador", nomeDoOperador);
        comando.Parameters.AddWithValue("$em", em.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        comando.ExecuteNonQuery();
    }

    private static Alerta LerAlerta(SqliteDataReader leitor)
    {
        var regra = (string)leitor["regra"] switch
        {
            "leitor_calado" => RegraDeAlerta.LeitorCalado,
            "comunicacao" => RegraDeAlerta.Comunicacao,
            "relogio" => RegraDeAlerta.Relogio,
            "configuracao" => RegraDeAlerta.Configuracao,
            "desconhecidos" => RegraDeAlerta.Desconhecidos,
            _ => RegraDeAlerta.NaoEspecificado,
        };

        var nivel = (string)leitor["nivel"] switch
        {
            "normal" => NivelDeAlerta.Normal,
            "atencao" => NivelDeAlerta.Atencao,
            "acao" => NivelDeAlerta.Acao,
            "sem_dados" => NivelDeAlerta.SemDados,
            "aprendendo" => NivelDeAlerta.Aprendendo,
            _ => NivelDeAlerta.NaoEspecificado,
        };

        return new Alerta(
            Id: (string)leitor["id"],
            Regra: regra,
            InnerNumber: leitor["inner_number"] is DBNull ? null : (int)leitor["inner_number"],
            Portao: leitor["portao"] is DBNull ? null : (string)leitor["portao"],
            Nivel: nivel,
            Texto: (string)leitor["texto"],
            Conta: (string)leitor["conta"],
            AbertaEm: DateTimeOffset.Parse((string)leitor["aberto_em"], CultureInfo.InvariantCulture),
            AtualizadaEm: DateTimeOffset.Parse((string)leitor["atualizado_em"], CultureInfo.InvariantCulture),
            FechadaEm: leitor["fechado_em"] is DBNull ? null : DateTimeOffset.Parse((string)leitor["fechado_em"], CultureInfo.InvariantCulture),
            CientePor: leitor["ciente_por"] is DBNull ? null : (string)leitor["ciente_por"],
            CienteEm: leitor["ciente_em"] is DBNull ? null : DateTimeOffset.Parse((string)leitor["ciente_em"], CultureInfo.InvariantCulture),
            VersaoDosParametros: (string)leitor["versao_dos_parametros"],
            Simulacao: ((long)leitor["simulacao"]) == 1,
            ElegiavelARele: ((long)leitor["elegivel_a_rele"]) == 1
        );
    }
}
