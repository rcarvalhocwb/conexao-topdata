using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>Login, sessão, usuários e papéis (ADR-0026).</summary>
public sealed partial class EdgeControlService
{
    private readonly UsuariosDoSistema? _usuarios;
    private readonly SessoesDoPainel _sessoes;

    public override Task<EntrarResponse> Entrar(EntrarRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new EntrarResponse();

        if (_usuarios is null)
        {
            resposta.Mensagem = "O login não está ligado neste serviço.";
            return Task.FromResult(resposta);
        }

        var (resultado, usuario) = _usuarios.Entrar(request.Login, request.Senha, _relogio());
        switch (resultado)
        {
            case ResultadoDaEntrada.Entrou:
                resposta.Aceito = true;
                resposta.Sessao = _sessoes.Abrir(usuario!.Id, _relogio());
                resposta.Usuario = DaSessao(usuario, _usuarios.PermissoesDe(usuario.Id));
                break;
            case ResultadoDaEntrada.Bloqueado:
                resposta.Mensagem =
                    $"Muitas tentativas erradas. Espere {UsuariosDoSistema.DuracaoDoBloqueio.TotalMinutes:0} minutos e tente de novo.";
                break;
            case ResultadoDaEntrada.Inativo:
                resposta.Mensagem = "Este usuário está desativado. Fale com o administrador.";
                break;
            default:
                resposta.Mensagem = "Usuário ou senha não conferem.";
                break;
        }

        return Task.FromResult(resposta);
    }

    public override Task<UsuarioDaSessao> ObterSessao(ObterSessaoRequest request, ServerCallContext context)
    {
        if (_usuarios is null)
        {
            // Sem base de usuários (ferramentas e testes): tudo liberado, como antes do login.
            var livre = new UsuarioDaSessao { LoginLigado = false, Nome = "Operador" };
            livre.Permissoes.AddRange(Permissoes.Todas.Select(p => p.Codigo));
            return Task.FromResult(livre);
        }

        var chamador = Chamador(context)
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Entre com usuário e senha."));
        return Task.FromResult(DaSessao(chamador.Usuario, chamador.Permissoes));
    }

    public override Task<TrocarSenhaResponse> TrocarSenha(TrocarSenhaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new TrocarSenhaResponse();
        if (_usuarios is null || Chamador(context) is not { } chamador)
        {
            resposta.Problemas.Add("Entre com usuário e senha.");
            return Task.FromResult(resposta);
        }

        var problemas = _usuarios.TrocarSenha(
            chamador.Usuario.Id, request.SenhaAtual, request.SenhaNova,
            string.IsNullOrWhiteSpace(request.NovoLogin) ? null : request.NovoLogin,
            string.IsNullOrWhiteSpace(request.NovoNome) ? null : request.NovoNome,
            _relogio());
        resposta.Trocada = problemas.Count == 0;
        resposta.Problemas.AddRange(problemas);
        return Task.FromResult(resposta);
    }

    public override Task<SairResponse> Sair(SairRequest request, ServerCallContext context)
    {
        if (Chamador(context) is { } chamador)
        {
            _sessoes.Encerrar(chamador.Token);
        }

        return Task.FromResult(new SairResponse());
    }

    public override Task<ListarUsuariosResponse> ListarUsuarios(ListarUsuariosRequest request, ServerCallContext context)
    {
        var resposta = new ListarUsuariosResponse();
        foreach (var u in _usuarios?.Listar() ?? [])
        {
            var item = new Contracts.Edge.V1.Usuario
            {
                Id = u.Id,
                Login = u.Login,
                Nome = u.Nome,
                Ativo = u.Ativo,
                TrocarSenha = u.TrocarSenha,
            };
            item.Papeis.AddRange(u.Papeis);
            if (u.UltimoAcesso is { } ultimo)
            {
                item.UltimoAcesso = Timestamp.FromDateTimeOffset(ultimo);
            }

            resposta.Usuarios.Add(item);
        }

        return Task.FromResult(resposta);
    }

