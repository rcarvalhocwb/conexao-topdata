using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Access.Domain.Tempo;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Os campos do formulário de pessoa, pelos nomes que os perfis usam em <c>required_fields</c>.</summary>
public static class CamposDaPessoa
{
    public const string Nome = "nome";
    public const string NomeSocial = "nome_social";
    public const string Documento = "documento";
    public const string Nascimento = "nascimento";
    public const string Telefone = "telefone";
    public const string Email = "email";
    public const string Empresa = "empresa";
    public const string Sala = "sala";
    public const string Anfitriao = "anfitriao";
    public const string Veiculo = "veiculo";
    public const string Responsavel = "responsavel";
    public const string Departamento = "departamento";
    public const string Cargo = "cargo";
    public const string Matricula = "matricula";

    /// <summary>Todos os campos, com o nome que a tela mostra.</summary>
    public static IReadOnlyList<(string Codigo, string Rotulo)> Todos { get; } =
    [
        (Nome, "Nome completo"),
        (NomeSocial, "Nome social"),
        (Documento, "Documento"),
        (Nascimento, "Data de nascimento"),
        (Telefone, "Telefone"),
        (Email, "E-mail"),
        (Empresa, "Empresa"),
        (Sala, "Sala ou unidade"),
        (Anfitriao, "Quem recebe (anfitrião)"),
        (Veiculo, "Veículo (placa)"),
        (Responsavel, "Responsável legal"),
        (Departamento, "Departamento"),
        (Cargo, "Cargo"),
        (Matricula, "Matrícula"),
    ];
}

/// <summary>O que o formulário de pessoa grava. Só o nome e o perfil são sempre obrigatórios; o resto, o perfil decide.</summary>
public sealed record DadosDaPessoa
{
    public string? Id { get; init; }

    public required string PerfilId { get; init; }

    public required string NomeCompleto { get; init; }

    public string? NomeSocial { get; init; }

    /// <summary>cpf, rg, cnh, passaporte, rne ou outro.</summary>
    public string? TipoDoDocumento { get; init; }

    public string? Documento { get; init; }

    public DateOnly? Nascimento { get; init; }

    public string? Telefone { get; init; }

    public string? Email { get; init; }

    public string? Veiculo { get; init; }

    /// <summary>Responsável legal (obrigatório para menor de 18 anos).</summary>
    public string? Responsavel { get; init; }

    public string? EmpresaId { get; init; }

    public string? SalaId { get; init; }

    public string? AnfitriaoId { get; init; }

    public string? Departamento { get; init; }

    public string? Cargo { get; init; }

    public string? Matricula { get; init; }

    public string? Observacao { get; init; }

    public DateTimeOffset? ValidoDe { get; init; }

    public DateTimeOffset? ValidoAte { get; init; }

    /// <summary>Tabela de horário só desta pessoa; nula usa a do perfil.</summary>
    public int? TabelaDeHorario { get; init; }

    /// <summary>Limite de entradas por dia só desta pessoa; nulo usa o do perfil.</summary>
    public int? LimiteDiario { get; init; }

    public bool AtendimentoPrioritario { get; init; }

    /// <summary>Catracas permitidas só para esta pessoa (número do Inner). Vazia usa as do perfil.</summary>
    public IReadOnlyList<int> Catracas { get; init; } = [];
}

/// <summary>Uma credencial da pessoa.</summary>
public sealed record CredencialCadastrada(
    string Id,
    string Tipo,
    string Valor,
    string Situacao,
    string? Motivo,
    DateTimeOffset? ValidoDe,
    DateTimeOffset? ValidoAte,
    DateTimeOffset CriadaEm);

/// <summary>A ficha completa da pessoa, já decifrada.</summary>
public sealed record PessoaCadastrada(
    DadosDaPessoa Dados,
    string Situacao,
    string? MotivoDaSituacao,
    DateTimeOffset CriadaEm,
    DateTimeOffset AtualizadaEm,
    IReadOnlyList<CredencialCadastrada> Credenciais);

/// <summary>Uma linha da lista de pessoas.</summary>
public sealed record ResumoDaPessoa(
    string Id,
    string Nome,
    string Perfil,
    string? Empresa,
    string? Sala,
    string Situacao,
    DateTimeOffset? ValidoAte,
    int Credenciais);

/// <summary>O desfecho de uma gravação: o id gravado, ou os problemas que impediram.</summary>
public sealed record ResultadoDoCadastro(string? Id, IReadOnlyList<string> Problemas)
{
    public bool Gravado => Problemas.Count == 0;

    /// <summary>Gravou, mas vale a pena avisar (por exemplo, mais faixas do que o Inner aceita).</summary>
    public IReadOnlyList<string> Avisos { get; init; } = [];

    public static ResultadoDoCadastro Ok(string id) => new(id, []);

    public static ResultadoDoCadastro Recusado(params string[] problemas) => new(null, problemas);
}

/// <summary>Uma catraca fechada pelo operador.</summary>
public sealed record CatracaFechadaPeloOperador(int Inner, DateTimeOffset Em, string Por, string Motivo);

