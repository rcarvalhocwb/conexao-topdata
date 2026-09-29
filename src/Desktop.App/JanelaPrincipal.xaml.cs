using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Rayzer.Design;

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

        TextoVersao.Text = "Versão " + (typeof(JanelaPrincipal).Assembly.GetName().Version?.ToString(3) ?? "—");
        MostrarTema();
        TemaRayzer.Mudou += (_, _) => MostrarTema();

        // Janela estreita (notebook, tela dividida): o menu recolhe sozinho para só ícones.
        SizeChanged += (_, e) =>
        {
            var compacta = e.NewSize.Width < (double)FindResource("Rayzer.Breakpoint.Compact");
            if (e.PreviousSize.Width == 0 || compacta != (e.PreviousSize.Width < (double)FindResource("Rayzer.Breakpoint.Compact")))
            {
                Recolher(compacta);
            }
        };
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

    private void AlternarTema(object sender, RoutedEventArgs e) => TemaRayzer.Alternar();

    private void AlternarBarra(object sender, RoutedEventArgs e) => Recolher(BarraLateral.Width > 100);

    private void Recolher(bool recolher)
    {
        BarraLateral.Width = recolher ? 72 : 248;
        Menu.Tag = recolher ? string.Empty : "aberto";
        Logo.Variante = recolher ? VarianteDoLogo.Simbolo : VarianteDoLogo.Horizontal;
        Cabecalho.Margin = recolher ? new Thickness(14, 14, 14, 8) : new Thickness(18, 14, 12, 8);
        Produto.Padding = recolher ? new Thickness(5) : new Thickness(10, 6, 10, 6);
        ProdutoTexto.Visibility = recolher ? Visibility.Collapsed : Visibility.Visible;
        var textos = recolher ? Visibility.Collapsed : Visibility.Visible;
        TextoTema.Visibility = textos;
        TextoRecolher.Visibility = textos;
        Autoria.Visibility = textos;
        SeloSimulacao.Margin = recolher ? new Thickness(4, 0, 4, 8) : new Thickness(8, 0, 8, 8);
        TextoRecolher.Text = recolher ? "Expandir menu" : "Recolher menu";
    }

    private void MostrarTema()
    {
        var escuro = TemaRayzer.EscuroAplicado;
        TextoTema.Text = escuro ? "Tema claro" : "Tema escuro";
        IconeTema.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, escuro ? "Rayzer.Icon.Sun" : "Rayzer.Icon.Moon");
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
