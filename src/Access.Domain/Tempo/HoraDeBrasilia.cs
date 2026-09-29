namespace Access.Domain.Tempo;

/// <summary>
/// O relógio do evento: horário de Brasília, independente do fuso configurado no Windows.
/// </summary>
/// <remarks>
/// A catraca guarda data e hora <b>sem fuso</b> (dia, mês, ano com dois dígitos, hora,
/// minuto e segundo — manual 4.6.1). A borda acerta esse relógio em horário de Brasília e
/// lê de volta no mesmo horário. Usar o fuso do Windows aqui faria o carimbo da catraca
/// mudar conforme o PC em que o serviço roda.
/// </remarks>
public static class HoraDeBrasilia
{
    /// <summary>Identificador IANA do fuso do evento.</summary>
    public const string Identificador = "America/Sao_Paulo";

    /// <summary>O fuso do evento.</summary>
    public static TimeZoneInfo Fuso { get; } = Localizar();

    /// <summary>O instante, visto no relógio do evento.</summary>
    public static DateTimeOffset NoEvento(DateTimeOffset instante) => TimeZoneInfo.ConvertTime(instante, Fuso);

    /// <summary>Uma data e hora sem fuso, lida no relógio do evento, como instante.</summary>
    public static DateTimeOffset DoEvento(DateTime dataEHora)
    {
        var semFuso = DateTime.SpecifyKind(dataEHora, DateTimeKind.Unspecified);
        return new DateTimeOffset(semFuso, Fuso.GetUtcOffset(semFuso));
    }

    // No Windows o .NET traduz o nome IANA; o nome do Windows fica de reserva. Brasil não tem
    // horário de verão desde 2019: se nenhum dos dois existir, UTC−3 fixo está correto.
    private static TimeZoneInfo Localizar()
    {
        foreach (var id in new[] { Identificador, "E. South America Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var fuso))
            {
                return fuso;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(Identificador, TimeSpan.FromHours(-3), "Horário de Brasília", "Horário de Brasília");
    }
}
