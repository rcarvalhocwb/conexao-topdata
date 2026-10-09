using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Edge.Supervisor;

/// <summary>Quem está chamando, depois que o <see cref="InterceptadorDeSessao"/> conferiu.</summary>
/// <param name="Usuario">O usuário da sessão.</param>
/// <param name="Token">O token da sessão (para Sair).</param>
/// <param name="Permissoes">As permissões efetivas.</param>
public sealed record ChamadorDoPainel(UsuarioDoSistema Usuario, string Token, IReadOnlySet<string> Permissoes);

/// <summary>
/// Confere a sessão e a permissão de cada RPC (ADR-0026). Roda depois do <see cref="InterceptadorDeToken"/>.
/// </summary>
/// <remarks>
/// <para>
/// Cada RPC do contrato está em <see cref="Exigida"/>. RPC fora do mapa é recusada (fail-secure): uma RPC
/// nova sem permissão definida não fica aberta por engano, e um teste reprova o mapa incompleto.
/// </para>
/// <para>
/// Com a senha padrão ou redefinida, só <c>TrocarSenha</c>, <c>ObterSessao</c> e <c>Sair</c> passam.
/// </para>
/// </remarks>
public sealed class InterceptadorDeSessao(UsuariosDoSistema usuarios, SessoesDoPainel sessoes, Func<DateTimeOffset>? relogio = null) : Interceptor
{
    /// <summary>Chave em <c>ServerCallContext.UserState</c> com o <see cref="ChamadorDoPainel"/>.</summary>
    public const string ChaveDoChamador = "chamador";

    /// <summary>Permissão exigida por RPC. Valor nulo: basta estar logado.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Exigida = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        // Sem sessão (ver Anonimas) ou só logado.
        ["Entrar"] = null,
        ["ObterEstado"] = null,
        ["TrocarSenha"] = null,
        ["Sair"] = null,
        ["ObterSessao"] = null,

        // Operação.
        ["ListarEquipamentos"] = null,
        ["AcompanharEventos"] = Permissoes.OperacaoVer,
        ["ListarAcessos"] = Permissoes.OperacaoVer,
        ["ObterConfiguracao"] = Permissoes.OperacaoVer,
        ["ObterSincronizacao"] = Permissoes.OperacaoVer,
        ["ListarUsosSemPassagem"] = Permissoes.OperacaoVer,
        ["ListarComandos"] = Permissoes.OperacaoVer,
        ["ObterConfiguracaoDaCatraca"] = Permissoes.OperacaoVer,
        ["ObterMapaDeGiro"] = Permissoes.OperacaoVer,
        ["ExplicarNegativa"] = Permissoes.OperacaoVer,
        ["ObterSugestoes"] = Permissoes.OperacaoVer,
        ["RegistrarDestinoDaSugestao"] = Permissoes.CatracaConfigurar,
        ["ObterSaudeDasCatracas"] = Permissoes.OperacaoVer,
        ["ObterRitmo"] = Permissoes.OperacaoVer,
        ["ListarAlertas"] = Permissoes.OperacaoVer,
        ["MarcarAlertaComoCiente"] = Permissoes.OperacaoVer,
        ["ObterRelatorioPosEvento"] = Permissoes.RelatoriosVer,
        ["ConsultarCodigo"] = Permissoes.CodigosConsultar,
        ["ObterPrestacaoDeContas"] = Permissoes.RelatoriosVer,
        ["ObterDiagnostico"] = Permissoes.DiagnosticoVer,
        ["ObterPacoteDeDiagnostico"] = Permissoes.DiagnosticoVer,

        // Ações.
        ["EnviarComando"] = Permissoes.CatracaComandar,
        ["GravarConfiguracaoDaCatraca"] = Permissoes.CatracaConfigurar,
        ["GravarMapaDeGiro"] = Permissoes.CatracaConfigurar,
        ["RegistrarConferenciaDoGiro"] = Permissoes.CatracaConfigurar,
        ["GravarConfiguracao"] = Permissoes.ConfiguracaoEditar,
        ["ReenviarCartasMortas"] = Permissoes.SincronizacaoOperar,
        ["EstornarUso"] = Permissoes.AcessosEstornar,
        ["SimularLeitura"] = Permissoes.SimuladorUsar,

