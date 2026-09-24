using System.Text;
using Topdata.EasyInner.Interop;

namespace Topdata.EasyInner.Adapter;

/// <summary>
/// Implementação de <see cref="IEasyInnerNative"/> que <b>recusa-se a adivinhar</b>.
/// </summary>
/// <remarks>
/// <para>
/// As declarações <c>DllImport</c> reais não foram escritas de propósito. O Manual de
/// Integração mostra o uso do <i>wrapper</i> (<c>EasyInner.AbrirPortaComunicacao(3570)</c>),
/// não os símbolos exportados pela DLL. Escrever os nomes de entry point e a convenção
/// de chamada por dedução seria inventar — exatamente o que este projeto proíbe, e o
/// tipo de erro que aparece como retorno 8 ou corrupção de memória em campo.
/// </para>
/// <para>
/// O que fecha esta lacuna é o arquivo <c>EasyInner.cs</c> do pacote de exemplos em C#,
/// que a Topdata distribui pelo portal do integrador. Ver
/// <c>vendor/topdata/README.md</c> e <c>docs/11-capacidades-do-sdk.md</c>, seção 5.
/// </para>
/// <para>
/// Enquanto isso, o worker roda com o simulador, e toda a lógica — laço, watchdog,
/// backoff, disjuntor, máquina de estados, persistência — já é exercitada e testada.
/// </para>
/// </remarks>
public sealed class VinculacaoNativaPendente : IEasyInnerNative
{
    private const string Explicacao =
        "As assinaturas P/Invoke da EasyInner.dll ainda não foram obtidas. " +
        "Elas vêm do arquivo EasyInner.cs do pacote de exemplos em C# do portal do integrador " +
        "(ver vendor/topdata/README.md). Deduzi-las seria inventar, e o modo de falha é " +
        "corrupção de memória, não uma exceção clara. " +
        "Antes de escrevê-las, execute o ensaio HIL-STACK-01: um processo .NET moderno " +
        "compilado win-x86 consegue carregar a DLL? Ver docs/12-decisao-de-stack.md.";

    private static NotSupportedException NaoDisponivel(string funcao) =>
        new($"{funcao}: {Explicacao}");

    public int DefinirTipoConexao(int tipo) => throw NaoDisponivel(nameof(DefinirTipoConexao));

    public int AbrirPortaComunicacao(int porta) => throw NaoDisponivel(nameof(AbrirPortaComunicacao));

    public int FecharPortaComunicacao() => throw NaoDisponivel(nameof(FecharPortaComunicacao));

    public int TestarConexaoInner(int inner) => throw NaoDisponivel(nameof(TestarConexaoInner));

    public int PingOnline(int inner) => throw NaoDisponivel(nameof(PingOnline));

    public int ReceberVersaoFirmware(
        int inner,
        ref byte linha,
        ref short variacao,
        ref byte versaoAlta,
        ref byte versaoBaixa,
        ref byte versaoSufixo,
        ref byte innerAcessoBio) => throw NaoDisponivel(nameof(ReceberVersaoFirmware));

    public int ReceberRelogio(
        int inner,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo) => throw NaoDisponivel(nameof(ReceberRelogio));

    public int EnviarRelogio(int inner, byte dia, byte mes, byte ano, byte hora, byte minuto, byte segundo) =>
        throw NaoDisponivel(nameof(EnviarRelogio));

    public int DefinirPadraoCartao(byte tipo) => throw NaoDisponivel(nameof(DefinirPadraoCartao));

    public int DefinirQuantidadeDigitosCartao(byte quantidade) =>
        throw NaoDisponivel(nameof(DefinirQuantidadeDigitosCartao));

    public int InserirQuantidadeDigitoVariavel(byte quantidade) =>
        throw NaoDisponivel(nameof(InserirQuantidadeDigitoVariavel));

    public int ConfigurarTipoLeitor(byte tipo) => throw NaoDisponivel(nameof(ConfigurarTipoLeitor));

