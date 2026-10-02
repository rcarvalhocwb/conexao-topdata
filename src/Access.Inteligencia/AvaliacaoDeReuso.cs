using System.Globalization;
using System.Text.Json;
using Access.Domain.Credentials;

namespace Access.Inteligencia;

/// <summary>
/// Um registro de tentativa agrupado por ticket_id e impressão de código (IN-03, Etapa I.7
/// do docs/36). Identifica reuso de um ingresso em múltiplas catracas em curto espaço de tempo.
/// </summary>
/// <param name="TicketId">
/// UUID local do ingresso quando conhecido; nulo para código desconhecido (a impressão HMAC
/// o identifica em seu lugar).
/// </param>
/// <param name="Impressao">
/// HMAC do código normalizado (só para código desconhecido). Nulo se ticket_id é conhecido.
/// </param>
/// <param name="Outcome">
/// "consumido" ou "negado" (conforme gravado em ticket_use_attempt).
/// </param>
/// <param name="Motivo">
/// Se negado: MotivoDoUso como gravado. Nulo se consumido.
/// </param>
/// <param name="InnerNumber">Catraca (1–99).</param>
/// <param name="Em">Quando a tentativa foi registrada.</param>
public sealed record RegistroDeTentativaDeReuso(
    string? TicketId,
    string? Impressao,
    string Outcome,
    string? Motivo,
    int InnerNumber,
    DateTimeOffset Em);

/// <summary>
/// Resultado de uma avaliação de reuso (IN-03): se há padrão suspeito e qual.
/// </summary>
/// <param name="Detectado">Se foi detectado reuso suspeito.</param>
/// <param name="Regra">Qual das 3 regras disparou (se Detectado = true).</param>
/// <param name="Catracas">As catracas envolvidas (se Detectado = true).</param>
/// <param name="Prova">
/// Descrição da evidência para o alerta: "ingresso X usado em catraca A às 10:00 e catraca B às
/// 10:02" ou "código desconhecido Y tentado 5 vezes em 2 catracas em 10 min".
/// </param>
public sealed record ResultadoDeReuso(bool Detectado = false, string? Regra = null, int[]? Catracas = null, string? Prova = null);

/// <summary>
/// Regras de reuso para alertar (A4, IN-03, Etapa I.7 do docs/36).
/// </summary>
/// <remarks>
/// <para>
/// Três regras, todas aplicadas a uma janela de 5–10 minutos de tentativas:
///
/// 1. **UsosEsgotados multi-catraca**: negação por "UsosEsgotados" ou "EmIntervaloDeReuso" do
///    mesmo ticket_id em ≥ 2 catracas em ≤ 5 min. É reuso de um ingresso que já foi consumido.
///
/// 2. **Intervalo de reuso**: negação "EmIntervaloDeReuso" do mesmo ticket_id em ≥ 2 catracas
///    em ≤ 5 min. O cartão está programado para usar uma vez a cada N minutos, mas tentou em
///    portões diferentes.
///
/// 3. **Código inválido circulando**: código desconhecido (ticket_id nulo) tentado ≥ 5 vezes
///    em ≥ 2 catracas distintas em ≤ 10 min. É um QR fabricado ou danificado sendo testado
///    em vários pontos.
/// </para>
/// <para>
/// LGPD (docs/36-anexos/02 §6): a impressão é guardada em memória com a chave do cofre
/// e nunca gravada em claro. O ticket_id é um UUID local, pseudônimo.
/// </para>
/// </remarks>
public static class AvaliacaoDeReuso
{
    /// <summary>
    /// Avalia se há reuso suspeito em um conjunto de tentativas de uma janela curta.
    /// </summary>
    /// <param name="tentativas">
    /// Tentativas das últimas tentativas (janela típica: 5–10 min). Esperado: ordenadas por tempo.
    /// </param>
    /// <param name="agora">Instante atual (para contexto; nulo = usa agora mesmo).</param>
    /// <returns>Resultado com Detectado = true se alguma regra disparou.</returns>
    public static ResultadoDeReuso Avaliar(
        IReadOnlyList<RegistroDeTentativaDeReuso> tentativas,
        DateTimeOffset? agora = null)
    {
        ArgumentNullException.ThrowIfNull(tentativas);

        agora ??= DateTimeOffset.UtcNow;

        // Regra 1: UsosEsgotados ou EmIntervaloDeReuso em múltiplas catracas.
        {
            var resultado = VerificarUsosEsgotadosMultiCatraca(tentativas, agora.Value);
            if (resultado.Detectado)
            {
                return resultado;
            }
        }

        // Regra 2: Intervalo de reuso (quando o cartão tem limite de tempo entre usos).
        // (Já coberta pela regra 1 na maioria dos casos; mantém o nome para a documentação.)

        // Regra 3: Código desconhecido em múltiplas catracas.
        {
            var resultado = VerificarCodigoDesconhecidoCirculando(tentativas, agora.Value);
            if (resultado.Detectado)
            {
                return resultado;
            }
        }

        return new ResultadoDeReuso(Detectado: false);
    }

