using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

    /// <summary>
    /// O assistente mora ao lado do painel (..\Configurador) e pede administrador: abre pelo
    /// shell com "runas", que mostra o aviso do Windows em vez de falhar calado.
    /// </summary>
    private void AbrirAssistente(object sender, RoutedEventArgs e)
    {
        var assistente = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Configurador", "Edge.Configurador.exe"));

        if (!File.Exists(assistente))
        {
            MessageBox.Show(this, $"O assistente não foi encontrado em:{Environment.NewLine}{assistente}", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(assistente) { UseShellExecute = true, Verb = "runas" })?.Dispose();
        }
        catch (Win32Exception)
        {
            // O operador recusou a permissão de administrador: nada a fazer.
        }
    }

    private static JanelaViewModel ConectarAoServico()
    {
        var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO") ?? TransporteLocal.EnderecoPadrao();
        var token = InstalacaoLocal.LerToken();
        var canal = TransporteLocal.CriarCanal(endereco, token);
        return new JanelaViewModel(new EdgeControl.EdgeControlClient(canal));
    }
}
