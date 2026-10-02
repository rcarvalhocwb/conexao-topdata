using System.Diagnostics;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// O worker morre com o serviço (docs/29, defeito de 01/10): Job Object no Windows, faxina dos
/// órfãos na partida e, no worker, a vigia do pai (essa em <c>Unit.Tests</c>).
/// </summary>
/// <remarks>
/// O Job Object e a leitura de processos do Windows só existem lá: esses testes rodam no CI
/// Windows e, fora dele, voltam sem afirmar nada (padrão de <see cref="InstalacaoRealTests"/>).
/// O resto — o supervisor entregando o processo à contenção e a regra de quem a faxina mata —
/// é provado em qualquer sistema, por abstração.
/// </remarks>
public sealed class WorkerMorreComOServicoTests
{
    private sealed class ContencaoQueAnota(bool aceita) : IContencaoDeProcessos
    {
        public List<int> Contidos { get; } = [];

        public string Descricao => "contenção de teste";

        public bool Conter(Process processo)
        {
            Contidos.Add(processo.Id);
            return aceita;
        }

        public void Dispose()
        {
        }
    }

    private sealed class MaquinaFalsa : IProcessosDaMaquina
    {
        public Dictionary<int, ProcessoDaMaquina> Processos { get; } = [];

        public List<int> Mortos { get; } = [];

        public IReadOnlyList<ProcessoDaMaquina> PorNome(string nome) =>
            [.. Processos.Values.Where(p => p.Caminho is { } c && Path.GetFileNameWithoutExtension(c) == nome)];

        public ProcessoDaMaquina? PorPid(int pid) => Processos.GetValueOrDefault(pid);

        public void Matar(int pid)
        {
            Mortos.Add(pid);
            Processos.Remove(pid);
        }
    }

    private static readonly string Pasta = Path.Combine(Path.GetTempPath(), "instalacao-xacess");
    private static readonly string NossoWorker = Path.Combine(Pasta, "Worker", "Edge.Worker.X86.exe");
    private static readonly DateTime Ontem = new(2026, 9, 30, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void O_supervisor_poe_cada_worker_que_sobe_na_contencao()
    {
        var contencao = new ContencaoQueAnota(aceita: true);
        using var worker = new ProcessoDeWorker("contido", 3570, [1], Cobaia(), argumentosExtras: ["--esperar"], contencao: contencao);

        try
        {
            worker.Iniciar();
            Assert.True(worker.EstaVivo);
            Assert.Single(contencao.Contidos);
            Assert.DoesNotContain(worker.UltimasLinhas(), l => l.Contains("fora da contenção", StringComparison.Ordinal));
        }
        finally
        {
            worker.Matar();
        }
    }

    [Fact]
    public void Contencao_recusada_nao_impede_o_worker_e_fica_no_diagnostico()
    {
        var contencao = new ContencaoQueAnota(aceita: false);
        using var worker = new ProcessoDeWorker("solto", 3570, [1], Cobaia(), argumentosExtras: ["--esperar"], contencao: contencao);

        try
        {
            worker.Iniciar();
            Assert.True(worker.EstaVivo);
            Assert.Contains(worker.UltimasLinhas(), l => l.Contains("fora da contenção (contenção de teste)", StringComparison.Ordinal));
        }
        finally
        {
            worker.Matar();
        }
    }

    [Fact]
    public void Faxina_encerra_so_o_worker_desta_instalacao_cujo_pai_morreu()
    {
        var maquina = new MaquinaFalsa();

        // Órfão: o serviço que o criou (500) não existe mais.
        maquina.Processos[11] = new(11, NossoWorker, 500, Ontem);

        // Órfão com o PID do pai reaproveitado por um programa que começou depois dele.
        maquina.Processos[12] = new(12, NossoWorker, 600, Ontem);
        maquina.Processos[600] = new(600, @"C:\Windows\notepad.exe", 4, Ontem.AddHours(1));

        // Pai vivo e anterior a ele: outra instância do serviço ou uma bancada aberta — fica.
        maquina.Processos[13] = new(13, NossoWorker, 700, Ontem);
        maquina.Processos[700] = new(700, Path.Combine(Pasta, "Servico", "Edge.Supervisor.exe"), 4, Ontem.AddMinutes(-1));

        // Mesmo nome, outra instalação: não é nosso.
        maquina.Processos[14] = new(14, Path.Combine(Path.GetTempPath(), "outra", "Edge.Worker.X86.exe"), 501, Ontem);

        // Caminho ilegível (sem permissão): na dúvida, fica.
        maquina.Processos[15] = new(15, null, 502, Ontem);

        // Filho deste serviço: fica.
        maquina.Processos[16] = new(16, NossoWorker, 900, Ontem);

        // Sem saber quem o criou: fica.
        maquina.Processos[17] = new(17, NossoWorker, null, Ontem);

        var linhas = FaxinaDeOrfaos.Executar([NossoWorker], pidDoServico: 900, maquina);

        Assert.Equal([11, 12], maquina.Mortos);
        Assert.Equal(2, linhas.Count(l => l.Contains("encerrado", StringComparison.Ordinal)));
        Assert.Contains(linhas, l => l.Contains("worker 13 poupado", StringComparison.Ordinal));
    }

    /// <summary>
    /// O Job Object de verdade: fechar a contenção — o que acontece quando o serviço morre de
    /// qualquer jeito — mata o worker. Só no Windows.
    /// </summary>
    [Fact]
    public void No_windows_fechar_o_job_object_mata_o_worker()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Job Object só existe no Windows; lá o teste roda no CI.
        }

