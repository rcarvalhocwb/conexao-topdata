using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using static Access.Infrastructure.SQLite.CadastroDePessoas;

namespace Access.Infrastructure.SQLite;

/// <summary>Uma empresa (condomínio, prestadora, expositor).</summary>
public sealed record Empresa(string? Id, string Nome, string? Cnpj, bool Ativa = true);

/// <summary>Uma sala, conjunto ou unidade, de uma empresa ou do prédio.</summary>
public sealed record Sala(string? Id, string Nome, string? EmpresaId, string? Andar, string? Bloco, bool Ativa = true);

/// <summary>Uma faixa de horário: dia 0 = domingo … 6 = sábado, 7 = feriado; minutos desde a meia-noite (Brasília), fim exclusivo.</summary>
public sealed record FaixaDeHorario(int Dia, int Inicio, int Fim);

/// <summary>Uma tabela de horário (1 a 100, como as tabelas do Inner).</summary>
public sealed record TabelaDeHorario(int Id, string Nome, IReadOnlyList<FaixaDeHorario> Faixas);

/// <summary>Um feriado (o dia usa as faixas do dia 7).</summary>
public sealed record Feriado(DateOnly Dia, string Nome);

/// <summary>Um perfil de pessoa: campos obrigatórios e regras padrão de quem tem esse perfil.</summary>
public sealed record PerfilDePessoa(
    string Id,
    string Nome,
    bool Pronto,
    IReadOnlyList<string> CamposObrigatorios,
    bool ExigeAnfitriao,
    int? DiasDeValidade,
    int? TabelaDeHorario,
    int? LimiteDiario,
    int DiasDeRetencao,
    bool Ativo,
    IReadOnlyList<int> Catracas);

/// <summary>Os parâmetros do cadastro de pessoas (docs/43 §5): empresas, salas, horários, feriados e perfis.</summary>
public sealed class ParametrosDoCadastro
{
    /// <summary>Faixas por dia que o software aceita (docs/43 §6.3).</summary>
    public const int FaixasPorDia = 4;

    /// <summary>Faixas por dia que a lista off-line do Inner aceita (A_CONFIRMAR na bancada).</summary>
    public const int FaixasPorDiaNoInner = 2;

    private readonly SqliteConnectionFactory _fabrica;

