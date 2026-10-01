namespace Access.Application.Devices;

/// <summary>
/// Os três envios de configuração da sequência oficial de conexão, cada um um passo do laço.
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.7 do docs/35 (docs/34 §4.3; anexo 01 §3.3; manual 2.1.2): cfg off-line → mudança
/// automática → cfg on-line. São os estados <c>EnviarCfgOffline</c>,
/// <c>EnviarConfigMudOnlineOffline</c> e <c>EnviarCfgOnline</c> da máquina, que já existiam;
/// até a A.7 os três mandavam a mesma configuração completa (defeito F3, docs/34 §2).
/// </para>
/// <para>
/// Só vale com a chave técnica <c>catraca.sequencia_oficial</c>, desligada até o ensaio
/// INT-SM-021. Cada etapa é uma unidade de montagem e envio no buffer global da DLL: nada com
/// Inner entre a primeira função de montagem e o enviador (ADR-0006).
/// </para>
/// </remarks>
public enum EtapaDaSequenciaOficial
{
    /// <summary>
    /// <c>ConfigurarInnerOffLine</c> (EI-019) + os campos comuns → <c>EnviarConfiguracoes</c> (EI-030).
    /// </summary>
    ConfiguracaoOffLine = 1,

    /// <summary>
    /// <c>HabilitarMudancaOnLineOffLine</c> (EI-028) →
    /// <c>EnviarConfiguracoesMudancaAutomaticaOnLineOffLine</c> (EI-029).
    /// </summary>
    MudancaAutomatica = 2,

    /// <summary>
    /// <c>ConfigurarInnerOnLine</c> (EI-018), ou off-line se o <see cref="RegimeAlvo"/> for
    /// off-line, + os <b>mesmos</b> campos comuns → <c>EnviarConfiguracoes</c> (EI-030).
    /// </summary>
    ConfiguracaoOnLine = 3,
}

/// <summary>Em que regime a catraca termina a sequência de conexão (docs/34 §4.1, grupo Regime).</summary>
/// <remarks>
/// Não é campo novo: é a leitura de <see cref="DeviceConfiguration.Online"/>, reaproveitado na
/// Etapa A.2 e não renomeado (renomear quebraria a configuração de todo teste e de toda camada).
/// Na sequência oficial a cfg off-line usa off-line <b>sempre</b>; é a cfg on-line que segue o
/// regime alvo (anexo 01 §3.1).
/// </remarks>
public enum RegimeAlvo
{
    /// <summary><c>ConfigurarInnerOnLine</c> (EI-018) no último envio. O padrão de fábrica.</summary>
    OnLine,

    /// <summary><c>ConfigurarInnerOffLine</c> (EI-019) também no último envio.</summary>
    OffLine,
}

/// <summary>O regime alvo, lido do campo <see cref="DeviceConfiguration.Online"/>.</summary>
/// <remarks>
/// Método de extensão, e não propriedade da <see cref="DeviceConfiguration"/>: uma propriedade
/// nova seria um campo a mais para a ADR-0020 item 3 cobrir, e não há valor novo nenhum aqui.
/// </remarks>
public static class RegimeDaConfiguracao
{
    /// <summary>On-line quando <see cref="DeviceConfiguration.Online"/> é verdadeiro.</summary>
    public static RegimeAlvo RegimeAlvo(this DeviceConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        return configuracao.Online ? Devices.RegimeAlvo.OnLine : Devices.RegimeAlvo.OffLine;
    }
}
