using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Access.Domain.Usuarios;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Um usuário do sistema, sem nada da senha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Login">Nome de entrada.</param>
/// <param name="Nome">Nome exibido (fica na autoria das ações).</param>
/// <param name="Ativo">Pode entrar.</param>
/// <param name="TrocarSenha">Precisa trocar a senha antes de qualquer outra função.</param>
/// <param name="Papeis">Ids dos papéis.</param>
/// <param name="UltimoAcesso">Última entrada com sucesso.</param>
public sealed record UsuarioDoSistema(
    string Id,
    string Login,
    string Nome,
    bool Ativo,
    bool TrocarSenha,
    IReadOnlyList<string> Papeis,
    DateTimeOffset? UltimoAcesso);

/// <summary>Um papel e as permissões dele.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome exibido.</param>
/// <param name="Descricao">Para que serve.</param>
/// <param name="DoSistema">Papel que vem instalado (não se apaga).</param>
/// <param name="Permissoes">Códigos de <see cref="Permissoes"/>.</param>
public sealed record PapelDoSistema(string Id, string Nome, string? Descricao, bool DoSistema, IReadOnlyList<string> Permissoes);

/// <summary>O resultado de uma tentativa de entrar.</summary>
public enum ResultadoDaEntrada
{
    /// <summary>Entrou.</summary>
    Entrou,

    /// <summary>Login ou senha errados (a mensagem não diz qual).</summary>
    Recusada,

    /// <summary>Muitas tentativas erradas seguidas.</summary>
    Bloqueado,

    /// <summary>Usuário desativado.</summary>
    Inativo,
}

/// <summary>
/// Usuários do sistema, papéis e permissões (migração 020, ADR-0026).
/// </summary>
/// <remarks>
/// <para>
/// Senha com PBKDF2-SHA256, sal de 16 bytes por usuário e <see cref="Iteracoes"/> iterações. A senha e
/// o hash nunca saem daqui: nenhum método devolve nem registra.
/// </para>
/// <para>
/// O administrador padrão (<see cref="LoginPadrao"/> / <see cref="SenhaPadrao"/>) só é criado quando não
/// há nenhum usuário, e nasce com troca obrigatória: com a senha padrão, só a troca funciona.
/// </para>
/// </remarks>
public sealed class UsuariosDoSistema
{
    /// <summary>Login do administrador que vem instalado.</summary>
    public const string LoginPadrao = "admin";

    /// <summary>Senha do administrador que vem instalado. É pública (está no manual) e só serve para a troca.</summary>
    public const string SenhaPadrao = "xacess";

    /// <summary>Iterações do PBKDF2-SHA256 (recomendação OWASP 2023).</summary>
    public const int Iteracoes = 600_000;

    /// <summary>Tamanho mínimo da senha nova.</summary>
    public const int TamanhoMinimoDaSenha = 8;

    /// <summary>Erros seguidos que bloqueiam a entrada.</summary>
    public const int ErrosAteBloquear = 5;

    /// <summary>Quanto dura o bloqueio.</summary>
    public static readonly TimeSpan DuracaoDoBloqueio = TimeSpan.FromMinutes(5);

    private const int TamanhoDoSal = 16;
    private const int TamanhoDoHash = 32;

    private readonly SqliteConnectionFactory _fabrica;
    private readonly int _iteracoes;

