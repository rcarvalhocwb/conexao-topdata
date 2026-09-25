using System.Windows.Controls;

namespace Desktop.App.Telas;

/// <summary>Tela "Consultar código". A lógica está na ViewModel; aqui só o desenho.</summary>
public partial class Consulta : UserControl
{
    public Consulta()
    {
        InitializeComponent();
        Loaded += AoCarregar;
    }

    // O leitor do balcão "digita" o código: o campo precisa estar com o foco assim
    // que a tela abre.
    private void AoCarregar(object sender, System.Windows.RoutedEventArgs e) => CampoDoCodigo.Focus();
}
