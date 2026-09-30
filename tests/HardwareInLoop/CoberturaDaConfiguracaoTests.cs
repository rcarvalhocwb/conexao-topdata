using System.Reflection;
using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;
using Topdata.EasyInner.Interop;

namespace HardwareInLoop.Tests;

/// <summary>
/// ADR-0020 item 3: <b>todo</b> campo da <see cref="DeviceConfiguration"/> chega à DLL, ou está
/// declarado aqui como "não enviado", com o motivo.
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.1 do docs/35. Um campo que o modelo guarda e ninguém envia é o pior tipo de
/// defeito: a tela mostra um valor e a catraca fica com o padrão da DLL (foi o F2, docs/34 §2).
/// Por isso a lista é fechada: campo novo que não entrar em <see cref="Enviados"/> nem em
/// <see cref="NaoEnviados"/> reprova o teste.
/// </para>
/// <para>
/// "Enviado" é provado de ponta a ponta — laço de verdade, adapter de verdade, costura falsa —
/// até <see cref="DeviceState.Polling"/>, porque a mensagem padrão sai por um passo próprio
/// do laço e não pela montagem (F7). A prova é pelo <b>valor</b>: muda-se só aquele campo e a
/// DLL precisa receber o valor novo na função certa, que a configuração de partida não manda.
/// </para>
/// </remarks>
public sealed class CoberturaDaConfiguracaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A costura falsa se apresenta como linha 4; o teste a homologa para chegar à operação.</summary>
    private static readonly HashSet<byte> LinhaDaCosturaFalsa = [4];

    private const int Inner = 1;

    /// <summary>
    /// Como um campo chega à DLL: a mudança que o isola, a função nativa e os argumentos que
    /// ela precisa receber.
    /// </summary>
    private sealed record Envio(Func<DeviceConfiguration, DeviceConfiguration> Mudar, string Funcao, object[] Argumentos);

    /// <summary>
    /// A configuração de hoje: o padrão de fábrica montado sem sobreposições (Etapa A.1).
    /// </summary>
    private static DeviceConfiguration DeHoje() => MontadorDaConfiguracao.Montar(
        PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma);

    /// <summary>Campos enviados, cada um com a função da DLL que o leva (matriz FUN).</summary>
    private static readonly Dictionary<string, Envio> Enviados = new(StringComparer.Ordinal)
    {
        [nameof(DeviceConfiguration.PadraoCartao)] = new(
            c => c with { PadraoCartao = 0 },
            nameof(IEasyInnerNative.DefinirPadraoCartao), [(byte)0]),

        // Hoje é nulo e a função não é chamada (padrão da DLL); preenchido, vai (EI-011).
        [nameof(DeviceConfiguration.QuantidadeFixaDeDigitos)] = new(
            c => c with { QuantidadeFixaDeDigitos = 10 },
            nameof(IEasyInnerNative.DefinirQuantidadeDigitosCartao), [(byte)10]),

        // Só com a chave catraca.enviar_digitos_variaveis ligada (F2, EI-012).
        [nameof(DeviceConfiguration.QuantidadesVariaveisDeDigitos)] = new(
            c => c with { QuantidadesVariaveisDeDigitos = [7], EnviarDigitosVariaveis = true },
            nameof(IEasyInnerNative.InserirQuantidadeDigitoVariavel), [(byte)7]),

        [nameof(DeviceConfiguration.TipoDeLeitor)] = new(
            c => c with { TipoDeLeitor = 5 },
            nameof(IEasyInnerNative.ConfigurarTipoLeitor), [(byte)5]),

        [nameof(DeviceConfiguration.OperacaoDoLeitor1)] = new(
            c => c with { OperacaoDoLeitor1 = 3 },
            nameof(IEasyInnerNative.ConfigurarLeitor1), [(byte)3]),

        [nameof(DeviceConfiguration.OperacaoDoLeitor2)] = new(
            c => c with { OperacaoDoLeitor2 = 3 },
            nameof(IEasyInnerNative.ConfigurarLeitor2), [(byte)3]),

        [nameof(DeviceConfiguration.FuncaoDoAcionamento1)] = new(
            c => c with { FuncaoDoAcionamento1 = 7 },
            nameof(IEasyInnerNative.ConfigurarAcionamento1), [(byte)7, (byte)5]),

        [nameof(DeviceConfiguration.TempoDoAcionamento1)] = new(
            c => c with { TempoDoAcionamento1 = 9 },
            nameof(IEasyInnerNative.ConfigurarAcionamento1), [(byte)2, (byte)9]),

        [nameof(DeviceConfiguration.FuncaoDoAcionamento2)] = new(
            c => c with { FuncaoDoAcionamento2 = 4 },
            nameof(IEasyInnerNative.ConfigurarAcionamento2), [(byte)4, (byte)0]),

        [nameof(DeviceConfiguration.TempoDoAcionamento2)] = new(
            c => c with { TempoDoAcionamento2 = 6 },
            nameof(IEasyInnerNative.ConfigurarAcionamento2), [(byte)0, (byte)6]),

        // Não é argumento: escolhe entre ConfigurarInnerOnLine e ConfigurarInnerOffLine.
        [nameof(DeviceConfiguration.Online)] = new(
            c => c with { Online = false },
            nameof(IEasyInnerNative.ConfigurarInnerOffLine), []),

        [nameof(DeviceConfiguration.TecladoHabilitado)] = new(
            c => c with { TecladoHabilitado = true },
            nameof(IEasyInnerNative.HabilitarTeclado), [(byte)1, (byte)0]),

        [nameof(DeviceConfiguration.EcoDoTeclado)] = new(
            c => c with { EcoDoTeclado = 2 },
            nameof(IEasyInnerNative.HabilitarTeclado), [(byte)0, (byte)2]),

        [nameof(DeviceConfiguration.MudancaAutomatica)] = new(
            c => c with { MudancaAutomatica = 1 },
            nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), [(byte)1, (byte)10]),

        [nameof(DeviceConfiguration.TempoDaMudancaAutomatica)] = new(
            c => c with { TempoDaMudancaAutomatica = 20 },
            nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), [(byte)0, (byte)20]),

        // Fora da montagem, pelo passo próprio EnviarMsgPadrao do laço (F7, ADR-0006).
        [nameof(DeviceConfiguration.MensagemPadrao)] = new(
            c => c with { MensagemPadrao = "Pista sintetica 9" },
            nameof(IEasyInnerNative.EnviarMensagemPadraoOnLine), [Inner, (byte)0, "Pista sintetica 9"]),
    };

    /// <summary>
    /// Campos que <b>não</b> chegam à DLL como valor, e por quê. Entrar aqui é uma decisão
    /// escrita, não um esquecimento.
    /// </summary>
    private static readonly Dictionary<string, string> NaoEnviados = new(StringComparer.Ordinal)
    {
        [nameof(DeviceConfiguration.EnviarDigitosVariaveis)] =
            "chave técnica catraca.enviar_digitos_variaveis: decide se QuantidadesVariaveisDeDigitos vai à " +
            "DLL (EI-012); não é parâmetro da catraca. Desligada até HIL-CARD-02 (F2, docs/34 §2). " +
            "Coberta por AdapterTests.Sem_a_chave_os_digitos_variaveis_nao_sao_enviados_e_a_sequencia_e_a_de_hoje.",

        [nameof(DeviceConfiguration.PerfilFisico)] =
            "não é parâmetro do buffer de configuração: escolhe qual função de liberação (EI-041 a EI-044) " +
            "o laço chama a cada giro autorizado (F1, docs/34 §2). Coberto por LiberacaoDePontaAPontaTests.",
    };

    public static TheoryData<string> CamposEnviados()
    {
        var dados = new TheoryData<string>();
        foreach (var campo in Enviados.Keys)
        {
            dados.Add(campo);
        }

        return dados;
    }

    /// <summary>Conecta uma catraca até Polling e devolve o que a DLL recebeu.</summary>
    private static List<(string Funcao, object[] Argumentos)> Conectar(DeviceConfiguration configuracao)
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => Agora, LinhaDaCosturaFalsa);
        var catraca = new DeviceSlot(Inner, configuracao, () => Agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        return costura.ChamadasComArgumentos;
    }

    private static bool Recebeu(List<(string Funcao, object[] Argumentos)> chamadas, string funcao, object[] argumentos) =>
        chamadas.Any(c => c.Funcao == funcao && c.Argumentos.SequenceEqual(argumentos));

    [Fact]
    public void Todo_campo_publico_e_enviado_ou_declarado_nao_enviado()
    {
        const BindingFlags Publicos = BindingFlags.Public | BindingFlags.Instance;
        var campos = typeof(DeviceConfiguration).GetProperties(Publicos).Select(p => p.Name)
            .Concat(typeof(DeviceConfiguration).GetFields(Publicos).Select(f => f.Name))
            .ToList();

        var semDestino = campos.Where(c => !Enviados.ContainsKey(c) && !NaoEnviados.ContainsKey(c)).ToList();
        Assert.True(
            semDestino.Count == 0,
            "Campos da DeviceConfiguration que nem são enviados nem estão declarados como 'não enviado, motivo X' " +
            $"(ADR-0020 item 3): {string.Join(", ", semDestino)}");

        var nasDuas = Enviados.Keys.Intersect(NaoEnviados.Keys, StringComparer.Ordinal).ToList();
        Assert.True(nasDuas.Count == 0, $"Campos nas duas listas: {string.Join(", ", nasDuas)}");

        // Lista velha também reprova: um nome que saiu do modelo não pode continuar "coberto".
        var inexistentes = Enviados.Keys.Concat(NaoEnviados.Keys).Except(campos, StringComparer.Ordinal).ToList();
        Assert.True(inexistentes.Count == 0, $"Nomes nas listas que não existem mais: {string.Join(", ", inexistentes)}");

        Assert.All(NaoEnviados.Values, motivo => Assert.False(string.IsNullOrWhiteSpace(motivo)));
    }

    [Theory]
    [MemberData(nameof(CamposEnviados))]
    public void Campo_enviado_chega_a_dll_com_o_valor_da_configuracao(string campo)
    {
        var envio = Enviados[campo];
        var hoje = DeHoje();
        var mudada = envio.Mudar(hoje);

        // A mudança não pode ser recusada antes da DLL: senão o teste não exercitaria nada.
        Assert.Empty(mudada.Validar());

        var deHoje = Conectar(hoje);
        var daMudada = Conectar(mudada);

        Assert.False(
            Recebeu(deHoje, envio.Funcao, envio.Argumentos),
            $"{campo}: a configuração de hoje já manda {envio.Funcao}({string.Join(", ", envio.Argumentos)}); a prova não isola o campo.");
        Assert.True(
            Recebeu(daMudada, envio.Funcao, envio.Argumentos),
            $"{campo}: a DLL não recebeu {envio.Funcao}({string.Join(", ", envio.Argumentos)}).");
    }

    /// <summary>
    /// Os não enviados não mudam a montagem por si. <c>PerfilFisico</c> só aparece na
    /// liberação; <c>EnviarDigitosVariaveis</c> sem tamanho é recusada por <c>Validar</c>, e com
    /// tamanho é a prova de <c>QuantidadesVariaveisDeDigitos</c> acima.
    /// </summary>
    [Fact]
    public void Perfil_fisico_nao_muda_a_sequencia_de_conexao()
    {
        var hoje = Conectar(DeHoje());
        var invertida = Conectar(DeHoje() with { PerfilFisico = new GatePhysicalProfile(FuncaoDeLiberacao.SaidaInvertida) });

        Assert.Equal(hoje.Select(c => c.Funcao), invertida.Select(c => c.Funcao));
    }

    /// <summary>
    /// A sequência nativa de uma conexão com a configuração de hoje, congelada: o montador da
    /// Etapa A.1 não pode mudar nem a ordem nem um valor (três envios iguais até a A.7, F3).
    /// </summary>
    [Fact]
    public void Configuracao_de_hoje_envia_a_mesma_sequencia_e_os_mesmos_valores()
    {
        object[][] montagem =
        [
            [nameof(IEasyInnerNative.DefinirPadraoCartao), (byte)1],
            [nameof(IEasyInnerNative.ConfigurarTipoLeitor), (byte)8],
            [nameof(IEasyInnerNative.ConfigurarLeitor1), (byte)1],
            [nameof(IEasyInnerNative.ConfigurarLeitor2), (byte)1],
            [nameof(IEasyInnerNative.ConfigurarAcionamento1), (byte)2, (byte)5],
            [nameof(IEasyInnerNative.ConfigurarAcionamento2), (byte)0, (byte)0],
            [nameof(IEasyInnerNative.ConfigurarInnerOnLine)],
            [nameof(IEasyInnerNative.HabilitarTeclado), (byte)0, (byte)0],
            [nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), (byte)0, (byte)10],
            [nameof(IEasyInnerNative.EnviarConfiguracoes), Inner],
        ];

        var chamadas = Conectar(DeHoje());
        var configuracao = chamadas
            .Where(c => montagem.Any(m => (string)m[0] == c.Funcao))
            .Select(c => (object[])[c.Funcao, .. c.Argumentos])
            .ToList();

        var esperado = Enumerable.Repeat(montagem, 3).SelectMany(m => m).ToList();
        Assert.Equal(esperado.Count, configuracao.Count);
        for (var i = 0; i < esperado.Count; i++)
        {
            Assert.Equal(esperado[i], configuracao[i]);
        }

        Assert.DoesNotContain(chamadas, c => c.Funcao == nameof(IEasyInnerNative.DefinirQuantidadeDigitosCartao));
        Assert.DoesNotContain(chamadas, c => c.Funcao == nameof(IEasyInnerNative.InserirQuantidadeDigitoVariavel));
        Assert.Single(chamadas, c => c.Funcao == nameof(IEasyInnerNative.EnviarMensagemPadraoOnLine)
                                     && c.Argumentos.SequenceEqual([Inner, (byte)0, "Aproxime o ingresso"]));
    }
}
