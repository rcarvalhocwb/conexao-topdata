using System.Text;
using Access.Domain.Ticketing;
using Sync.Connectors.Rest;

namespace Unit.Tests.Ingestao;

/// <summary>
/// O webhook real da Compre no Zet (compra CP, estorno ES), lido com payloads sintéticos no
/// formato conferido contra a edição 2025 — nunca com dados de cliente. Ver docs/30.
/// </summary>
public sealed class TradutorDaZetTests
{
    private const long Evento = 538;
    private static readonly TradutorDaZet Tradutor = new("zet", Evento);

    private static IReadOnlyList<IngressoRecebido> Ler(string json) =>
        Tradutor.Traduzir(Encoding.UTF8.GetBytes(json), "application/json");

    private static string Ingresso(long id, string voucher, string data = "2025-11-20", string descricao = "Meia-entrada") =>
        $$$"""
        {"id":{{{id}}},"voucher":"{{{voucher}}}","date":null,"time":null,"used":"NAO","dateTimeUsed":null,
         "name":null,"email":null,"phone":null,"document":null,"subCategory":null,
         "eventsValues":{"id":6285,"sector":"Ingresso","session":"19h15","description":"{{{descricao}}}",
           "eventsDates":{"id":1391,"startDate":"{{{data}}}T00:00:00.000Z","endDate":"{{{data}}}T00:00:00.000Z"} } }
        """;

    // Comprador fictício, com CPF nulo: esse caso derrubava o processamento da integração atual.
    private static string Webhook(string acao, string ingressos, long evento = Evento) =>
        $$$"""
        {"action":"{{{acao}}}","data":{
          "order":{"id":100001,"uuid":"00000000-0000-4000-8000-000000000001","name":"Pessoa de Teste",
                   "email":"teste@exemplo.invalid","phone":null,"cpf":null,"paymentType":"PIX",
                   "paymentSituation":"PAGO","totalValue":122.10,"totalTax":11.10},
          "event":{"id":{{{evento}}},"name":"Evento de teste","slug":"evento-de-teste"},
          "eventTicketCodes":[{{{ingressos}}}]}}
        """;

    [Fact]
    public void A_compra_vira_um_ingresso_por_voucher()
    {
        var lidos = Ler(Webhook("CP", Ingresso(1, "1100001000001") + "," + Ingresso(2, "1100001000002", descricao: "Inteira")));

        Assert.Equal(2, lidos.Count);
        Assert.All(lidos, i => Assert.False(i.Cancelado));
        Assert.Equal(["1", "2"], lidos.Select(i => i.ReferenciaExterna));
        Assert.Equal(["1100001000001", "1100001000002"], lidos.Select(i => i.QrNormalizado));
        Assert.Equal(["Meia-entrada", "Inteira"], lidos.Select(i => i.Categoria));
        Assert.All(lidos, i => Assert.Null(i.Setor));
        Assert.All(lidos, i => Assert.Equal(1, i.UsosMaximos));
    }

    [Fact]
    public void Vale_o_dia_de_operacao_da_data_do_ingresso_no_horario_de_Brasilia()
    {
        // A Zet manda "2025-11-20T00:00:00.000Z": é o dia do calendário, não meia-noite UTC
        // (que seria 21h do dia 19 em Brasília).
        var ingresso = Assert.Single(Ler(Webhook("CP", Ingresso(1, "1100001000001"))));

        Assert.Equal(new DateTimeOffset(2025, 11, 20, 6, 0, 0, TimeSpan.FromHours(-3)), ingresso.ValidoDe);
        Assert.Equal(new DateTimeOffset(2025, 11, 21, 6, 0, 0, TimeSpan.FromHours(-3)), ingresso.ValidoAte);
    }

    [Fact]
    public void O_inicio_do_dia_de_operacao_e_configuravel()
    {
        var meiaNoite = new TradutorDaZet("zet", Evento, TimeSpan.Zero);
        var ingresso = Assert.Single(meiaNoite.Traduzir(Encoding.UTF8.GetBytes(Webhook("CP", Ingresso(1, "1100001000001"))), null));

        Assert.Equal(new DateTimeOffset(2025, 11, 20, 0, 0, 0, TimeSpan.FromHours(-3)), ingresso.ValidoDe);
    }

    [Fact]
    public void O_estorno_cancela_pela_mesma_referencia_da_compra()
    {
        var compra = Assert.Single(Ler(Webhook("CP", Ingresso(365388, "1100001000001"))));
        var estorno = Assert.Single(Ler(Webhook("ES", Ingresso(365388, "1100001000001"))));

        Assert.True(estorno.Cancelado);
        Assert.Equal(compra.ReferenciaExterna, estorno.ReferenciaExterna);
    }

    [Fact]
    public void Estorno_sem_ingressos_nao_cancela_nada()
    {
        Assert.Empty(Ler("""{"action":"ES","data":{"order":{"id":1},"event":{"id":538},"eventTicketCodes":null}}"""));
        Assert.Empty(Ler("""{"action":"ES","data":{"order":{"id":1},"event":{"id":538}}}"""));
    }

    [Fact]
    public void Outro_evento_da_conta_nao_vira_ingresso()
    {
        // A conta da Zet tem outros eventos, inclusive de teste ("Teste rua iluminada").
        Assert.Empty(Ler(Webhook("CP", Ingresso(1, "T100001000000001"), evento: 355)));
        Assert.Empty(Ler("""{"action":"CP","data":{"order":{"id":1},"eventTicketCodes":[]}}"""));
    }

    [Fact]
    public void Voucher_em_numero_json_e_recusado()
    {
        var erro = Assert.Throws<FormatException>(() => Ler(Webhook("CP", Ingresso(1, "x").Replace("\"x\"", "1100001000001", StringComparison.Ordinal))));

        Assert.Contains("número JSON", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Zeros_a_esquerda_do_voucher_sobrevivem()
    {
        Assert.Equal("0001000001", Assert.Single(Ler(Webhook("CP", Ingresso(1, "0001000001")))).QrNormalizado);
    }

    [Fact]
    public void Compra_sem_data_do_evento_e_recusada()
    {
        var semData = Ingresso(1, "1100001000001").Replace("\"startDate\":\"2025-11-20T00:00:00.000Z\",", string.Empty, StringComparison.Ordinal);

        var erro = Assert.Throws<FormatException>(() => Ler(Webhook("CP", semData)));
        Assert.Contains("startDate", erro.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"action":"XX","data":{}}""", "action")]
    [InlineData("""{"data":{}}""", "action")]
    [InlineData("""{"action":"CP"}""", "data")]
    [InlineData("""[1,2]""", "objeto")]
    [InlineData("""nao é json""", "JSON")]
    public void Entrega_ilegivel_e_recusada_com_motivo(string corpo, string trecho)
    {
        var erro = Assert.Throws<FormatException>(() => Ler(corpo));

        Assert.Contains(trecho, erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Categoria_com_espacos_repetidos_e_normalizada()
    {
        var ingresso = Assert.Single(Ler(Webhook("CP", Ingresso(1, "1100001000001", descricao: "Estudantes ensino fundamental e  médio"))));

        Assert.Equal("Estudantes ensino fundamental e médio", ingresso.Categoria);
    }

    [Fact]
    public void Evento_invalido_na_configuracao_e_recusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TradutorDaZet("zet", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TradutorDaZet("zet", 538, TimeSpan.FromHours(24)));
    }
}
