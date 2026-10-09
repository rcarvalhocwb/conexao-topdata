using System.IO;
using System.Windows;
using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// O aviso "Novidades desta versão". Só desenha; a decisão de abrir e a marca de "já vi" moram
/// na <see cref="NovidadesViewModel"/>.
/// </summary>
public partial class JanelaDeNovidades : Window
{
    private readonly NovidadesViewModel _novidades;

    internal JanelaDeNovidades(NovidadesViewModel novidades)
    {
        ArgumentNullException.ThrowIfNull(novidades);
        InitializeComponent();
        _novidades = novidades;
        DataContext = novidades;

        // Fechar pelo X também conta como visto, senão o aviso voltaria na próxima abertura.
        Closed += (_, _) => _novidades.ConfirmarVisto();
    }

    /// <summary>
    /// Mostra o aviso sobre a janela dona, mas só quando a <see cref="NovidadesViewModel"/> decidiu
    /// que há novidade a contar. Quando não há, marca a edição como vista e não abre nada.
    /// </summary>
    internal static void MostrarSePreciso(Window dona)
    {
        ArgumentNullException.ThrowIfNull(dona);

        var versao = typeof(JanelaDeNovidades).Assembly.GetName().Version?.ToString(3) ?? "—";
        var novidades = new NovidadesViewModel(
            versao,
            Contracts.RegistroDeNovidades.EdicaoVista(),
            haInstalacaoAnterior: File.Exists(Contracts.InstalacaoLocal.ArquivoDeConfiguracao),
            marcarVista: edicao => Contracts.RegistroDeNovidades.Gravar(edicao));

        if (!novidades.DeveMostrar)
        {
            return;
        }

        new JanelaDeNovidades(novidades) { Owner = dona }.ShowDialog();
    }

    private void Entendi(object sender, RoutedEventArgs e) => Close();
}
