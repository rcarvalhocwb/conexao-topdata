using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Worker.Bancada;
using Edge.Worker.Operacao;
using Microsoft.Data.Sqlite;
using Simulator;
using TipoDeComandoIpc = Contracts.Edge.V1.TipoDeComando;
using SituacaoIpc = Contracts.Edge.V1.SituacaoDoComando;
using TipoDeComando = Access.Application.Devices.TipoDeComando;

namespace Integration.Tests;

/// <summary>
/// Etapa A.8 do docs/35 de ponta a ponta: bip curto e longo, liberar saída e liberar nos dois
/// sentidos. Painel → serviço → <c>operator_command</c> → worker → simulador → desfecho.
/// </summary>
/// <remarks>
/// <para>
/// Cada comando tem a sua chave técnica em <c>edge_setting</c>, desligada: o serviço recusa
/// antes de enfileirar, e nada chega à auditoria. Ligada a chave (como no ensaio do docs/21
/// §6E), o comando segue o caminho da fase 4b — só em <c>Polling</c>, com validade, desfecho e
/// auditoria. Os dois sentidos são recusados também pela decisão D5, mesmo com a chave; o que
/// o worker faz com eles é provado enfileirando o pedido direto na base.
/// </para>
/// <para>
/// Nenhuma migração nova: <c>operator_command.kind</c> não tem <c>CHECK</c> (009), e o teste
/// <see cref="Os_tipos_novos_cabem_na_tabela_de_sempre_sem_migracao"/> prova isso.
/// </para>
/// </remarks>
public sealed class ComandosNovosDaCatracaTests : IDisposable
{
    private const string Qr = "1000000001";
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly InnerSimulator _sim;
    private readonly RepositorioDeIngressos _repositorio;
    private readonly FilaDeComandosSqlite _fila;
    private readonly EdgeControlService _servico;
    private readonly SessaoDeOperacao _sessao;
    private readonly List<string> _registro = [];
    private DateTimeOffset _agora = Inicio;

