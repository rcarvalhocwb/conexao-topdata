using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;
using Sync.Core;

namespace Integration.Tests;

/// <summary>
/// A drenagem contra o banco de verdade: do que o diário gravou até o conector.
/// </summary>
/// <remarks>
/// Os testes de unidade provam a política; estes provam que o SQL faz o que a política
/// supõe — ordem, invisibilidade do item adiado, e a passagem para cartas mortas sem
/// perder o conteúdo. Ver docs/15-integracao-e-sincronizacao.md
/// </remarks>
public sealed class DrenagemDaOutboxTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed class ConectorDeTeste(string nome, Func<ItemDeSaida, ResultadoDoEnvio> veredito)
        : IConectorDeSincronizacao
    {
        public string Nome { get; } = nome;

        public int TamanhoMaximoDoLote => 50;

        public List<ItemDeSaida> Recebidos { get; } = [];

        public Task<IReadOnlyList<RespostaDeItem>> EnviarAsync(
            IReadOnlyList<ItemDeSaida> lote,
            CancellationToken cancelamento)
        {
            Recebidos.AddRange(lote);
            IReadOnlyList<RespostaDeItem> respostas =
                [.. lote.Select(i => new RespostaDeItem(i.Id, veredito(i), "detalhe"))];
            return Task.FromResult(respostas);
        }
    }

    private static DeviceEvent Evento(long seq) =>
        DeviceEvent.Create(
            new DeviceEventKey("catraca-01", "boot-1", seq),
            EventOrigin.From(KnownEventOrigin.Leitor1),
            Agora.AddSeconds(seq),
            $"corr-{seq}",
            rawCardData: "0001234567");

    private static Decision Permitido() =>
        new(DecisionOutcome.Allowed, ReasonCodes.Autorizado, DegradationTier.T1SemInternet, TimeSpan.FromMilliseconds(6), []);

    private static OutboxItem Item(string id, int prioridade, string conector = "erp") =>
        new("access_decision", id, $$"""{"id":"{{id}}"}""", prioridade, conector, $"chave-{id}");

    [Fact]
    public async Task Da_transacao_do_diario_ate_o_conector_sem_intervencao()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        diario.Registrar(
            Evento(1),
            Permitido(),
            outbox: [Item("d1", PrioridadeDeSincronizacao.DecisaoDeAcesso)]);

        var conector = new ConectorDeTeste("erp", _ => ResultadoDoEnvio.Aceito);
        var fila = new FilaDeSaidaSqlite(banco.Fabrica);
        var drenador = new DrenadorDaOutbox(fila, [conector], _ => TimeSpan.FromSeconds(1));

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Enviados);
        Assert.Equal("access_decision", Assert.Single(conector.Recebidos).TipoDoAgregado);
        Assert.Empty(fila.BacklogPorConector());
    }

    [Fact]
    public async Task No_banco_real_a_prioridade_vence_a_ordem_de_chegada()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        // Chegou primeiro: histórico. Chegou depois: revogação de cartão.
        diario.Registrar(Evento(1), outbox: [Item("historico", PrioridadeDeSincronizacao.Historico)]);
        diario.Registrar(Evento(2), outbox: [Item("revogacao", PrioridadeDeSincronizacao.RevogacaoDeCredencial)]);

        var conector = new ConectorDeTeste("erp", _ => ResultadoDoEnvio.Aceito);
        var drenador = new DrenadorDaOutbox(
            new FilaDeSaidaSqlite(banco.Fabrica),
            [conector],
            _ => TimeSpan.FromSeconds(1));

        await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(
            [PrioridadeDeSincronizacao.RevogacaoDeCredencial, PrioridadeDeSincronizacao.Historico],
            conector.Recebidos.Select(i => i.Prioridade));
    }

    [Fact]
    public async Task Item_enviado_nao_volta_na_rodada_seguinte()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();
        diario.Registrar(Evento(1), outbox: [Item("d1", PrioridadeDeSincronizacao.PassagemFisica)]);

        var conector = new ConectorDeTeste("erp", _ => ResultadoDoEnvio.Aceito);
        var drenador = new DrenadorDaOutbox(
            new FilaDeSaidaSqlite(banco.Fabrica),
            [conector],
            _ => TimeSpan.FromSeconds(1));

        await drenador.DrenarUmaVezAsync(CancellationToken.None);
        var segunda = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.False(segunda.TeveTrabalho);
        Assert.Single(conector.Recebidos);
    }

    [Fact]
    public async Task Recusa_permanente_preserva_o_conteudo_inteiro_em_cartas_mortas()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();
        diario.Registrar(Evento(1), outbox: [Item("d1", PrioridadeDeSincronizacao.DecisaoDeAcesso)]);

        var fila = new FilaDeSaidaSqlite(banco.Fabrica);
        var drenador = new DrenadorDaOutbox(
            fila,
            [new ConectorDeTeste("erp", _ => ResultadoDoEnvio.FalhaPermanente)],
            _ => TimeSpan.FromSeconds(1));

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.CartasMortas);
        Assert.Equal(1, fila.ContarCartasMortas());
        Assert.Empty(fila.BacklogPorConector());

        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT payload_json, idempotency_key, error FROM dead_letter;";
        using var leitor = comando.ExecuteReader();

        Assert.True(leitor.Read());
        Assert.Contains("\"id\":\"d1\"", leitor.GetString(0), StringComparison.Ordinal);
        Assert.Equal("chave-d1", leitor.GetString(1));
        Assert.False(string.IsNullOrWhiteSpace(leitor.GetString(2)));
    }

    [Fact]
    public async Task Adiamento_grava_tentativa_e_hora_e_esconde_o_item_ate_la()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();
        diario.Registrar(Evento(1), outbox: [Item("d1", PrioridadeDeSincronizacao.Alarme)]);

        var fila = new FilaDeSaidaSqlite(banco.Fabrica);
        var drenador = new DrenadorDaOutbox(
            fila,
            [new ConectorDeTeste("erp", _ => ResultadoDoEnvio.FalhaTemporaria)],
            _ => TimeSpan.FromHours(1));

        await drenador.DrenarUmaVezAsync(CancellationToken.None);

        using (var conexao = banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "SELECT attempts, next_attempt_at, last_error FROM outbox;";
            using var leitor = comando.ExecuteReader();
            Assert.True(leitor.Read());
            Assert.Equal(1, leitor.GetInt32(0));
            Assert.False(leitor.IsDBNull(1));
            Assert.False(leitor.IsDBNull(2));
        }

        // Uma hora de castigo: a rodada seguinte não enxerga o item.
        var segunda = await drenador.DrenarUmaVezAsync(CancellationToken.None);
        Assert.False(segunda.TeveTrabalho);

        // Mas ele continua na fila, contado no backlog que a operação acompanha.
        Assert.Equal(1L, fila.BacklogPorConector()["erp"]);
    }

    [Fact]
    public void A_chave_de_idempotencia_impede_a_mesma_linha_duas_vezes()
    {
        // Se o mesmo fato for enfileirado de novo — por reprocessamento, por retomada —
        // o índice único absorve em silêncio. Sem isso, a queda no meio de uma rotina de
        // reenvio vira registro duplicado no sistema do cliente.
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        diario.Registrar(Evento(1), outbox: [Item("d1", PrioridadeDeSincronizacao.DecisaoDeAcesso)]);
        diario.Registrar(Evento(2), outbox: [Item("d1", PrioridadeDeSincronizacao.DecisaoDeAcesso)]);

        var fila = new FilaDeSaidaSqlite(banco.Fabrica);

        Assert.Equal(1L, fila.BacklogPorConector()["erp"]);
    }

    [Fact]
    public async Task Cada_destino_tem_a_propria_fila_e_o_proprio_destino_ruim()
    {
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        diario.Registrar(Evento(1), outbox:
        [
            Item("bilhete", PrioridadeDeSincronizacao.DecisaoDeAcesso, "bilheteria"),
            Item("painel", PrioridadeDeSincronizacao.PassagemFisica, "painel"),
        ]);

        var bilheteria = new ConectorDeTeste("bilheteria", _ => ResultadoDoEnvio.FalhaTemporaria);
        var painel = new ConectorDeTeste("painel", _ => ResultadoDoEnvio.Aceito);

        var fila = new FilaDeSaidaSqlite(banco.Fabrica);
        var drenador = new DrenadorDaOutbox(fila, [bilheteria, painel], _ => TimeSpan.FromHours(1));

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Enviados);
        Assert.Equal(1, resumo.Adiados);

        var backlog = fila.BacklogPorConector();
        Assert.Equal(1L, backlog["bilheteria"]);
        Assert.False(backlog.ContainsKey("painel"));
    }

    [Fact]
    public async Task Backlog_de_uma_queda_longa_drena_em_rodadas_sucessivas()
    {
        // Oito horas sem internet, em miniatura: a fila é maior que o lote, e drenar é
        // chamar de novo enquanto houver trabalho.
        using var banco = new BancoTemporario();
        var diario = banco.Migrar();

        for (var i = 1; i <= 120; i++)
        {
            diario.Registrar(Evento(i), outbox: [Item($"d{i}", PrioridadeDeSincronizacao.Historico)]);
        }

        var conector = new ConectorDeTeste("erp", _ => ResultadoDoEnvio.Aceito);
        var fila = new FilaDeSaidaSqlite(banco.Fabrica);
        var drenador = new DrenadorDaOutbox(fila, [conector], _ => TimeSpan.FromSeconds(1));

        var rodadas = 0;
        while ((await drenador.DrenarUmaVezAsync(CancellationToken.None)).TeveTrabalho)
        {
            rodadas++;
            Assert.True(rodadas < 10, "A drenagem não convergiu.");
        }

        Assert.Equal(120, conector.Recebidos.Count);
        Assert.Empty(fila.BacklogPorConector());
    }
}
