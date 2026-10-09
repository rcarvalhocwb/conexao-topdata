using System.Security.Cryptography;

namespace Edge.Supervisor;

/// <summary>
/// As sessões abertas pelo painel depois do login (ADR-0026). Ficam só na memória do serviço:
/// reiniciar o serviço pede login de novo.
/// </summary>
public sealed class SessoesDoPainel
{
    /// <summary>Sem uso por mais que isto, a sessão acaba.</summary>
    public static readonly TimeSpan Inatividade = TimeSpan.FromHours(12);

    private readonly Dictionary<string, (string UsuarioId, DateTimeOffset UltimoUso)> _sessoes = new(StringComparer.Ordinal);
    private readonly Lock _trava = new();

    /// <summary>Abre uma sessão para o usuário e devolve o token.</summary>
    public string Abrir(string usuarioId, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usuarioId);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        lock (_trava)
        {
            _sessoes[token] = (usuarioId, agora);
        }

        return token;
    }

    /// <summary>O usuário da sessão, renovando o uso; nulo se não existe ou expirou.</summary>
    public string? Usuario(string? token, DateTimeOffset agora)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_trava)
        {
            if (!_sessoes.TryGetValue(token, out var sessao))
            {
                return null;
            }

            if (agora - sessao.UltimoUso > Inatividade)
            {
                _sessoes.Remove(token);
                return null;
            }

            _sessoes[token] = (sessao.UsuarioId, agora);
            return sessao.UsuarioId;
        }
    }

    /// <summary>Encerra uma sessão.</summary>
    public void Encerrar(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        lock (_trava)
        {
            _sessoes.Remove(token);
        }
    }

    /// <summary>Encerra todas as sessões de um usuário (desativado, senha redefinida). Devolve quantas.</summary>
    public int EncerrarDoUsuario(string usuarioId)
    {
        lock (_trava)
        {
            var dele = _sessoes.Where(s => s.Value.UsuarioId == usuarioId).Select(s => s.Key).ToList();
            foreach (var token in dele)
            {
                _sessoes.Remove(token);
            }

            return dele.Count;
        }
    }
}
