using System.Runtime.InteropServices;
using System.Text;

namespace Topdata.EasyInner.Interop;

/// <summary>
/// Declarações da <c>EasyInner.dll</c>, escritas por nós.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escritas por nós, não copiadas.</b> O exemplo oficial da Topdata traz as mesmas
/// funções, mas o cabeçalho dele diz que não deve ser incluído em aplicações comerciais.
/// O que se usa daqui são fatos sobre a interface — nome do símbolo, tipo de cada
/// parâmetro e convenção de chamada —, que é exatamente para o que o SDK existe.
/// </para>
/// <para>
/// <b>Diferença deliberada em relação ao exemplo:</b> ele aloca o buffer do cartão com
/// <c>new StringBuilder()</c>, sem capacidade. O padrão é 16 caracteres, o que dá um buffer
/// nativo de 17 — exatamente o necessário para um cartão de 16 dígitos mais o terminador,
/// com margem zero. Aqui a capacidade é explícita e folgada
/// (<see cref="TamanhoDoBufferDeCartao"/>).
/// </para>
/// <para>
/// A DLL é Win32 de <b>32 bits</b>, com 774 exports por nome, e
/// <c>CallingConvention.Winapi</c> — em Windows, <c>StdCall</c>. Todo retorno é
/// <see langword="byte"/>, e não <c>int</c>. Nada aqui funciona fora de um processo
/// Windows x86. Ver docs/ADR/ADR-0001.
/// </para>
/// <para>
/// Os identificadores <c>EI-xxx</c> apontam para
/// <c>docs/compatibility-matrix/funcoes-easyinner.csv</c>.
/// </para>
/// </remarks>
internal static partial class EasyInnerNative
{
    private const string Dll = "EasyInner.dll";

    /// <summary>Capacidade do buffer que recebe o número do cartão.</summary>
    /// <remarks>
    /// O cartão vai a 16 dígitos, mas existem variantes que devolvem letras
    /// (<c>ReceberDadosOnLine_ComLetras</c> e <c>_QRCodeComLetras</c>), cujo comprimento
    /// máximo <b>não está documentado</b>. Estourar este buffer é corrupção de memória, não
    /// exceção — por isso a folga é deliberada e não "otimizada" depois.
    /// </remarks>
    internal const int TamanhoDoBufferDeCartao = 64;

    /// <summary>Cria um buffer com a capacidade certa para receber um cartão.</summary>
    internal static byte[] NovoBufferDeCartao() => new byte[TamanhoDoBufferDeCartao];

    /// <summary>
    /// Lê o cartão devolvido pela DLL, parando no terminador nulo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O buffer é <c>byte[]</c> e não <c>StringBuilder</c> por duas razões. A primeira é que
    /// o analisador recusa <c>StringBuilder</c> em P/Invoke, e com razão: ele aloca e copia
    /// duas vezes, e a capacidade efetiva do buffer nativo fica implícita. A segunda é que
    /// aqui o tamanho é explícito, o que importa quando estourá-lo significa corromper a
    /// memória do processo.
    /// </para>
    /// <para>
    /// Se a DLL devolver o buffer cheio sem terminador, a leitura para no fim do buffer em
    /// vez de sair procurando — ler além é o defeito que se está tentando evitar.
    /// </para>
    /// </remarks>
    internal static string LerCartao(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var fim = Array.IndexOf(buffer, (byte)0);
        var tamanho = fim < 0 ? buffer.Length : fim;

        return Encoding.ASCII.GetString(buffer, 0, tamanho).Trim();
    }

    // ---------------------------------------------------------------------------------
    // Comunicação
    // ---------------------------------------------------------------------------------

    /// <summary>EI-001 — tipo de conexão. 2 = TCP com porta fixa, que é o nosso caso.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte DefinirTipoConexao(byte Tipo);

