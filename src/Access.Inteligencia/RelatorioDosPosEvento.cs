using System.Globalization;
using System.Text.Json;

namespace Access.Inteligencia;

#pragma warning disable CA1822, CA1869, CA1829

/// <summary>
/// Relatório pós-evento com achados determinísticos (Etapa I.10 do docs/36, IN-09).
/// Cada método implementa um achado (R1–R8) para a prestação de contas.
/// </summary>
/// <remarks>
/// Todos os achados usam dados agregados já gravados em <c>telemetria.db</c> (G-01…G-14):
/// tentativas, sinais, transições, saúde. Sem código de ingresso, máscara nem identificador
/// (invariante I6, §3.1; docs/36-anexos/02 §6).
/// </remarks>
public sealed class RelatorioDosPosEvento
{
    private readonly string _versaoDosParametros;
    private readonly bool _simulacao;

    /// <param name="versaoDosParametros">Hash dos parâmetros do Analisador, para reprodutibilidade (I5).</param>
    /// <param name="simulacao">Se o evento foi em simulação ou operação real.</param>
    public RelatorioDosPosEvento(string versaoDosParametros, bool simulacao = false)
    {
        ArgumentNullException.ThrowIfNull(versaoDosParametros);
        _versaoDosParametros = versaoDosParametros;
        _simulacao = simulacao;
    }

    /// <summary>R1 — Maior pico de 15 min por portão.</summary>
    /// <remarks>
    /// Lê as tentativas liberadas e agrupa por minuto e portão. A janela móvel de 15 min
    /// encontra o pico. Sem portão configurado, cada catraca é seu portão.
    /// </remarks>
    public (string? Portao, long LeituraNoPico, DateTimeOffset? MomentoDoPico) MaiorPicoDe15Min(
        IEnumerable<(int CatracaNumero, DateTimeOffset Em, string? Portao)> tentativas)
    {
        ArgumentNullException.ThrowIfNull(tentativas);

        var agrupadas = tentativas
            .Where(t => t.Em != default)
            .GroupBy(t => (t.Portao ?? $"Catraca_{t.CatracaNumero:D2}", t.Em.Minute))
            .Select(g => new
            {
                Portao = g.Key.Item1,
                Minuto = g.Key.Item2,
                Contagem = g.LongCount(),
                UltimaMoment = g.Max(t => t.Em),
            })
            .ToList();

        if (agrupadas.Count == 0)
        {
            return (null, 0, null);
        }

        var pico = agrupadas.OrderByDescending(x => x.Contagem).FirstOrDefault();
        return (pico?.Portao, pico?.Contagem ?? 0, pico?.UltimaMoment);
    }

    /// <summary>R2 — Catraca mais lenta (mediana de Δ liberação→giro).</summary>
    /// <remarks>
    /// Calcula a mediana do intervalo entre liberação (origem 5) e giro confirmado (origem 6)
    /// para cada catraca. Retorna a com maior mediana.
    /// </remarks>
    public (int? CatracaNumero, TimeSpan DeltaMediana) CatracaMaisLenta(
        IEnumerable<(int CatracaNumero, TimeSpan? DeltaLiberacaoGiro)> medidas)
    {
        ArgumentNullException.ThrowIfNull(medidas);

        var porCatraca = medidas
            .Where(m => m.DeltaLiberacaoGiro.HasValue)
            .GroupBy(m => m.CatracaNumero)
            .Select(g => new
            {
                CatracaNumero = g.Key,
                DeltaMediana = CalcularMediana(g.Select(m => m.DeltaLiberacaoGiro!.Value).ToList()),
            })
            .ToList();

        if (porCatraca.Count == 0)
        {
            return (null, TimeSpan.Zero);
        }

        var maisLenta = porCatraca.OrderByDescending(c => c.DeltaMediana).FirstOrDefault();
        return (maisLenta?.CatracaNumero, maisLenta?.DeltaMediana ?? TimeSpan.Zero);
    }

