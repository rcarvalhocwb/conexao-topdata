using System.Globalization;
using Access.Domain.Credentials;
using Access.Inteligencia;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Consulta tentativas de uso para avaliação de reuso (IN-03, I.7).
/// Extrai ticket_id, impressão HMAC e dados de tentativa de acesso.db sem expor o código em claro.
/// </summary>
public sealed class ConsultorDeReuso
{
    private readonly LeituraSomenteDaOperacao _leitura;
    private readonly ImpressaoDeCodigo? _impressao;

    /// <summary>
    /// Cria um consultor de reuso.
    /// </summary>
    /// <param name="leitura">Leitor da base de operação (acesso.db).</param>
    /// <param name="impressao">
    /// Calculadora de impressão HMAC. Se nulo, regra 3 (código desconhecido circulando)
    /// fica desligada (o sistema não consegue identificar reuso de desconhecidos).
    /// </param>
    public ConsultorDeReuso(LeituraSomenteDaOperacao leitura, ImpressaoDeCodigo? impressao = null)
    {
        ArgumentNullException.ThrowIfNull(leitura);
        _leitura = leitura;
        _impressao = impressao;
    }

    /// <summary>
    /// Lê tentativas das últimas N minutos para avaliação de reuso.
    /// Retorna ticket_id (UUID local), impressão HMAC (só para desconhecidos), outcome e motivo.
    /// </summary>
    /// <param name="ultimosMinutos">Quantos minutos para trás (padrão: 10 min).</param>
    /// <returns>Lista de tentativas com dados mínimos para reuso.</returns>
    public IReadOnlyList<RegistroDeTentativaDeReuso> TentativasDasUltimas(int ultimosMinutos = 10)
    {
        if (ultimosMinutos <= 0)
        {
            throw new ArgumentException("Deve ser positivo", nameof(ultimosMinutos));
        }

        var agora = DateTimeOffset.UtcNow;
        var corte = agora.AddMinutes(-ultimosMinutos);

        using var conexao = _leitura.Abrir();
        using var comando = conexao.CreateCommand();

        // Seleciona ticket_id, qr_normalized (para HMAC de desconhecidos), outcome, motivo,
        // inner_number e at. Sem expor qr_normalized para log/storage — só para calcular HMAC.
        comando.CommandText = """
            SELECT
                a.ticket_id,
                a.qr_normalized,
                a.outcome,
                a.reason,
                CAST(a.device_id AS INTEGER) AS inner_number,
                a.at
            FROM ticket_use_attempt a
            WHERE a.at > $corte AND a.person_id IS NULL
            ORDER BY a.at ASC;
            """;

        comando.Parameters.AddWithValue("$corte", corte.ToString("O", CultureInfo.InvariantCulture));

        var tentativas = new List<RegistroDeTentativaDeReuso>();

        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            var ticketId = leitor.IsDBNull(0) ? null : leitor.GetString(0);
            var qrNormalizado = leitor.IsDBNull(1) ? null : leitor.GetString(1);
            var outcome = leitor.GetString(2);
            var motivo = leitor.IsDBNull(3) ? null : leitor.GetString(3);
            var innerNumber = leitor.GetInt32(4);
            var em = DateTimeOffset.Parse(leitor.GetString(5), CultureInfo.InvariantCulture);

            // Calcula impressão para desconhecidos (ticket_id = null).
            string? impressaoDeCodigo = null;
            if (ticketId == null && qrNormalizado != null && _impressao != null)
            {
                try
                {
                    impressaoDeCodigo = _impressao.De(qrNormalizado);
                }
                catch
                {
                    // Se a impressão falhar, ignora e continua (não deve bloquear a avaliação).
                }
            }

            tentativas.Add(new RegistroDeTentativaDeReuso(
                TicketId: ticketId,
                Impressao: impressaoDeCodigo,
                Outcome: outcome,
                Motivo: outcome == "negado" ? motivo : null,
                InnerNumber: innerNumber,
                Em: em));
        }

        return tentativas;
    }
}
