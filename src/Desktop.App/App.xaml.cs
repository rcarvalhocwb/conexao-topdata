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
        Portugues.Aplicar();
        Rayzer.Design.TemaRayzer.Instalar(this, "XAcess");
        TratarErrosInesperados("painel");

        // Na captura de telas, a foto é do painel, não da abertura.
        Rayzer.Design.RayzerAbertura.Desligada = e.Args.Contains(ArgumentoDeCaptura);

        var autoteste = Array.IndexOf(e.Args, "--autoteste");
        if (autoteste >= 0)
        {
            _saidaDoAutoteste = autoteste + 1 < e.Args.Length ? e.Args[autoteste + 1] : "autoteste-painel.txt";
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = AutotesteAsync(_saidaDoAutoteste, e.Args.Contains("--com-simulacao"));
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

            // --tema escuro | claro: a captura de cada tema, sem mexer na preferência gravada.
            var indiceDoTema = Array.IndexOf(e.Args, "--tema");
            if (indiceDoTema >= 0 && indiceDoTema + 1 < e.Args.Length
                && Enum.TryParse<Rayzer.Design.Tema>(e.Args[indiceDoTema + 1], ignoreCase: true, out var tema))
            {
                Rayzer.Design.TemaRayzer.Aplicar(tema, gravar: false);
            }

            // Assíncrono de propósito: o laço de mensagens precisa rodar para as telas
            // receberem as respostas do serviço.
            _ = CapturarAsync(pasta);
            return;
        }

        AbrirComBandeja(naBandeja: e.Args.Contains(ArgumentoDaBandeja));
    }

    /// <summary>Abre escondido, só com o ícone perto do relógio (início com o Windows).</summary>
    private const string ArgumentoDaBandeja = "--bandeja";

    /// <summary>
    /// O jeito normal de abrir: uma instância por sessão, ícone perto do relógio, e fechar a
    /// janela só a esconde. As catracas não dependem de nada disto (quem as atende é o
    /// serviço), mas o operador vê a situação sem abrir nada.
    /// </summary>
    private void AbrirComBandeja(bool naBandeja)
    {
        var instancia = InstanciaUnica.Tentar();
        if (instancia is null)
        {
            // Já há um painel nesta sessão: ele foi avisado para aparecer.
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var janela = new JanelaPrincipal { FecharVaiParaBandeja = true };
        janela.Iniciar();

        var bandeja = new BandejaDoSistema(janela, sair: () =>
        {
            janela.SairDeVez();
            Shutdown();
        });
        janela.Atualizada += (_, _) => bandeja.Atualizar(janela.Janela.Painel);
        janela.FoiParaBandeja += (_, _) => bandeja.AvisarQueContinua();
        instancia.AoPedirParaMostrar(() => Dispatcher.BeginInvoke(janela.Mostrar));

        // Desligar ou sair do Windows fecha de verdade, sem ficar esperando a bandeja.
        SessionEnding += (_, _) => janela.SairDeVez();
        Exit += (_, _) =>
        {
            bandeja.Dispose();
            instancia.Dispose();
        };

        if (!naBandeja)
        {
            janela.Show();
            janela.MostrarNovidadesUmaVez();
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
                "Rayzer XAcess",
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
    private static async Task AutotesteAsync(string saida, bool comSimulacao)
    {
        // Cada passo vai para o arquivo na hora: se travar, dá para ver onde.
        void Passo(string texto) => File.AppendAllText(saida, texto + Environment.NewLine);

        try
        {
            File.WriteAllText(saida, string.Empty);
            var janela = new JanelaPrincipal();
            janela.Show();
            Passo("janela aberta");

            // Nos dois temas: recurso de tema com nome errado ou template quebrado só aparece
            // quando a tela é desenhada com aquele dicionário.
            foreach (var tema in new[] { Rayzer.Design.Tema.Claro, Rayzer.Design.Tema.Escuro })
            {
                Rayzer.Design.TemaRayzer.Aplicar(tema, gravar: false);

                foreach (var tela in janela.Janela.Telas)
                {
                    janela.Janela.TelaAtual = tela;
                    await janela.Janela.AtualizarAsync().ConfigureAwait(true);
                    await tela.AtualizarAsync().ConfigureAwait(true);
                    await janela.Dispatcher.InvokeAsync(janela.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Passo($"tema {tema} · tela {tela.Titulo}: ok");
                }
            }

            if (comSimulacao)
            {
                // Com o serviço em modo simulação: passa um QR de teste e exige a liberação.
                var simulador = janela.Janela.Telas.OfType<Desktop.ViewModels.SimuladorViewModel>().Single();
                janela.Janela.TelaAtual = simulador;
                simulador.Catraca = "1";
                simulador.Codigo = "1000000001";
                simulador.Girar = true;

                for (var tentativa = 0; tentativa < 30 && !simulador.Resultados.Any(r => r.Liberado); tentativa++)
                {
                    await simulador.Passar.ExecutarAsync().ConfigureAwait(true);
                    await Task.Delay(1000).ConfigureAwait(true);
                }

                if (!simulador.Resultados.Any(r => r.Liberado))
                {
                    throw new InvalidOperationException($"A catraca simulada não liberou o QR de teste: {simulador.Mensagem}");
                }

                Passo($"simulação: {simulador.Mensagem}");
            }

            // Aviso de novidades: nenhuma tela o exercita, então o autoteste o constrói, mostra e
            // fecha — é o que pega chave de recurso ou ligação quebrada antes da bancada.
            var novidades = new Desktop.ViewModels.NovidadesViewModel(
                "0.0.0", edicaoVista: 0, haInstalacaoAnterior: true, marcarVista: _ => { });
            var avisoNovidades = new JanelaDeNovidades(novidades) { Owner = janela };
            avisoNovidades.Show();
            await janela.Dispatcher.InvokeAsync(avisoNovidades.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            avisoNovidades.Close();
            Passo("novidades: ok");

            janela.Close();
            Passo("ok");
            Environment.Exit(0);
        }
        catch (Exception erro)
        {
            File.AppendAllText(saida, erro.ToString());
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

            // Também em arquivo, ao lado das imagens: num WinExe a saída de console se perde.
            try
            {
                Directory.CreateDirectory(pasta);
                File.WriteAllText(Path.Combine(pasta, "captura-erro.txt"), erro.ToString());
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Environment.Exit(1);
        }

        // Environment.Exit e não Shutdown: numa ferramenta de linha de comando, sair é o
        // que se quer — não pedir para sair.
        Console.Out.Flush();
        Environment.Exit(0);
    }
}
