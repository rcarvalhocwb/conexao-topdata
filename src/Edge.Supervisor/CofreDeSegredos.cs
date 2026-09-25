using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Edge.Supervisor;

/// <summary>Onde o serviço guarda segredos: a credencial da nuvem, hoje.</summary>
/// <remarks>
/// O segredo nunca fica em arquivo de configuração, nunca vai para log e nunca passa pelo
/// conector — quem o põe na requisição é o <see cref="CabecalhoDeSegredo"/>.
/// </remarks>
public interface ICofreDeSegredos
{
    /// <summary>O segredo, ou nulo se não foi gravado.</summary>
    string? Ler(string nome);

    /// <summary>Grava ou substitui.</summary>
    void Gravar(string nome, string valor);
}

/// <summary>
/// Cofre do Windows: DPAPI no escopo da máquina, arquivo com acesso só para SYSTEM e
/// Administradores.
/// </summary>
/// <remarks>
/// <para>
/// Escopo da <b>máquina</b>, e não do usuário, porque quem grava é o instalador (um
/// administrador) e quem lê é o serviço (outra conta). O arquivo cifrado não serve em
/// outro computador; a ACL impede que um usuário comum desta máquina o leia.
/// </para>
/// <para>Ver docs/ADR/ADR-0014-criptografia-e-biometria.md.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CofreDpapi : ICofreDeSegredos
{
    private readonly string _pasta;

    public CofreDpapi(string pasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        _pasta = pasta;
    }

    /// <inheritdoc />
    public string? Ler(string nome)
    {
        var caminho = Caminho(nome);

        if (!File.Exists(caminho))
        {
            return null;
        }

        var cifrado = File.ReadAllBytes(caminho);
        var aberto = ProtectedData.Unprotect(cifrado, Entropia(nome), DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(aberto);
    }

    /// <inheritdoc />
    public void Gravar(string nome, string valor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valor);

        Directory.CreateDirectory(_pasta);
        var caminho = Caminho(nome);
        var cifrado = ProtectedData.Protect(Encoding.UTF8.GetBytes(valor), Entropia(nome), DataProtectionScope.LocalMachine);

        File.WriteAllBytes(caminho, cifrado);
        Restringir(caminho);
    }

    private string Caminho(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);

        if (nome.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || nome.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Nome de segredo inválido.", nameof(nome));
        }

        return Path.Combine(_pasta, nome + ".segredo");
    }

    // A entropia amarra o arquivo ao nome: trocar um .segredo de lugar não o faz valer
    // como outro.
    private static byte[] Entropia(string nome) => Encoding.UTF8.GetBytes("ConexaoTopdata:" + nome);

    private static void Restringir(string caminho)
    {
        var seguranca = new FileSecurity();
        seguranca.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var conta in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            seguranca.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(conta, null), FileSystemRights.FullControl, AccessControlType.Allow));
        }

        new FileInfo(caminho).SetAccessControl(seguranca);
    }
}

/// <summary>Cofre em memória, para teste e desenvolvimento fora do Windows.</summary>
public sealed class CofreEmMemoria : ICofreDeSegredos
{
    private readonly Dictionary<string, string> _segredos = new(StringComparer.Ordinal);
    private readonly Lock _trava = new();

    public string? Ler(string nome)
    {
        lock (_trava)
        {
            return _segredos.GetValueOrDefault(nome);
        }
    }

    public void Gravar(string nome, string valor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valor);

        lock (_trava)
        {
            _segredos[nome] = valor;
        }
    }
}

/// <summary>
/// Põe o segredo da nuvem em cada requisição, lendo do cofre na hora.
/// </summary>
/// <remarks>
/// Lê a cada requisição para que trocar o segredo não exija reiniciar o serviço. Sem
/// segredo gravado, a requisição sai sem cabeçalho: é como as funções do painel funcionam
/// hoje (docs/22, seção 8.1), e quando elas passarem a exigir, a resposta 401 aparece
/// no painel como falha de sincronização.
/// </remarks>
public sealed class CabecalhoDeSegredo : DelegatingHandler
{
    /// <summary>Nome do segredo da nuvem no cofre.</summary>
    public const string NomeDoSegredo = "nuvem";

    private readonly ICofreDeSegredos _cofre;
    private readonly string _cabecalho;

    /// <param name="cofre">Onde está o segredo.</param>
    /// <param name="cabecalho">
    /// Cabeçalho que leva o segredo. <c>Authorization</c> vira <c>Bearer</c>.
    /// <c>A_CONFIRMAR</c> com quem corrigir as funções do painel.
    /// </param>
    public CabecalhoDeSegredo(ICofreDeSegredos cofre, string cabecalho = "Authorization")
    {
        ArgumentNullException.ThrowIfNull(cofre);
        ArgumentException.ThrowIfNullOrWhiteSpace(cabecalho);
        _cofre = cofre;
        _cabecalho = cabecalho;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_cofre.Ler(NomeDoSegredo) is { Length: > 0 } segredo)
        {
            if (string.Equals(_cabecalho, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", segredo);
            }
            else
            {
                request.Headers.Remove(_cabecalho);
                request.Headers.TryAddWithoutValidation(_cabecalho, segredo);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