        // Cadastro local de pessoas (docs/43). Os parâmetros não têm dado pessoal: basta estar logado.
        ["BuscarPessoas"] = Permissoes.PessoasVer,
        ["ObterPessoa"] = Permissoes.PessoasVer,
        ["GravarPessoa"] = Permissoes.PessoasEditar,
        ["AdicionarCredencial"] = Permissoes.PessoasEditar,
        ["MudarSituacaoDaPessoa"] = Permissoes.PessoasBloquear,
        ["MudarSituacaoDaCredencial"] = Permissoes.PessoasBloquear,
        ["ObterParametrosDoCadastro"] = null,
        ["GravarEmpresa"] = Permissoes.CadastroParametros,
        ["GravarSala"] = Permissoes.CadastroParametros,
        ["GravarHorario"] = Permissoes.CadastroParametros,
        ["ExcluirHorario"] = Permissoes.CadastroParametros,
        ["GravarFeriado"] = Permissoes.CadastroParametros,
        ["ExcluirFeriado"] = Permissoes.CadastroParametros,
        ["GravarPerfil"] = Permissoes.CadastroParametros,
        ["PreverImportacaoDePessoas"] = Permissoes.PessoasImportar,
        ["ImportarPessoas"] = Permissoes.PessoasImportar,
        ["ListarImportacoesDePessoas"] = Permissoes.PessoasImportar,
        ["DesfazerImportacaoDePessoas"] = Permissoes.PessoasImportar,
        ["AbrirSessaoDeCadastro"] = Permissoes.CartoesCadastrar,
        ["FecharSessaoDeCadastro"] = Permissoes.CartoesCadastrar,
        ["ObterSessaoDeCadastro"] = Permissoes.CartoesCadastrar,
        ["ListarLeiturasDesconhecidas"] = Permissoes.CartoesCadastrar,
        ["CadastrarLeituraDesconhecida"] = Permissoes.CartoesCadastrar,
        ["ListarCatracasFechadas"] = Permissoes.OperacaoVer,
        ["FecharCatraca"] = Permissoes.CatracaFechar,
        ["AbrirCatraca"] = Permissoes.CatracaFechar,

        // Administração.
        ["ListarUsuarios"] = Permissoes.UsuariosGerenciar,
        ["GravarUsuario"] = Permissoes.UsuariosGerenciar,
        ["RedefinirSenha"] = Permissoes.UsuariosGerenciar,
        ["ListarPapeis"] = Permissoes.UsuariosGerenciar,
        ["GravarPapel"] = Permissoes.UsuariosGerenciar,
    };

    /// <summary>
    /// RPCs que atendem sem sessão: entrar, e o estado geral e a situação das catracas que o ícone da
    /// bandeja mostra antes de alguém entrar (contagens, nomes e situação das catracas; nenhum dado
    /// pessoal nem código de acesso).
    /// </summary>
    public static readonly IReadOnlySet<string> Anonimas = new HashSet<string>(StringComparer.Ordinal) { "Entrar", "ObterEstado", "ListarEquipamentos" };

    /// <summary>O que passa com a senha padrão ou redefinida.</summary>
    public static readonly IReadOnlySet<string> DuranteATroca = new HashSet<string>(StringComparer.Ordinal) { "TrocarSenha", "ObterSessao", "Sair" };

    private readonly UsuariosDoSistema _usuarios = usuarios ?? throw new ArgumentNullException(nameof(usuarios));
    private readonly SessoesDoPainel _sessoes = sessoes ?? throw new ArgumentNullException(nameof(sessoes));
    private readonly Func<DateTimeOffset> _relogio = relogio ?? (() => DateTimeOffset.UtcNow);

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        Conferir(context);
        return continuation(request, context);
    }

    public override Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        Conferir(context);
        return continuation(request, responseStream, context);
    }

    /// <summary>O nome curto da RPC a partir do caminho gRPC (<c>/pacote.Servico/Metodo</c>).</summary>
    public static string NomeDaRpc(string metodo)
    {
        ArgumentNullException.ThrowIfNull(metodo);
        var barra = metodo.LastIndexOf('/');
        return barra >= 0 ? metodo[(barra + 1)..] : metodo;
    }

    private void Conferir(ServerCallContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var rpc = NomeDaRpc(contexto.Method);

        if (!Exigida.TryGetValue(rpc, out var exigida))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Função sem permissão definida: {rpc}."));
        }

        var token = contexto.RequestHeaders.GetValue(TransporteLocal.CabecalhoDaSessao);
        var usuarioId = _sessoes.Usuario(token, _relogio());
        var usuario = usuarioId is null ? null : _usuarios.Obter(usuarioId);

        if (usuario is { Ativo: false })
        {
            _sessoes.Encerrar(token);
            usuario = null;
        }

        if (usuario is null)
        {
            if (Anonimas.Contains(rpc))
            {
                return;
            }

            throw new RpcException(new Status(StatusCode.Unauthenticated, "Entre com usuário e senha."));
        }

        var permissoes = _usuarios.PermissoesDe(usuario.Id);
        contexto.UserState[ChaveDoChamador] = new ChamadorDoPainel(usuario, token!, permissoes);

        if (usuario.TrocarSenha && !DuranteATroca.Contains(rpc) && !Anonimas.Contains(rpc))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Troque a senha antes de continuar."));
        }

        if (exigida is not null && !permissoes.Contains(exigida))
        {
            var nome = Permissoes.Todas.FirstOrDefault(p => p.Codigo == exigida)?.Nome ?? exigida;
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Seu papel não permite: {nome}."));
        }
    }
}
