using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// Mapa de giro (decisão D9, docs/34 §9) no caminho inteiro: laço de verdade, adapter de verdade,
/// DLL falsa. A origem da leitura escolhe a função nativa; o mapa vazio é o de sempre.
/// </summary>
/// <remarks>
/// O caso do dono do produto: "quando eu coloco o cartão na urna, quero que o giro seja para a
/// esquerda e conte como entrada". Na instalação de teste, "para a esquerda" é
/// <c>LiberarCatracaSaida</c> (EI-042): a urna chama essa função e o giro continua contado como
/// entrada. Códigos sintéticos.
/// </remarks>
public sealed class MapaDeGiroDePontaAPontaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 1, 21, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<byte> LinhaDaCosturaFalsa = [4];

    /// <summary>Urna gira como EI-042 e conta como entrada; o resto, padrão.</summary>
    private static readonly MapaDeGiro UrnaPelaSaidaContandoEntrada = MapaDeGiro.Vazio
        .Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada));

    private static DeviceConfiguration Configuracao(MapaDeGiro mapa) =>
        MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma, mapa);

    private static Decision Autorizado() => new(
        DecisionOutcome.Allowed, ReasonCodes.Autorizado, DegradationTier.T1SemInternet, TimeSpan.FromMilliseconds(3), []);

    private static void Ate(DevicePump laco, DeviceSlot catraca, DeviceState alvo)
    {
        for (var i = 0; i < 40 && catraca.Maquina.Current != alvo; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(alvo, catraca.Maquina.Current);
    }

    private static (CosturaFalsa Costura, DevicePump Laco, DeviceSlot Catraca, TopdataInnerAdapter Adapter) EmOperacao(
        MapaDeGiro mapa, bool exibirTextoDoGiro = false)
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(
            adapter, () => Agora, LinhaDaCosturaFalsa, decidir: _ => Autorizado(), exibirTextoDoGiro: exibirTextoDoGiro);
        var catraca = new DeviceSlot(1, Configuracao(mapa), () => Agora);

        Ate(laco, catraca, DeviceState.Polling);
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerta o relógio
        costura.Chamadas.Clear();
        costura.ChamadasComArgumentos.Clear();
        return (costura, laco, catraca, adapter);
    }

    private static List<string> LiberarPelaLeitura(KnownEventOrigin origem, MapaDeGiro mapa)
    {
        var (costura, laco, catraca, adapter) = EmOperacao(mapa);
        using var _ = adapter;

        costura.OrigemADevolver = (byte)origem;
        costura.CartaoADevolver = "0000000101";
        Ate(laco, catraca, DeviceState.LiberarCatraca);
        costura.Chamadas.Clear();

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
        return [.. costura.Chamadas];
    }

    [Fact]
    public void Cartao_na_urna_com_o_mapa_chama_LiberarCatracaSaida() =>
        Assert.Equal(["LiberarCatracaSaida"], LiberarPelaLeitura(KnownEventOrigin.Leitor2, UrnaPelaSaidaContandoEntrada));

    [Theory]
    [InlineData(KnownEventOrigin.QrCode)]
    [InlineData(KnownEventOrigin.Leitor1)]
    public void Origem_sem_regra_segue_a_funcao_do_perfil(KnownEventOrigin origem) =>
        Assert.Equal(["LiberarCatracaEntrada"], LiberarPelaLeitura(origem, UrnaPelaSaidaContandoEntrada));

    /// <summary>Padrão = hoje: com o mapa vazio, toda leitura chama EI-041.</summary>
    [Theory]
    [InlineData(KnownEventOrigin.QrCode)]
    [InlineData(KnownEventOrigin.Leitor1)]
    [InlineData(KnownEventOrigin.Leitor2)]
    public void Mapa_vazio_e_o_comportamento_de_hoje(KnownEventOrigin origem) =>
        Assert.Equal(["LiberarCatracaEntrada"], LiberarPelaLeitura(origem, MapaDeGiro.Vazio));

    [Theory]
    [InlineData(FuncaoDeLiberacao.Entrada, "LiberarCatracaEntrada")]
    [InlineData(FuncaoDeLiberacao.Saida, "LiberarCatracaSaida")]
    [InlineData(FuncaoDeLiberacao.EntradaInvertida, "LiberarCatracaEntradaInvertida")]
    [InlineData(FuncaoDeLiberacao.SaidaInvertida, "LiberarCatracaSaidaInvertida")]
    public void Leitor_1_chama_exatamente_a_funcao_da_regra(FuncaoDeLiberacao funcao, string esperada) =>
        Assert.Equal(
            [esperada],
            LiberarPelaLeitura(KnownEventOrigin.QrCode, MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor1, new RegraDeGiro(funcao, SentidoContado.Entrada))));

    [Fact]
    public void Liberacao_manual_usa_a_regra_da_liberacao_manual()
    {
        var mapa = MapaDeGiro.Vazio.Com(OrigemDoGiro.LiberacaoManual, new RegraDeGiro(FuncaoDeLiberacao.SaidaInvertida, SentidoContado.Saida));
        var (costura, laco, catraca, adapter) = EmOperacao(mapa);
        using var _ = adapter;

        var (comando, problemas) = ComandoDeCatraca.Criar(1, TipoDeComando.LiberacaoManual, "Operador de teste", Agora, motivo: "ensaio sintético");
        Assert.Empty(problemas);
        catraca.Enfileirar(comando!);

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal(["LiberarCatracaSaidaInvertida"], costura.Chamadas);
    }

    /// <summary>
    /// Chave técnica <c>catraca.exibir_texto_do_giro</c> desligada (o padrão): nenhuma chamada a
    /// mais no caminho da passagem.
    /// </summary>
    [Fact]
    public void Sem_a_chave_nenhuma_mensagem_vai_ao_display() =>
        Assert.DoesNotContain(
            "EnviarMensagemTemporariaOnLine",
            LiberarPelaLeitura(KnownEventOrigin.Leitor2, MapaDeGiro.Vazio.Com(
                OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada, "Bem-vindo"))));

    /// <summary>
    /// Com a chave, o texto do giro vai num passo próprio (uma chamada por passo), antes da
    /// liberação; o estado continua <see cref="DeviceState.LiberarCatraca"/> entre os dois.
    /// </summary>
    [Fact]
    public void Com_a_chave_o_texto_vai_num_passo_proprio_antes_de_liberar()
    {
        var mapa = MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada, "Bem-vindo"));
        var (costura, laco, catraca, adapter) = EmOperacao(mapa, exibirTextoDoGiro: true);
        using var _ = adapter;

        costura.OrigemADevolver = (byte)KnownEventOrigin.Leitor2;
        costura.CartaoADevolver = "0000000101";
        Ate(laco, catraca, DeviceState.LiberarCatraca);
        costura.Chamadas.Clear();
        costura.ChamadasComArgumentos.Clear();

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(["EnviarMensagemTemporariaOnLine"], costura.Chamadas);
        Assert.Contains("Bem-vindo", costura.ChamadasComArgumentos[0].Argumentos);
        Assert.Equal(DeviceState.LiberarCatraca, catraca.Maquina.Current);

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(["EnviarMensagemTemporariaOnLine", "LiberarCatracaSaida"], costura.Chamadas);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
    }
}
