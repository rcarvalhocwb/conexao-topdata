namespace Desktop.ViewModels;

/// <summary>
/// Decide se o aviso "Novidades desta versão" aparece e, quando aparece, marca a edição como
/// vista para não repetir. Toda a regra mora aqui, testável sem WPF; o painel só mostra.
/// </summary>
/// <remarks>
/// <para>
/// Regra: se já há registro de edição vista, mostra quando a edição atual é mais nova. Sem
/// registro, só mostra quando a máquina já tinha uma instalação configurada — é o caso de "atualizei
/// por cima". Numa instalação nova, fica quieto (o próprio Setup já apresentou o sistema) e só grava
/// a edição atual, para o próximo aviso com novidade aparecer.
/// </para>
/// <para>
/// A gravação é injetada (<c>marcarVista</c>) para o teste observar sem tocar em disco; no painel,
/// ela engole falha de I/O — se não deu para gravar, o aviso aparece de novo da próxima vez, o que é
/// melhor que quebrar a abertura.
/// </para>
/// </remarks>
public sealed class NovidadesViewModel : Notificavel
{
    private readonly Action<int> _marcarVista;
    private bool _aberto;
    private bool _gravado;

    public NovidadesViewModel(string versao, int? edicaoVista, bool haInstalacaoAnterior, Action<int> marcarVista)
    {
        ArgumentNullException.ThrowIfNull(marcarVista);
        _marcarVista = marcarVista;

        Versao = string.IsNullOrWhiteSpace(versao) ? "—" : versao.Trim();
        DeveMostrar = Decidir(edicaoVista, Novidades.Edicao, haInstalacaoAnterior);

        Fechar = new ComandoAssincrono(() =>
        {
            Aberto = false;
            ConfirmarVisto();
            return Task.CompletedTask;
        });

        if (DeveMostrar)
        {
            Aberto = true;
        }
        else if (edicaoVista is null)
        {
            // Instalação nova, sem registro: grava a edição atual sem incomodar, para o próximo aviso
            // com novidade aparecer. Registro já existente não é mexido aqui (nunca abaixa).
            ConfirmarVisto();
        }
    }

    /// <summary>A versão instalada, como aparece no rodapé (0.1.N).</summary>
    public string Versao { get; }

    public string Titulo { get; } = Novidades.Titulo;

    public string Abertura { get; } = Novidades.Abertura;

    public IReadOnlyList<string> Itens { get; } = Novidades.Itens;

    /// <summary>O painel deve abrir o aviso nesta partida.</summary>
    public bool DeveMostrar { get; }

    /// <summary>O aviso está à mostra.</summary>
    public bool Aberto { get => _aberto; private set => Definir(ref _aberto, value); }

    /// <summary>Fecha o aviso e marca a edição como vista.</summary>
    public ComandoAssincrono Fechar { get; }

    /// <summary>
    /// Marca a edição atual como vista. Idempotente: pode ser chamado pelo botão e pelo fechar da
    /// janela sem gravar duas vezes.
    /// </summary>
    public void ConfirmarVisto()
    {
        if (_gravado)
        {
            return;
        }

        _gravado = true;
        _marcarVista(Novidades.Edicao);
    }

    /// <summary>
    /// A regra pura: com registro, mostra quando a edição avançou; sem registro, só quando já havia
    /// instalação (atualização por cima), nunca numa máquina limpa.
    /// </summary>
    public static bool Decidir(int? edicaoVista, int edicaoAtual, bool haInstalacaoAnterior) =>
        edicaoVista is int vista ? vista < edicaoAtual : haInstalacaoAnterior;
}
