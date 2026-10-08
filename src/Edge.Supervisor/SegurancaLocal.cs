using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Access.Infrastructure.SQLite;
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

    /// <summary>Um caminho que a restrição de permissão alcança.</summary>
    /// <param name="Caminho">Caminho absoluto.</param>
    /// <param name="Pasta">Pasta (com tudo o que estiver dentro) ou arquivo isolado.</param>
    public sealed record AlvoDaRestricao(string Caminho, bool Pasta);

    /// <summary>
    /// O que a restrição da pasta de dados alcança. Sempre, como primeiro alvo, a pasta da instalação inteira, que é nossa.
    /// Se a configuração puser o banco fora dela, só o que o serviço cria ao lado do banco: os arquivos
    /// do banco e da telemetria e as pastas <c>copias</c> e <c>registros</c>. Nunca a pasta do banco em
    /// si, que pode ser a raiz de um disco (achado E8-3 do docs/41).
    /// </summary>
    public static IReadOnlyList<AlvoDaRestricao> AlvosDaRestricao(string pastaDaInstalacao, string caminhoDoBanco)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDaInstalacao);
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoDoBanco);

        var instalacao = Path.GetFullPath(pastaDaInstalacao);
        var banco = Path.GetFullPath(caminhoDoBanco);
        var pastaDoBanco = Path.GetDirectoryName(banco)!;
        var alvos = new List<AlvoDaRestricao> { new(instalacao, Pasta: true) };

        if (EstaDentro(pastaDoBanco, instalacao))
        {
            return alvos;
        }

        string[] sufixos = ["", "-wal", "-shm", "-journal"];
        var telemetria = FabricaDaTelemetria.CaminhoAoLadoDe(banco);
        alvos.AddRange(sufixos.Select(s => new AlvoDaRestricao(banco + s, Pasta: false)));
        alvos.AddRange(sufixos.Select(s => new AlvoDaRestricao(telemetria + s, Pasta: false)));
        alvos.Add(new AlvoDaRestricao(Path.Combine(pastaDoBanco, "copias"), Pasta: true));
        alvos.Add(new AlvoDaRestricao(Path.Combine(pastaDoBanco, "registros"), Pasta: true));
        return alvos;
    }

    /// <summary>Verdadeiro se <paramref name="caminho"/> é <paramref name="pasta"/> ou fica dentro dela.</summary>
    public static bool EstaDentro(string caminho, string pasta)
    {
        var comparacao = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(caminho));
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pasta));

        return c.Equals(p, comparacao)
            || c.StartsWith(p + Path.DirectorySeparatorChar, comparacao);
    }

    /// <summary>
    /// Deixa os alvos (ver <see cref="AlvosDaRestricao"/>) só para SYSTEM e Administradores. O arquivo do
    /// token não entra aqui: ele mantém a leitura do grupo dos operadores (ver Restringir).
    /// </summary>
    /// <returns>
    /// O que não foi possível restringir, com o motivo. Uma falha aqui não impede o serviço de subir:
    /// as catracas não dependem dela, e o motivo vai para o registro.
    /// </returns>
    /// <remarks>
    /// Pergunta S04 (respondida: permissão de pasta, sem cifragem). Protege o acesso.db, que tem o número
    /// do cartão em claro (ADR-0014 recusa SQLCipher). A permissão real só se confirma numa VM Windows.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> RestringirPastaDeDados(IEnumerable<AlvoDaRestricao> alvos)
    {
        ArgumentNullException.ThrowIfNull(alvos);
        var falhas = new List<string>();

        foreach (var alvo in alvos)
        {
            if (alvo.Pasta)
            {
                RestringirArvore(alvo.Caminho, falhas);
            }
            else if (File.Exists(alvo.Caminho))
            {
                Tentar(alvo.Caminho, () => RestringirArquivo(new FileInfo(alvo.Caminho)), falhas);
            }
        }

        return falhas;
    }

    [SupportedOSPlatform("windows")]
    private static void RestringirArvore(string pasta, List<string> falhas)
    {
        var raiz = new DirectoryInfo(pasta);
        if (!raiz.Exists)
        {
            return;
        }

        Tentar(raiz.FullName, () => raiz.SetAccessControl(SegurancaDePasta()), falhas);

        foreach (var subpasta in raiz.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            Tentar(subpasta.FullName, () => subpasta.SetAccessControl(SegurancaDePasta()), falhas);
        }

        foreach (var arquivo in raiz.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (arquivo.Name.Equals("token", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Tentar(arquivo.FullName, () => RestringirArquivo(arquivo), falhas);
        }
    }

    private static void Tentar(string caminho, Action acao, List<string> falhas)
    {
        try
        {
            acao();
        }
        catch (Exception erro) when (erro is FileNotFoundException or DirectoryNotFoundException)
        {
            // Sumiu entre a enumeração e a aplicação (um -journal, por exemplo): nada a proteger.
        }
        catch (Exception erro) when (erro is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
        {
            falhas.Add($"{caminho}: {erro.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static DirectorySecurity SegurancaDePasta()
    {
        var herdam = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var seguranca = new DirectorySecurity();
        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
        return seguranca;
    }

    [SupportedOSPlatform("windows")]
    private static void RestringirArquivo(FileInfo arquivo)
    {
        var seguranca = new FileSecurity();
        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        arquivo.SetAccessControl(seguranca);
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
