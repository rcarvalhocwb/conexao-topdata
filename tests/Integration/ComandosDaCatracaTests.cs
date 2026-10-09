using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Fase 4b: comandos do operador executados pelo laço, só com a catraca livre (Polling),
/// e com o desfecho sempre informado.
/// </summary>
public sealed class ComandosDaCatracaTests : IDisposable
{
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private readonly InnerSimulator _sim;
    private readonly List<(ComandoDeCatraca Comando, SituacaoDoComando Situacao, string Resultado)> _desfechos = [];
    private readonly List<string> _pendentesDescartados = [];
    private DateTimeOffset _agora = Inicio;

    public ComandosDaCatracaTests()
    {
        _sim = new InnerSimulator(() => _agora);
        _sim.AbrirPorta(3570);
    }

    public void Dispose() => _sim.Dispose();

    private static DeviceConfiguration Configuracao(string mensagem = "Bem-vindo") => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 3,
        OperacaoDoLeitor1 = 3,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 2,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = mensagem,
        PerfilFisico = GatePhysicalProfile.Padrao,
    };

    private static Decision Autorizado(DeviceEvent _) => new(
        DecisionOutcome.Allowed, ReasonCodes.Autorizado, DegradationTier.T1SemInternet, TimeSpan.FromMilliseconds(5), []);

    /// <summary>Monta e leva a catraca até Polling, com o relógio já acertado.</summary>
    private (DevicePump Bomba, DeviceSlot Catraca) EmOperacao()
    {
        var bomba = new DevicePump(
            _sim,
            () => _agora,
            decidir: Autorizado,
            aoConcluirComando: (c, s, r) => _desfechos.Add((c, s, r)),
            antesDaLiberacaoManual: _pendentesDescartados.Add);
        var catraca = new DeviceSlot(1, Configuracao(), () => _agora);

        for (var i = 0; i < 30 && _sim.Dispositivo(1).AcertosDeRelogio == 0; i++)
        {
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        return (bomba, catraca);
    }

    private ComandoDeCatraca Pedido(TipoDeComando tipo, string? texto = null, string? motivo = null)
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(1, tipo, "Ana (portaria)", _agora, texto, 10, motivo);
        Assert.True(comando is not null, string.Join(" ", problemas));
        return comando;
    }

    private static void Passos(DevicePump bomba, DeviceSlot catraca, int quantos)
    {
        for (var i = 0; i < quantos; i++)
        {
            bomba.Passo(catraca, TimeSpan.Zero);
        }
    }

    [Fact]
    public void Liberacao_manual_libera_no_sentido_de_entrada_e_registra_o_giro()
    {
        var (bomba, catraca) = EmOperacao();
        catraca.Enfileirar(Pedido(TipoDeComando.LiberacaoManual, motivo: "Criança de colo sem ingresso"));

        Assert.Equal("comando LiberacaoManual: liberação manual pedida", bomba.Passo(catraca, TimeSpan.Zero));
        Assert.Equal(DeviceState.LiberarCatraca, catraca.Maquina.Current);
        Assert.Equal(["inner-1"], _pendentesDescartados);

        bomba.Passo(catraca, TimeSpan.Zero);
        Assert.Equal(1, _sim.Dispositivo(1).LiberacoesPedidas[GateDirection.Entrada]);

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Passos(bomba, catraca, 2);

        var (comando, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(TipoDeComando.LiberacaoManual, comando.Tipo);
        Assert.Equal(SituacaoDoComando.Concluido, situacao);
        Assert.Equal("liberada; girou", resultado);
    }

    [Fact]
    public void Liberacao_manual_sem_giro_diz_que_ninguem_girou()
    {
        var (bomba, catraca) = EmOperacao();
        catraca.Enfileirar(Pedido(TipoDeComando.LiberacaoManual, motivo: "Leitor não lê o QR"));
        Passos(bomba, catraca, 2);

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.FimTempoAcionamento)));
        Passos(bomba, catraca, 2);

        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Concluido, situacao);
        Assert.Equal("liberada; ninguém girou", resultado);
    }

    [Fact]
    public void Liberacao_manual_que_a_catraca_nao_recebe_e_falha_e_nao_fica_aberta()
    {
        var (bomba, catraca) = EmOperacao();
        catraca.Enfileirar(Pedido(TipoDeComando.LiberacaoManual, motivo: "Leitor não lê o QR"));
        bomba.Passo(catraca, TimeSpan.Zero);

        _sim.Dispositivo(1).Desconectado = true;
        bomba.Passo(catraca, TimeSpan.Zero);

        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Falhou, situacao);
        Assert.Equal("a catraca não recebeu a liberação", resultado);
        Assert.False(_sim.Dispositivo(1).LiberacoesPedidas.ContainsKey(GateDirection.Entrada));
    }

    /// <summary>
    /// Pedido feito com alguém no meio de uma passagem espera a passagem terminar: o
    /// comando nunca atropela quem está girando.
    /// </summary>
    [Fact]
    public void Comando_espera_a_passagem_em_andamento_terminar()
    {
        var (bomba, catraca) = EmOperacao();
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0000000001"));
        bomba.Passo(catraca, TimeSpan.Zero);
        Assert.Equal(DeviceState.ValidarAcesso, catraca.Maquina.Current);

        catraca.Enfileirar(Pedido(TipoDeComando.MensagemTemporaria, texto: "Use a catraca 2"));
        Passos(bomba, catraca, 2);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
        Assert.Empty(_sim.Dispositivo(1).MensagensTemporarias);

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        for (var i = 0; i < 10 && _desfechos.Count == 0; i++)
        {
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(("Use a catraca 2", TimeSpan.FromSeconds(10)), Assert.Single(_sim.Dispositivo(1).MensagensTemporarias));
        Assert.Equal(SituacaoDoComando.Concluido, Assert.Single(_desfechos).Situacao);
    }

    [Fact]
    public void Liberacao_manual_que_espera_demais_expira_sem_liberar()
    {
        var (bomba, catraca) = EmOperacao();
        _sim.Dispositivo(1).Desconectado = true;
        bomba.Passo(catraca, TimeSpan.Zero);

        catraca.Enfileirar(Pedido(TipoDeComando.LiberacaoManual, motivo: "Leitor não lê o QR"));
        _agora += TimeSpan.FromSeconds(16);
        bomba.Passo(catraca, TimeSpan.Zero);

        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Expirado, situacao);
        Assert.StartsWith("a catraca não ficou livre a tempo", resultado);

        // A catraca volta, e a liberação velha não acontece.
        _sim.Dispositivo(1).Desconectado = false;
        for (var i = 0; i < 30; i++)
        {
            _agora += TimeSpan.FromSeconds(30);
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.False(_sim.Dispositivo(1).LiberacoesPedidas.ContainsKey(GateDirection.Entrada));
    }

    [Fact]
    public void Acertar_relogio_agora_corrige_a_catraca_e_conclui()
    {
        var (bomba, catraca) = EmOperacao();
        _sim.Dispositivo(1).DesvioDeRelogio = TimeSpan.FromMinutes(4);

        catraca.Enfileirar(Pedido(TipoDeComando.AcertarRelogio));
        bomba.Passo(catraca, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, _sim.Dispositivo(1).DesvioDeRelogio);
        Assert.Equal((SituacaoDoComando.Concluido, "relógio acertado"), (_desfechos[0].Situacao, _desfechos[0].Resultado));
    }

    [Fact]
    public void Acertar_relogio_que_a_catraca_recusa_falha()
    {
        var (bomba, catraca) = EmOperacao();
        _sim.Dispositivo(1).RetornoDoRelogio = 1;

        catraca.Enfileirar(Pedido(TipoDeComando.AcertarRelogio));
        bomba.Passo(catraca, TimeSpan.Zero);

        Assert.Equal(SituacaoDoComando.Falhou, Assert.Single(_desfechos).Situacao);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
    }

    [Fact]
    public void Reiniciar_conexao_refaz_a_sequencia_e_conclui_quando_a_catraca_volta_a_atender()
    {
        var (bomba, catraca) = EmOperacao();
        var configuracoesAntes = _sim.Dispositivo(1).ConfiguracoesRecebidas.Count;

        catraca.Enfileirar(Pedido(TipoDeComando.ReiniciarConexao));
        Assert.Equal("comando ReiniciarConexao: reconectando", bomba.Passo(catraca, TimeSpan.Zero));
        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);
        Assert.Empty(_desfechos);

        Passos(bomba, catraca, 15);

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.True(_sim.Dispositivo(1).ConfiguracoesRecebidas.Count > configuracoesAntes);
        Assert.Equal((SituacaoDoComando.Concluido, "reconectada; catraca atendendo"), (_desfechos[0].Situacao, _desfechos[0].Resultado));
        Assert.Equal(2, _sim.Dispositivo(1).AcertosDeRelogio);
    }

    [Fact]
    public void Aplicar_configuracao_envia_a_configuracao_nova_completa()
    {
        var (bomba, catraca) = EmOperacao();
        catraca.Enfileirar(Pedido(TipoDeComando.AplicarConfiguracao), Configuracao("Entrada pelo portao 2"));
        Passos(bomba, catraca, 16);

        Assert.Equal("Entrada pelo portao 2", _sim.Dispositivo(1).ConfiguracoesRecebidas[^1].MensagemPadrao);
        Assert.Equal("Entrada pelo portao 2", catraca.Configuracao.MensagemPadrao);
        Assert.Equal((SituacaoDoComando.Concluido, "configuração enviada; catraca atendendo"), (_desfechos[0].Situacao, _desfechos[0].Resultado));
    }

    [Fact]
    public void Reconexao_pedida_que_nao_volta_em_dois_minutos_falha()
    {
        var (bomba, catraca) = EmOperacao();
        catraca.Enfileirar(Pedido(TipoDeComando.ReiniciarConexao));
        bomba.Passo(catraca, TimeSpan.Zero);
        _sim.Dispositivo(1).Desconectado = true;

        for (var i = 0; i < 20 && _desfechos.Count == 0; i++)
        {
            _agora += TimeSpan.FromSeconds(10);
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Falhou, situacao);
        Assert.StartsWith("a catraca não voltou a atender em 2 min", resultado);
    }

    [Fact]
    public void Pedido_invalido_nem_chega_a_existir()
    {
        Assert.Contains("A liberação manual exige o motivo (5 a 200 caracteres).",
            ComandoDeCatraca.Criar(1, TipoDeComando.LiberacaoManual, "Ana", _agora, motivo: "ok").Problemas);
        Assert.Contains("Informe a identificação de quem está pedindo (2 a 123 caracteres).",
            ComandoDeCatraca.Criar(1, TipoDeComando.AcertarRelogio, " ", _agora).Problemas);
        Assert.Contains("A mensagem precisa ter de 1 a 32 caracteres.",
            ComandoDeCatraca.Criar(1, TipoDeComando.MensagemTemporaria, "Ana", _agora, texto: new string('x', 33)).Problemas);
        Assert.Contains("A catraca vai de 1 a 99.",
            ComandoDeCatraca.Criar(0, TipoDeComando.AcertarRelogio, "Ana", _agora).Problemas);

        var (liberacao, _) = ComandoDeCatraca.Criar(1, TipoDeComando.LiberacaoManual, "Ana", _agora, motivo: "Criança de colo");
        Assert.Equal(_agora + TimeSpan.FromSeconds(15), liberacao!.ExpiraEm);
    }

    [Fact]
    public void Autoria_cabe_com_nome_e_login_completos_e_recusa_acima_do_limite()
    {
        var autoria = $"{new string('N', 80)} ({new string('u', 40)})";
        Assert.Equal(ComandoDeCatraca.LimiteDoOperador, autoria.Length);
        var (comando, problemas) = ComandoDeCatraca.Criar(1, TipoDeComando.AcertarRelogio, autoria, _agora);
        Assert.Empty(problemas);
        Assert.Equal(autoria, comando!.Operador);
        Assert.Null(ComandoDeCatraca.Criar(1, TipoDeComando.AcertarRelogio, autoria + "x", _agora).Comando);
    }
}
