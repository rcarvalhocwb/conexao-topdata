namespace Access.Inteligencia;

/// <summary>
/// Regras de alerta (docs/36-anexos/02 §5.2). Cada regra tem seu próprio
/// identificador, dados, conta e limite.
///
/// Etapa I.4: A1–A5 (leitor, comunicação, relógio, configuração, desconhecidos).
/// Etapa I.5: A6–A13 (comunicação v2, giro sem pedido, queda simultânea, liberação recusada).
/// </summary>
public enum RegraDeAlerta
{
    /// <summary>Não especificado.</summary>
    NaoEspecificado = 0,

    /// <summary>
    /// A1: Leitor calado (C13 do 01). Nenhuma leitura em ≥3 min, vizinhas leram ≥20.
    /// Probabilidade Poisson: λ ≥ 7, P(0) &lt; 0,1%.
    /// </summary>
    LeitorCalado = 1,

    /// <summary>
    /// A2: Pico de negação (I.4). Taxa de negação por motivo crescente.
    /// </summary>
    PicoDeNegacao = 2,

    /// <summary>
    /// A3: Desconhecidos e base atrasada (I.4). ≥10 em 5 min + sincronização sem sucesso &gt; 5 min.
    /// </summary>
    Desconhecidos = 3,

    /// <summary>
    /// A4: Reuso ou compartilhamento (I.7). Um ingresso em ≥2 catracas.
    /// </summary>
    Reuso = 4,

    /// <summary>
    /// A5: Liberação manual fora do padrão (I.4). &gt; max(3, 3 × mediana) por hora.
    /// </summary>
    LiberacaoManualForaPadrao = 5,

    /// <summary>
    /// A6: Comunicação instável (C12 do 01; I.5). ≥3 reconexões em 15 min, ou erros de recepção com z ≥ 4.
    /// </summary>
    ComunicacaoInstavel = 6,

    /// <summary>
    /// A7: Relógio derivando (I.4). Divergência &gt; 30s ou inclinação &gt; 2s/h.
    /// </summary>
    RelogioDerivando = 7,

    /// <summary>
    /// A8: Configuração não aplicada / equipamento trocado (C9; I.4). Salva ≠ aplicada &gt; 2 min.
    /// </summary>
    ConfiguracaoNaoAplicada = 8,

    /// <summary>
    /// A9: Liberou e não girou em série (C4; I.5). 3 liberações seguidas sem giro.
    /// </summary>
    LiberouENaoGirou = 9,

    /// <summary>
    /// A10: Giro sem pedido (C3; I.5) e correlação (C10). Origem 6 orfã ou ≥2 catracas em 10s.
    /// </summary>
    GiroSemPedido = 10,

    /// <summary>
    /// A11: Urna cheia (C7; I.5). Origem 20 ou outro sinal que urna está cheia.
    /// </summary>
    UrnaCheia = 11,

    /// <summary>
    /// A12: Queda simultânea (C10b; I.5). ≥2 catracas caíram no mesmo minuto.
    /// </summary>
    QuedaSimultanea = 12,

    /// <summary>
    /// A13: Liberação recusada (I.5). A catraca devolveu retorno ≠ 0 em ≥2 em 10 min.
    /// </summary>
    LiberacaoRecusada = 13,

    /// <summary>
    /// A14: Giro no sentido inesperado (com T14 confirmando complemento; I.5).
    /// </summary>
    GiroSentidoInesperado = 14,

    /// <summary>
    /// A15: Ocupação alta e redistribuição (I.6). ρ ≥ 0,9 com vizinha ≤ 0,6.
    /// </summary>
    OcupacaoAlta = 15,
}

/// <summary>
/// Nível de um sinal ou alerta.
/// </summary>
public enum NivelDeAlerta
{
    /// <summary>Não especificado.</summary>
    NaoEspecificado = 0,

    /// <summary>Dentro do esperado.</summary>
    Normal = 1,

    /// <summary>Alerta: atenção recomendada.</summary>
    Atencao = 2,

    /// <summary>Alerta crítico: ação necessária.</summary>
    Acao = 3,

    /// <summary>Sem dados suficientes.</summary>
    SemDados = 4,

    /// <summary>Aprendendo ainda; mínimo de amostras não atingido.</summary>
    Aprendendo = 5,
}

/// <summary>
/// Um alerta disparado. Ciclo de vida: Aberto → Ciente → Fechado.
/// </summary>
/// <param name="Id">Identificador único (UUIDv7).</param>
/// <param name="Regra">A regra que disparou.</param>
/// <param name="InnerNumber">Número da catraca, ou nulo se alerta de portão.</param>
/// <param name="Portao">Nome do portão, ou nulo se alerta de catraca.</param>
/// <param name="Nivel">Nível do alerta.</param>
/// <param name="Texto">O texto ao operador.</param>
/// <param name="Conta">A conta que disparou, em formato legível (JSON).</param>
/// <param name="AbertaEm">Quando foi disparado.</param>
/// <param name="AtualizadaEm">Última atualização.</param>
/// <param name="FechadaEm">Quando foi fechado (nulo = ainda aberto).</param>
/// <param name="CientePor">Nome do operador que marcou como ciente.</param>
/// <param name="CienteEm">Quando foi marcado como ciente.</param>
/// <param name="VersaoDosParametros">Hash dos parâmetros para reprodutibilidade (I5).</param>
/// <param name="Simulacao">Verdadeiro se veio de catraca simulada.</param>
/// <param name="ElegiavelARele">Verdadeiro se poderia acionar relé 2 no futuro.</param>
public sealed record Alerta(
    string Id,
    RegraDeAlerta Regra,
    int? InnerNumber,
    string? Portao,
    NivelDeAlerta Nivel,
    string Texto,
    string Conta,
    DateTimeOffset AbertaEm,
    DateTimeOffset AtualizadaEm,
    DateTimeOffset? FechadaEm,
    string? CientePor,
    DateTimeOffset? CienteEm,
    string VersaoDosParametros,
    bool Simulacao,
    bool ElegiavelARele)
{
    /// <summary>Verdadeiro se o alerta ainda está aberto (FechadaEm é nulo).</summary>
    public bool EstaAberto => FechadaEm is null;

    /// <summary>Verdadeiro se foi marcado como ciente.</summary>
    public bool EstaAbertoCiente => EstaAberto && CienteEm is not null;
}
