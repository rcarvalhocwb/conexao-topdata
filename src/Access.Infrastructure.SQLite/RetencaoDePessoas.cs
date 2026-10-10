using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using static Access.Infrastructure.SQLite.CadastroDePessoas;

namespace Access.Infrastructure.SQLite;

/// <summary>Direitos do titular e retenção local (docs/43 P7). Apagar não depende da chave do cofre.</summary>
public sealed class RetencaoDePessoas(SqliteConnectionFactory fabrica, CifraDeDadosPessoais? cifra = null)
{
    public const int TamanhoDoLote = 100;
    public const int TamanhoDoBloco = 64 * 1024;
    private readonly SqliteConnectionFactory _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
    private readonly CifraDeDadosPessoais? _cifra = cifra;

    /// <summary>Apaga até 100 vencidos. Reconfere situação e prazo na transação; nunca apaga ativos.</summary>
    public int LimparVencidos(DateTimeOffset agora)
    {
        using var con = _fabrica.Abrir();
        var ids = new List<string>();
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = """
                SELECT p.id, p.inactivated_at, f.retention_days FROM person p
                JOIN person_profile f ON f.id = p.profile_id WHERE p.status = 'inativo'
                  AND julianday(p.inactivated_at) + f.retention_days <= julianday($em) + 0.0000001
                ORDER BY p.inactivated_at, p.id;
                """;
            cmd.Parameters.AddWithValue("$em", Iso(agora));
            using var r = cmd.ExecuteReader();
            while (r.Read() && ids.Count < TamanhoDoLote)
            {
                if (Venceu(r.GetString(1), r.GetInt32(2), agora)) ids.Add(r.GetString(0));
            }
        }

