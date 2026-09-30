namespace Access.Application.Devices;

/// <summary>
/// Como a catraca conta os dígitos do cartão: um tamanho fixo ou um conjunto de tamanhos.
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.2 do docs/35 (docs/34 §4.1, grupo Cartão; anexo 01 §3.1). Antes havia só o par
/// <c>QuantidadeFixaDeDigitos</c> (anulável) e <c>QuantidadesVariaveisDeDigitos</c>, e nada
/// impedia "os dois" ou "nenhum dos dois". Declarar o modo liga as regras do docs/34 §4.2
/// (regras 1 e 2) em <see cref="DeviceConfiguration.Validar"/>.
/// </para>
/// <para>
/// Não é parâmetro da DLL: escolhe qual das funções de dígitos vale —
/// <c>DefinirQuantidadeDigitosCartao</c> (EI-011) ou <c>InserirQuantidadeDigitoVariavel</c>
/// (EI-012, só com a chave <c>catraca.enviar_digitos_variaveis</c>).
/// </para>
/// </remarks>
public enum ModoDeDigitos
{
    /// <summary>Conjunto de tamanhos aceitos, uma chamada de EI-012 por tamanho (FUN:13).</summary>
    Variavel,

    /// <summary>Um tamanho só, de 4 a 16, por EI-011 (FUN:12; 1–16 é <c>A_CONFIRMAR</c>, T6).</summary>
    Fixo,
}

/// <summary>
/// <c>ConfigurarWiegandDoisLeitores(Habilita, ExibirMensagem)</c> — EI-024, FUN:25, faixas 0–1.
/// </summary>
/// <param name="Habilitado">Habilita o segundo leitor Wiegand (1) ou não (0).</param>
/// <param name="ExibirMensagem">O segundo parâmetro da função, 0 ou 1.</param>
/// <remarks>
/// O padrão (0, 0) é a proposta do anexo 01 §3.1. Hoje a catraca recebe o padrão da DLL,
/// que ninguém conhece (ADR-0020); só é enviado com a chave técnica
/// <c>catraca.enviar_wiegand_dois_leitores</c>, desligada até HIL-CARD-05.
/// </remarks>
public readonly record struct WiegandDoisLeitores(bool Habilitado, bool ExibirMensagem);

/// <summary>
/// Os cinco parâmetros de <c>EnviarFormasEntradasOnLine</c> depois do Inner — EI-032, FUN:33.
/// </summary>
/// <param name="QtdeDigitosTeclado">Quantidade de dígitos do teclado.</param>
/// <param name="EcoTeclado">Eco do teclado.</param>
/// <param name="FormaEntrada">Forma de entrada: 0–7, 10–14 ou 100–105 (FUN:33).</param>
/// <param name="TempoTeclado">Tempo do teclado.</param>
/// <param name="PosicaoCursorTeclado">Posição do cursor.</param>
/// <remarks>
/// <para>
/// É a função que rearma o leitor a cada ciclo. Até a Etapa A.2 os valores eram constantes
/// do adapter; <see cref="DeHoje"/> guarda exatamente esses valores.
/// </para>
/// <para>
/// A matriz só documenta a faixa de <c>FormaEntrada</c>; o significado de cada valor e as
/// faixas dos outros quatro são <c>A_CONFIRMAR_COM_TOPDATA</c> (T26, ensaio INT-SM-032).
/// Por isso o adapter só usa estes valores com a chave técnica
/// <c>catraca.enviar_formas_de_entrada</c>; desligada, manda as constantes de sempre.
/// </para>
/// </remarks>
public sealed record FormasDeEntradaOnLine(
    byte QtdeDigitosTeclado,
    byte EcoTeclado,
    byte FormaEntrada,
    byte TempoTeclado,
    byte PosicaoCursorTeclado)
{
    /// <summary>
    /// Os valores que o adapter sempre mandou: (0, 0, 7, 0, 0). O 7 é o que o comentário do
    /// adapter chama de "teclado e os dois leitores" (enum FormaEntrada do SDK); o significado
    /// de cada valor continua <c>A_CONFIRMAR</c> (T26).
    /// </summary>
    public static FormasDeEntradaOnLine DeHoje { get; } = new(0, 0, 7, 0, 0);

    /// <summary>Faixa de <see cref="FormaEntrada"/> documentada na matriz (FUN:33).</summary>
    public static bool FormaEntradaDocumentada(byte forma) =>
        forma is <= 7 or (>= 10 and <= 14) or (>= 100 and <= 105);
}

