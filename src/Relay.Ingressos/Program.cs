using Relay.Ingressos;

// O relé de webhook. Fica na NUVEM, nunca na máquina das catracas.
//
// Ele existe porque o provedor EMPURRA (webhook) e a borda não pode receber: a máquina
// local fica na mesma rede das catracas e não pode ter porta aberta para a internet.
// O relé recebe, guarda e espera — a borda vem buscar quando puder.
//
// Ver docs/ADR/ADR-0022-rele-de-webhook.md

const string VariavelTokenDeEntrada = "RELE_TOKEN_ENTRADA";
const string VariavelTokenDeLeitura = "RELE_TOKEN_LEITURA";
const string VariavelBanco = "RELE_BANCO";

// Um POST de webhook é pequeno. Um POST de 200 MB é alguém tentando encher o disco.
const int TamanhoMaximoDoCorpo = 1024 * 1024;

var construtor = WebApplication.CreateBuilder(args);
construtor.Services.AddSingleton(_ =>
{
    var armazenamento = new ArmazenamentoDeEntregas(
        Environment.GetEnvironmentVariable(VariavelBanco) ?? "entregas.db");
    armazenamento.Preparar();
    return armazenamento;
});

var aplicacao = construtor.Build();

var tokenDeEntrada = Segredos.Exigir(Environment.GetEnvironmentVariable(VariavelTokenDeEntrada), VariavelTokenDeEntrada);
var tokenDeLeitura = Segredos.Exigir(Environment.GetEnvironmentVariable(VariavelTokenDeLeitura), VariavelTokenDeLeitura);

// Dois segredos distintos, de propósito: o de entrada fica cadastrado no painel do
// provedor e sai do nosso controle; o de leitura vive só na máquina da borda. Vazar um
// não entrega o outro.
if (string.Equals(tokenDeEntrada, tokenDeLeitura, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"{VariavelTokenDeEntrada} e {VariavelTokenDeLeitura} precisam ser diferentes.");
}

aplicacao.MapGet("/saude", () => Results.Ok(new { estado = "ok" }));

// O provedor entrega aqui. O token vai no caminho porque é o que a tela de cadastro
// dele permite configurar: um campo de link, e nada mais.
aplicacao.MapPost("/webhook/{token}", async (
    string token,
    HttpRequest requisicao,
    ArmazenamentoDeEntregas armazenamento,
    CancellationToken cancelamento) =>
{
    if (!Segredos.Conferem(token, tokenDeEntrada))
    {
        // Sem detalhe: quem não tem o token não descobre nada por aqui.
        return Results.NotFound();
    }

    if (requisicao.ContentLength > TamanhoMaximoDoCorpo)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    using var memoria = new MemoryStream();
    await requisicao.Body.CopyToAsync(memoria, cancelamento).ConfigureAwait(false);

    if (memoria.Length > TamanhoMaximoDoCorpo)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    var cabecalhos = requisicao.Headers
        .Where(c => !Cabecalhos.EhSensivel(c.Key))
        .ToDictionary(c => c.Key, c => string.Join(", ", c.Value.ToArray()), StringComparer.OrdinalIgnoreCase);

    var seq = armazenamento.Gravar(
        memoria.ToArray(),
        requisicao.ContentType,
        cabecalhos,
        DateTimeOffset.UtcNow);

    // Responde rápido e sem interpretar nada. Provedor que recebe demora desiste e
    // marca como falha — e reentrega, ou pior, não reentrega.
    return Results.Accepted(value: new { recebido = seq });
});

// A borda vem buscar. Nunca o contrário.
aplicacao.MapGet("/entregas", (
    HttpRequest requisicao,
    ArmazenamentoDeEntregas armazenamento,
    long? desde,
    int? limite) =>
{
    if (!Segredos.Conferem(Portador(requisicao), tokenDeLeitura))
    {
        return Results.Unauthorized();
    }

    var cursor = Math.Max(desde ?? 0, 0);
    var tamanho = Math.Clamp(limite ?? 200, 1, 1000);

    var entregas = armazenamento.Desde(cursor, tamanho);
    var ultima = entregas.Count > 0 ? entregas[^1].Seq : cursor;

    return Results.Ok(new
    {
        entregas,
        ultimoSeq = ultima,
        temMais = entregas.Count == tamanho,
    });
});

await aplicacao.RunAsync().ConfigureAwait(false);

static string? Portador(HttpRequest requisicao)
{
    var cabecalho = requisicao.Headers.Authorization.ToString();
    return cabecalho.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? cabecalho["Bearer ".Length..].Trim()
        : null;
}

/// <summary>Âncora para os testes alcançarem este assembly.</summary>
public partial class Program
{
    private Program()
    {
    }
}
