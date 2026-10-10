using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Access.Infrastructure.SQLite;
using Edge.Supervisor;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integration.Tests;

/// <summary>P7: prazos exatos, exclusão atômica, exportação integral e evidência versionada.</summary>
public sealed class LgpdTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 10, 14, 30, 0, TimeSpan.Zero);
    private readonly BancoTemporario _banco = new();
    private readonly CifraDeDadosPessoais _cifra = new(RandomNumberGenerator.GetBytes(32));
    private readonly CadastroDePessoas _pessoas;
    private readonly RetencaoDePessoas _retencao;
    public LgpdTests()
    {
        _banco.Migrar();
        _pessoas = new(_banco.Fabrica, _cifra);
        _retencao = new(_banco.Fabrica, _cifra);
    }
    public void Dispose() => _banco.Dispose();
    private string Criar(string nome = "Titular fictícia")
    {
        var r = _pessoas.Gravar("operador", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = nome }, Agora);
        Assert.True(r.Gravado, string.Join(" ", r.Problemas));
        return r.Id!;
    }
    private object? Escalar(string sql, params (string Nome, object Valor)[] parametros)
    {
        using var con = _banco.Fabrica.Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor);
        return cmd.ExecuteScalar();
    }
    private void Sql(string sql, params (string Nome, object Valor)[] parametros)
    {
        using var con = _banco.Fabrica.Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor);
        cmd.ExecuteNonQuery();
    }
    private JsonDocument Exportar(string id) => JsonDocument.Parse(_retencao.Exportar("exportador", id, Agora).SelectMany(b => b).ToArray());

    [Fact]
    public void Prazo_comeca_na_inativacao_e_editar_ou_repetir_inativo_nao_reinicia()
    {
        var id = Criar();
        var inicio = Agora.AddTicks(7654321);
        Assert.True(_pessoas.MudarSituacao("operador", id, "inativo", "Contrato encerrado", inicio).Gravado);
        Assert.True(_pessoas.Gravar("operador", _pessoas.Obter(id)!.Dados with { Observacao = "Ficha corrigida" }, inicio.AddDays(150)).Gravado);
        Assert.True(_pessoas.MudarSituacao("operador", id, "inativo", "Continua inativa", inicio.AddDays(170)).Gravado);
        Assert.Equal(0, _retencao.LimparVencidos(inicio.AddDays(180).AddTicks(-1)));
        Assert.NotNull(_pessoas.Obter(id));
        Assert.Equal(1, _retencao.LimparVencidos(inicio.AddDays(180)));
        Assert.Null(_pessoas.Obter(id));
        Assert.Equal("pessoas=1", Escalar("SELECT detail FROM person_event WHERE action = 'retencao.limpar' ORDER BY seq DESC LIMIT 1;"));
    }

    [Theory]
    [InlineData("ativo")]
    [InlineData("bloqueado")]
    public void Limpeza_nunca_apaga_ativo_ou_bloqueado(string situacao)
    {
        var id = Criar();
        if (situacao != "ativo") Assert.True(_pessoas.MudarSituacao("operador", id, situacao, "Bloqueio de teste", Agora).Gravado);
        Assert.Equal(0, _retencao.LimparVencidos(Agora.AddYears(20)));
        Assert.NotNull(_pessoas.Obter(id));
    }

    [Fact]
    public void Reativar_limpa_o_inicio_e_nova_inativacao_abre_outro_prazo()
    {
        var id = Criar();
        _pessoas.MudarSituacao("operador", id, "inativo", "Fim do contrato", Agora);
        _pessoas.MudarSituacao("operador", id, "ativo", null, Agora.AddDays(170));
        Assert.Equal(DBNull.Value, Escalar("SELECT inactivated_at FROM person WHERE id = $id;", ("$id", id)));
        _pessoas.MudarSituacao("operador", id, "inativo", "Novo encerramento", Agora.AddDays(171));
        Assert.Equal(0, _retencao.LimparVencidos(Agora.AddDays(180)));
        Assert.Equal(1, _retencao.LimparVencidos(Agora.AddDays(351)));
    }

    [Fact]
    public void Prazo_usa_o_perfil_atual_inclusive_quando_o_perfil_esta_inativo()
    {
        var id = Criar();
        _pessoas.MudarSituacao("operador", id, "inativo", "Fim do contrato", Agora);
        Sql("UPDATE person_profile SET retention_days = 365, status = 'inativo' WHERE id = 'aluno';");
        Assert.Equal(0, _retencao.LimparVencidos(Agora.AddDays(180)));
        Sql("UPDATE person_profile SET retention_days = 1 WHERE id = 'aluno';");
        Assert.Equal(1, _retencao.LimparVencidos(Agora.AddDays(180)));
    }

    [Fact]
    public void Excluir_remove_cifra_hashes_credenciais_aceites_e_ligacoes_sem_perder_passagens()
    {
        var id = Criar();
        var outro = Criar("Outra titular");
        var referencia = Criar("Pessoa recebida");
        Sql("UPDATE person SET host_person_id = $id WHERE id = $ref;", ("$id", id), ("$ref", referencia));
        Assert.True(_pessoas.AdicionarCredencial("operador", id, "cartao", "77009001", null, null, Agora).Gravado);
        var repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        var (_, tentativa) = repositorio.TentarUsar("77009001", "portao-1", "inner-1", Agora);
        Sql("""
            INSERT INTO raw_event VALUES ('leitura', 'inner-1', 'boot', 1, 1, NULL, $codigo, NULL, $em, NULL, 'corr');
            INSERT INTO raw_event VALUES ('giro', 'inner-1', 'boot', 2, 6, NULL, $codigo, NULL, $em, NULL, 'corr');
            INSERT INTO access_decision VALUES ('decisao', 'leitura', 'inner-1', 'portao-1', '77009001', 'Granted', 'Permitido', 'trilha pessoal', 1, 'Normal', $em);
            UPDATE ticket_use_attempt SET decision_id = 'decisao', passage_confirmed_at = $em WHERE id = $tentativa;
            INSERT INTO physical_passage VALUES ('prova', 'decisao', 'inner-1', 'giro', 'entrada', $em);
            """, ("$codigo", Encoding.UTF8.GetBytes("77009001")), ("$em", Agora.ToString("O", CultureInfo.InvariantCulture)), ("$tentativa", tentativa.ToString()));
        Assert.True(_retencao.GravarTermo("admin", "v1", "Termo fictício para o teste", Agora).Gravado);
        Assert.True(_retencao.RegistrarAceite("operador", id, "v1", Agora, Agora).Gravado);
        _pessoas.MudarSituacao("operador", id, "bloqueado", "Motivo potencialmente pessoal", Agora);

        Assert.True(new RetencaoDePessoas(_banco.Fabrica).Excluir("administrador", id, Agora).Gravado);
        Assert.Null(_pessoas.Obter(id));
        Assert.NotNull(_pessoas.Obter(outro));
        Assert.Equal(DBNull.Value, Escalar("SELECT host_person_id FROM person WHERE id = $id;", ("$id", referencia)));
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person_credential WHERE person_id = $id;", ("$id", id)));
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person_consent;"));
        Assert.Equal(1L, Escalar("SELECT COUNT(*) FROM consent_term;"));
        Assert.Equal(1L, Escalar("SELECT COUNT(*) FROM ticket_use_attempt WHERE id = $id AND person_id IS NULL AND qr_normalized = '' AND passage_confirmed_at IS NOT NULL;", ("$id", tentativa.ToString())));
        Assert.Equal(1L, Escalar("SELECT COUNT(*) FROM physical_passage;"));
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM raw_event WHERE length(payload) > 0;"));
        Assert.Equal(1L, Escalar("SELECT COUNT(*) FROM access_decision WHERE credential_value IS NULL AND rule_trace_json IS NULL;"));
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person_event WHERE person_id = $id OR detail LIKE '%potencialmente pessoal%';", ("$id", id)));
        Assert.Equal("administrador", Escalar("SELECT actor_id FROM person_event WHERE action = 'titular.excluir';"));
        Assert.True(_retencao.Excluir("administrador", id, Agora).Gravado);
        Assert.Equal("pessoas=0; passagens=0", Escalar("SELECT detail FROM person_event ORDER BY seq DESC LIMIT 1;"));
    }

    [Fact]
    public void Falha_ao_excluir_reverte_ficha_codigo_passagem_e_auditoria()
    {
        var id = Criar();
        _pessoas.AdicionarCredencial("operador", id, "cartao", "77009002", null, null, Agora);
        new RepositorioDeIngressos(_banco.Fabrica).TentarUsar("77009002", "p1", "d1", Agora);
        Sql("CREATE TRIGGER impedir_teste AFTER DELETE ON person BEGIN SELECT RAISE(ABORT, 'falha deliberada'); END;");
        Assert.Throws<SqliteException>(() => _retencao.Excluir("admin", id, Agora));
        Assert.NotNull(_pessoas.Obter(id));
        Assert.Equal(1L, Escalar("SELECT COUNT(*) FROM ticket_use_attempt WHERE person_id = $id AND qr_normalized = '77009002';", ("$id", id)));
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person_event WHERE action = 'titular.excluir';"));
        Assert.Throws<SqliteException>(() => Sql("UPDATE person_event SET detail = 'alterado';"));
        Assert.Throws<SqliteException>(() => Sql("DELETE FROM person_event;"));
    }

    [Fact]
    public async Task Servico_diario_processa_todos_os_lotes_e_resiste_a_falhas()
    {
        Sql("""
            WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 205)
            INSERT INTO person (id, profile_id, status, full_name_enc, created_at, updated_at)
            SELECT 'lote-' || i, 'aluno', 'inativo', X'00', $em, $em FROM n;
            """, ("$em", Agora.AddDays(-180).ToString("O", CultureInfo.InvariantCulture)));
        var servico = new LimpezaDiariaDePessoas(new RetencaoDePessoas(_banco.Fabrica), NullLogger<LimpezaDiariaDePessoas>.Instance, () => Agora);
        Assert.Equal(205, await servico.ExecutarAsync());
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person;"));
        Assert.Equal(3L, Escalar("SELECT COUNT(*) FROM person_event WHERE action = 'retencao.limpar';"));
        var id = Criar();
        _pessoas.MudarSituacao("operador", id, "inativo", "Fim do contrato", Agora.AddDays(-180));
        Sql("CREATE TRIGGER impedir_teste BEFORE DELETE ON person BEGIN SELECT RAISE(ABORT, 'falha deliberada'); END;");
        Assert.Equal(0, await servico.ExecutarAsync());
        Assert.NotNull(_pessoas.Obter(id));
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => servico.ExecutarAsync(cancelado.Token));
    }

    [Fact]
    public void Exportacao_decifra_todos_os_campos_e_inclui_credenciais_passagens_e_termos()
    {
        var id = Criar();
        var ficha = _pessoas.Obter(id)!.Dados with
        {
            NomeSocial = "Nome social fictício", TipoDoDocumento = "rg", Documento = "RG-TESTE-123",
            Nascimento = new DateOnly(1990, 1, 2), Telefone = "11 90000-1234", Email = "teste@example.invalid",
            Veiculo = "ABC1D23", Responsavel = "Responsável fictício", Departamento = "Departamento teste",
            Cargo = "Cargo teste", Matricula = "M-123", Observacao = "Observação teste", Catracas = [1, 2],
        };
        Assert.True(_pessoas.Gravar("operador", ficha, Agora).Gravado);
        _pessoas.AdicionarCredencial("operador", id, "cartao", "77009003", null, null, Agora);
        new RepositorioDeIngressos(_banco.Fabrica).TentarUsar("77009003", "p1", "d1", Agora);
        _retencao.GravarTermo("admin", "v1", "Texto fictício aceito", Agora);
        _retencao.RegistrarAceite("operador", id, "v1", Agora, Agora);
        using var dados = Exportar(id);
        var pessoa = dados.RootElement.GetProperty("pessoa")[0];
        Assert.Equal(ficha.NomeCompleto, pessoa.GetProperty("full_name").GetString());
        Assert.Equal(ficha.NomeSocial, pessoa.GetProperty("social_name").GetString());
        Assert.Equal(ficha.Documento, pessoa.GetProperty("document").GetString());
        Assert.Equal("1990-01-02", pessoa.GetProperty("birth_date").GetString());
        Assert.Equal(ficha.Telefone, pessoa.GetProperty("phone").GetString());
        Assert.Equal(ficha.Email, pessoa.GetProperty("email").GetString());
        Assert.Equal(ficha.Veiculo, pessoa.GetProperty("vehicle").GetString());
        Assert.Equal(ficha.Responsavel, pessoa.GetProperty("guardian").GetString());
        Assert.Equal(ficha.Observacao, pessoa.GetProperty("note").GetString());
        Assert.Equal("77009003", dados.RootElement.GetProperty("credenciais")[0].GetProperty("value_normalized").GetString());
        Assert.Equal(2, dados.RootElement.GetProperty("catracas").GetArrayLength());
        Assert.Single(dados.RootElement.GetProperty("passagens").EnumerateArray());
        Assert.Equal("Texto fictício aceito", dados.RootElement.GetProperty("aceites")[0].GetProperty("text").GetString());
        Assert.Equal("exportador", Escalar("SELECT actor_id FROM person_event WHERE action = 'titular.exportar';"));
    }

    [Fact]
    public void Exportacao_maior_que_quatro_MB_nao_trunca_e_os_blocos_sao_pequenos()
    {
        var id = Criar();
        Sql("""
            WITH RECURSIVE n(i) AS (VALUES(1) UNION ALL SELECT i + 1 FROM n WHERE i < 16000)
            INSERT INTO ticket_use_attempt (id, qr_normalized, gate_id, device_id, outcome, reason, at, person_id)
            SELECT 'a-' || i, '77009004', 'p1', 'd1', 'negado', 'PessoaInativa', $em, $id FROM n;
            """, ("$em", Agora.ToString("O", CultureInfo.InvariantCulture)), ("$id", id));
        var blocos = _retencao.Exportar("admin", id, Agora).ToArray();
        Assert.All(blocos, b => Assert.InRange(b.Length, 1, RetencaoDePessoas.TamanhoDoBloco));
        Assert.True(blocos.Sum(b => b.Length) > 4 * 1024 * 1024);
        using var json = JsonDocument.Parse(blocos.SelectMany(b => b).ToArray());
        Assert.Equal(16000, json.RootElement.GetProperty("passagens").GetArrayLength());
    }

    [Fact]
    public void Chave_errada_cancelamento_ou_pessoa_ausente_nao_registram_exportacao_concluida()
    {
        var id = Criar();
        var errada = new RetencaoDePessoas(_banco.Fabrica, new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(32)));
        Assert.ThrowsAny<CryptographicException>(() => errada.Exportar("admin", id, Agora).ToArray());
        Assert.Throws<InvalidOperationException>(() => new RetencaoDePessoas(_banco.Fabrica).Exportar("admin", id, Agora).ToArray());
        Assert.Throws<KeyNotFoundException>(() => _retencao.Exportar("admin", "ausente", Agora).ToArray());
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => _retencao.Exportar("admin", id, Agora, cancelado.Token).ToArray());
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person_event WHERE action = 'titular.exportar';"));
    }

    [Fact]
    public void Termos_sao_versionados_imutaveis_e_nao_criam_aceite_automatico()
    {
        var id = Criar();
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM consent_term;"));
        Assert.False(_retencao.GravarTermo("admin", " ", "texto", Agora).Gravado);
        Assert.True(_retencao.GravarTermo("admin", "v1", "Texto inicial", Agora).Gravado);
        Assert.True(_retencao.GravarTermo("admin", "v1", "Texto inicial", Agora).Gravado);
        Assert.False(_retencao.GravarTermo("admin", "v1", "Texto alterado", Agora).Gravado);
        Assert.True(_retencao.GravarTermo("admin", "v2", "Texto alterado", Agora).Gravado);
        Assert.Equal(0L, Escalar("SELECT COUNT(*) FROM person_consent;"));
        Assert.False(_retencao.RegistrarAceite("operador", id, "ausente", Agora, Agora).Gravado);
        Assert.False(_retencao.RegistrarAceite("operador", id, "v1", Agora.AddSeconds(1), Agora).Gravado);
        Assert.True(_retencao.RegistrarAceite("operador", id, "v1", Agora, Agora).Gravado);
        Assert.True(_retencao.RegistrarAceite("outro operador", id, "v1", Agora.AddDays(-1), Agora).Gravado);
        Assert.Equal("operador", Escalar("SELECT recorded_by FROM person_consent;"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("Texto inicial"))), Escalar("SELECT sha256 FROM consent_term WHERE version = 'v1';"));
        Assert.Throws<SqliteException>(() => Sql("UPDATE consent_term SET text = 'outro';"));
        Assert.Throws<SqliteException>(() => Sql("DELETE FROM consent_term;"));
    }

    [Fact]
    public void Migracao_recupera_o_inicio_do_ciclo_inativo_na_auditoria_antiga()
    {
        using var legado = new BancoTemporario();
        using (var con = legado.Fabrica.Abrir())
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "CREATE TABLE schema_version (nome TEXT PRIMARY KEY, aplicada_em TEXT NOT NULL);";
            cmd.ExecuteNonQuery();
            foreach (var nome in Migrator.Disponiveis().Where(n => n != "025_lgpd.sql"))
            {
                using var recurso = typeof(Migrator).Assembly.GetManifestResourceStream("Access.Infrastructure.SQLite.Migrations." + nome)!;
                using var leitor = new StreamReader(recurso);
                cmd.CommandText = leitor.ReadToEnd();
                cmd.ExecuteNonQuery();
                cmd.CommandText = "INSERT INTO schema_version VALUES ($nome, '2026-01-01');";
                cmd.Parameters.AddWithValue("$nome", nome);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
            }
        }
        var pessoas = new CadastroDePessoas(legado.Fabrica, _cifra);
        var id = pessoas.Gravar("operador", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Titular antiga" }, Agora).Id!;
        pessoas.MudarSituacao("operador", id, "inativo", "Primeiro encerramento", Agora);
        pessoas.MudarSituacao("operador", id, "ativo", null, Agora.AddDays(10));
        pessoas.MudarSituacao("operador", id, "inativo", "Segundo encerramento", Agora.AddDays(20));
        pessoas.MudarSituacao("operador", id, "inativo", "Repetição da operação", Agora.AddDays(100));
        pessoas.Gravar("operador", pessoas.Obter(id)!.Dados with { Observacao = "Correção posterior" }, Agora.AddDays(150));
        Assert.Single(new Migrator(legado.Fabrica).Aplicar());
        var retencao = new RetencaoDePessoas(legado.Fabrica);
        Assert.Equal(0, retencao.LimparVencidos(Agora.AddDays(199)));
        Assert.Equal(1, retencao.LimparVencidos(Agora.AddDays(200)));
        Assert.Empty(new Migrator(legado.Fabrica).Aplicar());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compatibilidade_com_P6_exporta_e_apaga_copia_da_visita_sem_expor_terceiros(bool limpeza)
    {
        var titular = Criar();
        var outra = Criar("Terceira pessoa");
        Sql("""
            CREATE TABLE visit (id TEXT PRIMARY KEY, visitor_person_id TEXT REFERENCES person(id) ON DELETE SET NULL,
                host_person_id TEXT REFERENCES person(id) ON DELETE SET NULL, visitor_name_enc BLOB NOT NULL,
                reason_enc BLOB NOT NULL, scheduled_from TEXT NOT NULL, scheduled_to TEXT NOT NULL,
                arrived_at TEXT, departed_at TEXT);
            INSERT INTO visit VALUES ('v-titular', $id, $outro, $nome, $motivo, $em, $em, NULL, NULL);
            INSERT INTO visit VALUES ('v-terceiro', $outro, $id, $nomeOutro, $motivoOutro, $em, $em, NULL, NULL);
            """, ("$id", titular), ("$outro", outra), ("$em", Agora.ToString("O", CultureInfo.InvariantCulture)),
            ("$nome", _cifra.Cifrar("Nome da titular na visita", "v-titular")!), ("$motivo", _cifra.Cifrar("Motivo da titular", "v-titular")!),
            ("$nomeOutro", _cifra.Cifrar("Nome pessoal de terceiro", "v-terceiro")!), ("$motivoOutro", _cifra.Cifrar("Motivo pessoal de terceiro", "v-terceiro")!));
        using var json = Exportar(titular);
        Assert.Equal("Nome da titular na visita", json.RootElement.GetProperty("visitas_do_titular")[0].GetProperty("visitor_name").GetString());
        Assert.Single(json.RootElement.GetProperty("visitas_recebidas").EnumerateArray());
        Assert.DoesNotContain("Nome pessoal de terceiro", json.RootElement.GetRawText(), StringComparison.Ordinal);
        if (limpeza)
        {
            _pessoas.MudarSituacao("operador", titular, "inativo", "Fim do contrato", Agora.AddDays(-180));
            Assert.Equal(1, _retencao.LimparVencidos(Agora));
        }
        else Assert.True(_retencao.Excluir("admin", titular, Agora).Gravado);
        Assert.Equal(1L, Escalar("SELECT COUNT(*) FROM visit;"));
        Assert.Equal(DBNull.Value, Escalar("SELECT host_person_id FROM visit WHERE id = 'v-terceiro';"));
        Assert.NotNull(_pessoas.Obter(outra));
    }
}
