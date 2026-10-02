using System.Net;
using System.Text;
using Sync.Connectors.Rest;
using Sync.Core;

namespace Unit.Tests.Conectores;

/// <summary>
/// O conector HTTP que leva o aviso de uso até o provedor.
/// </summary>
/// <remarks>
/// Ele decide uma coisa só: <b>se vale repetir</b>. Errar essa classificação tem dois
/// custos opostos — tratar falha de rede como recusa definitiva perde o aviso; tratar
/// recusa de conteúdo como temporária martela o destino por horas e atrasa a fila inteira.
/// Ver docs/15-integracao-e-sincronizacao.md, seção 7.
/// </remarks>
public sealed class ConectorRestTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private sealed class ManipuladorFalso(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requisicoes { get; } = [];

        public List<string> Corpos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requisicoes.Add(request);
            Corpos.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            return responder(request);
        }
    }

    private static ItemDeSaida Item(string id = "i1", string conteudo = """{"ingresso":"A1"}""") =>
        new(id, "zet", "ticket_use", "t1", conteudo, 5, $"uso:{id}:1", 0, Agora);

    private static (ConectorRest Conector, ManipuladorFalso Manipulador) Criar(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var manipulador = new ManipuladorFalso(responder);
        var http = new HttpClient(manipulador) { BaseAddress = new Uri("https://exemplo.invalid/") };
        return (new ConectorRest(http, new ConfiguracaoDoConectorRest("zet", "ingressos/uso")), manipulador);
    }

    private static ConectorRest Sempre(HttpStatusCode status) =>
        Criar(_ => new HttpResponseMessage(status)).Conector;

    private static async Task<ResultadoDoEnvio> EnviarUm(ConectorRest conector)
    {
        var respostas = await conector.EnviarAsync([Item()], CancellationToken.None);
        return Assert.Single(respostas).Resultado;
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Sucesso_e_aceito(HttpStatusCode status) =>
        Assert.Equal(ResultadoDoEnvio.Aceito, await EnviarUm(Sempre(status)));

    [Fact]
    public async Task Conflito_e_duplicado_e_conta_como_entregue()
    {
        // É o caminho normal depois de uma queda no meio do envio: o provedor gravou, a
        // resposta se perdeu na volta, e agora ele responde "já tenho".
        Assert.Equal(ResultadoDoEnvio.Duplicado, await EnviarUm(Sempre(HttpStatusCode.Conflict)));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Sobrecarga_e_indisponibilidade_mandam_repetir(HttpStatusCode status) =>
        Assert.Equal(ResultadoDoEnvio.FalhaTemporaria, await EnviarUm(Sempre(status)));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Recusa_de_conteudo_ou_de_permissao_nao_adianta_repetir(HttpStatusCode status)
    {
        // Martelar um 401 durante quatro horas não conserta a credencial e atrasa a fila
        // inteira. Vai para cartas mortas, onde alguém vê.
        Assert.Equal(ResultadoDoEnvio.FalhaPermanente, await EnviarUm(Sempre(status)));
    }

    [Fact]
    public async Task Rede_fora_manda_repetir()
    {
        var (conector, _) = Criar(_ => throw new HttpRequestException("Connection refused"));
        Assert.Equal(ResultadoDoEnvio.FalhaTemporaria, await EnviarUm(conector));
    }

    [Fact]
    public async Task Tempo_de_resposta_esgotado_manda_repetir()
    {
        // TaskCanceledException sem cancelamento pedido é o tempo do HttpClient, não uma
        // parada ordenada. Confundir os dois perderia o aviso.
        var (conector, _) = Criar(_ => throw new TaskCanceledException("tempo esgotado"));

        var respostas = await conector.EnviarAsync([Item()], CancellationToken.None);
        var resposta = Assert.Single(respostas);

        Assert.Equal(ResultadoDoEnvio.FalhaTemporaria, resposta.Resultado);
        Assert.Contains("tempo", resposta.Erro!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_chave_de_idempotencia_vai_no_cabecalho()
    {
        // Sem ela, uma resposta perdida na volta vira registro a mais no sistema do
        // provedor, e ninguém descobre até a conciliação não fechar.
        var (conector, manipulador) = Criar(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await conector.EnviarAsync([Item("i7")], CancellationToken.None);

        var requisicao = Assert.Single(manipulador.Requisicoes);
        Assert.True(requisicao.Headers.TryGetValues("Idempotency-Key", out var valores));
        Assert.Equal("uso:i7:1", Assert.Single(valores));
    }

    [Fact]
    public async Task O_corpo_enviado_e_exatamente_o_que_esta_na_outbox()
    {
        // Reescrever o conteúdo no meio do caminho tornaria impossível reproduzir o que
        // foi enviado a partir do que está gravado.
        const string conteudo = """{"ingresso":"A1","uso":1,"portao":"portao-3"}""";
        var (conector, manipulador) = Criar(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await conector.EnviarAsync([Item(conteudo: conteudo)], CancellationToken.None);

        Assert.Equal(conteudo, Assert.Single(manipulador.Corpos));
        Assert.Equal("application/json", manipulador.Requisicoes[0].Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task O_detalhe_do_erro_nao_carrega_o_corpo_da_resposta()
    {
        // O corpo pode devolver o ingresso inteiro, e esse texto vai para o banco e para a
        // tela do operador. O código de situação já diz o que precisa ser dito.
        var (conector, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"erro":"ingresso 0001234567 do CPF 000.000.000-00"}""", Encoding.UTF8),
        });

        var respostas = await conector.EnviarAsync([Item()], CancellationToken.None);
        var erro = Assert.Single(respostas).Erro!;

        Assert.Equal("HTTP 400 BadRequest", erro);
        Assert.DoesNotContain("0001234567", erro, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Um_item_recusado_nao_impede_os_outros_do_lote()
    {
        var (conector, _) = Criar(requisicao =>
            requisicao.Headers.GetValues("Idempotency-Key").Single().Contains("ruim", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                : new HttpResponseMessage(HttpStatusCode.OK));

        var respostas = await conector.EnviarAsync(
            [Item("bom1"), Item("ruim"), Item("bom2")],
            CancellationToken.None);

        Assert.Equal(3, respostas.Count);
        Assert.Equal(ResultadoDoEnvio.Aceito, respostas[0].Resultado);
        Assert.Equal(ResultadoDoEnvio.FalhaPermanente, respostas[1].Resultado);
        Assert.Equal(ResultadoDoEnvio.Aceito, respostas[2].Resultado);
    }

    [Fact]
    public async Task Cancelamento_propaga_e_nao_vira_veredito_sobre_o_item()
    {
        // Parada ordenada não é opinião sobre o item: o lote continua pendente, como se
        // nada tivesse acontecido.
        var (conector, _) = Criar(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var cancelamento = new CancellationTokenSource();
        await cancelamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => conector.EnviarAsync([Item()], cancelamento.Token));
    }

    [Fact]
    public void O_conector_nao_recebe_credencial_nenhuma()
    {
        // Autenticação é do HttpClient que lhe entregam. Não é elegância: um segredo não
        // pode vazar por aqui porque nunca passa por aqui.
        var propriedades = typeof(ConfiguracaoDoConectorRest).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(propriedades, nome =>
            nome.Contains("token", StringComparison.OrdinalIgnoreCase)
            || nome.Contains("senha", StringComparison.OrdinalIgnoreCase)
            || nome.Contains("chave", StringComparison.OrdinalIgnoreCase)
            || nome.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }
}
