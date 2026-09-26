using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

public sealed class PersistenciaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 19, 30, 0, TimeSpan.Zero);

    private static DeviceEvent Evento(long seq, KnownEventOrigin origem = KnownEventOrigin.Leitor1, string boot = "boot-1") =>
        DeviceEvent.Create(
            new DeviceEventKey("catraca-08", boot, seq),
            EventOrigin.From(origem),
            Agora.AddSeconds(seq),
            $"corr-{seq}",
            rawCardData: "0001234567");

    private static Decision Permitido() =>
        new(DecisionOutcome.Allowed, ReasonCodes.Autorizado, DegradationTier.T1SemInternet, TimeSpan.FromMilliseconds(8), []);

    [Fact]
    public void Migracoes_aplicam_e_sao_idempotentes()
    {
        using var banco = new BancoTemporario();
        var migrador = new Migrator(banco.Fabrica);

        var primeira = migrador.Aplicar();
        var segunda = migrador.Aplicar();

        Assert.NotEmpty(primeira);
        Assert.Empty(segunda);
    }

    [Fact]
    public void Banco_abre_em_wal_com_chaves_estrangeiras_e_synchronous_full()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        using var conexao = banco.Fabrica.Abrir();

        Assert.Equal("wal", SqliteConnectionFactory.Escalar<string>(conexao, "PRAGMA journal_mode;"));
        Assert.Equal(1L, SqliteConnectionFactory.Escalar<long>(conexao, "PRAGMA foreign_keys;"));
        Assert.Equal(2L, SqliteConnectionFactory.Escalar<long>(conexao, "PRAGMA synchronous;")); // 2 = FULL
    }

    [Fact]
    public void Integridade_do_banco_esta_ok()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        Assert.Equal("ok", banco.Fabrica.VerificarIntegridade());
    }

    /// <summary>
    /// O coração do CA-02: evento, decisão, comando e outbox numa transação só.
    /// </summary>
    [Fact]
    public void Evento_decisao_comando_e_outbox_sao_gravados_juntos()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        var resultado = diario.Registrar(
            Evento(1),
            Permitido(),
            new IssuedCommand("LiberarCatracaEntrada", null, "cmd-1"),
            [new OutboxItem("acesso", "ag-1", "{}", 5, "rest", "out-1")]);

        Assert.True(resultado.Gravado);
        Assert.NotNull(resultado.DecisionId);
        Assert.Equal(1, diario.ContarEventos("catraca-08"));
        Assert.Single(diario.OutboxPendente());
    }

    /// <summary>
    /// Se a gravação falha no meio, nada fica. Sem estados intermediários, sem acesso
    /// registrado que a sincronização nunca vai conhecer.
    /// </summary>
    [Fact]
    public void Falha_no_meio_da_transacao_nao_deixa_nada_gravado()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        // Chave de idempotência de comando duplicada viola o índice único e derruba a
        // transação inteira, depois de o evento já ter sido inserido nela.
        diario.Registrar(Evento(1), Permitido(), new IssuedCommand("LiberarCatracaEntrada", null, "cmd-repetido"));

        Assert.Throws<SqliteException>(() =>
            diario.Registrar(Evento(2), Permitido(), new IssuedCommand("LiberarCatracaEntrada", null, "cmd-repetido")));

        // O evento 2 não pode ter sobrado.
        Assert.Equal(1, diario.ContarEventos("catraca-08"));
    }

    /// <summary>
    /// Reenvio depois de reconexão é o caminho normal, não um erro.
    /// </summary>
    [Fact]
    public void Reenvio_do_mesmo_evento_e_ignorado()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        var primeira = diario.Registrar(Evento(1), Permitido());
        var segunda = diario.Registrar(Evento(1), Permitido());

        Assert.True(primeira.Gravado);
        Assert.False(segunda.Gravado);
        Assert.Equal(1, diario.ContarEventos("catraca-08"));
    }

    /// <summary>
    /// Reinício do equipamento gera novo bootId: a sequência recomeça do 1 sem colidir
    /// com os eventos do ciclo anterior.
    /// </summary>
    [Fact]
    public void Reinicio_do_equipamento_nao_colide_com_a_sequencia_anterior()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        diario.Registrar(Evento(1, boot: "boot-1"), Permitido());
        var aposReinicio = diario.Registrar(Evento(1, boot: "boot-2"), Permitido());

        Assert.True(aposReinicio.Gravado);
        Assert.Equal(2, diario.ContarEventos("catraca-08"));
    }

    [Fact]
    public void Origem_desconhecida_e_gravada_e_contabilizada()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        var evento = DeviceEvent.Create(
            new DeviceEventKey("catraca-08", "boot-1", 1),
            EventOrigin.FromRaw(14),
            Agora,
            "corr-14");

        Assert.True(diario.Registrar(evento).Gravado);
        Assert.Equal(1, diario.ContarOrigensDesconhecidas());
    }

    [Fact]
    public void Passagem_fisica_exige_prova_de_giro()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        var leitura = Evento(1, KnownEventOrigin.Leitor1);
        diario.Registrar(leitura);

        var erro = Assert.Throws<InvalidOperationException>(
            () => diario.RegistrarPassagemFisica(leitura, decisionId: null, "entrada"));

        Assert.Contains("origem 6", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passagem_fisica_e_aceita_com_a_origem_6()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        var giro = Evento(1, KnownEventOrigin.GiroConfirmado);
        diario.Registrar(giro);

        diario.RegistrarPassagemFisica(giro, decisionId: null, "entrada");

        using var conexao = banco.Fabrica.Abrir();
        Assert.Equal(1L, SqliteConnectionFactory.Escalar<long>(conexao, "SELECT COUNT(*) FROM physical_passage;"));
    }

    [Fact]
    public void Outbox_entrega_bloqueio_emergencial_antes_do_historico()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        diario.Registrar(
            Evento(1),
            outbox:
            [
                new OutboxItem("historico", "h", "{}", 9, "rest", "out-hist"),
                new OutboxItem("bloqueio", "b", "{}", 0, "rest", "out-bloq"),
                new OutboxItem("evento", "e", "{}", 5, "rest", "out-evt"),
            ]);

        var pendentes = diario.OutboxPendente();

        Assert.Equal([0, 5, 9], pendentes.Select(p => p.Priority));
    }

    [Fact]
    public void Auditoria_recusa_update_e_delete()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        using var conexao = banco.Fabrica.Abrir();
        SqliteConnectionFactory.Executar(
            conexao,
            "INSERT INTO audit_log (at, action, prev_hash, hash) VALUES ('2026-09-24', 'login', '0', 'abc');");

        Assert.Throws<SqliteException>(() =>
            SqliteConnectionFactory.Executar(conexao, "UPDATE audit_log SET action = 'outro';"));

        Assert.Throws<SqliteException>(() =>
            SqliteConnectionFactory.Executar(conexao, "DELETE FROM audit_log;"));
    }

    [Fact]
    public void Dados_sobrevivem_ao_fechamento_e_reabertura()
    {
        using var banco = new BancoTemporario();

        var diario = banco.Migrar();
        diario.Registrar(Evento(1), Permitido());

        // Nova fábrica, novas conexões: nada em cache.
        var outraFabrica = new SqliteConnectionFactory(banco.Caminho);
        var outroDiario = new AccessJournal(outraFabrica);

        Assert.Equal(1, outroDiario.ContarEventos("catraca-08"));
        Assert.Equal("ok", outraFabrica.VerificarIntegridade());
    }
}