    public override Task<GravarUsuarioResponse> GravarUsuario(GravarUsuarioRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new GravarUsuarioResponse();
        if (_usuarios is null || Chamador(context) is not { } chamador)
        {
            resposta.Problemas.Add("O login não está ligado neste serviço.");
            return Task.FromResult(resposta);
        }

        var (id, problemas) = _usuarios.Gravar(
            chamador.Usuario.Id,
            string.IsNullOrWhiteSpace(request.Id) ? null : request.Id,
            request.Login, request.Nome, request.Ativo, [.. request.Papeis],
            string.IsNullOrEmpty(request.SenhaInicial) ? null : request.SenhaInicial,
            _relogio());

        resposta.Gravado = problemas.Count == 0;
        resposta.Id = id ?? string.Empty;
        resposta.Problemas.AddRange(problemas);

        // Desativado: as sessões abertas dele acabam agora.
        if (resposta.Gravado && !request.Ativo)
        {
            _sessoes.EncerrarDoUsuario(id!);
        }

        return Task.FromResult(resposta);
    }

    public override Task<RedefinirSenhaResponse> RedefinirSenha(RedefinirSenhaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new RedefinirSenhaResponse();
        if (_usuarios is null || Chamador(context) is not { } chamador)
        {
            resposta.Problemas.Add("O login não está ligado neste serviço.");
            return Task.FromResult(resposta);
        }

        var problemas = _usuarios.RedefinirSenha(chamador.Usuario.Id, request.UsuarioId, request.SenhaProvisoria, _relogio());
        resposta.Redefinida = problemas.Count == 0;
        resposta.Problemas.AddRange(problemas);
        if (resposta.Redefinida)
        {
            _sessoes.EncerrarDoUsuario(request.UsuarioId);
        }

        return Task.FromResult(resposta);
    }

    public override Task<ListarPapeisResponse> ListarPapeis(ListarPapeisRequest request, ServerCallContext context)
    {
        var resposta = new ListarPapeisResponse();
        resposta.Catalogo.AddRange(Permissoes.Todas.Select(p => new PermissaoDoCatalogo { Codigo = p.Codigo, Grupo = p.Grupo, Nome = p.Nome }));
        foreach (var p in _usuarios?.ListarPapeis() ?? [])
        {
            var papel = new Papel { Id = p.Id, Nome = p.Nome, Descricao = p.Descricao ?? string.Empty, DoSistema = p.DoSistema };
            papel.Permissoes.AddRange(p.Permissoes);
            resposta.Papeis.Add(papel);
        }

        return Task.FromResult(resposta);
    }

    public override Task<GravarPapelResponse> GravarPapel(GravarPapelRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new GravarPapelResponse();
        if (_usuarios is null || Chamador(context) is not { } chamador)
        {
            resposta.Problemas.Add("O login não está ligado neste serviço.");
            return Task.FromResult(resposta);
        }

        var (id, problemas) = _usuarios.GravarPapel(
            chamador.Usuario.Id,
            string.IsNullOrWhiteSpace(request.Id) ? null : request.Id,
            request.Nome, request.Descricao, [.. request.Permissoes], _relogio());
        resposta.Gravado = problemas.Count == 0;
        resposta.Id = id ?? string.Empty;
        resposta.Problemas.AddRange(problemas);
        return Task.FromResult(resposta);
    }

    /// <summary>Quem chamou, quando o login está ligado e a sessão foi conferida.</summary>
    internal static ChamadorDoPainel? Chamador(ServerCallContext? contexto) =>
        contexto?.UserState.TryGetValue(InterceptadorDeSessao.ChaveDoChamador, out var valor) == true ? valor as ChamadorDoPainel : null;

    private static UsuarioDaSessao DaSessao(UsuarioDoSistema usuario, IReadOnlySet<string> permissoes)
    {
        var sessao = new UsuarioDaSessao
        {
            Id = usuario.Id,
            Login = usuario.Login,
            Nome = usuario.Nome,
            TrocarSenha = usuario.TrocarSenha,
            LoginLigado = true,
        };
        sessao.Permissoes.AddRange(permissoes.Order(StringComparer.Ordinal));
        return sessao;
    }
}
