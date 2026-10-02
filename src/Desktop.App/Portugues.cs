using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace Desktop.App;

/// <summary>
/// O painel fala português do Brasil, qualquer que seja o idioma do Windows.
/// </summary>
/// <remarks>
/// O WPF não usa o idioma do Windows nas telas: <c>FrameworkElement.Language</c> nasce
/// "en-US". Era por isso que o calendário mostrava "Select a date" e os meses em inglês.
/// </remarks>
internal static class Portugues
{
    /// <summary>Texto do campo de data vazio.</summary>
    internal const string DicaDeData = "dd/mm/aaaa";

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Uma vez, na partida, antes de abrir qualquer janela.</summary>
    internal static void Aplicar()
    {
        CultureInfo.DefaultThreadCurrentCulture = Cultura;
        CultureInfo.DefaultThreadCurrentUICulture = Cultura;
        CultureInfo.CurrentCulture = Cultura;
        CultureInfo.CurrentUICulture = Cultura;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Cultura.IetfLanguageTag)));

        // A dica "Select a date" vem de um recurso do WPF que nem sempre tem tradução;
        // troca-se a dica de todo campo de data quando ele aparece.
        EventManager.RegisterClassHandler(typeof(DatePicker), FrameworkElement.LoadedEvent, new RoutedEventHandler(TrocarDica));
    }

    private static void TrocarDica(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker campo || campo.Template?.FindName("PART_TextBox", campo) is not DatePickerTextBox caixa)
        {
            return;
        }

        caixa.ApplyTemplate();

        if (caixa.Template?.FindName("PART_Watermark", caixa) is ContentControl dica)
        {
            dica.Content = DicaDeData;
        }
    }
}
