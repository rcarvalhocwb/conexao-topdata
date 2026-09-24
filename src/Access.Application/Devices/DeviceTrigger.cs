namespace Access.Application.Devices;

/// <summary>
/// Gatilhos que movem a máquina de estados de um equipamento.
/// </summary>
/// <remarks>
/// Os gatilhos correspondem aos retornos e eventos descritos no Manual de Integração
/// SDK Inner Acesso, seção 2.1.2. Ver docs/03-arquitetura.md, seção 5.
/// </remarks>
public enum DeviceTrigger
{
    /// <summary>O operador habilitou o equipamento.</summary>
    Habilitar,

    /// <summary>O operador desabilitou o equipamento.</summary>
    Desabilitar,

    /// <summary>O equipamento estabeleceu contato pela primeira vez.</summary>
    EquipamentoApareceu,

    /// <summary><c>TestarConexaoInner</c> retornou 0.</summary>
    ConexaoOk,

    /// <summary><c>TestarConexaoInner</c> falhou ou deu timeout.</summary>
    ConexaoFalhou,

    /// <summary>Modelo e firmware foram lidos com sucesso.</summary>
    IdentidadeLida,

    /// <summary>Modelo e firmware constam da matriz como homologados.</summary>
    CompatibilidadeOk,

    /// <summary>Modelo ou firmware fora da matriz. Não configurar.</summary>
    CompatibilidadeRecusada,

    /// <summary>Uma etapa de <c>EnviarConfiguracoes</c> retornou 0.</summary>
    ConfiguracaoEnviada,

    /// <summary>Uma etapa de configuração falhou.</summary>
    ConfiguracaoFalhou,

    /// <summary>Lista, horários e demais dados de contingência foram enviados.</summary>
    DadosOfflineSincronizados,

    /// <summary><c>ReceberDadosOnLine</c> devolveu um evento.</summary>
    EventoRecebido,

    /// <summary><c>ReceberDadosOnLine</c> devolveu "sem eventos".</summary>
    SemEventos,

    /// <summary>Erro de comunicação em qualquer chamada.</summary>
    ErroDeComunicacao,

    /// <summary>A decisão de acesso foi favorável.</summary>
    AcessoPermitido,

    /// <summary>A decisão de acesso foi desfavorável.</summary>
    AcessoNegado,

    /// <summary>A mensagem de negação terminou de ser exibida.</summary>
    MensagemExibida,

    /// <summary>O comando de liberação retornou 0.</summary>
    ComandoDeLiberacaoOk,

    /// <summary>Origem 6: giro confirmado pelo sensor óptico.</summary>
    GiroConfirmado,

    /// <summary>Origem 5: o tempo de acionamento expirou sem giro.</summary>
    TempoDeAcionamentoEsgotado,

    /// <summary>Começar a coleta de bilhetes da memória do equipamento.</summary>
    IniciarColetaDeBilhetes,

    /// <summary><c>ColetarBilhete</c> devolveu um bilhete.</summary>
    BilheteColetado,

    /// <summary><c>ColetarBilhete</c> devolveu "sem bilhetes".</summary>
    SemBilhetes,

    /// <summary>O tempo-limite do estado atual estourou.</summary>
    TempoEsgotado,

    /// <summary>Falhas repetidas: isolar o equipamento sem contaminar o grupo.</summary>
    Quarentenar,

    /// <summary>Técnico colocou o equipamento em manutenção.</summary>
    EntrarEmManutencao,

    /// <summary>Técnico concluiu a manutenção.</summary>
    SairDeManutencao,

    /// <summary>Dependência ausente ou DLL inutilizável.</summary>
    DependenciaFatal,

    /// <summary>Perdeu contato com a borda: passa a operar pela lista local (T2).</summary>
    CairParaListaLocal,
}
