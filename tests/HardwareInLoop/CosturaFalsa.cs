using System.Text;
using Topdata.EasyInner.Interop;

namespace HardwareInLoop.Tests;

/// <summary>
/// Uma EasyInner.dll de mentira, para exercitar a tradução do adapter.
/// </summary>
/// <remarks>
/// Não simula o equipamento — isso é papel do <c>InnerSimulator</c>. Aqui só se controla o
/// que cada chamada devolve, para verificar o que o adapter faz com isso.
/// </remarks>
internal sealed class CosturaFalsa : IEasyInnerNative
{
    /// <summary>Retorno de cada função, por nome. Ausente significa 0.</summary>
    public Dictionary<string, byte> Retornos { get; } = [];

    /// <summary>Ordem em que as funções foram chamadas.</summary>
    public List<string> Chamadas { get; } = [];

    /// <summary>Preenchido em ReceberDadosOnLine e ColetarBilhete.</summary>
    public string CartaoADevolver { get; set; } = string.Empty;

    public byte OrigemADevolver { get; set; }

    public (byte Dia, byte Mes, byte Ano, byte Hora, byte Minuto, byte Segundo) DataADevolver { get; set; }
        = (24, 9, 26, 19, 30, 45);

    private byte Registrar(string nome)
    {
        Chamadas.Add(nome);
        return Retornos.TryGetValue(nome, out var r) ? r : (byte)0;
    }

    public byte DefinirTipoConexao(byte tipo) => Registrar(nameof(DefinirTipoConexao));

    public byte AbrirPortaComunicacao(int porta) => Registrar(nameof(AbrirPortaComunicacao));

    public void FecharPortaComunicacao() => Chamadas.Add(nameof(FecharPortaComunicacao));

    public byte Ping(int inner) => Registrar(nameof(Ping));

    public byte PingOnLine(int inner) => Registrar(nameof(PingOnLine));

    public byte ReceberVersaoFirmware(
        int inner, ref byte linha, ref short variacao, ref byte versaoAlta,
        ref byte versaoBaixa, ref byte versaoSufixo, ref byte innerAcessoBio)
    {
        linha = 4; variacao = 12; versaoAlta = 5; versaoBaixa = 20; versaoSufixo = 1; innerAcessoBio = 1;
        return Registrar(nameof(ReceberVersaoFirmware));
    }

    public byte ReceberRelogio(
        int inner, ref byte dia, ref byte mes, ref byte ano, ref byte hora, ref byte minuto, ref byte segundo)
    {
        (dia, mes, ano, hora, minuto, segundo) = DataADevolver;
        return Registrar(nameof(ReceberRelogio));
    }

    public byte DefinirPadraoCartao(byte padrao) => Registrar(nameof(DefinirPadraoCartao));

    public byte DefinirQuantidadeDigitosCartao(byte quantidade) => Registrar(nameof(DefinirQuantidadeDigitosCartao));

    public byte ConfigurarTipoLeitor(byte tipo) => Registrar(nameof(ConfigurarTipoLeitor));

    public byte ConfigurarLeitor1(byte operacao) => Registrar(nameof(ConfigurarLeitor1));

    public byte ConfigurarLeitor2(byte operacao) => Registrar(nameof(ConfigurarLeitor2));

    public byte ConfigurarAcionamento1(byte funcao, byte tempo) => Registrar(nameof(ConfigurarAcionamento1));

    public byte ConfigurarAcionamento2(byte funcao, byte tempo) => Registrar(nameof(ConfigurarAcionamento2));

    public byte ConfigurarInnerOnLine() => Registrar(nameof(ConfigurarInnerOnLine));

    public byte ConfigurarInnerOffLine() => Registrar(nameof(ConfigurarInnerOffLine));

    public byte HabilitarTeclado(byte habilita, byte ecoar) => Registrar(nameof(HabilitarTeclado));

    public byte HabilitarMudancaOnLineOffLine(byte habilita, byte tempo) =>
        Registrar(nameof(HabilitarMudancaOnLineOffLine));

    public byte EnviarConfiguracoes(int inner) => Registrar(nameof(EnviarConfiguracoes));

    public byte EnviarFormasEntradasOnLine(
        int inner, byte qtdeDigitosTeclado, byte ecoTeclado,
        byte formaEntrada, byte tempoTeclado, byte posicaoCursorTeclado) =>
        Registrar(nameof(EnviarFormasEntradasOnLine));

    public byte ColetarBilhete(
        int inner, ref byte tipo, ref byte dia, ref byte mes, ref byte ano,
        ref byte hora, ref byte minuto, byte[] cartao)
    {
        tipo = 1;
        (dia, mes, ano, hora, minuto) = (DataADevolver.Dia, DataADevolver.Mes, DataADevolver.Ano,
                                         DataADevolver.Hora, DataADevolver.Minuto);
        Escrever(cartao, CartaoADevolver);
        return Registrar(nameof(ColetarBilhete));
    }

    public byte ReceberDadosOnLine(
        int inner, ref byte origem, ref byte complemento, byte[] cartao,
        ref byte dia, ref byte mes, ref byte ano, ref byte hora, ref byte minuto, ref byte segundo)
    {
        origem = OrigemADevolver;
        complemento = 0;
        (dia, mes, ano, hora, minuto, segundo) = DataADevolver;
        Escrever(cartao, CartaoADevolver);
        return Registrar(nameof(ReceberDadosOnLine));
    }

    public byte LiberarCatracaEntrada(int inner) => Registrar(nameof(LiberarCatracaEntrada));

    public byte LiberarCatracaSaida(int inner) => Registrar(nameof(LiberarCatracaSaida));

    public byte LiberarCatracaEntradaInvertida(int inner) => Registrar(nameof(LiberarCatracaEntradaInvertida));

    public byte LiberarCatracaSaidaInvertida(int inner) => Registrar(nameof(LiberarCatracaSaidaInvertida));

    public byte LiberarCatracaDoisSentidos(int inner) => Registrar(nameof(LiberarCatracaDoisSentidos));

    public byte AcionarRele2(int inner) => Registrar(nameof(AcionarRele2));

    public byte EnviarMensagemPadraoOnLine(int inner, byte exibirData, string mensagem)
    {
        UltimaMensagem = mensagem;
        return Registrar(nameof(EnviarMensagemPadraoOnLine));
    }

    public byte EnviarMensagemTemporariaOnLine(int inner, byte exibirData, string mensagem, byte tempo)
    {
        UltimaMensagem = mensagem;
        UltimoTempoDeMensagem = tempo;
        return Registrar(nameof(EnviarMensagemTemporariaOnLine));
    }

    public string UltimaMensagem { get; private set; } = string.Empty;

    public byte UltimoTempoDeMensagem { get; private set; }

    // A capacidade é a mesma do interop real, para que um estouro apareça aqui também.
    public byte[] NovoBufferDeCartao() => new byte[64];

    public string LerCartao(byte[] buffer)
    {
        var fim = Array.IndexOf(buffer, (byte)0);
        return Encoding.ASCII.GetString(buffer, 0, fim < 0 ? buffer.Length : fim).Trim();
    }

    private static void Escrever(byte[] destino, string texto)
    {
        Array.Clear(destino);
        var bytes = Encoding.ASCII.GetBytes(texto);
        Array.Copy(bytes, destino, Math.Min(bytes.Length, destino.Length - 1));
    }
}
