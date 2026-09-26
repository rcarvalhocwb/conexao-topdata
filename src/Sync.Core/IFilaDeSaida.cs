namespace Sync.Core;

/// <summary>
/// A outbox, vista pelo drenador.
/// </summary>
/// <remarks>
/// Existe como porta para que a política de drenagem — ordem, repetição, cartas mortas —
/// seja testada em milissegundos contra uma fila em memória, sem SQLite, sem disco e sem
/// relógio de parede. A implementação durável vive em <c>Access.Infrastructure.SQLite</c>.
/// </remarks>
public interface IFilaDeSaida
{
    /// <summary>
    /// Conectores que têm ao menos um item vencido para enviar.
    /// </summary>
    /// <remarks>
    /// Consultar isto antes de pedir lotes evita percorrer conectores ociosos a cada
    /// rodada — num parque parado, a grande maioria.
    /// </remarks>
    Task<IReadOnlyList<string>> ConectoresComPendenciaAsync(
        DateTimeOffset agora,
        CancellationToken cancelamento);

    /// <summary>
    /// Próximos itens de um conector, do mais prioritário ao mais antigo, ignorando os
    /// que ainda estão de castigo por espera.
    /// </summary>
    Task<IReadOnlyList<ItemDeSaida>> ProximosAsync(
        string conector,
        int limite,
        DateTimeOffset agora,
        CancellationToken cancelamento);

    /// <summary>Marca como entregues. Itens marcados nunca mais são lidos.</summary>
    Task MarcarEnviadosAsync(
        IReadOnlyCollection<string> ids,
        DateTimeOffset agora,
        CancellationToken cancelamento);

    /// <summary>
    /// Adia um item para nova tentativa, guardando o erro para diagnóstico.
    /// </summary>
    Task AdiarAsync(
        string id,
        int tentativas,
        DateTimeOffset proximaTentativaEm,
        string erro,
        CancellationToken cancelamento);

    /// <summary>
    /// Tira o item da fila e o preserva em cartas mortas, para reprocessamento manual.
    /// </summary>
    /// <remarks>
    /// <b>Nada é apagado.</b> Um item que o destino recusa é um problema de integração
    /// que alguém precisa ver — não lixo. Ele sai da fila para não travar o resto e fica
    /// guardado inteiro, com o erro, até que alguém decida o que fazer.
    /// </remarks>
    Task MoverParaCartasMortasAsync(
        string id,
        string erro,
        DateTimeOffset agora,
        CancellationToken cancelamento);
}
