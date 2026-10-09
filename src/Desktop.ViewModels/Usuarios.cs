using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>Uma opção de marcar (papel de um usuário, permissão de um papel).</summary>
public sealed class OpcaoMarcavel(string codigo, string nome, string grupo, bool marcado) : Notificavel
{
    private bool _marcado = marcado;

    public string Codigo { get; } = codigo;

    public string Nome { get; } = nome;

    /// <summary>Agrupamento na tela (permissões).</summary>
    public string Grupo { get; } = grupo;

    public bool Marcado { get => _marcado; set => Definir(ref _marcado, value); }
}

/// <summary>Um usuário na lista.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Login">Login.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Situacao">Ativo, Inativo ou "Troca a senha no próximo acesso".</param>
/// <param name="Papeis">Nomes dos papéis.</param>
/// <param name="UltimoAcesso">Quando entrou pela última vez.</param>
public sealed record LinhaDeUsuario(string Id, string Login, string Nome, string Situacao, string Papeis, string UltimoAcesso);

/// <summary>Um papel na lista.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Descricao">Para que serve.</param>
/// <param name="Origem">"Do sistema" ou "Criado aqui".</param>
/// <param name="Resumo">Quantas permissões.</param>
public sealed record LinhaDePapel(string Id, string Nome, string Descricao, string Origem, string Resumo);

/// <summary>
/// Usuários do sistema e papéis (ADR-0026): o administrador cria usuários, escolhe os papéis de cada
/// um, redefine senhas e decide o que cada papel pode fazer.
/// </summary>
public sealed class UsuariosViewModel : TelaBase
{
    private IReadOnlyList<Contracts.Edge.V1.Usuario> _usuariosDoServico = [];
    private IReadOnlyList<Papel> _papeisDoServico = [];
    private IReadOnlyList<PermissaoDoCatalogo> _catalogo = [];
    private IReadOnlyList<LinhaDeUsuario> _usuarios = [];
    private IReadOnlyList<LinhaDePapel> _papeis = [];
    private LinhaDeUsuario? _usuarioSelecionado;
    private LinhaDePapel? _papelSelecionado;
    private string _usuarioId = string.Empty;
    private string _login = string.Empty;
    private string _nome = string.Empty;
    private bool _ativo = true;
    private string _senhaInicial = string.Empty;
    private string _senhaProvisoria = string.Empty;
    private IReadOnlyList<OpcaoMarcavel> _papeisDoUsuario = [];
    private string _papelId = string.Empty;
    private string _papelNome = string.Empty;
    private string _papelDescricao = string.Empty;
    private IReadOnlyList<OpcaoMarcavel> _permissoesDoPapel = [];