    private sealed class WorkerFalso(string nome, int porta, params int[] inners) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    public ComandosNovosDaCatracaTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), DateTimeOffset.UtcNow);
        _repositorio.Ingerir([new IngressoRecebido("zet", "T1", Qr, Qr, Categoria: "inteira")], DateTimeOffset.UtcNow.AddMinutes(-5));
        _fila = new FilaDeComandosSqlite(_banco.Fabrica);

        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1)]);
        supervisor.Iniciar();
        _servico = new EdgeControlService(
            supervisor,
            relogio: () => _agora,
            comandos: _fila,
            chavesDosComandos: new ChavesDosComandos(_banco.Fabrica));

        _sim = new InnerSimulator(() => _agora);
        _sessao = new SessaoDeOperacao(
            _sim,
            [1],
            ConfiguracaoDeBancada.TopFit4(),
            new DecisorDeIngresso(_repositorio),
            _registro.Add,
            _ => { },
            relogio: () => _agora,
            comandos: _fila);
        _sessao.Iniciar(3570);
        Voltas(12);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);
    }

    public void Dispose()
    {
        _sim.Dispose();
        _banco.Dispose();
    }

    public static TheoryData<TipoDeComandoIpc> TiposNovos() =>
    [
        TipoDeComandoIpc.BipCurto,
        TipoDeComandoIpc.BipLongo,
        TipoDeComandoIpc.LiberarSaida,
        TipoDeComandoIpc.LiberarDoisSentidos,
    ];

    /// <summary>Cada volta anda 1 s: o worker olha a fila de comandos a cada 500 ms.</summary>
    private void Voltas(int quantas = 6)
    {
        for (var i = 0; i < quantas; i++)
        {
            _agora += TimeSpan.FromSeconds(1);
            _sessao.UmaVolta();
        }
    }

    private EnviarComandoResponse Pedir(TipoDeComandoIpc tipo, string motivo = "", string confirmacao = "") =>
        _servico.EnviarComando(
            new EnviarComandoRequest
            {
                Inner = 1,
                Tipo = tipo,
                Operador = "Ana (portaria)",
                Motivo = motivo,
                Confirmacao = confirmacao,
            },
            null!).Result;

    private List<ComandoRegistrado> Historico() =>
        [.. _servico.ListarComandos(new ListarComandosRequest { Inner = 1 }, null!).Result.Comandos];

    // Como o ensaio do docs/21 §6E liga a chave: uma linha em edge_setting.
    private void Ligar(TipoDeComando tipo, string valor = "1")
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            INSERT INTO edge_setting (key, value, updated_at, updated_by) VALUES ($chave, $valor, $em, 'bancada')
            ON CONFLICT (key) DO UPDATE SET value = excluded.value;
            """;
        sql.Parameters.AddWithValue("$chave", ComandoDeCatraca.ChaveTecnica(tipo));
        sql.Parameters.AddWithValue("$valor", valor);
        sql.Parameters.AddWithValue("$em", "2026-12-06T21:00:00.0000000Z");
        sql.ExecuteNonQuery();
    }

    private static TipoDeComando Dominio(TipoDeComandoIpc tipo) => tipo switch
    {
        TipoDeComandoIpc.BipCurto => TipoDeComando.BipCurto,
        TipoDeComandoIpc.BipLongo => TipoDeComando.BipLongo,
        TipoDeComandoIpc.LiberarSaida => TipoDeComando.LiberarSaida,
        TipoDeComandoIpc.LiberarDoisSentidos => TipoDeComando.LiberarDoisSentidos,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    // ---------------------------------------------------------------------------------
    // O serviço recusa com a chave desligada, antes de enfileirar
    // ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TiposNovos))]
    public void Com_a_chave_desligada_o_servico_recusa_e_nada_vai_para_a_fila(TipoDeComandoIpc tipo)
    {
        var resposta = Pedir(tipo, motivo: "Ensaio sintético de recusa", confirmacao: "EVACUAR 1");

        Assert.False(resposta.Aceito);
        Assert.Empty(resposta.Ids);
        var chave = ComandoDeCatraca.ChaveTecnica(Dominio(tipo));
        Assert.Contains(resposta.Problemas, p => p.Contains($"a chave técnica {chave} fica desligada", StringComparison.Ordinal));

        Voltas(3);
        Assert.Empty(Historico());
        Assert.Empty(_sim.Dispositivo(1).BipsAcionados);
        Assert.Empty(_sim.Dispositivo(1).LiberacoesPedidas);
    }

    /// <summary>Sem quem leia as chaves (serviço montado sem elas), todas contam como desligadas.</summary>
    [Theory]
    [MemberData(nameof(TiposNovos))]
    public async Task Servico_sem_as_chaves_recusa_os_comandos_novos(TipoDeComandoIpc tipo)
    {
        var semChaves = new EdgeControlService(
            new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1)]), relogio: () => _agora, comandos: _fila);
        Ligar(Dominio(tipo));

        var resposta = await semChaves.EnviarComando(
            new EnviarComandoRequest
            {
                Inner = 1,
                Tipo = tipo,
                Operador = "Ana (portaria)",
                Motivo = "Ensaio sintético de recusa",
                Confirmacao = "EVACUAR 1",
            },
            null!);

        Assert.False(resposta.Aceito);
        Assert.Empty(Historico());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("sim")]
    [InlineData("")]
    public void Chave_com_valor_diferente_de_um_e_desligada(string valor)
    {
        Ligar(TipoDeComando.BipCurto, valor);

        Assert.False(Pedir(TipoDeComandoIpc.BipCurto).Aceito);
        Assert.Empty(Historico());
    }

    /// <summary>A chave de um comando não liga outro.</summary>
    [Fact]
    public void Cada_comando_tem_a_sua_chave()
    {
        Ligar(TipoDeComando.BipCurto);

        Assert.True(Pedir(TipoDeComandoIpc.BipCurto).Aceito);
        Assert.False(Pedir(TipoDeComandoIpc.BipLongo).Aceito);
        Assert.False(Pedir(TipoDeComandoIpc.LiberarSaida, motivo: "Saída de emergência").Aceito);
        Assert.Single(Historico());
    }

    // ---------------------------------------------------------------------------------
    // Dois sentidos: recusado pela D5 mesmo com a chave
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Dois_sentidos_com_a_chave_ligada_ainda_e_recusado_pela_decisao_d5()
    {
        Ligar(TipoDeComando.LiberarDoisSentidos);

        var resposta = Pedir(TipoDeComandoIpc.LiberarDoisSentidos, motivo: "Evacuação do setor A", confirmacao: "EVACUAR 1");

        Assert.False(resposta.Aceito);
        var problema = Assert.Single(resposta.Problemas);
        Assert.StartsWith("Aguardando decisão D5 do dono do produto", problema, StringComparison.Ordinal);

        Voltas(3);
        Assert.Empty(Historico());
        Assert.Empty(_sim.Dispositivo(1).LiberacoesPedidas);
    }

    [Fact]
    public void Dois_sentidos_com_a_chave_desligada_diz_os_dois_motivos()
    {
        var problemas = Pedir(TipoDeComandoIpc.LiberarDoisSentidos, motivo: "Evacuação do setor A", confirmacao: "EVACUAR 1").Problemas;

        Assert.Equal(2, problemas.Count);
        Assert.StartsWith(ComandoDeCatraca.AguardandoDecisaoD5, problemas[0], StringComparison.Ordinal);
        Assert.Contains("comando.liberar_dois_sentidos", problemas[1], StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    // Com a chave ligada: executa em Polling, com desfecho e auditoria
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(TipoDeComandoIpc.BipCurto, TipoDeBip.Curto)]
    [InlineData(TipoDeComandoIpc.BipLongo, TipoDeBip.Longo)]
    public void Bip_com_a_chave_ligada_toca_uma_vez_e_fica_auditado(TipoDeComandoIpc tipo, TipoDeBip esperado)
    {
        Ligar(Dominio(tipo));

        var resposta = Pedir(tipo);
        Assert.True(resposta.Aceito, string.Join(" ", resposta.Problemas));
        Assert.Equal(SituacaoIpc.Pendente, Assert.Single(Historico()).Situacao);

        Voltas(3);

        Assert.Equal([esperado], _sim.Dispositivo(1).BipsAcionados);
        var registro = Assert.Single(Historico());
        Assert.Equal(tipo, registro.Tipo);
        Assert.Equal((SituacaoIpc.Concluido, "bip acionado"), (registro.Situacao, registro.Resultado));
        Assert.Equal("Ana (portaria)", registro.Operador);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);
    }

    [Fact]
    public void Bip_que_a_catraca_recusa_falha_e_a_catraca_segue_atendendo()
    {
        Ligar(TipoDeComando.BipLongo);
        _sim.Dispositivo(1).RetornoForcado = 1;

        Assert.True(Pedir(TipoDeComandoIpc.BipLongo).Aceito);
        Voltas(1);
        _sim.Dispositivo(1).RetornoForcado = null;
        Voltas(2);

        var registro = Assert.Single(Historico());
        Assert.Equal(SituacaoIpc.Falhou, registro.Situacao);
        Assert.StartsWith("a catraca recusou o bip", registro.Resultado, StringComparison.Ordinal);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);
    }

    [Fact]
    public void Liberar_saida_com_a_chave_ligada_libera_a_saida_do_perfil_e_fica_auditada()
    {
        Ligar(TipoDeComando.LiberarSaida);

        var resposta = Pedir(TipoDeComandoIpc.LiberarSaida, motivo: "Pessoa passando mal, saída acompanhada");
        Assert.True(resposta.Aceito, string.Join(" ", resposta.Problemas));

        Voltas(3);
        Assert.Equal(1, _sim.Dispositivo(1).LiberacoesPedidas[GateDirection.Saida]);
        Assert.False(_sim.Dispositivo(1).LiberacoesPedidas.ContainsKey(GateDirection.Entrada));

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(3);

        var registro = Assert.Single(Historico());
        Assert.Equal((SituacaoIpc.Concluido, "liberada na saída; girou"), (registro.Situacao, registro.Resultado));
        Assert.Equal("Pessoa passando mal, saída acompanhada", registro.Motivo);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);

        // Nome e motivo digitados ficam na auditoria, não no registro do worker.
        Assert.DoesNotContain(_registro, l => l.Contains("passando mal", StringComparison.Ordinal) || l.Contains("Ana", StringComparison.Ordinal));
    }

    [Fact]
    public void Liberar_saida_sem_motivo_e_recusada_mesmo_com_a_chave()
    {
        Ligar(TipoDeComando.LiberarSaida);

        Assert.Contains("A liberação de saída exige o motivo (5 a 200 caracteres).", Pedir(TipoDeComandoIpc.LiberarSaida).Problemas);
        Assert.Empty(Historico());
    }

    /// <summary>
    /// O giro de uma liberação de saída não confirma a passagem do último ingresso lido (o
    /// mesmo cuidado da liberação manual).
    /// </summary>
    [Fact]
    public void Giro_da_saida_nao_vira_passagem_do_ultimo_ingresso()
    {
        Ligar(TipoDeComando.LiberarSaida);
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), Qr));
        Voltas(3);
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.FimTempoAcionamento)));
        Voltas(3);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);

        Assert.True(Pedir(TipoDeComandoIpc.LiberarSaida, motivo: "Saída acompanhada").Aceito);
        Voltas(3);
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(3);

        var conta = _repositorio.Conciliar("zet", DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(0, conta.UsosComPassagemFisica);
        Assert.Equal("liberada na saída; girou", Assert.Single(Historico()).Resultado);
    }

    /// <summary>
    /// O serviço recusa os dois sentidos (D5), mas o comando está completo: enfileirado direto
    /// na base, o worker o executa só em Polling, com a função dos dois sentidos, e registra.
    /// </summary>
    [Fact]
    public void Dois_sentidos_enfileirado_na_base_e_executado_com_a_funcao_dos_dois_sentidos()
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberarDoisSentidos, "Ana (portaria)", _agora, motivo: "Evacuação do setor A", confirmacao: "EVACUAR 1");
        Assert.True(comando is not null, string.Join(" ", problemas));
        _fila.Pedir(comando);

        Voltas(3);
        Assert.Equal(1, _sim.Dispositivo(1).LiberacoesPedidas[GateDirection.DoisSentidos]);

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.FimTempoAcionamento)));
        Voltas(3);

        var registro = Assert.Single(Historico());
        Assert.Equal(TipoDeComandoIpc.LiberarDoisSentidos, registro.Tipo);
        Assert.Equal((SituacaoIpc.Concluido, "liberada nos dois sentidos; ninguém girou"), (registro.Situacao, registro.Resultado));
        Assert.Equal("Evacuação do setor A", registro.Motivo);
    }

    /// <summary>Só em Polling: pedido feito com alguém no meio de uma passagem espera ela terminar.</summary>
    [Fact]
    public void Bip_pedido_no_meio_de_uma_passagem_espera_a_catraca_ficar_livre()
    {
        Ligar(TipoDeComando.BipCurto);
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), Qr));
        Voltas(3);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, _sessao.Dispositivos[0].Maquina.Current);

        Assert.True(Pedir(TipoDeComandoIpc.BipCurto).Aceito);
        Voltas(3);
        Assert.Empty(_sim.Dispositivo(1).BipsAcionados);
        Assert.Equal(SituacaoIpc.Recebido, Assert.Single(Historico()).Situacao);

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(4);

        Assert.Equal([TipoDeBip.Curto], _sim.Dispositivo(1).BipsAcionados);
        Assert.Equal(SituacaoIpc.Concluido, Assert.Single(Historico()).Situacao);
    }

    // ---------------------------------------------------------------------------------
    // Validade: catraca fora do ar, o pedido expira sem executar
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(TipoDeComandoIpc.BipCurto, 61)]
    [InlineData(TipoDeComandoIpc.LiberarSaida, 16)]
    public void Comando_que_a_catraca_nao_executa_a_tempo_expira_sem_executar(TipoDeComandoIpc tipo, int segundos)
    {
        Ligar(Dominio(tipo));
        _sim.Dispositivo(1).Desconectado = true;
        Voltas(2);
        Assert.NotEqual(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);

        Assert.True(Pedir(tipo, motivo: "Saída acompanhada").Aceito);
        Voltas(1);
        _agora += TimeSpan.FromSeconds(segundos);
        Voltas(1);

        Assert.Equal(SituacaoIpc.Expirado, Assert.Single(Historico()).Situacao);

        // A catraca volta, e o pedido vencido não acontece.
        _sim.Dispositivo(1).Desconectado = false;
        for (var i = 0; i < 30; i++)
        {
            _agora += TimeSpan.FromSeconds(30);
            _sessao.UmaVolta();
        }

        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);
        Assert.Empty(_sim.Dispositivo(1).BipsAcionados);
        Assert.False(_sim.Dispositivo(1).LiberacoesPedidas.ContainsKey(GateDirection.Saida));
    }

    // ---------------------------------------------------------------------------------
    // Base: sem migração, e a auditoria continua valendo para os tipos novos
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Os_tipos_novos_cabem_na_tabela_de_sempre_sem_migracao()
    {
        foreach (var tipo in new[] { TipoDeComando.BipCurto, TipoDeComando.BipLongo, TipoDeComando.LiberarSaida, TipoDeComando.LiberarDoisSentidos })
        {
            var (comando, problemas) = ComandoDeCatraca.Criar(
                1, tipo, "Ana (portaria)", _agora, motivo: "Ensaio sintético", confirmacao: "EVACUAR 1");
            Assert.True(comando is not null, string.Join(" ", problemas));
            _fila.Pedir(comando);
        }

        var lidos = _fila.Listar(1).Select(r => r.Comando.Tipo).Order().ToList();
        Assert.Equal([TipoDeComando.BipCurto, TipoDeComando.BipLongo, TipoDeComando.LiberarSaida, TipoDeComando.LiberarDoisSentidos], lidos);
    }

    [Fact]
    public void A_auditoria_dos_tipos_novos_nao_se_apaga_nem_se_reescreve()
    {
        Ligar(TipoDeComando.LiberarSaida);
        Assert.True(Pedir(TipoDeComandoIpc.LiberarSaida, motivo: "Saída acompanhada").Aceito);
        var id = Assert.Single(Historico()).Id;

        using var conexao = _banco.Fabrica.Abrir();

        void Executar(string sql)
        {
            using var comando = conexao.CreateCommand();
            comando.CommandText = sql;
            comando.Parameters.AddWithValue("$id", id);
            comando.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => Executar("DELETE FROM operator_command WHERE id = $id;"));
        Assert.Throws<SqliteException>(() => Executar("UPDATE operator_command SET reason = 'outro motivo' WHERE id = $id;"));
        Assert.Throws<SqliteException>(() => Executar("UPDATE operator_command SET kind = 'LiberacaoManual' WHERE id = $id;"));
    }
}
