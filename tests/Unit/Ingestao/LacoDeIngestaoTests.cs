using Access.Domain.Ticketing;
using Sync.Ingestao;

namespace Unit.Tests.Ingestao;

/// <summary>
/// O laço que puxa ingressos do provedor, sem perder nenhum.
/// </summary>
/// <remarks>
/// A promessa é "ao menos uma vez", e ela depende de uma regra só: o cursor avança
/// <b>depois</b> de gravar, nunca antes. Ver docs/16-multiplos-provedores-de-ingresso.md
/// </remarks>
public sealed class LacoDeIngestaoTests
{
    private const string Provedor = "zet";
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private static IngressoRecebido Ingresso(string referencia) =>
        new(Provedor, referencia, $"QR-{referencia}", $"QR-{referencia}");

    private sealed class CursoresEmMemoria : IArmazenamentoDeCursor
    {
        private readonly Dictionary<(string, string), string> _valores = [];

        public string? Ler(string conector, string fluxo) =>
            _valores.TryGetValue((conector, fluxo), out var v) ? v : null;

        public void Gravar(string conector, string fluxo, string cursor, DateTimeOffset agora) =>
            _valores[(conector, fluxo)] = cursor;

        public void Apagar(string conector, string fluxo) => _valores.Remove((conector, fluxo));
    }

    private sealed class FonteFalsa : IFonteDeIngressos
    {
        private readonly Dictionary<string, PaginaDeIngressos> _porCursor;

        public FonteFalsa(Dictionary<string, PaginaDeIngressos> porCursor) => _porCursor = porCursor;

        public string Provedor => LacoDeIngestaoTests.Provedor;

        public List<string?> CursoresPedidos { get; } = [];

        public Func<Exception>? Explodir { get; set; }

        public Task<PaginaDeIngressos> LerAsync(string? cursor, CancellationToken cancelamento)
        {
            CursoresPedidos.Add(cursor);

            if (Explodir is not null)
            {
                throw Explodir();
            }

            return Task.FromResult(_porCursor.TryGetValue(cursor ?? "", out var p) ? p : PaginaDeIngressos.Vazia);
        }
    }

    private sealed class DestinoFalso : IDestinoDeIngressos
    {
        public List<IReadOnlyCollection<IngressoRecebido>> Lotes { get; } = [];

        public Func<Exception>? Explodir { get; set; }

        public IReadOnlyList<ColisaoDeQr> ProximaColisao { get; set; } = [];

        public ResultadoDaIngestao Aplicar(IReadOnlyCollection<IngressoRecebido> lote, DateTimeOffset agora)
        {
            if (Explodir is not null)
            {
                throw Explodir();
            }

            Lotes.Add(lote);
            return new ResultadoDaIngestao(lote.Count, 0, ProximaColisao, 0);
        }
    }

    private static PaginaDeIngressos Pagina(string proximoCursor, bool temMais, params string[] referencias) =>
        new([.. referencias.Select(Ingresso)], proximoCursor, temMais);

