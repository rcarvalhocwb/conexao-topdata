using System.IO;
using System.Windows;
using System.Windows.Controls;
using Desktop.ViewModels;
using Microsoft.Win32;

namespace Desktop.App.Telas;

/// <summary>Tela "Pessoas" (docs/43). A lógica está na ViewModel; aqui só o desenho e a escolha do arquivo.</summary>
public partial class Pessoas : UserControl
{
    public Pessoas() => InitializeComponent();

    private async void EscolherPlanilha(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PessoasViewModel pessoas)
        {
            return;
        }

        var dialogo = new OpenFileDialog
        {
            Title = "Importar pessoas",
            Filter = "Planilha (*.csv;*.xlsx)|*.csv;*.xlsx",
        };

        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
        {
            byte[] conteudo;
            try
            {
                conteudo = await File.ReadAllBytesAsync(dialogo.FileName).ConfigureAwait(true);
            }
            catch (IOException erro)
            {
                MessageBox.Show(Window.GetWindow(this), $"Não foi possível ler o arquivo: {erro.Message}", "Importar pessoas");
                return;
            }

            await pessoas.PreverImportacaoAsync(Path.GetFileName(dialogo.FileName), conteudo).ConfigureAwait(true);
        }
    }
}
