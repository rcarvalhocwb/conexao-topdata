namespace Access.Domain.Devices;

/// <summary>
/// Estados do ciclo de vida de um equipamento.
/// </summary>
/// <remarks>
/// Os nomes seguem a máquina de estados publicada no Manual de Integração SDK Inner
/// Acesso (seção 2.1.2), de propósito: quando for preciso abrir chamado na Topdata, o
/// vocabulário é o mesmo. Os estados sem equivalente no manual são nossos e estão
/// marcados como tal. Ver docs/03-arquitetura.md, seção 5.
/// </remarks>
public enum DeviceState
{
    /// <summary>Nosso: cadastrado, porém fora de operação por decisão do operador.</summary>
    Disabled,

    /// <summary>Nosso: cadastrado e aguardando o primeiro contato do equipamento.</summary>
    Discovering,

    /// <summary>Manual: tentar estabelecer a conexão (<c>TestarConexaoInner</c>).</summary>
    Conectar,

    /// <summary>Manual: espera com retentativa limitada antes de voltar a conectar.</summary>
    Reconectar,

    /// <summary>Nosso: ler modelo e firmware (<c>ReceberVersaoFirmware</c>).</summary>
    LendoIdentidade,

    /// <summary>
    /// Nosso: conferir modelo e firmware contra a matriz de homologação antes de
    /// configurar qualquer coisa. Ver docs/ADR/ADR-0010-capability-discovery.md
    /// </summary>
    VerificandoCompatibilidade,

    /// <summary>Manual: montar e enviar a configuração de modo off-line.</summary>
    EnviarCfgOffline,

    /// <summary>Manual: enviar a configuração de mudança automática on-line/off-line.</summary>
    EnviarConfigMudOnlineOffline,

    /// <summary>Manual: montar e enviar a configuração de modo on-line.</summary>
    EnviarCfgOnline,

    /// <summary>Manual: configurar as formas de entrada permitidas no modo on-line.</summary>
    ConfigurarEntradasOnline,

    /// <summary>Manual: enviar a mensagem padrão do display.</summary>
    EnviarMsgPadrao,

    /// <summary>Nosso: enviar lista, horários e demais dados de contingência.</summary>
    SincronizandoDadosOffline,

    /// <summary>Manual: laço de <c>ReceberDadosOnLine</c> aguardando evento.</summary>
    Polling,

    /// <summary>Manual: decidir o acesso. Não envolve chamada à DLL.</summary>
    ValidarAcesso,

    /// <summary>Manual: exibir mensagem de acesso negado e aguardar.</summary>
    EnviarMsgAcessoNegado,

    /// <summary>Manual: enviar o comando de liberação de giro.</summary>
    LiberarCatraca,

    /// <summary>
    /// Manual: aguardar a origem 6 (giro) ou a origem 5 (tempo esgotado).
    /// Só aqui uma passagem física pode ser registrada.
    /// </summary>
    MonitoraGiroCatraca,

    /// <summary>Manual: coletar bilhetes da memória do equipamento.</summary>
    ColetarBilhetes,

    /// <summary>Nosso: operando pela lista local do equipamento (nível T2).</summary>
    OfflineAutonomo,

    /// <summary>Nosso: funcionando com capacidade reduzida ou sem contato recente.</summary>
    Degradado,

    /// <summary>Nosso: isolado após falhas repetidas, sem contaminar o grupo.</summary>
    Quarentena,

    /// <summary>Nosso: em manutenção; aceita configuração de técnico.</summary>
    Manutencao,

    /// <summary>Nosso: firmware fora da matriz. <b>Não será configurado.</b></summary>
    FirmwareIncompativel,

    /// <summary>Nosso: dependência ausente ou DLL inutilizável. Falha terminal.</summary>
    FalhaFatalDeDependencia,
}
