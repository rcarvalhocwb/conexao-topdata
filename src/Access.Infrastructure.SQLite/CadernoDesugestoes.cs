using Access.Inteligencia;
using Contracts.Edge.V1;
using Microsoft.Data.Sqlite;
using IntelSugestao = Access.Inteligencia.SugestaoDeParametrizacao;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Registro e consulta de sugestões de parametrização em <c>telemetria.db</c> (T008). O Analisador
/// (ciclo de 15 min) escreve sugestões novas; o serviço (RPC) lê e marca como usadas/descartadas.
/// </summary>
public sealed class CadernoDesugestoes
{
    private readonly FabricaDaTelemetria _fabrica;

    public CadernoDesugestoes(FabricaDaTelemetria fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>
    /// Grava uma sugestão nova (só do Analisador, ciclo 15 min).
    /// </summary>
    public void Gravar(int catraca, IntelSugestao sugestao, string? sessao, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(sugestao);
        ArgumentOutOfRangeException.ThrowIfLessThan(catraca, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(catraca, 99);

        var id = Uuid7.Gerar().ToString();
        var campo = sugestao.Campo switch
        {
            CampoDaCatraca.TempoDoAcionamento1 => "TempoDoAcionamento1",
            CampoDaCatraca.TipoDeLeitor => "TipoDeLeitor",
            CampoDaCatraca.MensagemPadrao => "MensagemPadrao",
            _ => throw new ArgumentException($"Campo não suportado: {sugestao.Campo}", nameof(sugestao)),
        };

        var evidencia = AvaliacaoDeSugestoes.SerializarEvidencia(sugestao.Evidencia);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText = """
            INSERT INTO suggestion (
                id, session_id, inner_number, field, current_value, suggested_value, evidence,
                created_at, situation
            ) VALUES (
                @id, @sessao, @catraca, @campo, @atual, @sugerido, @evidencia, @criada, @situacao
            )
            """;

        comando.Parameters.AddWithValue("@id", id);
        comando.Parameters.AddWithValue("@sessao", (object?)sessao ?? DBNull.Value);
        comando.Parameters.AddWithValue("@catraca", catraca);
        comando.Parameters.AddWithValue("@campo", campo);
        comando.Parameters.AddWithValue("@atual", sugestao.ValorAtual);
        comando.Parameters.AddWithValue("@sugerido", sugestao.ValorSugerido);
        comando.Parameters.AddWithValue("@evidencia", evidencia);
        comando.Parameters.AddWithValue("@criada", agora.ToRfc3339String());
        comando.Parameters.AddWithValue("@situacao", "aberta");

        comando.ExecuteNonQuery();
    }

    /// <summary>
    /// Lista as sugestões abertas de uma catraca (RPC `ObterSugestoes`).
    /// </summary>
    public IReadOnlyList<(string Id, CampoDaCatraca Campo, string Atual, string Sugerido)> ListarAbertas(int catraca)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catraca, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(catraca, 99);

        var resultado = new List<(string, CampoDaCatraca, string, string)>();

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText = """
            SELECT id, field, current_value, suggested_value
            FROM suggestion
            WHERE inner_number = @catraca AND situation = 'aberta'
            ORDER BY created_at DESC
            """;

        comando.Parameters.AddWithValue("@catraca", catraca);

        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            var id = leitor.GetString(0);
            var campo = leitor.GetString(1) switch
            {
                "TempoDoAcionamento1" => CampoDaCatraca.TempoDoAcionamento1,
                "TipoDeLeitor" => CampoDaCatraca.TipoDeLeitor,
                "MensagemPadrao" => CampoDaCatraca.MensagemPadrao,
                _ => CampoDaCatraca.CampoDaCatracaNaoEspecificado,
            };
            var atual = leitor.GetString(2);
            var sugerido = leitor.GetString(3);

            resultado.Add((id, campo, atual, sugerido));
        }

        return resultado;
    }

    /// <summary>
    /// Marca uma sugestão como usada ou descartada (RPC `RegistrarDestinoDaSugestao`).
    /// </summary>
    public void RegistrarDestino(string sugestaoId, string situacao, string operador, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(sugestaoId);
        ArgumentNullException.ThrowIfNull(operador);
        ArgumentNullException.ThrowIfNull(situacao);

        if (situacao is not ("usada" or "descartada"))
        {
            throw new ArgumentException($"Situação inválida: {situacao}", nameof(situacao));
        }

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText = """
            UPDATE suggestion
            SET situation = @situacao, used_by = @operador, used_at = @agora
            WHERE id = @id
            """;

        comando.Parameters.AddWithValue("@id", sugestaoId);
        comando.Parameters.AddWithValue("@situacao", situacao);
        comando.Parameters.AddWithValue("@operador", operador);
        comando.Parameters.AddWithValue("@agora", agora.ToRfc3339String());

        comando.ExecuteNonQuery();
    }
}
