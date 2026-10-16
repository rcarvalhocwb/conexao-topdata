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
    public const string CartoesCadastrar = "cartoes.cadastrar";
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
    private bool _mostrandoAjudaDaSenha;
    private string _pedidoDeRecuperacao = string.Empty;
    private string _validadeDoPedido = string.Empty;
    private string _respostaDoSuporte = string.Empty;
    private string _senhaRecuperada = string.Empty;
    private string _confirmacaoRecuperada = string.Empty;
    private string _mensagemDaRecuperacao = string.Empty;

    public SessaoDoUsuarioViewModel(EdgeControl.EdgeControlClient cliente, SessaoDoPainel sessao)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(sessao);
        _cliente = cliente;
        _sessao = sessao;
        Entrar = new ComandoAssincrono(EntrarAsync, () => Login.Trim().Length > 0 && Senha.Length > 0);
        TrocarSenha = new ComandoAssincrono(TrocarSenhaAsync, () => SenhaNova.Length > 0 && Confirmacao.Length > 0);
        Sair = new ComandoAssincrono(SairAsync, () => Logado && LoginLigado);
        AlternarAjudaDaSenha = new ComandoAssincrono(() =>
        {
            MostrandoAjudaDaSenha = !MostrandoAjudaDaSenha;
            return Task.CompletedTask;
        });
        PedirRecuperacao = new ComandoAssincrono(PedirRecuperacaoAsync, () => Login.Trim().Length > 0);
        ConfirmarRecuperacao = new ComandoAssincrono(
            ConfirmarRecuperacaoAsync,
            () => PedidoDeRecuperacao.Length > 0 && RespostaDoSuporte.Trim().Length > 0 && SenhaRecuperada.Length > 0);
    }

    /// <summary>Abre e fecha a ajuda "Esqueci minha senha" na tela de login.</summary>
    public ComandoAssincrono AlternarAjudaDaSenha { get; }

    /// <summary>Gera o código de pedido para o suporte Rayzer (docs/58).</summary>
    public ComandoAssincrono PedirRecuperacao { get; }

    /// <summary>Confere a resposta do suporte e grava a senha nova escolhida aqui.</summary>
    public ComandoAssincrono ConfirmarRecuperacao { get; }

    public bool MostrandoAjudaDaSenha { get => _mostrandoAjudaDaSenha; private set => Definir(ref _mostrandoAjudaDaSenha, value); }

    /// <summary>O código que a pessoa manda ao suporte; vazio até ser gerado.</summary>
    public string PedidoDeRecuperacao
    {
        get => _pedidoDeRecuperacao;
        private set
        {
            if (Definir(ref _pedidoDeRecuperacao, value))
            {
                ConfirmarRecuperacao.ReavaliarDisponibilidade();
            }
        }
    }

    /// <summary>"Vale até HH:mm".</summary>
    public string ValidadeDoPedido { get => _validadeDoPedido; private set => Definir(ref _validadeDoPedido, value); }

    public string RespostaDoSuporte
    {
        get => _respostaDoSuporte;
        set
        {
            if (Definir(ref _respostaDoSuporte, value ?? string.Empty))
            {
                ConfirmarRecuperacao.ReavaliarDisponibilidade();
            }
        }
    }

    /// <summary>A senha nova da recuperação; a janela a passa daqui (a caixa de senha não liga).</summary>
    public string SenhaRecuperada
    {
        get => _senhaRecuperada;
        set
        {
            if (Definir(ref _senhaRecuperada, value ?? string.Empty))
            {
                ConfirmarRecuperacao.ReavaliarDisponibilidade();
            }
        }
    }

    public string ConfirmacaoRecuperada { get => _confirmacaoRecuperada; set => Definir(ref _confirmacaoRecuperada, value ?? string.Empty); }

    public string MensagemDaRecuperacao { get => _mensagemDaRecuperacao; private set => Definir(ref _mensagemDaRecuperacao, value); }

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

    public string Login
    {
        get => _login;
        set
        {
            if (Definir(ref _login, value ?? string.Empty))
            {
                Entrar.ReavaliarDisponibilidade();
                PedirRecuperacao.ReavaliarDisponibilidade();

                // O pedido é deste login: trocar o login desfaz o pedido na tela.
                PedidoDeRecuperacao = string.Empty;
                ValidadeDoPedido = string.Empty;
            }
        }
    }

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
            if (!resposta.Aceito)
            {
                // A caixa de senha continua com o texto digitado: limpar só o ViewModel desabilitaria Entrar.
                Mensagem = resposta.Mensagem;
                return;
            }

            Senha = string.Empty;
            _sessao.Token = resposta.Sessao;
            Mensagem = string.Empty;
            Aplicar(resposta.Usuario);
        }
        catch (RpcException erro)
        {
            Mensagem = MensagemDeFalha.Para(erro);
        }
    }

    private async Task PedirRecuperacaoAsync()
    {
        try
        {
            var pedido = await _cliente.PedirRecuperacaoPeloSuporteAsync(new PedirRecuperacaoPeloSuporteRequest { Login = Login.Trim() });
            if (!pedido.Disponivel)
            {
                PedidoDeRecuperacao = string.Empty;
                MensagemDaRecuperacao = pedido.Motivo;
                return;
            }

            PedidoDeRecuperacao = pedido.Pedido;
            ValidadeDoPedido = $"Vale até {FusoDoEvento.NoEvento(pedido.ValidoAte.ToDateTimeOffset()):HH:mm} (horário de Brasília).";
            MensagemDaRecuperacao = "Mande este código ao suporte Rayzer e cole aqui a resposta que ele devolver.";
        }
        catch (RpcException erro)
        {
            MensagemDaRecuperacao = MensagemDeFalha.Para(erro);
        }
    }

    private async Task ConfirmarRecuperacaoAsync()
    {
        if (!string.Equals(SenhaRecuperada, ConfirmacaoRecuperada, StringComparison.Ordinal))
        {
            MensagemDaRecuperacao = "A confirmação não é igual à senha nova.";
            return;
        }

        try
        {
            var resultado = await _cliente.ConfirmarRecuperacaoPeloSuporteAsync(new ConfirmarRecuperacaoPeloSuporteRequest
            {
                Login = Login.Trim(),
                Resposta = RespostaDoSuporte.Trim(),
                SenhaNova = SenhaRecuperada,
            });

            if (!resultado.Gravada)
            {
                MensagemDaRecuperacao = string.Join(" ", resultado.Problemas);
                return;
            }

            SenhaRecuperada = ConfirmacaoRecuperada = RespostaDoSuporte = string.Empty;
            PedidoDeRecuperacao = string.Empty;
            ValidadeDoPedido = string.Empty;
            MensagemDaRecuperacao = string.Empty;
            MostrandoAjudaDaSenha = false;
            Mensagem = "Senha gravada pela recuperação do suporte. Entre com a senha nova.";
        }
        catch (RpcException erro)
        {
            MensagemDaRecuperacao = MensagemDeFalha.Para(erro);
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