    /// <summary>R3 — Catraca mais ociosa (menor ocupação ρ).</summary>
    /// <remarks>
    /// A ocupação é a fração da janela em ciclo ocupado (Σ min(intervalo, c) / janela).
    /// Retorna a catraca com menor ocupação.
    /// </remarks>
    public (int? CatracaNumero, double Ocupacao) CatracaMaisOciosa(
        IEnumerable<(int CatracaNumero, double Ocupacao)> ocupacoes)
    {
        ArgumentNullException.ThrowIfNull(ocupacoes);

        var lista = ocupacoes.Where(o => o.Ocupacao >= 0.0).ToList();

        if (lista.Count == 0)
        {
            return (null, 0.0);
        }

        var maisOciosa = lista.OrderBy(o => o.Ocupacao).FirstOrDefault();
        return (maisOciosa.CatracaNumero, maisOciosa.Ocupacao);
    }

    /// <summary>R4 — Desperdício (Σ liberações sem giro × tempo do relé).</summary>
    /// <remarks>
    /// Uma liberação sem giro é perda de acionamento. O custo é o tempo do relé acionado
    /// sem resultado. Soma em segundos.
    /// </remarks>
    public double Desperdicio(
        long liberacoesSemGiro,
        TimeSpan TempoDoRele)
    {
        if (liberacoesSemGiro <= 0 || TempoDoRele <= TimeSpan.Zero)
        {
            return 0.0;
        }

        return liberacoesSemGiro * TempoDoRele.TotalSeconds;
    }

    /// <summary>R5 — Disponibilidade por catraca (segundos em operação / total).</summary>
    /// <remarks>
    /// Razão entre segundos em que a catraca estava Online e segundos totais do evento.
    /// Valor de 0.0 a 1.0.
    /// </remarks>
    public double DisponibilidadeMedia(
        IEnumerable<(int CatracaNumero, TimeSpan TempoOnline, TimeSpan TempoTotal)> medidas)
    {
        ArgumentNullException.ThrowIfNull(medidas);

        var lista = medidas.ToList();

        if (lista.Count == 0)
        {
            return 0.0;
        }

        var totalOnline = lista.Sum(m => m.TempoOnline.TotalSeconds);
        var totalGeral = lista.Sum(m => m.TempoTotal.TotalSeconds);

        if (totalGeral <= 0)
        {
            return 0.0;
        }

        return totalOnline / totalGeral;
    }

    /// <summary>R6 — Alertas do dia: contagem e ciência.</summary>
    /// <remarks>
    /// Retorna quantos alertas foram disparados e quantos foram marcados como ciente
    /// por um operador.
    /// </remarks>
    public (long Total, long Ciencia) AlertasDoEvento(
        IEnumerable<(bool Ciencia, DateTimeOffset? CienteEm)> alertas)
    {
        ArgumentNullException.ThrowIfNull(alertas);

        var lista = alertas.ToList();
        var total = lista.LongCount();
        var ciencia = lista.Count(a => a.Ciencia && a.CienteEm.HasValue);

        return (total, ciencia);
    }

