using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Desktop.ViewModels;
using Google.Protobuf.WellKnownTypes;

namespace Desktop.App;

/// <summary>
/// Cor do sinal de situação. A cor nunca vem sozinha: toda tela põe o texto ao lado
/// (docs/10-interface.md, acessibilidade).
/// </summary>
public sealed class SinalParaPincel : IValueConverter
{
    // Verde, âmbar e vermelho escolhidos para continuar distinguíveis em daltonismo
    // vermelho-verde pela luminosidade, além do texto.
    private static readonly SolidColorBrush Bom = Congelado(0x1B, 0x7F, 0x3B);
    private static readonly SolidColorBrush Atencao = Congelado(0xB2, 0x6A, 0x00);
    private static readonly SolidColorBrush Problema = Congelado(0xC0, 0x1F, 0x1F);
    private static readonly SolidColorBrush Neutro = Congelado(0x6B, 0x72, 0x80);

    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            Sinal.Bom => Bom,
            Sinal.Atencao => Atencao,
            Sinal.Problema => Problema,
            _ => Neutro,
        };

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Congelado(byte r, byte g, byte b)
    {
        var pincel = new SolidColorBrush(Color.FromRgb(r, g, b));
        pincel.Freeze();
        return pincel;
    }
}

/// <summary>Saúde do cabeçalho vira sinal.</summary>
public sealed class SaudeParaPincel : IValueConverter
{
    private static readonly SinalParaPincel Sinais = new();

    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        Sinais.Convert(
            value switch
            {
                SaudeDoPainel.Normal => Sinal.Bom,
                SaudeDoPainel.Atencao => Sinal.Atencao,
                SaudeDoPainel.Acao => Sinal.Problema,
                _ => Sinal.Neutro,
            },
            targetType,
            parameter,
            culture);

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Instante do contrato em hora local; o parâmetro é o formato.</summary>
public sealed class InstanteParaTexto : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value is Timestamp t
            ? t.ToDateTimeOffset().ToLocalTime().ToString(parameter as string ?? "dd/MM HH:mm", CultureInfo.CurrentCulture)
            : "—";

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Verdadeiro vira "Sim"; falso, "—".</summary>
public sealed class SimOuTraco : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Sim" : "—";

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Texto vazio some da tela.</summary>
public sealed class VazioSome : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
