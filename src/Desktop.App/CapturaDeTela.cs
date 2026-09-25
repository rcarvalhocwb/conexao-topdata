using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
/// <b>Não funciona na CI.</b> O runner do Windows do GitHub não tem sessão gráfica e o
/// WPF não inicializa. Precisa de uma máquina Windows de verdade.
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

        foreach (var tela in janela.Telas)
        {
            janela.TelaAtual = tela;

            // Com await, e nunca .GetResult(): as telas voltam para a thread da interface,
            // e bloqueá-la esperando por elas trava o processo para sempre.
            await janela.AtualizarAsync().ConfigureAwait(true);
            await tela.AtualizarAsync().ConfigureAwait(true);

            var caminho = Path.Combine(
                pasta,
                string.Create(CultureInfo.InvariantCulture, $"{numero:D2}-{Arquivo(tela.Titulo)}.png"));
            Gravar(caminho, janela);
            gravados.Add(caminho);
            numero++;
        }

        // Relatório em arquivo porque num WinExe a saída de console não é confiável.
        File.WriteAllText(Path.Combine(pasta, "relatorio.txt"), Resumir(gravados));
        return gravados;
    }

    private static void Gravar(string caminho, JanelaViewModel vm)
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
            Background = SystemColors.WindowBrush,
        };

        moldura.Measure(new Size(Largura, Altura));
        moldura.Arrange(new Rect(0, 0, Largura, Altura));
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

        janela.Close();
    }

    private static string Arquivo(string titulo) =>
        new string([.. titulo.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ')])
        .Replace(' ', '-');

    internal static string Resumir(IReadOnlyList<string> arquivos) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{arquivos.Count} imagem(ns) gravada(s):{Environment.NewLine}" +
            $"{string.Join(Environment.NewLine, arquivos)}");
}
