using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Access.Inteligencia;

#pragma warning disable CA1869

/// <summary>
/// Campos da catraca sobre os quais a camada inteligente pode recomendar mudanças.
/// É o espelho puro do enum de contrato (<c>Contracts.Edge.V1.CampoDaCatraca</c>): a camada
/// não conhece o contrato (invariante NOVO-ARQ-IA-01); o serviço traduz um no outro.
/// </summary>
public enum CampoDaCatraca
{
    NaoEspecificado = 0,
    TipoDeLeitor = 1,
    OperacaoDoLeitor1 = 2,
    OperacaoDoLeitor2 = 3,
    TempoDoAcionamento1 = 4,
    FuncaoDeLiberacaoDaEntrada = 5,
    MensagemPadrao = 6,
    WiegandDoisLeitores = 7,
    FormasDeEntradaOnLine = 8,
}

/// <summary>Dados do cálculo que levou à sugestão, para a evidência em JSON.</summary>
/// <param name="Motivo">Por que surgiu a sugestão (ex: "p95 do Δ + margens").</param>
/// <param name="ValorMedido">O valor ou a taxa medida que disparou a sugestão.</param>
/// <param name="Amostra">Quantas tentativas/minutos foram usados no cálculo.</param>
/// <param name="Referencia">O limite ou a comparação que motivou (ex: "configurado 5 s, medido 4 s").</param>
public sealed record EvidenciaDaSugestao(
    string Motivo,
    string ValorMedido,
    long Amostra,
    string Referencia);

/// <summary>Uma sugestão de mudança numa campo da catraca (Etapa I.9 do docs/36, IN-07).</summary>
/// <param name="Campo">Qual campo (TempoDoAcionamento1, TipoDeLeitor, MensagemPadrao).</param>
/// <param name="ValorAtual">O que está configurado hoje.</param>
/// <param name="ValorSugerido">O que a análise recomenda.</param>
/// <param name="Evidencia">O cálculo que levou à sugestão.</param>
public sealed record SugestaoDeParametrizacao(
    CampoDaCatraca Campo,
    string ValorAtual,
    string ValorSugerido,
    EvidenciaDaSugestao Evidencia);

/// <summary>
/// Avaliação de sugestões (IN-07): analisa o histórico da catraca e recomenda mudanças nos
/// campos de Tempo do relé (P1), Leitor (P2) e Display (P3), sempre com evidência e nunca
/// aplicadas automaticamente — o operador decide usar ou descartar.
/// </summary>
/// <remarks>
/// <para>
/// Funções puras: mesmo dado de entrada (janelas de um tempo, parâmetros) sempre dão o mesmo
/// resultado, byte a byte (invariante I5, docs/36-anexos/02 §3.1).
/// </para>
/// <para>
/// Ciclo: a cada 15 minutos (docs/36-anexos/02 §3.4), o Analisador chama as três sugestões,
/// compara com as abertas, e grava o que for novo. Sugestões antigas ("fechadas" por terem
/// sido usadas ou descartadas) não são regeneradas.
/// </para>
/// <para>
/// Mínimos (docs/36-anexos/02 §4.5): P1 precisa de ≥ 200 liberações; P2 de ≥ 50 leituras com
/// problema; P3 sem mínimo fixo (muitas `ForaDaUrna` por motivo).
/// </para>
/// </remarks>
public static class AvaliacaoDeSugestoes
{
    /// <summary>
    /// P1 — Tempo do relé 1 × giro medido (IN-07 §5.2).
    ///
    /// Distribui os Δ (liberação → giro confirmado) e conta "giro tardio": aquele em que a pessoa
    /// só girou perto do fim da janela do relé (Δ ≥ tempo configurado), ou seja, quase não deu.
    /// Recomenda:
    /// - reduzir para p95(Δ) + 1 s se o tempo configurado é maior e há ≤ 2% de giros tardios;
    /// - aumentar em 1 s se há ≥ 2% de giros tardios (muita gente quase não passou).
    ///
    /// Sempre dentro da faixa 1–50 s (docs/34 §2.18) e da regra 11 (&lt; 8 s, docs/34 §10.4).
    /// </summary>
    /// <param name="catraca">Número da catraca (inner).</param>
    /// <param name="tempoConfigurado">Tempo atual em segundos.</param>
    /// <param name="deltaLiberacaoGiro">Histórico de Δ (liberação → giro confirmado), em segundos; pode incluir nulos (sem giro).</param>
    /// <returns>Sugestão, ou nula se não há motivo para mudar.</returns>
    public static SugestaoDeParametrizacao? SugerirTempoRele(
        int catraca,
        int tempoConfigurado,
        IReadOnlyList<double> deltaLiberacaoGiro)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catraca, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(catraca, 99);
        ArgumentOutOfRangeException.ThrowIfLessThan(tempoConfigurado, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tempoConfigurado, 50);
        ArgumentNullException.ThrowIfNull(deltaLiberacaoGiro);

