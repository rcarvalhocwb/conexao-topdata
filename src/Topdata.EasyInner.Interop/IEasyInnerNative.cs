using System.Text;

namespace Topdata.EasyInner.Interop;

/// <summary>
/// Costura sobre a EasyInner.dll. Existe para que o worker seja testável sem a DLL e
/// para que a forma de interoperabilidade possa mudar sem afetar quem chama.
/// </summary>
/// <remarks>
/// <para>
/// Todos os métodos são <b>bloqueantes</b> e a DLL <b>não é thread-safe</b>: uma única
/// thread por processo pode chamá-los, e nunca a da interface gráfica.
/// Fonte: Manual de Integração SDK Inner Acesso, seções 2.1 e 6.2.
/// </para>
/// <para>
/// Os identificadores <c>EI-xxx</c> nos comentários apontam para as linhas de
/// <c>docs/compatibility-matrix/funcoes-easyinner.csv</c>.
/// </para>
/// </remarks>
public interface IEasyInnerNative
{
    /// <summary>EI-001 — define o tipo de conexão.</summary>
    /// <remarks>Assinatura ainda não confirmada (o manual cita sem detalhar).</remarks>
    int DefinirTipoConexao(int tipo);

    /// <summary>
    /// EI-002 — abre a porta TCP em que o software escuta. Padrão 3570.
    /// </summary>
    /// <remarks>
    /// Chamada <b>uma única vez</b> por worker, antes do laço da máquina de estados.
    /// Cada worker escuta em porta própria (ADR-0021).
    /// Retorno 8 significa GPF: DLL não registrada, .NET Framework 3.5 ausente,
    /// arquitetura errada ou DLLs incompatíveis.
    /// </remarks>
    int AbrirPortaComunicacao(int porta);

    /// <summary>EI-003 — fecha a porta. Obrigatório ao encerrar.</summary>
    int FecharPortaComunicacao();

    /// <summary>EI-004 — testa a conexão com um equipamento (1 a 99).</summary>
    int TestarConexaoInner(int inner);

    /// <summary>
    /// EI-005 — mantém o equipamento em modo on-line.
    /// Obrigatório quando a mudança automática está no modo 2.
    /// </summary>
    int PingOnline(int inner);

    /// <summary>EI-006 — lê versão de firmware e linha do produto.</summary>
    int ReceberVersaoFirmware(
        int inner,
        ref byte linha,
        ref short variacao,
        ref byte versaoAlta,
        ref byte versaoBaixa,
        ref byte versaoSufixo,
        ref byte innerAcessoBio);

    /// <summary>EI-007 — lê o relógio do equipamento.</summary>
    int ReceberRelogio(
        int inner,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo);

    /// <summary>EI-008 — ajusta o relógio do equipamento.</summary>
    int EnviarRelogio(int inner, byte dia, byte mes, byte ano, byte hora, byte minuto, byte segundo);

    /// <summary>EI-010 — padrão do cartão: 0 Topdata, 1 Livre.</summary>
    int DefinirPadraoCartao(byte tipo);

    /// <summary>EI-011 — quantidade fixa de dígitos.</summary>
    /// <remarks>
    /// O manual diverge sobre a faixa: 4–16 na seção 4.1.2, 1–16 na tabela 4.1.9.
    /// Pendente com a Topdata.
    /// </remarks>
    int DefinirQuantidadeDigitosCartao(byte quantidade);

    /// <summary>EI-012 — adiciona um comprimento aceito (dígitos variáveis).</summary>
    int InserirQuantidadeDigitoVariavel(byte quantidade);

    /// <summary>EI-013 — tecnologia do leitor físico (0 a 7).</summary>
    int ConfigurarTipoLeitor(byte tipo);

    /// <summary>EI-014 — operação do leitor 1 (0 a 4).</summary>
    int ConfigurarLeitor1(byte operacao);

    /// <summary>EI-015 — operação do leitor 2. É o leitor da fenda da urna.</summary>
    int ConfigurarLeitor2(byte operacao);

    /// <summary>EI-016 — função e tempo do relé 1 (tempo 0 a 50 s).</summary>
    /// <remarks>Não usar para acionar giro: para isso existem as funções LiberarCatraca.</remarks>
    int ConfigurarAcionamento1(byte funcao, byte tempo);

