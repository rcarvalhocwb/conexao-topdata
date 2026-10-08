using System.Windows;
using System.Windows.Controls;
using Desktop.ViewModels;
using Microsoft.Win32;

namespace Desktop.App.Telas;

/// <summary>Tela "Diagnóstico". A lógica está na ViewModel; aqui só a caixa de salvar.</summary>
public partial class Diagnostico : UserControl
{
    public Diagnostico() => InitializeComponent();

    /// <summary>
    /// Pede o pacote à ViewModel e pergunta onde salvar. Falha ao gravar vira mensagem na tela, nunca
    /// exceção: o pacote é para o suporte, e a tela não pode fechar por causa dele.
    /// </summary>
    private async void SalvarPacote(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DiagnosticoViewModel tela)
        {
            return;
        }

        await tela.PedirPacote.ExecutarAsync().ConfigureAwait(true);
        if (tela.Pacote is null)
        {
            return;
        }

        var caixa = new SaveFileDialog
        {
            Title = "Salvar o pacote de diagnóstico",
            Filter = "Arquivo zip (*.zip)|*.zip",
            FileName = tela.NomeDoPacote,
        };

        if (caixa.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        tela.SalvarEm(caixa.FileName);
    }
}
