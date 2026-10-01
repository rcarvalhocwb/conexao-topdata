using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Credentials;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Worker;
using Edge.Worker.Operacao;
using Simulator;
using SituacaoIpc = Contracts.Edge.V1.SituacaoDoComando;
using TipoDeComandoIpc = Contracts.Edge.V1.TipoDeComando;
using SituacaoDoComando = Access.Application.Devices.SituacaoDoComando;
using TipoDeComando = Access.Application.Devices.TipoDeComando;

namespace Integration.Tests;

/// <summary>
/// Etapa A.9 do docs/35: a coleta de bilhetes de verdade. O laço grava cada bilhete na base
/// antes de pedir o próximo (R-68), só por comando do operador, só em Polling, e só com a chave
/// técnica <c>catraca.coletar_bilhetes</c> ligada. Catraca simulada, base temporária, códigos
/// sintéticos.
/// </summary>
public sealed class ColetaDeBilhetesTests : IDisposable
{
    private const int Porta = 3570;
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Marcado = new(2026, 12, 6, 18, 0, 0, TimeSpan.FromHours(-3));
    private static readonly ImpressaoDeCodigo Impressao = new(Enumerable.Repeat((byte)7, 32).ToArray());

    private readonly BancoTemporario _banco = new();
    private readonly InnerSimulator _sim;
    private readonly BilhetesColetados _bilhetes;
    private readonly GravadorQueFalha _gravador;
    private readonly List<(ComandoDeCatraca Comando, SituacaoDoComando Situacao, string Resultado)> _desfechos = [];
    private DateTimeOffset _agora = Inicio;

    public ColetaDeBilhetesTests()
    {
        _banco.Migrar();
        _bilhetes = new BilhetesColetados(_banco.Fabrica, Impressao);
        _gravador = new GravadorQueFalha(_bilhetes);
        _sim = new InnerSimulator(() => _agora);
    }

    public void Dispose()
    {
        _sim.Dispose();
        _banco.Dispose();
    }

    /// <summary>A base, com uma chave para simular "base ocupada"; conta as gravações.</summary>
    private sealed class GravadorQueFalha(IGravadorDeBilhetes base_) : IGravadorDeBilhetes
    {
        public bool Falhar { get; set; }

        public int Tentativas { get; private set; }

        public DesfechoDaGravacaoDoBilhete Gravar(BilheteColetado bilhete)
        {
            Tentativas++;
            if (Falhar)
            {
                throw new InvalidOperationException("base ocupada (simulada)");
            }

            return base_.Gravar(bilhete);
        }
    }

    private static Bilhete[] Bilhetes(int quantos, byte tipo = 10) =>
        [.. Enumerable.Range(1, quantos).Select(i => new Bilhete(tipo, Marcado.AddMinutes(i), $"9999{i:D10}"))];

    private (DevicePump Bomba, DeviceSlot Catraca) EmOperacao(IGravadorDeBilhetes? gravador)
    {
        _sim.AbrirPorta(Porta);
        var bomba = new DevicePump(
            _sim,
            () => _agora,
            decidir: _ => throw new InvalidOperationException("nenhuma leitura neste teste"),
            aoConcluirComando: (c, s, r) => _desfechos.Add((c, s, r)),
            gravadorDeBilhetes: gravador);
        var catraca = new DeviceSlot(1, PadroesDeFabrica.TopFit4, () => _agora);

        for (var i = 0; i < 30 && _sim.Dispositivo(1).AcertosDeRelogio == 0; i++)
        {
            bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        return (bomba, catraca);
    }

    private ComandoDeCatraca Pedido()
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(1, TipoDeComando.ColetarBilhetes, "Ana (portaria)", _agora);
        Assert.True(comando is not null, string.Join(" ", problemas));
        return comando;
    }

    private List<string> Passos(DevicePump bomba, DeviceSlot catraca, int quantos)
    {
        var feitos = new List<string>();
        for (var i = 0; i < quantos; i++)
        {
            _agora += TimeSpan.FromMilliseconds(100);
            feitos.Add(bomba.Passo(catraca, TimeSpan.Zero));
        }

        return feitos;
    }

    [Fact]
    public void Coleta_grava_todos_e_conclui_com_as_contagens()
    {
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(5));
        var (bomba, catraca) = EmOperacao(_gravador);

        catraca.Enfileirar(Pedido());
        Passos(bomba, catraca, 20);

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(0, _sim.Dispositivo(1).BilhetesNaMemoria);

