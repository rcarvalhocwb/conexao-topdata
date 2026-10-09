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
    /// <param name="arquivo">Caminho do arquivo do token.</param>
    /// <param name="dono">Dono a gravar no arquivo; nulo mantém o atual. Ver <see cref="DonoQuandoRodaComoSistema"/>.</param>
    public static string GarantirToken(string arquivo, SecurityIdentifier? dono = null)
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
            Restringir(arquivo, dono);
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
    /// <param name="alvos">O que restringir.</param>
    /// <param name="dono">
    /// Dono a gravar em cada alvo; nulo mantém o atual. Trocar só a permissão não basta: o dono de um
    /// arquivo reabre a permissão quando quiser (achado E8-2 do docs/41). Ver <see cref="DonoQuandoRodaComoSistema"/>.
    /// </param>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> RestringirPastaDeDados(IEnumerable<AlvoDaRestricao> alvos, SecurityIdentifier? dono = null)
    {
        ArgumentNullException.ThrowIfNull(alvos);
        var falhas = new List<string>();

        foreach (var alvo in alvos)
        {
            if (alvo.Pasta)
            {
                RestringirArvore(alvo.Caminho, dono, falhas);
            }
            else if (File.Exists(alvo.Caminho))
            {
                Tentar(alvo.Caminho, () => RestringirArquivo(new FileInfo(alvo.Caminho), dono), falhas);
            }
        }

        return falhas;
    }

    /// <summary>
    /// O SID do SYSTEM quando o processo roda como SYSTEM (o serviço instalado); nulo em qualquer outro
    /// caso (desenvolvimento, testes, o assistente).
    /// </summary>
    /// <remarks>
    /// Um processo sempre pode se tornar dono de um objeto em que tem permissão de trocar o dono, sem
    /// privilégio extra. Por isso o serviço grava a si mesmo, e não o grupo Administradores.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static SecurityIdentifier? DonoQuandoRodaComoSistema()
    {
        using var atual = WindowsIdentity.GetCurrent();
        return atual.User is { } usuario && usuario.IsWellKnown(WellKnownSidType.LocalSystemSid) ? usuario : null;
    }

    /// <summary>
    /// Os caminhos (dos que existem) cujo dono não é SYSTEM, Administradores nem TrustedInstaller.
    /// </summary>
    /// <remarks>
    /// Achado E8-2 do docs/41: um usuário comum consegue criar a pasta ou o <c>workers.json</c> antes da
    /// instalação e continuar dono deles. A partida troca o dono (ver <see cref="RestringirPastaDeDados"/>);
    /// o que continuar com outro dono depois disso foi preparado para impedir a troca, e o serviço não lê.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> ComDonoDesconhecido(IEnumerable<string> caminhos)
    {
        ArgumentNullException.ThrowIfNull(caminhos);
        var desconhecidos = new List<string>();

        foreach (var caminho in caminhos)
        {
            try
            {
                SecurityIdentifier? dono;
                if (Directory.Exists(caminho))
                {
                    dono = new DirectoryInfo(caminho).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                }
                else if (File.Exists(caminho))
                {
                    dono = new FileInfo(caminho).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                }
                else
                {
                    continue;
                }

                if (!DonoConfiavel(dono))
                {
                    desconhecidos.Add($"{caminho} (dono {dono?.Value ?? "ilegível"})");
                }
            }
            catch (Exception erro) when (erro is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
            {
                desconhecidos.Add($"{caminho} (dono ilegível: {erro.Message})");
            }
        }

        return desconhecidos;
    }

    [SupportedOSPlatform("windows")]
    private static bool DonoConfiavel(SecurityIdentifier? dono) =>
        dono is not null
        && (dono.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || dono.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || dono.Value == SidDoTrustedInstaller);

    private const string SidDoTrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    [SupportedOSPlatform("windows")]
    private static void RestringirArvore(string pasta, SecurityIdentifier? dono, List<string> falhas)
    {
        var raiz = new DirectoryInfo(pasta);
        if (!raiz.Exists)
        {
            return;
        }

        Tentar(raiz.FullName, () => raiz.SetAccessControl(SegurancaDePasta(dono)), falhas);

        foreach (var subpasta in raiz.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            Tentar(subpasta.FullName, () => subpasta.SetAccessControl(SegurancaDePasta(dono)), falhas);
        }

        foreach (var arquivo in raiz.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (arquivo.Name.Equals("token", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Tentar(arquivo.FullName, () => RestringirArquivo(arquivo, dono), falhas);
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
    private static DirectorySecurity SegurancaDePasta(SecurityIdentifier? dono)
    {
        var herdam = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var seguranca = new DirectorySecurity();
        if (dono is not null)
        {
            seguranca.SetOwner(dono);
        }

        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
        seguranca.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, herdam, PropagationFlags.None, AccessControlType.Allow));
        return seguranca;
    }

    [SupportedOSPlatform("windows")]
    private static void RestringirArquivo(FileInfo arquivo, SecurityIdentifier? dono)
    {
        var seguranca = new FileSecurity();
        if (dono is not null)
        {
            seguranca.SetOwner(dono);
        }

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
    private static void Restringir(string arquivo, SecurityIdentifier? dono)
    {
        var seguranca = new FileSecurity();
        if (dono is not null)
        {
            seguranca.SetOwner(dono);
        }

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