    [Fact]
    public async Task Cursor_avanca_e_fica_gravado()
    {
        var fonte = new FonteFalsa(new() { [""] = Pagina("c1", temMais: false, "A1", "A2") });
        var destino = new DestinoFalso();
        var cursores = new CursoresEmMemoria();

        var resumo = await new LacoDeIngestao(fonte, destino, cursores, new RelogioParado())
            .PuxarAsync(CancellationToken.None);

        Assert.Equal(2, resumo.Inseridos);
        Assert.Equal("c1", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Retomada_depois_de_reinicio_le_do_cursor_gravado()
    {
        var fonte = new FonteFalsa(new() { ["c1"] = Pagina("c2", temMais: false, "A3") });
        var cursores = new CursoresEmMemoria();
        cursores.Gravar(Provedor, LacoDeIngestao.FluxoIncremental, "c1", Agora);

        await new LacoDeIngestao(fonte, new DestinoFalso(), cursores, new RelogioParado())
            .PuxarAsync(CancellationToken.None);

        Assert.Equal(["c1"], fonte.CursoresPedidos);
    }

    [Fact]
    public async Task Le_varias_paginas_enquanto_o_provedor_disser_que_tem_mais()
    {
        var fonte = new FonteFalsa(new()
        {
            [""] = Pagina("c1", temMais: true, "A1"),
            ["c1"] = Pagina("c2", temMais: true, "A2"),
            ["c2"] = Pagina("c3", temMais: false, "A3"),
        });
        var destino = new DestinoFalso();
        var cursores = new CursoresEmMemoria();

        var resumo = await new LacoDeIngestao(fonte, destino, cursores, new RelogioParado())
            .PuxarAsync(CancellationToken.None);

        Assert.Equal(3, resumo.Paginas);
        Assert.Equal(3, resumo.Inseridos);
        Assert.Equal("c3", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Respeita_o_teto_de_paginas_por_rodada()
    {
        // Uma carga inicial de cinquenta mil ingressos não pode monopolizar o processo e
        // atrasar a drenagem dos avisos de uso, que é o que o provedor está esperando.
        var fonte = new FonteFalsa(new()
        {
            [""] = Pagina("c1", temMais: true, "A1"),
            ["c1"] = Pagina("c2", temMais: true, "A2"),
            ["c2"] = Pagina("c3", temMais: true, "A3"),
        });
        var cursores = new CursoresEmMemoria();

        var resumo = await new LacoDeIngestao(fonte, new DestinoFalso(), cursores, new RelogioParado(), maximoDePaginasPorRodada: 2)
            .PuxarAsync(CancellationToken.None);

        Assert.Equal(2, resumo.Paginas);
        // E a rodada seguinte continua exatamente de onde parou.
        Assert.Equal("c2", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Provedor_fora_do_ar_nao_move_o_cursor()
    {
        var fonte = new FonteFalsa(new() { [""] = Pagina("c1", temMais: false, "A1") })
        {
            Explodir = () => new HttpRequestException("sem resposta"),
        };
        var cursores = new CursoresEmMemoria();

        var resumo = await new LacoDeIngestao(fonte, new DestinoFalso(), cursores, new RelogioParado())
            .PuxarAsync(CancellationToken.None);

        Assert.True(resumo.Interrompido);
        Assert.Null(cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Falha_ao_gravar_nao_move_o_cursor_e_a_pagina_e_relida()
    {
        // A regra que sustenta tudo. Reler é barato porque o destino é idempotente;
        // perder um ingresso é caro porque a pessoa é barrada com o ingresso pago na mão.
        var fonte = new FonteFalsa(new() { [""] = Pagina("c1", temMais: false, "A1", "A2") });
        var destino = new DestinoFalso { Explodir = () => new InvalidOperationException("banco travado") };
        var cursores = new CursoresEmMemoria();
        var laco = new LacoDeIngestao(fonte, destino, cursores, new RelogioParado());

        var primeira = await laco.PuxarAsync(CancellationToken.None);

        Assert.True(primeira.Interrompido);
        Assert.Equal(0, primeira.Inseridos);
        Assert.Null(cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));

        // Banco volta: a mesma página é lida de novo, do mesmo cursor.
        destino.Explodir = null;
        var segunda = await laco.PuxarAsync(CancellationToken.None);

        Assert.Equal(2, segunda.Inseridos);
        Assert.Equal([null, null], fonte.CursoresPedidos);
        Assert.Equal("c1", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Pagina_vazia_nao_move_nada()
    {
        var fonte = new FonteFalsa([]);
        var destino = new DestinoFalso();
        var cursores = new CursoresEmMemoria();

        var resumo = await new LacoDeIngestao(fonte, destino, cursores, new RelogioParado())
            .PuxarAsync(CancellationToken.None);

        Assert.False(resumo.TeveTrabalho);
        Assert.Empty(destino.Lotes);
        Assert.Null(cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Colisao_de_qr_nao_interrompe_a_ingestao()
    {
        var fonte = new FonteFalsa(new()
        {
            [""] = Pagina("c1", temMais: true, "A1"),
            ["c1"] = Pagina("c2", temMais: false, "A2"),
        });
        var destino = new DestinoFalso
        {
            ProximaColisao = [new ColisaoDeQr("QR-A1", "outro", "X9", Provedor, "A1")],
        };
        var cursores = new CursoresEmMemoria();

        var resumo = await new LacoDeIngestao(fonte, destino, cursores, new RelogioParado())
            .PuxarAsync(CancellationToken.None);

        Assert.Equal(2, resumo.Paginas);
        Assert.Equal(2, resumo.Colisoes.Count);
        Assert.True(resumo.ExigeAtencao);
        Assert.False(resumo.Interrompido);
        Assert.Equal("c2", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    [Fact]
    public async Task Varredura_completa_nunca_mexe_no_cursor_do_incremental()
    {
        // Se mexesse, o incremental andaria para trás e reprocessaria horas de fila no
        // pior momento possível — durante o evento.
        var fonte = new FonteFalsa(new()
        {
            [""] = Pagina("v1", temMais: false, "A1"),
            ["c9"] = Pagina("c10", temMais: false, "A2"),
        });
        var cursores = new CursoresEmMemoria();
        cursores.Gravar(Provedor, LacoDeIngestao.FluxoIncremental, "c9", Agora);

        var laco = new LacoDeIngestao(fonte, new DestinoFalso(), cursores, new RelogioParado());
        await laco.VarrerTudoAsync(CancellationToken.None);

        Assert.Equal("c9", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
        Assert.Equal("v1", cursores.Ler(Provedor, LacoDeIngestao.FluxoCompleto));
    }

    [Fact]
    public async Task Varredura_interrompida_retoma_de_onde_parou()
    {
        // Numa base de trinta mil ingressos, retomar e recomeçar é a diferença entre
        // terminar e nunca terminar.
        var fonte = new FonteFalsa(new()
        {
            [""] = Pagina("v1", temMais: true, "A1"),
            ["v1"] = Pagina("v2", temMais: true, "A2"),
        });
        var cursores = new CursoresEmMemoria();
        var laco = new LacoDeIngestao(fonte, new DestinoFalso(), cursores, new RelogioParado(), maximoDePaginasPorRodada: 1);

        await laco.VarrerTudoAsync(CancellationToken.None);
        await laco.VarrerTudoAsync(CancellationToken.None);

        Assert.Equal([null, "v1"], fonte.CursoresPedidos);
        Assert.Equal("v2", cursores.Ler(Provedor, LacoDeIngestao.FluxoCompleto));
    }

    [Fact]
    public async Task Reiniciar_varredura_apaga_so_o_cursor_da_varredura()
    {
        var fonte = new FonteFalsa(new() { [""] = Pagina("v1", temMais: false, "A1") });
        var cursores = new CursoresEmMemoria();
        cursores.Gravar(Provedor, LacoDeIngestao.FluxoIncremental, "c9", Agora);

        var laco = new LacoDeIngestao(fonte, new DestinoFalso(), cursores, new RelogioParado());
        await laco.VarrerTudoAsync(CancellationToken.None);
        laco.ReiniciarVarredura();

        Assert.Null(cursores.Ler(Provedor, LacoDeIngestao.FluxoCompleto));
        Assert.Equal("c9", cursores.Ler(Provedor, LacoDeIngestao.FluxoIncremental));
    }

    private sealed class RelogioParado : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
