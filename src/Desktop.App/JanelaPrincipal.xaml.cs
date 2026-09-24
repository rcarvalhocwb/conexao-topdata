using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// Janela do painel. A lógica mora na ViewModel; aqui só há ligação de dados.
/// </summary>
public partial class JanelaPrincipal : Window
{
    private readonly PainelViewModel? _painel;
    private readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(2) };

    public JanelaPrincipal()
    {
        InitializeComponent();

        var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO") ?? TransporteLocal.EnderecoPadrao();
        var token = Environment.GetEnvironmentVariable("EDGE_TOKEN");

        var canal = TransporteLocal.CriarCanal(endereco, token);
        _painel = new PainelViewModel(new EdgeControl.EdgeControlClient(canal));

        _relogio.Tick += async (_, _) => await AtualizarAsync().ConfigureAwait(true);
        Loaded += async (_, _) =>
        {
            await AtualizarAsync().ConfigureAwait(true);
            _relogio.Start();
        };

        Closed += (_, _) => _relogio.Stop();
    }

    private async Task AtualizarAsync()
    {
        if (_painel is null)
        {
            return;
        }

        // A ViewModel nunca lança: falha vira estado. Por isso não há try/catch aqui —
        // um catch mudo esconderia um defeito de verdade.
        await _painel.AtualizarAsync().ConfigureAwait(true);

        TextoDoEstado.Text = _painel.Estado.Mensagem;
        TextoDoDetalhe.Text = _painel.Estado.Detalhe ?? "—";

        TextoDeResumo.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{_painel.Estado.EquipamentosConectados} catraca(s) conectada(s) · " +
            $"{_painel.Estado.WorkersAtivos} grupo(s) ativo(s) · " +
            $"{_painel.Estado.OutboxPendente} informação(ões) aguardando envio");

        GradeDeEquipamentos.ItemsSource = _painel.Equipamentos;
    }
}
