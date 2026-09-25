using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Edge.Worker.Bancada;
using Edge.Worker.Operacao;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// O worker em operação, com o simulador no lugar da catraca: o que ele decide, o que ele
/// grava para o serviço, e o que ele nunca escreve.
/// </summary>
/// <remarks>Ver docs/ADR/ADR-0024-worker-e-servico-pelo-banco-local.md.</remarks>
public sealed class OperacaoTests
{
    private const string Qr = "1000000001";

    private sealed class Montagem : IDisposable
    {
        public Montagem(IReadOnlyList<int>? inners = null)
        {
            Banco.Migrar();
            Repositorio = new RepositorioDeIngressos(Banco.Fabrica);
            Repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), DateTimeOffset.UtcNow);
            Repositorio.Ingerir(
                [new IngressoRecebido("zet", "T1", Qr, Qr, Categoria: "inteira")],
                DateTimeOffset.UtcNow.AddMinutes(-5));

            Operacao = new Operacao(Banco.Fabrica);
            Sessao = new SessaoDeOperacao(
                Simulador,
                inners ?? [1],
                ConfiguracaoDeBancada.TopFit4(),
                new DecisorDeIngresso(Repositorio),
                Registro.Add,
                situacoes => Operacao.GravarSituacao(
                    [.. situacoes.Select(c => new SituacaoDoEquipamento(
                        c.DeviceId, c.Inner, "setor-a", c.Estado.ToString(), c.EmOperacao, c.Firmware,
                        c.TentativasDeReconexao, c.UltimoEventoEm, c.UltimaDecisao, DateTimeOffset.UtcNow))]),
                intervaloDePublicacao: TimeSpan.Zero);

            Sessao.Iniciar(3570);
            Voltas(12);
        }

        public BancoTemporario Banco { get; } = new();

        public InnerSimulator Simulador { get; } = new();

        public RepositorioDeIngressos Repositorio { get; }

        public Operacao Operacao { get; }

        public SessaoDeOperacao Sessao { get; }

        public List<string> Registro { get; } = [];

        public void Voltas(int quantas = 10)
        {
            for (var i = 0; i < quantas; i++)
            {
                Sessao.UmaVolta();
            }
        }

        public void Dispose()
        {
            Simulador.Dispose();
            Banco.Dispose();
        }
    }

    [Fact]
    public void A_catraca_entra_em_operacao_e_o_servico_ve_isso_no_banco()
    {
        using var m = new Montagem([1, 2]);

        var situacao = m.Operacao.ListarSituacao();

        Assert.Equal([1, 2], situacao.Select(s => s.Inner));
        Assert.All(situacao, s =>
        {
            Assert.True(s.Online, $"inner {s.Inner} em {s.Estado}");
            Assert.Equal("setor-a", s.Worker);
        });
    }

    [Fact]
    public void Liberacao_e_giro_viram_linha_para_o_painel_com_o_codigo_mascarado()
    {
        using var m = new Montagem();
        var antes = m.Operacao.UltimaSequencia();

        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), Qr),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        m.Voltas();

        var tentativa = Assert.Single(m.Operacao.TentativasDepoisDe(antes));
        Assert.True(tentativa.Liberou);
        Assert.True(tentativa.Girou);
        Assert.Equal("inteira", tentativa.Categoria);
        Assert.Equal("inner-1", tentativa.DeviceId);
        Assert.DoesNotContain(Qr, tentativa.CodigoMascarado, StringComparison.Ordinal);

        var resumo = m.Operacao.Resumir(DateTimeOffset.UtcNow);
        Assert.Equal(1, resumo.Liberados);
        Assert.Equal(1, resumo.Giros);
        Assert.Equal(1, resumo.LiberadosNosUltimos5Minutos);

        Assert.Equal("liberado", Assert.Single(m.Operacao.ListarSituacao()).UltimaDecisao);
    }

    [Fact]
    public void O_registro_da_operacao_nunca_tem_o_codigo_inteiro()
    {
        using var m = new Montagem();

        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), Qr),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        m.Voltas();
        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "5555555555"));
        m.Voltas();

        Assert.Contains(m.Registro, l => l.Contains("LIBERADO", StringComparison.Ordinal));
        Assert.Contains(m.Registro, l => l.Contains("NEGADO", StringComparison.Ordinal));
        Assert.DoesNotContain(m.Registro, l => l.Contains(Qr, StringComparison.Ordinal));
        Assert.DoesNotContain(m.Registro, l => l.Contains("5555555555", StringComparison.Ordinal));
    }

    [Fact]
    public void Base_indisponivel_para_a_publicacao_nao_para_a_catraca()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), DateTimeOffset.UtcNow);
        repositorio.Ingerir([new IngressoRecebido("zet", "T1", Qr, Qr)], DateTimeOffset.UtcNow.AddMinutes(-5));

        using var simulador = new InnerSimulator();
        var registro = new List<string>();
        var sessao = new SessaoDeOperacao(
            simulador, [1], ConfiguracaoDeBancada.TopFit4(), new DecisorDeIngresso(repositorio),
            registro.Add,
            _ => throw new IOException("database is locked"),
            intervaloDePublicacao: TimeSpan.Zero);

        sessao.Iniciar(3570);
        for (var i = 0; i < 12; i++)
        {
            sessao.UmaVolta();
        }

        simulador.Dispositivo(1).Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), Qr));
        for (var i = 0; i < 10; i++)
        {
            sessao.UmaVolta();
        }

        Assert.NotEmpty(simulador.Dispositivo(1).LiberacoesPedidas);
        Assert.Contains(registro, l => l.Contains("não foi possível publicar", StringComparison.Ordinal));
    }

    [Fact]
    public void Configuracao_do_evento_vai_e_volta_do_banco_e_recusa_o_que_a_catraca_nao_aceita()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);

        Assert.Equal(new ConfiguracaoDaOperacao(), configuracoes.Ler().Configuracao);

        var nova = new ConfiguracaoDaOperacao(
            TipoDeLeitor: 5, LeitorDaUrna: false, TempoDeAcionamento: 7, MensagemPadrao: "Bem-vindo",
            ConectorDoEspelho: "painel-tentativas", EsperaPeloGiroSegundos: 12);
        configuracoes.Gravar(nova, DateTimeOffset.UtcNow, "operador");

        var (lida, ilegiveis) = configuracoes.Ler();
        Assert.Equal(nova, lida);
        Assert.Empty(ilegiveis);

        // Espera pelo giro menor que o acionamento: o evento subiria antes do giro.
        Assert.Throws<ArgumentException>(() => configuracoes.Gravar(
            nova with { EsperaPeloGiroSegundos = 5 }, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => configuracoes.Gravar(
            nova with { MensagemPadrao = new string('x', 33) }, DateTimeOffset.UtcNow));
    }
}