        // Mínimo: 200 liberações (docs/36 §4.5).
        if (deltaLiberacaoGiro.Count < 200)
        {
            return null;
        }

        // Calcula p95 do Δ (ignorando nulos, que representam "sem giro").
        var comGiro = deltaLiberacaoGiro.Where(d => d >= 0).OrderBy(d => d).ToList();
        if (comGiro.Count < 200)
        {
            return null; // Não há giros o suficiente.
        }

        var p95Index = (int)Math.Ceiling(comGiro.Count * 0.95) - 1;
        var p95 = comGiro[Math.Max(0, p95Index)];
        var sugerido = (int)Math.Ceiling(p95) + 1;

        // Taxa de giros tardios: Δ ≥ tempo configurado (a pessoa quase não passou dentro da janela).
        var girosTardios = comGiro.Count(d => d >= tempoConfigurado);
        var taxaTardia = (double)girosTardios / comGiro.Count;

        // Decisão:
        string motivo;
        int novoTempo;

        if (taxaTardia >= 0.02)
        {
            // ≥ 2% de giros tardios → aumentar 1 s.
            novoTempo = Math.Min(50, tempoConfigurado + 1);
            motivo = $"Giros tardios (Δ ≥ {tempoConfigurado} s): {(taxaTardia * 100):F1}% (limite: 2%)";
        }
        else if (sugerido < tempoConfigurado)
        {
            // Pode reduzir e não há giros tardios demais.
            novoTempo = sugerido;
            motivo = $"p95(Δ) = {p95:F2} s, sem giros tardios";
        }
        else
        {
            return null; // Nenhuma mudança recomendada.
        }

        // Garante faixa válida.
        novoTempo = Math.Clamp(novoTempo, 1, 50);

        if (novoTempo == tempoConfigurado)
        {
            return null; // Nenhuma mudança real.
        }

        var evidencia = new EvidenciaDaSugestao(
            Motivo: motivo,
            ValorMedido: $"{p95:F2} s",
            Amostra: comGiro.Count,
            Referencia: $"Configurado: {tempoConfigurado} s; p95: {p95:F2} s; tardios: {(taxaTardia * 100):F1}%");