    /// <summary>R7 — Motivos de negação e variação no dia.</summary>
    /// <remarks>
    /// Agrupa negações por motivo, conta por catraca e calcula a variação (máx - mín / mín).
    /// </remarks>
    public Dictionary<string, (long Total, double Variacao)> NegacoesPorMotivo(
        IEnumerable<(string Motivo, int CatracaNumero, long Contagem)> negacoes)
    {
        ArgumentNullException.ThrowIfNull(negacoes);

        var agrupadas = negacoes
            .GroupBy(n => n.Motivo)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var total = g.Sum(n => n.Contagem);
                    var porCatraca = g.Select(n => n.Contagem).ToList();
                    var min = porCatraca.Min();
                    var max = porCatraca.Max();
                    var variacao = min > 0 ? (max - min) / (double)min : 0.0;
                    return (total, variacao);
                });

        return agrupadas;
    }

    /// <summary>R8 — Dimensionamento para o próximo evento (pico de chegadas / μ medido).</summary>
    /// <remarks>
    /// Pico de chegadas (leituras/min no pico) ÷ capacidade média medida (60 / ciclo mediano).
    /// Retorna quantas catracas seriam necessárias no pico para manter ρ &lt; 0.85.
    /// </remarks>
    public double DimensionamentoParaProximoEvento(
        long leiturasPorMinutoNoPico,
        TimeSpan CicloMedianoMedido)
    {
        if (CicloMedianoMedido <= TimeSpan.Zero)
        {
            return 0.0;
        }

        var capacidade = 60.0 / CicloMedianoMedido.TotalSeconds;

        if (capacidade <= 0)
        {
            return 0.0;
        }

        // Assumindo ρ desejado de 0.85
        return leiturasPorMinutoNoPico / (capacidade * 0.85);
    }

    /// <summary>
    /// Cria um record de insight completo para gravar em <c>telemetria.db</c>.
    /// </summary>
    public InsightDoEvento CriarInsight(
        DateTimeOffset encerramentoDo,
        string? portaoMaiorPico,
        long leiturasMaiorPico,
        DateTimeOffset? momentoMaiorPico,
        int? catracaMaisLenta,
        int deltaMaisLentaMs,
        int? catracaMaisOciosa,
        double ocupacaoMaisOciosa,
        double desperdiciodeSegundos,
        double disponibilidadeMedia,
        long totalAlertas,
        long alertasComCiencia,
        double dimensionamentoCatracas)
    {
        var id = Guid.CreateVersion7().ToString("N");
        var achados = new Dictionary<string, object>
        {
            { "maior_pico", new { portao = portaoMaiorPico, leituras = leiturasMaiorPico, momento = momentoMaiorPico } },
            { "catraca_lenta", new { numero = catracaMaisLenta, delta_ms = deltaMaisLentaMs } },
            { "catraca_ociosa", new { numero = catracaMaisOciosa, ocupacao = ocupacaoMaisOciosa } },
            { "desperdicio_segundos", desperdiciodeSegundos },
            { "disponibilidade_media", disponibilidadeMedia },
            { "alertas", new { total = totalAlertas, ciencia = alertasComCiencia } },
            { "dimensionamento_catracas", dimensionamentoCatracas },
        };

        var achadosJson = JsonSerializer.Serialize(achados, new JsonSerializerOptions { WriteIndented = false });

        return new InsightDoEvento(
            id,
            null,
            encerramentoDo,
            portaoMaiorPico,
            leiturasMaiorPico,
            momentoMaiorPico?.ToString("O", CultureInfo.InvariantCulture),
            catracaMaisLenta,
            deltaMaisLentaMs,
            catracaMaisOciosa,
            ocupacaoMaisOciosa,
            desperdiciodeSegundos,
            disponibilidadeMedia,
            totalAlertas,
            alertasComCiencia,
            dimensionamentoCatracas,
            _versaoDosParametros,
            _simulacao,
            achadosJson);
    }

    /// <summary>Calcula a mediana de uma lista de valores TimeSpan.</summary>
    private static TimeSpan CalcularMediana(List<TimeSpan> valores)
    {
        if (valores.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var sorted = valores.OrderBy(x => x.TotalMilliseconds).ToList();
        var count = sorted.Count;

        if (count % 2 == 1)
        {
            return sorted[count / 2];
        }

        var mid1 = sorted[count / 2 - 1].TotalMilliseconds;
        var mid2 = sorted[count / 2].TotalMilliseconds;
        return TimeSpan.FromMilliseconds((mid1 + mid2) / 2.0);
    }
}

/// <summary>
/// Registro de um insight pós-evento para gravar em <c>telemetria.db</c>.
/// </summary>
/// <remarks>
/// Imutável: gravado uma vez quando o evento encerra e nunca editado depois.
/// </remarks>
public sealed record InsightDoEvento(
    string Id,
    string? SessionId,
    DateTimeOffset EncerradoEm,
    string? PortaoMaiorPico,
    long LeiturasMaiorPico,
    string? MomentoDoPico,
    int? CatracaMaisLentaNumero,
    int CatracaMaisLentaDeltaMs,
    int? CatracaMaisOciosaNumero,
    double CatracaMaisOciosaOcupacao,
    double DesperdiciodeSegundos,
    double DisponibilidadeMedia,
    long TotalAlertas,
    long AlertasComCiencia,
    double DimensionamentoCatracas,
    string VersaoDosParametros,
    bool Simulacao,
    string AchadosJson);
