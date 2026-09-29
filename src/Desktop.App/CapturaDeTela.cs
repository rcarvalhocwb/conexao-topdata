using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// Renderiza cada tela da janela real em PNG, sem precisar de alguém olhando o monitor.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque o projeto é construído em Linux, onde o WPF compila mas não roda. Roda na
/// máquina do evento, com o serviço local ligado: cada tela é preenchida pelo serviço de
/// verdade (as mesmas ViewModels da operação) e fotografada. Sem serviço, as telas saem com
/// a mensagem "sem resposta do serviço local" — o que também é uma imagem útil.
/// </para>
/// <para>
/// Limitação honesta: renderiza o <b>conteúdo</b> da janela, não a moldura do Windows.
/// </para>
/// <para>
/// Roda no CI do Windows (o runner tem sessão de desktop) com o serviço em modo simulação:
/// as imagens dos dois temas saem como artefato e no pré-lançamento, para revisão visual.
/// </para>
/// </remarks>
internal static class CapturaDeTela
{
    private const int Largura = 1366;
    private const int Altura = 768;

    /// <summary>Pontos por polegada da captura. 144 = uma vez e meia o padrão.</summary>
    private const double Resolucao = 144;

    /// <summary>Renderiza um PNG por tela na pasta indicada.</summary>
    /// <returns>Os arquivos gravados.</returns>
    internal static async Task<IReadOnlyList<string>> RenderizarAsync(string pasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        Directory.CreateDirectory(pasta);

        var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO") ?? TransporteLocal.EnderecoPadrao();
        var token = InstalacaoLocal.LerToken();
        var janela = new JanelaViewModel(new EdgeControl.EdgeControlClient(TransporteLocal.CriarCanal(endereco, token)));

        var gravados = new List<string>();
        var numero = 1;

        // A abertura (brand board, seção 08), parada no quadro final: marca e produto.
        var abertura = Path.Combine(pasta, $"00-abertura-{Rayzer.Design.TemaRayzer.Aplicado.ToLowerInvariant()}.png");
        Rayzer.Design.RayzerAbertura.SomenteQuadroFinal = true;
        try
        {
            await FotografarAsync(new Border { Child = new Rayzer.Design.RayzerAbertura(), Width = Largura, Height = Altura }, abertura).ConfigureAwait(true);
            gravados.Add(abertura);
        }
        finally
        {
            Rayzer.Design.RayzerAbertura.SomenteQuadroFinal = false;
        }

        var falhas = new List<string>();

        foreach (var tela in janela.Telas)
        {
            var nome = string.Create(
                CultureInfo.InvariantCulture,
                $"{numero:D2}-{Arquivo(tela.Titulo)}-{Rayzer.Design.TemaRayzer.Aplicado.ToLowerInvariant()}");
            numero++;

            // Uma tela que falha não leva as seguintes junto: o erro vai para um arquivo ao
            // lado das imagens e a captura segue. Antes, a primeira falha encerrava tudo, e
            // o erro ia para a saída de console, que num WinExe se perde.
            try
            {
                janela.TelaAtual = tela;

                // Com await, e nunca .GetResult(): as telas voltam para a thread da interface,
                // e bloqueá-la esperando por elas trava o processo para sempre.
                await janela.AtualizarAsync().ConfigureAwait(true);
                await tela.AtualizarAsync().ConfigureAwait(true);

                if (tela is GemeoDigitalViewModel gemeo)
                {
                    gravados.AddRange(await GravarGemeoAsync(pasta, nome, janela, gemeo).ConfigureAwait(true));
                }
                else
                {
                    var caminho = Path.Combine(pasta, nome + ".png");
                    await GravarAsync(caminho, janela).ConfigureAwait(true);
                    gravados.Add(caminho);
                }
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                var caminho = Path.Combine(pasta, nome + "-erro.txt");
                File.WriteAllText(caminho, erro.ToString());
                falhas.Add(caminho);
            }
        }

        // Relatório em arquivo porque num WinExe a saída de console não é confiável.
        File.WriteAllText(Path.Combine(pasta, "relatorio.txt"), Resumir(gravados, falhas));
        return gravados;
    }

    /// <summary>
    /// O gêmeo digital é fotografado na janela aberta de verdade, não pelo
    /// <see cref="RenderTargetBitmap"/>: renderizar o Viewport3D fora de uma janela derrubava
    /// o processo com estouro de pilha (0xC00000FD) no CI, sem exceção que desse para pegar.
    /// Na janela aberta o 3D é desenhado pelo caminho normal, e a foto é a que o operador vê,
    /// com o laço de animação rodando.
    /// </summary>
    private static async Task<IReadOnlyList<string>> GravarGemeoAsync(string pasta, string nome, JanelaViewModel vm, GemeoDigitalViewModel gemeo)
    {
        var gravados = new List<string>();
        var janela = new JanelaPrincipal(vm)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = Largura,
            Height = Altura,
            ShowInTaskbar = false,
            Topmost = true,
        };

