using Access.Application.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace Edge.Worker.X86;

/// <summary>
/// Hospedeiro do worker: confere pré-requisitos e sobe o laço.
/// </summary>
/// <remarks>
/// Deliberadamente fino. Toda a lógica está em <c>Edge.Worker</c>, que roda e é testada
/// em qualquer plataforma; aqui só se decide qual adapter injetar. Um assembly x86 não
/// carrega num processo de teste x64, então código que precisa de teste não pode morar
/// neste projeto.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("Conexao Topdata — Edge.Worker.X86");

        var preRequisitos = VerificadorDePreRequisitos.Verificar();

        foreach (var item in preRequisitos)
        {
            var marca = item.Atendido switch
            {
                true => "ok  ",
                false => "FALHA",
                null => "conferir",
            };

            Console.WriteLine($"[{marca}] {item.Id}: {item.Mensagem}");
        }

        var impeditivos = VerificadorDePreRequisitos.Impeditivos(preRequisitos);
        if (impeditivos.Count > 0)
        {
            Console.Error.WriteLine($"{impeditivos.Count} pré-requisito(s) impeditivo(s). O worker não sobe.");
            return 1;
        }

        // O adapter real existe desde que o SDK 6.0.2.0 chegou. O que ainda não aconteceu é
        // ele conversar com uma catraca: este passo é o ensaio HIL-STACK-01, e a primeira
        // chamada nativa é onde se descobre se um processo .NET 10 de 32 bits carrega a DLL.
        var porta = LerPorta(args);

        using var adapter = new TopdataInnerAdapter();

        Console.WriteLine($"Abrindo a porta {porta}...");

        try
        {
            var abertura = adapter.AbrirPorta(porta);
            Console.WriteLine($"  {abertura}");

            if (abertura.Status is AdapterStatus.FalhaDeDependencia)
            {
                Console.Error.WriteLine(
                    "Retorno 8 (GPF). As causas documentadas são: DLL não registrada, .NET " +
                    "Framework 3.5 ausente, versões incompatíveis das DLLs de apoio, ou " +
                    "arquitetura errada. Rode installer/verificar-ambiente.ps1.");
                return 2;
            }

            if (abertura.Status is not AdapterStatus.Ok)
            {
                Console.Error.WriteLine("A porta não abriu. O worker não sobe sem ela.");
                return 1;
            }

            Console.WriteLine("Porta aberta. O laço da máquina de estados entra na Fase 2.");
            return 0;
        }
        catch (DllNotFoundException erro)
        {
            // É aqui que HIL-STACK-01 responde "não" — e é uma resposta, não um defeito.
            Console.Error.WriteLine(
                $"A EasyInner.dll não foi encontrada ou não pôde ser carregada: {erro.Message}");
            Console.Error.WriteLine(
                "Se o ambiente estiver correto e mesmo assim falhar, o caminho é mover só este " +
                "hospedeiro para .NET Framework 4.8, atrás do mesmo IPC. Ver docs/12.");
            return 2;
        }
        catch (BadImageFormatException erro)
        {
            Console.Error.WriteLine(
                $"A DLL foi encontrada mas é de arquitetura incompatível: {erro.Message}");
            Console.Error.WriteLine("Este processo precisa ser de 32 bits. Confira PlatformTarget.");
            return 2;
        }
    }

    /// <summary>Lê a porta de <c>--porta N</c>. O supervisor sempre a informa.</summary>
    private static int LerPorta(string[] args)
    {
        var indice = Array.IndexOf(args, "--porta");

        return indice >= 0
            && indice + 1 < args.Length
            && int.TryParse(args[indice + 1], out var porta)
                ? porta
                : VerificadorDePreRequisitos.PortaPadrao;
    }
}