    /// <summary>
    /// Regra 1 e 2: negação de "UsosEsgotados" ou "EmIntervaloDeReuso" do mesmo ticket_id em ≥ 2
    /// catracas em ≤ 5 min.
    /// </summary>
    private static ResultadoDeReuso VerificarUsosEsgotadosMultiCatraca(
        IReadOnlyList<RegistroDeTentativaDeReuso> tentativas,
        DateTimeOffset agora)
    {
        const int JanelaEmSegundos = 5 * 60; // 5 minutos
        var janela = TimeSpan.FromSeconds(JanelaEmSegundos);

        // Agrupa por ticket_id (ignorando desconhecidos, que têm ticket_id = null).
        var porTicket = tentativas
            .Where(t => t.TicketId != null
                    && t.Outcome == "negado"
                    && (t.Motivo == "UsosEsgotados" || t.Motivo == "EmIntervaloDeReuso")
                    && (agora - t.Em) <= janela)
            .GroupBy(t => t.TicketId)
            .ToList();

        foreach (var grupo in porTicket)
        {
            var catracas = grupo.Select(t => t.InnerNumber).Distinct().ToList();

            // Precisa aparecer em ≥ 2 catracas diferentes.
            if (catracas.Count >= 2)
            {
                // Monta a prova descritiva.
                var prova = MontarProvaUsosEsgotados(grupo.ToList(), catracas);

                return new ResultadoDeReuso(
                    Detectado: true,
                    Regra: "UsosEsgotados",
                    Catracas: catracas.OrderBy(x => x).ToArray(),
                    Prova: prova);
            }
        }

        return new ResultadoDeReuso(Detectado: false);
    }

    /// <summary>
    /// Regra 3: código desconhecido tentado ≥ 5 vezes em ≥ 2 catracas diferentes em ≤ 10 min.
    /// </summary>
    private static ResultadoDeReuso VerificarCodigoDesconhecidoCirculando(
        IReadOnlyList<RegistroDeTentativaDeReuso> tentativas,
        DateTimeOffset agora)
    {
        const int JanelaEmSegundos = 10 * 60; // 10 minutos
        var janela = TimeSpan.FromSeconds(JanelaEmSegundos);

        // Agrupa por impressão de código (ticket_id = null, impressao ≠ null).
        var porImpressao = tentativas
            .Where(t => t.TicketId == null && t.Impressao != null
                    && (agora - t.Em) <= janela)
            .GroupBy(t => t.Impressao)
            .ToList();

        foreach (var grupo in porImpressao)
        {
            var catracas = grupo.Select(t => t.InnerNumber).Distinct().ToList();

            // Precisa de ≥ 5 tentativas em ≥ 2 catracas.
            if (grupo.Count() >= 5 && catracas.Count >= 2)
            {
                var prova = MontarProvaCodigoDesconhecido(grupo.ToList(), catracas, grupo.Key);

                return new ResultadoDeReuso(
                    Detectado: true,
                    Regra: "CodigoDesconhecidoCirculando",
                    Catracas: catracas.OrderBy(x => x).ToArray(),
                    Prova: prova);
            }
        }

        return new ResultadoDeReuso(Detectado: false);
    }

    /// <summary>Monta a prova para reuso de ingresso (regra 1/2).</summary>
    private static string MontarProvaUsosEsgotados(
        List<RegistroDeTentativaDeReuso> registros,
        List<int> catracas)
    {
        // Seleciona um registro por catraca para montar a prova (o primeiro de cada).
        var exemplos = registros
            .GroupBy(r => r.InnerNumber)
            .OrderBy(g => g.Key)
            .Take(2)
            .Select(g => g.First())
            .ToList();

        var partes = new List<string>();
        foreach (var ex in exemplos)
        {
            var hora = ex.Em.ToString("HH:mm", CultureInfo.InvariantCulture);
            partes.Add($"catraca {ex.InnerNumber:D2} às {hora}");
        }

        return $"Um ingresso foi negado (já utilizado) em {string.Join(" e ", partes)}.";
    }

    /// <summary>Monta a prova para código desconhecido circulando (regra 3).</summary>
    private static string MontarProvaCodigoDesconhecido(
        List<RegistroDeTentativaDeReuso> registros,
        List<int> catracas,
        string impressao)
    {
        var primeiro = registros.OrderBy(r => r.Em).First();
        var ultima = registros.OrderByDescending(r => r.Em).First();
        var duracao = (ultima.Em - primeiro.Em).TotalSeconds;

        return $"Código desconhecido ({impressao[..8]}...) tentado {registros.Count} vezes " +
               $"em {catracas.Count} catracas em {duracao:F0}s.";
    }
}
