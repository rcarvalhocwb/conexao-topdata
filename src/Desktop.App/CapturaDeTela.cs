using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// Renderiza a janela real em PNG, sem precisar de alguém olhando o monitor.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque o projeto é construído em Linux, onde o WPF compila mas não roda. Sem isto,
/// a única imagem da tela seria uma recriação feita à mão — que mostra o que <i>deveria</i>
/// aparecer, não o que aparece. A captura usa a árvore visual do próprio XAML e o mesmo
/// método <see cref="JanelaPrincipal.Aplicar"/> que a operação usa.
/// </para>
/// <para>
/// Limitação honesta: renderiza o <b>conteúdo</b> da janela, não a moldura do Windows. Barra
/// de título, botões de fechar e a sombra do sistema não aparecem, porque são desenhados pelo
/// sistema operacional e não pela aplicação.
/// </para>
/// </remarks>
internal static class CapturaDeTela
{
    private const int Largura = 1000;
    private const int Altura = 600;

    /// <summary>Pontos por polegada da captura. 192 = duas vezes o padrão, para leitura.</summary>
    private const double Resolucao = 192;

    /// <summary>Renderiza um PNG por cenário na pasta indicada.</summary>
    /// <returns>Os arquivos gravados.</returns>
    internal static IReadOnlyList<string> Renderizar(string pasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        Directory.CreateDirectory(pasta);

        var gravados = new List<string>();

        foreach (var (nome, estado, equipamentos) in Cenarios())
        {
            var caminho = Path.Combine(pasta, $"painel-{nome}.png");
            Gravar(caminho, estado, equipamentos);
            gravados.Add(caminho);
        }

        return gravados;
    }

    private static void Gravar(string caminho, EstadoDoPainel estado, IReadOnlyList<Equipamento> equipamentos)
    {
        var janela = new JanelaPrincipal(conectar: false);
        janela.Aplicar(estado, equipamentos);

        // A janela nunca é exibida. O conteúdo é desligado dela e medido num contêiner
        // próprio: renderizar uma Window que nunca apareceu devolve imagem vazia.
        var raiz = (FrameworkElement)janela.Content;
        janela.Content = null;

        var moldura = new Border
        {
            Child = raiz,
            Width = Largura,
            Height = Altura,

            // Fundo explícito: sem ele o PNG sai com transparência onde deveria haver a
            // cor da janela, e a imagem engana quem olhar.
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

    private static IEnumerable<(string Nome, EstadoDoPainel Estado, IReadOnlyList<Equipamento> Equipamentos)> Cenarios()
    {
        var frota = Frota();

        yield return ("sem-internet", De(NivelDeDegradacao.T1SemInternet, 6, 3, 1842), frota);
        yield return ("normal", De(NivelDeDegradacao.T0Normal, 6, 3, 0), frota);
        yield return ("lista-local", De(NivelDeDegradacao.T2ListaLocal, 6, 3, 1842), frota);
        yield return ("sem-catraca", De(NivelDeDegradacao.T3Isolado, 0, 3, 1842), []);

        // Estado de falha: nasce de um estado bom e recebe a queda, como na operação.
        yield return (
            "servico-caiu",
            De(NivelDeDegradacao.T1SemInternet, 6, 3, 1842)
                .ComFalhaDeComunicacao("Unavailable: failed to connect to all addresses", Agora.AddSeconds(22)),
            frota);
    }

    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 19, 40, 0, TimeSpan.Zero);

    private static EstadoDoPainel De(NivelDeDegradacao nivel, int conectados, int workers, long outbox) =>
        EstadoDoPainel.De(
            new ObterEstadoResponse
            {
                Versao = "1.0.0-fase1",
                Nivel = nivel,
                EquipamentosConectados = conectados,
                WorkersAtivos = workers,
                OutboxPendente = outbox,
                InternetDisponivel = nivel == NivelDeDegradacao.T0Normal,
            },
            Agora);

    private static List<Equipamento> Frota() =>
    [
        Equipamento(1, "Entrada Principal — A", "setor-a", 3570, "Polling"),
        Equipamento(2, "Entrada Principal — B", "setor-a", 3570, "MonitoraGiroCatraca"),
        Equipamento(3, "Entrada Principal — C", "setor-a", 3570, "Polling"),
        Equipamento(7, "Portão Norte — Coletor de cartão", "setor-b", 3571, "ColetarBilhetes"),
        Equipamento(8, "Portão Norte — Catraca 1", "setor-b", 3571, "OfflineAutonomo"),
        Equipamento(12, "Credenciamento", "setor-c", 3572, "Reconectar"),
    ];

    private static Equipamento Equipamento(int inner, string gate, string worker, int porta, string estado) =>
        new()
        {
            Inner = inner,
            NomeDoGate = gate,
            Worker = worker,
            Porta = porta,
            Estado = estado,
            Saudavel = estado is "Polling" or "MonitoraGiroCatraca" or "ColetarBilhetes",
            Firmware = "5.20",
            Homologado = true,
        };

    internal static string Resumir(IReadOnlyList<string> arquivos) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{arquivos.Count} imagem(ns) gravada(s):{Environment.NewLine}" +
            $"{string.Join(Environment.NewLine, arquivos)}");
}
