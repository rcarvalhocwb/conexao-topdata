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
            // Hora do evento (Brasília), não a do Windows (docs/29).
            FileName = contas.NomeDoArquivoSugerido(),
        };

        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
        {
            await contas.ExportarAsync(dialogo.FileName).ConfigureAwait(true);
        }
    }

    // Imprimir ou salvar em PDF ("Microsoft Print to PDF"), com a página numerada e o código de conferência.
    private void Imprimir(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ContasViewModel { Relatorios: not null } contas)
        {
            return;
        }

        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
        {
            return;
        }

        var documento = DocumentoDosRelatorios.Montar(contas, dialogo.PrintableAreaWidth, dialogo.PrintableAreaHeight);
        var paginador = new PaginadorComRodape(
            ((System.Windows.Documents.IDocumentPaginatorSource)documento).DocumentPaginator,
            $"Código de conferência {contas.Conferencia} · Rayzer XAcess");
        dialogo.PrintDocument(paginador, "Prestação de contas — Rayzer XAcess");
    }
}
