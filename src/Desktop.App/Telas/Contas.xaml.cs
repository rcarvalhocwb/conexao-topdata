using System.Windows;
using System.Windows.Controls;
using Desktop.ViewModels;
using Microsoft.Win32;

namespace Desktop.App.Telas;

/// <summary>Tela "Prestação de contas". A lógica está na ViewModel; aqui só o desenho.</summary>
public partial class Contas : UserControl
{
    public Contas() => InitializeComponent();

    private async void Exportar(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ContasViewModel contas)
        {
            return;
        }

        var dialogo = new SaveFileDialog
        {
            Title = "Exportar prestação de contas",
            Filter = "Planilha CSV (*.csv)|*.csv",
            FileName = $"prestacao-de-contas-{DateTime.Now:yyyy-MM-dd-HHmm}.csv",
        };

        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
        {
            await contas.ExportarAsync(dialogo.FileName).ConfigureAwait(true);
        }
    }
}
