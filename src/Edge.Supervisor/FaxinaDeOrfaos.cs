using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Edge.Supervisor;

/// <summary>Um processo da máquina, como a faxina o vê.</summary>
/// <param name="Pid">Identificador do processo.</param>
/// <param name="Caminho">Executável completo; nulo se o Windows não deixou ler.</param>
/// <param name="PaiPid">Quem o criou (no Windows, o PID não muda quando o pai morre).</param>
/// <param name="IniciadoEm">Hora de início; nula se o Windows não deixou ler.</param>
public sealed record ProcessoDaMaquina(int Pid, string? Caminho, int? PaiPid, DateTime? IniciadoEm);

/// <summary>O que a faxina precisa do sistema operacional. Injetável para teste.</summary>
public interface IProcessosDaMaquina
{
    /// <summary>Os processos com este nome de executável (sem a extensão).</summary>
    IReadOnlyList<ProcessoDaMaquina> PorNome(string nome);

    /// <summary>O processo com este PID, ou nulo se ele não existe.</summary>
    ProcessoDaMaquina? PorPid(int pid);

    /// <summary>Mata o processo e espera ele sair.</summary>
    void Matar(int pid);
}

/// <summary>
/// Na partida, o serviço encerra os workers órfãos que uma partida anterior deixou para trás.
/// </summary>
/// <remarks>
/// <para>
/// Defeito relatado pelo dono do produto (01/10, docs/29): workers simulados de uma partida
/// anterior, órfãos depois de o serviço morrer sem encerrá-los, seguiam gravando "catraca em
/// operação" — e, com a versão anterior, nada os parava. Esta faxina os encerra antes de o
/// serviço subir os workers novos.
/// </para>
/// <para>
/// <b>Só mata o que é nosso e está órfão.</b> Um processo só é encerrado se: (1) o executável
/// dele é, caminho completo, um dos executáveis de worker desta instalação; (2) não é este
/// serviço nem filho dele; e (3) o pai que o criou não existe mais — ou o PID do pai foi
/// reaproveitado por um processo que começou <b>depois</b> dele. Um worker cujo pai está vivo
/// (outra instância do serviço, uma sessão de bancada aberta num terminal) fica. Caminho ou
/// hora ilegíveis (sem permissão) também ficam: na dúvida, não mata.
/// </para>
/// </remarks>
public static class FaxinaDeOrfaos
{
    /// <summary>Encerra os órfãos e devolve uma linha por processo encerrado ou poupado com motivo.</summary>
    /// <param name="executaveisDosWorkers">Caminho completo de cada executável de worker configurado.</param>
    /// <param name="pidDoServico">Este processo.</param>
    /// <param name="maquina">O sistema operacional.</param>
    public static IReadOnlyList<string> Executar(
        IEnumerable<string> executaveisDosWorkers,
        int pidDoServico,
        IProcessosDaMaquina maquina)
    {
        ArgumentNullException.ThrowIfNull(executaveisDosWorkers);
        ArgumentNullException.ThrowIfNull(maquina);

        var comparacao = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var nossos = executaveisDosWorkers
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(Path.GetFullPath)
            .ToHashSet(comparacao);
        var linhas = new List<string>();

        foreach (var nome in nossos.Select(Path.GetFileNameWithoutExtension).Distinct(comparacao))
        {
            foreach (var candidato in maquina.PorNome(nome!))
            {
                if (candidato.Pid == pidDoServico
                    || candidato.PaiPid == pidDoServico
                    || candidato.Caminho is not { } caminho
                    || !nossos.Contains(Path.GetFullPath(caminho)))
                {
                    continue;
                }

                if (!Orfao(candidato, maquina))
                {
                    linhas.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"worker {candidato.Pid} poupado: o processo que o criou ({candidato.PaiPid}) continua de pé."));
                    continue;
                }

                try
                {
                    maquina.Matar(candidato.Pid);
                    linhas.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"worker órfão {candidato.Pid} encerrado (de uma partida anterior do serviço, processo {candidato.PaiPid})."));
                }
                catch (Exception erro) when (erro is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    linhas.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"worker órfão {candidato.Pid} não pôde ser encerrado: {erro.Message}"));
                }
            }
        }

        return linhas;
    }

    private static bool Orfao(ProcessoDaMaquina candidato, IProcessosDaMaquina maquina)
    {
        if (candidato.PaiPid is not { } paiPid)
        {
            // Sem saber quem criou: na dúvida, não mata.
            return false;
        }

        if (maquina.PorPid(paiPid) is not { } pai)
        {
            return true;
        }

        // PID reaproveitado: o "pai" de agora nasceu depois do filho, então não é quem o criou.
        return pai.IniciadoEm is { } paiNasceu
            && candidato.IniciadoEm is { } filhoNasceu
            && paiNasceu > filhoNasceu;
    }
}