    public UsuariosViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        NovoUsuario = new ComandoAssincrono(() =>
        {
            UsuarioSelecionado = null;
            PreencherUsuario(null);
            return Task.CompletedTask;
        });
        GravarUsuario = new ComandoAssincrono(GravarUsuarioAsync);
        RedefinirSenha = new ComandoAssincrono(RedefinirSenhaAsync, () => UsuarioId.Length > 0);
        NovoPapel = new ComandoAssincrono(() =>
        {
            PapelSelecionado = null;
            PreencherPapel(null);
            return Task.CompletedTask;
        });
        GravarPapel = new ComandoAssincrono(GravarPapelAsync);
    }

    public override string Titulo => "Usuários";

    public ComandoAssincrono NovoUsuario { get; }

    public ComandoAssincrono GravarUsuario { get; }

    public ComandoAssincrono RedefinirSenha { get; }

    public ComandoAssincrono NovoPapel { get; }

    public ComandoAssincrono GravarPapel { get; }

    public IReadOnlyList<LinhaDeUsuario> Usuarios { get => _usuarios; private set => Definir(ref _usuarios, value); }

    public IReadOnlyList<LinhaDePapel> Papeis { get => _papeis; private set => Definir(ref _papeis, value); }

    public LinhaDeUsuario? UsuarioSelecionado
    {
        get => _usuarioSelecionado;
        set
        {
            if (Definir(ref _usuarioSelecionado, value) && value is not null)
            {
                PreencherUsuario(_usuariosDoServico.FirstOrDefault(u => u.Id == value.Id));
            }
        }
    }

    public LinhaDePapel? PapelSelecionado
    {
        get => _papelSelecionado;
        set
        {
            if (Definir(ref _papelSelecionado, value) && value is not null)
            {
                PreencherPapel(_papeisDoServico.FirstOrDefault(p => p.Id == value.Id));
            }
        }
    }

    /// <summary>Vazio quando o formulário é de um usuário novo.</summary>
    public string UsuarioId
    {
        get => _usuarioId;
        private set
        {
            if (Definir(ref _usuarioId, value))
            {
                Avisar(nameof(CriandoUsuario));
                Avisar(nameof(TituloDoUsuario));
                RedefinirSenha.ReavaliarDisponibilidade();
            }
        }
    }

    public bool CriandoUsuario => UsuarioId.Length == 0;

    public string TituloDoUsuario => CriandoUsuario ? "Novo usuário" : $"Usuário: {Nome}";

    public string Login { get => _login; set => Definir(ref _login, value ?? string.Empty); }

    public string Nome { get => _nome; set => Definir(ref _nome, value ?? string.Empty); }

    public bool Ativo { get => _ativo; set => Definir(ref _ativo, value); }

    /// <summary>Só para usuário novo: a pessoa troca no primeiro acesso.</summary>
    public string SenhaInicial { get => _senhaInicial; set => Definir(ref _senhaInicial, value ?? string.Empty); }

    /// <summary>Para redefinir a senha de quem esqueceu: a pessoa troca no próximo acesso.</summary>
    public string SenhaProvisoria { get => _senhaProvisoria; set => Definir(ref _senhaProvisoria, value ?? string.Empty); }

    public IReadOnlyList<OpcaoMarcavel> PapeisDoUsuario { get => _papeisDoUsuario; private set => Definir(ref _papeisDoUsuario, value); }

    public string PapelId
    {
        get => _papelId;
        private set
        {
            if (Definir(ref _papelId, value))
            {
                Avisar(nameof(TituloDoPapel));
            }
        }
    }

    public string TituloDoPapel => PapelId.Length == 0 ? "Novo papel" : $"Papel: {PapelNome}";

    public string PapelNome { get => _papelNome; set => Definir(ref _papelNome, value ?? string.Empty); }

    public string PapelDescricao { get => _papelDescricao; set => Definir(ref _papelDescricao, value ?? string.Empty); }

    public IReadOnlyList<OpcaoMarcavel> PermissoesDoPapel { get => _permissoesDoPapel; private set => Definir(ref _permissoesDoPapel, value); }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        await Tentar(async () =>
        {
            var usuarios = await Cliente.ListarUsuariosAsync(new ListarUsuariosRequest(), cancellationToken: cancelamento);
            var papeis = await Cliente.ListarPapeisAsync(new ListarPapeisRequest(), cancellationToken: cancelamento);
            _usuariosDoServico = [.. usuarios.Usuarios];
            _papeisDoServico = [.. papeis.Papeis];
            _catalogo = [.. papeis.Catalogo];

            var nomes = _papeisDoServico.ToDictionary(p => p.Id, p => p.Nome, StringComparer.Ordinal);
            Usuarios = [.. _usuariosDoServico.Select(u => new LinhaDeUsuario(
                u.Id,
                u.Login,
                u.Nome,
                !u.Ativo ? "Inativo" : u.TrocarSenha ? "Troca a senha no próximo acesso" : "Ativo",
                string.Join(", ", u.Papeis.Select(p => nomes.GetValueOrDefault(p, p))),
                u.UltimoAcesso is null ? "—" : FusoDoEvento.NoEvento(u.UltimoAcesso.ToDateTimeOffset()).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)))];
            Papeis = [.. _papeisDoServico.Select(p => new LinhaDePapel(
                p.Id, p.Nome, p.Descricao, p.DoSistema ? "Do sistema" : "Criado aqui",
                string.Create(CultureInfo.InvariantCulture, $"{p.Permissoes.Count} de {_catalogo.Count} permissões")))];

            // Mantém o que está aberto no formulário, com as opções atualizadas.
            PreencherUsuario(_usuariosDoServico.FirstOrDefault(u => u.Id == UsuarioId), manterTexto: UsuarioId.Length == 0);
            PreencherPapel(_papeisDoServico.FirstOrDefault(p => p.Id == PapelId), manterTexto: PapelId.Length == 0);
        }).ConfigureAwait(true);
    }

    private void PreencherUsuario(Contracts.Edge.V1.Usuario? usuario, bool manterTexto = false)
    {
        if (!manterTexto)
        {
            UsuarioId = usuario?.Id ?? string.Empty;
            Login = usuario?.Login ?? string.Empty;
            Nome = usuario?.Nome ?? string.Empty;
            Ativo = usuario?.Ativo ?? true;
            SenhaInicial = string.Empty;
            SenhaProvisoria = string.Empty;
        }

        var marcados = usuario?.Papeis.ToHashSet(StringComparer.Ordinal) ?? PapeisDoUsuario.Where(o => o.Marcado).Select(o => o.Codigo).ToHashSet(StringComparer.Ordinal);
        PapeisDoUsuario = [.. _papeisDoServico.Select(p => new OpcaoMarcavel(p.Id, p.Nome, p.Descricao, marcados.Contains(p.Id)))];
        Avisar(nameof(TituloDoUsuario));
    }

    private void PreencherPapel(Papel? papel, bool manterTexto = false)
    {
        if (!manterTexto)
        {
            PapelId = papel?.Id ?? string.Empty;
            PapelNome = papel?.Nome ?? string.Empty;
            PapelDescricao = papel?.Descricao ?? string.Empty;
        }

        var marcadas = papel?.Permissoes.ToHashSet(StringComparer.Ordinal) ?? PermissoesDoPapel.Where(o => o.Marcado).Select(o => o.Codigo).ToHashSet(StringComparer.Ordinal);
        PermissoesDoPapel = [.. _catalogo.Select(p => new OpcaoMarcavel(p.Codigo, p.Nome, p.Grupo, marcadas.Contains(p.Codigo)))];
        Avisar(nameof(TituloDoPapel));
    }

    private async Task GravarUsuarioAsync()
    {
        var pedido = new GravarUsuarioRequest
        {
            Id = UsuarioId,
            Login = Login.Trim(),
            Nome = Nome.Trim(),
            Ativo = Ativo,
            SenhaInicial = CriandoUsuario ? SenhaInicial : string.Empty,
        };
        pedido.Papeis.AddRange(PapeisDoUsuario.Where(o => o.Marcado).Select(o => o.Codigo));

        await Tentar(async () =>
        {
            var r = await Cliente.GravarUsuarioAsync(pedido);
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = CriandoUsuario
                ? $"Usuário {pedido.Login} criado. No primeiro acesso, a pessoa troca a senha inicial."
                : $"Usuário {pedido.Login} gravado.";
            UsuarioId = r.Id;
            SenhaInicial = string.Empty;
        }).ConfigureAwait(true);

        await AtualizarAsync().ConfigureAwait(true);
    }

    private async Task RedefinirSenhaAsync()
    {
        await Tentar(async () =>
        {
            var r = await Cliente.RedefinirSenhaAsync(new RedefinirSenhaRequest { UsuarioId = UsuarioId, SenhaProvisoria = SenhaProvisoria });
            Mensagem = r.Redefinida
                ? $"Senha de {Login} redefinida. A pessoa troca no próximo acesso; as sessões abertas dela foram encerradas."
                : string.Join(" ", r.Problemas);
            if (r.Redefinida)
            {
                SenhaProvisoria = string.Empty;
            }
        }).ConfigureAwait(true);

        await AtualizarAsync().ConfigureAwait(true);
    }

    private async Task GravarPapelAsync()
    {
        var pedido = new GravarPapelRequest { Id = PapelId, Nome = PapelNome.Trim(), Descricao = PapelDescricao.Trim() };
        pedido.Permissoes.AddRange(PermissoesDoPapel.Where(o => o.Marcado).Select(o => o.Codigo));

        await Tentar(async () =>
        {
            var r = await Cliente.GravarPapelAsync(pedido);
            if (!r.Gravado)
            {
                Mensagem = string.Join(" ", r.Problemas);
                return;
            }

            Mensagem = $"Papel {pedido.Nome} gravado. Vale na próxima ação de quem tem este papel.";
            PapelId = r.Id;
        }).ConfigureAwait(true);

        await AtualizarAsync().ConfigureAwait(true);
    }
}
