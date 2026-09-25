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
            Servico = new EdgeControlService(Supervisor, relogio: () => Relogio, operacao: Operacao, nuvem: Nuvem);
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
}
