using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using static Access.Infrastructure.SQLite.CadastroDePessoas;

namespace Access.Infrastructure.SQLite;

/// <summary>Uma visita local; os dados pessoais são decifrados só para o serviço autorizado.</summary>
public sealed record VisitaCadastrada(
    string Id, string? PessoaId, string? AnfitriaoId, string Nome, string Anfitriao, string Motivo,
    DateTimeOffset De, DateTimeOffset Ate, DateTimeOffset? Chegada, DateTimeOffset? Saida,
    string? ValorDaCredencial, string? Documento, string Situacao);

/// <summary>Uma pessoa ativa que pode receber visitas.</summary>
public sealed record AnfitriaoDeVisita(string Id, string Nome);

/// <summary>
/// P6: pré-cadastro sem documento, chegada conferida e saída. Pessoa, credencial, visita e trilha
/// mudam na mesma transação; agendar nunca libera acesso. Tudo permanece neste computador.
/// </summary>
public sealed class Visitas
{
    private readonly SqliteConnectionFactory _fabrica;
    private readonly CifraDeDadosPessoais _cifra;
    private readonly CadastroDePessoas _pessoas;

    public Visitas(SqliteConnectionFactory fabrica, CifraDeDadosPessoais cifra)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(cifra);
        _fabrica = fabrica;
        _cifra = cifra;
        _pessoas = new CadastroDePessoas(fabrica, cifra);
    }

    public ResultadoDoCadastro Agendar(string? quem, string nome, string anfitriao, string motivo,
        DateTimeOffset de, DateTimeOffset ate, DateTimeOffset agora)
    {
        nome = nome.Trim();
        motivo = motivo.Trim();
        if (nome.Length is < 3 or > 150)
        {
            return ResultadoDoCadastro.Recusado("Nome completo: de 3 a 150 letras.");
        }

        if (motivo.Length is < 3 or > 200)
        {
            return ResultadoDoCadastro.Recusado("Motivo da visita: de 3 a 200 letras.");
        }

        if (ate <= de || ate <= agora)
        {
            return ResultadoDoCadastro.Recusado("Informe uma janela com fim depois do início e ainda não encerrada.");
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        if (!AnfitriaoAtivo(conexao, transacao, anfitriao))
        {
            return ResultadoDoCadastro.Recusado("Anfitrião não encontrado ou não está ativo.");
        }

        var id = Guid.CreateVersion7(agora).ToString();
        Executar(conexao, transacao,
            """
            INSERT INTO visit (id, host_person_id, visitor_name_enc, reason_enc, scheduled_from, scheduled_to, created_at, created_by)
            VALUES ($id, $host, $nome, $motivo, $de, $ate, $em, $quem);
            """,
            ("$id", id), ("$host", anfitriao), ("$nome", _cifra.Cifrar(nome, id)!),
            ("$motivo", _cifra.Cifrar(motivo, id)!), ("$de", Iso(de)), ("$ate", Iso(ate)),
            ("$em", Iso(agora)), ("$quem", Nulo(quem)));
        Registrar(conexao, transacao, agora, quem, anfitriao, "visita.agendar", "visita=" + id);
        transacao.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    public ResultadoDoCadastro Receber(string? quem, string id, string tipoDocumento, string documento,
        bool documentoConferido, string tipoCredencial, string codigo, DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var visita = Ler(conexao, transacao, id, agora);
        if (visita is null)
        {
            return ResultadoDoCadastro.Recusado("Visita não encontrada.");
        }

        if (visita.Chegada is not null || visita.Saida is not null)
        {
            return ResultadoDoCadastro.Recusado("A chegada desta visita já foi registrada.");
        }

        if (agora < visita.De || agora > visita.Ate)
        {
            return ResultadoDoCadastro.Recusado("A chegada precisa ocorrer dentro da janela agendada.");
        }

        if (!AnfitriaoAtivo(conexao, transacao, visita.AnfitriaoId))
        {
            return ResultadoDoCadastro.Recusado("Anfitrião não encontrado ou não está ativo.");
        }

        var campos = Escalar(conexao, transacao, "SELECT required_fields FROM person_profile WHERE id = 'visitante' AND status = 'ativo';") as string;
        if (campos is null)
        {
            return ResultadoDoCadastro.Recusado("Ative o perfil Visitante em Perfis e horários.");
        }

        documento = documento.Trim();
        var exigeDocumento = (JsonSerializer.Deserialize<string[]>(campos) ?? []).Contains(CamposDaPessoa.Documento);
        if (exigeDocumento && documento.Length == 0)
        {
            return ResultadoDoCadastro.Recusado("Documento: obrigatório para o perfil Visitante.");
        }

        if (documento.Length > 80 || (documento.Length > 0 && (!TiposDeDocumento.Contains(tipoDocumento)
            || (tipoDocumento == "cpf" && !CpfValido(documento)))))
        {
            return ResultadoDoCadastro.Recusado("Documento inválido: confira o tipo, o número e os dígitos do CPF.");
        }

        if ((exigeDocumento || documento.Length > 0) && !documentoConferido)
        {
            return ResultadoDoCadastro.Recusado("Confira o documento apresentado antes de entregar a credencial.");
        }

        // Documento repetido identifica um visitante que voltou. Nunca muda o perfil nem reativa
        // pessoa bloqueada/inativa. Outra visita ainda em curso impede dar duas credenciais.
        var hash = _cifra.ImpressaoDoDocumento(tipoDocumento, documento);
        var pessoa = hash is null ? null : Escalar(conexao, transacao,
            "SELECT id FROM person WHERE document_hash = $hash;", ("$hash", hash)) as string;
        var nome = _cifra.Decifrar(Blob(conexao, transacao, "SELECT visitor_name_enc FROM visit WHERE id = $id;", id), id)!;
        if (pessoa is not null)
        {
            if (Escalar(conexao, transacao, "SELECT 1 FROM person WHERE id = $id AND profile_id = 'visitante' AND status = 'ativo';", ("$id", pessoa)) is null)
            {
                return ResultadoDoCadastro.Recusado("Documento já cadastrado: a pessoa precisa estar ativa no perfil Visitante. Confira em Pessoas.");
            }

            if (pessoa == visita.AnfitriaoId || Escalar(conexao, transacao,
                "SELECT 1 FROM visit WHERE visitor_person_id = $pessoa AND arrived_at IS NOT NULL AND departed_at IS NULL AND scheduled_to >= $em;",
                ("$pessoa", pessoa), ("$em", Iso(agora))) is not null)
            {
                return ResultadoDoCadastro.Recusado("Esta pessoa já tem visita em curso ou é o próprio anfitrião.");
            }

            // A ficha identificada pelo documento conserva o nome e os demais dados já cadastrados.
            var atual = _pessoas.ObterNa(conexao, transacao, pessoa)!.Dados;
            nome = atual.NomeCompleto;
            var atualizada = _pessoas.GravarNa(conexao, transacao, quem, atual with
            {
                AnfitriaoId = visita.AnfitriaoId, ValidoDe = visita.De, ValidoAte = visita.Ate,
            }, agora);
            if (!atualizada.Gravado) { return atualizada; }
        }
        else
        {
            var criada = _pessoas.GravarNa(conexao, transacao, quem, new DadosDaPessoa
            {
                PerfilId = "visitante", NomeCompleto = nome, AnfitriaoId = visita.AnfitriaoId,
                TipoDoDocumento = documento.Length > 0 ? tipoDocumento : null, Documento = documento,
                ValidoDe = visita.De, ValidoAte = visita.Ate,
            }, agora);
            if (!criada.Gravado)
            {
                return criada;
            }

            pessoa = criada.Id!;
        }

        codigo = codigo.Trim();
        var credencial = Escalar(conexao, transacao, "SELECT id FROM person_credential WHERE value_normalized = $v;", ("$v", codigo)) as string;
        if (credencial is not null)
        {
            // Reutilizar só após a saída, só credencial provisória e nunca perdida/bloqueada.
            if (Escalar(conexao, transacao,
                """
                SELECT 1 FROM person_credential c JOIN visit v ON v.id = c.visit_id
                WHERE c.id = $id AND c.kind = $tipo AND c.status IN ('ativa', 'devolvida') AND v.departed_at IS NOT NULL;
                """, ("$id", credencial), ("$tipo", tipoCredencial)) is null)
            {
                return ResultadoDoCadastro.Recusado("Credencial indisponível. Use outra ou registre a saída da visita anterior; credencial pessoal, perdida ou bloqueada não é reutilizada.");
            }

            if (Escalar(conexao, transacao, "SELECT 1 FROM ticket WHERE qr_normalized = $v;", ("$v", codigo)) is not null)
            {
                return ResultadoDoCadastro.Recusado("Este código já é de um ingresso.");
            }

            Executar(conexao, transacao,
                """
                UPDATE person_credential SET person_id = $pessoa, status = 'ativa', status_reason = NULL,
                    valid_from = $de, valid_to = $ate, updated_at = $em, visit_id = $visita WHERE id = $id;
                """, ("$pessoa", pessoa), ("$de", Iso(agora)), ("$ate", Iso(visita.Ate)),
                ("$em", Iso(agora)), ("$visita", id), ("$id", credencial));
        }
        else
        {
            var criada = AdicionarCredencialNa(conexao, transacao, quem, pessoa, tipoCredencial, codigo, agora, visita.Ate, agora);
            if (!criada.Gravado)
            {
                return criada;
            }

            credencial = criada.Id!;
            Executar(conexao, transacao, "UPDATE person_credential SET visit_id = $visita WHERE id = $id;", ("$visita", id), ("$id", credencial));
        }

        Executar(conexao, transacao,
            """
            UPDATE visit SET visitor_person_id = $pessoa, visitor_name_enc = $nome, arrived_at = $em,
                arrived_by = $quem, credential_id = $credencial WHERE id = $id;
            """, ("$pessoa", pessoa), ("$nome", _cifra.Cifrar(nome, id)!), ("$em", Iso(agora)),
            ("$quem", Nulo(quem)), ("$credencial", credencial), ("$id", id));
        Registrar(conexao, transacao, agora, quem, pessoa, "visita.chegada", $"visita={id}; credencial={credencial}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    public ResultadoDoCadastro Encerrar(string? quem, string id, DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var pessoa = Escalar(conexao, transacao,
            "SELECT visitor_person_id FROM visit WHERE id = $id AND arrived_at IS NOT NULL AND departed_at IS NULL;", ("$id", id)) as string;
        if (pessoa is null)
        {
            return ResultadoDoCadastro.Recusado("Visita não encontrada, sem chegada ou já encerrada.");
        }

        Executar(conexao, transacao, "UPDATE visit SET departed_at = $em, departed_by = $quem WHERE id = $id;",
            ("$em", Iso(agora)), ("$quem", Nulo(quem)), ("$id", id));
        Executar(conexao, transacao,
            "UPDATE person_credential SET status = 'devolvida', status_reason = 'Visita encerrada', updated_at = $em WHERE visit_id = $id;",
            ("$em", Iso(agora)), ("$id", id));
        Registrar(conexao, transacao, agora, quem, pessoa, "visita.saida", "visita=" + id);
        transacao.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    public IReadOnlyList<VisitaCadastrada> Listar(DateTimeOffset agora, string? situacao = null, int limite = 200)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = Consulta +
            """
             WHERE ($situacao = '' OR CASE WHEN v.departed_at IS NOT NULL THEN 'encerrada'
                 WHEN v.scheduled_to < $em THEN 'expirada' WHEN v.arrived_at IS NOT NULL THEN 'em_visita' ELSE 'agendada' END = $situacao)
             ORDER BY v.scheduled_from DESC, v.id DESC LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$situacao", situacao ?? string.Empty);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 500));
        using var l = comando.ExecuteReader();
        var resultado = new List<VisitaCadastrada>();
        while (l.Read())
        {
            resultado.Add(Converter(l, agora));
        }

        return resultado;
    }

    public IReadOnlyList<AnfitriaoDeVisita> Anfitrioes(string? texto = null, int limite = 200)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        var termos = _cifra.TermosDaConsulta(texto);
        comando.CommandText = "SELECT id, full_name_enc FROM person WHERE status = 'ativo'"
            + string.Concat(termos.Select((_, i) => $" AND name_search LIKE $t{i}"))
            + " ORDER BY id LIMIT $limite;";
        for (var i = 0; i < termos.Count; i++)
        {
            comando.Parameters.AddWithValue($"$t{i}", "%" + termos[i] + "%");
        }
        comando.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 500));
        using var l = comando.ExecuteReader();
        var resultado = new List<AnfitriaoDeVisita>();
        while (l.Read())
        {
            resultado.Add(new AnfitriaoDeVisita(l.GetString(0), Seguro((byte[])l.GetValue(1), l.GetString(0), "(nome ilegível)")));
        }

        return resultado;
    }

    private const string Consulta =
        """
        SELECT v.id, v.visitor_person_id, v.host_person_id, v.visitor_name_enc, p.full_name_enc,
               v.reason_enc, v.scheduled_from, v.scheduled_to, v.arrived_at, v.departed_at,
               c.value_normalized, g.document_enc
        FROM visit v LEFT JOIN person p ON p.id = v.host_person_id
        LEFT JOIN person g ON g.id = v.visitor_person_id
        LEFT JOIN person_credential c ON c.id = v.credential_id
        """;

    private VisitaCadastrada? Ler(SqliteConnection c, SqliteTransaction t, string id, DateTimeOffset agora)
    {
        using var comando = c.CreateCommand();
        comando.Transaction = t;
        comando.CommandText = Consulta + " WHERE v.id = $id;";
        comando.Parameters.AddWithValue("$id", id);
        using var l = comando.ExecuteReader();
        return l.Read() ? Converter(l, agora) : null;
    }

    private VisitaCadastrada Converter(SqliteDataReader l, DateTimeOffset agora)
    {
        var id = l.GetString(0);
        var pessoa = Texto(l, 1);
        var host = Texto(l, 2);
        var ate = Instante(l, 7)!.Value;
        var chegada = Instante(l, 8);
        var saida = Instante(l, 9);
        return new VisitaCadastrada(id, pessoa, host,
            Seguro((byte[])l.GetValue(3), id, "(nome ilegível)"),
            host is null || l.IsDBNull(4) ? "(anfitrião indisponível)" : Seguro((byte[])l.GetValue(4), host, "(nome ilegível)"),
            Seguro((byte[])l.GetValue(5), id, "(motivo ilegível)"), Instante(l, 6)!.Value, ate, chegada, saida,
            Texto(l, 10), pessoa is null || l.IsDBNull(11) ? null : Seguro((byte[])l.GetValue(11), pessoa, "(documento ilegível)"),
            saida is not null ? "encerrada" : agora > ate ? "expirada" : chegada is not null ? "em_visita" : "agendada");
    }

    private string Seguro(byte[] cifrado, string id, string ilegivel)
    {
        try
        {
            return _cifra.Decifrar(cifrado, id) ?? string.Empty;
        }
        catch (CryptographicException)
        {
            return ilegivel;
        }
    }

    private static bool AnfitriaoAtivo(SqliteConnection c, SqliteTransaction t, string? id) => id is not null
        && Escalar(c, t, "SELECT 1 FROM person WHERE id = $id AND status = 'ativo';", ("$id", id)) is not null;

    private static byte[]? Blob(SqliteConnection c, SqliteTransaction t, string sql, string id) =>
        Escalar(c, t, sql, ("$id", id)) as byte[];
}
