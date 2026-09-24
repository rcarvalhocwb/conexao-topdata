using Access.Domain.Devices;

namespace Access.Application.Devices;

/// <summary>Sentido físico a liberar, já resolvido pelo perfil do portão.</summary>
/// <remarks>
/// A escolha entre <c>LiberarCatracaEntrada</c> e <c>LiberarCatracaEntradaInvertida</c>
/// é do perfil físico definido no comissionamento, não de código.
/// Ver docs/04-workflow-collect-card-then-enter.md, seção 7.
/// </remarks>
public enum GateDirection
{
    Entrada,
    Saida,
    DoisSentidos,
}

/// <summary>Como o retorno nativo foi interpretado.</summary>
public enum AdapterStatus
{
    /// <summary>Retorno 0 — comando aceito.</summary>
    Ok,

    /// <summary>Retorno 1 — erro genérico.</summary>
    Erro,

    /// <summary>Retorno 8 — GPF. DLL, .NET Framework, arquitetura ou versões.</summary>
    FalhaDeDependencia,

    /// <summary>Não houve evento dentro do tempo de espera. Situação normal.</summary>
    SemEventos,

    /// <summary>Não há mais bilhetes na memória do equipamento.</summary>
    SemBilhetes,

    /// <summary>Comunicação caiu.</summary>
    ErroDeComunicacao,

    /// <summary>
    /// Retorno fora do conjunto documentado. Preservado, nunca convertido em erro
    /// genérico. Ver docs/ADR/ADR-0018-eventos-desconhecidos.md
    /// </summary>
    RetornoDesconhecido,
}

/// <summary>Resultado de uma chamada ao adapter, com o retorno bruto preservado.</summary>
/// <param name="Status">Interpretação do retorno.</param>
/// <param name="NativeReturn">Valor exato devolvido pela DLL.</param>
/// <param name="Elapsed">Duração da chamada.</param>
public readonly record struct AdapterResult(AdapterStatus Status, int NativeReturn, TimeSpan Elapsed)
{
    public bool IsOk => Status is AdapterStatus.Ok;

    /// <summary>Mapeia um retorno nativo, sem nunca descartar o valor bruto.</summary>
    public static AdapterResult FromNative(int nativo, TimeSpan duracao) => nativo switch
    {
        0 => new AdapterResult(AdapterStatus.Ok, nativo, duracao),
        1 => new AdapterResult(AdapterStatus.Erro, nativo, duracao),
        8 => new AdapterResult(AdapterStatus.FalhaDeDependencia, nativo, duracao),
        _ => new AdapterResult(AdapterStatus.RetornoDesconhecido, nativo, duracao),
    };

    public override string ToString() => $"{Status}(retorno={NativeReturn}, {Elapsed.TotalMilliseconds:F0}ms)";
}

/// <summary>Identidade do equipamento, lida de <c>ReceberVersaoFirmware</c>.</summary>
public sealed record FirmwareInfo(byte Linha, short Variacao, byte VersaoAlta, byte VersaoBaixa, byte VersaoSufixo, bool TemBiometria)
{
    public string Versao => $"{VersaoAlta}.{VersaoBaixa}.{VersaoSufixo}";

    public override string ToString() => $"linha={Linha} versao={Versao} variacao={Variacao} bio={TemBiometria}";
}

/// <summary>Bilhete recuperado da memória do equipamento.</summary>
/// <remarks>
/// Não tem segundos: o manual documenta a assinatura sem esse campo (EI-039).
/// </remarks>
public sealed record Bilhete(byte Tipo, DateTimeOffset Quando, string Cartao);

/// <summary>
/// Interface que o restante do sistema usa para falar com um equipamento Inner.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que a EasyInner.dll possa ser trocada — por um simulador, por um
/// gravador, ou pelo protocolo de baixo nível sob NDA — sem que domínio, interface
/// gráfica, banco ou testes mudem uma linha.
/// Ver docs/ADR/ADR-0001 e docs/ADR/ADR-0021.
/// </para>
/// <para>
/// <b>Todos os métodos são bloqueantes</b> e precisam ser chamados de uma única thread
/// por instância: a DLL não é thread-safe.
/// </para>
/// </remarks>
public interface ITopdataInnerAdapter : IDisposable
{
    /// <summary>Abre a porta TCP em que este worker escuta. Uma vez por instância.</summary>
    AdapterResult AbrirPorta(int porta);

    /// <summary>Fecha a porta.</summary>
    AdapterResult FecharPorta();

    /// <summary>Testa a conexão com um equipamento.</summary>
    AdapterResult TestarConexao(int inner);

    /// <summary>Mantém o equipamento em modo on-line.</summary>
    AdapterResult Ping(int inner);

    /// <summary>Lê modelo e firmware. Base do capability discovery.</summary>
    (AdapterResult Resultado, FirmwareInfo? Firmware) LerFirmware(int inner);

    /// <summary>Lê o relógio do equipamento, para medir desvio.</summary>
    (AdapterResult Resultado, DateTimeOffset? Relogio) LerRelogio(int inner);

    /// <summary>
    /// Monta e envia a configuração <b>completa</b>.
    /// </summary>
    /// <remarks>
    /// Sempre completa: <c>EnviarConfiguracoes</c> preenche com os padrões da DLL tudo
    /// que não for setado. Ver docs/ADR/ADR-0020-configuracao-sempre-completa.md
    /// </remarks>
    AdapterResult EnviarConfiguracaoCompleta(int inner, DeviceConfiguration configuracao);

    /// <summary>
    /// Configura as formas de entrada aceitas no modo on-line.
    /// </summary>
    /// <remarks>
    /// É este passo que reabilita o leitor para a próxima leitura. Pulá-lo é a causa
    /// documentada de "catraca/leitor trava após passar o cartão" (manual, 7.2.3).
    /// </remarks>
    AdapterResult ConfigurarEntradasOnline(int inner);

    /// <summary>Envia a mensagem exibida no display quando ocioso.</summary>
    AdapterResult EnviarMensagemPadrao(int inner, string mensagem);

    /// <summary>
    /// Aguarda um evento. <b>Bloqueia</b> até haver evento, timeout ou erro.
    /// </summary>
    (AdapterResult Resultado, DeviceEvent? Evento) AguardarEvento(int inner, TimeSpan limite);

    /// <summary>Libera o giro no sentido informado, conforme o perfil do portão.</summary>
    AdapterResult LiberarGiro(int inner, GateDirection direcao);

    /// <summary>Aciona o relé 2, que abre a fenda da urna.</summary>
    AdapterResult AcionarReleDaUrna(int inner, TimeSpan tempo);

    /// <summary>
    /// Coleta um bilhete, que é <b>removido da memória do equipamento</b>.
    /// </summary>
    (AdapterResult Resultado, Bilhete? Bilhete) ColetarBilhete(int inner);

    /// <summary>Exibe uma mensagem temporária no display.</summary>
    AdapterResult ExibirMensagemTemporaria(int inner, string mensagem, TimeSpan duracao);
}