    /// <param name="fabrica">A base.</param>
    /// <param name="iteracoes">Iterações do hash; só os testes passam menos, para rodar rápido.</param>
    public UsuariosDoSistema(SqliteConnectionFactory fabrica, int iteracoes = Iteracoes)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentOutOfRangeException.ThrowIfLessThan(iteracoes, 100_000);
        _fabrica = fabrica;
        _iteracoes = iteracoes;
    }

    /// <summary>Cria o administrador padrão se a base não tem nenhum usuário. Devolve se criou.</summary>
    public bool GarantirAdministradorPadrao(DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        if (Escalar<long>(conexao, transacao, "SELECT COUNT(*) FROM app_user;") > 0)
        {
            return false;
        }

        var id = Guid.CreateVersion7(agora).ToString();
        var (hash, sal) = Hash(SenhaPadrao, _iteracoes);
        Executar(conexao, transacao,
            """
            INSERT INTO app_user (id, login, display_name, password_hash, password_salt, iterations, must_change, created_at, updated_at)
            VALUES ($id, $login, 'Administrador', $hash, $sal, $it, 1, $em, $em);
            INSERT INTO app_user_role (user_id, role_id) VALUES ($id, 'administrador');
            """,
            ("$id", id), ("$login", LoginPadrao), ("$hash", hash), ("$sal", sal), ("$it", _iteracoes), ("$em", Iso(agora)));
        Registrar(conexao, transacao, agora, null, "administrador_padrao_criado", LoginPadrao, null);
        transacao.Commit();
        return true;
    }

    /// <summary>Verdadeiro quando há pelo menos um usuário (o login vale).</summary>
    public bool HaUsuarios()
    {
        using var conexao = _fabrica.Abrir();
        return Escalar<long>(conexao, null, "SELECT COUNT(*) FROM app_user;") > 0;
    }

    /// <summary>
    /// Confere login e senha. Errar <see cref="ErrosAteBloquear"/> vezes seguidas bloqueia por
    /// <see cref="DuracaoDoBloqueio"/>, e o bloqueio vale mesmo com a senha certa.
    /// </summary>
    public (ResultadoDaEntrada Resultado, UsuarioDoSistema? Usuario) Entrar(string? login, string? senha, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(senha))
        {
            return (ResultadoDaEntrada.Recusada, null);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        string id;
        byte[] hash;
        byte[] sal;
        int iteracoes;
        string situacao;
        long erros;
        string? bloqueadoAte;

        using (var sql = conexao.CreateCommand())
        {
            sql.Transaction = transacao;
            sql.CommandText =
                "SELECT id, password_hash, password_salt, iterations, status, failed_count, locked_until FROM app_user WHERE login = $login;";
            sql.Parameters.AddWithValue("$login", login.Trim());
            using var leitor = sql.ExecuteReader();
            if (!leitor.Read())
            {
                // Mesmo custo de quando o usuário existe: o tempo de resposta não diz se o login existe.
                _ = Hash(senha, _iteracoes);
                return (ResultadoDaEntrada.Recusada, null);
            }

            id = leitor.GetString(0);
            hash = (byte[])leitor[1];
            sal = (byte[])leitor[2];
            iteracoes = leitor.GetInt32(3);
            situacao = leitor.GetString(4);
            erros = leitor.GetInt64(5);
            bloqueadoAte = leitor.IsDBNull(6) ? null : leitor.GetString(6);
        }

        if (bloqueadoAte is not null && Data(bloqueadoAte) > agora)
        {
            return (ResultadoDaEntrada.Bloqueado, null);
        }

        var confere = Confere(senha, hash, sal, iteracoes);

        if (!confere)
        {
            var novosErros = erros + 1;
            var bloqueia = novosErros >= ErrosAteBloquear;
            Executar(conexao, transacao,
                "UPDATE app_user SET failed_count = $n, locked_until = $ate WHERE id = $id;",
                ("$n", bloqueia ? 0 : novosErros),
                ("$ate", bloqueia ? Iso(agora + DuracaoDoBloqueio) : (object)DBNull.Value),
                ("$id", id));
            Registrar(conexao, transacao, agora, null, bloqueia ? "entrada_bloqueada" : "entrada_recusada", login.Trim(), null);
            transacao.Commit();
            return (bloqueia ? ResultadoDaEntrada.Bloqueado : ResultadoDaEntrada.Recusada, null);
        }

        if (situacao != "ativo")
        {
            Registrar(conexao, transacao, agora, id, "entrada_inativo", null, null);
            transacao.Commit();
            return (ResultadoDaEntrada.Inativo, null);
        }

        Executar(conexao, transacao,
            "UPDATE app_user SET failed_count = 0, locked_until = NULL, last_login_at = $em WHERE id = $id;",
            ("$em", Iso(agora)), ("$id", id));
        Registrar(conexao, transacao, agora, id, "entrou", null, null);
        transacao.Commit();
        return (ResultadoDaEntrada.Entrou, Obter(id));
    }

    /// <summary>
    /// Troca a própria senha (e, no primeiro acesso, o login e o nome). Exige a senha atual.
    /// Devolve os problemas; vazio quando trocou.
    /// </summary>
    public IReadOnlyList<string> TrocarSenha(string usuarioId, string? senhaAtual, string? senhaNova, string? novoLogin, string? novoNome, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usuarioId);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        byte[] hash;
        byte[] sal;
        int iteracoes;
        string loginAtual;
        using (var sql = conexao.CreateCommand())
        {
            sql.Transaction = transacao;
            sql.CommandText = "SELECT password_hash, password_salt, iterations, login FROM app_user WHERE id = $id;";
            sql.Parameters.AddWithValue("$id", usuarioId);
            using var leitor = sql.ExecuteReader();
            if (!leitor.Read())
            {
                return ["Usuário não encontrado."];
            }

            hash = (byte[])leitor[0];
            sal = (byte[])leitor[1];
            iteracoes = leitor.GetInt32(2);
            loginAtual = leitor.GetString(3);
        }

        if (string.IsNullOrEmpty(senhaAtual) || !Confere(senhaAtual, hash, sal, iteracoes))
        {
            return ["A senha atual não confere."];
        }

        var problemas = ProblemasNaSenha(senhaNova, senhaAtual).ToList();
        var login = string.IsNullOrWhiteSpace(novoLogin) ? loginAtual : novoLogin.Trim();
        problemas.AddRange(ProblemasNoLogin(login));
        if (novoNome is not null && novoNome.Trim().Length is < 2 or > 80)
        {
            problemas.Add("O nome precisa ter de 2 a 80 caracteres.");
        }

        if (string.Equals(login, LoginPadrao, StringComparison.OrdinalIgnoreCase) && string.Equals(senhaNova, SenhaPadrao, StringComparison.Ordinal))
        {
            problemas.Add("A senha padrão não pode continuar.");
        }

        if (problemas.Count == 0
            && !string.Equals(login, loginAtual, StringComparison.OrdinalIgnoreCase)
            && Escalar<long>(conexao, transacao, "SELECT COUNT(*) FROM app_user WHERE login = $l AND id <> $id;", ("$l", login), ("$id", usuarioId)) > 0)
        {
            problemas.Add($"Já existe um usuário com o login '{login}'.");
        }

        if (problemas.Count > 0)
        {
            return problemas;
        }

        var (novoHash, novoSal) = Hash(senhaNova!, _iteracoes);
        Executar(conexao, transacao,
            """
            UPDATE app_user SET password_hash = $h, password_salt = $s, iterations = $it, must_change = 0,
                login = $login, display_name = COALESCE($nome, display_name), updated_at = $em
            WHERE id = $id;
            """,
            ("$h", novoHash), ("$s", novoSal), ("$it", _iteracoes), ("$login", login),
            ("$nome", string.IsNullOrWhiteSpace(novoNome) ? DBNull.Value : novoNome.Trim()),
            ("$em", Iso(agora)), ("$id", usuarioId));
        Registrar(conexao, transacao, agora, usuarioId, "senha_trocada", null, null);
        transacao.Commit();
        return [];
    }

    /// <summary>Um usuário pelo id, ou nulo.</summary>
    public UsuarioDoSistema? Obter(string usuarioId)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            SELECT u.login, u.display_name, u.status, u.must_change, u.last_login_at,
                   (SELECT group_concat(role_id, char(31)) FROM app_user_role WHERE user_id = u.id)
            FROM app_user u WHERE u.id = $id;
            """;
        sql.Parameters.AddWithValue("$id", usuarioId);
        using var leitor = sql.ExecuteReader();
        if (!leitor.Read())
        {
            return null;
        }

        return new UsuarioDoSistema(
            usuarioId,
            leitor.GetString(0),
            leitor.GetString(1),
            leitor.GetString(2) == "ativo",
            leitor.GetInt64(3) == 1,
            leitor.IsDBNull(5) ? [] : leitor.GetString(5).Split('\u001f').Order(StringComparer.Ordinal).ToList(),
            leitor.IsDBNull(4) ? null : Data(leitor.GetString(4)));
    }

    /// <summary>Todos os usuários, por nome.</summary>
    public IReadOnlyList<UsuarioDoSistema> Listar()
    {
        using var conexao = _fabrica.Abrir();
        var papeis = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var sql = conexao.CreateCommand())
        {
            sql.CommandText = "SELECT user_id, role_id FROM app_user_role ORDER BY role_id;";
            using var leitor = sql.ExecuteReader();
            while (leitor.Read())
            {
                var lista = papeis.TryGetValue(leitor.GetString(0), out var l) ? l : papeis[leitor.GetString(0)] = [];
                lista.Add(leitor.GetString(1));
            }
        }

        var usuarios = new List<UsuarioDoSistema>();
        using (var sql = conexao.CreateCommand())
        {
            sql.CommandText = "SELECT id, login, display_name, status, must_change, last_login_at FROM app_user ORDER BY display_name COLLATE NOCASE;";
            using var leitor = sql.ExecuteReader();
            while (leitor.Read())
            {
                var id = leitor.GetString(0);
                usuarios.Add(new UsuarioDoSistema(
                    id,
                    leitor.GetString(1),
                    leitor.GetString(2),
                    leitor.GetString(3) == "ativo",
                    leitor.GetInt64(4) == 1,
                    papeis.TryGetValue(id, out var p) ? p : [],
                    leitor.IsDBNull(5) ? null : Data(leitor.GetString(5))));
            }
        }

        return usuarios;
    }

    /// <summary>As permissões efetivas de um usuário ativo (união dos papéis). Vazio se inativo ou inexistente.</summary>
    public IReadOnlySet<string> PermissoesDe(string usuarioId)
    {
        using var conexao = _fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText =
            """
            SELECT DISTINCT rp.permission
            FROM app_user u
            JOIN app_user_role ur ON ur.user_id = u.id
            JOIN app_role_permission rp ON rp.role_id = ur.role_id
            WHERE u.id = $id AND u.status = 'ativo';
            """;
        sql.Parameters.AddWithValue("$id", usuarioId);
        var permissoes = new HashSet<string>(StringComparer.Ordinal);
        using var leitor = sql.ExecuteReader();
        while (leitor.Read())
        {
            permissoes.Add(leitor.GetString(0));
        }

        return permissoes;
    }

    /// <summary>
    /// Cria (sem <paramref name="id"/>) ou altera um usuário: login, nome, ativo e papéis. Na criação, a
    /// <paramref name="senhaInicial"/> é obrigatória e o usuário troca no primeiro acesso.
    /// </summary>
    /// <param name="quem">Usuário que fez a mudança (autoria).</param>
    /// <param name="id">Nulo para criar.</param>
    /// <param name="login">Login.</param>
    /// <param name="nome">Nome exibido.</param>
    /// <param name="ativo">Pode entrar.</param>
    /// <param name="papeis">Ids dos papéis.</param>
    /// <param name="senhaInicial">Só na criação.</param>
    /// <param name="agora">Agora.</param>
    public (string? Id, IReadOnlyList<string> Problemas) Gravar(
        string quem, string? id, string? login, string? nome, bool ativo, IReadOnlyCollection<string> papeis, string? senhaInicial, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quem);
        ArgumentNullException.ThrowIfNull(papeis);

        var problemas = new List<string>();
        var loginLimpo = login?.Trim() ?? string.Empty;
        var nomeLimpo = nome?.Trim() ?? string.Empty;
        problemas.AddRange(ProblemasNoLogin(loginLimpo));
        if (nomeLimpo.Length is < 2 or > 80)
        {
            problemas.Add("O nome precisa ter de 2 a 80 caracteres.");
        }

        if (papeis.Count == 0)
        {
            problemas.Add("Escolha pelo menos um papel.");
        }

        var criando = string.IsNullOrWhiteSpace(id);
        if (criando)
        {
            problemas.AddRange(ProblemasNaSenha(senhaInicial, null));
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var papeisExistentes = new HashSet<string>(StringComparer.Ordinal);
        using (var sql = conexao.CreateCommand())
        {
            sql.Transaction = transacao;
            sql.CommandText = "SELECT id FROM app_role;";
            using var leitor = sql.ExecuteReader();
            while (leitor.Read())
            {
                papeisExistentes.Add(leitor.GetString(0));
            }
        }

        problemas.AddRange(papeis.Where(p => !papeisExistentes.Contains(p)).Select(p => $"Papel desconhecido: {p}."));

        if (problemas.Count == 0
            && Escalar<long>(conexao, transacao, "SELECT COUNT(*) FROM app_user WHERE login = $l AND id <> $id;", ("$l", loginLimpo), ("$id", id ?? string.Empty)) > 0)
        {
            problemas.Add($"Já existe um usuário com o login '{loginLimpo}'.");
        }

        if (!criando && problemas.Count == 0 && Escalar<long>(conexao, transacao, "SELECT COUNT(*) FROM app_user WHERE id = $id;", ("$id", id!)) == 0)
        {
            problemas.Add("Usuário não encontrado.");
        }

        // Nunca deixar o sistema sem administrador ativo.
        if (!criando && problemas.Count == 0 && (!ativo || !papeis.Contains("administrador"))
            && Escalar<long>(
                conexao, transacao,
                """
                SELECT COUNT(*) FROM app_user u JOIN app_user_role r ON r.user_id = u.id
                WHERE r.role_id = 'administrador' AND u.status = 'ativo' AND u.id <> $id;
                """,
                ("$id", id!)) == 0)
        {
            problemas.Add("Este é o último administrador ativo: o sistema não pode ficar sem administrador.");
        }

        if (problemas.Count > 0)
        {
            return (null, problemas);
        }

        var alvo = criando ? Guid.CreateVersion7(agora).ToString() : id!;
        if (criando)
        {
            var (hash, sal) = Hash(senhaInicial!, _iteracoes);
            Executar(conexao, transacao,
                """
                INSERT INTO app_user (id, login, display_name, password_hash, password_salt, iterations, must_change, status, created_at, updated_at)
                VALUES ($id, $login, $nome, $h, $s, $it, 1, $situacao, $em, $em);
                """,
                ("$id", alvo), ("$login", loginLimpo), ("$nome", nomeLimpo), ("$h", hash), ("$s", sal), ("$it", _iteracoes),
                ("$situacao", ativo ? "ativo" : "inativo"), ("$em", Iso(agora)));
        }
        else
        {
            Executar(conexao, transacao,
                "UPDATE app_user SET login = $login, display_name = $nome, status = $situacao, updated_at = $em WHERE id = $id;",
                ("$id", alvo), ("$login", loginLimpo), ("$nome", nomeLimpo), ("$situacao", ativo ? "ativo" : "inativo"), ("$em", Iso(agora)));
            Executar(conexao, transacao, "DELETE FROM app_user_role WHERE user_id = $id;", ("$id", alvo));
        }

        foreach (var papel in papeis.Distinct(StringComparer.Ordinal))
        {
            Executar(conexao, transacao, "INSERT INTO app_user_role (user_id, role_id) VALUES ($u, $r);", ("$u", alvo), ("$r", papel));
        }

        Registrar(conexao, transacao, agora, quem, criando ? "usuario_criado" : "usuario_alterado", alvo,
            $"papeis={string.Join(',', papeis.Order(StringComparer.Ordinal))};ativo={(ativo ? 1 : 0)}");
        transacao.Commit();
        return (alvo, []);
    }

    /// <summary>O administrador define uma senha provisória; o usuário troca no próximo acesso.</summary>
    public IReadOnlyList<string> RedefinirSenha(string quem, string usuarioId, string? senhaProvisoria, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quem);
        var problemas = ProblemasNaSenha(senhaProvisoria, null);
        if (problemas.Count > 0)
        {
            return problemas;
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var (hash, sal) = Hash(senhaProvisoria!, _iteracoes);
        var mudou = Executar(conexao, transacao,
            """
            UPDATE app_user SET password_hash = $h, password_salt = $s, iterations = $it, must_change = 1,
                failed_count = 0, locked_until = NULL, updated_at = $em
            WHERE id = $id;
            """,
            ("$h", hash), ("$s", sal), ("$it", _iteracoes), ("$em", Iso(agora)), ("$id", usuarioId));
        if (mudou == 0)
        {
            return ["Usuário não encontrado."];
        }

        Registrar(conexao, transacao, agora, quem, "senha_redefinida", usuarioId, null);
        transacao.Commit();
        return [];
    }

    /// <summary>Todos os papéis, com as permissões.</summary>
    public IReadOnlyList<PapelDoSistema> ListarPapeis()
    {
        using var conexao = _fabrica.Abrir();
        var permissoes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var sql = conexao.CreateCommand())
        {
            sql.CommandText = "SELECT role_id, permission FROM app_role_permission ORDER BY permission;";
            using var leitor = sql.ExecuteReader();
            while (leitor.Read())
            {
                var lista = permissoes.TryGetValue(leitor.GetString(0), out var l) ? l : permissoes[leitor.GetString(0)] = [];
                lista.Add(leitor.GetString(1));
            }
        }

        var papeis = new List<PapelDoSistema>();
        using (var sql = conexao.CreateCommand())
        {
            sql.CommandText = "SELECT id, name, description, builtin FROM app_role ORDER BY builtin DESC, name COLLATE NOCASE;";
            using var leitor = sql.ExecuteReader();
            while (leitor.Read())
            {
                var id = leitor.GetString(0);
                papeis.Add(new PapelDoSistema(
                    id, leitor.GetString(1), leitor.IsDBNull(2) ? null : leitor.GetString(2), leitor.GetInt64(3) == 1,
                    permissoes.TryGetValue(id, out var p) ? p : []));
            }
        }

        return papeis;
    }

    /// <summary>
    /// Cria (sem <paramref name="id"/>) ou altera um papel e as permissões dele. O papel Administrador
    /// sempre mantém a gestão de usuários.
    /// </summary>
    /// <param name="quem">Usuário que fez a mudança.</param>
    /// <param name="id">Nulo para criar.</param>
    /// <param name="nome">Nome exibido.</param>
    /// <param name="descricao">Para que serve.</param>
    /// <param name="permissoes">Códigos do catálogo.</param>
    /// <param name="agora">Agora.</param>
    public (string? Id, IReadOnlyList<string> Problemas) GravarPapel(
        string quem, string? id, string? nome, string? descricao, IReadOnlyCollection<string> permissoes, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quem);
        ArgumentNullException.ThrowIfNull(permissoes);

        var problemas = new List<string>();
        var nomeLimpo = nome?.Trim() ?? string.Empty;
        if (nomeLimpo.Length is < 2 or > 40)
        {
            problemas.Add("O nome do papel precisa ter de 2 a 40 caracteres.");
        }

        problemas.AddRange(permissoes.Where(p => !Permissoes.Existe(p)).Select(p => $"Permissão desconhecida: {p}."));
        if (id == "administrador" && !permissoes.Contains(Permissoes.UsuariosGerenciar))
        {
            problemas.Add("O papel Administrador sempre gerencia usuários.");
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var criando = string.IsNullOrWhiteSpace(id);
        if (problemas.Count == 0
            && Escalar<long>(conexao, transacao, "SELECT COUNT(*) FROM app_role WHERE name = $n AND id <> $id;", ("$n", nomeLimpo), ("$id", id ?? string.Empty)) > 0)
        {
            problemas.Add($"Já existe um papel chamado '{nomeLimpo}'.");
        }

        if (!criando && problemas.Count == 0 && Escalar<long>(conexao, transacao, "SELECT COUNT(*) FROM app_role WHERE id = $id;", ("$id", id!)) == 0)
        {
            problemas.Add("Papel não encontrado.");
        }

        if (problemas.Count > 0)
        {
            return (null, problemas);
        }

        var alvo = criando ? Guid.CreateVersion7(agora).ToString() : id!;
        if (criando)
        {
            Executar(conexao, transacao, "INSERT INTO app_role (id, name, description, builtin) VALUES ($id, $n, $d, 0);",
                ("$id", alvo), ("$n", nomeLimpo), ("$d", (object?)descricao?.Trim() ?? DBNull.Value));
        }
        else
        {
            Executar(conexao, transacao, "UPDATE app_role SET name = $n, description = $d WHERE id = $id;",
                ("$id", alvo), ("$n", nomeLimpo), ("$d", (object?)descricao?.Trim() ?? DBNull.Value));
            Executar(conexao, transacao,
                "DELETE FROM app_role_permission WHERE role_id = $id AND NOT (role_id = 'administrador' AND permission = 'usuarios.gerenciar');",
                ("$id", alvo));
        }

        foreach (var permissao in permissoes.Distinct(StringComparer.Ordinal))
        {
            Executar(conexao, transacao, "INSERT OR IGNORE INTO app_role_permission (role_id, permission) VALUES ($r, $p);", ("$r", alvo), ("$p", permissao));
        }

        Registrar(conexao, transacao, agora, quem, criando ? "papel_criado" : "papel_alterado", alvo,
            "permissoes=" + string.Join(',', permissoes.Order(StringComparer.Ordinal)));
        transacao.Commit();
        return (alvo, []);
    }

    /// <summary>O que impede uma senha nova; vazio quando serve.</summary>
    public static IReadOnlyList<string> ProblemasNaSenha(string? senha, string? senhaAtual)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < TamanhoMinimoDaSenha)
        {
            return [$"A senha precisa ter pelo menos {TamanhoMinimoDaSenha} caracteres."];
        }

        if (senha.Length > 128)
        {
            return ["A senha pode ter no máximo 128 caracteres."];
        }

        if (senhaAtual is not null && string.Equals(senha, senhaAtual, StringComparison.Ordinal))
        {
            return ["A senha nova precisa ser diferente da atual."];
        }

        return [];
    }

    private static IEnumerable<string> ProblemasNoLogin(string login)
    {
        if (login.Length is < 3 or > 40 || !login.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            yield return "O login precisa ter de 3 a 40 caracteres: letras sem acento, números, ponto, hífen ou sublinhado.";
        }
    }

    private static (byte[] Hash, byte[] Sal) Hash(string senha, int iteracoes)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanhoDoSal);
        return (Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, iteracoes, HashAlgorithmName.SHA256, TamanhoDoHash), sal);
    }

    private static bool Confere(string senha, byte[] hash, byte[] sal, int iteracoes) =>
        CryptographicOperations.FixedTimeEquals(
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(senha), sal, iteracoes, HashAlgorithmName.SHA256, hash.Length),
            hash);

    private static void Registrar(SqliteConnection conexao, SqliteTransaction transacao, DateTimeOffset agora, string? quem, string acao, string? alvo, string? detalhe) =>
        Executar(conexao, transacao,
            "INSERT INTO app_user_event (at, actor_id, action, target, detail) VALUES ($em, $quem, $acao, $alvo, $detalhe);",
            ("$em", Iso(agora)), ("$quem", (object?)quem ?? DBNull.Value), ("$acao", acao),
            ("$alvo", (object?)alvo ?? DBNull.Value), ("$detalhe", (object?)detalhe ?? DBNull.Value));

    private static int Executar(SqliteConnection conexao, SqliteTransaction? transacao, string sql, params (string Nome, object Valor)[] parametros)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }

        return comando.ExecuteNonQuery();
    }

    private static T Escalar<T>(SqliteConnection conexao, SqliteTransaction? transacao, string sql, params (string Nome, object Valor)[] parametros)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }

        return (T)Convert.ChangeType(comando.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private static string Iso(DateTimeOffset valor) => valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Data(string texto) => DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