        var gravados = _bilhetes.Listar(1);
        Assert.Equal(5, gravados.Count);
        Assert.Equal([1, 2, 3, 4, 5], gravados.Select(g => g.Ordem));
        Assert.All(gravados, g => Assert.Equal(_desfechos[0].Comando.Id, g.ColetaId));
        Assert.Equal(
            Enumerable.Range(1, 5).Select(i => Impressao.De($"9999{i:D10}")),
            gravados.Select(g => g.Impressao));

        var (comando, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(TipoDeComando.ColetarBilhetes, comando.Tipo);
        Assert.Equal(SituacaoDoComando.Concluido, situacao);
        Assert.Equal("memória da catraca vazia; 5 bilhete(s) coletado(s): 5 gravado(s), 0 já estava(m) na base", resultado);
    }

    /// <summary>R-68: um passo coleta, o seguinte grava; nunca dois ColetarBilhete sem gravação no meio.</summary>
    [Fact]
    public void Coletar_e_gravar_sao_passos_alternados()
    {
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(3));
        var (bomba, catraca) = EmOperacao(_gravador);
        catraca.Enfileirar(Pedido());

        var feitos = Passos(bomba, catraca, 8);

        Assert.Equal("comando ColetarBilhetes: coleta iniciada", feitos[0]);
        Assert.StartsWith("bilhete tipo 10 coletado (nº 1)", feitos[1], StringComparison.Ordinal);
        Assert.Equal("bilhete nº 1 gravado (tipo 10)", feitos[2]);
        Assert.StartsWith("bilhete tipo 10 coletado (nº 2)", feitos[3], StringComparison.Ordinal);
        Assert.Equal("bilhete nº 2 gravado (tipo 10)", feitos[4]);
        Assert.StartsWith("bilhete tipo 10 coletado (nº 3)", feitos[5], StringComparison.Ordinal);
        Assert.Equal("bilhete nº 3 gravado (tipo 10)", feitos[6]);
        Assert.StartsWith("sem bilhetes", feitos[7], StringComparison.Ordinal);
    }

    /// <summary>Base ocupada: o bilhete fica com o worker e a catraca não é chamada de novo.</summary>
    [Fact]
    public void Base_ocupada_segura_a_coleta_ate_o_bilhete_ficar_duravel()
    {
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(3));
        var (bomba, catraca) = EmOperacao(_gravador);
        catraca.Enfileirar(Pedido());
        Passos(bomba, catraca, 2);
        Assert.NotNull(catraca.BilheteAGravar);
        var chamadas = _sim.ChamadasNativas;

        _gravador.Falhar = true;
        var feitos = Passos(bomba, catraca, 10);

        Assert.All(feitos, f => Assert.Contains("ainda não gravado", f, StringComparison.Ordinal));
        Assert.Equal(chamadas, _sim.ChamadasNativas);
        Assert.Equal(2, _sim.Dispositivo(1).BilhetesNaMemoria);
        Assert.Equal(10, catraca.FalhasAoGravarBilhete);
        Assert.Empty(_desfechos);

        _gravador.Falhar = false;
        Passos(bomba, catraca, 10);

        Assert.Equal(3, _bilhetes.Listar(1).Count);
        Assert.Equal(SituacaoDoComando.Concluido, Assert.Single(_desfechos).Situacao);
    }

    [Fact]
    public void Sem_onde_gravar_o_comando_falha_e_nada_sai_da_catraca()
    {
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(3));
        var (bomba, catraca) = EmOperacao(gravador: null);
        catraca.Enfileirar(Pedido());

        Passos(bomba, catraca, 3);

        Assert.Equal(3, _sim.Dispositivo(1).BilhetesNaMemoria);
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Falhou, situacao);
        Assert.Equal("este worker não tem onde gravar bilhetes; nada foi coletado", resultado);
    }

    /// <summary>Comandos só em Polling: com a catraca fora do ar, o pedido expira sem coletar.</summary>
    [Fact]
    public void Coleta_so_acontece_com_a_catraca_em_polling()
    {
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(3));
        var (bomba, catraca) = EmOperacao(_gravador);

        _sim.Dispositivo(1).Desconectado = true;
        Passos(bomba, catraca, 1);
        Assert.NotEqual(DeviceState.Polling, catraca.Maquina.Current);

        catraca.Enfileirar(Pedido());
        _agora += ComandoDeCatraca.ValidadePara(TipoDeComando.ColetarBilhetes) + TimeSpan.FromSeconds(1);
        Passos(bomba, catraca, 1);

        Assert.Equal(3, _sim.Dispositivo(1).BilhetesNaMemoria);
        Assert.Empty(_bilhetes.Listar());
        var (_, situacao, _) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Expirado, situacao);
    }

    [Fact]
    public void Queda_da_comunicacao_no_meio_falha_com_o_que_ja_foi_gravado()
    {
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(4));
        var (bomba, catraca) = EmOperacao(_gravador);
        catraca.Enfileirar(Pedido());
        Passos(bomba, catraca, 5); // inicia, coleta 1, grava 1, coleta 2, grava 2

        _sim.Dispositivo(1).Desconectado = true;
        Passos(bomba, catraca, 1);

        Assert.Equal(2, _bilhetes.Listar(1).Count);
        Assert.Equal(2, _sim.Dispositivo(1).BilhetesNaMemoria);
        var (_, situacao, resultado) = Assert.Single(_desfechos);
        Assert.Equal(SituacaoDoComando.Falhou, situacao);
        Assert.StartsWith("coleta interrompida (estado Reconectar); 2 bilhete(s) coletado(s): 2 gravado(s)", resultado, StringComparison.Ordinal);
    }

    /// <summary>A catraca devolve o mesmo bilhete duas vezes: grava uma, e o desfecho conta.</summary>
    [Fact]
    public void O_mesmo_bilhete_duas_vezes_na_coleta_grava_uma()
    {
        var bilhete = new Bilhete(10, Marcado, "99990000000101");
        _sim.Dispositivo(1).ComBilhetes(bilhete, bilhete, bilhete with { Tipo = Bilhete.TipoRepetido });
        var (bomba, catraca) = EmOperacao(_gravador);
        catraca.Enfileirar(Pedido());

        Passos(bomba, catraca, 10);

        Assert.Single(_bilhetes.Listar(1));
        Assert.Equal(
            "memória da catraca vazia; 3 bilhete(s) coletado(s): 1 gravado(s), 2 já estava(m) na base",
            Assert.Single(_desfechos).Resultado);
    }

    /// <summary>
    /// Queda da conexão depois de gravar e antes de pedir o próximo: no modo em que só o próximo
    /// pedido confirma, a catraca devolve o bilhete de novo como 128, e ele não duplica.
    /// </summary>
    [Fact]
    public void Bilhete_devolvido_de_novo_como_128_apos_reconexao_nao_duplica()
    {
        var dispositivo = _sim.Dispositivo(1);
        dispositivo.ConfirmaNaProximaColeta = true;
        dispositivo.ComBilhetes(Bilhetes(3));
        var (bomba, catraca) = EmOperacao(_gravador);
        catraca.Enfileirar(Pedido());
        Passos(bomba, catraca, 3); // inicia, coleta 1, grava 1

        dispositivo.Desconectado = true;
        Passos(bomba, catraca, 1);
        Assert.Equal(SituacaoDoComando.Falhou, Assert.Single(_desfechos).Situacao);

        dispositivo.Desconectado = false;
        _agora += TimeSpan.FromMinutes(5);
        for (var i = 0; i < 40 && catraca.Maquina.Current is not DeviceState.Polling; i++)
        {
            Passos(bomba, catraca, 1);
            _agora += TimeSpan.FromSeconds(10);
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        catraca.Enfileirar(Pedido());
        Passos(bomba, catraca, 12);

        var gravados = _bilhetes.Listar(1);
        Assert.Equal(3, gravados.Count);
        Assert.DoesNotContain(gravados, g => g.Repetido);
        Assert.Equal(
            "memória da catraca vazia; 3 bilhete(s) coletado(s): 2 gravado(s), 1 já estava(m) na base",
            _desfechos[^1].Resultado);
    }

    // --- Serviço e worker pela base (ADR-0024) -----------------------------------------------

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

    private EdgeControlService Servico(FilaDeComandosSqlite fila)
    {
        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", Porta, 1)]);
        supervisor.Iniciar();
        return new EdgeControlService(
            supervisor,
            relogio: () => _agora,
            configuracoes: new ConfiguracoesDaBorda(_banco.Fabrica),
            comandos: fila);
    }

    private void LigarAChave(bool ligada)
    {
        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        configuracoes.Gravar(configuracoes.Ler().Configuracao with { ColetarBilhetes = ligada }, _agora, "teste");
    }

    private static EnviarComandoResponse Pedir(EdgeControlService servico) =>
        servico.EnviarComando(
            new EnviarComandoRequest { Inner = 1, Tipo = TipoDeComandoIpc.ColetarBilhetes, Operador = "Ana (portaria)" },
            null!).GetAwaiter().GetResult();

    [Fact]
    public void A_chave_nasce_desligada()
    {
        Assert.False(new ConfiguracaoDaOperacao().ColetarBilhetes);
        Assert.False(new ConfiguracoesDaBorda(_banco.Fabrica).Ler().Configuracao.ColetarBilhetes);
    }

    [Fact]
    public void Servico_recusa_a_coleta_com_a_chave_desligada_e_nada_vai_para_a_fila()
    {
        var fila = new FilaDeComandosSqlite(_banco.Fabrica);

        var resposta = Pedir(Servico(fila));

        Assert.Empty(resposta.Ids);
        Assert.Contains(resposta.Problemas, p => p.Contains("catraca.coletar_bilhetes", StringComparison.Ordinal));
        Assert.Empty(fila.Listar(null));
    }

    [Fact]
    public void Chave_ilegivel_conta_como_desligada()
    {
        using (var conexao = _banco.Fabrica.Abrir())
        using (var sql = conexao.CreateCommand())
        {
            sql.CommandText = "INSERT INTO edge_setting (key, value, updated_at) VALUES ('catraca.coletar_bilhetes', 'sim', '2026-12-06T21:00:00Z');";
            sql.ExecuteNonQuery();
        }

        var fila = new FilaDeComandosSqlite(_banco.Fabrica);
        Assert.Empty(Pedir(Servico(fila)).Ids);
        Assert.Empty(fila.Listar(null));
    }

    /// <summary>De ponta a ponta: serviço grava o pedido, o worker executa, o histórico mostra o desfecho.</summary>
    [Fact]
    public async Task Com_a_chave_ligada_o_pedido_chega_ao_worker_e_o_desfecho_ao_historico()
    {
        LigarAChave(true);
        var fila = new FilaDeComandosSqlite(_banco.Fabrica);
        var servico = Servico(fila);
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(4));
        var sessao = Sessao(fila, coletaLigada: () => new ConfiguracoesDaBorda(_banco.Fabrica).Ler().Configuracao.ColetarBilhetes);

        var resposta = Pedir(servico);
        Assert.Empty(resposta.Problemas);
        Voltas(sessao, 15);

        Assert.Equal(4, _bilhetes.Listar(1).Count);
        Assert.Equal(0, _sim.Dispositivo(1).BilhetesNaMemoria);

        var historico = await servico.ListarComandos(new ListarComandosRequest { Inner = 1 }, null!).ConfigureAwait(true);
        var registrado = Assert.Single(historico.Comandos);
        Assert.Equal(TipoDeComandoIpc.ColetarBilhetes, registrado.Tipo);
        Assert.Equal(SituacaoIpc.Concluido, registrado.Situacao);
        Assert.Contains("4 gravado(s)", registrado.Resultado, StringComparison.Ordinal);
    }

    /// <summary>O pedido que chegou à fila com a chave ligada e a encontrou desligada no worker não coleta.</summary>
    [Fact]
    public void Worker_confere_a_chave_ao_receber_e_recusa_se_desligada()
    {
        LigarAChave(true);
        var fila = new FilaDeComandosSqlite(_banco.Fabrica);
        var servico = Servico(fila);
        _sim.Dispositivo(1).ComBilhetes(Bilhetes(2));
        var sessao = Sessao(fila, coletaLigada: () => new ConfiguracoesDaBorda(_banco.Fabrica).Ler().Configuracao.ColetarBilhetes);

        Assert.Empty(Pedir(servico).Problemas);
        LigarAChave(false);
        Voltas(sessao, 5);

        Assert.Equal(2, _sim.Dispositivo(1).BilhetesNaMemoria);
        Assert.Empty(_bilhetes.Listar());
        var registrado = Assert.Single(fila.Listar(1));
        Assert.Equal(SituacaoDoComando.Falhou, registrado.Situacao);
        Assert.Contains("catraca.coletar_bilhetes", registrado.Resultado, StringComparison.Ordinal);
    }

    private SessaoDeOperacao Sessao(FilaDeComandosSqlite fila, Func<bool> coletaLigada)
    {
        var sessao = new SessaoDeOperacao(
            _sim,
            [1],
            PadroesDeFabrica.TopFit4,
            new DecisorDeIngresso(new RepositorioDeIngressos(_banco.Fabrica)),
            _ => { },
            _ => { },
            relogio: () => _agora,
            comandos: fila,
            gravadorDeBilhetes: _bilhetes,
            coletaLigada: coletaLigada);

        sessao.Iniciar(Porta);
        for (var i = 0; i < 40 && sessao.Dispositivos[0].Maquina.Current is not DeviceState.Polling; i++)
        {
            Voltas(sessao, 1);
        }

        Assert.Equal(DeviceState.Polling, sessao.Dispositivos[0].Maquina.Current);
        return sessao;
    }

    /// <summary>Cada volta anda 1 s: o worker olha a fila de comandos a cada 500 ms.</summary>
    private void Voltas(SessaoDeOperacao sessao, int quantas)
    {
        for (var i = 0; i < quantas; i++)
        {
            _agora += TimeSpan.FromSeconds(1);
            sessao.UmaVolta();
        }
    }
}
