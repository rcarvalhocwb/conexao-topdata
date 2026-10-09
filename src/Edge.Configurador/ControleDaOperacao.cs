using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Edge.Supervisor.Instalacao;

namespace Edge.Configurador;

/// <summary>
/// O que o painel pede ao assistente por linha de comando, porque exige administrador:
/// parar e iniciar a operação (o serviço) e liberar o firewall.
/// </summary>
/// <remarks>
/// O painel roda sem administrador, de propósito. Para parar as catracas, ele abre o
/// assistente com <c>--parar-operacao</c>; o Windows pede a permissão (UAC), o assistente
/// faz só isso e sai com um código. Nenhuma janela abre.
/// </remarks>
internal static class ControleDaOperacao
{
    /// <summary>Nome do serviço do Windows (nome técnico mantido desde a instalação original).</summary>
    public const string NomeDoServico = "ConexaoTopdataEdge";

    public const string ArgumentoParar = "--parar-operacao";
    public const string ArgumentoIniciar = "--iniciar-operacao";
    public const string ArgumentoLiberarFirewall = "--liberar-firewall";

    /// <summary>Códigos de saída que o painel entende.</summary>
    public const int Ok = 0;
    public const int Falhou = 1;
    public const int ServicoNaoInstalado = 2;

    /// <summary>Executa o argumento de controle, se houver. Nulo: não era um controle.</summary>
    public static int? Executar(string[] argumentos)
    {
        if (argumentos.Contains(ArgumentoParar))
        {
            return Parar();
        }

        if (argumentos.Contains(ArgumentoIniciar))
        {
            return Iniciar();
        }

        if (argumentos.Contains(ArgumentoLiberarFirewall))
        {
            return LiberarFirewall(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Worker", "Edge.Worker.X86.exe")))
                ? Ok
                : Falhou;
        }

        return null;
    }

    /// <summary>
    /// Para o serviço. Ao parar, ele encerra o programa das catracas: elas deixam de ser
    /// atendidas pelo sistema até a operação ser iniciada de novo.
    /// </summary>
    public static int Parar() => Mudar(ServiceControllerStatus.Stopped, s => s.Stop());

    /// <summary>Inicia o serviço.</summary>
    public static int Iniciar() => Mudar(ServiceControllerStatus.Running, s => s.Start());

    /// <summary>Se a regra de entrada das catracas existe. Nulo se não deu para conferir.</summary>
    public static bool? FirewallLiberado()
    {
        var codigo = Netsh(FirewallDasCatracas.ArgumentosParaConferir);
        return codigo switch
        {
            0 => true,
            1 => false,
            _ => null,
        };
    }

    /// <summary>Recria a regra de entrada das catracas (TCP, só da sub-rede local).</summary>
    public static bool LiberarFirewall(string programaDoWorker) =>
        FirewallLiberado() is true || Netsh(FirewallDasCatracas.ArgumentosParaCriar(programaDoWorker)) == 0;

    private static int Mudar(ServiceControllerStatus desejado, Action<ServiceController> acao)
    {
        try
        {
            using var servico = new ServiceController(NomeDoServico);

            if (servico.Status != desejado)
            {
                acao(servico);
                servico.WaitForStatus(desejado, TimeSpan.FromSeconds(60));
            }

            return Ok;
        }
        catch (InvalidOperationException erro) when (erro.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1060 })
        {
            // 1060: o serviço não existe (instalação incompleta).
            return ServicoNaoInstalado;
        }
        catch (Exception erro) when (erro is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            Contracts.RegistroDeFalhas.Gravar("assistente", erro);
            return Falhou;
        }
    }

    private static int Netsh(string argumentos)
    {
        try
        {
            using var processo = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "netsh.exe"),
                Arguments = argumentos,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            })!;
            processo.StandardOutput.ReadToEnd();
            return processo.WaitForExit(30_000) ? processo.ExitCode : -1;
        }
        catch (Exception erro) when (erro is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }
}
