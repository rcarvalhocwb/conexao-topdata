using System.IO;
using System.Windows;

namespace Edge.Configurador;

public partial class App : Application
{
    /// <summary>Abre o assistente, percorre os passos e sai: 0 se nada quebrou.</summary>
    private const string ArgumentoDeAutoteste = "--autoteste";

    private string? _saidaDoAutoteste;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var indice = Array.IndexOf(e.Args, ArgumentoDeAutoteste);

        if (indice >= 0)
        {
            _saidaDoAutoteste = indice + 1 < e.Args.Length ? e.Args[indice + 1] : "autoteste-assistente.txt";
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        TratarErrosInesperados("assistente");
        base.OnStartup(e);

        if (_saidaDoAutoteste is not null)
        {
            _ = AutotesteAsync(_saidaDoAutoteste);
            return;
        }

        new JanelaDoAssistente().Show();
    }

    private static async Task AutotesteAsync(string saida)
    {
        try
        {
            var janela = new JanelaDoAssistente();
            janela.Show();
            await janela.PercorrerPassosAsync().ConfigureAwait(true);
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
}
