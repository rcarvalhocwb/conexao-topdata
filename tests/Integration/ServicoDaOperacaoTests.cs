using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// O serviço lendo da base local o que os workers gravaram, e entregando ao painel.
/// </summary>
/// <remarks>Ver docs/ADR/ADR-0024-worker-e-servico-pelo-banco-local.md.</remarks>
public sealed class ServicoDaOperacaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 21, 0, 0, TimeSpan.Zero);
    private const string Qr = "1000000001";

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

    private sealed class Cenario : IDisposable
    {
        public Cenario()
        {
            Banco.Migrar();
            Operacao = new Operacao(Banco.Fabrica);
            Repositorio = new RepositorioDeIngressos(Banco.Fabrica);
            Repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), Agora);
            Repositorio.Ingerir([new IngressoRecebido("zet", "T1", Qr, Qr, Categoria: "meia")], Agora.AddHours(-1));

            Supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1, 2)]);
            Supervisor.Iniciar();
            Nuvem = new EstadoDaNuvem();
            Servico = new EdgeControlService(
                Supervisor,
                relogio: () => Relogio,
                operacao: Operacao,
                nuvem: Nuvem,
                consultas: new ConsultasDaOperacao(Banco.Fabrica),
                configuracoes: new ConfiguracoesDaBorda(Banco.Fabrica),
                pastaDeDados: "C:\\dados");
        }

        public DateTimeOffset Relogio { get; set; } = Agora;

        public BancoTemporario Banco { get; } = new();

        public Operacao Operacao { get; }

        public RepositorioDeIngressos Repositorio { get; }

        public WorkerSupervisor Supervisor { get; }

        public EstadoDaNuvem Nuvem { get; }

        public EdgeControlService Servico { get; }

        public void Catraca(int inner, bool online, DateTimeOffset atualizadoEm, string estado = "Polling") =>
            Operacao.GravarSituacao(
            [
                new SituacaoDoEquipamento($"inner-{inner}", inner, "setor-a", estado, online, "4.2.0", 0,
                    atualizadoEm.AddSeconds(-1), "liberado", atualizadoEm),
            ]);

        public void Dispose() => Banco.Dispose();
    }

    [Fact]
    public async Task O_painel_ve_cada_catraca_pelo_que_o_worker_gravou_e_desconfia_de_noticia_velha()
    {
        using var c = new Cenario();
        c.Catraca(1, online: true, Agora.AddSeconds(-2));
        c.Catraca(2, online: true, Agora - EdgeControlService.NoticiaVelha - TimeSpan.FromSeconds(1));

        var lista = await c.Servico.ListarEquipamentos(new ListarEquipamentosRequest(), null!);

        var um = lista.Equipamentos.Single(e => e.Inner == 1);
        Assert.True(um.EmOperacao);
        Assert.True(um.Saudavel);
        Assert.Equal("Polling", um.Estado);
        Assert.Equal("4.2.0", um.Firmware);
        Assert.Equal("liberado", um.UltimaDecisao);

        // Worker parou de dar notícia: a última situação gravada dizia "em operação", mas
        // não dá mais para acreditar nela.
        var dois = lista.Equipamentos.Single(e => e.Inner == 2);
        Assert.False(dois.EmOperacao);
        Assert.False(dois.Saudavel);
        Assert.StartsWith("sem notícia do worker", dois.Estado, StringComparison.Ordinal);

        var estado = await c.Servico.ObterEstado(new ObterEstadoRequest(), null!);
        Assert.Equal(1, estado.EquipamentosConectados);
        Assert.Equal(2, estado.EquipamentosCadastrados);
    }

    [Fact]
    public async Task Catraca_cadastrada_que_ainda_nao_apareceu_nao_conta_como_em_operacao()
    {
        using var c = new Cenario();

        var lista = await c.Servico.ListarEquipamentos(new ListarEquipamentosRequest(), null!);

        Assert.All(lista.Equipamentos, e =>
        {
            Assert.False(e.EmOperacao);
            Assert.False(e.Saudavel);
            Assert.Equal("aguardando a catraca", e.Estado);
        });
    }

    [Fact]
    public async Task Contagens_e_fila_de_envio_vem_da_base()
    {
        using var c = new Cenario();
        var (_, tentativa) = c.Repositorio.TentarUsar(Qr, "portao-1", "inner-1", Agora.AddMinutes(-1));
        c.Repositorio.ConfirmarPassagemFisica(tentativa, Agora.AddMinutes(-1).AddSeconds(2));
        c.Repositorio.TentarUsar("9999999999", "portao-1", "inner-1", Agora.AddMinutes(-1));

        var estado = await c.Servico.ObterEstado(new ObterEstadoRequest(), null!);

        Assert.Equal(1, estado.Liberados);
        Assert.Equal(1, estado.Negados);
        Assert.Equal(1, estado.Giros);
        Assert.Equal(1, estado.LiberadosUltimos5Minutos);
        Assert.False(estado.InternetDisponivel);
        Assert.Equal(NivelDeDegradacao.T1SemInternet, estado.Nivel);

        c.Nuvem.RegistrarSucesso(Agora.AddSeconds(-30));
        estado = await c.Servico.ObterEstado(new ObterEstadoRequest(), null!);
        Assert.True(estado.InternetDisponivel);
        Assert.Equal(NivelDeDegradacao.T0Normal, estado.Nivel);
    }

    [Fact]
    public async Task Tentativa_gravada_pelo_worker_chega_a_todos_os_paineis_abertos()
    {
        using var c = new Cenario();
        var acompanhamento = new AcompanhamentoDaOperacao(c.Operacao, c.Servico.Eventos, TimeSpan.FromSeconds(1));
        acompanhamento.ComecarDoFim();

        using var portaria = c.Servico.Eventos.Assinar();
        using var supervisao = c.Servico.Eventos.Assinar();

        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-2", Agora);
        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-2", Agora.AddSeconds(5));

        Assert.Equal(2, acompanhamento.UmaLeitura());
        Assert.Equal(0, acompanhamento.UmaLeitura());

        foreach (var painel in new[] { portaria, supervisao })
        {
            Assert.True(painel.Leitor.TryRead(out var primeiro));
            Assert.True(painel.Leitor.TryRead(out var segundo));

            Assert.Equal(2, primeiro.Inner);
            Assert.Equal(ResultadoDoAcesso.Permitido, primeiro.Resultado);
            Assert.Equal("Liberado · meia", primeiro.MensagemAoOperador);
            Assert.Equal("meia", primeiro.Categoria);
            Assert.DoesNotContain(Qr, primeiro.CredencialMascarada, StringComparison.Ordinal);

            Assert.Equal(ResultadoDoAcesso.Negado, segundo.Resultado);
            Assert.Equal("Negado · já utilizado", segundo.MensagemAoOperador);
        }

        // Quem abre o painel depois recebe os recentes.
        using var atrasado = c.Servico.Eventos.Assinar();
        Assert.True(atrasado.Leitor.TryRead(out _));
        Assert.True(atrasado.Leitor.TryRead(out _));
        Assert.False(atrasado.Leitor.TryRead(out _));

        await Task.CompletedTask;
    }

    [Fact]
    public void Painel_que_fecha_deixa_de_receber()
    {
        var difusor = new DifusorDeEventos();
        var assinatura = difusor.Assinar();
        Assert.Equal(1, difusor.Assinantes);

        assinatura.Dispose();

        Assert.Equal(0, difusor.Assinantes);
        Assert.True(difusor.Writer.TryWrite(new EventoDeAcesso { EventoId = "x" }));
    }

    [Fact]
    public async Task Tela_de_acessos_filtra_ordena_do_mais_recente_e_nunca_mostra_o_codigo()
    {
        using var c = new Cenario();
        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-1", Agora.AddMinutes(-3));
        c.Repositorio.TentarUsar("9999999999", "portao-1", "inner-2", Agora.AddMinutes(-2));
        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-1", Agora.AddMinutes(-1));

        var todos = await c.Servico.ListarAcessos(new ListarAcessosRequest(), null!);
        Assert.Equal(3, todos.Acessos.Count);
        Assert.True(todos.Acessos[0].RecebidoEm.ToDateTimeOffset() > todos.Acessos[2].RecebidoEm.ToDateTimeOffset());
        Assert.DoesNotContain(Qr, todos.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("9999999999", todos.ToString(), StringComparison.Ordinal);

        var negados = await c.Servico.ListarAcessos(new ListarAcessosRequest { Resultado = FiltroDeResultado.Negados }, null!);
        Assert.Equal(2, negados.Acessos.Count);

        var catraca2 = await c.Servico.ListarAcessos(new ListarAcessosRequest { Inner = 2 }, null!);
        Assert.Equal("Negado · código não cadastrado", Assert.Single(catraca2.Acessos).MensagemAoOperador);

        var limitado = await c.Servico.ListarAcessos(new ListarAcessosRequest { Limite = 2 }, null!);
        Assert.Equal(2, limitado.Acessos.Count);
        Assert.True(limitado.HaMais);
    }

    [Fact]
    public async Task Configuracao_do_evento_pela_tela_valida_grava_e_avisa_que_precisa_reiniciar()
    {
        using var c = new Cenario();

        var atual = await c.Servico.ObterConfiguracao(new ObterConfiguracaoRequest(), null!);
        Assert.Equal(8, atual.TipoDeLeitor);
        Assert.False(atual.NuvemLigada);

        var ruim = atual.Clone();
        ruim.MensagemPadrao = new string('x', 40);
        var recusada = await c.Servico.GravarConfiguracao(new GravarConfiguracaoRequest { Configuracao = ruim }, null!);
        Assert.False(recusada.Gravada);
        Assert.NotEmpty(recusada.Problemas);

        var nova = atual.Clone();
        nova.MensagemPadrao = "Bem-vindo";
        nova.LeitorDaUrna = false;
        var gravada = await c.Servico.GravarConfiguracao(new GravarConfiguracaoRequest { Configuracao = nova, Operador = "ana" }, null!);
        Assert.True(gravada.Gravada);
        Assert.True(gravada.ExigeReinicio);

        var lida = await c.Servico.ObterConfiguracao(new ObterConfiguracaoRequest(), null!);
        Assert.Equal("Bem-vindo", lida.MensagemPadrao);
        Assert.False(lida.LeitorDaUrna);
    }

    [Fact]
    public async Task Prestacao_de_contas_por_categoria_catraca_hora_e_motivo()
    {
        using var c = new Cenario();
        var (_, t1) = c.Repositorio.TentarUsar(Qr, "portao-1", "inner-1", Agora.AddMinutes(-50));
        c.Repositorio.ConfirmarPassagemFisica(t1, Agora.AddMinutes(-50).AddSeconds(2));
        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-2", Agora.AddMinutes(-40));
        c.Repositorio.TentarUsar("9999999999", "portao-1", "inner-2", Agora.AddMinutes(-30));

        var contas = await c.Servico.ObterPrestacaoDeContas(
            new ObterPrestacaoDeContasRequest
            {
                Desde = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(Agora.AddHours(-2)),
                Ate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(Agora),
            },
            null!);

        Assert.Equal(1, contas.Liberados);
        Assert.Equal(1, contas.Giros);
        Assert.Equal(2, contas.Negados);

        var meia = Assert.Single(contas.PorCategoria);
        Assert.Equal(("meia", 1L, 1L), (meia.Categoria, meia.Liberados, meia.Giros));

        Assert.Equal([1, 2], contas.PorCatraca.Select(l => l.Inner));
        Assert.Equal(2, contas.PorCatraca.Single(l => l.Inner == 2).Negados);

        Assert.Equal(3, contas.PorHora.Sum(h => h.Liberados + h.Negados));
        Assert.Contains(contas.Negativas, n => n.Mensagem == "Negado · já utilizado" && n.Quantidade == 1);
        Assert.Contains(contas.Negativas, n => n.Mensagem == "Negado · código não cadastrado" && n.Quantidade == 1);
    }

    [Fact]
    public async Task Consulta_de_codigo_mostra_situacao_e_historico_sem_devolver_o_codigo()
    {
        using var c = new Cenario();
        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-1", Agora.AddMinutes(-10));
        c.Repositorio.TentarUsar(Qr, "portao-1", "inner-1", Agora.AddMinutes(-5));

        var achado = await c.Servico.ConsultarCodigo(new ConsultarCodigoRequest { Codigo = Qr }, null!);

        Assert.True(achado.Encontrado);
        Assert.Equal("meia", achado.Categoria);
        Assert.Equal("consumido", achado.Situacao);
        Assert.Equal(1, achado.UsosFeitos);
        Assert.Equal(1, achado.UsosMaximos);
        Assert.Equal(2, achado.Historico.Count);
        Assert.DoesNotContain(Qr, achado.ToString(), StringComparison.Ordinal);

        var ausente = await c.Servico.ConsultarCodigo(new ConsultarCodigoRequest { Codigo = "0000000000" }, null!);
        Assert.False(ausente.Encontrado);
        Assert.DoesNotContain("0000000000", ausente.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sincronizacao_e_diagnostico_para_as_telas()
    {
        using var c = new Cenario();
        c.Nuvem.Configurada = true;
        c.Nuvem.RegistrarFalha("cartões: HTTP 503");

        var sincronizacao = await c.Servico.ObterSincronizacao(new ObterSincronizacaoRequest(), null!);
        Assert.True(sincronizacao.Configurada);
        Assert.Equal("cartões: HTTP 503", sincronizacao.UltimaFalha);
        Assert.Equal(1, Assert.Single(sincronizacao.Provedores).Codigos);

        var diagnostico = await c.Servico.ObterDiagnostico(new ObterDiagnosticoRequest(), null!);
        Assert.Equal("C:\\dados", diagnostico.PastaDeDados);
        Assert.Equal("setor-a", Assert.Single(diagnostico.Workers).Nome);
    }
}