    /// <summary>EI-002 — abre a porta TCP em que o software escuta. Uma vez por worker.</summary>
    /// <remarks>Retorno 8 é GPF: DLL não registrada, .NET 3.5 ausente ou arquitetura errada.</remarks>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte AbrirPortaComunicacao(int Porta);

    /// <summary>EI-003 — fecha a porta. Devolve <c>void</c>, não byte.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern void FecharPortaComunicacao();

    /// <summary>EI-004 — testa a presença do equipamento.</summary>
    /// <remarks>
    /// O manual cita <c>TestarConexaoInner</c>, que <b>não existe</b> entre os exports.
    /// Quem testa presença é <c>Ping</c>.
    /// </remarks>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte Ping(int Inner);

    /// <summary>EI-005 — mantém o equipamento em modo on-line. Sem isto ele cai para off-line.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte PingOnLine(int Inner);

    // ---------------------------------------------------------------------------------
    // Diagnóstico
    // ---------------------------------------------------------------------------------

    /// <summary>EI-006 — versão de firmware. <c>Variacao</c> é <c>short</c>, os demais byte.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ReceberVersaoFirmware(
        int Inner,
        ref byte Linha,
        ref short Variacao,
        ref byte VersaoAlta,
        ref byte VersaoBaixa,
        ref byte VersaoSufixo,
        ref byte InnerAcessoBio);

    /// <summary>EI-007 — relógio do equipamento. Ano com dois dígitos.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ReceberRelogio(
        int Inner,
        ref byte Dia,
        ref byte Mes,
        ref byte Ano,
        ref byte Hora,
        ref byte Minuto,
        ref byte Segundo);

    /// <summary>EI-008 — ajusta o relógio. Afeta a ordem dos eventos entre pistas.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte EnviarRelogio(
        int Inner,
        byte Dia,
        byte Mes,
        byte Ano,
        byte Hora,
        byte Minuto,
        byte Segundo);

    // ---------------------------------------------------------------------------------
    // Configuração — montada no buffer da DLL e aplicada por EnviarConfiguracoes
    // ---------------------------------------------------------------------------------

    /// <summary>EI-010 — 0 Topdata, 1 Livre.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte DefinirPadraoCartao(byte Padrao);

    /// <summary>EI-011 — quantidade fixa de dígitos.</summary>
    /// <remarks>O manual diverge sobre o mínimo: 4 na seção 4.1.2, 1 na tabela 4.1.9.</remarks>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte DefinirQuantidadeDigitosCartao(byte Quantidade);

    /// <summary>EI-013 — tecnologia do leitor, 0 a 8. O 8 é QR Code por letras.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarTipoLeitor(byte Tipo);

    /// <summary>EI-014 — sentido lógico do leitor 1, 0 a 4.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarLeitor1(byte Operacao);

    /// <summary>EI-015 — sentido lógico do leitor 2, o da fenda da urna.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarLeitor2(byte Operacao);

    /// <summary>EI-016 — função e tempo do relé 1. Função 0 a 9, tempo 0 a 50 s.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarAcionamento1(byte Funcao, byte Tempo);

    /// <summary>EI-017 — função e tempo do relé 2, o da urna.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarAcionamento2(byte Funcao, byte Tempo);

    /// <summary>EI-018 — prepara o modo on-line.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarInnerOnLine();

    /// <summary>EI-019 — prepara o modo off-line.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ConfigurarInnerOffLine();

    /// <summary>EI-020 — teclado e eco dos dígitos.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte HabilitarTeclado(byte Habilita, byte Ecoar);

    /// <summary>EI-028 — mudança automática on-line/off-line. 2 pressupõe PingOnLine.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte HabilitarMudancaOnLineOffLine(byte Habilita, byte Tempo);

    /// <summary>EI-029 — aplica a configuração de mudança automática.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte EnviarConfiguracoesMudancaAutomaticaOnLineOffLine(int Inner);

    /// <summary>
    /// EI-030 — aplica tudo que foi montado. <b>Envia também os padrões da DLL</b> para o
    /// que não foi definido, sobrescrevendo em silêncio o que havia no equipamento.
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte EnviarConfiguracoes(int Inner);

    /// <summary>
    /// EI-032 — formas de entrada aceitas. É <b>esta</b> que rearma o leitor a cada ciclo.
    /// </summary>
    /// <remarks>
    /// O manual cita <c>LiberarLeitor</c> numa tabela de solução de problemas; esse nome não
    /// existe entre os exports. Sem o rearme, a pista para de ler sem dar erro.
    /// </remarks>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte EnviarFormasEntradasOnLine(
        int Inner,
        byte QtdeDigitosTeclado,
        byte EcoTeclado,
        byte FormaEntrada,
        byte TempoTeclado,
        byte PosicaoCursorTeclado);

    // ---------------------------------------------------------------------------------
    // Tempo real
    // ---------------------------------------------------------------------------------

    /// <summary>EI-040 — evento on-line. Bloqueante. Tem segundos, ao contrário do bilhete.</summary>
    /// <remarks>
    /// O buffer de <paramref name="Cartao"/> precisa vir de
    /// <see cref="NovoBufferDeCartao"/>, e o conteúdo é lido por <see cref="LerCartao"/>.
    /// </remarks>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ReceberDadosOnLine(
        int Inner,
        ref byte Origem,
        ref byte Complemento,
        byte[] Cartao,
        ref byte Dia,
        ref byte Mes,
        ref byte Ano,
        ref byte Hora,
        ref byte Minuto,
        ref byte Segundo);

    /// <summary>EI-041 — libera o giro no sentido entrada.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LiberarCatracaEntrada(int Inner);

    /// <summary>EI-042 — libera o giro no sentido saída.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LiberarCatracaSaida(int Inner);

    /// <summary>EI-043 — entrada, com o sentido físico invertido.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LiberarCatracaEntradaInvertida(int Inner);

    /// <summary>EI-044 — saída, com o sentido físico invertido.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LiberarCatracaSaidaInvertida(int Inner);

    /// <summary>EI-045 — libera nos dois sentidos. Proibido em operação normal: carona.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LiberarCatracaDoisSentidos(int Inner);

    /// <summary>
    /// EI-047 — aciona o relé 2, o da urna. <b>Sem parâmetro de tempo</b>: a duração vem de
    /// <see cref="ConfigurarAcionamento2"/>.
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte AcionarRele2(int Inner);

    /// <summary>EI-039 — coleta um bilhete off-line. <b>Remove o bilhete da memória.</b></summary>
    /// <remarks>Não tem campo de segundos, ao contrário do evento on-line.</remarks>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte ColetarBilhete(
        int Inner,
        ref byte Tipo,
        ref byte Dia,
        ref byte Mes,
        ref byte Ano,
        ref byte Hora,
        ref byte Minuto,
        byte[] Cartao);

    /// <summary>EI-056 — mensagem fixa do display ocioso. 32 caracteres, 16 com data.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte EnviarMensagemPadraoOnLine(int Inner, byte ExibirData, string Mensagem);

    /// <summary>EI-057 — mensagem temporária, com tempo em segundos.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte EnviarMensagemTemporariaOnLine(
        int Inner,
        byte ExibirData,
        string Mensagem,
        byte Tempo);

    // ---------------------------------------------------------------------------------
    // Sinalização — independentes da decisão de acesso
    // ---------------------------------------------------------------------------------

    /// <summary>EI-048.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte AcionarBipCurto(int Inner);

    /// <summary>EI-049.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte AcionarBipLongo(int Inner);

    /// <summary>EI-050.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LigarLedVerde(int Inner);

    /// <summary>EI-051.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte DesligarLedVerde(int Inner);

    /// <summary>EI-052.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte LigarLedVermelho(int Inner);

    /// <summary>EI-053.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Winapi, ExactSpelling = true)]
    internal static extern byte DesligarLedVermelho(int Inner);
}
