using System.Net;
using System.Text;
using Access.Domain.Ticketing;
using Sync.Connectors.Rest;

namespace Unit.Tests.Ingestao;

/// <summary>
/// A borda buscando no relé o que o provedor empurrou para lá.
/// </summary>
/// <remarks>
/// Duas decisões opostas convivem aqui, e é essa oposição que importa: tradutor
/// <b>ausente</b> trava tudo de propósito, e entrega <b>ilegível</b> não trava nada.
/// Ver docs/ADR/ADR-0022-rele-de-webhook.md
/// </remarks>
public sealed class FonteDeRelayTests
{
    private sealed class ReleFalso(string json) : HttpMessageHandler
    {
        public List<string> Consultas { get; } = [];

        public Func<Exception>? Explodir { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Consultas.Add(request.RequestUri!.PathAndQuery);

            if (Explodir is not null)
            {
                throw Explodir();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class TradutorFalso : ITradutorDeIngresso
    {
        public string Provedor => "zet";

        public bool Configurado => true;

        /// <summary>Corpos que este tradutor recusa, por serem ilegíveis.</summary>
        public HashSet<string> Recusa { get; } = [];

        public IReadOnlyList<IngressoRecebido> Traduzir(byte[] corpo, string? tipoDeConteudo)
        {
            var texto = Encoding.UTF8.GetString(corpo);

            if (Recusa.Contains(texto))
            {
                throw new FormatException($"campo obrigatório ausente em '{texto}'");
            }

            return [new IngressoRecebido("zet", texto, $"QR-{texto}", $"QR-{texto}")];
        }
    }

    private static string Entregas(params (long Seq, string Corpo)[] itens)
    {
        var lista = itens.Select(i =>
            $$"""{"seq":{{i.Seq}},"recebidaEm":"2026-11-14T18:00:00Z","tipoDeConteudo":"application/json","corpoBase64":"{{Convert.ToBase64String(Encoding.UTF8.GetBytes(i.Corpo))}}","cabecalhosJson":"{}"}""");

        var ultimo = itens.Length > 0 ? itens[^1].Seq : 0;
        return $$"""{"entregas":[{{string.Join(",", lista)}}],"ultimoSeq":{{ultimo}},"temMais":false}""";
    }

    private static (FonteDeRelay Fonte, ReleFalso Rele, List<(long, string)> Pulos) Criar(
        string json,
        TradutorFalso? tradutor = null)
    {
        var rele = new ReleFalso(json);
        var http = new HttpClient(rele) { BaseAddress = new Uri("https://apizet.exemplo.invalid/") };
        var pulos = new List<(long, string)>();
        var fonte = new FonteDeRelay(http, tradutor ?? new TradutorFalso(), 200, (seq, motivo) => pulos.Add((seq, motivo)));
        return (fonte, rele, pulos);
    }

    [Fact]
    public async Task Tradutor_nao_configurado_nao_consome_entrega_nenhuma()
    {
        // Se ele lesse sem saber traduzir, o laço avançaria o cursor e os ingressos
        // sumiriam sem ninguém perceber. Travar é o comportamento correto.
        var rele = new ReleFalso(Entregas((1, "A1")));
        using var http = new HttpClient(rele) { BaseAddress = new Uri("https://apizet.exemplo.invalid/") };
        var fonte = new FonteDeRelay(http, new TradutorPendente("zet"));

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fonte.LerAsync(null, CancellationToken.None));

        Assert.Contains("não está configurado", erro.Message, StringComparison.Ordinal);
        Assert.Empty(rele.Consultas);
    }

    [Fact]
    public async Task O_tradutor_pendente_diz_onde_esta_a_resposta_que_falta()
    {
        var erro = Assert.Throws<NotSupportedException>(
            () => new TradutorPendente("zet").Traduzir([1, 2, 3], "application/json"));

        Assert.Contains("docs/17", erro.Message, StringComparison.Ordinal);
        Assert.False(new TradutorPendente("zet").Configurado);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Traduz_as_entregas_e_devolve_o_cursor_do_ultimo_seq()
    {
        var (fonte, rele, _) = Criar(Entregas((7, "A1"), (8, "A2")));

        var pagina = await fonte.LerAsync("6", CancellationToken.None);

        Assert.Equal(2, pagina.Itens.Count);
        Assert.Equal("8", pagina.ProximoCursor);
        Assert.Contains("desde=6", Assert.Single(rele.Consultas), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cursor_ausente_ou_invalido_le_do_comeco()
    {
        var (fonte, rele, _) = Criar(Entregas((1, "A1")));

        await fonte.LerAsync(null, CancellationToken.None);
        await fonte.LerAsync("nao-e-numero", CancellationToken.None);

        Assert.All(rele.Consultas, c => Assert.Contains("desde=0", c, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Entrega_ilegivel_e_pulada_sem_travar_as_outras()
    {
        // Parar no primeiro payload estranho significa que nenhum ingresso posterior
        // entra — no meio de um evento. Os bytes ficam no relé para reprocessar.
        var tradutor = new TradutorFalso();
        tradutor.Recusa.Add("A2");

        var (fonte, _, pulos) = Criar(Entregas((1, "A1"), (2, "A2"), (3, "A3")), tradutor);

        var pagina = await fonte.LerAsync(null, CancellationToken.None);

        Assert.Equal(2, pagina.Itens.Count);
        Assert.Equal("3", pagina.ProximoCursor);
        Assert.Equal(1, fonte.Ilegiveis);

        var (seq, motivo) = Assert.Single(pulos);
        Assert.Equal(2, seq);
        Assert.Contains("campo obrigatório ausente", motivo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corpo_que_nao_e_base64_e_pulado_e_nao_derruba_a_pagina()
    {
        const string json = """
            {"entregas":[{"seq":1,"corpoBase64":"isto-nao-e-base64!!"},
                         {"seq":2,"corpoBase64":"QTI="}],
             "ultimoSeq":2,"temMais":false}
            """;

        var (fonte, _, pulos) = Criar(json);

        var pagina = await fonte.LerAsync(null, CancellationToken.None);

        Assert.Single(pagina.Itens);
        Assert.Equal("A2", pagina.Itens[0].ReferenciaExterna);
        Assert.Equal(1, Assert.Single(pulos).Item1);
    }

    [Fact]
    public async Task Rele_sem_entregas_devolve_pagina_vazia()
    {
        var (fonte, _, _) = Criar("""{"entregas":[],"ultimoSeq":0,"temMais":false}""");

        var pagina = await fonte.LerAsync("5", CancellationToken.None);

        Assert.Empty(pagina.Itens);
        Assert.Null(pagina.ProximoCursor);
        Assert.False(pagina.TemMais);
    }

    [Fact]
    public async Task Tem_mais_do_rele_e_propagado_para_o_laco()
    {
        var corpo = Convert.ToBase64String(Encoding.UTF8.GetBytes("A1"));
        var (fonte, _, _) = Criar(
            $$"""{"entregas":[{"seq":1,"corpoBase64":"{{corpo}}"}],"ultimoSeq":1,"temMais":true}""");

        Assert.True((await fonte.LerAsync(null, CancellationToken.None)).TemMais);
    }

    [Fact]
    public async Task Rele_fora_do_ar_propaga_a_falha_para_o_laco_nao_avancar_o_cursor()
    {
        var rele = new ReleFalso("{}") { Explodir = () => new HttpRequestException("sem resposta") };
        using var http = new HttpClient(rele) { BaseAddress = new Uri("https://apizet.exemplo.invalid/") };
        var fonte = new FonteDeRelay(http, new TradutorFalso());

        await Assert.ThrowsAsync<HttpRequestException>(() => fonte.LerAsync("3", CancellationToken.None));
    }

    [Fact]
    public void A_fonte_nao_recebe_credencial_nenhuma()
    {
        // O token de leitura do relé fica no HttpClient, montado no ponto de composição.
        var parametros = typeof(FonteDeRelay).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name!)
            .ToList();

        Assert.DoesNotContain(parametros, nome =>
            nome.Contains("token", StringComparison.OrdinalIgnoreCase)
            || nome.Contains("segredo", StringComparison.OrdinalIgnoreCase)
            || nome.Contains("senha", StringComparison.OrdinalIgnoreCase));
    }
}
