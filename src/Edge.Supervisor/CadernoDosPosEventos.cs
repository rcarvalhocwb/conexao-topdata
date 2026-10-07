using System.Globalization;
using Access.Infrastructure.SQLite;
using Access.Inteligencia;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Registro de insights pós-evento em <c>telemetria.db</c> (T009, Etapa I.10 do docs/36).
/// Gravado quando o evento encerra. Vive no serviço (Edge.Supervisor), não na infraestrutura
/// comum, porque conhece a camada inteligente (invariante NOVO-ARQ-IA-01: o worker nunca a alcança).
/// </summary>
public sealed class CadernoDosPosEventos
{
    private readonly FabricaDaTelemetria _fabrica;

    public CadernoDosPosEventos(FabricaDaTelemetria fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Grava um insight quando o evento encerra.</summary>
    public void RegistrarInsight(InsightDoEvento insight)
    {
        ArgumentNullException.ThrowIfNull(insight);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        comando.CommandText =
            """
            INSERT INTO insight (
                id, session_id, encerrado_em, maior_pico_portao, maior_pico_leituras,
                maior_pico_minuto, catraca_mais_lenta_numero, catraca_mais_lenta_delta_ms,
                catraca_mais_ociosa_numero, catraca_mais_ociosa_ocupacao, desperdicio_segundos,
                disponibilidade_media, total_alertas, alertas_ciencia, dimensionamento_catracas,
                versao_dos_parametros, simulacao, achados_json
            ) VALUES (
                $id, $sessao, $encerrado, $pico_portao, $pico_leituras,
                $pico_minuto, $lenta_numero, $lenta_delta,
                $ociosa_numero, $ociosa_ocupacao, $desperdicio,
                $disponibilidade, $total_alertas, $ciencia, $dimensionamento,
                $versao, $simulacao, $achados
            );
            """;

        comando.Parameters.AddWithValue("$id", insight.Id);
        comando.Parameters.AddWithValue("$sessao", (object?)insight.SessionId ?? DBNull.Value);
        comando.Parameters.AddWithValue("$encerrado", insight.EncerradoEm.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$pico_portao", (object?)insight.PortaoMaiorPico ?? DBNull.Value);
        comando.Parameters.AddWithValue("$pico_leituras", insight.LeiturasMaiorPico);
        comando.Parameters.AddWithValue("$pico_minuto", (object?)insight.MomentoDoPico ?? DBNull.Value);
        comando.Parameters.AddWithValue("$lenta_numero", (object?)insight.CatracaMaisLentaNumero ?? DBNull.Value);
        comando.Parameters.AddWithValue("$lenta_delta", insight.CatracaMaisLentaDeltaMs);
        comando.Parameters.AddWithValue("$ociosa_numero", (object?)insight.CatracaMaisOciosaNumero ?? DBNull.Value);
        comando.Parameters.AddWithValue("$ociosa_ocupacao", insight.CatracaMaisOciosaOcupacao);
        comando.Parameters.AddWithValue("$desperdicio", insight.DesperdiciodeSegundos);
        comando.Parameters.AddWithValue("$disponibilidade", insight.DisponibilidadeMedia);
        comando.Parameters.AddWithValue("$total_alertas", insight.TotalAlertas);
        comando.Parameters.AddWithValue("$ciencia", insight.AlertasComCiencia);
        comando.Parameters.AddWithValue("$dimensionamento", insight.DimensionamentoCatracas);
        comando.Parameters.AddWithValue("$versao", insight.VersaoDosParametros);
        comando.Parameters.AddWithValue("$simulacao", insight.Simulacao ? 1 : 0);
        comando.Parameters.AddWithValue("$achados", insight.AchadosJson);

        comando.ExecuteNonQuery();
    }
}
