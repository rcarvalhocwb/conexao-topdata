using Access.Domain.Ticketing;

namespace Sync.Ingestao;

/// <summary>Uma página de ingressos vinda do provedor.</summary>
/// <param name="Itens">Ingressos desta página.</param>
/// <param name="ProximoCursor">
/// Marca de retomada devolvida pelo provedor. Nula quando ele não devolveu nada novo.
/// </param>
/// <param name="TemMais">Se há mais páginas imediatamente disponíveis.</param>
public sealed record PaginaDeIngressos(
    IReadOnlyList<IngressoRecebido> Itens,
    string? ProximoCursor,
    bool TemMais)
{
    /// <summary>Página sem nada, que não move o cursor.</summary>
    public static PaginaDeIngressos Vazia { get; } = new([], null, false);
}

/// <summary>
/// O provedor de ingressos, visto pela ingestão.
/// </summary>
/// <remarks>
/// <para>
/// <b>É a única peça específica de cada bilheteria.</b> Tudo o que é difícil — ordem,
/// retomada, idempotência, colisão, não perder ingresso numa falha — está no laço, não
/// aqui. Integrar um site novo é escrever esta interface e mais nada.
/// </para>
/// <para>
/// A borda <b>puxa</b>, e não recebe: a máquina fica na mesma rede das catracas e não
/// pode ter porta aberta para a internet. Provedor que só oferece <i>webhook</i> exige um
/// relé na nuvem, e a implementação desta interface puxa do relé.
/// Ver docs/16-multiplos-provedores-de-ingresso.md, seção 2.
/// </para>
/// </remarks>
public interface IFonteDeIngressos
{
    /// <summary>Identificador do provedor, igual ao cadastrado localmente.</summary>
    string Provedor { get; }

    /// <summary>
    /// Lê uma página a partir do cursor. Cursor nulo significa "do começo".
    /// </summary>
    /// <remarks>
    /// Ler do começo precisa ser seguro: é o que a varredura completa faz, e é o que
    /// acontece depois de uma reinstalação. A idempotência do destino é quem torna isso
    /// barato.
    /// </remarks>
    Task<PaginaDeIngressos> LerAsync(string? cursor, CancellationToken cancelamento);
}

/// <summary>Onde os ingressos lidos são gravados.</summary>
public interface IDestinoDeIngressos
{
    /// <summary>Aplica um lote. Precisa ser idempotente.</summary>
    ResultadoDaIngestao Aplicar(IReadOnlyCollection<IngressoRecebido> lote, DateTimeOffset agora);
}

/// <summary>
/// Onde a marca de retomada é guardada, para sobreviver a reinício.
/// </summary>
/// <remarks>
/// Por conector <b>e</b> por fluxo: a queda do fluxo incremental não pode fazer a
/// varredura completa recomeçar, nem o contrário.
/// </remarks>
public interface IArmazenamentoDeCursor
{
    string? Ler(string conector, string fluxo);

    void Gravar(string conector, string fluxo, string cursor, DateTimeOffset agora);

    void Apagar(string conector, string fluxo);
}
