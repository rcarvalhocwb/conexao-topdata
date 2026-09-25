using System.Text;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Sync.Connectors.Rest;

namespace Unit.Tests.Ingestao;

/// <summary>
/// O contrato de webhook que <b>nós</b> especificamos, lido de verdade.
/// </summary>
/// <remarks>
/// Ele é rigoroso porque um campo mal interpretado aqui é uma pessoa barrada na porta, e
/// ninguém descobre a causa no meio do evento. Ver docs/18-contrato-do-webhook.md
/// </remarks>
public sealed class TradutorDoContratoV1Tests
{
    private static readonly TradutorDoContratoV1 Tradutor = new("zet");

    private static IReadOnlyList<IngressoRecebido> Ler(string json) =>
        Tradutor.Traduzir(Encoding.UTF8.GetBytes(json), "application/json");

    private static FormatException Recusa(string json) =>
        Assert.Throws<FormatException>(() => Ler(json));

    private static string Envelope(string ingressos) =>
        $$"""{"versao":1,"id":"e-1","emitidoEm":"2026-11-14T20:31:07-03:00","ingressos":[{{ingressos}}]}""";

    private const string Minimo = """{"referencia":"ZET-1","qr":"0081AC33F0","situacao":"valido"}""";

    [Fact]
    public void O_exemplo_canonico_do_contrato_e_lido_inteiro()
    {
        // Se este teste quebrar, o exemplo da documentação deixou de valer — e é ele que
        // o provedor vai usar como referência para produzir.
        var ingresso = Assert.Single(Tradutor.Traduzir(TradutorDoContratoV1.Exemplo(), "application/json"));

        Assert.Equal("zet", ingresso.ProvedorId);
        Assert.Equal("ZET-8842179", ingresso.ReferenciaExterna);
        Assert.Equal("0081AC33F0", ingresso.QrBruto);
        Assert.Equal("pista", ingresso.Setor);
        Assert.Equal(1, ingresso.UsosMaximos);
        Assert.Equal("inteira", ingresso.Categoria);
        Assert.False(ingresso.Cancelado);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 18, 0, 0, TimeSpan.FromHours(-3)), ingresso.ValidoDe);
    }

    [Fact]
    public void O_qr_vindo_como_numero_e_recusado()
    {
        // A recusa mais importante do tradutor. Se o QR chegou como número JSON, os zeros
        // à esquerda já foram destruídos antes de chegar aqui: aceitar "convertendo para
        // texto" produz um código que não existe, e a pessoa é recusada sem explicação.
        var erro = Recusa(Envelope("""{"referencia":"ZET-1","qr":81443,"situacao":"valido"}"""));

        Assert.Contains("número JSON", erro.Message, StringComparison.Ordinal);
        Assert.Contains("zeros à esquerda", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Zeros_a_esquerda_sobrevivem_a_traducao()
    {
        var ingresso = Assert.Single(Ler(Envelope("""{"referencia":"ZET-1","qr":"0000123","situacao":"valido"}""")));

        Assert.Equal("0000123", ingresso.QrBruto);
        Assert.Equal("0000123", ingresso.QrNormalizado);
    }

    [Fact]
    public void A_normalizacao_padrao_nao_mexe_no_conteudo_do_qr()
    {
        // Passar para maiúsculas destruiria um QR em base64. O padrão só tira espaço das
        // pontas, e mudar isso é decisão com a resposta da pergunta 9 na mão.
        var ingresso = Assert.Single(Ler(Envelope("""{"referencia":"ZET-1","qr":" aB9/xY+z= ","situacao":"valido"}""")));

        Assert.Equal("aB9/xY+z=", ingresso.QrNormalizado);
    }

    [Fact]
    public void Um_perfil_explicito_e_aplicado_quando_informado()
    {
        var tradutor = new TradutorDoContratoV1("zet", new CredentialNormalization("qr-maiusculo", upperCase: true));

        var ingresso = Assert.Single(tradutor.Traduzir(
            Encoding.UTF8.GetBytes(Envelope("""{"referencia":"ZET-1","qr":"abc123","situacao":"valido"}""")),
            "application/json"));

        Assert.Equal("abc123", ingresso.QrBruto);
        Assert.Equal("ABC123", ingresso.QrNormalizado);
    }

    [Fact]
    public void Versao_desconhecida_e_recusada_em_vez_de_lida_mais_ou_menos()
    {
        // Uma versão nova pode ter mudado o significado de um campo sem mudar o nome, e é
        // exatamente aí que se aceita um ingresso que devia ser recusado.
        var erro = Recusa("""{"versao":2,"ingressos":[]}""");

        Assert.Contains("versão 2", erro.Message, StringComparison.Ordinal);
        Assert.Contains("docs/18", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Versao_ausente_e_recusada()
    {
        Assert.Contains("'versao' ausente", Recusa("""{"ingressos":[]}""").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Campos_a_mais_sao_ignorados()
    {
        // É o que permite ao provedor evoluir o payload sem combinar conosco antes.
        var json = Envelope(
            """{"referencia":"ZET-1","qr":"0081","situacao":"valido","preco":120.5,"comprador":{"nome":"x"}}""")
            .Replace("\"id\":\"e-1\"", "\"id\":\"e-1\",\"loteInterno\":77", StringComparison.Ordinal);

        var ingresso = Assert.Single(Ler(json));

        Assert.Equal("ZET-1", ingresso.ReferenciaExterna);
    }

    [Theory]
    [InlineData("""{"qr":"0081","situacao":"valido"}""", "referencia")]
    [InlineData("""{"referencia":"ZET-1","situacao":"valido"}""", "qr")]
    [InlineData("""{"referencia":"ZET-1","qr":"0081"}""", "situacao")]
    public void Campo_obrigatorio_ausente_e_recusado(string ingresso, string campoEsperado)
    {
        Assert.Contains(campoEsperado, Recusa(Envelope(ingresso)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Qr_vazio_e_recusado()
    {
        Assert.Contains("vazio", Recusa(Envelope("""{"referencia":"ZET-1","qr":"  ","situacao":"valido"}""")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Situacao_desconhecida_e_recusada()
    {
        var erro = Recusa(Envelope("""{"referencia":"ZET-1","qr":"0081","situacao":"reservado"}"""));

        Assert.Contains("'reservado'", erro.Message, StringComparison.Ordinal);
        Assert.Contains("'valido' ou 'cancelado'", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelado_chega_como_cancelado()
    {
        // Cancelamento não é um tipo de mensagem diferente: é o mesmo ingresso com outra
        // situação. Menos coisa para o provedor errar.
        var ingresso = Assert.Single(Ler(Envelope("""{"referencia":"ZET-1","qr":"0081","situacao":"cancelado"}""")));

        Assert.True(ingresso.Cancelado);
    }

    [Fact]
    public void Usos_ausente_vale_um_e_usos_invalido_e_recusado()
    {
        Assert.Equal(1, Assert.Single(Ler(Envelope(Minimo))).UsosMaximos);
        Assert.Equal(3, Assert.Single(Ler(Envelope("""{"referencia":"ZET-1","qr":"0081","situacao":"valido","usos":3}"""))).UsosMaximos);

        Assert.Throws<FormatException>(() => Ler(Envelope("""{"referencia":"ZET-1","qr":"0081","situacao":"valido","usos":0}""")));
        Assert.Throws<FormatException>(() => Ler(Envelope("""{"referencia":"ZET-1","qr":"0081","situacao":"valido","usos":"dois"}""")));
    }

    [Fact]
    public void Data_sem_fuso_e_recusada()
    {
        // "18:00:00" sem fuso é ambíguo, e a ambiguidade aparece como ingresso recusado
        // uma hora antes ou depois — no escuro, durante o evento.
        var erro = Recusa(Envelope(
            """{"referencia":"ZET-1","qr":"0081","situacao":"valido","validoDe":"2026-11-14T18:00:00"}"""));

        Assert.Contains("com fuso", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_em_z_e_aceita()
    {
        var ingresso = Assert.Single(Ler(Envelope(
            """{"referencia":"ZET-1","qr":"0081","situacao":"valido","validoAte":"2026-11-15T05:00:00Z"}""")));

        Assert.Equal(new DateTimeOffset(2026, 11, 15, 5, 0, 0, TimeSpan.Zero), ingresso.ValidoAte);
    }

    [Fact]
    public void Uma_entrega_pode_trazer_varios_ingressos()
    {
        var ingressos = Ler(Envelope(
            """{"referencia":"ZET-1","qr":"0081","situacao":"valido"},""" +
            """{"referencia":"ZET-2","qr":"0082","situacao":"valido"},""" +
            """{"referencia":"ZET-3","qr":"0083","situacao":"cancelado"}"""));

        Assert.Equal(3, ingressos.Count);
        Assert.Equal(["ZET-1", "ZET-2", "ZET-3"], ingressos.Select(i => i.ReferenciaExterna));
    }

    [Fact]
    public void Lista_vazia_e_aceita_sem_erro()
    {
        Assert.Empty(Ler("""{"versao":1,"ingressos":[]}"""));
    }

    [Fact]
    public void Corpo_que_nao_e_json_vira_recusa_e_nao_derruba_o_processo()
    {
        // FormatException é o que a fonte entende como "pule esta entrega". Qualquer
        // outra exceção pararia a fila inteira.
        Assert.Throws<FormatException>(() => Ler("isto nao e json"));
        Assert.Throws<FormatException>(() => Ler("[1,2,3]"));
        Assert.Throws<FormatException>(() => Ler("""{"versao":1}"""));
    }

    [Fact]
    public void O_erro_diz_qual_ingresso_da_lista_esta_errado()
    {
        // Numa entrega com trezentos ingressos, "campo ausente" sem a posição é inútil.
        var erro = Recusa(Envelope(
            """{"referencia":"ZET-1","qr":"0081","situacao":"valido"},""" +
            """{"referencia":"ZET-2","situacao":"valido"}"""));

        Assert.Contains("ingressos[1]", erro.Message, StringComparison.Ordinal);
    }
}
