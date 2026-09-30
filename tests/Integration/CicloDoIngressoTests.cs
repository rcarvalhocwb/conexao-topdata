using System.Net;
using Access.Application.Ingressos;
using Access.Domain.Devices;
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

    // ------------------------------------------------------------------------------------
    // Etapa B.2 do docs/35: tipo de entrada desativado nega, dentro do único UPDATE do
    // consumo; categoria sem tipo cadastrado continua valendo.
    // ------------------------------------------------------------------------------------

    private static IngressoRecebido IngressoDoTipo(string referencia, string? categoria, int usos = 1) =>
        new(Provedor, referencia, $"ZET-{referencia}", $"ZET-{referencia}", Setor: "pista",
            UsosMaximos: usos, Categoria: categoria);

    private static RepositorioDeIngressos PrepararComTipos(BancoTemporario banco, params IngressoRecebido[] ingressos)
    {
        banco.Migrar();
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(new ProvedorDeIngresso(Provedor, "Zet", "qr-maiusculo", "zet-rest"), Abertura);
        repositorio.Ingerir(ingressos, Abertura);
        return repositorio;
    }

    private static void GravarTipo(BancoTemporario banco, string codigo, bool ativo)
    {
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO ticket_type (code, display_name, sort_order, active, created_at)
            VALUES ($codigo, $codigo, 1, $ativo, $em)
            ON CONFLICT (code) DO UPDATE SET active = excluded.active;
            """;
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$ativo", ativo ? 1 : 0);
        comando.Parameters.AddWithValue("$em", Abertura.ToString("O"));
        comando.ExecuteNonQuery();
    }

    private static void GravarApelido(BancoTemporario banco, string apelido, string tipo)
    {
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            "INSERT INTO ticket_type_alias (provider_id, alias, type_code, created_at) VALUES ($p, $a, $t, $em);";
        comando.Parameters.AddWithValue("$p", Provedor);
        comando.Parameters.AddWithValue("$a", apelido);
        comando.Parameters.AddWithValue("$t", tipo);
        comando.Parameters.AddWithValue("$em", Abertura.ToString("O"));
        comando.ExecuteNonQuery();
    }

    private static long UsosFeitos(BancoTemporario banco, string qr)
    {
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT used_count FROM ticket WHERE qr_normalized = $qr;";
        comando.Parameters.AddWithValue("$qr", qr);
        return (long)comando.ExecuteScalar()!;
    }

    [Fact]
    public void Tipo_inativo_nega_com_TipoInativo_e_nao_consome()
    {
        using var banco = new BancoTemporario();
        var repositorio = PrepararComTipos(banco, IngressoDoTipo("M1", "MEIA"));
        GravarTipo(banco, "MEIA", ativo: false);

        var (uso, _) = repositorio.TentarUsar("ZET-M1", "portao-1", "catraca-01", Abertura.AddHours(1));

        Assert.False(uso.Liberou);
        Assert.Equal(MotivoDoUso.TipoInativo, uso.Motivo);
        Assert.Equal("MEIA", uso.Categoria);
        Assert.Equal(0, UsosFeitos(banco, "ZET-M1"));

        // Pela decisão da catraca, o motivo vira o código próprio do catálogo.
        var decisao = new DecisorDeIngresso(repositorio).Decidir(DeviceEvent.Create(
            new DeviceEventKey("catraca-01", "boot", 1),
            EventOrigin.From(KnownEventOrigin.Leitor1),
            Abertura.AddHours(1),
            "corr",
            rawCardData: "ZET-M1"));
        Assert.False(decisao.ShouldRelease);
        Assert.Equal("TIPO_INATIVO", decisao.Reason.Value);

        // A negativa entra na prestação de contas com o motivo próprio.
        Assert.Equal(2, repositorio.Conciliar(Provedor, Abertura.AddHours(12)).TentativasNegadas[MotivoDoUso.TipoInativo]);

        // Reativar o tipo devolve o ingresso: quem negou foi o tipo, e nada mais.
        GravarTipo(banco, "MEIA", ativo: true);
        Assert.True(repositorio.TentarUsar("ZET-M1", "portao-1", "catraca-01", Abertura.AddHours(1)).Resultado.Liberou);
    }

    [Fact]
    public void Tipo_inativo_nega_tambem_pelo_apelido_do_provedor()
    {
        // A Zet manda "Meia Entrada"; o operador mapeou essa grafia para MEIA.
        using var banco = new BancoTemporario();
        var repositorio = PrepararComTipos(banco, IngressoDoTipo("M2", "Meia Entrada"));
        GravarTipo(banco, "MEIA", ativo: false);
        GravarApelido(banco, "Meia Entrada", "MEIA");

        var (uso, _) = repositorio.TentarUsar("ZET-M2", "portao-1", "catraca-01", Abertura.AddHours(1));

        Assert.Equal(MotivoDoUso.TipoInativo, uso.Motivo);
        Assert.Equal(0, UsosFeitos(banco, "ZET-M2"));
    }

    [Fact]
    public void Categoria_sem_tipo_cadastrado_continua_valendo()
    {
        using var banco = new BancoTemporario();
        var repositorio = PrepararComTipos(
            banco,
            IngressoDoTipo("S1", "SOCIAL"),        // nenhum tipo SOCIAL cadastrado
            IngressoDoTipo("S2", null),            // sem categoria
            IngressoDoTipo("S3", "meia"),          // caixa diferente: não é o tipo MEIA
            IngressoDoTipo("S4", "INTEIRA"));      // tipo cadastrado e ativo

        // Existem tipos, e um deles está desativado: nada disso alcança quem não é dele.
        GravarTipo(banco, "MEIA", ativo: false);
        GravarTipo(banco, "INTEIRA", ativo: true);

        foreach (var referencia in new[] { "S1", "S2", "S3", "S4" })
        {
            var (uso, _) = repositorio.TentarUsar($"ZET-{referencia}", "portao-1", "catraca-01", Abertura.AddHours(1));
            Assert.True(uso.Liberou, $"{referencia} deveria valer: {uso.Motivo}");
        }
    }

    [Fact]
    public void Corrida_de_catracas_com_ingresso_de_tipo_ativo_ainda_tem_um_vencedor()
    {
        using var banco = new BancoTemporario();
        var repositorio = PrepararComTipos(banco, IngressoDoTipo("D1", "MEIA"));
        GravarTipo(banco, "MEIA", ativo: true);

        const int catracas = 8;
        var resultados = new ResultadoDoUso[catracas];
        using var largada = new Barrier(catracas);

        Parallel.For(0, catracas, i =>
        {
            largada.SignalAndWait();
            resultados[i] = repositorio.TentarUsar("ZET-D1", $"portao-{i}", $"catraca-{i:00}", Abertura.AddHours(1)).Resultado;
        });

        Assert.Single(resultados, r => r.Liberou);
        Assert.Equal(catracas - 1, resultados.Count(r => r.Motivo == MotivoDoUso.UsosEsgotados));
        Assert.Equal(1, UsosFeitos(banco, "ZET-D1"));
    }

    [Fact]
    public async Task Desativar_o_tipo_no_meio_da_corrida_nunca_consome_sem_liberar()
    {
        // Um ingresso de vários usos, oito catracas e o tipo sendo desativado ao mesmo tempo.
        // Seja qual for a ordem, a conta fecha: cada uso gravado é uma liberação, cada
        // negativa é "tipo inativo", e depois da desativação ninguém mais entra.
        using var banco = new BancoTemporario();
        var repositorio = PrepararComTipos(banco, IngressoDoTipo("V1", "MEIA", usos: 100));
        GravarTipo(banco, "MEIA", ativo: true);

        const int catracas = 8;
        var resultados = new ResultadoDoUso[catracas];
        using var largada = new Barrier(catracas + 1);

        var desativar = Task.Run(() =>
        {
            largada.SignalAndWait();
            GravarTipo(banco, "MEIA", ativo: false);
        });

        Parallel.For(0, catracas, i =>
        {
            largada.SignalAndWait();
            resultados[i] = repositorio.TentarUsar("ZET-V1", $"portao-{i}", $"catraca-{i:00}", Abertura.AddHours(1)).Resultado;
        });
        await desativar;

        Assert.All(resultados, r => Assert.True(r.Liberou || r.Motivo == MotivoDoUso.TipoInativo, r.Motivo.ToString()));
        Assert.Equal(resultados.Count(r => r.Liberou), UsosFeitos(banco, "ZET-V1"));
        Assert.Equal(MotivoDoUso.TipoInativo,
            repositorio.TentarUsar("ZET-V1", "portao-1", "catraca-01", Abertura.AddHours(1)).Resultado.Motivo);
    }
}
