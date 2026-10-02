namespace Access.Application.Devices;

/// <summary>
/// Um bilhete que saiu da memória da catraca e ainda precisa ficar durável na base.
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.9 do docs/35 (R-68): <c>ColetarBilhete</c> (EI-039) <b>remove</b> o bilhete do
/// equipamento (FUN:40). Entre a chamada e a gravação, ele só existe aqui, na memória do
/// worker; por isso o <c>DevicePump</c> não pede o próximo enquanto este não for gravado.
/// </para>
/// <para>
/// O bilhete da DLL não traz número de sequência nem identificação de reinício do
/// equipamento (EI-039: tipo, data, hora ao minuto e código). <see cref="ColetaId"/> e
/// <see cref="Ordem"/> dizem <i>qual coleta</i> trouxe o bilhete e em que posição — servem à
/// auditoria, não à deduplicação, que é pelo conteúdo (ver <see cref="IGravadorDeBilhetes"/>).
/// </para>
/// </remarks>
/// <param name="Inner">Número da catraca.</param>
/// <param name="Bilhete">O que a catraca devolveu. O código só sai daqui mascarado e como impressão.</param>
/// <param name="ColetaId">A coleta (o comando do operador, quando houver) que trouxe o bilhete.</param>
/// <param name="Ordem">Posição do bilhete nesta coleta, a partir de 1.</param>
/// <param name="ColetadoEm">Relógio da borda no momento da coleta.</param>
public sealed record BilheteColetado(int Inner, Bilhete Bilhete, Guid ColetaId, int Ordem, DateTimeOffset ColetadoEm);

/// <summary>O que aconteceu com um bilhete levado à base.</summary>
public enum DesfechoDaGravacaoDoBilhete
{
    /// <summary>Gravado agora: é a primeira vez que a base o vê.</summary>
    Gravado,

    /// <summary>
    /// Já estava na base (mesmo bilhete devolvido de novo, ou tipo 128 cujo original já foi
    /// gravado). Nada foi gravado; o bilhete conta como coletado.
    /// </summary>
    Repetido,
}

/// <summary>
/// Quem torna um bilhete coletado durável (Etapa A.9 do docs/35).
/// </summary>
/// <remarks>
/// <para>
/// A implementação é a base local (<c>collected_ticket</c>, migração 015). Voltar sem exceção
/// quer dizer <b>durável</b>: o <c>DevicePump</c> só pede o próximo bilhete depois disso. Exceção
/// quer dizer "não gravado": o bilhete fica com o worker e a gravação é tentada de novo no
/// próximo passo — a catraca não é chamada enquanto isso.
/// </para>
/// <para>
/// Deduplicação pelo que o bilhete tem (catraca, data e hora ao minuto, tipo, impressão do
/// código) e pelo tipo 128 ("já retornado em coleta anterior", manual 5.2.2): um 128 cujo
/// original já está na base é <see cref="DesfechoDaGravacaoDoBilhete.Repetido"/>; um 128 sem
/// original é gravado como está, para não perder a marcação.
/// </para>
/// </remarks>
public interface IGravadorDeBilhetes
{
    /// <summary>Grava, ou reconhece como repetido. Exceção: não gravado.</summary>
    DesfechoDaGravacaoDoBilhete Gravar(BilheteColetado bilhete);
}