/// <summary>
/// Os processos do Windows, por kernel32: a lista com o PID do pai (Toolhelp32), o caminho
/// completo (<c>QueryFullProcessImageNameW</c>, que lê também um processo de 32 bits a partir
/// do serviço de 64) e a hora de início.
/// </summary>
public sealed class ProcessosDoWindows : IProcessosDaMaquina
{
    private const uint Th32csSnapProcess = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public IReadOnlyList<ProcessoDaMaquina> PorNome(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        var exe = nome + ".exe";
        return [.. Todos().Where(p => string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase))
            .Select(p => Descrever(p.Pid, p.PaiPid))];
    }

    public ProcessoDaMaquina? PorPid(int pid)
    {
        var entrada = Todos().FirstOrDefault(p => p.Pid == pid);
        return entrada.Exe is null ? null : Descrever(entrada.Pid, entrada.PaiPid);
    }

    public void Matar(int pid)
    {
        using var processo = Process.GetProcessById(pid);
        processo.Kill(entireProcessTree: true);
        processo.WaitForExit(TimeSpan.FromSeconds(10));
    }

    private static ProcessoDaMaquina Descrever(int pid, int paiPid)
    {
        DateTime? inicio = null;
        try
        {
            using var processo = Process.GetProcessById(pid);
            inicio = processo.StartTime;
        }
        catch (Exception erro) when (erro is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Sem permissão ou já saiu: hora desconhecida, e a faxina não mata.
        }

        return new ProcessoDaMaquina(pid, Caminho(pid), paiPid, inicio);
    }

    private static string? Caminho(int pid)
    {
        var identificador = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (identificador == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var texto = new char[1024];
            var tamanho = (uint)texto.Length;
            return QueryFullProcessImageNameW(identificador, 0, texto, ref tamanho) ? new string(texto, 0, (int)tamanho) : null;
        }
        finally
        {
            _ = CloseHandle(identificador);
        }
    }

    private static List<(int Pid, int PaiPid, string? Exe)> Todos()
    {
        var lista = new List<(int, int, string?)>();
        var foto = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (foto == IntPtr.Zero || foto == new IntPtr(-1))
        {
            return lista;
        }

        try
        {
            var entrada = new EntradaDeProcesso { Tamanho = (uint)Marshal.SizeOf<EntradaDeProcesso>() };
            for (var ok = Process32FirstW(foto, ref entrada); ok; ok = Process32NextW(foto, ref entrada))
            {
                lista.Add(((int)entrada.Pid, (int)entrada.PaiPid, entrada.Exe));
            }
        }
        finally
        {
            _ = CloseHandle(foto);
        }

        return lista;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EntradaDeProcesso
    {
        public uint Tamanho;
        public uint Uso;
        public uint Pid;
        public UIntPtr HeapPadrao;
        public uint Modulo;
        public uint Threads;
        public uint PaiPid;
        public int Prioridade;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Exe;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr foto, ref EntradaDeProcesso entrada);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr foto, ref EntradaDeProcesso entrada);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenProcess(uint acesso, [MarshalAs(UnmanagedType.Bool)] bool herdar, uint pid);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr processo, uint flags, [Out] char[] nome, ref uint tamanho);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr identificador);
}