    public int ConfigurarLeitor1(byte operacao) => throw NaoDisponivel(nameof(ConfigurarLeitor1));

    public int ConfigurarLeitor2(byte operacao) => throw NaoDisponivel(nameof(ConfigurarLeitor2));

    public int ConfigurarAcionamento1(byte funcao, byte tempo) => throw NaoDisponivel(nameof(ConfigurarAcionamento1));

    public int ConfigurarAcionamento2(byte funcao, byte tempo) => throw NaoDisponivel(nameof(ConfigurarAcionamento2));

    public int ConfigurarInnerOnLine() => throw NaoDisponivel(nameof(ConfigurarInnerOnLine));

    public int ConfigurarInnerOffLine() => throw NaoDisponivel(nameof(ConfigurarInnerOffLine));

    public int HabilitarTeclado(byte habilita, byte ecoar) => throw NaoDisponivel(nameof(HabilitarTeclado));

    public int HabilitarMudancaOnLineOffLine(byte habilita, byte tempo) =>
        throw NaoDisponivel(nameof(HabilitarMudancaOnLineOffLine));

    public int EnviarConfiguracoesMudancaAutomaticaOnLineOffLine(int inner) =>
        throw NaoDisponivel(nameof(EnviarConfiguracoesMudancaAutomaticaOnLineOffLine));

    public int EnviarConfiguracoes(int inner) => throw NaoDisponivel(nameof(EnviarConfiguracoes));

    public int DefinirTipoListaAcesso(byte tipo) => throw NaoDisponivel(nameof(DefinirTipoListaAcesso));

    public int InserirUsuarioListaAcesso(string cartao, byte horario) =>
        throw NaoDisponivel(nameof(InserirUsuarioListaAcesso));

    public int EnviarListaAcesso(int inner) => throw NaoDisponivel(nameof(EnviarListaAcesso));

    public int ApagarListaAcesso(int inner) => throw NaoDisponivel(nameof(ApagarListaAcesso));

    public int ColetarBilhete(
        int inner,
        ref byte tipo,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        StringBuilder cartao) => throw NaoDisponivel(nameof(ColetarBilhete));

    public int ReceberDadosOnLine(
        int inner,
        ref byte origem,
        ref byte complemento,
        StringBuilder cartao,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo) => throw NaoDisponivel(nameof(ReceberDadosOnLine));

    public int LiberarCatracaEntrada(int inner) => throw NaoDisponivel(nameof(LiberarCatracaEntrada));

    public int LiberarCatracaSaida(int inner) => throw NaoDisponivel(nameof(LiberarCatracaSaida));

    public int LiberarCatracaEntradaInvertida(int inner) => throw NaoDisponivel(nameof(LiberarCatracaEntradaInvertida));

    public int LiberarCatracaSaidaInvertida(int inner) => throw NaoDisponivel(nameof(LiberarCatracaSaidaInvertida));

    public int LiberarCatracaDoisSentidos(int inner) => throw NaoDisponivel(nameof(LiberarCatracaDoisSentidos));

    public int AcionarRele2(int inner, byte tempo) => throw NaoDisponivel(nameof(AcionarRele2));

    public int AcionarBipCurto(int inner) => throw NaoDisponivel(nameof(AcionarBipCurto));

    public int AcionarBipLongo(int inner) => throw NaoDisponivel(nameof(AcionarBipLongo));

    public int LigarBackLite(int inner) => throw NaoDisponivel(nameof(LigarBackLite));

    public int DesligarBackLite(int inner) => throw NaoDisponivel(nameof(DesligarBackLite));

    public int EnviarMensagemPadraoOnLine(int inner, byte exibirData, string mensagem) =>
        throw NaoDisponivel(nameof(EnviarMensagemPadraoOnLine));

    public int EnviarMensagemTemporariaOnLine(int inner, byte exibirData, string mensagem, byte tempo) =>
        throw NaoDisponivel(nameof(EnviarMensagemTemporariaOnLine));
}
