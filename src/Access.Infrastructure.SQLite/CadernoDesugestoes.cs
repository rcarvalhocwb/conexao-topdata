using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Stub implementation of CadernoDesugestoes.
/// TODO: Implement full suggestion management logic.
/// </summary>
public sealed class CadernoDesugestoes
{
    private readonly FabricaDaTelemetria _fabrica;

    public CadernoDesugestoes(FabricaDaTelemetria fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>
    /// Placeholder for saving suggestions.
    /// </summary>
    public void Gravar(int catraca, object sugestao, string? sessao, DateTimeOffset agora)
    {
        // Stub implementation
    }

    /// <summary>
    /// Placeholder for listing suggestions.
    /// </summary>
    public IReadOnlyList<(string Id, string Campo, string Atual, string Sugerido)> ListarAbertas(int catraca)
    {
        return new List<(string, string, string, string)>();
    }

    /// <summary>
    /// Placeholder for marking suggestions as used/discarded.
    /// </summary>
    public void RegistrarDestino(string sugestaoId, string situacao, string operador, DateTimeOffset agora)
    {
        // Stub implementation
    }
}
