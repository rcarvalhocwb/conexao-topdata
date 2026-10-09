namespace Access.Domain.Devices;

/// <summary>
/// Identidade de um evento vindo de um equipamento.
/// </summary>
/// <remarks>
/// A chave de deduplicação é <c>(DeviceId, BootId, DeviceSeq)</c>. É ela que torna
/// reenvio após reconexão e reconciliação de bilhetes naturalmente idempotentes.
/// Ver docs/ADR/ADR-0009-identidade-de-evento.md
/// </remarks>
/// <param name="DeviceId">Identificador do equipamento no nosso sistema.</param>
/// <param name="BootId">Ciclo de vida do equipamento desde o último reinício.</param>
/// <param name="DeviceSeq">Sequência monotônica dentro do <paramref name="BootId"/>.</param>
public readonly record struct DeviceEventKey(string DeviceId, string BootId, long DeviceSeq)
{
    public override string ToString() => $"{DeviceId}/{BootId}/{DeviceSeq}";
}

/// <summary>
/// Evento bruto recebido do equipamento, antes de qualquer interpretação.
/// </summary>
/// <remarks>
/// <para>
/// Guarda <b>três</b> carimbos de tempo. O do equipamento é preservado como veio e o
/// desvio é medido e alertado — nunca corrigido em silêncio.
/// </para>
/// <para>
/// Um evento de origem desconhecida é um evento válido. Ele é persistido íntegro.
/// Ver docs/ADR/ADR-0018-eventos-desconhecidos.md
/// </para>
/// </remarks>
public sealed record DeviceEvent
{
    public required Guid EventId { get; init; }

    public required DeviceEventKey Key { get; init; }

    public required EventOrigin Origin { get; init; }

    /// <summary>Complemento da origem, conforme devolvido pela DLL. Semântica varia.</summary>
    public byte Complement { get; init; }

    /// <summary>
    /// Conteúdo do campo <c>Cartao</c>: número de cartão, QR Code, senha digitada ou
    /// identificador biométrico, conforme a origem. Preservado como texto.
    /// </summary>
    public string? RawCardData { get; init; }

    /// <summary>Horário informado pelo equipamento. Pode estar errado.</summary>
    public DateTimeOffset? DeviceTime { get; init; }

    /// <summary>Horário em que a borda recebeu o evento. É por ele que se ordena.</summary>
    public required DateTimeOffset ReceivedTime { get; init; }

    /// <summary>Horário atribuído pelo servidor na sincronização, quando houver.</summary>
    public DateTimeOffset? ServerTime { get; init; }

    public required string CorrelationId { get; init; }

    /// <summary>
    /// Desvio entre o relógio do equipamento e o da borda, quando ambos são conhecidos.
    /// </summary>
    public TimeSpan? ClockDrift =>
        DeviceTime is { } device ? device - ReceivedTime : null;

    /// <summary>Cria um evento com identidade UUIDv7, ordenável por tempo.</summary>
    public static DeviceEvent Create(
        DeviceEventKey key,
        EventOrigin origin,
        DateTimeOffset receivedTime,
        string correlationId,
        byte complement = 0,
        string? rawCardData = null,
        DateTimeOffset? deviceTime = null) =>
        new()
        {
            EventId = Guid.CreateVersion7(receivedTime),
            Key = key,
            Origin = origin,
            Complement = complement,
            RawCardData = rawCardData,
            DeviceTime = deviceTime,
            ReceivedTime = receivedTime,
            CorrelationId = correlationId,
        };
}
