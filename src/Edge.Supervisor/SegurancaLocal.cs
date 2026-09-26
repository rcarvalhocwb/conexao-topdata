using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Contracts;
using Microsoft.AspNetCore.Hosting;

namespace Edge.Supervisor;

/// <summary>
/// O que protege o canal entre o painel e o serviço na máquina do evento.
/// </summary>
/// <remarks>
/// <para>
/// Duas camadas (ADR-0004): a permissão do named pipe, que diz quem pode abrir o canal, e
/// o token de sessão, que o painel apresenta em cada chamada.
/// </para>
/// <para>
/// O serviço roda como SYSTEM. Sem permissão explícita, o pipe que ele cria dá a um usuário
/// comum só leitura, e o painel — que roda como o operador — não conseguiria conversar.
/// </para>
/// </remarks>
public static class SegurancaLocal
{
    /// <summary>
    /// Garante o arquivo do token: reaproveita o que existe, ou gera um novo. Devolve o token.
    /// </summary>
    /// <remarks>
    /// No Windows, só SYSTEM e Administradores escrevem; os usuários da máquina leem, porque
    /// é o painel, rodando como o operador, que precisa apresentá-lo.
    /// </remarks>
    public static string GarantirToken(string arquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arquivo);

        // EDGE_TOKEN (desenvolvimento) ou o arquivo de uma subida anterior: o painel já
        // conhece esse token, e trocar a cada subida o desconectaria à toa.
        if (InstalacaoLocal.LerToken(arquivo) is { Length: > 0 } existente)
        {
            return existente;
        }

        var token = InterceptadorDeToken.GerarToken();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivo))!);
        File.WriteAllText(arquivo, token);

        if (OperatingSystem.IsWindows())
        {
            Restringir(arquivo);
        }

        return token;
    }

    /// <summary>Permissões do named pipe: SYSTEM e Administradores total; usuários leem e escrevem.</summary>
    [SupportedOSPlatform("windows")]
    public static void AplicarNoCanal(IWebHostBuilder construtor)
    {
        ArgumentNullException.ThrowIfNull(construtor);

        var seguranca = new PipeSecurity();
        seguranca.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        construtor.UseNamedPipes(opcoes => opcoes.PipeSecurity = seguranca);
    }

    [SupportedOSPlatform("windows")]
    private static void Restringir(string arquivo)
    {
        var seguranca = new FileSecurity();
        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));

        new FileInfo(arquivo).SetAccessControl(seguranca);
    }
}
