using Access.Domain.Devices;

namespace Access.Application.Devices;

/// <summary>A liberação a executar: cada valor é exatamente uma função da DLL.</summary>
/// <remarks>
/// <para>
/// A escolha entre <c>LiberarCatracaEntrada</c> e as variantes invertidas é do perfil
/// físico definido no comissionamento (<see cref="GatePhysicalProfile.LiberacaoDaEntrada"/>),
/// não de código. Ver docs/04-workflow-collect-card-then-enter.md, seção 7.
/// </para>
/// <para>
/// O adapter traduz um para um e não consulta perfil nenhum: antes, laço e adapter
/// aplicavam a inversão cada um por sua conta, e o perfil invertido acabava em
/// <c>LiberarCatracaSaidaInvertida</c> (defeito F1, docs/34 §2). Os valores novos entram
/// no fim para não mudar os números dos que já existiam.
/// </para>
/// </remarks>
public enum GateDirection
{
    /// <summary><c>LiberarCatracaEntrada</c> (EI-041).</summary>
    Entrada,

    /// <summary><c>LiberarCatracaSaida</c> (EI-042).</summary>
    Saida,

    /// <summary><c>LiberarCatracaDoisSentidos</c> (EI-045). Só evacuação: permite carona.</summary>
    DoisSentidos,

    /// <summary><c>LiberarCatracaEntradaInvertida</c> (EI-043).</summary>
    EntradaInvertida,

    /// <summary><c>LiberarCatracaSaidaInvertida</c> (EI-044).</summary>
    SaidaInvertida,
}

/// <summary>O bip a acionar: cada valor é exatamente uma função da DLL.</summary>
/// <remarks>
/// Só por comando manual do operador (Etapa A.8 do docs/35), cada um atrás da sua chave
/// técnica desligada. Nunca acoplado à decisão automática: cada chamada a mais no caminho da
/// passagem reduz a vazão, e o bip, quando vier, é só para negação ou erro (docs/34 §8).
/// </remarks>
public enum TipoDeBip
{
    /// <summary><c>AcionarBipCurto</c> (EI-048).</summary>
    Curto,

    /// <summary><c>AcionarBipLongo</c> (EI-049).</summary>
    Longo,
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

    /// <summary>
    /// A função recusou um parâmetro, com o retorno documentado para ela na matriz
    /// (128/129/130 nas funções de montagem; 9 em <c>DefinirTipoConexao</c>). Ver
    /// <see cref="RetornosDocumentados"/>.
    /// </summary>
    ConfiguracaoRecusada,

    /// <summary>
    /// Retorno 3 de <c>AbrirPortaComunicacao</c>: a porta já estava aberta — em geral um
    /// processo anterior que não a fechou.
    /// </summary>
    PortaJaAberta,
}

