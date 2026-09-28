using System.IO;
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
        TratarErrosInesperados("painel");

        var autoteste = Array.IndexOf(e.Args, "--autoteste");
        if (autoteste >= 0)
        {
            _saidaDoAutoteste = autoteste + 1 < e.Args.Length ? e.Args[autoteste + 1] : "autoteste-painel.txt";
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = AutotesteAsync(_saidaDoAutoteste);
            return;
        }

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

    /// <summary>
    /// Erro inesperado vira mensagem na tela e linha em arquivo, e o aplicativo continua
    /// aberto. Sem isto, o WPF fecha a janela calado e ninguém sabe por quê.
    /// </summary>
    private void TratarErrosInesperados(string aplicativo)
    {
        DispatcherUnhandledException += (_, e) =>
        {
            if (_saidaDoAutoteste is not null)
            {
                // No autoteste ninguém clica em OK: grava e sai com erro.
                File.WriteAllText(_saidaDoAutoteste, e.Exception.ToString());
                Environment.Exit(1);
            }

            var arquivo = Contracts.RegistroDeFalhas.Gravar(aplicativo, e.Exception);
            MessageBox.Show(
                $"Aconteceu um erro inesperado e a tela continuou aberta.{Environment.NewLine}{Environment.NewLine}" +
                $"{e.Exception.GetType().Name}: {e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                (arquivo is null ? "Não foi possível gravar o detalhe." : $"Detalhe gravado em:{Environment.NewLine}{arquivo}"),
                "Conexão Topdata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception erro)
            {
                Contracts.RegistroDeFalhas.Gravar(aplicativo, erro);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Contracts.RegistroDeFalhas.Gravar(aplicativo, e.Exception);
            e.SetObserved();
        };
    }

    private string? _saidaDoAutoteste;

    /// <summary>
    /// Abre a janela de verdade, passa por todas as telas e sai: 0 se nada quebrou. Sem
    /// serviço, cada tela mostra "sem resposta" — que também precisa desenhar sem erro.
    /// </summary>
    private static async Task AutotesteAsync(string saida)
    {
        try
        {
            var janela = new JanelaPrincipal();
            janela.Show();

            foreach (var tela in janela.Janela.Telas)
            {
                janela.Janela.TelaAtual = tela;
                await janela.Janela.AtualizarAsync().ConfigureAwait(true);
                await tela.AtualizarAsync().ConfigureAwait(true);
                await janela.Dispatcher.InvokeAsync(janela.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            janela.Close();
            File.WriteAllText(saida, "ok");
            Environment.Exit(0);
        }
        catch (Exception erro)
        {
            File.WriteAllText(saida, erro.ToString());
            Environment.Exit(1);
        }
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