/// <summary>
/// O cadastro local de pessoas e credenciais (docs/43, ADR-0026). Só local: nada daqui vai para a nuvem.
/// </summary>
/// <remarks>
/// <para>
/// Nome, nome social, documento, nascimento, telefone, e-mail, veículo e responsável são gravados cifrados
/// por campo (<see cref="CifraDeDadosPessoais"/>). Departamento, cargo e matrícula são dado funcional e
/// ficam em claro, como a empresa e a sala.
/// </para>
/// <para>
/// Toda mudança vai para <c>person_event</c> (só-INSERT), com quem fez, sem dado pessoal.
/// </para>
/// </remarks>
public sealed class CadastroDePessoas
{
    /// <summary>Motivo de bloqueio, inativação e fechamento: de 5 a 200 letras.</summary>
    public const int MotivoMinimo = 5;

    /// <summary>Tamanho máximo do motivo.</summary>
    public const int MotivoMaximo = 200;

    /// <summary>Situações da pessoa.</summary>
    public static readonly IReadOnlyList<string> Situacoes = ["ativo", "bloqueado", "inativo"];

    /// <summary>Situações da credencial.</summary>
    public static readonly IReadOnlyList<string> SituacoesDaCredencial = ["ativa", "bloqueada", "perdida", "devolvida"];

    /// <summary>Tipos de credencial.</summary>
    public static readonly IReadOnlyList<string> TiposDeCredencial = ["cartao", "qr", "senha"];

    /// <summary>Tipos de documento.</summary>
    public static readonly IReadOnlyList<string> TiposDeDocumento = ["cpf", "rg", "cnh", "passaporte", "rne", "outro"];

    private readonly SqliteConnectionFactory _fabrica;
    private readonly CifraDeDadosPessoais _cifra;