        var contencao = ContencaoDosWorkers.Criar();
        Assert.IsType<ContencaoPorJobObject>(contencao);

        using var processo = Process.Start(new ProcessStartInfo(Cobaia(), "--esperar") { UseShellExecute = false })!;

        try
        {
            Assert.True(contencao.Conter(processo));
            Assert.False(processo.HasExited);

            contencao.Dispose();

            Assert.True(processo.WaitForExit(TimeSpan.FromSeconds(10)), "o worker sobreviveu ao fechamento do Job Object.");
        }
        finally
        {
            if (!processo.HasExited)
            {
                processo.Kill();
            }
        }
    }

    /// <summary>
    /// A faxina de verdade, no Windows: um "worker" cujo pai já morreu (cmd que o lançou e saiu)
    /// é encerrado; um filho do próprio teste, com o pai vivo, fica. Só no Windows: no Linux o
    /// órfão é adotado por outro processo e o PID do pai muda.
    /// </summary>
    [Fact]
    public void No_windows_a_faxina_encerra_o_orfao_de_verdade_e_poupa_o_filho_vivo()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Toolhelp32 e QueryFullProcessImageName só existem no Windows; lá o teste roda no CI.
        }

        var cobaia = Cobaia();
        using var filho = Process.Start(new ProcessStartInfo(cobaia, "--esperar") { UseShellExecute = false })!;

        // O cmd lança a cobaia sem esperar e sai: a cobaia fica órfã.
        using (var lancador = Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" /b \"{cobaia}\" --esperar")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!)
        {
            lancador.WaitForExit();
        }

        var maquina = new ProcessosDoWindows();
        var nome = Path.GetFileNameWithoutExtension(cobaia);
        ProcessoDaMaquina? orfao = null;
        SpinWait.SpinUntil(
            () => (orfao = maquina.PorNome(nome).FirstOrDefault(p => p.Pid != filho.Id && maquina.PorPid(p.PaiPid ?? 0) is null)) is not null,
            TimeSpan.FromSeconds(10));
        Assert.NotNull(orfao);

        try
        {
            FaxinaDeOrfaos.Executar([cobaia], Environment.ProcessId, maquina);

            Assert.Null(maquina.PorPid(orfao.Pid));
            Assert.False(filho.HasExited);
        }
        finally
        {
            filho.Kill();
            if (maquina.PorPid(orfao.Pid) is not null)
            {
                maquina.Matar(orfao.Pid);
            }
        }
    }

    private static string Cobaia()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);

        var nome = OperatingSystem.IsWindows() ? "CrashProbe.exe" : "CrashProbe";
        var candidatos = Directory
            .EnumerateFiles(Path.Combine(raiz.FullName, "tests", "CrashProbe", "bin"), nome, SearchOption.AllDirectories)
            .Where(c => File.Exists(Path.Combine(Path.GetDirectoryName(c)!, "CrashProbe.runtimeconfig.json")))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        Assert.True(candidatos.Count > 0, "o executável do CrashProbe não foi construído.");
        return candidatos[0];
    }
}
