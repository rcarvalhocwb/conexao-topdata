using System.Globalization;

namespace Desktop.ViewModels;

/// <summary>
/// O fuso do evento: horário de Brasília. Telas, filtros e relatórios usam este fuso, e não
/// o do Windows — um PC configurado em outro fuso não pode deslocar o dia do evento.
/// </summary>
public static class FusoDoEvento
{
    /// <summary>Identificador IANA do fuso do evento.</summary>
    public const string Identificador = "America/Sao_Paulo";

    /// <summary>O fuso do evento.</summary>
    public static TimeZoneInfo Fuso { get; } = Localizar();

    /// <summary>O instante, visto no relógio do evento.</summary>
    public static DateTimeOffset NoEvento(DateTimeOffset instante) => TimeZoneInfo.ConvertTime(instante, Fuso);

    /// <summary>Uma data e hora do relógio do evento, como instante.</summary>
    public static DateTimeOffset DoEvento(DateTime dataEHora)
    {
        var semFuso = DateTime.SpecifyKind(dataEHora, DateTimeKind.Unspecified);
        return new DateTimeOffset(semFuso, Fuso.GetUtcOffset(semFuso));
    }

    /// <summary>Verdadeiro se o Windows está, agora, num fuso diferente do evento.</summary>
    public static bool WindowsEmOutroFuso(DateTimeOffset agora) =>
        TimeZoneInfo.Local.GetUtcOffset(agora) != Fuso.GetUtcOffset(agora);

    // No Windows o .NET traduz o nome IANA; o nome do Windows fica de reserva. Brasil não tem
    // horário de verão desde 2019: se nenhum dos dois existir, UTC-3 fixo está correto.
    private static TimeZoneInfo Localizar()
    {
        foreach (var id in new[] { Identificador, "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(Identificador, TimeSpan.FromHours(-3), "Horário de Brasília", "Horário de Brasília");
    }
}

/// <summary>
/// Início e fim de um período escolhido na tela: data opcional, hora opcional, no fuso do
/// evento.
/// </summary>
/// <remarks>
/// "Até 28/09" sem hora quer dizer o dia 28 inteiro. Antes, virava 28/09 00:00 e o dia
/// escolhido ficava de fora do filtro.
/// </remarks>
public static class Periodo
{
    private static readonly string[] FormatosDeHora = [@"h\:mm", @"hh\:mm", "hh", "h", "hhmm"];

    /// <summary>Lê "hh:mm" (também "h:mm", "hh" e "hhmm"). Vazio é válido e vira nulo.</summary>
    public static bool TentarLerHora(string? texto, out TimeSpan? hora)
    {
        hora = null;

        if (string.IsNullOrWhiteSpace(texto))
        {
            return true;
        }

        if (TimeSpan.TryParseExact(texto.Trim(), FormatosDeHora, CultureInfo.InvariantCulture, out var lida)
            && lida >= TimeSpan.Zero && lida < TimeSpan.FromDays(1))
        {
            hora = lida;
            return true;
        }

        return false;
    }

    /// <summary>Primeiro instante do período. Sem data e sem hora: sem limite.</summary>
    public static DateTimeOffset? Inicio(DateTime? dia, TimeSpan? hora, DateTimeOffset agora)
    {
        if (dia is null && hora is null)
        {
            return null;
        }

        var data = dia?.Date ?? FusoDoEvento.NoEvento(agora).Date;
        return FusoDoEvento.DoEvento(data + (hora ?? TimeSpan.Zero));
    }

    /// <summary>
    /// Último instante do período, inclusive: o fim do dia sem hora, ou o fim do minuto
    /// escolhido. Sem data e sem hora: sem limite.
    /// </summary>
    public static DateTimeOffset? Fim(DateTime? dia, TimeSpan? hora, DateTimeOffset agora)
    {
        if (dia is null && hora is null)
        {
            return null;
        }

        var data = dia?.Date ?? FusoDoEvento.NoEvento(agora).Date;
        var depois = hora is { } h ? FusoDoEvento.DoEvento(data + h).AddMinutes(1) : FusoDoEvento.DoEvento(data.AddDays(1));
        return depois.AddTicks(-1);
    }
}
