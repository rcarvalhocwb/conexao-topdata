using System.Net;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Sync.Connectors.Rest;
using Sync.Core;
using Sync.Ingestao;

namespace Integration.Tests;

/// <summary>
/// A volta inteira: o provedor publica, a catraca valida, o provedor recebe de volta.
/// </summary>
/// <remarks>
/// Os testes de unidade provam cada peça isolada. Este prova que elas se encaixam — que é
/// onde integração costuma quebrar. Nada aqui usa rede: o provedor é um manipulador HTTP
/// falso, e o banco é um SQLite descartável.
/// Ver docs/16-multiplos-provedores-de-ingresso.md
/// </remarks>
public sealed class CicloDoIngressoTests
{
    private const string Provedor = "zet";
    private static readonly DateTimeOffset Abertura = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private sealed class ProvedorFalso : IFonteDeIngressos
    {
        private readonly Queue<PaginaDeIngressos> _paginas;

        public ProvedorFalso(params PaginaDeIngressos[] paginas) => _paginas = new(paginas);

        public string Provedor => CicloDoIngressoTests.Provedor;

        public Task<PaginaDeIngressos> LerAsync(string? cursor, CancellationToken cancelamento) =>
            Task.FromResult(_paginas.Count > 0 ? _paginas.Dequeue() : PaginaDeIngressos.Vazia);
    }

