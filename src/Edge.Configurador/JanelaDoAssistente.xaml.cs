using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using Contracts;
using Edge.Supervisor;
using Edge.Supervisor.Instalacao;
using Microsoft.Win32;
using Rayzer.Design;

namespace Edge.Configurador;

/// <summary>
/// As telas do assistente. Tudo o que decide — validar, montar, gravar — está em
/// <see cref="AssistenteDeConfiguracao"/>, testado fora do Windows; aqui só se lê a tela e
/// se chama.
/// </summary>
public partial class JanelaDoAssistente : Window
{
    private const string NomeDoServico = "ConexaoTopdataEdge";

    private readonly ObservableCollection<LinhaDeCatraca> _catracas = [];

    public JanelaDoAssistente()
    {
        InitializeComponent();
        GradeDeCatracas.ItemsSource = _catracas;
        Preencher(AssistenteDeConfiguracao.Carregar(InstalacaoLocal.ArquivoDeConfiguracao));
        VerificarAmbiente(this, new RoutedEventArgs());
        AtualizarBotoes();
    }

    /// <summary>Linha editável da grade de catracas.</summary>
    public sealed class LinhaDeCatraca : INotifyPropertyChanged
    {
        private int _inner;
        private string _nome = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Inner
        {
            get => _inner;
            set
            {
                _inner = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Inner)));
            }
        }

        public string Nome
        {
            get => _nome;
            set
            {
                _nome = value ?? string.Empty;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Nome)));
            }
        }
    }

    /// <summary>Item da verificação do ambiente: símbolo, texto e tom (nunca só cor).</summary>
    public sealed record LinhaDoAmbiente(string Item, string Orientacao, string Situacao, Tom Tom);

    private static string PastaDoWorker =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Worker"));

    private void Preencher(DadosDaInstalacao dados)
    {
        _catracas.Clear();
        foreach (var c in dados.Catracas)
        {
            _catracas.Add(new LinhaDeCatraca { Inner = c.Inner, Nome = c.Nome });
        }

        CampoPorta.Text = dados.Porta.ToString(CultureInfo.InvariantCulture);
        CaixaNuvem.IsChecked = dados.NuvemLigada;
        CampoBase.Text = dados.BaseDaNuvem;
        CampoDispositivo.Text = dados.Dispositivo;
        CampoPerfil.Text = dados.Perfil;
        CampoPerfil.SelectedIndex = dados.Perfil switch
        {
            "mifare-catraca4" => 1,
            "qr-catraca4" => 2,
            _ => 0,
        };
        CampoReuso.Text = dados.IntervaloDeReusoSegundos.ToString(CultureInfo.InvariantCulture);
        CaixaUrna.IsChecked = dados.SomenteNaUrna;
        CaixaSimulacao.IsChecked = dados.Simulacao;
    }

    private DadosDaInstalacao Ler() => new()
    {
        Catracas = [.. _catracas.Where(c => c.Inner != 0 || c.Nome.Length > 0).Select(c => new CatracaInformada(c.Inner, c.Nome))],
        Porta = int.TryParse(CampoPorta.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var porta) ? porta : 0,
        NuvemLigada = CaixaNuvem.IsChecked == true,
        BaseDaNuvem = CampoBase.Text,
        Dispositivo = CampoDispositivo.Text,
        Perfil = (CampoPerfil.SelectedItem as ComboBoxItem)?.Content as string ?? "raw",
        IntervaloDeReusoSegundos = int.TryParse(CampoReuso.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var reuso) ? reuso : -1,
        SomenteNaUrna = CaixaUrna.IsChecked == true,
        Simulacao = CaixaSimulacao.IsChecked == true,
        Segredo = string.IsNullOrEmpty(CampoSegredo.Password) ? null : CampoSegredo.Password,
    };

    private void VerificarAmbiente(object sender, RoutedEventArgs e)
    {
        var itens = AssistenteDeConfiguracao.VerificarAmbiente(
            File.Exists,
            Net35Instalado(),
            PastaDoWorker,
            CaixaSimulacao.IsChecked == true,
            ControleDaOperacao.FirewallLiberado());

        ListaDoAmbiente.ItemsSource = itens.Select(i => i.Ok switch
        {
            true => new LinhaDoAmbiente(i.Item, i.Orientacao, "OK", Tom.Sucesso),
            false => new LinhaDoAmbiente(i.Item, i.Orientacao, "FALTA", Tom.Perigo),
            _ => new LinhaDoAmbiente(i.Item, i.Orientacao, "CONFERIR", Tom.Atencao),
        }).ToList();
    }

    private void LocalizarEasyInner(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Localizar a EasyInner.dll do SDK da Topdata",
            Filter = "EasyInner.dll|EasyInner.dll",
        };

        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var destino = AssistenteDeConfiguracao.CopiarEasyInner(dialogo.FileName, PastaDoWorker);
            TextoDoAmbiente.Text = $"EasyInner.dll copiada para {destino}.";
        }
        catch (Exception erro) when (erro is ArgumentException or IOException or UnauthorizedAccessException)
        {
            TextoDoAmbiente.Text = $"Não foi possível copiar: {erro.Message}";
        }

        VerificarAmbiente(this, e);
    }

    private void LiberarFirewall(object sender, RoutedEventArgs e)
    {
        TextoDoAmbiente.Text = ControleDaOperacao.LiberarFirewall(Path.Combine(PastaDoWorker, "Edge.Worker.X86.exe"))
            ? "Firewall liberado para as catracas da rede local (entrada TCP)."
            : "O Windows não aceitou criar a regra. Veja o RB-02 ou libere a porta das catracas no firewall à mão.";
        VerificarAmbiente(this, e);
    }

    // DISM é a ferramenta do próprio Windows para ligar recursos. Pode precisar de internet
    // (Windows Update) e de alguns minutos; roda em segundo plano para a janela não travar.
    private void HabilitarNet35(object sender, RoutedEventArgs e) =>
        _ = RodarDism(null, e);

    // Sem internet (achado E10-8 do docs/41): a pasta sources\sxs da mídia do Windows.
    private void HabilitarNet35SemInternet(object sender, RoutedEventArgs e)
    {
        var escolha = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pasta sources\\sxs da mídia de instalação do Windows",
        };

        if (escolha.ShowDialog(this) != true)
        {
            return;
        }

        var problema = NetFx35.ProblemaNaPasta(escolha.FolderName);
        if (problema.Length > 0)
        {
            TextoDoAmbiente.Text = problema;
            return;
        }

        _ = RodarDism(escolha.FolderName, e);
    }

    private async Task RodarDism(string? pastaDaMidia, RoutedEventArgs e)
    {
        BotaoNet35.IsEnabled = false;
        BotaoNet35SemInternet.IsEnabled = false;
        TextoDoAmbiente.Text = pastaDaMidia is null
            ? "Habilitando o .NET Framework 3.5… pode levar alguns minutos."
            : "Habilitando o .NET Framework 3.5 pela mídia do Windows, sem internet… pode levar alguns minutos.";

        try
        {
            var inicio = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "dism.exe"),
                Arguments = NetFx35.ArgumentosDoDism(pastaDaMidia),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var processo = System.Diagnostics.Process.Start(inicio)!;
            await processo.WaitForExitAsync().ConfigureAwait(true);

            TextoDoAmbiente.Text = processo.ExitCode switch
            {
                0 => ".NET Framework 3.5 habilitado.",
                3010 => ".NET Framework 3.5 habilitado. Reinicie o computador para concluir.",
                _ when pastaDaMidia is null => $"O Windows não conseguiu habilitar (código {processo.ExitCode}). Sem internet, use " +
                     "\"Habilitar sem internet\" com a mídia do Windows, ou habilite em 'Ativar ou desativar recursos do Windows'.",
                _ => $"O Windows não conseguiu habilitar pela mídia (código {processo.ExitCode}). Confira se a mídia é da mesma " +
                     "versão do Windows deste PC.",
            };
        }
        catch (Exception erro) when (erro is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            TextoDoAmbiente.Text = $"Não foi possível executar o DISM: {erro.Message}";
        }
        finally
        {
            BotaoNet35.IsEnabled = true;
            BotaoNet35SemInternet.IsEnabled = true;
            VerificarAmbiente(this, e);
        }
    }

    /// <summary>
    /// Autoteste: vai do primeiro ao último passo pelo botão Avançar e volta, deixando a
    /// tela desenhar cada um. Não grava nada.
    /// </summary>
    internal async Task PercorrerPassosAsync()
    {
        for (var i = 0; i < Passos.Items.Count * 2; i++)
        {
            if (i < Passos.Items.Count - 1)
            {
                Avancar(this, new RoutedEventArgs());
            }
            else if (Passos.SelectedIndex > 0)
            {
                Voltar(this, new RoutedEventArgs());
            }

            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // Uma catraca digitada na grade, como o instalador faria, e o resumo de novo.
        _catracas.Add(new LinhaDeCatraca { Inner = 1, Nome = "Entrada 1" });
        Passos.SelectedIndex = Passos.Items.Count - 1;
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static bool? Net35Instalado()
    {
        try
        {
            using var chave = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5");
            return chave?.GetValue("Install") is int instalado ? instalado == 1 : false;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private void PassoMudou(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Passos))
        {
            return;
        }

        AtualizarBotoes();

        if (Passos.SelectedIndex == Passos.Items.Count - 1)
        {
            MostrarResumo();
        }
    }

    private void AtualizarBotoes()
    {
        BotaoVoltar.IsEnabled = Passos.SelectedIndex > 0;
        BotaoAvancar.IsEnabled = Passos.SelectedIndex < Passos.Items.Count - 1;
    }

    private void Voltar(object sender, RoutedEventArgs e) => Passos.SelectedIndex--;

    private void Avancar(object sender, RoutedEventArgs e) => Passos.SelectedIndex++;

    private void MostrarResumo()
    {
        GradeDeCatracas.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        var dados = Ler();

        var catracas = string.Join(", ", dados.Catracas.Select(c => $"{c.Inner} ({c.Nome})"));
        var nuvem = dados.NuvemLigada
            ? $"Nuvem ligada, este computador como \"{dados.Dispositivo}\", cartão no formato {dados.Perfil}, " +
              $"reuso após {dados.IntervaloDeReusoSegundos} s{(dados.SomenteNaUrna ? ", só na urna" : string.Empty)}. " +
              (dados.Segredo is null ? "Segredo: mantém o atual." : "Segredo: novo.")
            : "Nuvem desligada.";

        TextoResumo.Text =
            (dados.Simulacao ? "MODO SIMULAÇÃO: nenhuma catraca física será acionada." + Environment.NewLine : string.Empty) +
            $"{dados.Catracas.Count} catraca(s) na porta {dados.Porta}: {catracas}.{Environment.NewLine}{nuvem}";

        var problemas = AssistenteDeConfiguracao.Validar(dados);
        ListaDeProblemas.ItemsSource = problemas;
        BotaoGravar.IsEnabled = problemas.Count == 0;
        TextoResultado.Text = problemas.Count == 0 ? string.Empty : "Corrija os itens acima nos passos anteriores.";
    }

    private void Gravar(object sender, RoutedEventArgs e)
    {
        // Achado E6-5 do docs/41: com o serviço rodando, gravar o reinicia, e todas as catracas ficam
        // sem atender por até um minuto. Antes isso acontecia sem aviso; feito durante o evento,
        // parava o portão inteiro. Agora pergunta, com "Não" como padrão.
        if (ServicoRodando() && MessageBox.Show(
                this,
                "O serviço das catracas está rodando. Gravar a configuração reinicia o serviço, e todas as " +
                "catracas ficam sem atender por até um minuto.\n\nGravar e reiniciar agora?",
                "Reiniciar o serviço das catracas",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            TextoResultado.Text = "Nada foi gravado. O serviço segue rodando com a configuração anterior.";
            return;
        }

        var dados = Ler();

        try
        {
            var arquivo = AssistenteDeConfiguracao.Gravar(
                dados,
                InstalacaoLocal.ArquivoDeConfiguracao,
                Path.Combine(PastaDoWorker, "Edge.Worker.X86.exe"),
                new CofreDpapi(InstalacaoLocal.PastaDosSegredos));

            CampoSegredo.Clear();
            TextoResultado.Text = $"Configuração gravada em {arquivo}. {ReiniciarServico()}";
        }
        catch (Exception erro) when (erro is ArgumentException or IOException or UnauthorizedAccessException)
        {
            TextoResultado.Text = $"Não foi possível gravar: {erro.Message}";
        }
    }

    private static bool ServicoRodando()
    {
        try
        {
            using var servico = new ServiceController(NomeDoServico);
            return servico.Status is not ServiceControllerStatus.Stopped;
        }
        catch (InvalidOperationException)
        {
            // Serviço não instalado (ou sem permissão para consultar): não há o que interromper.
            return false;
        }
    }

    private static string ReiniciarServico()
    {
        try
        {
            using var servico = new ServiceController(NomeDoServico);

            if (servico.Status is not ServiceControllerStatus.Stopped)
            {
                servico.Stop();
                servico.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            servico.Start();
            servico.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            return "O serviço foi reiniciado e já está usando a nova configuração. Abra o Painel do evento.";
        }
        catch (Exception erro) when (erro is InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            return $"Mas o serviço não reiniciou ({erro.Message}). Reinicie o computador ou o serviço \"{NomeDoServico}\".";
        }
    }
}