    public ParametrosDoCadastro(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>As empresas, ativas primeiro.</summary>
    public IReadOnlyList<Empresa> Empresas() =>
        Ler("SELECT id, name, cnpj, status FROM company ORDER BY status, name COLLATE NOCASE;",
            l => new Empresa(l.GetString(0), l.GetString(1), Texto(l, 2), l.GetString(3) == "ativa"));

    /// <summary>Cria ou altera uma empresa.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="empresa">A empresa.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro GravarEmpresa(string? quem, Empresa empresa, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(empresa);
        var nome = empresa.Nome?.Trim() ?? string.Empty;
        if (nome.Length is < 1 or > 120)
        {
            return ResultadoDoCadastro.Recusado("Nome da empresa: de 1 a 120 letras.");
        }

        var cnpj = new string((empresa.Cnpj ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (cnpj.Length > 0 && !CnpjValido(cnpj))
        {
            return ResultadoDoCadastro.Recusado("CNPJ inválido (confira os dígitos).");
        }

        return Gravar(quem, empresa.Id, agora, "empresa",
            "INSERT INTO company (id, name, cnpj, status, created_at, updated_at) VALUES ($id, $nome, $cnpj, $s, $em, $em);",
            "UPDATE company SET name = $nome, cnpj = $cnpj, status = $s, updated_at = $em WHERE id = $id;",
            ("$nome", nome), ("$cnpj", Nulo(cnpj)), ("$s", empresa.Ativa ? "ativa" : "inativa"));
    }

    /// <summary>As salas, ativas primeiro.</summary>
    public IReadOnlyList<Sala> Salas() =>
        Ler("SELECT id, name, company_id, floor, block, status FROM place ORDER BY status, name COLLATE NOCASE;",
            l => new Sala(l.GetString(0), l.GetString(1), Texto(l, 2), Texto(l, 3), Texto(l, 4), l.GetString(5) == "ativo"));

    /// <summary>Cria ou altera uma sala.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="sala">A sala.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro GravarSala(string? quem, Sala sala, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(sala);
        var nome = sala.Nome?.Trim() ?? string.Empty;
        if (nome.Length is < 1 or > 80)
        {
            return ResultadoDoCadastro.Recusado("Nome da sala: de 1 a 80 letras.");
        }

        if (!string.IsNullOrWhiteSpace(sala.EmpresaId) && !Empresas().Any(e => e.Id == sala.EmpresaId))
        {
            return ResultadoDoCadastro.Recusado("Empresa não encontrada.");
        }

        return Gravar(quem, sala.Id, agora, "sala",
            "INSERT INTO place (id, company_id, name, floor, block, status, created_at, updated_at) VALUES ($id, $empresa, $nome, $andar, $bloco, $s, $em, $em);",
            "UPDATE place SET company_id = $empresa, name = $nome, floor = $andar, block = $bloco, status = $s, updated_at = $em WHERE id = $id;",
            ("$empresa", Nulo(sala.EmpresaId)), ("$nome", nome), ("$andar", Nulo(sala.Andar)), ("$bloco", Nulo(sala.Bloco)),
            ("$s", sala.Ativa ? "ativo" : "inativo"));
    }

    /// <summary>As tabelas de horário, com as faixas.</summary>
    public IReadOnlyList<TabelaDeHorario> Horarios()
    {
        var faixas = Ler("SELECT schedule_id, weekday, start_min, end_min FROM time_schedule_slot ORDER BY schedule_id, weekday, start_min;",
            l => (Tabela: l.GetInt32(0), Faixa: new FaixaDeHorario(l.GetInt32(1), l.GetInt32(2), l.GetInt32(3))));
        return Ler("SELECT id, name FROM time_schedule ORDER BY id;",
            l => new TabelaDeHorario(l.GetInt32(0), l.GetString(1), [.. faixas.Where(f => f.Tabela == l.GetInt32(0)).Select(f => f.Faixa)]));
    }

    /// <summary>
    /// Cria ou substitui uma tabela de horário. Id 0 usa o primeiro livre. Avisos (não impedem): faixas a mais
    /// do que a lista off-line do Inner aceita.
    /// </summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="tabela">A tabela.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro GravarHorario(string? quem, TabelaDeHorario tabela, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(tabela);
        var nome = tabela.Nome?.Trim() ?? string.Empty;
        var problemas = new List<string>();
        if (nome.Length is < 2 or > 60)
        {
            problemas.Add("Nome da tabela: de 2 a 60 letras.");
        }

        foreach (var faixa in tabela.Faixas)
        {
            if (faixa.Dia is < 0 or > 7 || faixa.Inicio is < 0 or > 1439 || faixa.Fim is < 1 or > 1440 || faixa.Fim <= faixa.Inicio)
            {
                problemas.Add($"Faixa inválida: {Rotulo(faixa)}.");
            }
        }

        foreach (var dia in tabela.Faixas.GroupBy(f => f.Dia))
        {
            var ordenadas = dia.OrderBy(f => f.Inicio).ToList();
            if (ordenadas.Count > FaixasPorDia)
            {
                problemas.Add($"{NomeDoDia(dia.Key)}: no máximo {FaixasPorDia} faixas.");
            }

            for (var i = 1; i < ordenadas.Count; i++)
            {
                if (ordenadas[i].Inicio < ordenadas[i - 1].Fim)
                {
                    problemas.Add($"{NomeDoDia(dia.Key)}: faixas sobrepostas.");
                    break;
                }
            }
        }

        if (problemas.Count > 0)
        {
            return new ResultadoDoCadastro(null, problemas);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var id = tabela.Id;
        if (id == 0)
        {
            // O primeiro número livre de 1 a 100.
            id = Convert.ToInt32(Escalar(conexao, transacao,
                """
                WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 100)
                SELECT COALESCE(MIN(i), 0) FROM n WHERE i NOT IN (SELECT id FROM time_schedule);
                """),
                CultureInfo.InvariantCulture);
            if (id == 0)
            {
                return ResultadoDoCadastro.Recusado("Já existem 100 tabelas de horário (o máximo do Inner).");
            }
        }
        else if (id is < 1 or > 100)
        {
            return ResultadoDoCadastro.Recusado("Tabela de horário: número de 1 a 100.");
        }

        if (Escalar(conexao, transacao, "SELECT 1 FROM time_schedule WHERE name = $n AND id <> $id;", ("$n", nome), ("$id", id)) is not null)
        {
            return ResultadoDoCadastro.Recusado("Já existe uma tabela de horário com este nome.");
        }

        Executar(conexao, transacao,
            """
            INSERT INTO time_schedule (id, name, created_at, updated_at) VALUES ($id, $n, $em, $em)
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, updated_at = excluded.updated_at;
            """,
            ("$id", id), ("$n", nome), ("$em", Iso(agora)));
        Executar(conexao, transacao, "DELETE FROM time_schedule_slot WHERE schedule_id = $id;", ("$id", id));
        foreach (var faixa in tabela.Faixas)
        {
            Executar(conexao, transacao,
                "INSERT INTO time_schedule_slot (schedule_id, weekday, start_min, end_min) VALUES ($id, $d, $i, $f);",
                ("$id", id), ("$d", faixa.Dia), ("$i", faixa.Inicio), ("$f", faixa.Fim));
        }

        Registrar(conexao, transacao, agora, quem, null, "horario.gravar", $"tabela={id}");
        transacao.Commit();
        var avisos = tabela.Faixas.GroupBy(f => f.Dia).Where(g => g.Count() > FaixasPorDiaNoInner)
            .Select(g => $"{NomeDoDia(g.Key)} tem mais de {FaixasPorDiaNoInner} faixas: a lista off-line do Inner talvez não aceite (a confirmar na bancada).")
            .ToList();
        return new ResultadoDoCadastro(id.ToString(CultureInfo.InvariantCulture), []) { Avisos = avisos };
    }

    /// <summary>Apaga uma tabela de horário que nenhum perfil nem pessoa usa.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="id">A tabela.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro ExcluirHorario(string? quem, int id, DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        if (Escalar(conexao, transacao,
                "SELECT 1 FROM person_profile WHERE schedule_id = $id UNION ALL SELECT 1 FROM person WHERE schedule_id = $id LIMIT 1;",
                ("$id", id)) is not null)
        {
            return ResultadoDoCadastro.Recusado("A tabela está em uso por um perfil ou uma pessoa.");
        }

        if (Executar(conexao, transacao, "DELETE FROM time_schedule WHERE id = $id;", ("$id", id)) == 0)
        {
            return ResultadoDoCadastro.Recusado("Tabela de horário não encontrada.");
        }

        Registrar(conexao, transacao, agora, quem, null, "horario.excluir", $"tabela={id}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Os feriados, por data.</summary>
    public IReadOnlyList<Feriado> Feriados() =>
        Ler("SELECT day, name FROM holiday ORDER BY day;",
            l => new Feriado(DateOnly.ParseExact(l.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), l.GetString(1)));

    /// <summary>Marca (ou renomeia) um feriado.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="feriado">O feriado.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro GravarFeriado(string? quem, Feriado feriado, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(feriado);
        var nome = feriado.Nome?.Trim() ?? string.Empty;
        if (nome.Length is < 2 or > 60)
        {
            return ResultadoDoCadastro.Recusado("Nome do feriado: de 2 a 60 letras.");
        }

        var dia = feriado.Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        Executar(conexao, transacao, "INSERT INTO holiday (day, name) VALUES ($d, $n) ON CONFLICT (day) DO UPDATE SET name = excluded.name;", ("$d", dia), ("$n", nome));
        Registrar(conexao, transacao, agora, quem, null, "feriado.gravar", dia);
        transacao.Commit();
        return ResultadoDoCadastro.Ok(dia);
    }

    /// <summary>Desmarca um feriado.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="dia">O dia.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro ExcluirFeriado(string? quem, DateOnly dia, DateTimeOffset agora)
    {
        var texto = dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        if (Executar(conexao, transacao, "DELETE FROM holiday WHERE day = $d;", ("$d", texto)) == 0)
        {
            return ResultadoDoCadastro.Recusado("Feriado não encontrado.");
        }

        Registrar(conexao, transacao, agora, quem, null, "feriado.excluir", texto);
        transacao.Commit();
        return ResultadoDoCadastro.Ok(texto);
    }

    /// <summary>Os perfis, com as catracas permitidas.</summary>
    public IReadOnlyList<PerfilDePessoa> Perfis()
    {
        var catracas = Ler("SELECT profile_id, inner_number FROM person_profile_gate ORDER BY inner_number;", l => (Perfil: l.GetString(0), Inner: l.GetInt32(1)));
        return Ler(
            """
            SELECT id, name, builtin, required_fields, requires_host, default_days, schedule_id, daily_limit, retention_days, status
            FROM person_profile ORDER BY builtin DESC, name COLLATE NOCASE;
            """,
            l => new PerfilDePessoa(
                l.GetString(0),
                l.GetString(1),
                l.GetInt32(2) == 1,
                JsonSerializer.Deserialize<string[]>(l.GetString(3)) ?? [],
                l.GetInt32(4) == 1,
                l.IsDBNull(5) ? null : l.GetInt32(5),
                l.IsDBNull(6) ? null : l.GetInt32(6),
                l.IsDBNull(7) ? null : l.GetInt32(7),
                l.GetInt32(8),
                l.GetString(9) == "ativo",
                [.. catracas.Where(c => c.Perfil == l.GetString(0)).Select(c => c.Inner)]));
    }

    /// <summary>Cria ou altera um perfil. O id de um perfil novo vem do nome.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="perfil">O perfil.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro GravarPerfil(string? quem, PerfilDePessoa perfil, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        var nome = perfil.Nome?.Trim() ?? string.Empty;
        var problemas = new List<string>();
        if (nome.Length is < 2 or > 40)
        {
            problemas.Add("Nome do perfil: de 2 a 40 letras.");
        }

        var conhecidos = CamposDaPessoa.Todos.Select(c => c.Codigo).ToHashSet(StringComparer.Ordinal);
        if (perfil.CamposObrigatorios.Any(c => !conhecidos.Contains(c)))
        {
            problemas.Add("Campo obrigatório desconhecido.");
        }

        if (perfil.DiasDeValidade is < 0 or > 3650)
        {
            problemas.Add("Validade padrão: de 0 a 3650 dias.");
        }

        if (perfil.LimiteDiario is < 1 or > 100)
        {
            problemas.Add("Limite por dia: de 1 a 100.");
        }

        if (perfil.DiasDeRetencao is < 1 or > 3650)
        {
            problemas.Add("Retenção: de 1 a 3650 dias.");
        }

        if (perfil.Catracas.Any(c => c is < 1 or > 99))
        {
            problemas.Add("Catraca fora de 1 a 99.");
        }

        if (problemas.Count > 0)
        {
            return new ResultadoDoCadastro(null, problemas);
        }

        var campos = perfil.CamposObrigatorios.Append(CamposDaPessoa.Nome).Distinct(StringComparer.Ordinal).ToArray();
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var existe = !string.IsNullOrWhiteSpace(perfil.Id)
            && Escalar(conexao, transacao, "SELECT 1 FROM person_profile WHERE id = $id;", ("$id", perfil.Id)) is not null;
        var id = existe ? perfil.Id : IdDoNome(nome);

        if (Escalar(conexao, transacao, "SELECT 1 FROM person_profile WHERE (name = $n COLLATE NOCASE OR id = $id) AND id <> $atual;",
                ("$n", nome), ("$id", id), ("$atual", existe ? id : string.Empty)) is not null)
        {
            return ResultadoDoCadastro.Recusado("Já existe um perfil com este nome.");
        }

        if (perfil.TabelaDeHorario is { } tabela && Escalar(conexao, transacao, "SELECT 1 FROM time_schedule WHERE id = $id;", ("$id", tabela)) is null)
        {
            return ResultadoDoCadastro.Recusado("Tabela de horário não encontrada.");
        }

        Executar(conexao, transacao,
            existe
                ? """
                  UPDATE person_profile SET name = $n, required_fields = $campos, requires_host = $anfitriao, default_days = $dias,
                      schedule_id = $horario, daily_limit = $limite, retention_days = $retencao, status = $s
                  WHERE id = $id;
                  """
                : """
                  INSERT INTO person_profile (id, name, builtin, required_fields, requires_host, default_days, schedule_id, daily_limit, retention_days, status)
                  VALUES ($id, $n, 0, $campos, $anfitriao, $dias, $horario, $limite, $retencao, $s);
                  """,
            ("$id", id), ("$n", nome), ("$campos", JsonSerializer.Serialize(campos)), ("$anfitriao", perfil.ExigeAnfitriao ? 1 : 0),
            ("$dias", (object?)perfil.DiasDeValidade ?? DBNull.Value), ("$horario", (object?)perfil.TabelaDeHorario ?? DBNull.Value),
            ("$limite", (object?)perfil.LimiteDiario ?? DBNull.Value), ("$retencao", perfil.DiasDeRetencao), ("$s", perfil.Ativo ? "ativo" : "inativo"));

        Executar(conexao, transacao, "DELETE FROM person_profile_gate WHERE profile_id = $id;", ("$id", id));
        foreach (var inner in perfil.Catracas.Distinct())
        {
            Executar(conexao, transacao, "INSERT INTO person_profile_gate (profile_id, inner_number) VALUES ($id, $i);", ("$id", id), ("$i", inner));
        }

        Registrar(conexao, transacao, agora, quem, null, existe ? "perfil.alterar" : "perfil.criar", $"perfil={id}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    /// <summary>Confere o CNPJ (14 dígitos) pelos dígitos verificadores.</summary>
    public static bool CnpjValido(string? cnpj)
    {
        var d = (cnpj ?? string.Empty).Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (d.Length != 14 || d.All(x => x == d[0]))
        {
            return false;
        }

        static int Digito(int[] numeros, int tamanho)
        {
            int[] pesos = tamanho == 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
            var soma = 0;
            for (var i = 0; i < tamanho; i++)
            {
                soma += numeros[i] * pesos[i];
            }

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(d, 12) == d[12] && Digito(d, 13) == d[13];
    }

    /// <summary>O nome do dia: 0 = domingo … 6 = sábado, 7 = feriado.</summary>
    public static string NomeDoDia(int dia) => dia switch
    {
        0 => "Domingo",
        1 => "Segunda",
        2 => "Terça",
        3 => "Quarta",
        4 => "Quinta",
        5 => "Sexta",
        6 => "Sábado",
        7 => "Feriado",
        _ => $"Dia {dia}",
    };

    private static string Rotulo(FaixaDeHorario faixa) =>
        $"{NomeDoDia(faixa.Dia)} {faixa.Inicio / 60:00}:{faixa.Inicio % 60:00}–{faixa.Fim / 60:00}:{faixa.Fim % 60:00}";

    private static string IdDoNome(string nome)
    {
        var limpo = new string(nome.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_')
            .ToArray()).Trim('_');
        return limpo.Length == 0 ? Guid.NewGuid().ToString("N")[..12] : limpo;
    }

    private ResultadoDoCadastro Gravar(
        string? quem, string? id, DateTimeOffset agora, string tipo, string inserir, string alterar, params (string Nome, object Valor)[] parametros)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var nova = string.IsNullOrWhiteSpace(id);
        var chave = nova ? Guid.CreateVersion7(agora).ToString() : id!;
        var todos = parametros.Append(("$id", chave)).Append(("$em", Iso(agora))).ToArray();
        if (Executar(conexao, transacao, nova ? inserir : alterar, todos) == 0)
        {
            return ResultadoDoCadastro.Recusado("Registro não encontrado.");
        }

        Registrar(conexao, transacao, agora, quem, null, $"{tipo}.{(nova ? "criar" : "alterar")}", $"{tipo}={chave}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(chave);
    }

    private List<T> Ler<T>(string sql, Func<SqliteDataReader, T> linha)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        var linhas = new List<T>();
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            linhas.Add(linha(l));
        }

        return linhas;
    }
}
