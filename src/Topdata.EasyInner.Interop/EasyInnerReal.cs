namespace Topdata.EasyInner.Interop;

/// <summary>
/// A costura ligada na DLL de verdade.
/// </summary>
/// <remarks>
/// Fina de propósito: só encaminha. Toda decisão mora no adapter, que é testável sem a DLL.
/// Instanciar isto fora de um processo Windows x86 compila, mas a primeira chamada falha
/// ao carregar a biblioteca.
/// </remarks>
public sealed class EasyInnerReal : IEasyInnerNative
{
    public byte DefinirTipoConexao(byte tipo) => EasyInnerNative.DefinirTipoConexao(tipo);

    public byte AbrirPortaComunicacao(int porta) => EasyInnerNative.AbrirPortaComunicacao(porta);

    public void FecharPortaComunicacao() => EasyInnerNative.FecharPortaComunicacao();

    public byte Ping(int inner) => EasyInnerNative.Ping(inner);

    public byte PingOnLine(int inner) => EasyInnerNative.PingOnLine(inner);

    public byte ReceberVersaoFirmware(
        int inner,
        ref byte linha,
        ref short variacao,
        ref byte versaoAlta,
        ref byte versaoBaixa,
        ref byte versaoSufixo,
        ref byte innerAcessoBio) =>
        EasyInnerNative.ReceberVersaoFirmware(
            inner, ref linha, ref variacao, ref versaoAlta, ref versaoBaixa, ref versaoSufixo, ref innerAcessoBio);

    public byte ReceberRelogio(
        int inner,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo) =>
        EasyInnerNative.ReceberRelogio(inner, ref dia, ref mes, ref ano, ref hora, ref minuto, ref segundo);

    public byte DefinirPadraoCartao(byte padrao) => EasyInnerNative.DefinirPadraoCartao(padrao);

    public byte DefinirQuantidadeDigitosCartao(byte quantidade) =>
        EasyInnerNative.DefinirQuantidadeDigitosCartao(quantidade);

    public byte ConfigurarTipoLeitor(byte tipo) => EasyInnerNative.ConfigurarTipoLeitor(tipo);

    public byte ConfigurarLeitor1(byte operacao) => EasyInnerNative.ConfigurarLeitor1(operacao);

    public byte ConfigurarLeitor2(byte operacao) => EasyInnerNative.ConfigurarLeitor2(operacao);

    public byte ConfigurarAcionamento1(byte funcao, byte tempo) =>
        EasyInnerNative.ConfigurarAcionamento1(funcao, tempo);

    public byte ConfigurarAcionamento2(byte funcao, byte tempo) =>
        EasyInnerNative.ConfigurarAcionamento2(funcao, tempo);

    public byte ConfigurarInnerOnLine() => EasyInnerNative.ConfigurarInnerOnLine();

    public byte ConfigurarInnerOffLine() => EasyInnerNative.ConfigurarInnerOffLine();

    public byte HabilitarTeclado(byte habilita, byte ecoar) => EasyInnerNative.HabilitarTeclado(habilita, ecoar);

    public byte HabilitarMudancaOnLineOffLine(byte habilita, byte tempo) =>
        EasyInnerNative.HabilitarMudancaOnLineOffLine(habilita, tempo);

    public byte EnviarConfiguracoes(int inner) => EasyInnerNative.EnviarConfiguracoes(inner);

    public byte EnviarFormasEntradasOnLine(
        int inner,
        byte qtdeDigitosTeclado,
        byte ecoTeclado,
        byte formaEntrada,
        byte tempoTeclado,
        byte posicaoCursorTeclado) =>
        EasyInnerNative.EnviarFormasEntradasOnLine(
            inner, qtdeDigitosTeclado, ecoTeclado, formaEntrada, tempoTeclado, posicaoCursorTeclado);

    public byte ColetarBilhete(
        int inner,
        ref byte tipo,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        byte[] cartao) =>
        EasyInnerNative.ColetarBilhete(inner, ref tipo, ref dia, ref mes, ref ano, ref hora, ref minuto, cartao);

    public byte ReceberDadosOnLine(
        int inner,
        ref byte origem,
        ref byte complemento,
        byte[] cartao,
        ref byte dia,
        ref byte mes,
        ref byte ano,
        ref byte hora,
        ref byte minuto,
        ref byte segundo) =>
        EasyInnerNative.ReceberDadosOnLine(
            inner, ref origem, ref complemento, cartao, ref dia, ref mes, ref ano, ref hora, ref minuto, ref segundo);

    public byte LiberarCatracaEntrada(int inner) => EasyInnerNative.LiberarCatracaEntrada(inner);

    public byte LiberarCatracaSaida(int inner) => EasyInnerNative.LiberarCatracaSaida(inner);

    public byte LiberarCatracaEntradaInvertida(int inner) => EasyInnerNative.LiberarCatracaEntradaInvertida(inner);

    public byte LiberarCatracaSaidaInvertida(int inner) => EasyInnerNative.LiberarCatracaSaidaInvertida(inner);

    public byte LiberarCatracaDoisSentidos(int inner) => EasyInnerNative.LiberarCatracaDoisSentidos(inner);

    public byte AcionarRele2(int inner) => EasyInnerNative.AcionarRele2(inner);

    public byte EnviarMensagemPadraoOnLine(int inner, byte exibirData, string mensagem) =>
        EasyInnerNative.EnviarMensagemPadraoOnLine(inner, exibirData, mensagem);

    public byte EnviarMensagemTemporariaOnLine(int inner, byte exibirData, string mensagem, byte tempo) =>
        EasyInnerNative.EnviarMensagemTemporariaOnLine(inner, exibirData, mensagem, tempo);

    public byte[] NovoBufferDeCartao() => EasyInnerNative.NovoBufferDeCartao();

    public string LerCartao(byte[] buffer) => EasyInnerNative.LerCartao(buffer);
}
