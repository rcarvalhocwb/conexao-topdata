using System.Net;
using System.Text;
using Access.Application.Ingressos;
using Access.Domain.Credentials;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Sync.Connectors.Rest.Painel;

namespace Integration.Tests;

/// <summary>
/// Leitura, cadastro, sincronização e consulta comparando o mesmo texto (docs/34 §2,
/// defeito F8; docs/35, Etapa 0.7), contra a base real.
/// </summary>
/// <remarks>
/// O perfil que completa zeros (<c>mifare-catraca4</c>) é o caso que expõe a assimetria;
/// com <c>raw</c>, o padrão de hoje, tudo precisa continuar exatamente como era. Números
/// fictícios.
/// </remarks>
public sealed class NormalizacaoSimetricaTests : IDisposable
{
    private const string Bilheteria = "bilheteria-local";
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 21, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repositorio;
    private readonly ConsultasDaOperacao _consultas;

    public NormalizacaoSimetricaTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _consultas = new ConsultasDaOperacao(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private sealed class Painel(string corpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
            });
    }

    private void Provedor(CredentialNormalization perfil) =>
        _repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Bilheteria, "Bilheteria", perfil.Name, "", Reutilizavel: true),
            Agora);

    /// <summary>Uma sincronização completa com o painel: lê e grava.</summary>
    private async Task<ResultadoDaIngestao> Sincronizar(CredentialNormalization perfil, string[] cartoes, string[]? removidos = null)
    {
        var lista = string.Join(",", cartoes.Select(c => $$"""{"card_number":"{{c}}","active":true,"admission_type":"inteira"}"""));
        var fora = string.Join(",", (removidos ?? []).Select(r => $"\"{r}\""));
        var corpo = $$"""{"success":true,"cards":[{{lista}}],"removed_cards":[{{fora}}],"sync_timestamp":"2026-11-14T20:00:00.000+00:00"}""";

        using var http = new HttpClient(new Painel(corpo)) { BaseAddress = new Uri("https://painel.invalid/functions/v1/") };
        var fonte = new FonteDeCartoesDoPainel(http, Bilheteria, "borda-01", perfil);
        var pagina = await fonte.LerAsync(null, CancellationToken.None);
        return _repositorio.Ingerir(pagina.Itens, Agora);
    }

    private List<(string Referencia, string Codigo, string Situacao)> Cartoes()
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT external_ref, qr_normalized, status FROM ticket ORDER BY external_ref;";
        using var leitor = comando.ExecuteReader();
        var lista = new List<(string, string, string)>();
        while (leitor.Read())
        {
            lista.Add((leitor.GetString(0), leitor.GetString(1), leitor.GetString(2)));
        }

        return lista;
    }

    private static DeviceEvent Leitura(string codigo) =>
        DeviceEvent.Create(
            new DeviceEventKey("inner-1", "boot", 1),
            EventOrigin.From(KnownEventOrigin.Leitor2),
            Agora,
            "corr",
            rawCardData: codigo);

    [Fact]
    public async Task Reenvio_do_mesmo_cartao_e_atualizacao_e_o_cancelamento_acha_o_cartao()
    {
        var perfil = PerfisDeLeitura.MifareCatraca4;
        Provedor(perfil);

        var primeira = await Sincronizar(perfil, ["99994567"]);
        var segunda = await Sincronizar(perfil, ["99994567"]);

        Assert.Equal((1, 0), (primeira.Inseridos, primeira.Atualizados));
        Assert.Equal((0, 1), (segunda.Inseridos, segunda.Atualizados));
        Assert.Empty(segunda.Colisoes);
        Assert.Equal([("0099994567", "0099994567", "valido")], Cartoes());

        // A catraca entrega os 10 dígitos; a leitura (perfil raw, o de hoje) casa com o cadastro.
        var decisao = new DecisorDeIngresso(_repositorio).Decidir(Leitura("0099994567"));
        Assert.True(decisao.ShouldRelease);

        var cancelamento = await Sincronizar(perfil, [], removidos: ["99994567"]);

        Assert.Empty(cancelamento.Colisoes);
        Assert.Equal(1, cancelamento.Atualizados);
        Assert.Equal([("0099994567", "0099994567", "cancelado")], Cartoes());
    }

    /// <summary>
    /// Base sincronizada antes da Etapa 0.7, com a referência bruta: o reenvio adota a
    /// referência normalizada em vez de virar colisão, e o cancelamento continua valendo.
    /// </summary>
    [Fact]
    public async Task Cartao_gravado_com_a_referencia_bruta_e_adotado_sem_duplicar_nem_colidir()
    {
        var perfil = PerfisDeLeitura.MifareCatraca4;
        Provedor(perfil);
        _repositorio.Ingerir([new IngressoRecebido(Bilheteria, "99994567", "99994567", "0099994567", UsosMaximos: 5)], Agora);

        var reenvio = await Sincronizar(perfil, ["99994567"]);

        Assert.Empty(reenvio.Colisoes);
        Assert.Equal((0, 1), (reenvio.Inseridos, reenvio.Atualizados));
        Assert.Equal([("0099994567", "0099994567", "valido")], Cartoes());

        var cancelamento = await Sincronizar(perfil, [], removidos: ["99994567"]);

        Assert.Empty(cancelamento.Colisoes);
        Assert.Equal([("0099994567", "0099994567", "cancelado")], Cartoes());
    }

    /// <summary>Com o perfil raw, o padrão de hoje, a referência é o mesmo texto de antes.</summary>
    [Fact]
    public async Task Com_o_perfil_raw_nada_muda_e_os_zeros_ficam()
    {
        var perfil = CredentialNormalization.Raw;
        Provedor(perfil);

        await Sincronizar(perfil, ["0000000101"]);
        var reenvio = await Sincronizar(perfil, ["0000000101"]);

        Assert.Equal((0, 1), (reenvio.Inseridos, reenvio.Atualizados));
        Assert.Equal([("0000000101", "0000000101", "valido")], Cartoes());

        // Zeros à esquerda nunca são removidos nem acrescentados pelo raw: "101" é outro código.
        Assert.NotNull(_consultas.ConsultarCodigo(" 0000000101 "));
        Assert.Null(_consultas.ConsultarCodigo("101"));
        Assert.False(new DecisorDeIngresso(_repositorio).Decidir(Leitura("101")).ShouldRelease);
    }

    /// <summary>
    /// O operador digita o número como está impresso, sem os zeros; a consulta aplica o
    /// perfil do provedor e acha o cartão, com o código mascarado.
    /// </summary>
    [Fact]
    public async Task A_consulta_aplica_o_perfil_do_provedor_ao_codigo_digitado()
    {
        var perfil = PerfisDeLeitura.MifareCatraca4;
        Provedor(perfil);
        await Sincronizar(perfil, ["99994567"]);

        var achado = _consultas.ConsultarCodigo("99994567");

        Assert.NotNull(achado);
        Assert.Equal(CredentialValue.Mascarar("0099994567"), achado.CodigoMascarado);
        Assert.DoesNotContain("99994567", achado.CodigoMascarado, StringComparison.Ordinal);
        Assert.NotNull(_consultas.ConsultarCodigo("0099994567"));
        Assert.Null(_consultas.ConsultarCodigo("9999456"));
    }
}
