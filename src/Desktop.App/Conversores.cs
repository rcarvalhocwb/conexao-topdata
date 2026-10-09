using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Desktop.ViewModels;
using Google.Protobuf.WellKnownTypes;
using Rayzer.Design;

namespace Desktop.App;

/// <summary>
/// Sinal da ViewModel vira o tom Rayzer. A cor sai do tema; o símbolo e o texto vão juntos
/// (RayzerStatus), porque a situação nunca depende só da cor.
/// </summary>
public sealed class SinalParaTom : IValueConverter
{
    public static Tom De(Sinal sinal) => sinal switch
    {
        Sinal.Bom => Tom.Sucesso,
        Sinal.Atencao => Tom.Atencao,
        Sinal.Problema => Tom.Perigo,
        _ => Tom.Neutro,
    };

    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value is Sinal sinal ? De(sinal) : Tom.Neutro;

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Símbolo de dispositivo: ● online, ! atenção, ○ offline ou parado. (O de acesso é o
/// padrão do tom: ✓ autorizado, × negado.)
/// </summary>
public sealed class SinalParaGlifoDeDispositivo : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            Sinal.Bom => "●",
            Sinal.Atencao => "!",
            _ => "○",
        };

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Saúde do cabeçalho vira o tom Rayzer.</summary>
public sealed class SaudeParaTom : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            SaudeDoPainel.Normal => Tom.Sucesso,
            SaudeDoPainel.Atencao => Tom.Atencao,
            SaudeDoPainel.Acao => Tom.Perigo,
            _ => Tom.Info,
        };

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Fila de envio: vazia é sincronizado (✓); com itens, sincronizando (↻).</summary>
public sealed class PendenciaParaTom : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value is long n && n > 0 ? Tom.Info : Tom.Sucesso;

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Lista vazia (ou quantidade zero) mostra o estado vazio; o parâmetro "inverso" faz o
/// contrário.
/// </summary>
/// <remarks>
/// Recebe a própria lista, e não "Lista.Count": as listas somente leitura das ViewModels
/// implementam Count de forma explícita, e a ligação do WPF não o enxerga — o estado vazio
/// apareceria por cima dos dados. ObservableCollection, que avisa ao mudar, liga em
/// "Count" mesmo.
/// </remarks>
public sealed class ZeroParaVisivel : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture)
    {
        var zero = value switch
        {
            int n => n == 0,
            long n => n == 0,
            System.Collections.ICollection colecao => colecao.Count == 0,
            System.Collections.IEnumerable itens => !itens.GetEnumerator().MoveNext(),
            _ => true,
        };
        var inverso = parameter as string == "inverso";
        return zero != inverso ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Ícone de cada tela no menu lateral.</summary>
public sealed class TelaParaIcone : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            PainelAoVivoViewModel => "\uE80F",
            CatracasViewModel => "\uE772",
            AcessosViewModel => "\uE8FD",
            ConsultaViewModel => "\uE721",
            SincronizacaoViewModel => "\uE895",
            ContasViewModel => "\uE8A5",
            GerenciarCatracaViewModel => "\uE90F",
            GemeoDigitalViewModel => "\uE81E",
            ConfiguracoesViewModel => "\uE713",
            DiagnosticoViewModel => "\uE9D9",
            SimuladorViewModel => "\uE768",
            _ => "\uE8FD",
        };

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Instante do contrato no relógio do evento (Brasília); o parâmetro é o formato.</summary>
public sealed class InstanteParaTexto : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        value is Timestamp t
            ? Desktop.ViewModels.FusoDoEvento.NoEvento(t.ToDateTimeOffset()).ToString(parameter as string ?? "dd/MM HH:mm", CultureInfo.CurrentCulture)
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
        value switch
        {
            string texto => texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
            null => Visibility.Collapsed,

            // Objeto presente (a catraca escolhida, por exemplo): aparece.
            _ => Visibility.Visible,
        };

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
