namespace Access.Domain.Access;

/// <summary>
/// Catálogo de motivos de decisão. Valores estáveis e versionados.
/// </summary>
/// <remarks>
/// Renomear um código aqui quebra relatórios e integrações de clientes. Para mudar o
/// texto exibido, altere a tradução na camada de apresentação — não o código.
/// </remarks>
public static class ReasonCodes
{
    // --- Permitido ---
    public static readonly ReasonCode Autorizado = new("AUTORIZADO");
    public static readonly ReasonCode AutorizadoPorListaLocal = new("AUTORIZADO_LISTA_LOCAL");

    // --- Negado: credencial ---
    public static readonly ReasonCode CredencialDesconhecida = new("CREDENCIAL_DESCONHECIDA");
    public static readonly ReasonCode CredencialBloqueada = new("CREDENCIAL_BLOQUEADA");
    public static readonly ReasonCode CredencialComprimentoInvalido = new("CREDENCIAL_COMPRIMENTO_INVALIDO");

    // --- Negado: regra ---
    public static readonly ReasonCode ForaDaJanela = new("CREDENCIAL_FORA_DA_JANELA");
    public static readonly ReasonCode SetorNaoPermitido = new("SETOR_NAO_PERMITIDO");
    public static readonly ReasonCode GateNaoPermitido = new("GATE_NAO_PERMITIDO");
    public static readonly ReasonCode UsosEsgotados = new("USOS_ESGOTADOS");
    public static readonly ReasonCode LotacaoAtingida = new("LOTACAO_ATINGIDA");
    public static readonly ReasonCode AntiPassback = new("ANTI_PASSBACK");
    public static readonly ReasonCode BloqueioEmergencial = new("BLOQUEIO_EMERGENCIAL");

    // --- Suprimido / duplicado ---
    public static readonly ReasonCode ReplaySuprimido = new("REPLAY_SUPRIMIDO");
    public static readonly ReasonCode IngressoJaReservado = new("INGRESSO_JA_RESERVADO");
    public static readonly ReasonCode CredencialConcorrente = new("CREDENCIAL_CONCORRENTE");

    // --- Fluxo da urna ---
    public static readonly ReasonCode UrnaCheia = new("URNA_CHEIA");
    public static readonly ReasonCode RecolhimentoNaoConfirmado = new("RECOLHIMENTO_NAO_CONFIRMADO");
    public static readonly ReasonCode CartaoPresoSuspeita = new("CARTAO_PRESO_SUSPEITA");
    public static readonly ReasonCode RecolhimentoOrfao = new("RECOLHIMENTO_ORFAO");
    public static readonly ReasonCode DesistenciaAntesDoRecolhimento = new("DESISTENCIA_ANTES_RECOLHIMENTO");

    // --- Passagem ---
    public static readonly ReasonCode AutorizadoSemConfirmacao = new("AUTORIZADO_SEM_CONFIRMACAO");
    public static readonly ReasonCode GiroReverso = new("GIRO_REVERSO");

    // --- Operação ---
    public static readonly ReasonCode LiberacaoManual = new("LIBERACAO_MANUAL");
    public static readonly ReasonCode TempoDeDecisaoEsgotado = new("TEMPO_DE_DECISAO_ESGOTADO");
    public static readonly ReasonCode EquipamentoEmManutencao = new("EQUIPAMENTO_EM_MANUTENCAO");
    public static readonly ReasonCode EquipamentoNaoHomologado = new("EQUIPAMENTO_NAO_HOMOLOGADO");
}
