using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Desktop.ViewModels;
using Forms = System.Windows.Forms;

namespace Desktop.App;

/// <summary>
/// O ícone do Rayzer XAcess perto do relógio do Windows.
/// </summary>
/// <remarks>
/// <para>
/// As catracas são atendidas pelo <b>serviço</b>, que sobe com o Windows mesmo sem ninguém
/// entrar. O painel é só a janela do operador: fechar o painel não para as catracas. O
/// ícone deixa isso visível — mostra a situação ao passar o mouse, avisa quando uma catraca
/// para — e dá o caminho explícito para encerrar a operação, que pede administrador.
/// </para>
/// <para>
/// A lógica (texto e avisos) está em <see cref="ResumoDaBandeja"/>, testada sem Windows.
/// </para>
/// </remarks>
internal sealed class BandejaDoSistema : IDisposable
{
    private readonly Forms.NotifyIcon _icone;
    private readonly JanelaPrincipal _janela;
    private readonly ResumoDaBandeja _resumo = new();
    private bool _avisouQueContinua;

    public BandejaDoSistema(JanelaPrincipal janela, Action sair)
    {
        ArgumentNullException.ThrowIfNull(janela);
        ArgumentNullException.ThrowIfNull(sair);
        _janela = janela;

        var menu = new Forms.ContextMenuStrip();
        var abrir = menu.Items.Add("Abrir o painel", null, (_, _) => _janela.Mostrar());
        abrir.Font = new System.Drawing.Font(abrir.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Gerenciar catraca", null, (_, _) =>
        {
            _janela.Mostrar();
            _janela.Janela.TelaAtual = _janela.Janela.Telas.OfType<GerenciarCatracaViewModel>().First();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Iniciar a operação", null, async (_, _) => await ControlarAsync(parar: false).ConfigureAwait(true));
        menu.Items.Add("Encerrar a operação (parar as catracas)…", null, async (_, _) => await ControlarAsync(parar: true).ConfigureAwait(true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Fechar o painel (as catracas continuam)", null, (_, _) => sair());

        _icone = new Forms.NotifyIcon
        {
            Icon = Icone(),
            Text = "Rayzer XAcess",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icone.DoubleClick += (_, _) => _janela.Mostrar();
        _icone.BalloonTipClicked += (_, _) => _janela.Mostrar();
    }

    /// <summary>Atualiza o texto do ícone e avisa se algo mudou para pior.</summary>
    public void Atualizar(PainelAoVivoViewModel painel)
    {
        _icone.Text = ResumoDaBandeja.Dica(painel);

        if (_resumo.Observar(painel) is { } aviso)
        {
            _icone.ShowBalloonTip(8000, aviso.Titulo, aviso.Texto, aviso.Problema ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>Na primeira vez que a janela vai para a bandeja, explica que nada parou.</summary>
    public void AvisarQueContinua()
    {
        if (_avisouQueContinua)
        {
            return;
        }

        _avisouQueContinua = true;
        _icone.ShowBalloonTip(
            8000,
            "O Rayzer XAcess continua operando",
            "As catracas seguem atendendo. O painel está aqui, perto do relógio: clique duas vezes para abrir.",
            Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icone.Visible = false;
        _icone.ContextMenuStrip?.Dispose();
        _icone.Icon?.Dispose();
        _icone.Dispose();
    }

    private static System.Drawing.Icon Icone()
    {
        var recurso = Application.GetResourceStream(new Uri("pack://application:,,,/Rayzer.Design;component/Marca/rayzer-xacess.ico"));
        using var fluxo = recurso.Stream;
        return new System.Drawing.Icon(fluxo, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>
    /// Para ou inicia o serviço pelo assistente, que pede administrador (UAC). O painel
    /// continua sem administrador.
    /// </summary>
    private async Task ControlarAsync(bool parar)
    {
        if (parar)
        {
            _janela.Mostrar();
            var resposta = MessageBox.Show(
                _janela,
                "Encerrar a operação para o serviço do Rayzer XAcess: as catracas deixam de ser atendidas pelo sistema " +
                "até a operação ser iniciada de novo." + Environment.NewLine + Environment.NewLine +
                "Faça isso só com o evento fechado. O Windows vai pedir permissão de administrador.",
                "Encerrar a operação",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (resposta != MessageBoxResult.OK)
            {
                return;
            }
        }

        var assistente = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Configurador", "Edge.Configurador.exe"));
        int? codigo;

        try
        {
            using var processo = Process.Start(new ProcessStartInfo(assistente, parar ? "--parar-operacao" : "--iniciar-operacao")
            {
                UseShellExecute = true,
                Verb = "runas",
            });

            if (processo is null)
            {
                codigo = 1;
            }
            else
            {
                await processo.WaitForExitAsync().ConfigureAwait(true);
                codigo = processo.ExitCode;
            }
        }
        catch (Win32Exception erro) when (erro.NativeErrorCode == 1223)
        {
            // 1223: o operador recusou a permissão.
            codigo = null;
        }
        catch (Win32Exception)
        {
            codigo = 1;
        }

        var texto = ResumoDaBandeja.ResultadoDoControle(parar, codigo);
        _icone.ShowBalloonTip(8000, parar ? "Encerrar a operação" : "Iniciar a operação", texto,
            codigo == 0 ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
    }
}
