using System.Text;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Edge.Supervisor;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// Etapa B.1 do docs/35: a migração 011 (tipos, colunas de cadastro, trilha e lotes) recusa
/// o que não deve entrar e não guarda o número do cartão fora de <c>ticket</c>.
/// </summary>
/// <remarks>Todo código aqui é sintético (<c>9999…</c>, <c>0000000101</c>).</remarks>
public sealed class CadastroDeCartoesEsquemaTests : IDisposable
{
    private const string Provedor = "bilheteria";
    private const string Codigo = "99990000000101";
    private const string CodigoComZeros = "00999900000202";
    private static readonly DateTimeOffset Agora = new(2026, 12, 5, 21, 0, 0, TimeSpan.Zero);

    // Chave fixa de teste: torna a impressão determinística. A de produção nasce no cofre.
    private static readonly byte[] ChaveDeTeste = [.. Enumerable.Range(1, 32).Select(i => (byte)i)];

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repositorio;
    private readonly ImpressaoDeCodigo _impressao = new(ChaveDeTeste);

    public CadastroDeCartoesEsquemaTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Provedor, "Bilheteria", "raw", "", Reutilizavel: true), Agora);
        _repositorio.Ingerir(
            [
                new IngressoRecebido(Provedor, Codigo, Codigo, Codigo, Categoria: "INTEIRA"),
                new IngressoRecebido(Provedor, CodigoComZeros, CodigoComZeros, CodigoComZeros, Categoria: "MEIA"),
            ],
            Agora);
    }

    public void Dispose() => _banco.Dispose();

    // ----------------------------------------------------------------- a migração nasce neutra

    [Fact]
    public void A_migracao_nasce_neutra_e_o_cartao_de_hoje_continua_valendo()
    {
        using var conexao = _banco.Fabrica.Abrir();

        foreach (var coluna in new[]
                 {
                     "kind", "source", "status_reason", "status_changed_at", "status_changed_by",
                     "created_by", "updated_at", "updated_by", "allowed_gates", "batch_label",
                 })
        {
            Assert.Equal(
                0L,
                SqliteConnectionFactory.Escalar<long>(conexao, $"SELECT COUNT(*) FROM ticket WHERE {coluna} IS NOT NULL;"));
        }

        // ADR-0025: owner_of_fields registra quem mudou por último; o que já existe veio da
        // nuvem ou do balcão, e nada espera para subir.
        Assert.Equal(
            0L,
            SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COUNT(*) FROM ticket WHERE owner_of_fields <> 'nuvem';"));

        // Tabelas novas são STRICT, como as outras.
        foreach (var tabela in new[] { "ticket_type", "ticket_type_alias", "credential_event", "import_batch", "import_batch_row" })
        {
            Assert.Equal(
                1L,
                SqliteConnectionFactory.Escalar<long>(conexao, $"SELECT strict FROM pragma_table_list WHERE name = '{tabela}';"));
        }

        Assert.True(_repositorio.TentarUsar(Codigo, "portao-1", "catraca-01", Agora).Resultado.Liberou);
    }

    // ----------------------------------------------------------------------- ticket_type

    [Theory]
    [InlineData("meia")]
    [InlineData("MEIA ENTRADA")]
    [InlineData("MEIA-ENTRADA")]
    [InlineData("MÉIA")]
    [InlineData("M")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void Tipo_recusa_codigo_fora_do_padrao(string codigo)
    {
        Assert.Throws<SqliteException>(() => GravarTipo(codigo));
    }

    [Theory]
    [InlineData("MEIA")]
    [InlineData("SOCIAL_2")]
    [InlineData("ABCDEFGHIJKLMNOPQRST")]
    public void Tipo_aceita_codigo_em_maiusculas(string codigo) => GravarTipo(codigo);

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("azul")]
    [InlineData("Rayzer.Success")]
    [InlineData("Rayzer.Success.Subtle")]
    [InlineData("Rayzer.Danger")]
    [InlineData("Rayzer.Warning")]
    [InlineData("Rayzer.Access.Denied")]
    [InlineData("Rayzer.Device.Online")]
    [InlineData("Rayzer.Brand Cyan")]
    public void Cor_do_tipo_e_token_e_nunca_cor_de_situacao(string cor)
    {
        Assert.Throws<SqliteException>(() => GravarTipo("MEIA", cor: cor));
    }

    [Theory]
    [InlineData("Rayzer.Brand.Cyan")]
    [InlineData("Rayzer.Info")]
    public void Cor_do_tipo_aceita_token_do_design_system(string cor) => GravarTipo("MEIA", cor: cor);

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("Meia", -1, 1)]
    [InlineData("Meia", 1, 2)]
    public void Tipo_recusa_nome_ordem_e_ativo_invalidos(string nome, long ordem, long ativo)
    {
        Assert.Throws<SqliteException>(() => GravarTipo("MEIA", nome, ordem, ativo));
    }

    [Fact]
    public void Apelido_aponta_para_um_tipo_so_em_cada_provedor()
    {
        GravarTipo("MEIA");
        GravarTipo("INTEIRA");
        GravarApelido("Meia Entrada", "MEIA");

        Assert.Throws<SqliteException>(() => GravarApelido("Meia Entrada", "INTEIRA"));
        Assert.Throws<SqliteException>(() => GravarApelido("Camarote", "CAMAROTE"));   // tipo inexistente
        Assert.Throws<SqliteException>(() => GravarApelido(" Meia ", "MEIA"));         // espaço nas pontas
    }

    // ------------------------------------------------------------- colunas novas do ticket

    [Theory]
    [InlineData("kind", "'vip'")]
    [InlineData("source", "'excel'")]
    [InlineData("owner_of_fields", "'ambos'")]
    [InlineData("owner_of_fields", "''")]
    [InlineData("owner_of_fields", "NULL")]
    [InlineData("status_reason", "'curt'")]
    [InlineData("status_changed_by", "'X'")]
    [InlineData("created_by", "'X'")]
    [InlineData("updated_by", "'X'")]
    [InlineData("allowed_gates", "'[]'")]
    [InlineData("allowed_gates", "'portao-1'")]
    [InlineData("allowed_gates", "'{\"portao\":1}'")]
    [InlineData("batch_label", "'   '")]
    public void Colunas_novas_do_ticket_recusam_valor_fora_da_regra(string coluna, string valor)
    {
        using var conexao = _banco.Fabrica.Abrir();
        Assert.Throws<SqliteException>(() =>
            SqliteConnectionFactory.Executar(conexao, $"UPDATE ticket SET {coluna} = {valor};"));
    }

    [Theory]
    [InlineData("kind", "'cartao_bilheteria'")]
    [InlineData("source", "'importacao'")]
    [InlineData("owner_of_fields", "'nuvem'")]
    [InlineData("owner_of_fields", "'local'")]
    [InlineData("status_reason", "'Cartão perdido no balcão'")]
    [InlineData("allowed_gates", "'[\"portao-1\",\"portao-2\"]'")]
    [InlineData("batch_label", "'LOTE-2026-A'")]
    public void Colunas_novas_do_ticket_aceitam_o_que_a_regra_permite(string coluna, string valor)
    {
        using var conexao = _banco.Fabrica.Abrir();
        SqliteConnectionFactory.Executar(conexao, $"UPDATE ticket SET {coluna} = {valor};");
    }

    // ------------------------------------------------------------------ credential_event

    [Fact]
    public void A_trilha_recusa_update_e_delete()
    {
        Trilha().Registrar(Codigo, Evento(AcaoSobreCredencial.Criado, IdDe(Codigo)));

        using var conexao = _banco.Fabrica.Abrir();
        Assert.Throws<SqliteException>(() =>
            SqliteConnectionFactory.Executar(conexao, "UPDATE credential_event SET actor = 'Outra Pessoa';"));
        Assert.Throws<SqliteException>(() =>
            SqliteConnectionFactory.Executar(conexao, "DELETE FROM credential_event;"));
        Assert.Equal(1L, SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COUNT(*) FROM credential_event;"));
    }

    [Fact]
    public void A_trilha_recusa_o_codigo_em_claro_por_qualquer_coluna()
    {
        var mascara = CredentialValue.Mascarar(Codigo);
        var impressao = _impressao.De(Codigo);
        var ticket = IdDe(Codigo).ToString();

        // Na coluna da máscara, na da impressão e no texto livre de um cartão que existe.
        Assert.Throws<SqliteException>(() => InserirEventoCru(Codigo, impressao, null, null));
        Assert.Throws<SqliteException>(() => InserirEventoCru(mascara, "hmac-sha256:" + Codigo, null, null));
        Assert.Throws<SqliteException>(() => InserirEventoCru(mascara, impressao, ticket, $"{{\"codigo\":\"{Codigo}\"}}"));
        Assert.Throws<SqliteException>(() => InserirEventoCru(mascara, impressao, ticket, "{}", autor: $"Operador {Codigo}"));

        // Pelo caminho de produção também — e a mensagem não repete o número.
        var erro = Assert.Throws<ArgumentException>(() => Trilha().Registrar(
            Codigo, Evento(AcaoSobreCredencial.Editado, null, depois: $"{{\"obs\":\"{Codigo}\"}}")));
        Assert.DoesNotContain(Codigo, erro.Message, StringComparison.Ordinal);

        InserirEventoCru(mascara, impressao, ticket, "{\"tipo\":\"MEIA\"}");
    }

    [Fact]
    public void Nenhum_numero_em_claro_em_nenhuma_coluna_da_trilha()
    {
        var trilha = Trilha();
        trilha.Registrar(Codigo, Evento(AcaoSobreCredencial.Criado, IdDe(Codigo), depois: "{\"tipo\":\"INTEIRA\"}"));
        trilha.Registrar(Codigo, Evento(AcaoSobreCredencial.Bloqueado, IdDe(Codigo), motivo: "Cartão perdido no balcão"));
        trilha.Registrar(CodigoComZeros, Evento(AcaoSobreCredencial.Importado, IdDe(CodigoComZeros)));

        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT * FROM credential_event;";
        using var leitor = comando.ExecuteReader();

        var linhas = 0;
        while (leitor.Read())
        {
            linhas++;
            for (var i = 0; i < leitor.FieldCount; i++)
            {
                var valor = leitor.IsDBNull(i) ? string.Empty : Convert.ToString(leitor.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
                foreach (var codigo in new[] { Codigo, CodigoComZeros, CodigoComZeros.TrimStart('0') })
                {
                    Assert.False(
                        valor.Contains(codigo, StringComparison.Ordinal),
                        $"A coluna {leitor.GetName(i)} da trilha contém o código em claro.");
                }
            }
        }

        Assert.Equal(3, linhas);
    }

    [Fact]
    public void Codigo_sem_cartao_nao_aparece_em_lugar_nenhum_do_arquivo_da_base()
    {
        // Código que nunca virou ticket: se ele estiver em qualquer byte do arquivo, veio da
        // trilha. Varre a base e o WAL inteiros.
        const string Nunca = "99990000000303";
        Trilha().Registrar(Nunca, Evento(AcaoSobreCredencial.Importado, null));

        var bytes = BytesDaBase();

        // Controle: a varredura acha o código que está, de propósito, em ticket.
        Assert.NotEqual(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Codigo)));

        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Nunca)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(ChaveDeTeste));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Convert.ToBase64String(ChaveDeTeste))));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Convert.ToHexString(ChaveDeTeste))));
    }

    [Fact]
    public void O_historico_de_um_codigo_se_acha_pela_impressao_e_so_com_a_mesma_chave()
    {
        var trilha = Trilha();
        trilha.Registrar(Codigo, Evento(AcaoSobreCredencial.Criado, IdDe(Codigo)));
        trilha.Registrar(CodigoComZeros, Evento(AcaoSobreCredencial.Criado, IdDe(CodigoComZeros)));
        trilha.Registrar(Codigo, Evento(AcaoSobreCredencial.Bloqueado, IdDe(Codigo), motivo: "Cartão perdido no balcão"));

        var historico = trilha.Historico(Codigo);
        Assert.Equal([AcaoSobreCredencial.Criado, AcaoSobreCredencial.Bloqueado], historico.Select(h => h.Acao));
        Assert.All(historico, h => Assert.Equal(CredentialValue.Mascarar(Codigo), h.CodigoMascarado));

        // Zeros à esquerda fazem parte do código: 00999900000202 não é 999900000202.
        Assert.Empty(trilha.Historico(CodigoComZeros.TrimStart('0')));

        // Outra chave não acha nada: sem a chave, a trilha não se liga ao código.
        var outraChave = new TrilhaDeCredenciais(_banco.Fabrica, new ImpressaoDeCodigo(new byte[32]));
        Assert.Empty(outraChave.Historico(Codigo));
    }

    [Fact]
    public void Bloqueio_desbloqueio_e_cancelamento_na_trilha_exigem_motivo()
    {
        var trilha = Trilha();
        foreach (var acao in new[] { AcaoSobreCredencial.Bloqueado, AcaoSobreCredencial.Desbloqueado, AcaoSobreCredencial.Cancelado })
        {
            Assert.Throws<SqliteException>(() => trilha.Registrar(Codigo, Evento(acao, IdDe(Codigo))));
            Assert.Throws<SqliteException>(() => trilha.Registrar(Codigo, Evento(acao, IdDe(Codigo), motivo: "curt")));
            trilha.Registrar(Codigo, Evento(acao, IdDe(Codigo), motivo: "Pedido do produtor"));
        }
    }

    [Fact]
    public void A_cadeia_da_trilha_fecha_e_acusa_adulteracao()
    {
        var trilha = Trilha();
        trilha.Registrar(Codigo, Evento(AcaoSobreCredencial.Criado, IdDe(Codigo)));
        var segundo = trilha.Registrar(Codigo, Evento(AcaoSobreCredencial.Editado, IdDe(Codigo), depois: "{\"tipo\":\"MEIA\"}"));
        trilha.Registrar(CodigoComZeros, Evento(AcaoSobreCredencial.Criado, IdDe(CodigoComZeros)));

        Assert.Null(TrilhaDeCredenciais.VerificarCadeia(_banco.Fabrica));

        // Só dá para adulterar tirando o gatilho — e o verificador acusa.
        using var conexao = _banco.Fabrica.Abrir();
        SqliteConnectionFactory.Executar(conexao, "DROP TRIGGER credential_event_nao_muda;");
        SqliteConnectionFactory.Executar(
            conexao, $"UPDATE credential_event SET after_json = '{{\"tipo\":\"INTEIRA\"}}' WHERE id = '{segundo}';");

        Assert.Equal(segundo.ToString(), TrilhaDeCredenciais.VerificarCadeia(_banco.Fabrica));
    }

    [Fact]
    public void A_cadeia_da_trilha_nao_bifurca()
    {
        Trilha().Registrar(Codigo, Evento(AcaoSobreCredencial.Criado, IdDe(Codigo)));

        // Um segundo evento que diz ser o primeiro da cadeia é recusado pelo índice único.
        Assert.Throws<SqliteException>(() => InserirEventoCru(
            CredentialValue.Mascarar(Codigo), _impressao.De(Codigo), null, null, anterior: TrilhaDeCredenciais.Origem));
    }

    // ------------------------------------------------------- import_batch e import_batch_row

    [Fact]
    public void O_lote_de_importacao_nao_se_apaga_e_a_situacao_so_anda_para_frente()
    {
        var lote = GravarLote();
        using var conexao = _banco.Fabrica.Abrir();

        Assert.Throws<SqliteException>(() => SqliteConnectionFactory.Executar(conexao, "DELETE FROM import_batch;"));
        Assert.Throws<SqliteException>(() => SqliteConnectionFactory.Executar(
            conexao, $"UPDATE import_batch SET file_sha256 = '{new string('b', 64)}';"));
        Assert.Throws<SqliteException>(() => SqliteConnectionFactory.Executar(
            conexao, "UPDATE import_batch SET requested_by = 'Outra Pessoa';"));

        MudarSituacao(conexao, lote, "aplicada");
        Assert.Throws<SqliteException>(() => MudarSituacao(conexao, lote, "previa"));
        MudarSituacao(conexao, lote, "desfeita");
        Assert.Throws<SqliteException>(() => MudarSituacao(conexao, lote, "aplicada"));
    }

    [Theory]
    [InlineData("cartoes", "'bilheteria'", "'planilha.csv'", "csv", 64)]
    [InlineData("cartoes", "NULL", "'planilha.csv'", "csv", 64)]                      // cartões sem provedor
    [InlineData("cartoes", "'bilheteria'", "'C:/Users/fulano/planilha.csv'", "csv", 64)] // caminho, não nome
    [InlineData("cartoes", "'bilheteria'", "'planilha.ods'", "ods", 64)]
    [InlineData("cartoes", "'bilheteria'", "'planilha.csv'", "csv", 63)]
    [InlineData("tudo", "'bilheteria'", "'planilha.csv'", "csv", 64)]
    public void O_lote_de_importacao_confere_a_origem(string tipo, string provedor, string arquivo, string formato, int tamanhoDoHash)
    {
        var valido = tipo == "cartoes" && provedor != "NULL" && !arquivo.Contains('/', StringComparison.Ordinal)
                     && formato == "csv" && tamanhoDoHash == 64;
        void Gravar() => GravarLote(tipo, provedor, arquivo, formato, new string('a', tamanhoDoHash));

        if (valido)
        {
            Gravar();
        }
        else
        {
            Assert.Throws<SqliteException>(Gravar);
        }
    }

    [Fact]
    public void Linha_do_lote_e_so_insert_e_sem_codigo_em_claro()
    {
        var lote = GravarLote();
        var ticket = $"'{IdDe(Codigo)}'";
        var mascara = $"'{CredentialValue.Mascarar(Codigo)}'";

        Assert.Throws<SqliteException>(() => GravarLinha(lote, 2, "NULL", "'erro'", "NULL", "NULL", "NULL"));     // erro sem motivo
        Assert.Throws<SqliteException>(() => GravarLinha(lote, 2, ticket, "'alterado'", mascara, "NULL", "NULL")); // alterado sem o antes
        Assert.Throws<SqliteException>(() => GravarLinha(lote, 2, "NULL", "'erro'", $"'{Codigo}'", "'Tipo inexistente'", "NULL"));
        Assert.Throws<SqliteException>(() => GravarLinha(lote, 2, ticket, "'alterado'", mascara, "NULL", $"'{{\"c\":\"{Codigo}\"}}'"));

        GravarLinha(lote, 2, ticket, "'alterado'", mascara, "NULL", "'{\"tipo\":\"MEIA\"}'");
        GravarLinha(lote, 3, "NULL", "'erro'", "NULL", "'Linha 3: tipo inexistente'", "NULL");

        using var conexao = _banco.Fabrica.Abrir();
        Assert.Throws<SqliteException>(() => SqliteConnectionFactory.Executar(conexao, "UPDATE import_batch_row SET outcome = 'igual';"));
        Assert.Throws<SqliteException>(() => SqliteConnectionFactory.Executar(conexao, "DELETE FROM import_batch_row;"));
    }

    // ------------------------------------------------------------------ chave do HMAC

    [Fact]
    public void A_chave_da_impressao_nasce_no_cofre_e_nunca_vai_para_a_base()
    {
        var cofre = new CofreEmMemoria();

        var primeira = ChaveDaImpressao.Obter(cofre);
        var segunda = ChaveDaImpressao.Obter(cofre);
        Assert.Equal(primeira.IdDaChave, segunda.IdDaChave);
        Assert.Equal(primeira.De(Codigo), segunda.De(Codigo));

        var guardada = cofre.Ler(ChaveDaImpressao.NomeNoCofre);
        Assert.NotNull(guardada);
        Assert.DoesNotContain(guardada, primeira.ToString(), StringComparison.Ordinal);

        new TrilhaDeCredenciais(_banco.Fabrica, primeira).Registrar(Codigo, Evento(AcaoSobreCredencial.Criado, IdDe(Codigo)));

        var bytes = BytesDaBase();
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(guardada)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Convert.FromBase64String(guardada)));
    }

    [Fact]
    public void Chave_ilegivel_no_cofre_nao_e_trocada_em_silencio()
    {
        var cofre = new CofreEmMemoria();
        cofre.Gravar(ChaveDaImpressao.NomeNoCofre, "curta");

        Assert.Throws<InvalidOperationException>(() => ChaveDaImpressao.Obter(cofre));
        Assert.Equal("curta", cofre.Ler(ChaveDaImpressao.NomeNoCofre));
    }

    // ------------------------------------------------------------------------ apoio

    private TrilhaDeCredenciais Trilha() => new(_banco.Fabrica, _impressao);

    private static EventoDeCredencial Evento(
        AcaoSobreCredencial acao,
        Guid? ingresso,
        string? depois = null,
        string? motivo = null) =>
        new(ingresso, acao, "Operadora Teste", @"MAQUINA\operador", Agora, DepoisJson: depois, Motivo: motivo);

    private Guid IdDe(string codigo)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT id FROM ticket WHERE qr_normalized = $qr;";
        comando.Parameters.AddWithValue("$qr", codigo);
        return Guid.Parse((string)comando.ExecuteScalar()!);
    }

    private void GravarTipo(string codigo, string nome = "Meia-entrada", long ordem = 1, long ativo = 1, string? cor = null)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO ticket_type (code, display_name, sort_order, color_token, active, created_at)
            VALUES ($codigo, $nome, $ordem, $cor, $ativo, $em);
            """;
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$nome", nome);
        comando.Parameters.AddWithValue("$ordem", ordem);
        comando.Parameters.AddWithValue("$cor", (object?)cor ?? DBNull.Value);
        comando.Parameters.AddWithValue("$ativo", ativo);
        comando.Parameters.AddWithValue("$em", Agora.ToString("O"));
        comando.ExecuteNonQuery();
    }

    private void GravarApelido(string apelido, string tipo)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            "INSERT INTO ticket_type_alias (provider_id, alias, type_code, created_at) VALUES ($p, $a, $t, $em);";
        comando.Parameters.AddWithValue("$p", Provedor);
        comando.Parameters.AddWithValue("$a", apelido);
        comando.Parameters.AddWithValue("$t", tipo);
        comando.Parameters.AddWithValue("$em", Agora.ToString("O"));
        comando.ExecuteNonQuery();
    }

    private void InserirEventoCru(
        string mascara,
        string impressao,
        string? ticket,
        string? depois,
        string autor = "Operadora Teste",
        string? anterior = null)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO credential_event
                (id, ticket_id, code_masked, code_hmac, code_key_id, action, after_json, actor, workstation, at, prev_hash, hash)
            VALUES ($id, $ticket, $mascara, $impressao, 'k1:teste', 'editado', $depois, $autor, 'MAQUINA', $em, $anterior, $hash);
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString());
        comando.Parameters.AddWithValue("$ticket", (object?)ticket ?? DBNull.Value);
        comando.Parameters.AddWithValue("$mascara", mascara);
        comando.Parameters.AddWithValue("$impressao", impressao);
        comando.Parameters.AddWithValue("$depois", (object?)depois ?? DBNull.Value);
        comando.Parameters.AddWithValue("$autor", autor);
        comando.Parameters.AddWithValue("$em", Agora.ToString("O"));
        comando.Parameters.AddWithValue("$anterior", anterior ?? Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32));
        comando.Parameters.AddWithValue("$hash", new string('c', 64));
        comando.ExecuteNonQuery();
    }

    private string GravarLote(
        string tipo = "cartoes",
        string provedor = "'bilheteria'",
        string arquivo = "'planilha.csv'",
        string formato = "csv",
        string? hash = null)
    {
        var id = Guid.CreateVersion7().ToString();
        using var conexao = _banco.Fabrica.Abrir();
        SqliteConnectionFactory.Executar(
            conexao,
            $"""
            INSERT INTO import_batch
                (id, kind, provider_id, file_name, file_format, file_sha256, file_bytes, mode,
                 rows_total, rows_new, requested_by, workstation, previewed_at)
            VALUES ('{id}', '{tipo}', {provedor}, {arquivo}, '{formato}', '{hash ?? new string('a', 64)}', 1024,
                    'incluir_e_atualizar', 2, 1, 'Operadora Teste', 'MAQUINA', '{Agora:O}');
            """);
        return id;
    }

    private static void MudarSituacao(SqliteConnection conexao, string lote, string situacao) =>
        SqliteConnectionFactory.Executar(conexao, $"UPDATE import_batch SET status = '{situacao}' WHERE id = '{lote}';");

    private void GravarLinha(string lote, int linha, string ticket, string desfecho, string mascara, string erro, string antes)
    {
        using var conexao = _banco.Fabrica.Abrir();
        SqliteConnectionFactory.Executar(
            conexao,
            $"""
            INSERT INTO import_batch_row (batch_id, line, ticket_id, outcome, code_masked, error, before_json)
            VALUES ('{lote}', {linha}, {ticket}, {desfecho}, {mascara}, {erro}, {antes});
            """);
    }

    private byte[] BytesDaBase()
    {
        var conteudo = new List<byte>();
        foreach (var arquivo in new[] { _banco.Caminho, _banco.Caminho + "-wal" })
        {
            if (File.Exists(arquivo))
            {
                using var fluxo = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var memoria = new MemoryStream();
                fluxo.CopyTo(memoria);
                conteudo.AddRange(memoria.ToArray());
            }
        }

        return [.. conteudo];
    }
}
