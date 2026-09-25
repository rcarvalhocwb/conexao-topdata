namespace Sync.Core;

/// <summary>Item da outbox pronto para ser enviado a um sistema externo.</summary>
/// <param name="Id">Identificador da linha na outbox.</param>
/// <param name="Conector">Destino.</param>
/// <param name="TipoDoAgregado">Que espécie de fato é este: passagem, decisão, revogação.</param>
/// <param name="IdDoAgregado">Identificador do fato na base local.</param>
/// <param name="PayloadJson">Conteúdo já serializado, no formato do conector.</param>
/// <param name="Prioridade">Ver <see cref="PrioridadeDeSincronizacao"/>.</param>
/// <param name="ChaveDeIdempotencia">
/// Chave que o destino usa para reconhecer reenvio. Gerada na origem e <b>estável</b>:
/// é ela que impede que uma queda no meio do envio vire registro duplicado do outro lado.
/// </param>
/// <param name="Tentativas">Quantas vezes já se tentou enviar este item.</param>
/// <param name="CriadoEm">Quando o fato ocorreu na borda.</param>
public sealed record ItemDeSaida(
    string Id,
    string Conector,
    string TipoDoAgregado,
    string IdDoAgregado,
    string PayloadJson,
    int Prioridade,
    string ChaveDeIdempotencia,
    int Tentativas,
    DateTimeOffset CriadoEm);

/// <summary>Como o destino respondeu a um item.</summary>
public enum ResultadoDoEnvio
{
    /// <summary>Recebido e persistido do outro lado.</summary>
    Aceito,

    /// <summary>
    /// O destino já tinha este item, reconhecido pela chave de idempotência.
    /// Conta como sucesso: é o caminho normal depois de uma queda no meio do envio.
    /// </summary>
    Duplicado,

    /// <summary>
    /// Rede fora, destino fora, tempo esgotado, 5xx, limite de requisições.
    /// Vale repetir mais tarde.
    /// </summary>
    FalhaTemporaria,

    /// <summary>
    /// O destino recusou e vai recusar de novo: payload inválido, contrato mudou,
    /// referência inexistente. Repetir só queima a fila — vai para cartas mortas.
    /// </summary>
    FalhaPermanente,
}

/// <summary>Resposta do destino para um item do lote.</summary>
/// <param name="Id">O mesmo <see cref="ItemDeSaida.Id"/> enviado.</param>
/// <param name="Resultado">Veredito.</param>
/// <param name="Erro">Detalhe para diagnóstico. Nunca deve conter dado sensível.</param>
public sealed record RespostaDeItem(string Id, ResultadoDoEnvio Resultado, string? Erro = null);

/// <summary>
/// Ponte para um sistema externo: bilheteria, ERP de academia, aplicativo de condomínio,
/// portaria terceirizada, data warehouse.
/// </summary>
/// <remarks>
/// <para>
/// É a única coisa que um integrador precisa escrever para plugar um sistema novo. Tudo
/// o que é difícil — ordem por prioridade, repetição com espera crescente, idempotência,
/// cartas mortas, não perder nada numa queda — está no drenador, não aqui.
/// </para>
/// <para>
/// <b>Um conector nunca é chamado do caminho de decisão.</b> Ele roda em segundo plano,
/// e pode demorar o quanto for: a catraca já girou faz tempo.
/// </para>
/// <para>
/// A implementação precisa ser tolerante a reenvio. O drenador repete um item sempre que
/// não tiver certeza de que ele chegou — e "não tiver certeza" inclui o caso em que o
/// destino recebeu, gravou, e a resposta se perdeu na volta.
/// </para>
/// </remarks>
public interface IConectorDeSincronizacao
{
    /// <summary>
    /// Nome gravado na coluna <c>connector</c> da outbox. Estável: mudar renomeia o
    /// destino de tudo que já está na fila.
    /// </summary>
    string Nome { get; }

    /// <summary>
    /// Maior lote que este destino aceita de uma vez. O drenador nunca envia mais que isso.
    /// </summary>
    int TamanhoMaximoDoLote { get; }

    /// <summary>
    /// Envia o lote e responde <b>item a item</b>.
    /// </summary>
    /// <remarks>
    /// A resposta é por item, e não do lote inteiro, porque aceitação parcial é o caso
    /// comum: de 500 passagens, 499 entram e uma referencia um setor que foi apagado no
    /// sistema de origem. Tratar isso como falha do lote trava a fila para sempre.
    /// Item sem resposta correspondente é considerado não enviado e será repetido.
    /// </remarks>
    /// <param name="lote">Itens a enviar, já ordenados por prioridade.</param>
    /// <param name="cancelamento">Cancelamento.</param>
    /// <returns>Uma resposta por item processado.</returns>
    Task<IReadOnlyList<RespostaDeItem>> EnviarAsync(
        IReadOnlyList<ItemDeSaida> lote,
        CancellationToken cancelamento);
}
