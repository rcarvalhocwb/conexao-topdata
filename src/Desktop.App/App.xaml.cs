using System.Windows;

namespace Desktop.App;

public partial class App : Application
{
    /// <summary>Argumento que liga o modo de captura em vez de abrir a janela.</summary>
    private const string ArgumentoDeCaptura = "--capturar";

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        var indice = Array.IndexOf(e.Args, ArgumentoDeCaptura);

        if (indice >= 0)
        {
            // Modo de captura: renderiza a tela em PNG e encerra. É o que permite ter
            // imagem real do painel sem ninguém na frente do monitor.
            var pasta = indice + 1 < e.Args.Length ? e.Args[indice + 1] : "capturas";
            var arquivos = CapturaDeTela.Renderizar(pasta);

            Console.WriteLine(CapturaDeTela.Resumir(arquivos));
            Shutdown(0);
            return;
        }

        new JanelaPrincipal().Show();
    }
}