        return new SugestaoDeParametrizacao(
            Campo: CampoDaCatraca.TempoDoAcionamento1,
            ValorAtual: tempoConfigurado.ToString(CultureInfo.InvariantCulture),
            ValorSugerido: novoTempo.ToString(CultureInfo.InvariantCulture),
            Evidencia: evidencia);
    }

    /// <summary>
    /// P2 — Leitor (IN-07 §5.2).
    ///
    /// Detecta leituras vazias e códigos desconhecidos concentrados num leitor (por `reader_origin`,
    /// por exemplo QR vs. cartão). Recomenda limpeza, reposicionamento ou confirmação de tipo.
    ///
    /// Não sugere valor (A.6 recusa campos "aguardando confirmação").
    /// </summary>
    /// <param name="catraca">Número da catraca.</param>
    /// <param name="leitoresComProblema">Mapeamento de origen → contagem de (vazias + desconhecidos fora do perfil).</param>
    /// <returns>Sugestão, ou nula se o problema está distribuído ou é pequeno.</returns>
    public static SugestaoDeParametrizacao? SugerirLimpezaLeitor(
        int catraca,
        IReadOnlyDictionary<int, int> leitoresComProblema)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catraca, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(catraca, 99);
        ArgumentNullException.ThrowIfNull(leitoresComProblema);

        if (leitoresComProblema.Count == 0)
        {
            return null;
        }

        // Mínimo: 50 leituras com problema (docs/36 §4.5).
        var totalProblemas = leitoresComProblema.Values.Sum();
        if (totalProblemas < 50)
        {
            return null;
        }

        // Concentração: o leitor com mais problemas tem > 50% do total?
        var origem = leitoresComProblema.OrderByDescending(p => p.Value).First();
        if ((double)origem.Value / totalProblemas <= 0.5)
        {
            return null; // Distribuído entre leitores.
        }

        // Mapeamento de reader_origin (10 do docs/34) para nome.
        var nomeOrigem = origem.Key switch
        {
            2 => "1 (frente)",
            3 => "2 (urna)",
            _ => $"{origem.Key}",
        };

        var motivo = $"Leitor {nomeOrigem}: {origem.Value} vazias/desconhecidos de {totalProblemas} ({(double)origem.Value / totalProblemas * 100:F0}%)";

        // P2 não tem valor sugerido (só recomendação de ação).
        var evidencia = new EvidenciaDaSugestao(
            Motivo: motivo,
            ValorMedido: $"{origem.Value} de {totalProblemas}",
            Amostra: totalProblemas,
            Referencia: "Concentração > 50% em um leitor");

        return new SugestaoDeParametrizacao(
            Campo: CampoDaCatraca.TipoDeLeitor,
            ValorAtual: "—",
            ValorSugerido: $"Limpe/reposicione o leitor {nomeOrigem} da catraca {catraca}",
            Evidencia: evidencia);
    }

    /// <summary>
    /// P3 — Display (IN-07 §5.2).
    ///
    /// Sugere mensagem quando muitas negações são por um motivo específico (ex: `ForaDaUrna`).
    /// A sugestão pode ser o texto "Cartao: use a urna" (≤ 32 caracteres) ou similar, dependendo
    /// do motivo mais frequente.
    /// </summary>
    /// <param name="catraca">Número da catraca.</param>
    /// <param name="mensagemAtual">A mensagem configurada hoje.</param>
    /// <param name="negacoesPorMotivo">Mapeamento de motivo → contagem.</param>
    /// <param name="taxaMinimaDeMotivo">Qual fração do total faz uma recomendação surgir (default 0,30).</param>
    /// <returns>Sugestão, ou nula se nenhum motivo domina.</returns>
    public static SugestaoDeParametrizacao? SugerirDisplay(
        int catraca,
        string mensagemAtual,
        IReadOnlyDictionary<string, long> negacoesPorMotivo,
        double taxaMinimaDeMotivo = 0.30)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(catraca, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(catraca, 99);
        ArgumentNullException.ThrowIfNull(mensagemAtual);
        ArgumentNullException.ThrowIfNull(negacoesPorMotivo);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(taxaMinimaDeMotivo, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(taxaMinimaDeMotivo, 1);

        if (negacoesPorMotivo.Count == 0)
        {
            return null;
        }

        var totalNegacoes = negacoesPorMotivo.Values.Sum();
        if (totalNegacoes == 0)
        {
            return null;
        }

        // Motivo mais frequente.
        var (motivo, contagem) = negacoesPorMotivo.OrderByDescending(p => p.Value).First();
        var taxa = (double)contagem / totalNegacoes;

        if (taxa < taxaMinimaDeMotivo)
        {
            return null; // Nenhum motivo domina.
        }

        // Mapeamento de motivo (do DecisorDeIngresso.cs, C6 do 01) para mensagem sugerida.
        var mensagemSugerida = motivo switch
        {
            "ForaDaUrna" => "Cartao: use a urna",
            "IngressoJaUsado" => "Ingresso ja foi usado",
            "EmIntervaloDeReuso" => "Retorne em alguns min",
            "TipoInativo" => "Este tipo nao entra",
            "UsosEsgotados" => "Ingresso esgotado",
            _ => "Confira o ingresso",
        };

        // Se já tem mensagem e essa é a mesma, não sugere mudar.
        if (string.Equals(mensagemAtual, mensagemSugerida, StringComparison.Ordinal))
        {
            return null;
        }

        var evidencia = new EvidenciaDaSugestao(
            Motivo: $"Muitas negações por '{motivo}' ({contagem} de {totalNegacoes})",
            ValorMedido: $"{contagem} negações",
            Amostra: totalNegacoes,
            Referencia: $"Taxa: {(taxa * 100):F0}% (limite: {(taxaMinimaDeMotivo * 100):F0}%)");

        return new SugestaoDeParametrizacao(
            Campo: CampoDaCatraca.MensagemPadrao,
            ValorAtual: string.IsNullOrWhiteSpace(mensagemAtual) ? "(padrão)" : $"\"{mensagemAtual}\"",
            ValorSugerido: mensagemSugerida,
            Evidencia: evidencia);
    }

    /// <summary>
    /// Serializa a evidência para JSON, no formato que vai para a base (sugestão.evidence).
    /// </summary>
    public static string SerializarEvidencia(EvidenciaDaSugestao evidencia)
    {
        ArgumentNullException.ThrowIfNull(evidencia);
        var opcoes = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Serialize(evidencia, opcoes);
    }

    /// <summary>
    /// Desserializa a evidência a partir de JSON.
    /// </summary>
    public static EvidenciaDaSugestao? DesserializarEvidencia(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var opcoes = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return JsonSerializer.Deserialize<EvidenciaDaSugestao>(json, opcoes);
        }
        catch
        {
            return null;
        }
    }
}
