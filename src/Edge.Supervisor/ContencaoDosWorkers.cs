using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Edge.Supervisor;

/// <summary>
/// Prende os workers ao serviço: quando o serviço some, por qualquer motivo, os workers somem
/// junto.
/// </summary>
/// <remarks>
/// <para>
/// Defeito relatado pelo dono do produto (01/10, docs/29): o <c>Kill(entireProcessTree)</c> só
/// roda na parada limpa (<see cref="WorkerSupervisor.Encerrar"/>). Com o serviço morto pelo
/// Gerenciador de Tarefas ou por uma queda, os workers ficavam órfãos, gravando "catraca em
/// operação" na base — e o painel do serviço seguinte mostrava "Atendendo" sem catraca na rede.
/// </para>
/// <para>
/// No Windows, um Job Object com <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: o kernel mata todo
/// processo do job quando o último identificador dele fecha — e o serviço é o único que o tem
/// aberto, então ele fecha quando o serviço morre, de qualquer jeito. Só kernel32, sem pacote.
/// Fora do Windows não há contenção (o produto roda no Windows; o resto é desenvolvimento e
/// CI); a vigia do processo pai, no worker, continua valendo em todo sistema.
/// </para>
/// </remarks>
public interface IContencaoDeProcessos : IDisposable
{
    /// <summary>Como a contenção funciona aqui, para o diagnóstico.</summary>
    string Descricao { get; }

    /// <summary>Põe o processo recém-criado sob a contenção.</summary>
    /// <returns>Falso se não foi possível (o worker roda assim mesmo; a vigia do pai cobre).</returns>
    bool Conter(Process processo);
}

/// <summary>Fábrica da contenção certa para o sistema.</summary>
public static class ContencaoDosWorkers
{
    /// <summary>
    /// A contenção por Job Object no Windows; sem contenção nos outros sistemas ou se o
    /// Windows recusar criar o job (o motivo fica na descrição).
    /// </summary>
    public static IContencaoDeProcessos Criar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SemContencao("sem Job Object fora do Windows; o worker vigia o serviço (--pai)");
        }

        try
        {
            return new ContencaoPorJobObject();
        }
        catch (System.ComponentModel.Win32Exception erro)
        {
            return new SemContencao($"o Windows recusou o Job Object ({erro.NativeErrorCode}); o worker vigia o serviço (--pai)");
        }
    }
}

/// <summary>Nenhuma contenção: <see cref="Conter"/> sempre devolve falso.</summary>
public sealed class SemContencao(string descricao) : IContencaoDeProcessos
{
    public string Descricao { get; } = descricao;

    public bool Conter(Process processo) => false;

    public void Dispose()
    {
    }
}

/// <summary>
/// Job Object do Windows com <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>. Fechar (ou o serviço
/// morrer) mata todos os workers postos nele.
/// </summary>
public sealed class ContencaoPorJobObject : IContencaoDeProcessos
{
    // JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation e o limite que mata ao fechar.
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private readonly IdentificadorDoJob _job;

    public ContencaoPorJobObject()
    {
        _job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (_job.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }

        var limites = new LimitesEstendidos
        {
            Basicos = new LimitesBasicos { Flags = JobObjectLimitKillOnJobClose },
        };

        if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformation, ref limites, (uint)Marshal.SizeOf<LimitesEstendidos>()))
        {
            var codigo = Marshal.GetLastPInvokeError();
            _job.Dispose();
            throw new System.ComponentModel.Win32Exception(codigo);
        }
    }

    public string Descricao => "Job Object do Windows: os workers morrem com o serviço";

    public bool Conter(Process processo)
    {
        ArgumentNullException.ThrowIfNull(processo);

        try
        {
            return AssignProcessToJobObject(_job, processo.SafeHandle);
        }
        catch (InvalidOperationException)
        {
            // O processo já saiu: não há o que conter.
            return false;
        }
    }

    public void Dispose() => _job.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct LimitesBasicos
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint Flags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ContadoresDeEs
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LimitesEstendidos
    {
        public LimitesBasicos Basicos;
        public ContadoresDeEs Es;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class IdentificadorDoJob : SafeHandleZeroOrMinusOneIsInvalid
    {
        public IdentificadorDoJob()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IdentificadorDoJob CreateJobObjectW(IntPtr atributos, IntPtr nome);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IdentificadorDoJob job, int classe, ref LimitesEstendidos informacao, uint tamanho);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IdentificadorDoJob job, SafeProcessHandle processo);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr identificador);
}