/// <summary>Resultado de uma chamada ao adapter, com o retorno bruto preservado.</summary>
/// <param name="Status">Interpretação do retorno.</param>
/// <param name="NativeReturn">Valor exato devolvido pela DLL.</param>
/// <param name="Elapsed">Duração da chamada.</param>
/// <param name="Funcao">
/// A função nativa que devolveu <paramref name="NativeReturn"/>, quando conhecida. O mesmo
/// número significa coisas diferentes em funções diferentes (ADR-0018; docs/34 §2, F6).
/// </param>
public readonly record struct AdapterResult(AdapterStatus Status, int NativeReturn, TimeSpan Elapsed, string? Funcao = null)
{
    public bool IsOk => Status is AdapterStatus.Ok;

    /// <summary>
    /// O significado documentado deste retorno para <see cref="Funcao"/>, ou <c>null</c>
    /// quando a matriz não diz nada específico.
    /// </summary>
    public string? Significado => RetornosDocumentados.Consultar(Funcao, NativeReturn)?.Significado;

    /// <summary>Mapeia um retorno nativo, sem nunca descartar o valor bruto.</summary>
    /// <param name="nativo">O retorno da DLL.</param>
    /// <param name="duracao">Duração da chamada.</param>
    /// <param name="funcao">
    /// A função que devolveu o retorno. Com ela, os retornos específicos da matriz FUN
    /// (<see cref="RetornosDocumentados"/>) deixam de cair em
    /// <see cref="AdapterStatus.RetornoDesconhecido"/>; sem ela, vale só o mapeamento geral.
    /// </param>
    public static AdapterResult FromNative(int nativo, TimeSpan duracao, string? funcao = null)
    {
        if (RetornosDocumentados.Consultar(funcao, nativo) is { } documentado)
        {
            return new AdapterResult(documentado.Status, nativo, duracao, funcao);
        }

        var status = nativo switch
        {
            0 => AdapterStatus.Ok,
            1 => AdapterStatus.Erro,
            8 => AdapterStatus.FalhaDeDependencia,
            _ => AdapterStatus.RetornoDesconhecido,
        };

        return new AdapterResult(status, nativo, duracao, funcao);
    }

    public override string ToString()
    {
        var onde = Funcao is null ? string.Empty : $"{Funcao}: ";
        var porque = Significado is { } s ? $" — {s}" : string.Empty;
        return $"{Status}({onde}retorno={NativeReturn}{porque}, {Elapsed.TotalMilliseconds:F0}ms)";
    }
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
    /// Acerta o relógio do equipamento (<c>EnviarRelogio</c>, manual 4.6.1) no horário de
    /// Brasília.
    /// </summary>
    /// <remarks>
    /// O equipamento guarda hora sem fuso e ano com dois dígitos: só 2000–2099. O fluxo
    /// oficial acerta o relógio na passagem para on-line; acertar com a catraca em uso é
    /// <c>A_CONFIRMAR_COM_TOPDATA</c> (bancada, docs/21).
    /// </remarks>
    AdapterResult AcertarRelogio(int inner, DateTimeOffset instante);

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
    /// <para>
    /// Com a configuração e a chave <c>catraca.enviar_formas_de_entrada</c> ligada nela, os
    /// parâmetros vêm de <see cref="DeviceConfiguration.FormasDeEntradaOnLine"/>; sem uma ou
    /// outra, são os de sempre (Etapa A.2 do docs/35; T26).
    /// </para>
    /// </remarks>
    AdapterResult ConfigurarEntradasOnline(int inner, DeviceConfiguration? configuracao = null);

    /// <summary>Envia a mensagem exibida no display quando ocioso.</summary>
    AdapterResult EnviarMensagemPadrao(int inner, string mensagem);

    /// <summary>
    /// Aguarda um evento. <b>Bloqueia</b> até haver evento, timeout ou erro.
    /// </summary>
    (AdapterResult Resultado, DeviceEvent? Evento) AguardarEvento(int inner, TimeSpan limite);

    /// <summary>
    /// Libera o giro chamando exatamente a função pedida. Quem escolhe a função é o perfil
    /// do portão (<see cref="GatePhysicalProfile.LiberacaoDaEntrada"/>), e mais ninguém.
    /// </summary>
    AdapterResult LiberarGiro(int inner, GateDirection direcao);

    /// <summary>
    /// Aciona o bip pedido, chamando exatamente a função dele (EI-048 ou EI-049). Só o Inner
    /// vai para a DLL: é a assinatura do SDK, e a matriz não documenta outro parâmetro.
    /// </summary>
    /// <remarks>
    /// Se a Linha 4 emite os dois bips e como soam é <c>A_CONFIRMAR_COM_TOPDATA</c> (INT-UX-03,
    /// docs/21 §6D); por isso só sai por comando do operador com a chave ligada.
    /// </remarks>
    AdapterResult AcionarBip(int inner, TipoDeBip bip);

    /// <summary>Aciona o relé 2, que abre a fenda da urna.</summary>
    /// <remarks>
    /// <b>Sem parâmetro de tempo.</b> O SDK expõe <c>AcionarRele2(int Inner)</c> e mais nada:
    /// a duração é a que foi gravada em <c>ConfigurarAcionamento2</c>, junto com a
    /// configuração do equipamento. O manual sugeria uma assinatura com tempo, e estava
    /// errado. Mudar a duração significa reenviar a configuração.
    /// </remarks>
    AdapterResult AcionarReleDaUrna(int inner);

    /// <summary>
    /// Coleta um bilhete, que é <b>removido da memória do equipamento</b>.
    /// </summary>
    (AdapterResult Resultado, Bilhete? Bilhete) ColetarBilhete(int inner);

    /// <summary>Exibe uma mensagem temporária no display.</summary>
    AdapterResult ExibirMensagemTemporaria(int inner, string mensagem, TimeSpan duracao);
}