        try
        {
            janela.Show();

            // 1. Como a tela abre: a catraca livre, a câmera na vista inicial.
            await EsperarAsync(janela, TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(janela, Path.Combine(pasta, nome + ".png")));

            // 2. No meio de um cenário, de frente: o QR lido, sinal verde, braço solto.
            await gemeo.MudarVista.ExecutarAsync("Frente").ConfigureAwait(true);
            await EsperarAsync(janela, TimeSpan.FromSeconds(1.5)).ConfigureAwait(true);
            await gemeo.RodarRoteiro.ExecutarAsync(Desktop.ViewModels.GemeoDigital.Roteiros.QrValido).ConfigureAwait(true);
            await EsperarAsync(janela, TimeSpan.FromSeconds(2.2)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(janela, Path.Combine(pasta, nome + "-cenario.png")));

            // 3. As peças separadas, com a ficha dos braços.
            await EsperarAsync(janela, TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            gemeo.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
            gemeo.PecasSeparadas = true;
            await gemeo.MudarVista.ExecutarAsync("Inicial").ConfigureAwait(true);
            await EsperarAsync(janela, TimeSpan.FromSeconds(2.5)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(janela, Path.Combine(pasta, nome + "-separadas.png")));
        }
        finally
        {
            gemeo.PecasSeparadas = false;
            janela.Close();
        }

        return gravados;
    }

    private static async Task EsperarAsync(Window janela, TimeSpan quanto)
    {
        await Task.Delay(quanto).ConfigureAwait(true);
        await janela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Copia a janela como o Windows a desenhou (PrintWindow com PW_RENDERFULLCONTENT, o mesmo
    /// recurso da foto do Setup no CI): funciona mesmo com parte da janela fora do monitor.
    /// </summary>
    private static string FotografarJanela(Window janela, string caminho)
    {
        var alca = new System.Windows.Interop.WindowInteropHelper(janela).Handle;
        if (!GetWindowRect(alca, out var r))
        {
            throw new InvalidOperationException("Não foi possível ler o tamanho da janela para a foto.");
        }

        using var foto = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (var g = System.Drawing.Graphics.FromImage(foto))
        {
            var dc = g.GetHdc();
            try
            {
                if (!PrintWindow(alca, dc, 2))
                {
                    throw new InvalidOperationException("O Windows recusou a foto da janela (PrintWindow).");
                }
            }
            finally
            {
                g.ReleaseHdc(dc);
            }
        }

        foto.Save(caminho, System.Drawing.Imaging.ImageFormat.Png);
        return caminho;
    }

    // Preenchida pelo Windows em GetWindowRect: o compilador não vê a atribuição.
#pragma warning disable CS0649
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Retangulo
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
#pragma warning restore CS0649

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr janela, out Retangulo retangulo);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr janela, IntPtr dc, uint opcoes);

    private static async Task GravarAsync(string caminho, JanelaViewModel vm)
    {
        var janela = new JanelaPrincipal(vm);

        // A janela nunca é exibida. O conteúdo é desligado dela e medido num contêiner
        // próprio: renderizar uma Window que nunca apareceu devolve imagem vazia.
        var raiz = (FrameworkElement)janela.Content;
        janela.Content = null;

        var moldura = new Border
        {
            Child = raiz,
            Width = Largura,
            Height = Altura,
            DataContext = vm,
        };

        // As mesmas chaves que a janela usa (Rayzer.Janela), para a captura sair no tema.
        moldura.SetResourceReference(Border.BackgroundProperty, "Rayzer.Background");
        moldura.SetResourceReference(TextElement.ForegroundProperty, "Rayzer.Text.Primary");
        moldura.SetResourceReference(TextElement.FontFamilyProperty, "Rayzer.Font.Text");
        moldura.SetResourceReference(TextElement.FontSizeProperty, "Rayzer.FontSize.Body");

        await FotografarAsync(moldura, caminho).ConfigureAwait(true);
        janela.Close();
    }

    private static async Task FotografarAsync(Border moldura, string caminho)
    {
        moldura.Measure(new Size(Largura, Altura));
        moldura.Arrange(new Rect(0, 0, Largura, Altura));
        moldura.UpdateLayout();

        // A DataGrid calcula as colunas de largura proporcional (Width="*") num passo
        // adiado do despachante. Sem deixá-lo rodar, a captura saía com essas colunas em
        // largura zero — o aplicativo aberto não tem esse problema, só a foto.
        await moldura.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        moldura.UpdateLayout();

        var escala = Resolucao / 96.0;
        var bitmap = new RenderTargetBitmap(
            (int)(Largura * escala),
            (int)(Altura * escala),
            Resolucao,
            Resolucao,
            PixelFormats.Pbgra32);

        bitmap.Render(moldura);

        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(bitmap));

        using var arquivo = File.Create(caminho);
        codificador.Save(arquivo);
    }

    private static string Arquivo(string titulo) =>
        new string([.. titulo.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ')])
        .Replace(' ', '-');

    internal static string Resumir(IReadOnlyList<string> arquivos, IReadOnlyList<string>? falhas = null)
    {
        var resumo = string.Create(
            CultureInfo.InvariantCulture,
            $"{arquivos.Count} imagem(ns) gravada(s):{Environment.NewLine}{string.Join(Environment.NewLine, arquivos)}");

        return falhas is { Count: > 0 }
            ? resumo + string.Create(
                CultureInfo.InvariantCulture,
                $"{Environment.NewLine}{falhas.Count} tela(s) com erro:{Environment.NewLine}{string.Join(Environment.NewLine, falhas)}")
            : resumo;
    }
}