    private sealed class RecebedorFalso(Func<string, HttpStatusCode> responder) : HttpMessageHandler
    {
        public List<string> Avisos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var corpo = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Avisos.Add(corpo);
            return new HttpResponseMessage(responder(corpo));
        }
    }

    private static IngressoRecebido Ingresso(string referencia) =>
        new(Provedor, referencia, $"ZET-{referencia}", $"ZET-{referencia}", Setor: "pista");

    [Fact]
    public async Task Do_provedor_ate_a_catraca_e_de_volta_ao_provedor()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Provedor, "Zet", "qr-maiusculo", "zet-rest"), Abertura);

        // 1. O provedor publica dois ingressos; a borda puxa.
        var fonte = new ProvedorFalso(
            new PaginaDeIngressos([Ingresso("A1"), Ingresso("A2")], "venda-1002", TemMais: false));

        var ingestao = new LacoDeIngestao(fonte, repositorio, new CursoresSqlite(banco.Fabrica));
        var resumo = await ingestao.PuxarAsync(CancellationToken.None);

        Assert.Equal(2, resumo.Inseridos);
        Assert.Equal("venda-1002", new CursoresSqlite(banco.Fabrica).Ler(Provedor, LacoDeIngestao.FluxoIncremental));

        // 2. A pessoa apresenta o QR na catraca. Nada disso encosta na internet.
        var (uso, tentativa) = repositorio.TentarUsar("ZET-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        Assert.True(uso.Liberou);
        repositorio.ConfirmarPassagemFisica(tentativa, Abertura.AddHours(1).AddSeconds(3));

        // 3. O aviso de uso já está na fila. O provedor recebe quando houver internet.
        var recebedor = new RecebedorFalso(_ => HttpStatusCode.OK);
        using var http = new HttpClient(recebedor) { BaseAddress = new Uri("https://zet.invalid/") };
        var conector = new ConectorRest(http, new ConfiguracaoDoConectorRest("zet-rest", "ingressos/uso"));

        var drenador = new DrenadorDaOutbox(
            new FilaDeSaidaSqlite(banco.Fabrica), [conector], _ => TimeSpan.FromSeconds(1));

        var drenagem = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, drenagem.Enviados);
        Assert.Contains("\"ingresso\":\"A1\"", Assert.Single(recebedor.Avisos), StringComparison.Ordinal);

        // 4. E a conta fecha.
        repositorio.ConfirmarAvisoDeUso(uso.IngressoId!.Value, Abertura.AddHours(1).AddSeconds(9));
        var conta = repositorio.Conciliar(Provedor, Abertura.AddHours(12));

        Assert.Equal(2, conta.IngressosRecebidos);
        Assert.Equal(1, conta.UsosComPassagemFisica);
        Assert.Equal(1, conta.NuncaUsados);
        Assert.True(conta.Fecha);
    }

    [Fact]
    public async Task Sem_internet_a_catraca_continua_validando_e_o_aviso_espera()
    {
        // O regime normal de um evento. A pessoa entra igual; o que para é a sincronização.
        using var banco = new BancoTemporario();
        banco.Migrar();

        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Provedor, "Zet", "qr-maiusculo", "zet-rest"), Abertura);
        repositorio.Ingerir([Ingresso("A1")], Abertura);

        var (uso, _) = repositorio.TentarUsar("ZET-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        Assert.True(uso.Liberou);

        var fila = new FilaDeSaidaSqlite(banco.Fabrica);

        // Internet fora: o destino não responde.
        var foraDoAr = new RecebedorFalso(_ => HttpStatusCode.ServiceUnavailable);
        using var httpRuim = new HttpClient(foraDoAr) { BaseAddress = new Uri("https://zet.invalid/") };
        var drenadorRuim = new DrenadorDaOutbox(
            fila, [new ConectorRest(httpRuim, new ConfiguracaoDoConectorRest("zet-rest", "ingressos/uso"))],
            _ => TimeSpan.Zero);

        Assert.Equal(1, (await drenadorRuim.DrenarUmaVezAsync(CancellationToken.None)).Adiados);
        Assert.Equal(1L, fila.BacklogPorConector()["zet-rest"]);
        Assert.Equal(0L, fila.ContarCartasMortas());

        // Internet volta: o mesmo aviso sobe, sem nada ter sido perdido.
        var deVolta = new RecebedorFalso(_ => HttpStatusCode.OK);
        using var httpBom = new HttpClient(deVolta) { BaseAddress = new Uri("https://zet.invalid/") };
        var drenadorBom = new DrenadorDaOutbox(
            fila, [new ConectorRest(httpBom, new ConfiguracaoDoConectorRest("zet-rest", "ingressos/uso"))],
            _ => TimeSpan.Zero);

        Assert.Equal(1, (await drenadorBom.DrenarUmaVezAsync(CancellationToken.None)).Enviados);
        Assert.Empty(fila.BacklogPorConector());
    }

    [Fact]
    public async Task O_provedor_dizendo_que_ja_tinha_conta_como_entregue()
    {
        // Queda no meio do envio: ele gravou, a resposta se perdeu. Repetir e ouvir
        // "já tenho" é sucesso, não erro.
        using var banco = new BancoTemporario();
        banco.Migrar();

        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Provedor, "Zet", "qr-maiusculo", "zet-rest"), Abertura);
        repositorio.Ingerir([Ingresso("A1")], Abertura);
        repositorio.TentarUsar("ZET-A1", "portao-1", "catraca-01", Abertura.AddHours(1));

        var recebedor = new RecebedorFalso(_ => HttpStatusCode.Conflict);
        using var http = new HttpClient(recebedor) { BaseAddress = new Uri("https://zet.invalid/") };
        var fila = new FilaDeSaidaSqlite(banco.Fabrica);
        var drenador = new DrenadorDaOutbox(
            fila, [new ConectorRest(http, new ConfiguracaoDoConectorRest("zet-rest", "ingressos/uso"))],
            _ => TimeSpan.Zero);

        Assert.Equal(1, (await drenador.DrenarUmaVezAsync(CancellationToken.None)).Enviados);
        Assert.Empty(fila.BacklogPorConector());
        Assert.Equal(0L, fila.ContarCartasMortas());
    }

    [Fact]
    public async Task O_cursor_sobrevive_ao_reinicio_do_processo()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Provedor, "Zet", "qr-maiusculo", "zet-rest"), Abertura);

        await new LacoDeIngestao(
                new ProvedorFalso(new PaginaDeIngressos([Ingresso("A1")], "venda-500", TemMais: false)),
                repositorio,
                new CursoresSqlite(banco.Fabrica))
            .PuxarAsync(CancellationToken.None);

        // Tudo reconstruído do zero, como depois de um reinício do serviço.
        var cursoresNovos = new CursoresSqlite(new SqliteConnectionFactory(banco.Caminho));

        Assert.Equal("venda-500", cursoresNovos.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }
}
