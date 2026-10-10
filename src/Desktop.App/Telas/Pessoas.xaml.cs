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

    private async void ExportarDados(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PessoasViewModel { PodeExportarDados: true } pessoas) return;
        var dialogo = new SaveFileDialog
        {
            Title = "Exportar dados do titular",
            Filter = "Dados do titular (*.json)|*.json",
            FileName = "dados-do-titular.json",
            AddExtension = true,
            DefaultExt = ".json",
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) == true)
            await pessoas.ExportarDadosAsync(dialogo.FileName).ConfigureAwait(true);
    }

    private async void ExcluirTitular(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PessoasViewModel { PodeExcluirTitular: true } pessoas) return;
        var id = pessoas.Id;
        var confirmada = MessageBox.Show(Window.GetWindow(this),
            "Excluir definitivamente a ficha deste titular e todas as suas credenciais?\n\nAs passagens ficam sem identificação pessoal. Esta ação não pode ser desfeita.",
            "Excluir titular", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (confirmada && pessoas.Id == id) await pessoas.ExcluirTitularAsync(true).ConfigureAwait(true);
    }

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
