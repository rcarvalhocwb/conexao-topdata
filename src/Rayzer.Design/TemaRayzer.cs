using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Rayzer.Design;

/// <summary>Preferência de tema da pessoa.</summary>
public enum Tema
{
    /// <summary>Segue o tema de aplicativos do Windows (claro ou escuro).</summary>
    Sistema,

    /// <summary>Tema claro Rayzer.</summary>
    Claro,

    /// <summary>Tema escuro operacional Rayzer.</summary>
    Escuro,
}

/// <summary>
/// Liga o Rayzer Design System a um aplicativo WPF e troca o tema em tempo de execução.
/// </summary>
/// <remarks>
/// Toda tela usa <c>DynamicResource</c> para as chaves Rayzer.*: trocar o dicionário do
/// tema recolore a janela aberta, sem reiniciar. Com o alto contraste do Windows ligado,
/// o tema cede ao dicionário AltoContraste, que aponta para as cores do sistema.
/// </remarks>
public static class TemaRayzer
{
    private const string Pacote = "pack://application:,,,/Rayzer.Design;component/";

    private static ResourceDictionary? _dicionarioDoTema;
    private static Application? _aplicativo;
    private static string? _pastaDePreferencias;

    /// <summary>Preferência atual.</summary>
    public static Tema Preferencia { get; private set; } = Tema.Sistema;

    /// <summary>O tema de fato aplicado: "Claro", "Escuro" ou "AltoContraste".</summary>
    public static string Aplicado { get; private set; } = "Claro";

    /// <summary>Verdadeiro quando o tema aplicado é o escuro.</summary>
    public static bool EscuroAplicado => Aplicado == "Escuro";

    /// <summary>Avisa quando o tema aplicado muda.</summary>
    public static event EventHandler? Mudou;

    /// <summary>
    /// Instala tokens, tema e estilos no aplicativo. Chamar uma vez, na partida, antes de
    /// abrir janelas.
    /// </summary>
    /// <param name="aplicativo">O aplicativo.</param>
    /// <param name="produto">Nome curto do produto Rayzer, usado na pasta de preferências.</param>
    public static void Instalar(Application aplicativo, string produto)
    {
        ArgumentNullException.ThrowIfNull(aplicativo);
        ArgumentException.ThrowIfNullOrWhiteSpace(produto);

        _aplicativo = aplicativo;
        _pastaDePreferencias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rayzer", produto);
        Preferencia = LerPreferencia();

        // O App.xaml pode já declarar os dicionários (é o recomendado: assim StaticResource
        // funciona desde o carregamento). Se não declarar, são inseridos aqui.
        var dicionarios = aplicativo.Resources.MergedDictionaries;
        _dicionarioDoTema = dicionarios.FirstOrDefault(d => d.Source?.OriginalString.Contains("/Temas/", StringComparison.Ordinal) == true);

        if (_dicionarioDoTema is null)
        {
            dicionarios.Insert(0, Carregar("Tokens.xaml"));
            _dicionarioDoTema = Carregar("Temas/Claro.xaml");
            dicionarios.Insert(1, _dicionarioDoTema);
            dicionarios.Insert(2, Carregar("Controles.xaml"));
        }

        Aplicado = Path.GetFileNameWithoutExtension(_dicionarioDoTema.Source!.OriginalString);
        Reaplicar();

        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
            {
                Reaplicar();
            }
        };

        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Preferencia == Tema.Sistema)
            {
                aplicativo.Dispatcher.BeginInvoke(Reaplicar);
            }
        };
    }

    /// <summary>Muda a preferência, aplica na hora e grava para a próxima vez.</summary>
    public static void Definir(Tema tema) => Aplicar(tema, gravar: true);

    /// <summary>Aplica um tema; sem gravar, vale só até fechar (captura de tela, teste).</summary>
    public static void Aplicar(Tema tema, bool gravar)
    {
        Preferencia = tema;
        if (gravar)
        {
            GravarPreferencia(tema);
        }

        Reaplicar();
    }

    /// <summary>Alterna entre claro e escuro a partir do tema aplicado agora.</summary>
    public static void Alternar() => Definir(EscuroAplicado ? Tema.Claro : Tema.Escuro);

    private static void Reaplicar()
    {
        if (_aplicativo is null || _dicionarioDoTema is null)
        {
            return;
        }

        var nome = Resolver(Preferencia);
        if (nome == Aplicado)
        {
            return;
        }

        var dicionarios = _aplicativo.Resources.MergedDictionaries;
        var indice = dicionarios.IndexOf(_dicionarioDoTema);
        _dicionarioDoTema = Carregar($"Temas/{nome}.xaml");
        dicionarios[indice < 0 ? 1 : indice] = _dicionarioDoTema;
        Aplicado = nome;
        Mudou?.Invoke(null, EventArgs.Empty);
    }

    private static string Resolver(Tema tema)
    {
        if (SystemParameters.HighContrast)
        {
            return "AltoContraste";
        }

        return tema switch
        {
            Tema.Claro => "Claro",
            Tema.Escuro => "Escuro",
            _ => WindowsEmTemaEscuro() ? "Escuro" : "Claro",
        };
    }

    private static bool WindowsEmTemaEscuro()
    {
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return chave?.GetValue("AppsUseLightTheme") is int valor && valor == 0;
        }
        catch (Exception erro) when (erro is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static ResourceDictionary Carregar(string caminho) => new() { Source = new Uri(Pacote + caminho, UriKind.Absolute) };

    private static Tema LerPreferencia()
    {
        try
        {
            var arquivo = Path.Combine(_pastaDePreferencias!, "tema.txt");
            return File.Exists(arquivo) && Enum.TryParse<Tema>(File.ReadAllText(arquivo).Trim(), ignoreCase: true, out var tema)
                ? tema
                : Tema.Sistema;
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            return Tema.Sistema;
        }
    }

    private static void GravarPreferencia(Tema tema)
    {
        try
        {
            Directory.CreateDirectory(_pastaDePreferencias!);
            File.WriteAllText(Path.Combine(_pastaDePreferencias!, "tema.txt"), tema.ToString());
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            // Preferência é conveniência: sem gravar, vale só até fechar.
        }
    }
}
