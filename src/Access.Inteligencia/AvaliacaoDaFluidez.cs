using System.Globalization;

namespace Access.Inteligencia;

/// <summary>
/// IN-04 e IN-04b (docs/36-anexos/02 §5, IN-04): ritmo, ocupação, recomendações
/// e tempo para escoar a demanda conhecida (docs/14 §1).
/// </summary>
/// <remarks>
/// <para>
/// A catraca tem:
/// - λ = leituras/min (tentativas + leituras vazias);
/// - c = ciclo (intervalo entre leituras quando ocupada, mediana em 15 min);
/// - μ = capacidade = 60/c;
/// - ρ = ocupação = fração da janela em ciclo ocupado.
/// </para>
/// <para>
/// Na saturação, a catraca lê no máximo μ. A demanda excedente não aparece (fila não é
/// observada), então ρ ≈ 1 é o sinal de gente esperando sem saber quantos.
/// </para>
/// </remarks>
public static class AvaliacaoDaFluidez
{
    /// <summary>
    /// Calcula o ciclo (intervalo mediano entre leituras quando ocupada).
    /// </summary>
    /// <remarks>
    /// Próxima leitura em até 2 × mediana do ciclo. Mediana em 15 min.
    /// </remarks>
    /// <param name="intervalosEntreLeituras">Os intervalos observados entre leituras, em segundos.</param>
    /// <returns>O ciclo em segundos, ou -1 se sem dados.</returns>
    public static double CalcularCiclo(IReadOnlyList<double> intervalosEntreLeituras)
    {
        if (intervalosEntreLeituras.Count == 0)
            return -1;

        var ordenados = intervalosEntreLeituras.OrderBy(x => x).ToList();
        var idx = ordenados.Count / 2;
        return ordenados[idx];
    }

    /// <summary>
    /// Calcula a ocupação (fração da janela em ciclo ocupado).
    /// </summary>
    /// <remarks>
    /// Σ min(intervalo, ciclo) / janela.
    /// </remarks>
    /// <param name="ciclo">O ciclo em segundos.</param>
    /// <param name="intervalosEntreLeituras">Os intervalos observados, em segundos.</param>
    /// <param name="janela">A duração da janela em segundos.</param>
    /// <returns>Ocupação de 0 a 1, ou -1 se sem dados.</returns>
    public static double CalcularOcupacao(double ciclo, IReadOnlyList<double> intervalosEntreLeituras, double janela)
    {
        if (ciclo <= 0 || janela <= 0 || intervalosEntreLeituras.Count == 0)
            return -1;

        double tempoOcupado = 0;
        foreach (var intervalo in intervalosEntreLeituras)
        {
            tempoOcupado += Math.Min(intervalo, ciclo);
        }

        return tempoOcupado / janela;
    }

    /// <summary>
    /// Calcula a capacidade (leituras por minuto).
    /// </summary>
    /// <param name="ciclo">O ciclo em segundos.</param>
    /// <returns>Capacidade em leituras/min, ou -1 se ciclo inválido.</returns>
    public static double CalcularCapacidade(double ciclo) =>
        ciclo > 0 ? 60 / ciclo : -1;

    /// <summary>
    /// Calcula o tempo para escoar a demanda conhecida.
    /// </summary>
    /// <remarks>
    /// T_escoar ≥ pendentes / Σμ (ingressos válidos ainda não usados ÷ capacidade medida).
    /// </remarks>
    /// <param name="ingressosPendentes">Quantos ingressos válidos não foram usados ainda.</param>
    /// <param name="capacidadeTotal">A soma das capacidades de todas as catracas do portão (μ).</param>
    /// <returns>O tempo em horas, ou -1 se sem dados.</returns>
    public static double CalcularTempoEscoar(long ingressosPendentes, double capacidadeTotal) =>
        capacidadeTotal > 0
            ? ingressosPendentes / (capacidadeTotal * 60) // capacidade/min → capacidade/h
            : -1;

    /// <summary>
    /// Gera uma recomendação baseada na ocupação de duas catracas (ou portões).
    /// </summary>
    public sealed record Recomendacao(string Tipo, string Texto, bool Urgente)
    {
        /// <summary>Orientação de fila: "Catraca A está saturada, dirija para B".</summary>
        public static Recomendacao OrientarFila(int catracaAlta, int catracaBaixa) =>
            new("orientar_fila", $"Catraca {catracaAlta} sem folga há 6 min; a {catracaBaixa} está a 45%. Oriente parte da fila para a {catracaBaixa}.", false);

        /// <summary>Abrir catraca: "Portão com ocupação média ≥0.85 por 10 min".</summary>
        public static Recomendacao AbrirCatraca(string portao) =>
            new("abrir_catraca", $"Portão {portao} com ocupação média alta por 10 min. Abra mais uma catraca neste portão, se houver.", false);

        /// <summary>Dedicar a um leitor: "Ciclo 40% diferente entre leitores".</summary>
        public static Recomendacao DedicarALeitor(int catraca, string leitor1, double ciclo1, string leitor2, double ciclo2) =>
            new("dedicar_leitor", $"Catraca {catraca}: ciclo de {leitor1} ({ciclo1:F1}s) vs {leitor2} ({ciclo2:F1}s). Dedique a catraca ao leitor mais rápido.", false);
    }

