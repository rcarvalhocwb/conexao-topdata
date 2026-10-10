using Contracts;
using Contracts.Edge.V1;
using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>
/// Os códigos de permissão que o painel usa para mostrar ou esconder telas e botões. São os mesmos de
/// <c>Access.Domain.Usuarios.Permissoes</c> (um teste confere); o serviço é quem decide de verdade.
/// </summary>
public static class CodigosDePermissao
{
    public const string OperacaoVer = "operacao.ver";
    public const string CodigosConsultar = "codigos.consultar";
    public const string RelatoriosVer = "relatorios.ver";
    public const string DiagnosticoVer = "diagnostico.ver";
    public const string CatracaComandar = "catraca.comandar";
    public const string CatracaConfigurar = "catraca.configurar";
    public const string ConfiguracaoEditar = "configuracao.editar";
    public const string SincronizacaoOperar = "sincronizacao.operar";
    public const string AcessosEstornar = "acessos.estornar";
    public const string SimuladorUsar = "simulador.usar";
    public const string UsuariosGerenciar = "usuarios.gerenciar";
    public const string PessoasVer = "pessoas.ver";
    public const string PessoasVerDados = "pessoas.ver_dados";
    public const string PessoasEditar = "pessoas.editar";
    public const string PessoasBloquear = "pessoas.bloquear";
    public const string PessoasImportar = "pessoas.importar";
    public const string CadastroParametros = "cadastro.parametros";
    public const string CatracaFechar = "catraca.fechar";
    public const string VisitasVer = "visitas.ver";
    public const string VisitasAgendar = "visitas.agendar";
    public const string VisitasReceber = "visitas.receber";
    public const string VisitasEncerrar = "visitas.encerrar";
}

/// <summary>Em que ponto está o login do painel.</summary>
public enum EtapaDoLogin
{
    /// <summary>Perguntando ao serviço se há sessão.</summary>
    Verificando,

    /// <summary>Pedindo usuário e senha.</summary>
    Entrar,

    /// <summary>Primeiro acesso ou senha redefinida: só a troca.</summary>
    TrocarSenha,

    /// <summary>Dentro do sistema.</summary>
    Logado,
}

/// <summary>
/// O login do painel (ADR-0026): entrar, trocar a senha no primeiro acesso e sair. As permissões do
/// usuário decidem o que o menu mostra.
/// </summary>
public sealed class SessaoDoUsuarioViewModel : Notificavel
{
    /// <summary>O login do administrador que vem instalado.</summary>
    public const string LoginPadrao = "admin";

    private readonly EdgeControl.EdgeControlClient _cliente;
    private readonly SessaoDoPainel _sessao;
    private EtapaDoLogin _etapa = EtapaDoLogin.Verificando;
    private string _login = string.Empty;
    private string _senha = string.Empty;
    private string _senhaNova = string.Empty;
    private string _confirmacao = string.Empty;
    private string _novoLogin = string.Empty;
    private string _novoNome = string.Empty;
    private string _mensagem = string.Empty;
    private UsuarioDaSessao _usuario = new();
    private HashSet<string> _permissoes = new(StringComparer.Ordinal);

