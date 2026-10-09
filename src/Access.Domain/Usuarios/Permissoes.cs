namespace Access.Domain.Usuarios;

/// <summary>Uma função do sistema que um papel pode ou não ter.</summary>
/// <param name="Codigo">Identificador estável, gravado na base (<c>app_role_permission</c>).</param>
/// <param name="Grupo">Agrupamento na tela de papéis.</param>
/// <param name="Nome">O que o administrador lê na tela.</param>
public sealed record Permissao(string Codigo, string Grupo, string Nome);

/// <summary>
/// O catálogo das permissões (ADR-0026). Cada RPC do serviço exige uma delas; o administrador
/// escolhe quais cada papel tem.
/// </summary>
/// <remarks>
/// Os códigos são gravados na base: renomear um código exige migração. Acrescentar é livre.
/// </remarks>
public static class Permissoes
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
    public const string CadastroParametros = "cadastro.parametros";
    public const string CatracaFechar = "catraca.fechar";

    /// <summary>Todas, na ordem da tela.</summary>
    public static IReadOnlyList<Permissao> Todas { get; } =
    [
        new(OperacaoVer, "Operação", "Ver o painel, as catracas, os acessos e a sincronização"),
        new(CodigosConsultar, "Operação", "Consultar um código"),
        new(RelatoriosVer, "Operação", "Ver e exportar a prestação de contas"),
        new(DiagnosticoVer, "Operação", "Ver o diagnóstico e gerar o pacote de diagnóstico"),
        new(CatracaComandar, "Catracas", "Liberar um giro, mensagem no display, acertar relógio, refazer conexão"),
        new(CatracaConfigurar, "Catracas", "Parametrizar a catraca e o mapa de giro"),
        new(ConfiguracaoEditar, "Configuração", "Mudar a configuração do evento"),
        new(SincronizacaoOperar, "Configuração", "Reenviar à nuvem o que ela recusou"),
        new(AcessosEstornar, "Acessos", "Estornar um uso sem passagem"),
        new(SimuladorUsar, "Acessos", "Usar o simulador de leituras"),
        new(CatracaFechar, "Catracas", "Fechar e reabrir uma catraca (ninguém passa enquanto fechada)"),
        new(PessoasVer, "Pessoas", "Ver e buscar pessoas e credenciais (documento e contato mascarados)"),
        new(PessoasVerDados, "Pessoas", "Ver documento, contato e nascimento completos"),
        new(PessoasEditar, "Pessoas", "Cadastrar e alterar pessoas e dar credenciais"),
        new(PessoasBloquear, "Pessoas", "Bloquear, desbloquear e inativar pessoas e credenciais"),
        new(CadastroParametros, "Pessoas", "Empresas, salas, horários, feriados e perfis"),
        new(UsuariosGerenciar, "Administração", "Criar usuários, papéis e permissões"),
    ];

    /// <summary>Verdadeiro se o código está no catálogo.</summary>
    public static bool Existe(string codigo) => Todas.Any(p => p.Codigo == codigo);
}
