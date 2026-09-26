using Edge.Supervisor;

namespace Integration.Tests;

public sealed class SupervisorTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

    /// <summary>Dublê de worker, controlável pelo teste.</summary>
    private sealed class WorkerFalso(string nome, int porta, params int[] inners) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel { get; set; } = true;

        public string Diagnostico { get; set; } = "saudável";

        public int Iniciadas { get; private set; }

        public int Mortes { get; private set; }

        public void Iniciar()
        {
            EstaVivo = true;
            EstaSaudavel = true;
            Iniciadas++;
        }

        public void Matar()
        {
            EstaVivo = false;
            Mortes++;
        }

        public void Dispose() => EstaVivo = false;

        public void Morrer() => EstaVivo = false;

        public void Travar()
        {
            EstaSaudavel = false;
            Diagnostico = "SEM BATIMENTO há 45s, travado em 'inner 8 em Polling'";
        }
    }

    /// <summary>Dublê que explode ao ser inspecionado, para provar o isolamento.</summary>
    private sealed class WorkerExplosivo(string nome, int porta) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = [99];

        public bool EstaVivo => throw new InvalidOperationException("worker inacessível");

        public bool EstaSaudavel => false;

        public string Diagnostico => "inacessível";

        public void Iniciar()
        {
        }

        public void Matar()
        {
        }

        public void Dispose()
        {
        }
    }

    private static (WorkerSupervisor Supervisor, WorkerFalso A, WorkerFalso B) Montar(Func<DateTimeOffset>? relogio = null)
    {
        var a = new WorkerFalso("setor-A", 3570, 1, 2, 3);
        var b = new WorkerFalso("setor-B", 3571, 4, 5, 6);
        var supervisor = new WorkerSupervisor([a, b], relogio ?? (() => Inicio));
        supervisor.Iniciar();
        return (supervisor, a, b);
    }

    [Fact]
    public void Workers_saudaveis_nao_sao_tocados()
    {
        var (supervisor, a, b) = Montar();

        var acoes = supervisor.Supervisionar();

        Assert.All(acoes, x => Assert.Equal("ok", x.Acao));
        Assert.Equal(1, a.Iniciadas);
        Assert.Equal(1, b.Iniciadas);
        Assert.Equal(0, a.Mortes);
    }

    [Fact]
    public void Worker_morto_e_reiniciado()
    {
        var (supervisor, a, _) = Montar();
        a.Morrer();

        var acoes = supervisor.Supervisionar();

        Assert.Equal(2, a.Iniciadas);
        Assert.Contains("reiniciado", acoes.Single(x => x.Worker == "setor-A").Acao, StringComparison.Ordinal);
    }

    /// <summary>
    /// O laço travado não lança: o processo segue vivo e mudo. Quem percebe é o
    /// watchdog, e a ação é matar — pedir parada não funciona com a DLL presa.
    /// </summary>
    [Fact]
    public void Worker_vivo_porem_travado_e_morto_e_recriado()
    {
        var (supervisor, a, _) = Montar();
        a.Travar();

        var acoes = supervisor.Supervisionar();
        var acao = acoes.Single(x => x.Worker == "setor-A");

        Assert.Equal(SituacaoDoWorker.SemBatimento, acao.Situacao);
        Assert.Equal(1, a.Mortes);
        Assert.Equal(2, a.Iniciadas);
        Assert.Contains("laço travado", acao.Acao, StringComparison.Ordinal);
    }

    /// <summary>O ponto central: a falha de um grupo não encosta no outro.</summary>
    [Fact]
    public void Falha_de_um_grupo_nao_afeta_o_outro()
    {
        var (supervisor, a, b) = Montar();
        a.Travar();

        supervisor.Supervisionar();

        Assert.Equal(1, b.Iniciadas);
        Assert.Equal(0, b.Mortes);
        Assert.Equal(SituacaoDoWorker.Saudavel, supervisor.Situacao(b));
    }

    /// <summary>
    /// Nem inspecionar um worker pode derrubar a supervisão dos demais.
    /// </summary>
    [Fact]
    public void Worker_inacessivel_e_isolado_sem_parar_a_supervisao()
    {
        var bom = new WorkerFalso("setor-bom", 3570, 1);
        var ruim = new WorkerExplosivo("setor-ruim", 3571);
        var supervisor = new WorkerSupervisor([bom, ruim], () => Inicio);
        supervisor.Iniciar();

        var acoes = supervisor.Supervisionar();

        Assert.Equal("ok", acoes.Single(x => x.Worker == "setor-bom").Acao);
        Assert.Equal(SituacaoDoWorker.Quarentena, acoes.Single(x => x.Worker == "setor-ruim").Situacao);
    }

    /// <summary>
    /// Reiniciar sem limite esconde a causa. Depois do teto, o grupo é isolado e o
    /// operador precisa agir.
    /// </summary>
    [Fact]
    public void Reinicios_demais_na_janela_levam_a_quarentena()
    {
        var agora = Inicio;
        var a = new WorkerFalso("setor-A", 3570, 1);
        var supervisor = new WorkerSupervisor([a], () => agora, reiniciosMaximos: 3);
        supervisor.Iniciar();

        for (var i = 0; i < 4; i++)
        {
            a.Morrer();
            supervisor.Supervisionar();
            agora = agora.AddMinutes(1); // além do backoff, dentro da janela
        }

        Assert.Equal(SituacaoDoWorker.Quarentena, supervisor.Situacao(a));
        Assert.Equal(3, supervisor.Reinicios("setor-A"));
    }

    /// <summary>
    /// Reinícios espalhados no tempo não são o mesmo problema que reinícios em rajada.
    /// </summary>
    [Fact]
    public void Reinicios_fora_da_janela_nao_acumulam()
    {
        var agora = Inicio;
        var a = new WorkerFalso("setor-A", 3570, 1);
        var supervisor = new WorkerSupervisor(
            [a],
            () => agora,
            reiniciosMaximos: 3,
            janelaDeReinicios: TimeSpan.FromMinutes(5));
        supervisor.Iniciar();

        for (var i = 0; i < 6; i++)
        {
            a.Morrer();
            supervisor.Supervisionar();
            agora = agora.AddMinutes(10); // cada falha fora da janela da anterior
        }

        Assert.NotEqual(SituacaoDoWorker.Quarentena, supervisor.Situacao(a));
    }

    [Fact]
    public void Backoff_evita_reinicio_imediato_em_rajada()
    {
        var agora = Inicio;
        var a = new WorkerFalso("setor-A", 3570, 1);
        var supervisor = new WorkerSupervisor([a], () => agora);
        supervisor.Iniciar();

        a.Morrer();
        supervisor.Supervisionar();
        var iniciadasAposPrimeiro = a.Iniciadas;

        // Sem avançar o relógio, a próxima passagem não pode reiniciar de novo.
        a.Morrer();
        var acao = supervisor.Supervisionar().Single();

        Assert.Equal(iniciadasAposPrimeiro, a.Iniciadas);
        Assert.Contains("backoff", acao.Acao, StringComparison.Ordinal);
    }

    [Fact]
    public void Porta_repetida_entre_workers_e_recusada()
    {
        var a = new WorkerFalso("A", 3570, 1);
        var b = new WorkerFalso("B", 3570, 2);

        var erro = Assert.Throws<ArgumentException>(() => new WorkerSupervisor([a, b]));

        Assert.Contains("Porta TCP repetida", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equipamento_em_dois_workers_e_recusado()
    {
        var a = new WorkerFalso("A", 3570, 1, 2);
        var b = new WorkerFalso("B", 3571, 2, 3);

        var erro = Assert.Throws<ArgumentException>(() => new WorkerSupervisor([a, b]));

        Assert.Contains("mais de um worker", erro.Message, StringComparison.Ordinal);
    }
}