    public SessaoDoUsuarioViewModel(EdgeControl.EdgeControlClient cliente, SessaoDoPainel sessao)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(sessao);
        _cliente = cliente;
        _sessao = sessao;
        Entrar = new ComandoAssincrono(EntrarAsync, () => Login.Trim().Length > 0 && Senha.Length > 0);
        TrocarSenha = new ComandoAssincrono(TrocarSenhaAsync, () => SenhaNova.Length > 0 && Confirmacao.Length > 0);
        Sair = new ComandoAssincrono(SairAsync, () => Logado && LoginLigado);
    }

    /// <summary>Muda quando o usuário entra, sai ou as permissões mudam.</summary>
    public event EventHandler? SessaoMudou;

    public ComandoAssincrono Entrar { get; }

    public ComandoAssincrono TrocarSenha { get; }

    public ComandoAssincrono Sair { get; }

    public EtapaDoLogin Etapa
    {
        get => _etapa;
        private set
        {
            if (Definir(ref _etapa, value))
            {
                Avisar(nameof(Logado));
                Avisar(nameof(ForaDoSistema));
                Avisar(nameof(PedindoLogin));
                Avisar(nameof(PedindoTroca));
                Sair.ReavaliarDisponibilidade();
                SessaoMudou?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool Logado => Etapa == EtapaDoLogin.Logado;

    /// <summary>A tela de login cobre o painel.</summary>
    public bool ForaDoSistema => !Logado;

    public bool PedindoLogin => Etapa is EtapaDoLogin.Entrar or EtapaDoLogin.Verificando;

    public bool PedindoTroca => Etapa == EtapaDoLogin.TrocarSenha;

    /// <summary>O login está ligado no serviço (falso só em ferramentas sem base de usuários).</summary>
    public bool LoginLigado => _usuario.LoginLigado;

    public string Login { get => _login; set { if (Definir(ref _login, value ?? string.Empty)) { Entrar.ReavaliarDisponibilidade(); } } }

    /// <summary>A senha digitada; a janela a passa daqui, sem ligação (a caixa de senha não liga).</summary>
    public string Senha { get => _senha; set { if (Definir(ref _senha, value ?? string.Empty)) { Entrar.ReavaliarDisponibilidade(); } } }

    public string SenhaNova { get => _senhaNova; set { if (Definir(ref _senhaNova, value ?? string.Empty)) { TrocarSenha.ReavaliarDisponibilidade(); } } }

    public string Confirmacao { get => _confirmacao; set { if (Definir(ref _confirmacao, value ?? string.Empty)) { TrocarSenha.ReavaliarDisponibilidade(); } } }

    /// <summary>No primeiro acesso do administrador padrão: o login do administrador real.</summary>
    public string NovoLogin { get => _novoLogin; set => Definir(ref _novoLogin, value ?? string.Empty); }

    /// <summary>No primeiro acesso do administrador padrão: o nome do administrador real.</summary>
    public string NovoNome { get => _novoNome; set => Definir(ref _novoNome, value ?? string.Empty); }

    public string Mensagem { get => _mensagem; private set => Definir(ref _mensagem, value); }

    /// <summary>Quem está logado.</summary>
    public string Nome => _usuario.Nome;

    /// <summary>O primeiro acesso do administrador que veio instalado: pede o login e o nome reais.</summary>
    public bool PrimeiroAcessoDoAdministrador =>
        PedindoTroca && string.Equals(_usuario.Login, LoginPadrao, StringComparison.OrdinalIgnoreCase);

    public IReadOnlySet<string> Permissoes => _permissoes;

    /// <summary>Verdadeiro se o usuário logado tem a permissão.</summary>
    public bool Pode(string permissao) => _permissoes.Contains(permissao);

    /// <summary>Pergunta ao serviço se já há sessão (ou se o login está desligado).</summary>
    public async Task IniciarAsync()
    {
        try
        {
            Aplicar(await _cliente.ObterSessaoAsync(new ObterSessaoRequest()));
        }
        catch (RpcException erro)
        {
            Etapa = EtapaDoLogin.Entrar;
            if (erro.StatusCode is not StatusCode.Unauthenticated)
            {
                Mensagem = MensagemDeFalha.Para(erro);
            }
        }
    }

    /// <summary>
    /// Confere a sessão a cada atualização: o serviço reiniciado (sessões só na memória), o usuário
    /// desativado ou o papel mudado chegam aqui.
    /// </summary>
    public async Task ConferirAsync(CancellationToken cancelamento = default)
    {
        if (!Logado || !LoginLigado)
        {
            return;
        }

        try
        {
            Aplicar(await _cliente.ObterSessaoAsync(new ObterSessaoRequest(), cancellationToken: cancelamento));
        }
        catch (RpcException erro) when (erro.StatusCode is StatusCode.Unauthenticated)
        {
            Encerrar("Sessão encerrada. Entre de novo com usuário e senha.");
        }
        catch (RpcException)
        {
            // Serviço fora: o painel já mostra. A sessão continua quando ele voltar.
        }
    }

    private async Task EntrarAsync()
    {
        try
        {
            var resposta = await _cliente.EntrarAsync(new EntrarRequest { Login = Login.Trim(), Senha = Senha });
            Senha = string.Empty;
            if (!resposta.Aceito)
            {
                Mensagem = resposta.Mensagem;
                return;
            }

            _sessao.Token = resposta.Sessao;
            Mensagem = string.Empty;
            Aplicar(resposta.Usuario);
        }
        catch (RpcException erro)
        {
            Senha = string.Empty;
            Mensagem = MensagemDeFalha.Para(erro);
        }
    }

    private async Task TrocarSenhaAsync()
    {
        if (!string.Equals(SenhaNova, Confirmacao, StringComparison.Ordinal))
        {
            Mensagem = "A confirmação não é igual à senha nova.";
            return;
        }

        try
        {
            var resposta = await _cliente.TrocarSenhaAsync(new TrocarSenhaRequest
            {
                SenhaAtual = Senha,
                SenhaNova = SenhaNova,
                NovoLogin = PrimeiroAcessoDoAdministrador ? NovoLogin.Trim() : string.Empty,
                NovoNome = PrimeiroAcessoDoAdministrador ? NovoNome.Trim() : string.Empty,
            });

            if (!resposta.Trocada)
            {
                Mensagem = string.Join(" ", resposta.Problemas);
                return;
            }

            Senha = SenhaNova = Confirmacao = string.Empty;
            Mensagem = string.Empty;
            Aplicar(await _cliente.ObterSessaoAsync(new ObterSessaoRequest()));
        }
        catch (RpcException erro)
        {
            Mensagem = MensagemDeFalha.Para(erro);
        }
    }

    private async Task SairAsync()
    {
        try
        {
            await _cliente.SairAsync(new SairRequest());
        }
        catch (RpcException)
        {
            // Sem serviço, a sessão some com ele; o painel sai do mesmo jeito.
        }

        Encerrar(string.Empty);
    }

    private void Encerrar(string mensagem)
    {
        _sessao.Token = null;
        _usuario = new UsuarioDaSessao();
        _permissoes = new HashSet<string>(StringComparer.Ordinal);
        Login = string.Empty;
        Senha = string.Empty;
        Mensagem = mensagem;
        Etapa = EtapaDoLogin.Entrar;
        Avisar(nameof(Nome));
        Avisar(nameof(Permissoes));
        Avisar(nameof(LoginLigado));
        SessaoMudou?.Invoke(this, EventArgs.Empty);
    }

    private void Aplicar(UsuarioDaSessao usuario)
    {
        var mudouPermissao = !_permissoes.SetEquals(usuario.Permissoes);
        _usuario = usuario;
        _permissoes = new HashSet<string>(usuario.Permissoes, StringComparer.Ordinal);
        Avisar(nameof(Nome));
        Avisar(nameof(LoginLigado));
        Avisar(nameof(Permissoes));
        Etapa = usuario.TrocarSenha ? EtapaDoLogin.TrocarSenha : EtapaDoLogin.Logado;
        Avisar(nameof(PrimeiroAcessoDoAdministrador));
        if (mudouPermissao)
        {
            SessaoMudou?.Invoke(this, EventArgs.Empty);
        }
    }
}