    public CadastroDePessoas(SqliteConnectionFactory fabrica, CifraDeDadosPessoais cifra)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(cifra);
        _fabrica = fabrica;
        _cifra = cifra;
    }

    /// <summary>Cria ou atualiza uma pessoa, conferindo os campos que o perfil exige.</summary>
    /// <param name="quem">O usuário do sistema que gravou.</param>
    /// <param name="dados">O formulário.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro Gravar(string? quem, DadosDaPessoa dados, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(dados);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var resultado = GravarNa(conexao, transacao, quem, dados, agora);
        if (resultado.Gravado)
        {
            transacao.Commit();
        }

        return resultado;
    }

    /// <summary>O mesmo que <see cref="Gravar"/>, numa transação de quem chama (a importação grava tudo ou nada).</summary>
    internal ResultadoDoCadastro GravarNa(SqliteConnection conexao, SqliteTransaction transacao, string? quem, DadosDaPessoa dados, DateTimeOffset agora)
    {
        var nova = string.IsNullOrWhiteSpace(dados.Id);
        var id = nova ? Guid.CreateVersion7(agora).ToString() : dados.Id!.Trim();
        if (!nova && Escalar(conexao, transacao, "SELECT 1 FROM person WHERE id = $id;", ("$id", id)) is null)
        {
            return ResultadoDoCadastro.Recusado("Pessoa não encontrada.");
        }

        var perfil = Perfil(conexao, transacao, dados.PerfilId);
        if (perfil is null)
        {
            return ResultadoDoCadastro.Recusado("Escolha um perfil ativo.");
        }

        var problemas = Conferir(conexao, transacao, dados, id, perfil.Value, agora);
        var hashDoDocumento = _cifra.ImpressaoDoDocumento(dados.TipoDoDocumento, dados.Documento);
        if (hashDoDocumento is not null
            && Escalar(conexao, transacao, "SELECT 1 FROM person WHERE document_hash = $h AND id <> $id;", ("$h", hashDoDocumento), ("$id", id)) is not null)
        {
            problemas.Add("Já existe uma pessoa com este documento.");
        }

        if (problemas.Count > 0)
        {
            return new ResultadoDoCadastro(null, problemas);
        }

        // Validade padrão do perfil, só na criação: até o fim do dia (Brasília) de hoje + N dias.
        var validoAte = dados.ValidoAte;
        if (nova && validoAte is null && perfil.Value.DiasPadrao is { } dias)
        {
            var hoje = HoraDeBrasilia.NoEvento(agora).Date;
            validoAte = HoraDeBrasilia.DoEvento(hoje.AddDays(dias + 1)).AddSeconds(-1);
        }

        using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText = nova
                ? """
                  INSERT INTO person
                      (id, profile_id, company_id, place_id, host_person_id, valid_from, valid_to, schedule_id, daily_limit,
                       priority_service, full_name_enc, social_name_enc, document_type, document_enc, document_hash,
                       birth_date_enc, phone_enc, email_enc, vehicle_enc, guardian_enc, department, role_title, registration,
                       note, name_search, created_at, created_by, updated_at, updated_by)
                  VALUES
                      ($id, $perfil, $empresa, $sala, $anfitriao, $de, $ate, $horario, $limite,
                       $prioridade, $nome, $social, $tipoDoc, $doc, $hashDoc,
                       $nascimento, $telefone, $email, $veiculo, $responsavel, $departamento, $cargo, $matricula,
                       $nota, $busca, $em, $quem, $em, $quem);
                  """
                : """
                  UPDATE person SET
                      profile_id = $perfil, company_id = $empresa, place_id = $sala, host_person_id = $anfitriao,
                      valid_from = $de, valid_to = $ate, schedule_id = $horario, daily_limit = $limite,
                      priority_service = $prioridade, full_name_enc = $nome, social_name_enc = $social,
                      document_type = $tipoDoc, document_enc = $doc, document_hash = $hashDoc,
                      birth_date_enc = $nascimento, phone_enc = $telefone, email_enc = $email, vehicle_enc = $veiculo,
                      guardian_enc = $responsavel, department = $departamento, role_title = $cargo,
                      registration = $matricula, note = $nota, name_search = $busca, updated_at = $em, updated_by = $quem
                  WHERE id = $id;
                  """;
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$perfil", perfil.Value.Id);
            comando.Parameters.AddWithValue("$empresa", Nulo(dados.EmpresaId));
            comando.Parameters.AddWithValue("$sala", Nulo(dados.SalaId));
            comando.Parameters.AddWithValue("$anfitriao", Nulo(dados.AnfitriaoId));
            comando.Parameters.AddWithValue("$de", (object?)IsoOuNulo(dados.ValidoDe) ?? DBNull.Value);
            comando.Parameters.AddWithValue("$ate", (object?)IsoOuNulo(validoAte) ?? DBNull.Value);
            comando.Parameters.AddWithValue("$horario", (object?)dados.TabelaDeHorario ?? DBNull.Value);
            comando.Parameters.AddWithValue("$limite", (object?)dados.LimiteDiario ?? DBNull.Value);
            comando.Parameters.AddWithValue("$prioridade", dados.AtendimentoPrioritario ? 1 : 0);
            comando.Parameters.AddWithValue("$nome", _cifra.Cifrar(dados.NomeCompleto.Trim(), id)!);
            comando.Parameters.AddWithValue("$social", Cifrado(dados.NomeSocial, id));
            comando.Parameters.AddWithValue("$tipoDoc", Nulo(string.IsNullOrWhiteSpace(dados.Documento) ? null : dados.TipoDoDocumento ?? "outro"));
            comando.Parameters.AddWithValue("$doc", Cifrado(dados.Documento, id));
            comando.Parameters.AddWithValue("$hashDoc", (object?)hashDoDocumento ?? DBNull.Value);
            comando.Parameters.AddWithValue("$nascimento", Cifrado(dados.Nascimento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), id));
            comando.Parameters.AddWithValue("$telefone", Cifrado(dados.Telefone, id));
            comando.Parameters.AddWithValue("$email", Cifrado(dados.Email, id));
            comando.Parameters.AddWithValue("$veiculo", Cifrado(dados.Veiculo, id));
            comando.Parameters.AddWithValue("$responsavel", Cifrado(dados.Responsavel, id));
            comando.Parameters.AddWithValue("$departamento", Nulo(dados.Departamento));
            comando.Parameters.AddWithValue("$cargo", Nulo(dados.Cargo));
            comando.Parameters.AddWithValue("$matricula", Nulo(dados.Matricula));
            comando.Parameters.AddWithValue("$nota", Nulo(dados.Observacao));
            comando.Parameters.AddWithValue("$busca", _cifra.TermosDeBusca(string.Join(' ', dados.NomeCompleto, dados.NomeSocial)));
            comando.Parameters.AddWithValue("$em", Iso(agora));
            comando.Parameters.AddWithValue("$quem", (object?)quem ?? DBNull.Value);
            comando.ExecuteNonQuery();
        }

        Executar(conexao, transacao, "DELETE FROM person_gate WHERE person_id = $id;", ("$id", id));
        foreach (var inner in dados.Catracas.Distinct())
        {
            Executar(conexao, transacao, "INSERT INTO person_gate (person_id, inner_number) VALUES ($id, $inner);", ("$id", id), ("$inner", inner));
        }

        Registrar(conexao, transacao, agora, quem, id, nova ? "pessoa.criar" : "pessoa.alterar", $"perfil={perfil.Value.Id}");
        return ResultadoDoCadastro.Ok(id);
    }

    /// <summary>A ficha da pessoa, decifrada; nula se não existir.</summary>
    /// <exception cref="CryptographicException">A chave do cofre não é a que cifrou os dados.</exception>
    public PessoaCadastrada? Obter(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, profile_id, company_id, place_id, host_person_id, status, status_reason, valid_from, valid_to,
                   schedule_id, daily_limit, priority_service, full_name_enc, social_name_enc, document_type, document_enc,
                   birth_date_enc, phone_enc, email_enc, vehicle_enc, guardian_enc, department, role_title, registration,
                   note, created_at, updated_at
            FROM person WHERE id = $id;
            """;
        comando.Parameters.AddWithValue("$id", id);

        PessoaCadastrada pessoa;
        using (var l = comando.ExecuteReader())
        {
            if (!l.Read())
            {
                return null;
            }

            var nascimento = Decifrar(l, 16, id);
            var dados = new DadosDaPessoa
            {
                Id = id,
                PerfilId = l.GetString(1),
                EmpresaId = Texto(l, 2),
                SalaId = Texto(l, 3),
                AnfitriaoId = Texto(l, 4),
                ValidoDe = Instante(l, 7),
                ValidoAte = Instante(l, 8),
                TabelaDeHorario = l.IsDBNull(9) ? null : l.GetInt32(9),
                LimiteDiario = l.IsDBNull(10) ? null : l.GetInt32(10),
                AtendimentoPrioritario = l.GetInt32(11) == 1,
                NomeCompleto = Decifrar(l, 12, id) ?? string.Empty,
                NomeSocial = Decifrar(l, 13, id),
                TipoDoDocumento = Texto(l, 14),
                Documento = Decifrar(l, 15, id),
                Nascimento = nascimento is null ? null : DateOnly.ParseExact(nascimento, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Telefone = Decifrar(l, 17, id),
                Email = Decifrar(l, 18, id),
                Veiculo = Decifrar(l, 19, id),
                Responsavel = Decifrar(l, 20, id),
                Departamento = Texto(l, 21),
                Cargo = Texto(l, 22),
                Matricula = Texto(l, 23),
                Observacao = Texto(l, 24),
                Catracas = Catracas(conexao, id),
            };
            pessoa = new PessoaCadastrada(dados, l.GetString(5), Texto(l, 6), Instante(l, 25)!.Value, Instante(l, 26)!.Value, Credenciais(conexao, id));
        }

        return pessoa;
    }

    /// <summary>
    /// Procura pessoas pelo nome (todas as partes digitadas, sem acento), pelo documento ou pelo código de
    /// uma credencial. Sem texto, lista as mais recentes.
    /// </summary>
    /// <param name="texto">Nome, documento ou código.</param>
    /// <param name="perfil">Só deste perfil.</param>
    /// <param name="empresa">Só desta empresa.</param>
    /// <param name="situacao">Só nesta situação.</param>
    /// <param name="limite">Quantas linhas, no máximo.</param>
    public IReadOnlyList<ResumoDaPessoa> Buscar(string? texto = null, string? perfil = null, string? empresa = null, string? situacao = null, int limite = 200)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        var filtros = new List<string>();
        var termos = _cifra.TermosDaConsulta(texto);

        if (!string.IsNullOrWhiteSpace(texto))
        {
            // Nome (cada parte), documento de qualquer tipo, ou credencial exata.
            var porNome = termos.Count == 0
                ? "0"
                : string.Join(" AND ", termos.Select((_, i) => $"(' ' || p.name_search || ' ') LIKE $t{i}"));
            var documentos = TiposDeDocumento
                .Select(tipo => _cifra.ImpressaoDoDocumento(tipo, texto))
                .OfType<string>()
                .ToList();
            var porDocumento = documentos.Count == 0
                ? "0"
                : $"p.document_hash IN ({string.Join(", ", documentos.Select((_, i) => $"$d{i}"))})";
            filtros.Add($"(({porNome}) OR {porDocumento} OR EXISTS (SELECT 1 FROM person_credential c WHERE c.person_id = p.id AND c.value_normalized = $codigo))");

            for (var i = 0; i < termos.Count; i++)
            {
                comando.Parameters.AddWithValue($"$t{i}", $"% {termos[i]} %");
            }

            for (var i = 0; i < documentos.Count; i++)
            {
                comando.Parameters.AddWithValue($"$d{i}", documentos[i]);
            }

            comando.Parameters.AddWithValue("$codigo", texto.Trim());
        }

        if (!string.IsNullOrWhiteSpace(perfil))
        {
            filtros.Add("p.profile_id = $perfil");
            comando.Parameters.AddWithValue("$perfil", perfil);
        }

        if (!string.IsNullOrWhiteSpace(empresa))
        {
            filtros.Add("p.company_id = $empresa");
            comando.Parameters.AddWithValue("$empresa", empresa);
        }

        if (!string.IsNullOrWhiteSpace(situacao))
        {
            filtros.Add("p.status = $situacao");
            comando.Parameters.AddWithValue("$situacao", situacao);
        }

        comando.CommandText =
            $"""
            SELECT p.id, p.full_name_enc, pr.name, co.name, pl.name, p.status, p.valid_to,
                   (SELECT COUNT(*) FROM person_credential c WHERE c.person_id = p.id)
            FROM person p
            JOIN person_profile pr ON pr.id = p.profile_id
            LEFT JOIN company co ON co.id = p.company_id
            LEFT JOIN place pl ON pl.id = p.place_id
            {(filtros.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filtros))}
            ORDER BY p.updated_at DESC
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 2000));

        var linhas = new List<ResumoDaPessoa>();
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            var id = l.GetString(0);
            string nome;
            try
            {
                nome = Decifrar(l, 1, id) ?? string.Empty;
            }
            catch (CryptographicException)
            {
                nome = "(nome ilegível: confira o cofre desta máquina)";
            }

            linhas.Add(new ResumoDaPessoa(id, nome, l.GetString(2), Texto(l, 3), Texto(l, 4), l.GetString(5), Instante(l, 6), l.GetInt32(7)));
        }

        return linhas;
    }

    /// <summary>
    /// Muda a situação da pessoa na hora: a leitura seguinte já decide pela situação nova (docs/43 §6.2).
    /// Bloquear e inativar exigem motivo.
    /// </summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="id">A pessoa.</param>
    /// <param name="situacao">ativo, bloqueado ou inativo.</param>
    /// <param name="motivo">Por quê (5 a 200 letras; obrigatório para bloquear e inativar).</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro MudarSituacao(string? quem, string id, string situacao, string? motivo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Situacoes.Contains(situacao))
        {
            return ResultadoDoCadastro.Recusado("Situação desconhecida.");
        }

        motivo = motivo?.Trim();
        if (situacao != "ativo" && ProblemaNoMotivo(motivo) is { } problema)
        {
            return ResultadoDoCadastro.Recusado(problema);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var mudou = Executar(conexao, transacao,
            "UPDATE person SET status = $s, status_reason = $m, updated_at = $em, updated_by = $quem WHERE id = $id;",
            ("$s", situacao), ("$m", Nulo(motivo)), ("$em", Iso(agora)), ("$quem", Nulo(quem)), ("$id", id));
        if (mudou == 0)
        {
            return ResultadoDoCadastro.Recusado("Pessoa não encontrada.");
        }

        Registrar(conexao, transacao, agora, quem, id, "pessoa." + situacao, motivo);
        transacao.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    /// <summary>
    /// Dá uma credencial à pessoa. O código não pode ser de outra pessoa nem de um ingresso: na catraca, o
    /// ingresso é procurado primeiro e venceria.
    /// </summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="pessoaId">A pessoa.</param>
    /// <param name="tipo">cartao, qr ou senha.</param>
    /// <param name="valor">O código como a catraca lê.</param>
    /// <param name="validoDe">Início da validade da credencial.</param>
    /// <param name="validoAte">Fim da validade da credencial.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro AdicionarCredencial(
        string? quem, string pessoaId, string tipo, string valor, DateTimeOffset? validoDe, DateTimeOffset? validoAte, DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var resultado = AdicionarCredencialNa(conexao, transacao, quem, pessoaId, tipo, valor, validoDe, validoAte, agora);
        if (resultado.Gravado)
        {
            transacao.Commit();
        }

        return resultado;
    }

    /// <summary>O mesmo que <see cref="AdicionarCredencial"/>, numa transação de quem chama.</summary>
    internal static ResultadoDoCadastro AdicionarCredencialNa(
        SqliteConnection conexao, SqliteTransaction transacao,
        string? quem, string pessoaId, string tipo, string valor, DateTimeOffset? validoDe, DateTimeOffset? validoAte, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pessoaId);
        var codigo = (valor ?? string.Empty).Trim();
        if (!TiposDeCredencial.Contains(tipo))
        {
            return ResultadoDoCadastro.Recusado("Tipo de credencial desconhecido.");
        }

        if (ProblemaNoCodigo(tipo, codigo) is { } problema)
        {
            return ResultadoDoCadastro.Recusado(problema);
        }

        if (validoDe is { } de && validoAte is { } ate && ate < de)
        {
            return ResultadoDoCadastro.Recusado("O fim da validade vem antes do início.");
        }

        if (Escalar(conexao, transacao, "SELECT 1 FROM person WHERE id = $id;", ("$id", pessoaId)) is null)
        {
            return ResultadoDoCadastro.Recusado("Pessoa não encontrada.");
        }

        if (Escalar(conexao, transacao, "SELECT 1 FROM person_credential WHERE value_normalized = $v;", ("$v", codigo)) is not null)
        {
            return ResultadoDoCadastro.Recusado("Este código já é de outra credencial.");
        }

        if (Escalar(conexao, transacao, "SELECT 1 FROM ticket WHERE qr_normalized = $v;", ("$v", codigo)) is not null)
        {
            return ResultadoDoCadastro.Recusado("Este código já é de um ingresso; na catraca, o ingresso venceria.");
        }

        var id = Guid.CreateVersion7(agora).ToString();
        Executar(conexao, transacao,
            """
            INSERT INTO person_credential (id, person_id, kind, value_normalized, valid_from, valid_to, created_at, updated_at)
            VALUES ($id, $pessoa, $tipo, $valor, $de, $ate, $em, $em);
            """,
            ("$id", id), ("$pessoa", pessoaId), ("$tipo", tipo), ("$valor", codigo),
            ("$de", (object?)IsoOuNulo(validoDe) ?? DBNull.Value), ("$ate", (object?)IsoOuNulo(validoAte) ?? DBNull.Value), ("$em", Iso(agora)));
        Registrar(conexao, transacao, agora, quem, pessoaId, "credencial.adicionar", $"credencial={id}; tipo={tipo}");
        return ResultadoDoCadastro.Ok(id);
    }

    /// <summary>Muda a situação de uma credencial na hora (bloqueada, perdida e devolvida exigem motivo).</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="credencialId">A credencial.</param>
    /// <param name="situacao">ativa, bloqueada, perdida ou devolvida.</param>
    /// <param name="motivo">Por quê.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro MudarCredencial(string? quem, string credencialId, string situacao, string? motivo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credencialId);
        if (!SituacoesDaCredencial.Contains(situacao))
        {
            return ResultadoDoCadastro.Recusado("Situação desconhecida.");
        }

        motivo = motivo?.Trim();
        if (situacao != "ativa" && ProblemaNoMotivo(motivo) is { } problema)
        {
            return ResultadoDoCadastro.Recusado(problema);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        if (Escalar(conexao, transacao, "SELECT person_id FROM person_credential WHERE id = $id;", ("$id", credencialId)) is not string pessoa)
        {
            return ResultadoDoCadastro.Recusado("Credencial não encontrada.");
        }

        Executar(conexao, transacao,
            "UPDATE person_credential SET status = $s, status_reason = $m, updated_at = $em WHERE id = $id;",
            ("$s", situacao), ("$m", Nulo(motivo)), ("$em", Iso(agora)), ("$id", credencialId));
        Registrar(conexao, transacao, agora, quem, pessoa, "credencial." + situacao, $"credencial={credencialId}; {motivo}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(credencialId);
    }

    /// <summary>Fecha a catraca: ninguém passa (motivo <c>CatracaFechada</c>) até reabrir.</summary>
    /// <param name="quem">Quem fechou (login ou nome).</param>
    /// <param name="inner">O número da catraca.</param>
    /// <param name="motivo">Por quê (5 a 200 letras).</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro FecharCatraca(string quem, int inner, string? motivo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quem);
        motivo = motivo?.Trim();
        if (inner is < 1 or > 99)
        {
            return ResultadoDoCadastro.Recusado("Número de catraca fora de 1 a 99.");
        }

        if (ProblemaNoMotivo(motivo) is { } problema)
        {
            return ResultadoDoCadastro.Recusado(problema);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        Executar(conexao, transacao,
            "INSERT OR REPLACE INTO gate_closure (inner_number, closed_at, closed_by, reason) VALUES ($i, $em, $quem, $m);",
            ("$i", inner), ("$em", Iso(agora)), ("$quem", quem), ("$m", motivo!));
        Registrar(conexao, transacao, agora, quem, null, "catraca.fechar", $"inner={inner}; {motivo}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(inner.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Reabre a catraca fechada pelo operador.</summary>
    /// <param name="quem">Quem reabriu.</param>
    /// <param name="inner">O número da catraca.</param>
    /// <param name="motivo">Por quê (5 a 200 letras).</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro AbrirCatraca(string quem, int inner, string? motivo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quem);
        motivo = motivo?.Trim();
        if (ProblemaNoMotivo(motivo) is { } problema)
        {
            return ResultadoDoCadastro.Recusado(problema);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        if (Executar(conexao, transacao, "DELETE FROM gate_closure WHERE inner_number = $i;", ("$i", inner)) == 0)
        {
            return ResultadoDoCadastro.Recusado("Esta catraca não está fechada.");
        }

        Registrar(conexao, transacao, agora, quem, null, "catraca.abrir", $"inner={inner}; {motivo}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(inner.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>As catracas fechadas agora.</summary>
    public IReadOnlyList<CatracaFechadaPeloOperador> CatracasFechadas()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT inner_number, closed_at, closed_by, reason FROM gate_closure ORDER BY inner_number;";
        var linhas = new List<CatracaFechadaPeloOperador>();
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            linhas.Add(new CatracaFechadaPeloOperador(l.GetInt32(0), Instante(l, 1)!.Value, l.GetString(2), l.GetString(3)));
        }

        return linhas;
    }

    /// <summary>Confere o CPF pelos dígitos verificadores.</summary>
    public static bool CpfValido(string? cpf)
    {
        var d = (cpf ?? string.Empty).Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (d.Length != 11 || d.All(x => x == d[0]))
        {
            return false;
        }

        static int Digito(int[] numeros, int tamanho)
        {
            var soma = 0;
            for (var i = 0; i < tamanho; i++)
            {
                soma += numeros[i] * (tamanho + 1 - i);
            }

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(d, 9) == d[9] && Digito(d, 10) == d[10];
    }

    private readonly record struct PerfilDaPessoa(string Id, IReadOnlyList<string> Obrigatorios, bool ExigeAnfitriao, int? DiasPadrao);

    private static PerfilDaPessoa? Perfil(SqliteConnection conexao, SqliteTransaction transacao, string? perfilId)
    {
        if (string.IsNullOrWhiteSpace(perfilId))
        {
            return null;
        }

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT id, required_fields, requires_host, default_days FROM person_profile WHERE id = $id AND status = 'ativo';";
        comando.Parameters.AddWithValue("$id", perfilId);
        using var l = comando.ExecuteReader();
        if (!l.Read())
        {
            return null;
        }

        var obrigatorios = JsonSerializer.Deserialize<string[]>(l.GetString(1)) ?? [];
        return new PerfilDaPessoa(l.GetString(0), obrigatorios, l.GetInt32(2) == 1, l.IsDBNull(3) ? null : l.GetInt32(3));
    }

    private static List<string> Conferir(SqliteConnection conexao, SqliteTransaction transacao, DadosDaPessoa d, string id, PerfilDaPessoa perfil, DateTimeOffset agora)
    {
        var problemas = new List<string>();
        var nome = d.NomeCompleto?.Trim() ?? string.Empty;
        if (nome.Length is < 3 or > 150)
        {
            problemas.Add("Nome completo: de 3 a 150 letras.");
        }

        var preenchido = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [CamposDaPessoa.Nome] = nome.Length > 0,
            [CamposDaPessoa.NomeSocial] = !string.IsNullOrWhiteSpace(d.NomeSocial),
            [CamposDaPessoa.Documento] = !string.IsNullOrWhiteSpace(d.Documento),
            [CamposDaPessoa.Nascimento] = d.Nascimento is not null,
            [CamposDaPessoa.Telefone] = !string.IsNullOrWhiteSpace(d.Telefone),
            [CamposDaPessoa.Email] = !string.IsNullOrWhiteSpace(d.Email),
            [CamposDaPessoa.Empresa] = !string.IsNullOrWhiteSpace(d.EmpresaId),
            [CamposDaPessoa.Sala] = !string.IsNullOrWhiteSpace(d.SalaId),
            [CamposDaPessoa.Anfitriao] = !string.IsNullOrWhiteSpace(d.AnfitriaoId),
            [CamposDaPessoa.Veiculo] = !string.IsNullOrWhiteSpace(d.Veiculo),
            [CamposDaPessoa.Responsavel] = !string.IsNullOrWhiteSpace(d.Responsavel),
            [CamposDaPessoa.Departamento] = !string.IsNullOrWhiteSpace(d.Departamento),
            [CamposDaPessoa.Cargo] = !string.IsNullOrWhiteSpace(d.Cargo),
            [CamposDaPessoa.Matricula] = !string.IsNullOrWhiteSpace(d.Matricula),
        };

        var obrigatorios = perfil.ExigeAnfitriao ? perfil.Obrigatorios.Append(CamposDaPessoa.Anfitriao) : perfil.Obrigatorios;
        foreach (var campo in obrigatorios.Distinct(StringComparer.Ordinal))
        {
            if (preenchido.TryGetValue(campo, out var tem) && !tem)
            {
                var rotulo = CamposDaPessoa.Todos.First(c => c.Codigo == campo).Rotulo;
                problemas.Add($"{rotulo}: obrigatório para este perfil.");
            }
        }

        if (!string.IsNullOrWhiteSpace(d.Documento))
        {
            if (d.TipoDoDocumento is not null && !TiposDeDocumento.Contains(d.TipoDoDocumento))
            {
                problemas.Add("Tipo de documento desconhecido.");
            }
            else if (d.TipoDoDocumento == "cpf" && !CpfValido(d.Documento))
            {
                problemas.Add("CPF inválido (confira os dígitos).");
            }
        }

        if (!string.IsNullOrWhiteSpace(d.Email) && (!d.Email.Contains('@', StringComparison.Ordinal) || d.Email.Trim().Length > 120))
        {
            problemas.Add("E-mail inválido.");
        }

        if (d.Nascimento is { } nascimento)
        {
            var hoje = DateOnly.FromDateTime(HoraDeBrasilia.NoEvento(agora).Date);
            if (nascimento > hoje || nascimento.Year < 1900)
            {
                problemas.Add("Data de nascimento inválida.");
            }
            else if (nascimento.AddYears(18) > hoje && string.IsNullOrWhiteSpace(d.Responsavel))
            {
                problemas.Add("Menor de 18 anos: informe o responsável legal.");
            }
        }

        if (d.Observacao is { Length: > 300 })
        {
            problemas.Add("Observação: até 300 letras.");
        }

        if (d.ValidoDe is { } de && d.ValidoAte is { } ate && ate < de)
        {
            problemas.Add("O fim da validade vem antes do início.");
        }

        if (d.LimiteDiario is < 1 or > 100)
        {
            problemas.Add("Limite por dia: de 1 a 100.");
        }

        if (d.Catracas.Any(c => c is < 1 or > 99))
        {
            problemas.Add("Catraca fora de 1 a 99.");
        }

        Existe(conexao, transacao, problemas, "SELECT 1 FROM company WHERE id = $v AND status = 'ativa';", d.EmpresaId, "Empresa não encontrada ou inativa.");
        Existe(conexao, transacao, problemas, "SELECT 1 FROM place WHERE id = $v AND status = 'ativo';", d.SalaId, "Sala não encontrada ou inativa.");
        Existe(conexao, transacao, problemas, "SELECT 1 FROM time_schedule WHERE id = $v;", d.TabelaDeHorario?.ToString(CultureInfo.InvariantCulture), "Tabela de horário não encontrada.");
        if (!string.IsNullOrWhiteSpace(d.AnfitriaoId))
        {
            if (d.AnfitriaoId == id)
            {
                problemas.Add("A pessoa não pode ser anfitriã de si mesma.");
            }
            else
            {
                Existe(conexao, transacao, problemas, "SELECT 1 FROM person WHERE id = $v AND status = 'ativo';", d.AnfitriaoId, "Anfitrião não encontrado ou não está ativo.");
            }
        }

        return problemas;
    }

    private static void Existe(SqliteConnection conexao, SqliteTransaction transacao, List<string> problemas, string sql, string? valor, string problema)
    {
        if (!string.IsNullOrWhiteSpace(valor) && Escalar(conexao, transacao, sql, ("$v", valor)) is null)
        {
            problemas.Add(problema);
        }
    }

    private static string? ProblemaNoMotivo(string? motivo) =>
        motivo is null || motivo.Length < MotivoMinimo || motivo.Length > MotivoMaximo
            ? $"Informe o motivo (de {MotivoMinimo} a {MotivoMaximo} letras)."
            : null;

    private static string? ProblemaNoCodigo(string tipo, string codigo) => tipo switch
    {
        _ when codigo.Length == 0 => "Informe o código.",
        _ when codigo.Length > 128 => "Código longo demais (até 128 caracteres).",
        "senha" when codigo.Length is < 4 or > 12 || !codigo.All(char.IsAsciiDigit) => "Senha de teclado: de 4 a 12 números.",
        "cartao" when !codigo.All(char.IsAsciiLetterOrDigit) => "Número do cartão: só letras e números, como a catraca lê.",
        _ => null,
    };

    private static List<CredencialCadastrada> Credenciais(SqliteConnection conexao, string pessoaId)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, kind, value_normalized, status, status_reason, valid_from, valid_to, created_at
            FROM person_credential WHERE person_id = $id ORDER BY created_at;
            """;
        comando.Parameters.AddWithValue("$id", pessoaId);
        var linhas = new List<CredencialCadastrada>();
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            linhas.Add(new CredencialCadastrada(l.GetString(0), l.GetString(1), l.GetString(2), l.GetString(3), Texto(l, 4), Instante(l, 5), Instante(l, 6), Instante(l, 7)!.Value));
        }

        return linhas;
    }

    private static List<int> Catracas(SqliteConnection conexao, string pessoaId)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT inner_number FROM person_gate WHERE person_id = $id ORDER BY inner_number;";
        comando.Parameters.AddWithValue("$id", pessoaId);
        var linhas = new List<int>();
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            linhas.Add(l.GetInt32(0));
        }

        return linhas;
    }

    private string? Decifrar(SqliteDataReader leitor, int coluna, string id) =>
        leitor.IsDBNull(coluna) ? null : _cifra.Decifrar((byte[])leitor.GetValue(coluna), id);

    private object Cifrado(string? valor, string id) =>
        string.IsNullOrWhiteSpace(valor) ? DBNull.Value : _cifra.Cifrar(valor.Trim(), id)!;

    internal static void Registrar(SqliteConnection conexao, SqliteTransaction transacao, DateTimeOffset agora, string? quem, string? pessoa, string acao, string? detalhe) =>
        Executar(conexao, transacao,
            "INSERT INTO person_event (at, actor_id, person_id, action, detail) VALUES ($em, $quem, $pessoa, $acao, $detalhe);",
            ("$em", Iso(agora)), ("$quem", Nulo(quem)), ("$pessoa", Nulo(pessoa)), ("$acao", acao), ("$detalhe", Nulo(detalhe)));

    internal static int Executar(SqliteConnection conexao, SqliteTransaction transacao, string sql, params (string Nome, object Valor)[] parametros)
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

    internal static object? Escalar(SqliteConnection conexao, SqliteTransaction transacao, string sql, params (string Nome, object Valor)[] parametros)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nome, valor);
        }

        return comando.ExecuteScalar();
    }

    internal static object Nulo(string? valor) => string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();

    internal static string? Texto(SqliteDataReader leitor, int coluna) => leitor.IsDBNull(coluna) ? null : leitor.GetString(coluna);

    internal static DateTimeOffset? Instante(SqliteDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : DateTimeOffset.Parse(leitor.GetString(coluna), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    internal static string Iso(DateTimeOffset valor) => valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? IsoOuNulo(DateTimeOffset? valor) => valor is { } v ? Iso(v) : null;
}
