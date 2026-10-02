using Desktop.ViewModels;

namespace Integration.Tests;

/// <summary>Período das telas: data e hora opcionais, no horário de Brasília.</summary>
public sealed class PeriodoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 12, 5, 23, 30, 0, TimeSpan.FromHours(-3));

    [Theory]
    [InlineData("", true, null)]
    [InlineData("  ", true, null)]
    [InlineData("18:30", true, "18:30")]
    [InlineData("8:05", true, "08:05")]
    [InlineData("07", true, "07:00")]
    [InlineData("1845", true, "18:45")]
    [InlineData("24:00", false, null)]
    [InlineData("18:60", false, null)]
    [InlineData("dezoito", false, null)]
    public void Le_a_hora_da_tela(string texto, bool valida, string? esperada)
    {
        Assert.Equal(valida, Periodo.TentarLerHora(texto, out var hora));
        Assert.Equal(esperada, hora?.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Fim_sem_hora_e_o_ultimo_instante_do_dia_em_Brasilia()
    {
        var fim = Periodo.Fim(new DateTime(2026, 12, 5), null, Agora)!.Value;

        Assert.Equal(new DateTimeOffset(2026, 12, 6, 3, 0, 0, TimeSpan.Zero).AddTicks(-1), fim.ToUniversalTime());
    }

    [Fact]
    public void Fim_com_hora_inclui_o_minuto_inteiro()
    {
        var fim = Periodo.Fim(new DateTime(2026, 12, 5), TimeSpan.FromHours(18), Agora)!.Value;

        Assert.Equal(new DateTimeOffset(2026, 12, 5, 21, 1, 0, TimeSpan.Zero).AddTicks(-1), fim.ToUniversalTime());
    }

    [Fact]
    public void Inicio_sem_hora_e_meia_noite_em_Brasilia_e_hora_sem_data_e_hoje()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 12, 5, 3, 0, 0, TimeSpan.Zero),
            Periodo.Inicio(new DateTime(2026, 12, 5), null, Agora)!.Value.ToUniversalTime());

        Assert.Equal(
            new DateTimeOffset(2026, 12, 5, 21, 0, 0, TimeSpan.Zero),
            Periodo.Inicio(null, TimeSpan.FromHours(18), Agora)!.Value.ToUniversalTime());

        Assert.Null(Periodo.Inicio(null, null, Agora));
        Assert.Null(Periodo.Fim(null, null, Agora));
    }

    [Fact]
    public void O_fuso_do_evento_e_Brasilia_sem_horario_de_verao()
    {
        // Brasil não tem horário de verão desde 2019: UTC-3 no ano inteiro.
        Assert.Equal(TimeSpan.FromHours(-3), FusoDoEvento.Fuso.GetUtcOffset(new DateTime(2026, 1, 15)));
        Assert.Equal(TimeSpan.FromHours(-3), FusoDoEvento.Fuso.GetUtcOffset(new DateTime(2026, 7, 15)));

        // 02:30 UTC é 23:30 do dia anterior em Brasília: o dia do evento não é o dia UTC.
        var noEvento = FusoDoEvento.NoEvento(new DateTimeOffset(2026, 12, 6, 2, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateTime(2026, 12, 5, 23, 30, 0), noEvento.DateTime);
    }
}