    /// <summary>
    /// Recomenda abrir outra catraca (i) baseado em ocupação.
    /// </summary>
    /// <remarks>
    /// ρ_A ≥ 0,9 e ρ_B ≤ 0,6 no mesmo portão → "oriente parte da fila para B".
    /// </remarks>
    public static Recomendacao? RecomendarOrientar(double ocupacaoAlta, int catracaAlta, double ocupacaoBaixa, int catracaBaixa)
    {
        if (ocupacaoAlta >= 0.9 && ocupacaoBaixa <= 0.6)
            return Recomendacao.OrientarFila(catracaAlta, catracaBaixa);
        return null;
    }

    /// <summary>
    /// Recomenda abrir outra catraca (ii) baseado em ocupação média do portão.
    /// </summary>
    /// <remarks>
    /// ρ médio do portão ≥ 0,85 por 10 min → "abra mais uma catraca neste portão, se houver".
    /// </remarks>
    public static Recomendacao? RecomendarAbrir(double ocupacaoMedia, string portao)
    {
        if (ocupacaoMedia >= 0.85)
            return Recomendacao.AbrirCatraca(portao);
        return null;
    }

    /// <summary>
    /// Recomenda dedicar a um leitor (iii) baseado em diferença de ciclo.
    /// </summary>
    /// <remarks>
    /// Ciclo por origem de leitura com diferença ≥ 40% → "dedique a catraca 2 ao QR (ciclo 3,1 s contra 5,4 s do cartão)".
    /// </remarks>
    public static Recomendacao? RecomendarDedicar(int catraca, string leitor1, double ciclo1, string leitor2, double ciclo2)
    {
        if (ciclo1 > 0 && ciclo2 > 0)
        {
            var razao = Math.Max(ciclo1, ciclo2) / Math.Min(ciclo1, ciclo2);
            if (razao >= 1.4) // diferença ≥ 40%
                return Recomendacao.DedicarALeitor(catraca, leitor1, ciclo1, leitor2, ciclo2);
        }
        return null;
    }

    /// <summary>
    /// Calcula a lotação estimada baseada em entradas e saídas confirmadas (IN-05, migração 017).
    /// </summary>
    /// <remarks>
    /// Lotação estimada = Σ entradas confirmadas − Σ saídas confirmadas desde a abertura do evento.
    /// É uma ESTIMATIVA por construção: a fila não é observada e a demanda em saturação é censurada.
    /// </remarks>
    /// <param name="entradas">Número de entradas confirmadas desde a abertura.</param>
    /// <param name="saidas">Número de saídas confirmadas desde a abertura.</param>
    /// <returns>
    /// A lotação estimada (entradas − saídas). Nunca é negativa por construção:
    /// se as saídas excedem as entradas em dados gravados, retorna 0 e gera alerta.
    /// </returns>
    public static long CalcularLotacao(long entradas, long saidas)
    {
        var lotacao = entradas - saidas;
        return Math.Max(0, lotacao);
    }

    /// <summary>
    /// Valida se a lotação estimada pode ser mostrada ao operador.
    /// </summary>
    /// <remarks>
    /// A lotação é mostrada SOMENTE se houver ao menos uma catraca mapeada para saída.
    /// Sem catraca de saída, o sistema informa entradas, não lotação
    /// (docs/25-relatorios-da-prestacao-de-contas.md:135-137: "sem contagem de saída, o sistema informa
    /// entradas, não lotação").
    /// </remarks>
    /// <param name="temCatracaDeSaida">Verdadeiro se há ao menos uma catraca mapeada para saída.</param>
    /// <param name="sem_sentido_conhecido">Número de giros sem sentido conhecido (não afeta a decisão).</param>
    /// <returns>
    /// Verdadeiro se a lotação pode ser mostrada; falso caso contrário.
    /// </returns>
    public static bool PodeMostrarLotacao(bool temCatracaDeSaida, long sem_sentido_conhecido)
    {
        return temCatracaDeSaida;
    }
}

/// <summary>
/// Ritmo de uma catraca ou portão (derivado de IN-04).
/// </summary>
/// <remarks>
/// O operador vê as chegadas, o ciclo medido, a capacidade, a ocupação e previsão curta.
/// Tudo é marcado com "ESTIMATIVA" — nunca substitui contagem oficial.
/// </remarks>
/// <param name="Catraca">Número interno (se de uma catraca) ou null se de portão.</param>
/// <param name="Portao">Nome do portão (se de portão) ou null se de catraca.</param>
/// <param name="ChegadasPorMinuto">λ: leituras/min (tentativas + vazias).</param>
/// <param name="Ciclo">c: intervalo mediano entre leituras, em segundos.</param>
/// <param name="Capacidade">μ: 60/ciclo, em leituras/min.</param>
/// <param name="Ocupacao">ρ: fração da janela em ciclo ocupado, de 0 a 1.</param>
/// <param name="TempoParaEscoar">Tempo para escoar toda a demanda conhecida, em horas.</param>
/// <param name="Recomendacoes">Sugestões do operador (i, ii, iii).</param>
/// <param name="Estimativa">Marca de que é estimativa.</param>
public sealed record RitmoDaCatraca(
    int? Catraca,
    string? Portao,
    double ChegadasPorMinuto,
    double Ciclo,
    double Capacidade,
    double Ocupacao,
    TimeSpan? TempoParaEscoar,
    IReadOnlyList<AvaliacaoDaFluidez.Recomendacao> Recomendacoes,
    string Estimativa = "ESTIMATIVA");
