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

    /// <summary>O que o adapter mandou em EnviarRelogio.</summary>
    public (byte Dia, byte Mes, byte Ano, byte Hora, byte Minuto, byte Segundo)? DataEnviada { get; private set; }

    /// <summary>
    /// Cada chamada com os argumentos que a DLL recebeu, na ordem de <see cref="Chamadas"/>.
    /// </summary>
    /// <remarks>
    /// Existe para provar que um valor da configuração chega à DLL, e não só que a função foi
    /// chamada (ADR-0020 item 3; <c>CoberturaDaConfiguracaoTests</c>, Etapa A.1 do docs/35).
    /// </remarks>
    public List<(string Funcao, object[] Argumentos)> ChamadasComArgumentos { get; } = [];

    private byte Registrar(string nome, params object[] argumentos)
    {
        Chamadas.Add(nome);
        ChamadasComArgumentos.Add((nome, argumentos));
        return Retornos.TryGetValue(nome, out var r) ? r : (byte)0;
    }

    public byte DefinirTipoConexao(byte tipo) => Registrar(nameof(DefinirTipoConexao), tipo);

    public byte AbrirPortaComunicacao(int porta) => Registrar(nameof(AbrirPortaComunicacao), porta);

    public void FecharPortaComunicacao()
    {
        Chamadas.Add(nameof(FecharPortaComunicacao));
        ChamadasComArgumentos.Add((nameof(FecharPortaComunicacao), []));
    }

    public byte Ping(int inner) => Registrar(nameof(Ping), inner);

    public byte PingOnLine(int inner) => Registrar(nameof(PingOnLine), inner);

    public byte ReceberVersaoFirmware(
        int inner, ref byte linha, ref short variacao, ref byte versaoAlta,
        ref byte versaoBaixa, ref byte versaoSufixo, ref byte innerAcessoBio)
    {
        linha = 4; variacao = 12; versaoAlta = 5; versaoBaixa = 20; versaoSufixo = 1; innerAcessoBio = 1;
        return Registrar(nameof(ReceberVersaoFirmware), inner);
    }

    public byte ReceberRelogio(
        int inner, ref byte dia, ref byte mes, ref byte ano, ref byte hora, ref byte minuto, ref byte segundo)
    {
        (dia, mes, ano, hora, minuto, segundo) = DataADevolver;
        return Registrar(nameof(ReceberRelogio), inner);
    }

    public byte EnviarRelogio(int inner, byte dia, byte mes, byte ano, byte hora, byte minuto, byte segundo)
    {
        DataEnviada = (dia, mes, ano, hora, minuto, segundo);
        return Registrar(nameof(EnviarRelogio), inner, dia, mes, ano, hora, minuto, segundo);
    }

    public byte DefinirPadraoCartao(byte padrao) => Registrar(nameof(DefinirPadraoCartao), padrao);

    public byte DefinirQuantidadeDigitosCartao(byte quantidade) => Registrar(nameof(DefinirQuantidadeDigitosCartao), quantidade);

    /// <summary>Tamanhos recebidos por InserirQuantidadeDigitoVariavel, na ordem.</summary>
    public List<byte> DigitosVariaveis { get; } = [];

    public byte InserirQuantidadeDigitoVariavel(byte digito)
    {
        DigitosVariaveis.Add(digito);
        return Registrar(nameof(InserirQuantidadeDigitoVariavel), digito);
    }

    public byte ConfigurarTipoLeitor(byte tipo) => Registrar(nameof(ConfigurarTipoLeitor), tipo);

    public byte ConfigurarLeitor1(byte operacao) => Registrar(nameof(ConfigurarLeitor1), operacao);

    public byte ConfigurarLeitor2(byte operacao) => Registrar(nameof(ConfigurarLeitor2), operacao);

    public byte ConfigurarAcionamento1(byte funcao, byte tempo) => Registrar(nameof(ConfigurarAcionamento1), funcao, tempo);

    public byte ConfigurarAcionamento2(byte funcao, byte tempo) => Registrar(nameof(ConfigurarAcionamento2), funcao, tempo);

    public byte ConfigurarInnerOnLine() => Registrar(nameof(ConfigurarInnerOnLine));

    public byte ConfigurarInnerOffLine() => Registrar(nameof(ConfigurarInnerOffLine));

    public byte HabilitarTeclado(byte habilita, byte ecoar) => Registrar(nameof(HabilitarTeclado), habilita, ecoar);

    public byte HabilitarMudancaOnLineOffLine(byte habilita, byte tempo) =>
        Registrar(nameof(HabilitarMudancaOnLineOffLine), habilita, tempo);

    // Etapa A.2: funções de montagem que o adapter só chama com a chave técnica de cada uma.
    public byte ConfigurarWiegandDoisLeitores(byte habilita, byte exibirMensagem) =>
        Registrar(nameof(ConfigurarWiegandDoisLeitores), habilita, exibirMensagem);

    public byte RegistrarAcessoNegado(byte tipoRegistro) => Registrar(nameof(RegistrarAcessoNegado), tipoRegistro);

    public byte ReceberDataHoraDadosOnLine(byte recebe) => Registrar(nameof(ReceberDataHoraDadosOnLine), recebe);

    // Só números sintéticos chegam aqui (regra do docs/35); o registro é da costura de teste.
    public byte DefinirNumeroCartaoMaster(string master) => Registrar(nameof(DefinirNumeroCartaoMaster), master);

    public byte DefinirTipoListaAcesso(byte tipo) => Registrar(nameof(DefinirTipoListaAcesso), tipo);

    public byte EnviarConfiguracoes(int inner) => Registrar(nameof(EnviarConfiguracoes), inner);

    // Etapa A.7: só chamada na sequência oficial de conexão (chave catraca.sequencia_oficial).
    public byte EnviarConfiguracoesMudancaAutomaticaOnLineOffLine(int inner) =>
        Registrar(nameof(EnviarConfiguracoesMudancaAutomaticaOnLineOffLine), inner);

    public byte EnviarFormasEntradasOnLine(
        int inner, byte qtdeDigitosTeclado, byte ecoTeclado,
        byte formaEntrada, byte tempoTeclado, byte posicaoCursorTeclado) =>
        Registrar(nameof(EnviarFormasEntradasOnLine), inner, qtdeDigitosTeclado, ecoTeclado, formaEntrada, tempoTeclado, posicaoCursorTeclado);

    public byte ColetarBilhete(
        int inner, ref byte tipo, ref byte dia, ref byte mes, ref byte ano,
        ref byte hora, ref byte minuto, byte[] cartao)
    {
        tipo = 1;
        (dia, mes, ano, hora, minuto) = (DataADevolver.Dia, DataADevolver.Mes, DataADevolver.Ano,
                                         DataADevolver.Hora, DataADevolver.Minuto);
        Escrever(cartao, CartaoADevolver);
        return Registrar(nameof(ColetarBilhete), inner);
    }

    public byte ReceberDadosOnLine(
        int inner, ref byte origem, ref byte complemento, byte[] cartao,
        ref byte dia, ref byte mes, ref byte ano, ref byte hora, ref byte minuto, ref byte segundo)
    {
        origem = OrigemADevolver;
        complemento = 0;
        (dia, mes, ano, hora, minuto, segundo) = DataADevolver;
        Escrever(cartao, CartaoADevolver);
        return Registrar(nameof(ReceberDadosOnLine), inner);
    }

    public byte LiberarCatracaEntrada(int inner) => Registrar(nameof(LiberarCatracaEntrada), inner);

    public byte LiberarCatracaSaida(int inner) => Registrar(nameof(LiberarCatracaSaida), inner);

    public byte LiberarCatracaEntradaInvertida(int inner) => Registrar(nameof(LiberarCatracaEntradaInvertida), inner);

    public byte LiberarCatracaSaidaInvertida(int inner) => Registrar(nameof(LiberarCatracaSaidaInvertida), inner);

    public byte LiberarCatracaDoisSentidos(int inner) => Registrar(nameof(LiberarCatracaDoisSentidos), inner);

    public byte AcionarRele2(int inner) => Registrar(nameof(AcionarRele2), inner);

    public byte EnviarMensagemPadraoOnLine(int inner, byte exibirData, string mensagem)
    {
        UltimaMensagem = mensagem;
        return Registrar(nameof(EnviarMensagemPadraoOnLine), inner, exibirData, mensagem);
    }

    public byte EnviarMensagemTemporariaOnLine(int inner, byte exibirData, string mensagem, byte tempo)
    {
        UltimaMensagem = mensagem;
        UltimoTempoDeMensagem = tempo;
        return Registrar(nameof(EnviarMensagemTemporariaOnLine), inner, exibirData, mensagem, tempo);
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
