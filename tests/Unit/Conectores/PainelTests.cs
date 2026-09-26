using System.Net;
using System.Text;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Sync.Connectors.Rest.Painel;
using Sync.Core;

namespace Unit.Tests.Conectores;

/// <summary>
/// Leitura das respostas do painel na nuvem: o que aceitar, o que recusar, e quando
/// repetir. O ciclo completo está em Integration.Tests.PainelNaNuvemTests.
/// </summary>
public sealed class PainelTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 21, 0, 0, TimeSpan.Zero);

    private sealed class Servidor(HttpStatusCode status, string corpo) : HttpMessageHandler
    {
        public List<string> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Pedidos.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpClient Http(Servidor servidor) =>
        new(servidor) { BaseAddress = new Uri("https://painel.invalid/functions/v1/") };

    private static (FonteDeCartoesDoPainel Fonte, List<string> Recusas) Fonte(Servidor servidor)
    {
        var recusas = new List<string>();
        var fonte = new FonteDeCartoesDoPainel(
            Http(servidor), "bilheteria-local", "borda-01", PerfisDeLeitura.MifareCatraca4,
            aoRecusarCartao: (_, m) => recusas.Add(m));
        return (fonte, recusas);
    }

    private static string Resposta(string cartoes, string marca = "\"2026-11-14T20:00:00.000+00:00\"") =>
        $$"""{"success":true,"cards":[{{cartoes}}],"removed_cards":[],"sync_timestamp":{{marca}}}""";

    [Fact]
    public async Task Numero_de_cartao_como_numero_json_e_recusado()
    {
        var (fonte, recusas) = Fonte(new Servidor(HttpStatusCode.OK, Resposta("""{"card_number":123,"active":true}""")));

        var pagina = await fonte.LerAsync(null, CancellationToken.None);

        Assert.Empty(pagina.Itens);
        Assert.Contains("número JSON", Assert.Single(recusas), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zeros_a_esquerda_sao_preservados_e_o_perfil_completa_ate_10()
    {
        var (fonte, _) = Fonte(new Servidor(HttpStatusCode.OK, Resposta("""{"card_number":"00123","active":true}""")));

        var item = Assert.Single((await fonte.LerAsync(null, CancellationToken.None)).Itens);

        Assert.Equal("00123", item.QrBruto);
        Assert.Equal("0000000123", item.QrNormalizado);
        Assert.Equal(FonteDeCartoesDoPainel.SemLimiteDeUsos, item.UsosMaximos);
    }

    [Fact]
    public async Task Validade_sem_fuso_e_recusada()
    {
        var (fonte, recusas) = Fonte(new Servidor(HttpStatusCode.OK,
            Resposta("""{"card_number":"0000000101","active":true,"valid_until":"2026-11-14T23:00:00"}""")));

        Assert.Empty((await fonte.LerAsync(null, CancellationToken.None)).Itens);
        Assert.Contains("fuso", Assert.Single(recusas), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Usos_ja_feitos_na_nuvem_sao_descontados()
    {
        var (fonte, _) = Fonte(new Servidor(HttpStatusCode.OK,
            Resposta("""{"card_number":"0000000101","active":true,"max_uses":3,"times_used":1}""")));

        Assert.Equal(2, Assert.Single((await fonte.LerAsync(null, CancellationToken.None)).Itens).UsosMaximos);
    }

    [Fact]
    public async Task Cartao_sem_usos_restantes_na_nuvem_entra_cancelado_e_nunca_com_zero_usos()
    {
        var (fonte, _) = Fonte(new Servidor(HttpStatusCode.OK,
            Resposta("""{"card_number":"0000000101","active":true,"max_uses":1,"times_used":1}""")));

        var item = Assert.Single((await fonte.LerAsync(null, CancellationToken.None)).Itens);

        Assert.True(item.Cancelado);
        Assert.Equal(1, item.UsosMaximos);
    }

    [Fact]
    public async Task Resposta_sem_marca_do_servidor_nao_e_aceita()
    {
        // Sem ela não há cursor confiável; seguir com o relógio do PC perderia mudanças.
        var (fonte, _) = Fonte(new Servidor(HttpStatusCode.OK, Resposta("", marca: "null")));

        await Assert.ThrowsAsync<FormatException>(() => fonte.LerAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Erro_do_servidor_na_leitura_de_cartoes_lanca_em_vez_de_fingir_lista_vazia()
    {
        var (fonte, _) = Fonte(new Servidor(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => fonte.LerAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task Pedido_incremental_leva_o_cursor_e_o_primeiro_pede_tudo()
    {
        var servidor = new Servidor(HttpStatusCode.OK, Resposta(""));
        var (fonte, _) = Fonte(servidor);

        await fonte.LerAsync(null, CancellationToken.None);
        await fonte.LerAsync("2026-11-14T20:00:00.000+00:00", CancellationToken.None);

        using var primeiro = System.Text.Json.JsonDocument.Parse(servidor.Pedidos[0]);
        using var segundo = System.Text.Json.JsonDocument.Parse(servidor.Pedidos[1]);

        Assert.True(primeiro.RootElement.GetProperty("full_sync").GetBoolean());
        Assert.False(primeiro.RootElement.TryGetProperty("last_sync_at", out _));
        Assert.Equal("borda-01", segundo.RootElement.GetProperty("device_id").GetString());
        Assert.False(segundo.RootElement.GetProperty("full_sync").GetBoolean());
        Assert.Equal("2026-11-14T20:00:00.000+00:00", segundo.RootElement.GetProperty("last_sync_at").GetString());
    }

    private static ItemDeSaida Item(string id, string codigo = "0000000101") =>
        new(id, "painel-tentativas", TentativaEspelhada.TipoDoAgregado, id,
            new TentativaEspelhada(1, Guid.NewGuid(), codigo, "catraca-01", "portao-1", Agora, true,
                "Consumido", "bilheteria-local", "meia", null).ParaJson(),
            PrioridadeDeSincronizacao.PassagemFisica, $"tentativa:{id}", 0, Agora);

    private static async Task<IReadOnlyList<RespostaDeItem>> Enviar(Servidor servidor, params ItemDeSaida[] itens) =>
        await new ConectorDeTentativasDoPainel(Http(servidor)).EnviarAsync(itens, CancellationToken.None);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ResultadoDoEnvio.FalhaPermanente)]
    [InlineData(HttpStatusCode.BadRequest, ResultadoDoEnvio.FalhaPermanente)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ResultadoDoEnvio.FalhaTemporaria)]
    [InlineData(HttpStatusCode.TooManyRequests, ResultadoDoEnvio.FalhaTemporaria)]
    public async Task Situacao_http_decide_se_vale_repetir(HttpStatusCode status, ResultadoDoEnvio esperado)
    {
        var resposta = Assert.Single(await Enviar(new Servidor(status, "{}"), Item("a")));
        Assert.Equal(esperado, resposta.Resultado);
    }

    [Fact]
    public async Task Duzentos_com_corpo_ilegivel_e_repetido()
    {
        var resposta = Assert.Single(await Enviar(new Servidor(HttpStatusCode.OK, "<html>"), Item("a")));
        Assert.Equal(ResultadoDoEnvio.FalhaTemporaria, resposta.Resultado);
    }

    [Fact]
    public async Task Falhas_que_nao_se_consegue_apontar_fazem_o_grupo_inteiro_ser_repetido()
    {
        // O servidor diz "1 falhou" e a lista não bate com nada enviado. Dar os outros por
        // entregues perderia evento em silêncio.
        var corpo = """{"success":true,"saved":1,"failed":1,"duplicates_ignored":0,"failed_events":[]}""";
        var respostas = await Enviar(new Servidor(HttpStatusCode.OK, corpo), Item("a"), Item("b", "0000000102"));

        Assert.All(respostas, r => Assert.Equal(ResultadoDoEnvio.FalhaTemporaria, r.Resultado));
    }

    [Fact]
    public async Task Conteudo_ilegivel_na_outbox_vai_para_cartas_mortas_sem_ser_enviado()
    {
        var servidor = new Servidor(HttpStatusCode.OK, "{}");
        var ruim = Item("a") with { PayloadJson = "{\"versao\":2}" };

        var resposta = Assert.Single(await Enviar(servidor, ruim));

        Assert.Equal(ResultadoDoEnvio.FalhaPermanente, resposta.Resultado);
        Assert.Empty(servidor.Pedidos);
    }

    [Fact]
    public void Horario_tem_grafia_fixa_em_milissegundos_utc()
    {
        var comFuso = new DateTimeOffset(2026, 11, 14, 18, 0, 0, 123, TimeSpan.FromHours(-3)).AddTicks(4567);
        Assert.Equal("2026-11-14T21:00:00.123Z", ConectorDeTentativasDoPainel.Horario(comFuso));
    }
}
