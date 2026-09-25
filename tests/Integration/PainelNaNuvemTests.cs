using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Access.Domain.Credentials;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Sync.Connectors.Rest.Painel;
using Sync.Core;
using Sync.Ingestao;

namespace Integration.Tests;

/// <summary>
/// A borda conversando com o painel na nuvem: cartões descem, tentativas sobem.
/// </summary>
/// <remarks>
/// <para>
/// O painel aqui é um manipulador HTTP falso que imita o comportamento do servidor
/// descrito em docs/22-sistema-supabase-atual.md, seção 8 — inclusive as partes
/// desconfortáveis: repetição reconhecida por equipamento + cartão + horário, o
/// <c>event_id</c> ignorado, e 200 mesmo quando parte do lote falha.
/// </para>
/// <para>
/// Não é o servidor real. O que ele prova é que a borda trata esse comportamento do jeito
/// certo; se o servidor real se comportar diferente, é o teste com o projeto de teste na
/// nuvem que vai mostrar.
/// </para>
/// </remarks>
public sealed class PainelNaNuvemTests
{
    private const string Bilheteria = "bilheteria-local";
    private const string Conector = "painel-tentativas";
    private static readonly DateTimeOffset Abertura = new(2026, 11, 14, 21, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan EsperaPeloGiro = TimeSpan.FromSeconds(10);

    // Números de cartão de teste. Nenhum é de cartão real.
    private const string CartaoMeia = "0000000101";
    private const string CartaoInteira = "0000000102";
    private const string CartaoUmUso = "0000000103";

    private sealed class RelogioManual(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;

        public override DateTimeOffset GetUtcNow() => Agora;
    }

    /// <summary>Imita as duas funções do painel usadas pela borda.</summary>
    private sealed class PainelFalso : HttpMessageHandler
    {
        public List<JsonObject> Cartoes { get; } = [];

        public List<string> Removidos { get; } = [];

        public string MarcaDoServidor { get; set; } = "2026-11-14T20:00:00.000+00:00";

        /// <summary>Eventos gravados, pela chave que o servidor usa para repetição.</summary>
        public Dictionary<(string Equipamento, string Cartao, string Horario), JsonObject> Eventos { get; } = [];

        public List<JsonObject> PedidosDeCartoes { get; } = [];

        public int RequisicoesDeEventos { get; private set; }

        public HashSet<string> CartoesQueFalham { get; } = [];

        /// <summary>Grava e depois finge que a resposta se perdeu, N vezes.</summary>
        public int PerderRespostas { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var corpo = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();

            return request.RequestUri!.AbsolutePath switch
            {
                "/functions/v1/middleware-sync-cards" => Cartoes_(corpo),
                "/functions/v1/middleware-sync-events" => Eventos_(corpo),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private HttpResponseMessage Cartoes_(JsonObject pedido)
        {
            PedidosDeCartoes.Add(pedido);
            var completa = pedido["full_sync"]!.GetValue<bool>();

            var resposta = new JsonObject
            {
                ["success"] = true,
                ["sync_type"] = completa ? "full" : "incremental",
                ["cards"] = new JsonArray([.. Cartoes.Select(c => c.DeepClone())]),
                ["removed_cards"] = completa ? new JsonArray() : new JsonArray([.. Removidos.Select(r => JsonValue.Create(r))]),
                ["total_cards"] = Cartoes.Count,
                ["sync_timestamp"] = MarcaDoServidor,
            };

            return Json(resposta);
        }

        private HttpResponseMessage Eventos_(JsonObject pedido)
        {
            RequisicoesDeEventos++;
            var equipamento = pedido["device_id"]!.GetValue<string>();
            var eventos = pedido["events"]!.AsArray();

            if (eventos.Count is 0 or > 100)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            int salvos = 0, repetidos = 0;
            var falhas = new JsonArray();

            foreach (var e in eventos.Select(e => e!.AsObject()))
            {
                var cartao = e["card_id"]!.GetValue<string>();
                var horario = e["occurred_at"]!.GetValue<string>();

                if (CartoesQueFalham.Contains(cartao))
                {
                    falhas.Add(new JsonObject { ["card_id"] = cartao, ["occurred_at"] = horario, ["error"] = "violação" });
                    continue;
                }

                // Como o servidor: event_id não conta, a chave é equipamento + cartão + horário.
                if (!Eventos.TryAdd((equipamento, cartao, horario), (JsonObject)e.DeepClone()))
                {
                    repetidos++;
                    continue;
                }

                salvos++;
            }

            if (PerderRespostas > 0)
            {
                PerderRespostas--;
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            return Json(new JsonObject
            {
                ["success"] = true,
                ["saved"] = salvos,
                ["failed"] = falhas.Count,
                ["duplicates_ignored"] = repetidos,
                ["failed_events"] = falhas,
                ["server_time"] = "2026-11-14T21:00:00.000Z",
            });
        }

        private static HttpResponseMessage Json(JsonNode conteudo) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(conteudo.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Cenario : IDisposable
    {
        private readonly BancoTemporario _banco = new();
        private readonly HttpClient _http;

        public Cenario(bool comEspelho = true, int limiteDeLinhas = 1000)
        {
            _banco.Migrar();
            Painel = new PainelFalso();
            _http = new HttpClient(Painel) { BaseAddress = new Uri("https://painel.invalid/functions/v1/") };

            Repositorio = new RepositorioDeIngressos(
                _banco.Fabrica,
                comEspelho ? new EspelhoDeTentativas(Conector, EsperaPeloGiro) : null);

            Repositorio.RegistrarProvedor(
                new ProvedorDeIngresso(
                    Bilheteria, "Bilheteria", PerfisDeLeitura.MifareCatraca4.Name, Conector: string.Empty,
                    Reutilizavel: true, IntervaloDeReuso: TimeSpan.FromMinutes(4), SomenteNaUrna: true),
                Abertura);

            Fonte = new FonteDeCartoesDoPainel(
                _http, Bilheteria, "borda-01", PerfisDeLeitura.MifareCatraca4,
                limiteDeLinhas: limiteDeLinhas,
                aoRecusarCartao: (_, motivo) => Recusas.Add(motivo),
                aoSuspeitarDeCorte: n => SuspeitasDeCorte.Add(n));

            Ingestao = new LacoDeIngestao(Fonte, Repositorio, new CursoresSqlite(_banco.Fabrica), Relogio);
            Fila = new FilaDeSaidaSqlite(_banco.Fabrica);
            Drenador = new DrenadorDaOutbox(
                Fila, [new ConectorDeTentativasDoPainel(_http, Conector)], _ => TimeSpan.Zero, Relogio);
        }

        public RelogioManual Relogio { get; } = new(Abertura);

        public PainelFalso Painel { get; }

        public RepositorioDeIngressos Repositorio { get; }

        public FonteDeCartoesDoPainel Fonte { get; }

        public LacoDeIngestao Ingestao { get; }

        public FilaDeSaidaSqlite Fila { get; }

        public DrenadorDaOutbox Drenador { get; }

        public List<string> Recusas { get; } = [];

        public List<int> SuspeitasDeCorte { get; } = [];

        public string? Cursor => new CursoresSqlite(_banco.Fabrica).Ler(Bilheteria, LacoDeIngestao.FluxoIncremental);

        public (ResultadoDoUso Uso, Guid Tentativa) NaUrna(string cartao, string catraca = "catraca-01") =>
            Repositorio.TentarUsar(cartao, "portao-1", catraca, Relogio.Agora, leitor: KnownEventOrigin.Leitor2);

        public Task<ResumoDaRodada> Drenar() => Drenador.DrenarUmaVezAsync(CancellationToken.None);

        public void Dispose()
        {
            _http.Dispose();
            _banco.Dispose();
        }
    }

    private static JsonObject Cartao(string numero, string tipo, int? maximo = null, int usados = 0) => new()
    {
        ["card_number"] = numero,
        ["customer_name"] = "VISITANTE",
        ["active"] = true,
        ["max_uses"] = maximo,
        ["times_used"] = usados,
        ["valid_from"] = null,
        ["valid_until"] = null,
        ["ticket_type"] = tipo,
        ["admission_type"] = tipo,
    };

    [Fact]
    public async Task Cartao_desce_da_nuvem_passa_na_urna_e_sobe_um_evento_so_ja_com_o_giro()
    {
        using var c = new Cenario();
        c.Painel.Cartoes.Add(Cartao(CartaoMeia, "meia"));

        var resumo = await c.Ingestao.PuxarAsync(CancellationToken.None);
        Assert.Equal(1, resumo.Inseridos);
        Assert.Equal(c.Painel.MarcaDoServidor, c.Cursor);
        Assert.True(c.Painel.PedidosDeCartoes[0]["full_sync"]!.GetValue<bool>());

        var (uso, tentativa) = c.NaUrna(CartaoMeia);
        Assert.True(uso.Liberou);
        Assert.Equal("meia", uso.Categoria);

        // Antes do giro nada sobe: o evento está esperando a prova de passagem.
        Assert.Equal(0, (await c.Drenar()).Enviados);
        Assert.Empty(c.Painel.Eventos);

        c.Relogio.Agora = Abertura.AddSeconds(2);
        c.Repositorio.ConfirmarPassagemFisica(tentativa, c.Relogio.Agora);

        // O giro libera o item na hora, sem esperar o fim da janela.
        Assert.Equal(1, (await c.Drenar()).Enviados);

        var (chave, evento) = Assert.Single(c.Painel.Eventos);
        Assert.Equal("catraca-01", chave.Equipamento);
        Assert.Equal(CartaoMeia, chave.Cartao);
        Assert.Equal("2026-11-14T21:00:00.000Z", chave.Horario);
        Assert.True(evento["authorized"]!.GetValue<bool>());
        Assert.Equal("meia", evento["admission_type"]!.GetValue<string>());
        Assert.Equal("Consumido", evento["reason"]!.GetValue<string>());
        Assert.True(evento["extra"]!["giro_confirmado"]!.GetValue<bool>());
        Assert.Equal("2026-11-14T21:00:02.000Z", evento["extra"]!["giro_em"]!.GetValue<string>());
        Assert.Equal(tentativa.ToString(), evento["event_id"]!.GetValue<string>());
        Assert.Empty(c.Fila.BacklogPorConector());
    }

    [Fact]
    public async Task Liberacao_sem_giro_sobe_depois_da_espera_dizendo_que_nao_girou()
    {
        using var c = new Cenario();
        c.Painel.Cartoes.Add(Cartao(CartaoInteira, "inteira"));
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        Assert.True(c.NaUrna(CartaoInteira).Uso.Liberou);

        c.Relogio.Agora = Abertura + EsperaPeloGiro - TimeSpan.FromSeconds(1);
        Assert.Equal(0, (await c.Drenar()).Enviados);

        c.Relogio.Agora = Abertura + EsperaPeloGiro;
        Assert.Equal(1, (await c.Drenar()).Enviados);

        var evento = Assert.Single(c.Painel.Eventos).Value;
        Assert.True(evento["authorized"]!.GetValue<bool>());
        Assert.False(evento["extra"]!["giro_confirmado"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Negativa_sobe_na_hora_com_o_motivo()
    {
        using var c = new Cenario();

        var (uso, _) = c.NaUrna("0000009999");
        Assert.Equal(MotivoDoUso.Desconhecido, uso.Motivo);

        Assert.Equal(1, (await c.Drenar()).Enviados);

        var evento = Assert.Single(c.Painel.Eventos).Value;
        Assert.False(evento["authorized"]!.GetValue<bool>());
        Assert.Equal("Desconhecido", evento["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Giro_que_chega_depois_do_evento_ter_saido_nao_manda_segundo_evento()
    {
        // O painel conta cada evento "liberado" como uma entrada. Um segundo evento para
        // a mesma tentativa seria uma pessoa a mais no relatório.
        using var c = new Cenario();
        c.Painel.Cartoes.Add(Cartao(CartaoMeia, "meia"));
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        var (_, tentativa) = c.NaUrna(CartaoMeia);
        c.Relogio.Agora = Abertura + EsperaPeloGiro;
        Assert.Equal(1, (await c.Drenar()).Enviados);

        c.Repositorio.ConfirmarPassagemFisica(tentativa, c.Relogio.Agora.AddSeconds(1));
        Assert.Equal(0, (await c.Drenar()).Enviados);

        Assert.Single(c.Painel.Eventos);
        Assert.Equal(1, c.Repositorio.Conciliar(Bilheteria, Abertura.AddHours(1)).UsosComPassagemFisica);
    }

    [Fact]
    public async Task Resposta_perdida_na_volta_nao_vira_entrada_duplicada_na_nuvem()
    {
        using var c = new Cenario();
        c.NaUrna("0000009999");
        c.Painel.PerderRespostas = 1;

        var primeira = await c.Drenar();
        Assert.Equal(1, primeira.Adiados);
        Assert.Single(c.Painel.Eventos);   // o servidor gravou; a borda não sabe

        var segunda = await c.Drenar();
        Assert.Equal(1, segunda.Enviados);
        Assert.Single(c.Painel.Eventos);   // reenviado, reconhecido como repetido
        Assert.Equal(2, c.Painel.RequisicoesDeEventos);
        Assert.Empty(c.Fila.BacklogPorConector());
    }

    [Fact]
    public async Task Falha_parcial_com_200_manda_para_cartas_mortas_so_o_evento_recusado()
    {
        using var c = new Cenario();
        c.NaUrna("0000009998");
        c.NaUrna("0000009999");
        c.Painel.CartoesQueFalham.Add("0000009998");

        var rodada = await c.Drenar();

        Assert.Equal(1, rodada.Enviados);
        Assert.Equal(1, rodada.CartasMortas);
        Assert.Equal("0000009999", Assert.Single(c.Painel.Eventos).Key.Cartao);
    }

    [Fact]
    public async Task Uma_requisicao_por_catraca()
    {
        using var c = new Cenario();
        c.NaUrna("0000009998", catraca: "catraca-01");
        c.NaUrna("0000009999", catraca: "catraca-02");

        Assert.Equal(2, (await c.Drenar()).Enviados);
        Assert.Equal(2, c.Painel.RequisicoesDeEventos);
        Assert.Equal(
            ["catraca-01", "catraca-02"],
            c.Painel.Eventos.Keys.Select(k => k.Equipamento).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_nuvem_nao_devolve_uso_que_a_borda_ja_contou()
    {
        // O painel não soma usos a partir dos eventos. Se a sincronização seguinte
        // sobrescrevesse o contador local, o cartão de um uso só passaria de novo.
        using var c = new Cenario();
        c.Painel.Cartoes.Add(Cartao(CartaoUmUso, "inteira", maximo: 1));
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        Assert.True(c.NaUrna(CartaoUmUso).Uso.Liberou);

        c.Painel.MarcaDoServidor = "2026-11-14T21:05:00.000+00:00";
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        c.Relogio.Agora = Abertura.AddMinutes(10);
        Assert.Equal(MotivoDoUso.UsosEsgotados, c.NaUrna(CartaoUmUso).Uso.Motivo);
    }

    [Fact]
    public async Task Cartao_desativado_na_nuvem_para_de_passar_e_reativado_volta()
    {
        using var c = new Cenario();
        c.Painel.Cartoes.Add(Cartao(CartaoMeia, "meia"));
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        // Desativado: sai da lista e aparece em removed_cards na incremental.
        c.Painel.Cartoes.Clear();
        c.Painel.Removidos.Add(CartaoMeia);
        c.Painel.MarcaDoServidor = "2026-11-14T21:01:00.000+00:00";
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        Assert.False(c.Painel.PedidosDeCartoes[1]["full_sync"]!.GetValue<bool>());
        Assert.Equal("2026-11-14T20:00:00.000+00:00", c.Painel.PedidosDeCartoes[1]["last_sync_at"]!.GetValue<string>());
        Assert.Equal(MotivoDoUso.Cancelado, c.NaUrna(CartaoMeia).Uso.Motivo);

        // Achado e reativado.
        c.Painel.Removidos.Clear();
        c.Painel.Cartoes.Add(Cartao(CartaoMeia, "meia"));
        c.Painel.MarcaDoServidor = "2026-11-14T21:02:00.000+00:00";
        await c.Ingestao.PuxarAsync(CancellationToken.None);

        Assert.True(c.NaUrna(CartaoMeia).Uso.Liberou);
    }

    [Fact]
    public async Task Lista_possivelmente_cortada_avisa_e_nao_da_por_sincronizado()
    {
        using var c = new Cenario(limiteDeLinhas: 2);
        c.Painel.Cartoes.Add(Cartao(CartaoMeia, "meia"));
        c.Painel.Cartoes.Add(Cartao(CartaoInteira, "inteira"));

        var resumo = await c.Ingestao.PuxarAsync(CancellationToken.None);

        // O que veio é gravado — é melhor que nada —, mas o cursor não anda, e alguém
        // é avisado.
        Assert.Equal(2, resumo.Inseridos);
        Assert.Null(c.Cursor);
        Assert.Equal([2], c.SuspeitasDeCorte);
    }

    [Fact]
    public async Task Cartao_que_a_catraca_nao_leria_e_recusado_sem_o_numero_no_motivo()
    {
        using var c = new Cenario();
        c.Painel.Cartoes.Add(Cartao("12345678901234", "meia"));   // 14 dígitos: não é Mifare de 10
        c.Painel.Cartoes.Add(Cartao(CartaoInteira, "inteira"));

        var resumo = await c.Ingestao.PuxarAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Inseridos);
        var motivo = Assert.Single(c.Recusas);
        Assert.Contains("14 caracteres", motivo, StringComparison.Ordinal);
        Assert.DoesNotContain("12345678901234", motivo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_espelho_nada_entra_na_fila()
    {
        using var c = new Cenario(comEspelho: false);
        c.NaUrna("0000009999");

        Assert.Empty(c.Fila.BacklogPorConector());
        Assert.Equal(0, (await c.Drenar()).Enviados);
    }
}
