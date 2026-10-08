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
    /// Grupo local cujos membros são os operadores do evento. Só eles (e administradores e o
    /// serviço) abrem o canal e leem o token. Quem não está no grupo não consegue comandar as
    /// catracas, mesmo sendo usuário da máquina (auditoria de 07/10: S03).
    /// </summary>
    public const string GrupoDosOperadores = "ConexaoTopdata Operadores";

    /// <summary>
    /// Garante o arquivo do token: reaproveita o que existe, ou gera um novo. Devolve o token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No Windows, a permissão do arquivo é reaplicada a cada partida, inclusive quando o
    /// arquivo já existe: instalações anteriores deixaram o token legível por todos os usuários,
    /// e isso não pode sobreviver a uma atualização.
    /// </para>
    /// </remarks>
    public static string GarantirToken(string arquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arquivo);

        // EDGE_TOKEN (desenvolvimento) ou o arquivo de uma subida anterior: o painel já
        // conhece esse token, e trocar a cada subida o desconectaria à toa.
        var existente = InstalacaoLocal.LerToken(arquivo);
        var token = existente is { Length: > 0 } ? existente : InterceptadorDeToken.GerarToken();

        if (existente is not { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivo))!);
            File.WriteAllText(arquivo, token);
        }

        if (OperatingSystem.IsWindows() && File.Exists(arquivo))
        {
            Restringir(arquivo);
        }

        return token;
    }

    /// <summary>
    /// Permissões do named pipe: SYSTEM e Administradores com controle total; o grupo dos
    /// operadores lê e escreve. Usuários comuns fora do grupo não entram.
    /// </summary>
    /// <remarks>
    /// Sem o grupo criado, só SYSTEM e Administradores entram, e o painel mostra "sem resposta do
    /// serviço" — melhor que abrir o canal para todos. O grupo é criado pelo instalador (ver
    /// installer/configurar-operador.ps1).
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static void AplicarNoCanal(IWebHostBuilder construtor)
    {
        ArgumentNullException.ThrowIfNull(construtor);

        var seguranca = new PipeSecurity();
        seguranca.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        if (SidDoGrupoDosOperadores() is { } operadores)
        {
            seguranca.AddAccessRule(new PipeAccessRule(operadores, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        }
        else
        {
            Console.Error.WriteLine($"Grupo '{GrupoDosOperadores}' não existe: o painel dos operadores não conecta até ele ser criado.");
        }

        // CurrentUserOnly vem ligado no Kestrel e não convive com PipeSecurity: o serviço
        // caía ao abrir o canal ("'pipeSecurity' must be null when 'options' contains
        // 'PipeOptions.CurrentUserOnly'"). Quem pode entrar é a ACL acima que decide.
        construtor.UseNamedPipes(opcoes =>
        {
            opcoes.CurrentUserOnly = false;
            opcoes.PipeSecurity = seguranca;
        });
    }

    /// <summary>O SID do grupo dos operadores, ou nulo se o grupo ainda não existe na máquina.</summary>
    [SupportedOSPlatform("windows")]
    public static SecurityIdentifier? SidDoGrupoDosOperadores()
    {
        try
        {
            return (SecurityIdentifier)new NTAccount(GrupoDosOperadores).Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A pasta de dados (banco, cópias, registros, cofre) só para SYSTEM e Administradores, por herança.
    /// O arquivo do token não entra aqui: ele mantém a leitura do grupo dos operadores (ver Restringir).
    /// </summary>
    /// <remarks>
    /// Pergunta S04 (respondida: permissão de pasta, sem cifragem). Protege o acesso.db, que tem o número
    /// do cartão em claro (ADR-0014 recusa SQLCipher). A permissão real só se confirma numa VM Windows.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static void RestringirPastaDeDados(string pasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);

        var raiz = new DirectoryInfo(pasta);
        if (!raiz.Exists)
        {
            return;
        }

        var herdam = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var diretorio = new DirectorySecurity();
        diretorio.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        diretorio.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
        diretorio.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
        raiz.SetAccessControl(diretorio);

        foreach (var subpasta in raiz.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            var seguranca = new DirectorySecurity();
            seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            seguranca.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
            seguranca.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
            subpasta.SetAccessControl(seguranca);
        }

        foreach (var arquivo in raiz.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (arquivo.Name.Equals("token", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var seguranca = new FileSecurity();
            seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            seguranca.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            seguranca.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            arquivo.SetAccessControl(seguranca);
        }
    }

    /// <summary>
    /// O token: SYSTEM e Administradores com controle total; o grupo dos operadores só lê. Nenhum
    /// usuário comum tem acesso, nem o de leitura.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void Restringir(string arquivo)
    {
        var seguranca = new FileSecurity();
        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));

        if (SidDoGrupoDosOperadores() is { } operadores)
        {
            seguranca.AddAccessRule(new FileSystemAccessRule(operadores, FileSystemRights.Read, AccessControlType.Allow));
        }

        new FileInfo(arquivo).SetAccessControl(seguranca);
    }
}
