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
        : this(conectar: true)
    {
    }

    /// <summary>
    /// Monta a janela, opcionalmente sem conectar ao serviço local.
    /// </summary>
    /// <param name="conectar">
    /// Falso monta só a tela, sem cliente e sem temporizador. É o que a captura de tela usa:
    /// ela precisa dos controles reais, não de uma conexão.
    /// </param>
    internal JanelaPrincipal(bool conectar)
    {
        InitializeComponent();

        if (!conectar)
        {
            return;
        }

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

        Aplicar(_painel.Estado, _painel.Equipamentos);
    }

    /// <summary>
    /// Escreve um estado na tela.
    /// </summary>
    /// <remarks>
    /// Separado da atualização para que a captura de tela use <b>exatamente</b> o mesmo
    /// caminho que a operação. Uma captura montada por fora mostraria uma tela que não
    /// existe.
    /// </remarks>
    internal void Aplicar(EstadoDoPainel estado, IReadOnlyList<Equipamento> equipamentos)
    {
        ArgumentNullException.ThrowIfNull(estado);

        TextoDoEstado.Text = estado.Mensagem;
        TextoDoDetalhe.Text = estado.Detalhe ?? "—";

        TextoDeResumo.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"{estado.EquipamentosConectados} catraca(s) conectada(s) · " +
            $"{estado.WorkersAtivos} grupo(s) ativo(s) · " +
            $"{estado.OutboxPendente} informação(ões) aguardando envio");

        GradeDeEquipamentos.ItemsSource = equipamentos;
    }
}
