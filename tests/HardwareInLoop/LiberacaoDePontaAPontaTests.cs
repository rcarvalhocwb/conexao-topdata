using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// O caminho inteiro do giro: laço de verdade, adapter de verdade, DLL falsa.
/// </summary>
/// <remarks>
/// <para>
/// Existe por causa do defeito F1 do docs/34 §2. O laço trocava Entrada por Saída quando o
/// perfil dizia "invertido", e o adapter, olhando o mesmo perfil, trocava de novo para a
/// variante invertida: a catraca instalada à esquerda receberia
/// <c>LiberarCatracaSaidaInvertida</c>. Os testes de cada lado passavam, porque cada um via
/// só a sua metade.
/// </para>
/// <para>
/// Agora o perfil guarda a função exata (<see cref="FuncaoDeLiberacao"/>) e aqui se prova,
/// para cada valor, que é essa a função nativa chamada — por ingresso e por liberação manual,
/// que passam pelo mesmo estado <see cref="DeviceState.LiberarCatraca"/>.
/// </para>
/// </remarks>
public sealed class LiberacaoDePontaAPontaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);

    /// <summary>A costura falsa se apresenta como linha 4; o teste a homologa para chegar à operação.</summary>
    private static readonly HashSet<byte> LinhaDaCosturaFalsa = [4];

    private static DeviceConfiguration Configuracao(FuncaoDeLiberacao funcao) => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 0,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Aproxime o ingresso",
        PerfilFisico = new GatePhysicalProfile(funcao),
    };

    private static Decision Autorizado() => new(
        DecisionOutcome.Allowed,
        ReasonCodes.Autorizado,
        DegradationTier.T1SemInternet,
        TimeSpan.FromMilliseconds(3),
        []);

    private static void Ate(DevicePump laco, DeviceSlot catraca, DeviceState alvo)
    {
        for (var i = 0; i < 40 && catraca.Maquina.Current != alvo; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(alvo, catraca.Maquina.Current);
    }

    /// <summary>Leva a catraca até Polling, com o relógio já acertado, e limpa o registro.</summary>
    private static (CosturaFalsa Costura, DevicePump Laco, DeviceSlot Catraca, TopdataInnerAdapter Adapter) EmOperacao(
        FuncaoDeLiberacao funcao, Func<DeviceEvent, Decision>? decidir = null)
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => Agora, LinhaDaCosturaFalsa, decidir: decidir ?? (_ => Autorizado()));
        var catraca = new DeviceSlot(1, Configuracao(funcao), () => Agora);

        Ate(laco, catraca, DeviceState.Polling);

        // O primeiro passo em Polling acerta o relógio; o seguinte já espera evento.
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Contains("EnviarRelogio", costura.Chamadas);

        costura.Chamadas.Clear();
        costura.ChamadasComArgumentos.Clear();
        return (costura, laco, catraca, adapter);
    }

    [Theory]
    [InlineData(FuncaoDeLiberacao.Entrada, "LiberarCatracaEntrada")]
    [InlineData(FuncaoDeLiberacao.EntradaInvertida, "LiberarCatracaEntradaInvertida")]
    [InlineData(FuncaoDeLiberacao.Saida, "LiberarCatracaSaida")]
    [InlineData(FuncaoDeLiberacao.SaidaInvertida, "LiberarCatracaSaidaInvertida")]
    public void Ingresso_autorizado_chama_exatamente_a_funcao_do_perfil(FuncaoDeLiberacao funcao, string esperada)
    {
        var (costura, laco, catraca, adapter) = EmOperacao(funcao);
        using var _ = adapter;

        costura.OrigemADevolver = (byte)KnownEventOrigin.QrCode;
        costura.CartaoADevolver = "0000000101";

        Ate(laco, catraca, DeviceState.LiberarCatraca);
        costura.Chamadas.Clear();

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal([esperada], costura.Chamadas);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
    }

    [Theory]
    [InlineData(FuncaoDeLiberacao.Entrada, "LiberarCatracaEntrada")]
    [InlineData(FuncaoDeLiberacao.EntradaInvertida, "LiberarCatracaEntradaInvertida")]
    [InlineData(FuncaoDeLiberacao.Saida, "LiberarCatracaSaida")]
    [InlineData(FuncaoDeLiberacao.SaidaInvertida, "LiberarCatracaSaidaInvertida")]
    public void Liberacao_manual_usa_a_mesma_funcao_do_perfil(FuncaoDeLiberacao funcao, string esperada)
    {
        var (costura, laco, catraca, adapter) = EmOperacao(funcao);
        using var _ = adapter;

        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberacaoManual, "Operador de teste", Agora, motivo: "ensaio sintético");
        Assert.Empty(problemas);
        catraca.Enfileirar(comando!);

        // Um passo pede a liberação; o seguinte a executa.
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(DeviceState.LiberarCatraca, catraca.Maquina.Current);
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal([esperada], costura.Chamadas);
    }

    /// <summary>
    /// Etapa A.8: "Liberar saída" chama a outra função do par da entrada comissionada (EI-041/042
    /// na instalação direta, EI-043/044 na invertida), só com o Inner, pelo mesmo estado
    /// <see cref="DeviceState.LiberarCatraca"/>.
    /// </summary>
    [Theory]
    [InlineData(FuncaoDeLiberacao.Entrada, "LiberarCatracaSaida")]
    [InlineData(FuncaoDeLiberacao.EntradaInvertida, "LiberarCatracaSaidaInvertida")]
    [InlineData(FuncaoDeLiberacao.Saida, "LiberarCatracaEntrada")]
    [InlineData(FuncaoDeLiberacao.SaidaInvertida, "LiberarCatracaEntradaInvertida")]
    public void Liberar_saida_chama_a_outra_funcao_do_par_do_perfil(FuncaoDeLiberacao funcao, string esperada)
    {
        var (costura, laco, catraca, adapter) = EmOperacao(funcao);
        using var _ = adapter;

        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberarSaida, "Operador de teste", Agora, motivo: "ensaio sintético");
        Assert.Empty(problemas);
        catraca.Enfileirar(comando!);

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(DeviceState.LiberarCatraca, catraca.Maquina.Current);
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        var (chamada, argumentos) = Assert.Single(costura.ChamadasComArgumentos);
        Assert.Equal(esperada, chamada);
        Assert.Equal(new object[] { 1 }, argumentos);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
    }

    /// <summary>Dois sentidos é uma função só (EI-045), qualquer que seja o perfil.</summary>
    [Theory]
    [InlineData(FuncaoDeLiberacao.Entrada)]
    [InlineData(FuncaoDeLiberacao.EntradaInvertida)]
    [InlineData(FuncaoDeLiberacao.Saida)]
    [InlineData(FuncaoDeLiberacao.SaidaInvertida)]
    public void Liberar_dois_sentidos_chama_so_a_funcao_dos_dois_sentidos(FuncaoDeLiberacao funcao)
    {
        var (costura, laco, catraca, adapter) = EmOperacao(funcao);
        using var _ = adapter;

        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberarDoisSentidos, "Operador de teste", Agora, motivo: "ensaio sintético", confirmacao: "EVACUAR 1");
        Assert.Empty(problemas);
        catraca.Enfileirar(comando!);

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        var (chamada, argumentos) = Assert.Single(costura.ChamadasComArgumentos);
        Assert.Equal("LiberarCatracaDoisSentidos", chamada);
        Assert.Equal(new object[] { 1 }, argumentos);
    }

    /// <summary>
    /// Etapa A.8: o bip pedido pelo operador é uma chamada só, em Polling, e a catraca segue
    /// em Polling — nada de reconectar nem de rearmar o leitor.
    /// </summary>
    [Theory]
    [InlineData(TipoDeComando.BipCurto, "AcionarBipCurto")]
    [InlineData(TipoDeComando.BipLongo, "AcionarBipLongo")]
    public void Bip_do_operador_e_uma_chamada_so_e_a_catraca_segue_em_polling(TipoDeComando tipo, string esperada)
    {
        var (costura, laco, catraca, adapter) = EmOperacao(FuncaoDeLiberacao.Entrada);
        using var _ = adapter;

        var (comando, problemas) = ComandoDeCatraca.Criar(1, tipo, "Operador de teste", Agora);
        Assert.Empty(problemas);
        catraca.Enfileirar(comando!);

        Assert.Equal($"comando {tipo}: bip acionado", laco.Passo(catraca, TimeSpan.FromMilliseconds(10)));

        var (chamada, argumentos) = Assert.Single(costura.ChamadasComArgumentos);
        Assert.Equal(esperada, chamada);
        Assert.Equal(new object[] { 1 }, argumentos);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
    }

    /// <summary>
    /// Bip não é acoplado à decisão nesta etapa: nem a passagem autorizada nem a negada chamam
    /// bip (cada chamada a mais no caminho da passagem reduz a vazão, docs/34 §8).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decisao_automatica_nunca_bipa(bool autorizado)
    {
        var negado = new Decision(
            DecisionOutcome.Denied, ReasonCodes.CredencialDesconhecida, DegradationTier.T1SemInternet, TimeSpan.FromMilliseconds(3), []);
        var (costura, laco, catraca, adapter) = EmOperacao(FuncaoDeLiberacao.Entrada, _ => autorizado ? Autorizado() : negado);
        using var _ = adapter;

        costura.OrigemADevolver = (byte)KnownEventOrigin.QrCode;
        costura.CartaoADevolver = "0000000101";
        Ate(laco, catraca, autorizado ? DeviceState.MonitoraGiroCatraca : DeviceState.EnviarMsgAcessoNegado);

        // Autorizado: a pessoa gira; negado: nada mais chega. Depois, silêncio até voltar a Polling.
        costura.OrigemADevolver = autorizado ? (byte)KnownEventOrigin.GiroConfirmado : (byte)0;
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        costura.OrigemADevolver = 0;
        for (var i = 0; i < 5; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.DoesNotContain(costura.Chamadas, c => c.StartsWith("AcionarBip", StringComparison.Ordinal));
        Assert.Contains(autorizado ? "LiberarCatracaEntrada" : "EnviarMensagemTemporariaOnLine", costura.Chamadas);
    }

    /// <summary>
    /// A regressão exata do F1: com o perfil "entrada invertida", a saída invertida nunca é
    /// chamada — nem por ingresso, nem pelo operador.
    /// </summary>
    [Fact]
    public void Perfil_invertido_nunca_chega_a_saida_invertida()
    {
        var (costura, laco, catraca, adapter) = EmOperacao(FuncaoDeLiberacao.EntradaInvertida);
        using var _ = adapter;

        costura.OrigemADevolver = (byte)KnownEventOrigin.Leitor1;
        costura.CartaoADevolver = "0000000101";
        Ate(laco, catraca, DeviceState.MonitoraGiroCatraca);

        Assert.Contains("LiberarCatracaEntradaInvertida", costura.Chamadas);
        Assert.DoesNotContain("LiberarCatracaSaidaInvertida", costura.Chamadas);
        Assert.DoesNotContain("LiberarCatracaSaida", costura.Chamadas);
    }
}
