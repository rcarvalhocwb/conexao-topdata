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

namespace Integration.Tests;

/// <summary>
/// Fase 4b de ponta a ponta: o painel pede pelo serviço, o pedido vira auditoria na base
/// local, o worker da catraca pega, executa com a catraca livre e grava o desfecho.
/// </summary>
public sealed class GerenciarCatracaTests : IDisposable
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
    private (DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas) _relida =
        (ConfiguracaoDeBancada.TopFit4() with { MensagemPadrao = "Entrada pelo portao 2" }, []);

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

    public GerenciarCatracaTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), DateTimeOffset.UtcNow);
        _repositorio.Ingerir([new IngressoRecebido("zet", "T1", Qr, Qr, Categoria: "inteira")], DateTimeOffset.UtcNow.AddMinutes(-5));
        _fila = new FilaDeComandosSqlite(_banco.Fabrica);

        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1, 2)]);
        supervisor.Iniciar();
        _servico = new EdgeControlService(supervisor, relogio: () => _agora, comandos: _fila);

        _sim = new InnerSimulator(() => _agora);
        _sessao = new SessaoDeOperacao(
            _sim,
            [1],
            ConfiguracaoDeBancada.TopFit4(),
            new DecisorDeIngresso(_repositorio),
            _registro.Add,
            _ => { },
            relogio: () => _agora,
            comandos: _fila,
            recarregarConfiguracao: () => _relida);
        _sessao.Iniciar(3570);
        Voltas(12);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);
    }

    public void Dispose()
    {
        _sim.Dispose();
        _banco.Dispose();
    }

    /// <summary>Cada volta anda 1 s: o worker olha a fila de comandos a cada 500 ms.</summary>
    private void Voltas(int quantas = 6)
    {
        for (var i = 0; i < quantas; i++)
        {
            _agora += TimeSpan.FromSeconds(1);
            _sessao.UmaVolta();
        }
    }

    private EnviarComandoResponse Pedir(int inner, TipoDeComandoIpc tipo, string operador = "Ana (portaria)", string texto = "", string motivo = "") =>
        _servico.EnviarComando(new EnviarComandoRequest { Inner = inner, Tipo = tipo, Operador = operador, Texto = texto, Motivo = motivo }, null!).Result;

    private List<ComandoRegistrado> Historico(int inner = 0) =>
        [.. _servico.ListarComandos(new ListarComandosRequest { Inner = inner }, null!).Result.Comandos];

    [Fact]
    public void Liberacao_manual_pelo_painel_gira_a_catraca_e_fica_auditada()
    {
        var resposta = Pedir(1, TipoDeComandoIpc.LiberacaoManual, motivo: "Criança de colo sem ingresso");
        Assert.True(resposta.Aceito, string.Join(" ", resposta.Problemas));
        Assert.Equal(SituacaoIpc.Pendente, Assert.Single(Historico()).Situacao);

        Voltas(3);
        Assert.Equal(1, _sim.Dispositivo(1).LiberacoesPedidas[GateDirection.Entrada]);

        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(3);

        var registro = Assert.Single(Historico(1));
        Assert.Equal(SituacaoIpc.Concluido, registro.Situacao);
        Assert.Equal("liberada; girou", registro.Resultado);
        Assert.Equal("Ana (portaria)", registro.Operador);
        Assert.Equal("Criança de colo sem ingresso", registro.Motivo);

        // Nome e motivo digitados ficam na auditoria, não no registro do worker.
        Assert.DoesNotContain(_registro, l => l.Contains("Criança", StringComparison.Ordinal) || l.Contains("Ana", StringComparison.Ordinal));
    }

    /// <summary>
    /// O giro de uma liberação manual não pode confirmar a passagem do último ingresso
    /// lido: seria atribuir a entrada de uma pessoa ao ingresso de outra.
    /// </summary>
    [Fact]
    public void Giro_da_liberacao_manual_nao_vira_passagem_do_ultimo_ingresso()
    {
        // Ingresso lido e liberado; a comunicação cai antes do giro ou do fim do tempo.
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), Qr));
        Voltas(3);
        Assert.Equal(DeviceState.MonitoraGiroCatraca, _sessao.Dispositivos[0].Maquina.Current);
        _sim.Dispositivo(1).Desconectado = true;
        Voltas(1);
        _sim.Dispositivo(1).Desconectado = false;
        for (var i = 0; i < 20 && _sessao.Dispositivos[0].Maquina.Current is not DeviceState.Polling; i++)
        {
            _agora += TimeSpan.FromSeconds(30);
            _sessao.UmaVolta();
        }

        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);

        // O operador libera à mão, e alguém gira.
        Assert.True(Pedir(1, TipoDeComandoIpc.LiberacaoManual, motivo: "Leitor não leu o QR").Aceito);
        Voltas(3);
        _sim.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(3);

        var conta = _repositorio.Conciliar("zet", DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(1, conta.UsosConsumidos);
        Assert.Equal(0, conta.UsosComPassagemFisica);
        Assert.Equal(1, conta.UsosSemPassagemFisica);
        Assert.Equal("liberada; girou", Assert.Single(Historico()).Resultado);
    }

    [Fact]
    public void Aplicar_agora_vale_para_todas_as_catracas_e_quem_nao_pega_expira()
    {
        var resposta = Pedir(0, TipoDeComandoIpc.AplicarConfiguracao);
        Assert.Equal(2, resposta.Ids.Count);

        Voltas(16);

        Assert.Equal("Entrada pelo portao 2", _sim.Dispositivo(1).ConfiguracoesRecebidas[^1].MensagemPadrao);
        var um = Assert.Single(Historico(1));
        Assert.Equal((SituacaoIpc.Concluido, "configuração enviada; catraca atendendo"), (um.Situacao, um.Resultado));

        // A catraca 2 é de um worker que não está rodando neste teste: ninguém pega.
        _agora += TimeSpan.FromMinutes(2);
        Assert.Equal(SituacaoIpc.Expirado, Assert.Single(Historico(2)).Situacao);
    }

    [Fact]
    public void Configuracao_invalida_nao_e_aplicada_e_o_motivo_fica_no_historico()
    {
        _relida = (null, ["A mensagem do display precisa ter de 1 a 32 caracteres."]);
        var enviadasAntes = _sim.Dispositivo(1).ConfiguracoesRecebidas.Count;

        Pedir(1, TipoDeComandoIpc.AplicarConfiguracao);
        Voltas(6);

        var registro = Assert.Single(Historico(1));
        Assert.Equal(SituacaoIpc.Falhou, registro.Situacao);
        Assert.Contains("1 a 32 caracteres", registro.Resultado, StringComparison.Ordinal);
        Assert.Equal(enviadasAntes, _sim.Dispositivo(1).ConfiguracoesRecebidas.Count);
        Assert.Equal(DeviceState.Polling, _sessao.Dispositivos[0].Maquina.Current);
    }

    [Fact]
    public void Mensagem_temporaria_aparece_no_display()
    {
        Pedir(1, TipoDeComandoIpc.MensagemTemporaria, texto: "Use a catraca 2");
        Voltas(3);

        Assert.Equal("Use a catraca 2", Assert.Single(_sim.Dispositivo(1).MensagensTemporarias).Texto);
        Assert.Equal(SituacaoIpc.Concluido, Assert.Single(Historico()).Situacao);
    }

    [Fact]
    public void O_servico_recusa_pedido_incompleto_ou_para_catraca_que_nao_existe()
    {
        Assert.Contains("A liberação manual exige o motivo (5 a 200 caracteres).", Pedir(1, TipoDeComandoIpc.LiberacaoManual).Problemas);
        Assert.Contains("A catraca 7 não está cadastrada.", Pedir(7, TipoDeComandoIpc.AcertarRelogio).Problemas);
        Assert.Contains("A catraca 0 não está cadastrada.", Pedir(0, TipoDeComandoIpc.LiberacaoManual, motivo: "Criança de colo").Problemas);
        Assert.Contains("Comando desconhecido.", Pedir(1, TipoDeComandoIpc.NaoEspecificado).Problemas);
        Assert.Contains("Informe o nome de quem está pedindo (2 a 80 caracteres).", Pedir(1, TipoDeComandoIpc.AcertarRelogio, operador: "").Problemas);

        Assert.Empty(Historico());
    }

    [Fact]
    public void A_auditoria_nao_se_apaga_nem_se_reescreve()
    {
        Pedir(1, TipoDeComandoIpc.AcertarRelogio);
        Voltas(3);
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
        Assert.Throws<SqliteException>(() => Executar("UPDATE operator_command SET requested_by = 'outro' WHERE id = $id;"));
        Assert.Throws<SqliteException>(() => Executar("UPDATE operator_command SET status = 'pendente' WHERE id = $id;"));
        Assert.Equal(SituacaoIpc.Concluido, Assert.Single(Historico()).Situacao);
    }

    [Fact]
    public void Dois_workers_nao_pegam_o_mesmo_comando_e_pedido_vencido_nao_e_pego()
    {
        var (comando, _) = ComandoDeCatraca.Criar(2, Access.Application.Devices.TipoDeComando.AcertarRelogio, "Ana", _agora);
        _fila.Pedir(comando!);

        Assert.True(_fila.Receber(comando!.Id, _agora));
        Assert.False(_fila.Receber(comando.Id, _agora));

        var (vencido, _) = ComandoDeCatraca.Criar(2, Access.Application.Devices.TipoDeComando.AcertarRelogio, "Ana", _agora);
        _fila.Pedir(vencido!);
        Assert.False(_fila.Receber(vencido!.Id, _agora + TimeSpan.FromMinutes(2)));
    }
}
