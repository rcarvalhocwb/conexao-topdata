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

    /// <summary>
    /// Fechar (o X, Alt+F4) só esconde a janela: o painel fica na bandeja, perto do relógio.
    /// Desligado na captura e no autoteste, que precisam fechar de verdade.
    /// </summary>
    internal bool FecharVaiParaBandeja { get; set; }

    /// <summary>Depois de cada atualização periódica, mesmo com a janela escondida.</summary>
    internal event EventHandler? Atualizada;

    /// <summary>A janela foi para a bandeja.</summary>
    internal event EventHandler? FoiParaBandeja;

    private bool _saindo;

    /// <summary>
    /// Liga o relógio e o fluxo ao vivo, com a janela aberta ou não: na bandeja, o ícone
    /// também precisa saber das catracas. A captura de tela não chama isto.
    /// </summary>
    internal void Iniciar()
    {
        // Vive enquanto a janela vive; é descartado quando ela fecha de verdade.
        var fechando = new CancellationTokenSource();

        async Task AtualizarAsync()
        {
            await Janela.AtualizarAsync().ConfigureAwait(true);
            Atualizada?.Invoke(this, EventArgs.Empty);
        }

        _relogio.Tick += async (_, _) => await AtualizarAsync().ConfigureAwait(true);
        Loaded += (_, _) => Menu.Focus();

        Dispatcher.BeginInvoke(async () =>
        {
            await AtualizarAsync().ConfigureAwait(true);
            _relogio.Start();

            // Os acessos ao vivo chegam numa thread do gRPC; a lista é da tela.
            _ = Janela.Painel.AcompanharAsync(
                acao => Dispatcher.BeginInvoke(acao),
                fechando.Token);
        });

        Closed += (_, _) =>
        {
            _relogio.Stop();
            fechando.Cancel();
            fechando.Dispose();
        };
    }

    /// <summary>Traz a janela de volta (da bandeja, minimizada ou atrás de outra).</summary>
    internal void Mostrar()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Fecha de verdade (sair do painel, desligar o Windows).</summary>
    internal void SairDeVez()
    {
        _saindo = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (FecharVaiParaBandeja && !_saindo)
        {
            // Fechar o painel nunca para as catracas: quem as atende é o serviço.
            e.Cancel = true;
            Hide();
            FoiParaBandeja?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
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