        using var tx = con.BeginTransaction();
        var apagadas = 0;
        foreach (var id in ids)
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT p.inactivated_at, f.retention_days FROM person p JOIN person_profile f ON f.id = p.profile_id
                WHERE p.id = $id AND p.status = 'inativo';
                """;
            cmd.Parameters.AddWithValue("$id", id);
            bool venceu;
            using (var r = cmd.ExecuteReader()) venceu = r.Read() && !r.IsDBNull(0) && Venceu(r.GetString(0), r.GetInt32(1), agora);
            if (venceu)
            {
                ApagarVisitasDoTitularSePresentes(con, tx, id);
                apagadas += Executar(con, tx, "DELETE FROM person WHERE id = $id;", ("$id", id));
            }
        }

        Registrar(con, tx, agora, null, null, "retencao.limpar", $"pessoas={apagadas}");
        tx.Commit();
        return apagadas;
    }

    /// <summary>Exclusão explícita, atômica e idempotente. Os gatilhos removem todos os vínculos da passagem.</summary>
    public ResultadoDoCadastro Excluir(string? quem, string id, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(id)) return ResultadoDoCadastro.Recusado("Escolha a pessoa.");
        using var con = _fabrica.Abrir();
        using var tx = con.BeginTransaction();
        var passagens = Convert.ToInt32(Escalar(con, tx, "SELECT COUNT(*) FROM ticket_use_attempt WHERE person_id = $id;", ("$id", id)), CultureInfo.InvariantCulture);
        ApagarVisitasDoTitularSePresentes(con, tx, id);
        var apagadas = Executar(con, tx, "DELETE FROM person WHERE id = $id;", ("$id", id));
        // Não recoloca o identificador apagado na auditoria. Só autoria e contagens.
        Registrar(con, tx, agora, quem, null, "titular.excluir", $"pessoas={apagadas}; passagens={passagens}");
        tx.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    /// <summary>JSON UTF-8 legível, em blocos, com uma fotografia consistente da base e sem limite de linhas.</summary>
    /// <remarks>Quem chama deve descartar os blocos se a enumeração falhar ou for cancelada.</remarks>
    public IEnumerable<byte[]> Exportar(string? quem, string id, DateTimeOffset agora, CancellationToken cancelamento = default)
    {
        if (_cifra is null) throw new InvalidOperationException("Chave dos dados pessoais indisponível.");
        using var con = _fabrica.Abrir();
        using var tx = con.BeginTransaction(deferred: true);
        if (Escalar(con, tx, "SELECT 1 FROM person WHERE id = $id;", ("$id", id)) is null)
            throw new KeyNotFoundException("Pessoa não encontrada.");
        using var buffer = new MemoryStream();
        using var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartObject();
        json.WriteString("formato", "Rayzer XAcess — dados do titular v1");
        json.WriteString("exportado_em", Iso(agora));
        var temVisitas = TemVisitas(con, tx);
        foreach (var (nome, sql) in temVisitas ? Secoes.Concat(SecoesDeVisitas) : Secoes)
        {
            json.WriteStartArray(nome);
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                cancelamento.ThrowIfCancellationRequested();
                json.WriteStartObject();
                for (var i = 0; i < r.FieldCount; i++)
                {
                    var campo = r.GetName(i);
                    var cifrado = campo.EndsWith("_enc", StringComparison.Ordinal);
                    json.WritePropertyName(cifrado ? campo[..^4] : campo);
                    if (r.IsDBNull(i)) json.WriteNullValue();
                    else if (cifrado) json.WriteStringValue(_cifra.Decifrar((byte[])r.GetValue(i), r["id"].ToString()!));
                    else if (r.GetValue(i) is long n) json.WriteNumberValue(n);
                    else if (r.GetValue(i) is byte[] b) json.WriteStringValue(Encoding.UTF8.GetString(b));
                    else json.WriteStringValue(r.GetString(i));
                }
                json.WriteEndObject();
                json.Flush();
                if (buffer.Length >= TamanhoDoBloco)
                    foreach (var bloco in Esvaziar(buffer)) yield return bloco;
            }
            json.WriteEndArray();
        }
        json.WriteEndObject();
        json.Flush();
        foreach (var bloco in Esvaziar(buffer)) yield return bloco;
        cancelamento.ThrowIfCancellationRequested();
        // Termina a leitura antes de adquirir o bloqueio de escrita (WAL).
        tx.Commit();
        using var registro = con.BeginTransaction();
        Registrar(con, registro, agora, quem, null, "titular.exportar", "pessoas=1");
        registro.Commit();
    }

    public ResultadoDoCadastro GravarTermo(string? quem, string versao, string texto, DateTimeOffset agora)
    {
        versao = versao.Trim();
        if (versao.Length is < 1 or > 60 || texto.Length is < 1 or > 100000 || string.IsNullOrWhiteSpace(texto))
            return ResultadoDoCadastro.Recusado("Informe a versão (até 60 letras) e o texto do termo (até 100 mil letras).");
        using var con = _fabrica.Abrir();
        using var tx = con.BeginTransaction();
        var anterior = Escalar(con, tx, "SELECT text FROM consent_term WHERE version = $v;", ("$v", versao));
        if (anterior is not null)
            return anterior.Equals(texto) ? ResultadoDoCadastro.Ok(versao) : ResultadoDoCadastro.Recusado("Esta versão já existe com outro texto. Crie uma nova versão.");
        Executar(con, tx, "INSERT INTO consent_term VALUES ($v, $t, $h, $em, $quem);",
            ("$v", versao), ("$t", texto), ("$h", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))), ("$em", Iso(agora)), ("$quem", Nulo(quem)));
        Registrar(con, tx, agora, quem, null, "termo.criar", $"versao={versao}");
        tx.Commit();
        return ResultadoDoCadastro.Ok(versao);
    }

    /// <summary>Registra um aceite informado explicitamente; nunca infere consentimento do cadastro.</summary>
    public ResultadoDoCadastro RegistrarAceite(string? quem, string id, string versao, DateTimeOffset aceitoEm, DateTimeOffset agora)
    {
        if (aceitoEm > agora) return ResultadoDoCadastro.Recusado("O aceite não pode estar no futuro.");
        using var con = _fabrica.Abrir();
        using var tx = con.BeginTransaction();
        if (Escalar(con, tx, "SELECT 1 FROM person WHERE id = $id;", ("$id", id)) is null
            || Escalar(con, tx, "SELECT 1 FROM consent_term WHERE version = $v;", ("$v", versao)) is null)
            return ResultadoDoCadastro.Recusado("Pessoa ou versão do termo não encontrada.");
        // Repetir uma operação não substitui a evidência do primeiro aceite.
        var gravadas = Executar(con, tx, "INSERT OR IGNORE INTO person_consent VALUES ($id, $v, $aceite, $em, $quem);",
            ("$id", id), ("$v", versao), ("$aceite", Iso(aceitoEm)), ("$em", Iso(agora)), ("$quem", Nulo(quem)));
        if (gravadas > 0) Registrar(con, tx, agora, quem, id, "termo.aceitar", $"versao={versao}");
        tx.Commit();
        return ResultadoDoCadastro.Ok(id);
    }

    private static bool Venceu(string inicio, int dias, DateTimeOffset agora) =>
        DateTimeOffset.Parse(inicio, CultureInfo.InvariantCulture).AddDays(dias) <= agora;

    // Compatibilidade com a P6 (PR #17), ainda independente: a tabela pode não existir.
    // Quando existir, não deixa uma cópia cifrada do nome/motivo depois de apagar o titular.
    private static bool TemVisitas(SqliteConnection con, SqliteTransaction tx) =>
        Escalar(con, tx, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'visit';") is not null;
    private static void ApagarVisitasDoTitularSePresentes(SqliteConnection con, SqliteTransaction tx, string id)
    {
        if (TemVisitas(con, tx)) Executar(con, tx, "DELETE FROM visit WHERE visitor_person_id = $id;", ("$id", id));
    }

    private static readonly (string Nome, string Sql)[] SecoesDeVisitas =
    [
        ("visitas_do_titular", "SELECT * FROM visit WHERE visitor_person_id = $id ORDER BY scheduled_from, id;"),
        // Não exporta o nome nem o motivo de terceiros recebidos pelo titular.
        ("visitas_recebidas", "SELECT id, host_person_id, scheduled_from, scheduled_to, arrived_at, departed_at FROM visit WHERE host_person_id = $id ORDER BY scheduled_from, id;"),
    ];

    private static IEnumerable<byte[]> Esvaziar(MemoryStream buffer)
    {
        var bytes = buffer.ToArray();
        buffer.SetLength(0);
        buffer.Position = 0;
        for (var i = 0; i < bytes.Length; i += TamanhoDoBloco)
            yield return bytes.AsSpan(i, Math.Min(TamanhoDoBloco, bytes.Length - i)).ToArray();
    }

    private static readonly (string Nome, string Sql)[] Secoes =
    [
        ("pessoa", "SELECT * FROM person WHERE id = $id;"),
        ("perfil", "SELECT f.* FROM person_profile f JOIN person p ON p.profile_id = f.id WHERE p.id = $id;"),
        ("empresa", "SELECT c.* FROM company c JOIN person p ON p.company_id = c.id WHERE p.id = $id;"),
        ("sala", "SELECT s.* FROM place s JOIN person p ON p.place_id = s.id WHERE p.id = $id;"),
        ("importacao", "SELECT i.* FROM person_import i JOIN person p ON p.import_id = i.id WHERE p.id = $id;"),
        ("credenciais", "SELECT * FROM person_credential WHERE person_id = $id ORDER BY created_at, id;"),
        ("catracas", "SELECT * FROM person_gate WHERE person_id = $id ORDER BY inner_number;"),
        ("catracas_do_perfil", "SELECT g.* FROM person_profile_gate g JOIN person p ON p.profile_id = g.profile_id WHERE p.id = $id ORDER BY inner_number;"),
        ("horarios", "SELECT s.* FROM time_schedule s JOIN person p ON s.id IN (p.schedule_id, (SELECT schedule_id FROM person_profile WHERE id = p.profile_id)) WHERE p.id = $id ORDER BY s.id;"),
        ("faixas_de_horario", "SELECT s.* FROM time_schedule_slot s JOIN person p ON s.schedule_id IN (p.schedule_id, (SELECT schedule_id FROM person_profile WHERE id = p.profile_id)) WHERE p.id = $id ORDER BY schedule_id, weekday, start_min;"),
        ("aceites", "SELECT c.*, t.text, t.sha256, t.created_at AS term_created_at, t.created_by AS term_created_by FROM person_consent c JOIN consent_term t ON t.version = c.term_version WHERE c.person_id = $id ORDER BY c.recorded_at, c.term_version;"),
        ("eventos", "SELECT * FROM person_event WHERE person_id = $id ORDER BY seq;"),
        ("passagens", "SELECT * FROM ticket_use_attempt WHERE person_id = $id ORDER BY at, id;"),
        ("decisoes", "SELECT d.* FROM access_decision d WHERE d.id IN (SELECT decision_id FROM ticket_use_attempt WHERE person_id = $id) ORDER BY d.decided_at, d.id;"),
        ("provas_fisicas", "SELECT p.* FROM physical_passage p WHERE p.decision_id IN (SELECT decision_id FROM ticket_use_attempt WHERE person_id = $id) ORDER BY confirmed_at, id;"),
        ("eventos_brutos", "SELECT r.* FROM raw_event r WHERE r.id IN (SELECT raw_event_id FROM access_decision WHERE id IN (SELECT decision_id FROM ticket_use_attempt WHERE person_id = $id) UNION SELECT raw_event_id FROM physical_passage WHERE decision_id IN (SELECT decision_id FROM ticket_use_attempt WHERE person_id = $id)) ORDER BY received_time, id;"),
        ("comandos", "SELECT c.* FROM device_command c WHERE c.decision_id IN (SELECT decision_id FROM ticket_use_attempt WHERE person_id = $id) ORDER BY issued_at, id;"),
    ];
}