    /// <summary>EI-017 — função e tempo do relé 2. É o relé da urna.</summary>
    int ConfigurarAcionamento2(byte funcao, byte tempo);

    /// <summary>EI-018 — prepara o modo on-line no buffer.</summary>
    int ConfigurarInnerOnLine();

    /// <summary>EI-019 — prepara o modo off-line no buffer.</summary>
    int ConfigurarInnerOffLine();

    /// <summary>EI-020 — habilita o teclado e o eco no display.</summary>
    int HabilitarTeclado(byte habilita, byte ecoar);

    /// <summary>EI-028 — mudança automática on-line/off-line (0, 1 ou 2) e tempo 1 a 50.</summary>
    int HabilitarMudancaOnLineOffLine(byte habilita, byte tempo);

    /// <summary>EI-029 — envia a configuração de mudança automática.</summary>
    int EnviarConfiguracoesMudancaAutomaticaOnLineOffLine(int inner);

    /// <summary>
    /// EI-030 — aplica a configuração montada no buffer e <b>limpa o buffer</b>.
    /// </summary>
    /// <remarks>
    /// <b>Envia também os valores padrão da DLL</b> para tudo que não foi setado
    /// explicitamente, sobrescrevendo em silêncio o que havia no equipamento. Por isso a
    /// configuração enviada é sempre completa — ver docs/ADR/ADR-0020.
    /// </remarks>
    int EnviarConfiguracoes(int inner);

    /// <summary>EI-033 — tipo da lista de acesso: 0 não usar, 1 branca, 2 negra.</summary>
    int DefinirTipoListaAcesso(byte tipo);

    /// <summary>
    /// EI-034 — adiciona usuário ao buffer da lista.
    /// Horário 1–100 = tabela; 101 = sempre liberado; 102 = sempre negado.
    /// </summary>
    int InserirUsuarioListaAcesso(string cartao, byte horario);

    /// <summary>EI-035 — envia a lista, <b>sobrescrevendo</b> a existente.</summary>
    int EnviarListaAcesso(int inner);

    /// <summary>EI-036 — limpa o buffer da lista.</summary>
    int ApagarListaAcesso(int inner);

    /// <summary>
    /// EI-039 — coleta um bilhete e o <b>remove da memória do equipamento</b>.
    /// </summary>
    /// <remarks>
    /// Como o bilhete é removido na chamada, a gravação local precisa estar commitada
    /// antes de pedir o próximo. Ver risco R-68.
    /// </remarks>
    int ColetarBilhete(
        int inner,
        ref byte tipo,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        StringBuilder cartao);

    /// <summary>
    /// EI-040 — aguarda um evento. <b>Bloqueia a thread</b> até haver evento, timeout
    /// ou erro.
    /// </summary>
    int ReceberDadosOnLine(
        int inner,
        ref byte origem,
        ref byte complemento,
        StringBuilder cartao,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo);

    /// <summary>EI-041 — libera o giro no sentido de entrada.</summary>
    int LiberarCatracaEntrada(int inner);

    /// <summary>EI-042 — libera o giro no sentido de saída.</summary>
    int LiberarCatracaSaida(int inner);

    /// <summary>EI-043 — libera entrada em instalação com sentido invertido.</summary>
    int LiberarCatracaEntradaInvertida(int inner);

    /// <summary>EI-044 — libera saída em instalação com sentido invertido.</summary>
    int LiberarCatracaSaidaInvertida(int inner);

    /// <summary>EI-045 — libera o giro nos dois sentidos.</summary>
    int LiberarCatracaDoisSentidos(int inner);

    /// <summary>EI-047 — aciona o relé 2, que abre a fenda da urna.</summary>
    int AcionarRele2(int inner, byte tempo);

    /// <summary>EI-048 / EI-049 — bipes de feedback.</summary>
    int AcionarBipCurto(int inner);

    int AcionarBipLongo(int inner);

    /// <summary>EI-054 / EI-055 — luz de fundo do display.</summary>
    int LigarBackLite(int inner);

    int DesligarBackLite(int inner);

    /// <summary>EI-056 — mensagem fixa do display (32 caracteres, ou 16 com data).</summary>
    int EnviarMensagemPadraoOnLine(int inner, byte exibirData, string mensagem);

    /// <summary>EI-057 — mensagem temporária no display.</summary>
    int EnviarMensagemTemporariaOnLine(int inner, byte exibirData, string mensagem, byte tempo);
}
