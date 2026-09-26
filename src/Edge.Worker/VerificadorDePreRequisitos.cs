using System.Runtime.InteropServices;

namespace Edge.Worker;

/// <summary>Um pré-requisito conferido antes de tentar carregar a DLL.</summary>
/// <param name="Id">Identificador estável, para log e para o pacote de diagnóstico.</param>
/// <param name="Atendido">
/// Verdadeiro quando verificável e satisfeito; falso quando verificável e violado;
/// <c>null</c> quando só uma pessoa ou a bancada pode confirmar.
/// </param>
/// <param name="Mensagem">Texto acionável, no idioma do operador.</param>
public sealed record PreRequisito(string Id, bool? Atendido, string Mensagem)
{
    /// <summary>Verdadeiro quando este item impede o worker de funcionar.</summary>
    public bool Impeditivo => Atendido is false;
}

/// <summary>
/// Confere o que a documentação aponta como causa do retorno 8, <b>antes</b> de chamar
/// a DLL.
/// </summary>
/// <remarks>
/// <para>
/// O manual lista quatro causas para o GPF: DLL não registrada, .NET Framework 3.5
/// ausente, versões incompatíveis das DLLs de apoio e arquitetura errada
/// (seção 7.2.2). Descobrir isso como "erro 8" no dia do evento é o pior momento
/// possível; descobrir na inicialização, com a instrução do que fazer, é o objetivo.
/// </para>
/// <para>
/// Mora em <c>Edge.Worker</c>, não no hospedeiro x86, justamente para poder ser testado:
/// um assembly x86 não carrega num processo de teste x64.
/// </para>
/// </remarks>
public static class VerificadorDePreRequisitos
{
    /// <summary>Porta padrão documentada. Cada worker usa a sua (ADR-0021).</summary>
    public const int PortaPadrao = 3570;

    /// <summary>Confere o ambiente atual.</summary>
    /// <param name="processoE64Bits">Sobrescreve a detecção, para teste.</param>
    /// <param name="ehWindows">Sobrescreve a detecção, para teste.</param>
    public static IReadOnlyList<PreRequisito> Verificar(bool? processoE64Bits = null, bool? ehWindows = null)
    {
        var e64 = processoE64Bits ?? Environment.Is64BitProcess;
        var windows = ehWindows ?? RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        return
        [
            new PreRequisito(
                "ARQUITETURA_X86",
                !e64,
                e64
                    ? "Este processo é de 64 bits e a EasyInner.dll é de 32. Compile com " +
                      "PlatformTarget=x86 (manual, seções 1.3.3 e 6.6)."
                    : "Processo de 32 bits, compatível com a EasyInner.dll."),

            new PreRequisito(
                "SISTEMA_WINDOWS",
                windows,
                windows
                    ? "Sistema Windows, compatível com a EasyInner.dll."
                    : "A EasyInner.dll só funciona em Windows. O laço e os testes rodam em " +
                      "qualquer plataforma contra o simulador, mas o hardware real exige Windows."),

            new PreRequisito(
                "DOTNET_FRAMEWORK_35",
                null,
                "Confirme que o .NET Framework 3.5 está habilitado em 'Ativar ou desativar " +
                "recursos do Windows'. A ausência dele é causa documentada de retorno 8."),

            new PreRequisito(
                "DLLS_REGISTRADAS",
                null,
                "Confirme que o instalador do SDK Inner Acesso registrou as DLLs. " +
                "DLL ausente, corrompida ou de versão divergente também produz retorno 8."),

            new PreRequisito(
                "PORTA_DEDICADA",
                null,
                $"Confirme a porta TCP deste worker (padrão {PortaPadrao}) e que as catracas " +
                "deste grupo apontam para ela. Cada worker escuta numa porta própria."),
        ];
    }

    /// <summary>Itens que impedem o worker de funcionar.</summary>
    public static IReadOnlyList<PreRequisito> Impeditivos(IEnumerable<PreRequisito> itens)
    {
        ArgumentNullException.ThrowIfNull(itens);
        return [.. itens.Where(i => i.Impeditivo)];
    }
}
