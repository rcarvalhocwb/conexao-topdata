namespace Topdata.EasyInner.Interop;

/// <summary>
/// Costura sobre a <c>EasyInner.dll</c>.
/// </summary>
/// <remarks>
/// <para>
/// Existe por dois motivos. O primeiro é poder trocar a forma de interoperabilidade — a DLL
/// hoje, o protocolo de baixo nível sob NDA amanhã — sem mexer em quem chama. O segundo é
/// poder testar a tradução do adapter <b>sem a DLL</b>, que só carrega em Windows de 32
/// bits: sem esta costura, nenhuma linha do adapter seria exercitável antes da bancada.
/// </para>
/// <para>
/// As assinaturas espelham o SDK 6.0.2.0: retorno <see langword="byte"/>, e não
/// <c>int</c> como o manual sugeria. Os identificadores <c>EI-xxx</c> apontam para
/// <c>docs/compatibility-matrix/funcoes-easyinner.csv</c>.
/// </para>
/// <para>
/// Todos os métodos são <b>bloqueantes</b> e a DLL <b>não é thread-safe</b>: uma única
/// thread por processo pode chamá-los, e nunca a da interface gráfica.
/// </para>
/// </remarks>
public interface IEasyInnerNative
{
    /// <summary>EI-001 — tipo de conexão. 2 = TCP com porta fixa.</summary>
    byte DefinirTipoConexao(byte tipo);

    /// <summary>EI-002 — abre a porta TCP. Retorno 8 é GPF.</summary>
    byte AbrirPortaComunicacao(int porta);

    /// <summary>EI-003 — fecha a porta. Sem retorno.</summary>
    void FecharPortaComunicacao();

    /// <summary>EI-004 — testa a presença do equipamento.</summary>
    byte Ping(int inner);

    /// <summary>EI-005 — mantém o equipamento em modo on-line.</summary>
    byte PingOnLine(int inner);

    /// <summary>EI-006 — versão de firmware.</summary>
    byte ReceberVersaoFirmware(
        int inner,
        ref byte linha,
        ref short variacao,
        ref byte versaoAlta,
        ref byte versaoBaixa,
        ref byte versaoSufixo,
        ref byte innerAcessoBio);

    /// <summary>EI-007 — relógio do equipamento. Ano com dois dígitos.</summary>
    byte ReceberRelogio(
        int inner,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo);

    /// <summary>EI-010 — padrão do cartão. 0 Topdata, 1 Livre.</summary>
    byte DefinirPadraoCartao(byte padrao);

    /// <summary>EI-011 — quantidade fixa de dígitos.</summary>
    byte DefinirQuantidadeDigitosCartao(byte quantidade);

    /// <summary>EI-013 — tecnologia do leitor, 0 a 8.</summary>
    byte ConfigurarTipoLeitor(byte tipo);

    /// <summary>EI-014 — sentido lógico do leitor 1.</summary>
    byte ConfigurarLeitor1(byte operacao);

    /// <summary>EI-015 — sentido lógico do leitor 2.</summary>
    byte ConfigurarLeitor2(byte operacao);

    /// <summary>EI-016 — função e tempo do relé 1.</summary>
    byte ConfigurarAcionamento1(byte funcao, byte tempo);

    /// <summary>EI-017 — função e tempo do relé 2, o da urna.</summary>
    byte ConfigurarAcionamento2(byte funcao, byte tempo);

    /// <summary>EI-018 — prepara o modo on-line.</summary>
    byte ConfigurarInnerOnLine();

    /// <summary>EI-019 — prepara o modo off-line.</summary>
    byte ConfigurarInnerOffLine();

    /// <summary>EI-020 — teclado e eco.</summary>
    byte HabilitarTeclado(byte habilita, byte ecoar);

    /// <summary>EI-028 — mudança automática on-line/off-line.</summary>
    byte HabilitarMudancaOnLineOffLine(byte habilita, byte tempo);

    /// <summary>EI-030 — aplica a configuração montada. Envia também os padrões da DLL.</summary>
    byte EnviarConfiguracoes(int inner);

    /// <summary>EI-032 — formas de entrada. É esta que rearma o leitor a cada ciclo.</summary>
    byte EnviarFormasEntradasOnLine(
        int inner,
        byte qtdeDigitosTeclado,
        byte ecoTeclado,
        byte formaEntrada,
        byte tempoTeclado,
        byte posicaoCursorTeclado);

    /// <summary>EI-039 — coleta um bilhete. Remove-o da memória do equipamento.</summary>
    byte ColetarBilhete(
        int inner,
        ref byte tipo,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        byte[] cartao);

    /// <summary>EI-040 — evento on-line. Tem segundos, ao contrário do bilhete.</summary>
    byte ReceberDadosOnLine(
        int inner,
        ref byte origem,
        ref byte complemento,
        byte[] cartao,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo);

    /// <summary>EI-041 — libera o giro no sentido entrada.</summary>
    byte LiberarCatracaEntrada(int inner);

    /// <summary>EI-042 — libera o giro no sentido saída.</summary>
    byte LiberarCatracaSaida(int inner);

    /// <summary>EI-043 — entrada, com o sentido físico invertido.</summary>
    byte LiberarCatracaEntradaInvertida(int inner);

    /// <summary>EI-044 — saída, com o sentido físico invertido.</summary>
    byte LiberarCatracaSaidaInvertida(int inner);

    /// <summary>EI-045 — libera nos dois sentidos. Proibido em operação normal.</summary>
    byte LiberarCatracaDoisSentidos(int inner);

    /// <summary>EI-047 — aciona o relé da urna. A duração vem da configuração.</summary>
    byte AcionarRele2(int inner);

    /// <summary>EI-056 — mensagem fixa do display ocioso.</summary>
    byte EnviarMensagemPadraoOnLine(int inner, byte exibirData, string mensagem);

    /// <summary>EI-057 — mensagem temporária, com tempo em segundos.</summary>
    byte EnviarMensagemTemporariaOnLine(int inner, byte exibirData, string mensagem, byte tempo);

    /// <summary>Buffer com a capacidade certa para receber um cartão.</summary>
    byte[] NovoBufferDeCartao();

    /// <summary>Lê o cartão do buffer, parando no terminador nulo.</summary>
    string LerCartao(byte[] buffer);
}
