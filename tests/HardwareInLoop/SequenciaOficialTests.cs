using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;
using Topdata.EasyInner.Interop;

namespace HardwareInLoop.Tests;

/// <summary>
/// Etapa A.7 do docs/35: a sequência oficial de conexão (cfg off-line → mudança automática →
/// cfg on-line, EI-029), atrás da chave técnica <c>catraca.sequencia_oficial</c>.
/// </summary>
/// <remarks>
/// <para>
/// Com a chave, cada chamada à DLL — função e argumentos, na ordem — é a do docs/34 §4.3, provada
/// pela costura falsa (<see cref="CosturaFalsa.ChamadasComArgumentos"/>), no adapter e de ponta a
/// ponta (laço + adapter + costura) até <see cref="DeviceState.Polling"/>. Sem a chave, a sequência
/// de hoje, byte a byte (defeito F3, docs/34 §2).
/// </para>
/// <para>
/// O que fica fora, sem linha na matriz FUN: mensagens off-line e <c>EnviarMensagensOffLine</c>,
/// entradas e mensagens da mudança, e o <c>PingOnLine</c> periódico (T24). A chave não liga a
/// contingência: a mudança automática vai com o valor da configuração, 0 no padrão (D8).
/// </para>
/// </remarks>
public sealed class SequenciaOficialTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A costura falsa se apresenta como linha 4; o teste a homologa para chegar à operação.</summary>
    private static readonly HashSet<byte> LinhaDaCosturaFalsa = [4];

    private const int Inner = 1;

    /// <summary>A configuração de hoje: o padrão de fábrica montado sem sobreposições (Etapa A.1).</summary>
    private static DeviceConfiguration DeHoje() => MontadorDaConfiguracao.Montar(
        PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma);

    private static object[] Chamada(string funcao, params object[] argumentos) => [funcao, .. argumentos];

    /// <summary>Os campos comuns da configuração de hoje, iguais nos dois envios (docs/34 §4.3).</summary>
    private static readonly object[][] CamposComunsDeHoje =
    [
        Chamada(nameof(IEasyInnerNative.DefinirPadraoCartao), (byte)1),
        Chamada(nameof(IEasyInnerNative.ConfigurarTipoLeitor), (byte)8),
        Chamada(nameof(IEasyInnerNative.ConfigurarLeitor1), (byte)1),
        Chamada(nameof(IEasyInnerNative.ConfigurarLeitor2), (byte)1),
        Chamada(nameof(IEasyInnerNative.ConfigurarAcionamento1), (byte)2, (byte)5),
        Chamada(nameof(IEasyInnerNative.ConfigurarAcionamento2), (byte)0, (byte)0),
        Chamada(nameof(IEasyInnerNative.HabilitarTeclado), (byte)0, (byte)0),
    ];

    /// <summary>3 cfg off-line: <c>ConfigurarInnerOffLine</c> + campos comuns → <c>EnviarConfiguracoes</c>.</summary>
    private static readonly object[][] CfgOffLineDeHoje =
    [
        Chamada(nameof(IEasyInnerNative.ConfigurarInnerOffLine)),
        .. CamposComunsDeHoje,
        Chamada(nameof(IEasyInnerNative.EnviarConfiguracoes), Inner),
    ];

    /// <summary>5 mudança: EI-028 com o padrão (0 = sem contingência, 10) → EI-029.</summary>
    private static readonly object[][] MudancaDeHoje =
    [
        Chamada(nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), (byte)0, (byte)10),
        Chamada(nameof(IEasyInnerNative.EnviarConfiguracoesMudancaAutomaticaOnLineOffLine), Inner),
    ];

    /// <summary>6 cfg on-line: <c>ConfigurarInnerOnLine</c> + os mesmos campos comuns → <c>EnviarConfiguracoes</c>.</summary>
    private static readonly object[][] CfgOnLineDeHoje =
    [
        Chamada(nameof(IEasyInnerNative.ConfigurarInnerOnLine)),
        .. CamposComunsDeHoje,
        Chamada(nameof(IEasyInnerNative.EnviarConfiguracoes), Inner),
    ];

    /// <summary>A montagem de hoje, num envio só, como sai três vezes sem a chave (F3).</summary>
    private static readonly object[][] MontagemCompletaDeHoje =
    [
        Chamada(nameof(IEasyInnerNative.DefinirPadraoCartao), (byte)1),
        Chamada(nameof(IEasyInnerNative.ConfigurarTipoLeitor), (byte)8),
        Chamada(nameof(IEasyInnerNative.ConfigurarLeitor1), (byte)1),
        Chamada(nameof(IEasyInnerNative.ConfigurarLeitor2), (byte)1),
        Chamada(nameof(IEasyInnerNative.ConfigurarAcionamento1), (byte)2, (byte)5),
        Chamada(nameof(IEasyInnerNative.ConfigurarAcionamento2), (byte)0, (byte)0),
        Chamada(nameof(IEasyInnerNative.ConfigurarInnerOnLine)),
        Chamada(nameof(IEasyInnerNative.HabilitarTeclado), (byte)0, (byte)0),
        Chamada(nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), (byte)0, (byte)10),
        Chamada(nameof(IEasyInnerNative.EnviarConfiguracoes), Inner),
    ];

    /// <summary>1 conectar e 2 identidade, antes da configuração.</summary>
    private static readonly object[][] ConectarEIdentificar =
    [
        Chamada(nameof(IEasyInnerNative.Ping), Inner),
        Chamada(nameof(IEasyInnerNative.ReceberVersaoFirmware), Inner),
    ];

    /// <summary>8 entradas on-line (o rearme de sempre) e 9 mensagem padrão.</summary>
    private static readonly object[][] RearmarEMensagem =
    [
        Chamada(nameof(IEasyInnerNative.EnviarFormasEntradasOnLine), Inner, (byte)0, (byte)0, (byte)7, (byte)0, (byte)0),
        Chamada(nameof(IEasyInnerNative.EnviarMensagemPadraoOnLine), Inner, (byte)0, "Aproxime o ingresso"),
    ];

    private static List<object[]> Planas(CosturaFalsa costura) =>
        [.. costura.ChamadasComArgumentos.Select(c => Chamada(c.Funcao, c.Argumentos))];

    private static void Iguais(IReadOnlyList<object[]> esperado, List<object[]> obtido)
    {
        var texto = string.Join(" · ", obtido.Select(c => $"{c[0]}({string.Join(", ", c.Skip(1))})"));
        Assert.True(esperado.Count == obtido.Count, $"Esperava {esperado.Count} chamadas, vieram {obtido.Count}: {texto}");
        for (var i = 0; i < esperado.Count; i++)
        {
            Assert.True(esperado[i].SequenceEqual(obtido[i]), $"Chamada {i}: esperava {string.Join(", ", esperado[i])}; veio {texto}");
        }
    }

    /// <summary>Uma etapa, só ela, pelo adapter de verdade.</summary>
    private static (AdapterResult Resultado, CosturaFalsa Costura) Etapa(
        DeviceConfiguration configuracao, EtapaDaSequenciaOficial etapa, Action<CosturaFalsa>? preparar = null)
    {
        var costura = new CosturaFalsa();
        preparar?.Invoke(costura);
        using var adapter = new TopdataInnerAdapter(costura);
        return (adapter.EnviarEtapaDaSequenciaOficial(Inner, configuracao, etapa), costura);
    }

    /// <summary>Conecta uma catraca até Polling pelo laço de verdade e devolve o laço e a catraca.</summary>
    private static (DevicePump Laco, DeviceSlot Catraca) Conectar(
        TopdataInnerAdapter adapter, DeviceConfiguration configuracao, bool sequenciaOficial)
    {
        var laco = new DevicePump(adapter, () => Agora, LinhaDaCosturaFalsa, sequenciaOficial: sequenciaOficial);
        var catraca = new DeviceSlot(Inner, configuracao, () => Agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        return (laco, catraca);
    }

    [Fact]
    public void Com_a_chave_a_cfg_off_line_e_off_line_com_os_campos_comuns()
    {
        var (resultado, costura) = Etapa(DeHoje(), EtapaDaSequenciaOficial.ConfiguracaoOffLine);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal(nameof(IEasyInnerNative.EnviarConfiguracoes), resultado.Funcao);
        Iguais(CfgOffLineDeHoje, Planas(costura));
    }

    [Fact]
    public void Com_a_chave_a_mudanca_vai_por_ei_029_sem_ligar_a_contingencia()
    {
        var (resultado, costura) = Etapa(DeHoje(), EtapaDaSequenciaOficial.MudancaAutomatica);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal(nameof(IEasyInnerNative.EnviarConfiguracoesMudancaAutomaticaOnLineOffLine), resultado.Funcao);
        Iguais(MudancaDeHoje, Planas(costura));

        // 0 é o padrão de fábrica: a chave muda a forma de envio, não decide a D8.
        Assert.Equal(0, DeHoje().MudancaAutomatica);
    }

    [Fact]
    public void Com_a_chave_a_cfg_on_line_leva_os_mesmos_campos_comuns()
    {
        var (resultado, costura) = Etapa(DeHoje(), EtapaDaSequenciaOficial.ConfiguracaoOnLine);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Iguais(CfgOnLineDeHoje, Planas(costura));
    }

    /// <summary>
    /// <c>Online</c> é o regime alvo: a cfg off-line é off-line sempre; a cfg on-line segue o
    /// regime em que a catraca termina.
    /// </summary>
    [Fact]
    public void Regime_alvo_off_line_termina_off_line_e_a_cfg_off_line_nao_muda()
    {
        var offLine = DeHoje() with { Online = false };
        Assert.Equal(RegimeAlvo.OffLine, offLine.RegimeAlvo());
        Assert.Equal(RegimeAlvo.OnLine, DeHoje().RegimeAlvo());

        var (_, cfgOffLine) = Etapa(offLine, EtapaDaSequenciaOficial.ConfiguracaoOffLine);
        Iguais(CfgOffLineDeHoje, Planas(cfgOffLine));

        var (_, cfgFinal) = Etapa(offLine, EtapaDaSequenciaOficial.ConfiguracaoOnLine);
        Iguais([Chamada(nameof(IEasyInnerNative.ConfigurarInnerOffLine)), .. CamposComunsDeHoje,
                Chamada(nameof(IEasyInnerNative.EnviarConfiguracoes), Inner)], Planas(cfgFinal));
    }

    /// <summary>
    /// Com as chaves da Etapa A.2 ligadas, os campos delas vão nos <b>dois</b> envios, na mesma
    /// ordem: cada <c>EnviarConfiguracoes</c> limpa o buffer e manda o padrão da DLL para o que
    /// faltar (ADR-0020). O tipo de lista fica no fim dos dois (anexo 01 §3.3).
    /// </summary>
    [Fact]
    public void Com_as_chaves_da_a2_os_dois_envios_levam_os_mesmos_campos()
    {
        var configuracao = DeHoje() with
        {
            QuantidadesVariaveisDeDigitos = [4, 16],
            EnviarDigitosVariaveis = true,
            WiegandDoisLeitores = new WiegandDoisLeitores(false, false),
            EnviarWiegandDoisLeitores = true,
            RegistrarAcessoNegado = 1,
            EnviarDataHoraNoEventoOnLine = true,
            CartaoMaster = new CodigoDoCartaoMaster("99990000000101"),
            EnviarTipoDeLista = true,
        };
        Assert.Empty(configuracao.Validar());

        object[][] comuns =
        [
            Chamada(nameof(IEasyInnerNative.DefinirPadraoCartao), (byte)1),
            Chamada(nameof(IEasyInnerNative.InserirQuantidadeDigitoVariavel), (byte)4),
            Chamada(nameof(IEasyInnerNative.InserirQuantidadeDigitoVariavel), (byte)16),
            Chamada(nameof(IEasyInnerNative.ConfigurarTipoLeitor), (byte)8),
            Chamada(nameof(IEasyInnerNative.ConfigurarLeitor1), (byte)1),
            Chamada(nameof(IEasyInnerNative.ConfigurarLeitor2), (byte)1),
            Chamada(nameof(IEasyInnerNative.ConfigurarAcionamento1), (byte)2, (byte)5),
            Chamada(nameof(IEasyInnerNative.ConfigurarAcionamento2), (byte)0, (byte)0),
            Chamada(nameof(IEasyInnerNative.HabilitarTeclado), (byte)0, (byte)0),
            Chamada(nameof(IEasyInnerNative.ConfigurarWiegandDoisLeitores), (byte)0, (byte)0),
            Chamada(nameof(IEasyInnerNative.RegistrarAcessoNegado), (byte)1),
            Chamada(nameof(IEasyInnerNative.ReceberDataHoraDadosOnLine), (byte)1),
            Chamada(nameof(IEasyInnerNative.DefinirNumeroCartaoMaster), "99990000000101"),
            Chamada(nameof(IEasyInnerNative.DefinirTipoListaAcesso), (byte)0),
        ];

        var (_, offLine) = Etapa(configuracao, EtapaDaSequenciaOficial.ConfiguracaoOffLine);
        Iguais([Chamada(nameof(IEasyInnerNative.ConfigurarInnerOffLine)), .. comuns,
                Chamada(nameof(IEasyInnerNative.EnviarConfiguracoes), Inner)], Planas(offLine));

        var (_, onLine) = Etapa(configuracao, EtapaDaSequenciaOficial.ConfiguracaoOnLine);
        Iguais([Chamada(nameof(IEasyInnerNative.ConfigurarInnerOnLine)), .. comuns,
                Chamada(nameof(IEasyInnerNative.EnviarConfiguracoes), Inner)], Planas(onLine));

        // A mudança não leva campo comum nenhum.
        var (_, mudanca) = Etapa(configuracao, EtapaDaSequenciaOficial.MudancaAutomatica);
        Iguais(MudancaDeHoje, Planas(mudanca));
    }

    /// <summary>
    /// O laço de verdade, o adapter de verdade e a costura falsa, com a chave ligada: da conexão
    /// a Polling, a sequência do docs/34 §4.3 exata — função, argumentos e ordem.
    /// </summary>
    /// <remarks>
    /// O relógio (passo 7 do §4.3) é acertado no primeiro passo em Polling, como sempre: relógio
    /// e comandos só em Polling (regra do docs/35; anexo 01 §3.3: "mover para cá é equivalente").
    /// O passo 4 (lista) não chama a DLL: não há lista na catraca (Etapa D).
    /// </remarks>
    [Fact]
    public void Ponta_a_ponta_com_a_chave_a_sequencia_do_4_3_ate_polling()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var (laco, catraca) = Conectar(adapter, DeHoje(), sequenciaOficial: true);

        Iguais(
            [.. ConectarEIdentificar, .. CfgOffLineDeHoje, .. MudancaDeHoje, .. CfgOnLineDeHoje, .. RearmarEMensagem],
            Planas(costura));

        // As transições são as de sempre: a máquina de estados (tabela de dados) não mudou.
        Assert.Equal(
            [
                DeviceState.EnviarCfgOffline, DeviceState.EnviarConfigMudOnlineOffline, DeviceState.EnviarCfgOnline,
                DeviceState.SincronizandoDadosOffline, DeviceState.ConfigurarEntradasOnline, DeviceState.EnviarMsgPadrao,
                DeviceState.Polling,
            ],
            catraca.Maquina.History.Select(h => h.To).SkipWhile(e => e != DeviceState.EnviarCfgOffline));

        // Primeiro passo em Polling: o relógio (7), depois a recepção (10). Sem PingOnLine: a
        // mudança é 0 (T24, D8).
        var antes = costura.Chamadas.Count;
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(
            [nameof(IEasyInnerNative.EnviarRelogio), nameof(IEasyInnerNative.ReceberDadosOnLine)],
            costura.Chamadas.Skip(antes));
        Assert.DoesNotContain(nameof(IEasyInnerNative.PingOnLine), costura.Chamadas);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
    }

    /// <summary>
    /// Sem a chave (o padrão do laço), a sequência de hoje, byte a byte: três envios completos
    /// iguais, sem EI-029 (F3).
    /// </summary>
    [Fact]
    public void Sem_a_chave_a_sequencia_de_hoje_byte_a_byte()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        Conectar(adapter, DeHoje(), sequenciaOficial: false);

        Iguais(
            [.. ConectarEIdentificar, .. MontagemCompletaDeHoje, .. MontagemCompletaDeHoje, .. MontagemCompletaDeHoje,
             .. RearmarEMensagem],
            Planas(costura));

        // E o construtor sem o parâmetro é o mesmo que desligado.
        var padrao = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapterPadrao = new TopdataInnerAdapter(padrao);
        var laco = new DevicePump(adapterPadrao, () => Agora, LinhaDaCosturaFalsa);
        var catraca = new DeviceSlot(Inner, DeHoje(), () => Agora);
        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Iguais(Planas(costura), Planas(padrao));
        Assert.DoesNotContain(nameof(IEasyInnerNative.EnviarConfiguracoesMudancaAutomaticaOnLineOffLine), padrao.Chamadas);
    }

    /// <summary>Em cada etapa, nada com Inner entre a primeira função de montagem e o enviador (ADR-0006).</summary>
    [Theory]
    [InlineData(EtapaDaSequenciaOficial.ConfiguracaoOffLine)]
    [InlineData(EtapaDaSequenciaOficial.MudancaAutomatica)]
    [InlineData(EtapaDaSequenciaOficial.ConfiguracaoOnLine)]
    public void Nada_com_inner_entre_montar_e_enviar_em_cada_etapa(EtapaDaSequenciaOficial etapa)
    {
        var comInner = typeof(IEasyInnerNative).GetMethods()
            .Where(m => m.GetParameters() is [{ Name: "inner" }, ..])
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(IEasyInnerNative.EnviarConfiguracoesMudancaAutomaticaOnLineOffLine), comInner);

        var (_, costura) = Etapa(DeHoje() with { QuantidadesVariaveisDeDigitos = [4, 16], EnviarDigitosVariaveis = true }, etapa);

        // Só a última chamada fala com a catraca: é o enviador da etapa.
        Assert.DoesNotContain(costura.Chamadas.Take(costura.Chamadas.Count - 1), comInner.Contains);
        Assert.Contains(costura.Chamadas[^1], comInner);
    }

    /// <summary>
    /// Passo recusado: volta com a função que recusou, a montagem para ali (nada mais no buffer)
    /// e o enviador não é chamado — enviar aplicaria uma configuração pela metade (F7).
    /// </summary>
    [Fact]
    public void Passo_recusado_para_a_montagem_e_nao_envia()
    {
        var (resultado, costura) = Etapa(
            DeHoje(),
            EtapaDaSequenciaOficial.ConfiguracaoOffLine,
            c => c.Retornos[nameof(IEasyInnerNative.ConfigurarLeitor2)] = 129);

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Equal(nameof(IEasyInnerNative.ConfigurarLeitor2), resultado.Funcao);
        Assert.Equal(nameof(IEasyInnerNative.ConfigurarLeitor2), costura.Chamadas[^1]);
        Assert.DoesNotContain(nameof(IEasyInnerNative.EnviarConfiguracoes), costura.Chamadas);
    }

    /// <summary>Tempo inválido na mudança (129, FUN:29) é recusa documentada, e EI-029 não sai.</summary>
    [Fact]
    public void Mudanca_recusada_diz_a_funcao_e_nao_chama_ei_029()
    {
        var (resultado, costura) = Etapa(
            DeHoje(),
            EtapaDaSequenciaOficial.MudancaAutomatica,
            c => c.Retornos[nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine)] = 129);

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Equal(nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), resultado.Funcao);
        Assert.DoesNotContain(nameof(IEasyInnerNative.EnviarConfiguracoesMudancaAutomaticaOnLineOffLine), costura.Chamadas);
    }

    /// <summary>EI-029 com erro (1, FUN:30) é erro, e o laço reconecta em vez de seguir para a cfg on-line.</summary>
    [Fact]
    public void Erro_no_ei_029_leva_o_laco_a_reconectar()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        costura.Retornos[nameof(IEasyInnerNative.EnviarConfiguracoesMudancaAutomaticaOnLineOffLine)] = 1;
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => Agora, LinhaDaCosturaFalsa, sequenciaOficial: true);
        var catraca = new DeviceSlot(Inner, DeHoje(), () => Agora);

        for (var i = 0; i < 10 && catraca.Maquina.Current != DeviceState.Reconectar; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);
        Assert.Equal(DeviceState.EnviarConfigMudOnlineOffline, catraca.Maquina.History.Last().From);
        Assert.Equal(1, costura.Chamadas.Count(c => c == nameof(IEasyInnerNative.EnviarConfiguracoes)));
        Assert.DoesNotContain(nameof(IEasyInnerNative.ConfigurarInnerOnLine), costura.Chamadas);
    }

    /// <summary>Configuração inválida e etapa desconhecida são recusadas antes de qualquer chamada nativa.</summary>
    [Fact]
    public void Recusa_antes_da_dll()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        Assert.Throws<ArgumentException>(() => adapter.EnviarEtapaDaSequenciaOficial(
            Inner, DeHoje() with { TipoDeLista = 1, EnviarTipoDeLista = true }, EtapaDaSequenciaOficial.ConfiguracaoOffLine));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            adapter.EnviarEtapaDaSequenciaOficial(Inner, DeHoje(), (EtapaDaSequenciaOficial)0));
        Assert.Empty(costura.Chamadas);
    }
}
