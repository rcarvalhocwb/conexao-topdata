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
    public static readonly ReasonCode CadastradoNoLote = new("CADASTRADO_NO_LOTE");
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

    // --- Ingresso e cartão de bilheteria (docs/16, docs/19) ---
    public static readonly ReasonCode IngressoCancelado = new("INGRESSO_CANCELADO");
    public static readonly ReasonCode ProvedorDesabilitado = new("PROVEDOR_DESABILITADO");
    public static readonly ReasonCode IntervaloDeReuso = new("INTERVALO_DE_REUSO");
    public static readonly ReasonCode ForaDaUrna = new("FORA_DA_URNA");
    public static readonly ReasonCode VendaAnteriorNaoUsada = new("VENDA_ANTERIOR_NAO_USADA");

    /// <summary>O tipo de entrada do ingresso está desativado (docs/26 §1; docs/35 B.2).</summary>
    public static readonly ReasonCode TipoInativo = new("TIPO_INATIVO");

    /// <summary>Ingresso lido fora do período do evento, com a recusa ligada (docs/57 §4).</summary>
    public static readonly ReasonCode ForaDoPeriodoDoEvento = new("EVENTO_FORA_DO_PERIODO");

    // --- Cadastro local de pessoas (docs/43, ADR-0026) ---
    public static readonly ReasonCode PessoaBloqueada = new("PESSOA_BLOQUEADA");
    public static readonly ReasonCode PessoaInativa = new("PESSOA_INATIVA");
    public static readonly ReasonCode ForaDoHorario = new("FORA_DO_HORARIO");
    public static readonly ReasonCode LimiteDiario = new("LIMITE_DIARIO");
    public static readonly ReasonCode CatracaFechada = new("CATRACA_FECHADA");
    public static readonly ReasonCode PortaoNaoPermitido = new("PORTAO_NAO_PERMITIDO");
    public static readonly ReasonCode CredencialInativa = new("CREDENCIAL_INATIVA");
    public static readonly ReasonCode PessoaForaDaValidade = new("PESSOA_FORA_DA_VALIDADE");

    // --- Uso de cada entrada da catraca (docs/61) ---

    /// <summary>Ingresso no leitor da frente ou no teclado, reservados à equipe.</summary>
    public static readonly ReasonCode SomenteEquipe = new("ENTRADA_SO_EQUIPE");

    /// <summary>Crachá de pessoa na fenda da urna, que recolheria o cartão.</summary>
    public static readonly ReasonCode CredencialNaUrna = new("CREDENCIAL_NA_URNA");

    /// <summary>A entrada usada está desligada.</summary>
    public static readonly ReasonCode EntradaDesligada = new("ENTRADA_DESLIGADA");

    // --- Operação ---
    public static readonly ReasonCode LiberacaoManual = new("LIBERACAO_MANUAL");
    public static readonly ReasonCode TempoDeDecisaoEsgotado = new("TEMPO_DE_DECISAO_ESGOTADO");
    public static readonly ReasonCode EquipamentoEmManutencao = new("EQUIPAMENTO_EM_MANUTENCAO");
    public static readonly ReasonCode EquipamentoNaoHomologado = new("EQUIPAMENTO_NAO_HOMOLOGADO");

    /// <summary>A base local falhou ao responder. Nega — B4 (fail-safe × fail-secure) segue sem resposta.</summary>
    public static readonly ReasonCode FalhaNaBaseLocal = new("FALHA_NA_BASE_LOCAL");

    /// <summary>Motivo que o catálogo não conhece. Nega e aparece no relatório, em vez de virar outro código.</summary>
    public static readonly ReasonCode MotivoNaoMapeado = new("MOTIVO_NAO_MAPEADO");
}
