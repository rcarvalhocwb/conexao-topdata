namespace Access.Inteligencia;

/// <summary>
/// Regras de alerta da Etapa I.4 (docs/36-anexos/02 §5.2). Cada regra tem seu próprio
/// identificador, dados, conta e limite.
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
    /// A2: Comunicação (C12 do 01). ≥3 reconexões em 15 min, ou erros crescentes.
    /// </summary>
    Comunicacao = 2,

    /// <summary>
    /// A3: Relógio (já existe, confere com 008). Divergência &gt; 30s, inclinação &gt; 2s/h.
    /// </summary>
    Relogio = 3,

    /// <summary>
    /// A4: Configuração não aplicada (A.5/A.6). Salva ≠ aplicada &gt; 2 min.
    /// </summary>
    Configuracao = 4,

    /// <summary>
    /// A5: Desconhecidos e base atrasada. ≥10 em 5 min + sincronização sem sucesso &gt; 5 min.
    /// </summary>
    Desconhecidos = 5,
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
