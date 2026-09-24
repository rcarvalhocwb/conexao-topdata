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

        var preRequisitos = global::Edge.Worker.VerificadorDePreRequisitos.Verificar();

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

        var impeditivos = global::Edge.Worker.VerificadorDePreRequisitos.Impeditivos(preRequisitos);
        if (impeditivos.Count > 0)
        {
            Console.Error.WriteLine($"{impeditivos.Count} pré-requisito(s) impeditivo(s). O worker não sobe.");
            return 1;
        }

        // A vinculação P/Invoke real ainda não existe, e não será deduzida:
        // ver VinculacaoNativaPendente e docs/11-capacidades-do-sdk.md, seção 5.
        Console.Error.WriteLine(
            "Adapter real indisponível: as assinaturas da EasyInner.dll ainda não foram obtidas. " +
            "Rode vendor/topdata/fetch-sdk.ps1 e execute o ensaio HIL-STACK-01.");

        _ = args;
        return 2;
    }
}
