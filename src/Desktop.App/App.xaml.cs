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
            //
            // ShutdownMode explícito é obrigatório aqui, e custou 24 minutos de runner para
            // eu descobrir. O padrão é OnLastWindowClose: em modo de captura nenhuma janela
            // é aberta, então não há última janela para fechar, e o laço de mensagens fica
            // rodando para sempre depois do Shutdown. O processo trava sem erro nenhum.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var pasta = indice + 1 < e.Args.Length ? e.Args[indice + 1] : "capturas";

            // Assíncrono de propósito: o laço de mensagens precisa rodar para as telas
            // receberem as respostas do serviço.
            _ = CapturarAsync(pasta);
            return;
        }

        var janela = new JanelaPrincipal();
        janela.Iniciar();
        janela.Show();
    }

    private static async Task CapturarAsync(string pasta)
    {
        try
        {
            var arquivos = await CapturaDeTela.RenderizarAsync(pasta).ConfigureAwait(true);
            Console.WriteLine(CapturaDeTela.Resumir(arquivos));
        }
        catch (Exception erro)
        {
            // Engolir aqui seria cruel: quem roda isto está justamente tentando descobrir
            // se funciona, e um processo que sai calado não responde nada.
            Console.Error.WriteLine($"A captura falhou: {erro}");
            Console.Error.Flush();
            Environment.Exit(1);
        }

        // Environment.Exit e não Shutdown: numa ferramenta de linha de comando, sair é o
        // que se quer — não pedir para sair.
        Console.Out.Flush();
        Environment.Exit(0);
    }
}
