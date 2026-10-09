using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// Etapa I.1b do docs/36 (capacidade C1, achado F9 do docs/36-anexos/01): o prazo do estado
/// <see cref="DeviceState.MonitoraGiroCatraca"/> passa a valer.
/// </summary>
/// <remarks>
/// <para>
/// O prazo estava na tabela da máquina e a transição <c>TempoEsgotado</c> também, mas nada no
/// laço a disparava: se a origem 5 (fim do tempo de acionamento) não chegasse depois de uma
/// liberação, a pista ficava em <c>MonitoraGiroCatraca</c> para sempre, sem rearmar o leitor e
/// sem atender ninguém — e sem erro. Aqui se prova, com o laço de verdade, o adapter de verdade
/// e a DLL falsa, que o laço volta a <c>Polling</c> rearmando o leitor pela chamada nativa exata.
/// </para>
/// <para>
/// A origem 5 continua sendo o caminho normal; o prazo é a rede de segurança. Não há
/// comportamento novo da catraca: a costura falsa simplesmente não manda nada.
/// </para>
/// </remarks>
public sealed class PrazoDoGiroTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);

    /// <summary>A costura falsa se apresenta como linha 4; o teste a homologa para chegar à operação.</summary>
    private static readonly HashSet<byte> LinhaDaCosturaFalsa = [4];

    private DateTimeOffset _agora = Inicio;

    private static DeviceConfiguration Configuracao(byte tempoDoRele1 = 5) => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = tempoDoRele1,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 0,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Aproxime o ingresso",
        PerfilFisico = GatePhysicalProfile.Padrao,
    };

    private static Decision Autorizado() => new(
        DecisionOutcome.Allowed,
        ReasonCodes.Autorizado,
        DegradationTier.T1SemInternet,
        TimeSpan.FromMilliseconds(3),
        []);

    private static string Passo(DevicePump laco, DeviceSlot catraca) => laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

    private static void Ate(DevicePump laco, DeviceSlot catraca, DeviceState alvo)
    {
        for (var i = 0; i < 40 && catraca.Maquina.Current != alvo; i++)
        {
            Passo(laco, catraca);
        }

        Assert.Equal(alvo, catraca.Maquina.Current);
    }

    /// <summary>Leva a catraca até Polling, com o relógio já acertado, e limpa o registro.</summary>
    private (CosturaFalsa Costura, DevicePump Laco, DeviceSlot Catraca, TopdataInnerAdapter Adapter) EmOperacao(
        byte tempoDoRele1 = 5)
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => _agora, LinhaDaCosturaFalsa, decidir: _ => Autorizado());
        var catraca = new DeviceSlot(1, Configuracao(tempoDoRele1), () => _agora);

        Ate(laco, catraca, DeviceState.Polling);

        // O primeiro passo em Polling acerta o relógio; o seguinte já espera evento.
        Passo(laco, catraca);
        Assert.Contains("EnviarRelogio", costura.Chamadas);

        costura.Chamadas.Clear();
        costura.ChamadasComArgumentos.Clear();
        return (costura, laco, catraca, adapter);
    }

    /// <summary>Uma leitura de QR autorizada, até a liberação; depois disso a costura fica muda.</summary>
    private static void LiberarPorLeitura(CosturaFalsa costura, DevicePump laco, DeviceSlot catraca)
    {
        costura.OrigemADevolver = (byte)KnownEventOrigin.QrCode;
        costura.CartaoADevolver = "0000000101";
        Passo(laco, catraca);
        Assert.Equal(DeviceState.ValidarAcesso, catraca.Maquina.Current);

        // Nem origem 5 nem origem 6 daqui em diante: a catraca "esqueceu" de avisar.
        costura.OrigemADevolver = 0;
        costura.CartaoADevolver = string.Empty;

        Ate(laco, catraca, DeviceState.MonitoraGiroCatraca);
        Assert.Contains("LiberarCatracaEntrada", costura.Chamadas);
    }

    /// <summary>
    /// Achado E1-05 do docs/41: quando a chamada de liberação falhava depois da autorização, a
    /// catraca ia para reconexão e a tentativa pendente ficava aberta. Um giro que chegasse depois
    /// confirmaria a passagem errada. Agora a falha encerra a tentativa, como o prazo já fazia.
    /// </summary>
    [Fact]
    public void Liberacao_que_a_dll_recusa_encerra_a_tentativa_pendente()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var desistencias = new List<string>();
        var causas = new List<(string Catraca, string Causa)>();
        var laco = new DevicePump(
            adapter, () => _agora, LinhaDaCosturaFalsa, decidir: _ => Autorizado(), aoDesistirDoGiro: desistencias.Add,
            aoFalharALiberacao: (c, causa) => causas.Add((c, causa)));
        var catraca = new DeviceSlot(1, Configuracao(), () => _agora);
        Ate(laco, catraca, DeviceState.Polling);
        Passo(laco, catraca);

        costura.Retornos["LiberarCatracaEntrada"] = 1;
        costura.OrigemADevolver = (byte)KnownEventOrigin.QrCode;
        costura.CartaoADevolver = "0000000101";
        Passo(laco, catraca);
        costura.OrigemADevolver = 0;
        costura.CartaoADevolver = string.Empty;

        string? feito = null;
        for (var i = 0; i < 10 && feito?.Contains("falha ao liberar giro", StringComparison.Ordinal) != true; i++)
        {
            feito = Passo(laco, catraca);
        }

        Assert.Contains("falha ao liberar giro", feito, StringComparison.Ordinal);
        Assert.Equal([catraca.Maquina.DeviceId], desistencias);

        // A causa vai junto, para a tentativa consumida ficar no painel do estorno (E1-05).
        var (comCausa, causa) = Assert.Single(causas);
        Assert.Equal(catraca.Maquina.DeviceId, comCausa);
        Assert.Contains("não aceitou a liberação", causa, StringComparison.Ordinal);
    }

    [Fact]
    public void Liberacao_sem_origem_5_nem_6_volta_a_atender_depois_do_prazo_rearmando_o_leitor()
    {
        var (costura, laco, catraca, adapter) = EmOperacao();
        using var _ = adapter;

        LiberarPorLeitura(costura, laco, catraca);
        var liberadaEm = _agora;

        // Antes do prazo (8 s da tabela; relé de 5 s + margem de 3 s dá o mesmo), espera o giro.
        _agora = liberadaEm + TimeSpan.FromSeconds(7.9);
        Passo(laco, catraca);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);

        // No prazo: uma espera sem evento, e só então desiste — com o motivo no registro.
        costura.Chamadas.Clear();
        _agora = liberadaEm + TimeSpan.FromSeconds(8);
        var feito = Passo(laco, catraca);
        Assert.Contains("giro não confirmado: prazo de 8 s", feito, StringComparison.Ordinal);
        Assert.Equal(DeviceState.ConfigurarEntradasOnline, catraca.Maquina.Current);
        Assert.Equal(["ReceberDadosOnLine"], costura.Chamadas);

        // O caminho de volta é o mesmo da origem 5: rearmar o leitor e repor a mensagem.
        Ate(laco, catraca, DeviceState.Polling);
        Assert.Equal(["ReceberDadosOnLine", "EnviarFormasEntradasOnLine", "EnviarMensagemPadraoOnLine"], costura.Chamadas);

        // A próxima pessoa é atendida.
        costura.Chamadas.Clear();
        LiberarPorLeitura(costura, laco, catraca);
        Assert.Equal(1, costura.Chamadas.Count(c => c == "LiberarCatracaEntrada"));
    }

    /// <summary>
    /// O relé 1 fica liberado pelo tempo configurado (0 a 50 s, EI-016). Com 20 s, o prazo fixo
    /// de 8 s cortaria a espera no meio da janela do giro; o prazo efetivo é 20 s + 3 s.
    /// </summary>
    [Fact]
    public void Rele_de_20_s_nao_e_cortado_antes_de_20_s_mais_a_margem()
    {
        var (costura, laco, catraca, adapter) = EmOperacao(tempoDoRele1: 20);
        using var _ = adapter;

        LiberarPorLeitura(costura, laco, catraca);
        var liberadaEm = _agora;

        foreach (var segundos in new[] { 8.0, 15.0, 20.0, 22.9 })
        {
            _agora = liberadaEm + TimeSpan.FromSeconds(segundos);
            Assert.Equal("sem eventos", Passo(laco, catraca));
            Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
        }

        _agora = liberadaEm + TimeSpan.FromSeconds(23);
        Assert.Contains("giro não confirmado: prazo de 23 s", Passo(laco, catraca), StringComparison.Ordinal);
        Assert.Equal(DeviceState.ConfigurarEntradasOnline, catraca.Maquina.Current);
    }

    /// <summary>
    /// O caminho de hoje não muda: a origem 5 dentro do prazo sai de MonitoraGiro pelo gatilho
    /// dela, rearma o leitor pelas mesmas chamadas, e nenhum prazo velho dispara depois.
    /// </summary>
    [Fact]
    public void Origem_5_dentro_do_prazo_segue_o_caminho_de_hoje()
    {
        var (costura, laco, catraca, adapter) = EmOperacao();
        using var _ = adapter;

        LiberarPorLeitura(costura, laco, catraca);
        var liberadaEm = _agora;
        costura.Chamadas.Clear();

        _agora = liberadaEm + TimeSpan.FromSeconds(5);
        costura.OrigemADevolver = (byte)KnownEventOrigin.FimTempoAcionamento;
        var feito = Passo(laco, catraca);
        costura.OrigemADevolver = 0;

        Assert.DoesNotContain("giro não confirmado", feito, StringComparison.Ordinal);
        Assert.Equal(DeviceTrigger.TempoDeAcionamentoEsgotado, catraca.Maquina.History.Last().Trigger);
        Ate(laco, catraca, DeviceState.Polling);
        Assert.Equal(["ReceberDadosOnLine", "EnviarFormasEntradasOnLine", "EnviarMensagemPadraoOnLine"], costura.Chamadas);

        // Em Polling não há prazo: o tempo passa e a catraca segue esperando leitura (a
        // conferência do relógio, que vence no caminho, é um passo em Polling como outro).
        _agora = liberadaEm + TimeSpan.FromMinutes(5);
        for (var i = 0; i < 3; i++)
        {
            Passo(laco, catraca);
            Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        }

        Assert.DoesNotContain(catraca.Maquina.History, t => t.Trigger is DeviceTrigger.TempoEsgotado);
    }

    /// <summary>
    /// O laço só desiste depois de uma espera que voltou vazia. Com a volta lenta (outras
    /// catracas, chamadas demoradas), o giro que já está na fila da DLL é lido antes — e conta
    /// como giro, não como desistência.
    /// </summary>
    [Fact]
    public void Giro_que_ja_esta_na_fila_quando_o_prazo_passa_e_lido_e_nao_cortado()
    {
        var (costura, laco, catraca, adapter) = EmOperacao();
        using var _ = adapter;

        LiberarPorLeitura(costura, laco, catraca);

        _agora += TimeSpan.FromSeconds(30);
        costura.OrigemADevolver = (byte)KnownEventOrigin.GiroConfirmado;
        var feito = Passo(laco, catraca);
        costura.OrigemADevolver = 0;

        Assert.DoesNotContain("giro não confirmado", feito, StringComparison.Ordinal);
        Assert.Equal(DeviceTrigger.GiroConfirmado, catraca.Maquina.History.Last().Trigger);
        Assert.Equal(DeviceState.ConfigurarEntradasOnline, catraca.Maquina.Current);
    }

    /// <summary>
    /// O prazo de 150 ms de ValidarAcesso é orçamento da decisão, e o destino dele na tabela é
    /// liberar. O laço nunca o aplica: sem motor de decisão, a catraca espera — não libera.
    /// </summary>
    [Fact]
    public void Validar_acesso_sem_decisao_nunca_libera_pelo_prazo()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => _agora, LinhaDaCosturaFalsa, decidir: null);
        var catraca = new DeviceSlot(1, Configuracao(), () => _agora);
        Ate(laco, catraca, DeviceState.Polling);
        Passo(laco, catraca);

        costura.OrigemADevolver = (byte)KnownEventOrigin.QrCode;
        costura.CartaoADevolver = "0000000101";
        Passo(laco, catraca);
        costura.OrigemADevolver = 0;
        Assert.Equal(DeviceState.ValidarAcesso, catraca.Maquina.Current);
        costura.Chamadas.Clear();

        for (var i = 0; i < 5; i++)
        {
            _agora += TimeSpan.FromSeconds(1);
            Assert.Equal("aguardando motor de decisão (nenhum configurado)", Passo(laco, catraca));
        }

        Assert.Equal(DeviceState.ValidarAcesso, catraca.Maquina.Current);
        Assert.Empty(costura.Chamadas);
    }

    /// <summary>
    /// Estado de configuração preso (o passo não pode chamar: disjuntor aberto) estoura o prazo
    /// da tabela e segue o caminho de falha que já existia — reconectar —, e a catraca volta.
    /// </summary>
    [Fact]
    public void Prazo_de_estado_de_configuracao_estourado_vai_para_a_reconexao()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => _agora, LinhaDaCosturaFalsa, decidir: _ => Autorizado());
        var catraca = new DeviceSlot(1, Configuracao(), () => _agora);

        Ate(laco, catraca, DeviceState.EnviarCfgOnline);
        var entrouEm = _agora;

        _agora = entrouEm + TimeSpan.FromSeconds(5);
        for (var i = 0; i < 5; i++)
        {
            catraca.Disjuntor.RegistrarFalha();
        }

        costura.Chamadas.Clear();
        _agora = entrouEm + TimeSpan.FromSeconds(29);
        Assert.Equal("disjuntor aberto", Passo(laco, catraca));
        Assert.Equal(DeviceState.EnviarCfgOnline, catraca.Maquina.Current);

        _agora = entrouEm + TimeSpan.FromSeconds(30);
        Assert.Equal("prazo de 30 s em EnviarCfgOnline esgotado — Reconectar (disjuntor aberto)", Passo(laco, catraca));
        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);
        Assert.Empty(costura.Chamadas);

        // Fechado o disjuntor (meio aberto depois de 30 s), a reconexão leva de volta à operação.
        _agora = entrouEm + TimeSpan.FromSeconds(36);
        Ate(laco, catraca, DeviceState.Polling);
    }

    /// <summary>
    /// A vez da catraca no laço não conta contra os estados de uma chamada só. Numa reconexão em
    /// massa a volta passa do prazo de cada estado; aplicar o prazo no começo do passo mandaria
    /// a catraca sadia para a quarentena (identidade, 5 s) ou num ciclo de reconexão sem fim.
    /// </summary>
    [Fact]
    public void Volta_lenta_do_laco_nao_derruba_a_conexao_que_esta_dando_certo()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => _agora, LinhaDaCosturaFalsa, decidir: _ => Autorizado());
        var catraca = new DeviceSlot(1, Configuracao(), () => _agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            // Cada volta leva mais que o maior prazo de configuração (30 s).
            _agora += TimeSpan.FromSeconds(40);
            Passo(laco, catraca);
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.DoesNotContain(catraca.Maquina.History, t => t.Trigger is DeviceTrigger.TempoEsgotado);
        Assert.DoesNotContain(catraca.Maquina.History, t => t.To is DeviceState.Quarentena or DeviceState.Reconectar);
    }
}
