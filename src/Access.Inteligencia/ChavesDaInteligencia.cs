namespace Access.Inteligencia;

/// <summary>
/// As chaves técnicas da camada inteligente, em <c>edge_setting</c>, sem tela — o padrão das
/// chaves da A.2 e da A.8 (docs/36-anexos/02 §3.5).
/// </summary>
/// <remarks>
/// <para>
/// Todas nascem <b>desligadas</b>: sem linha na tabela, nada liga. Só o valor <c>1</c> liga;
/// qualquer outro, inclusive ilegível, é desligada. A camada só passa a ser ligada por padrão na
/// I.11, depois de <c>NOVO-LOAD-IA-01</c>, <c>NOVO-CHAOS-IA-01</c> e <c>NOVO-SOAK-IA-24H</c>
/// (docs/36 §4).
/// </para>
/// <para>
/// "Por que negou" (I.2) NÃO depende destas chaves: é texto determinístico, montado sob pedido a
/// partir do que a decisão já gravou, e não precisa do Analisador (docs/36-anexos/02 §3.4, linha
/// "sob pedido", e §9: a I.2 não toca no worker).
/// </para>
/// </remarks>
public static class ChavesDaInteligencia
{
    /// <summary>
    /// O serviço roda o Analisador (<c>AnalisadorDaOperacao</c>). Desligada: o Analisador não
    /// lê a base nem cria <c>telemetria.db</c>; o Diagnóstico diz "desligada nesta instalação".
    /// Ligada por padrão na I.11 após <c>NOVO-LOAD-IA-01</c>, <c>NOVO-CHAOS-IA-01</c> e
    /// <c>NOVO-SOAK-IA-24H</c> passarem (docs/36-anexos/02 §9).
    /// </summary>
    public const string Ligada = "inteligencia.ligada";

    /// <summary>
    /// O coletor mínimo no worker (I.1 do docs/36) escreve sinais e saúde em <c>telemetria.db</c>.
    /// Ligada por padrão na I.11.
    /// </summary>
    public const string ColetorLigado = "inteligencia.coletor";

    /// <summary>Valor gravado em <c>edge_setting.value</c> que liga uma chave.</summary>
    public const string ValorQueLiga = "1";

    /// <summary>Lê o valor da chave como a A.8 lê as dela: só <c>"1"</c> liga.</summary>
    public static bool EstaLigada(string? valor) => string.Equals(valor, ValorQueLiga, StringComparison.Ordinal);
}