/// <summary>
/// Uma mensagem do display (2 linhas de 16, 32 caracteres) com a opção de exibir a data.
/// </summary>
/// <param name="Texto">O texto. Até 32 caracteres, ou 16 com a data (docs/34 §4.2, regra 6).</param>
/// <param name="ExibirData">O parâmetro <c>ExibirData</c> das funções de mensagem.</param>
/// <remarks>
/// Os limites vêm da mensagem padrão (EI-056, FUN:57). Para as mensagens de apresentação e
/// off-line a matriz não tem linha, e o limite de cada uma é <c>INFERIDO</c> por analogia
/// (anexo 01 §1.9) — por isso elas estão no modelo e <b>não são enviadas</b> (docs/34 §4.1).
/// </remarks>
public sealed record MensagemDoDisplay(string Texto, bool ExibirData = false)
{
    /// <summary>Caracteres do display sem a data (FUN:57).</summary>
    public const int LimiteSemData = 32;

    /// <summary>Caracteres que sobram com a data (FUN:57).</summary>
    public const int LimiteComData = 16;
}

/// <summary>
/// O número do cartão master (<c>DefinirNumeroCartaoMaster</c>, EI-023): texto, nunca número,
/// e nunca impresso.
/// </summary>
/// <remarks>
/// <para>
/// O cartão master passa por cima da lista de acesso (FUN:24; docs/14): é credencial e tem o
/// tratamento de segredo das regras do docs/35. É texto porque é código de cartão (zeros à
/// esquerda contam). <see cref="ToString"/> não mostra o número, para que nenhum registro da
/// <see cref="DeviceConfiguration"/> (que é um <c>record</c> e imprime os campos) o exponha.
/// </para>
/// <para>
/// Na Etapa A.2 não há de onde vir o número: gerar um valor aleatório, cifrar com DPAPI
/// (como o <c>CofreDpapi</c> do serviço) e entregá-lo ao worker é PROPOSTA FUTURA
/// (SEC-MASTER-01; como "não ter" master é T13). Enquanto isso o campo fica nulo e nada é
/// enviado. Não existe chave em <c>edge_setting</c> para ele porque o número não pode morar
/// lá em claro.
/// </para>
/// </remarks>
public sealed class CodigoDoCartaoMaster : IEquatable<CodigoDoCartaoMaster>
{
    /// <summary>"até 14 dígitos" (FUN:24).</summary>
    public const int MaximoDeDigitos = 14;

    private readonly string _codigo;

    /// <param name="codigo">O número, como texto. Validado por <see cref="DeviceConfiguration.Validar"/>.</param>
    public CodigoDoCartaoMaster(string codigo)
    {
        ArgumentNullException.ThrowIfNull(codigo);
        _codigo = codigo;
    }

    /// <summary>Só dígitos, de 1 a 14 (FUN:24).</summary>
    public bool Valido => _codigo.Length is >= 1 and <= MaximoDeDigitos && _codigo.All(char.IsAsciiDigit);

    /// <summary>O número em claro, para a chamada nativa e só para ela.</summary>
    /// <remarks>O nome é longo de propósito: quem chama precisa ver que está abrindo o segredo.</remarks>
    public string RevelarParaADll() => _codigo;

    /// <summary>Nunca o número: só que ele existe.</summary>
    public override string ToString() => "(cartão master definido)";

    public bool Equals(CodigoDoCartaoMaster? other) =>
        other is not null && string.Equals(_codigo, other._codigo, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CodigoDoCartaoMaster);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_codigo);
}
