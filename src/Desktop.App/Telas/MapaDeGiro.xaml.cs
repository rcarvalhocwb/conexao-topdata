using System.Windows.Controls;

namespace Desktop.App.Telas;

/// <summary>
/// "Giro desta catraca" (mapa de giro, D9 do docs/34 §9): o painel do gêmeo e a aba Giro da
/// Parametrização. A lógica está na ViewModel; aqui só o desenho.
/// </summary>
public partial class MapaDeGiro : UserControl
{
    public MapaDeGiro() => InitializeComponent();
}
