using System.Windows;
using System.Windows.Threading;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// A janela do operador. Só liga a <see cref="JanelaViewModel"/> ao relógio e ao fluxo ao
/// vivo; tudo o que ela mostra vem das ViewModels, testadas sem WPF.
/// </summary>
public partial class JanelaPrincipal : Window
{
    private readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromSeconds(2) };

    public JanelaPrincipal()
        : this(ConectarAoServico())
    {
    }

    internal JanelaPrincipal(JanelaViewModel janela)
    {
        ArgumentNullException.ThrowIfNull(janela);
        InitializeComponent();
        DataContext = janela;
        Janela = janela;
    }

    internal JanelaViewModel Janela { get; }

    /// <summary>Liga o relógio e o fluxo ao vivo. A captura de tela não chama isto.</summary>
    internal void Iniciar()
    {
        // Vive enquanto a janela vive; é descartado quando ela fecha.
        var fechando = new CancellationTokenSource();

        _relogio.Tick += async (_, _) => await Janela.AtualizarAsync().ConfigureAwait(true);

        Loaded += async (_, _) =>
        {
            Menu.Focus();
            await Janela.AtualizarAsync().ConfigureAwait(true);
            _relogio.Start();

            // Os acessos ao vivo chegam numa thread do gRPC; a lista é da tela.
            _ = Janela.Painel.AcompanharAsync(
                acao => Dispatcher.BeginInvoke(acao),
                fechando.Token);
        };

        Closed += (_, _) =>
        {
            _relogio.Stop();
            fechando.Cancel();
            fechando.Dispose();
        };
    }

    private static JanelaViewModel ConectarAoServico()
    {
        var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO") ?? TransporteLocal.EnderecoPadrao();
        var token = Environment.GetEnvironmentVariable("EDGE_TOKEN");
        var canal = TransporteLocal.CriarCanal(endereco, token);
        return new JanelaViewModel(new EdgeControl.EdgeControlClient(canal));
    }
}
