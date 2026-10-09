using System.Net;
using System.Text;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// O serviço sincronizando com o painel na nuvem, do jeito que sobe em produção.
/// </summary>
/// <remarks>
/// O painel é um manipulador HTTP falso; o formato está em docs/22, seção 8, e o
/// comportamento detalhado do servidor é exercitado em <see cref="PainelNaNuvemTests"/>.
/// </remarks>
public sealed class SincronizacaoDoServicoTests
{
    private const string Base = "https://painel.invalid/functions/v1/";
    private const string Cartao = "0000000101";

    private sealed class Painel : HttpMessageHandler
    {
        public HttpStatusCode Situacao { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode SituacaoDasTentativas { get; set; } = HttpStatusCode.OK;

        public int CartoesNaLista { get; set; } = 1;

        public List<(string Caminho, string? Autorizacao, string Corpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), corpo));

            if (Situacao != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Situacao);
            }

            var cartoes = request.RequestUri.AbsolutePath.EndsWith("middleware-sync-cards", StringComparison.Ordinal);
            if (!cartoes && SituacaoDasTentativas != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(SituacaoDasTentativas);
            }

            var lista = string.Join(",", Enumerable.Range(0, CartoesNaLista).Select(i =>
                $$"""{"card_number":"{{(i == 0 ? Cartao : (1_000_000_000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture))}}","active":true,"max_uses":null,"admission_type":"meia"}"""));
            var resposta = cartoes
                ? $$"""{"success":true,"cards":[{{lista}}],"removed_cards":[],"sync_timestamp":"2026-11-14T20:00:00.000+00:00"}"""
                : """{"success":true,"saved":1,"failed":0,"duplicates_ignored":0,"failed_events":[]}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resposta, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Cenario : IDisposable
    {
        public const string Segredo = "segredo-de-teste-com-mais-de-32-caracteres";

        public Cenario(ConfiguracaoDaNuvem? configuracao = null, bool comSegredo = true)
        {
            Banco.Migrar();
            if (comSegredo)
            {
                Cofre.Gravar(CabecalhoDeSegredo.NomeDoSegredo, Segredo);
            }

            Sincronizacao = SincronizacaoComANuvem.Montar(
                configuracao ?? new ConfiguracaoDaNuvem(Base, "borda-01"),
                Banco.Fabrica,
                Cofre,
                Estado,
                Registro.Add,
                Painel);
        }

        public BancoTemporario Banco { get; } = new();

        public Painel Painel { get; } = new();

        public CofreEmMemoria Cofre { get; } = new();

        public EstadoDaNuvem Estado { get; } = new();

        public List<string> Registro { get; } = [];

        public SincronizacaoComANuvem Sincronizacao { get; }

        public void Dispose()
        {
            Sincronizacao.Dispose();
            Banco.Dispose();
        }
    }

    [Fact]
    public async Task Montar_liga_o_espelho_para_o_worker_e_a_rodada_traz_os_cartoes()
    {
        using var c = new Cenario();

        var (configuracao, _) = new ConfiguracoesDaBorda(c.Banco.Fabrica).Ler();
        Assert.True(configuracao.EspelhoLigado);
        Assert.Equal("painel-tentativas", configuracao.ConectorDoEspelho);

        Assert.True(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));
        Assert.NotNull(c.Estado.UltimoSucesso);
        Assert.True(c.Estado.Configurada);

        // O cartão que desceu passa na urna, com as regras da bilheteria.
        var repositorio = new RepositorioDeIngressos(c.Banco.Fabrica);
        var agora = DateTimeOffset.UtcNow;
        Assert.Equal(
            Access.Domain.Ticketing.MotivoDoUso.ForaDaUrna,
            repositorio.TentarUsar(Cartao, "p1", "inner-1", agora, leitor: KnownEventOrigin.Leitor1).Resultado.Motivo);
        Assert.True(repositorio.TentarUsar(Cartao, "p1", "inner-1", agora, leitor: KnownEventOrigin.Leitor2).Resultado.Liberou);
    }

    /// <summary>
    /// Achado E8-1 do docs/41: sem segredo gravado, as requisições saíam sem credencial e só uma linha
    /// no registro avisava. Agora nada sai, e o painel diz o que falta; gravado o segredo, a próxima
    /// rodada já vai com ele, sem reiniciar o serviço.
    /// </summary>
    [Fact]
    public async Task Sem_segredo_nada_sai_e_o_painel_diz_o_que_falta_com_segredo_ele_vai_no_cabecalho()
    {
        using var c = new Cenario(comSegredo: false);

        Assert.False(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));
        Assert.Empty(c.Painel.Pedidos);
        Assert.Equal(SincronizacaoComANuvem.FaltaOSegredo, c.Estado.UltimaFalha);

        c.Cofre.Gravar(CabecalhoDeSegredo.NomeDoSegredo, Cenario.Segredo);
        Assert.True(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));

        Assert.NotEmpty(c.Painel.Pedidos);
        Assert.All(c.Painel.Pedidos, p => Assert.Equal($"Bearer {Cenario.Segredo}", p.Autorizacao));
        Assert.DoesNotContain(c.Registro, l => l.Contains(Cenario.Segredo, StringComparison.Ordinal));
    }

    /// <summary>
    /// Achado E5-1 do docs/41: a lista de cartões cortada pelo limite do servidor (1.000; o evento tem
    /// 2.243) dava a rodada por bem-sucedida, e o painel mostrava "nuvem ok" com cartões faltando.
    /// </summary>
    [Fact]
    public async Task Lista_de_cartoes_possivelmente_cortada_faz_a_rodada_falhar_com_o_motivo()
    {
        using var c = new Cenario();
        c.Painel.CartoesNaLista = 1000;

        Assert.False(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));
        Assert.Null(c.Estado.UltimoSucesso);
        Assert.Contains("pode ter sido cortada", c.Estado.UltimaFalha, StringComparison.Ordinal);

        // A lista volta inteira: a rodada seguinte dá certo e o aviso some.
        c.Painel.CartoesNaLista = 1;
        Assert.True(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));
        Assert.Null(c.Estado.UltimaFalha);
    }

    /// <summary>
    /// Achados E5-2 e E5-3 do docs/41: o 401 mandava as tentativas para cartas mortas sem volta, e
    /// o device_id ia "inner-N", que não bate com o segredo do equipamento. Agora o 401 deixa a
    /// tentativa na fila com o motivo no painel, e o device_id é o da borda.
    /// </summary>
    [Fact]
    public async Task Credencial_recusada_mantem_a_tentativa_na_fila_e_o_device_id_e_o_da_borda()
    {
        using var c = new Cenario();
        var fila = new FilaDeSaidaSqlite(c.Banco.Fabrica);
        using (var conexao = c.Banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                INSERT INTO outbox (id, aggregate_type, aggregate_id, payload_json, priority, connector, idempotency_key, created_at)
                VALUES ('t1', 'tentativa', 't1', $payload, 5, 'painel-tentativas', 'tentativa:t1', $agora);
                """;
            comando.Parameters.AddWithValue("$payload", new Access.Domain.Ticketing.TentativaEspelhada(
                1, Guid.NewGuid(), Cartao, "inner-3", "portao-1", DateTimeOffset.UtcNow, true,
                "Consumido", "bilheteria-local", "meia", null).ParaJson());
            comando.Parameters.AddWithValue("$agora", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            comando.ExecuteNonQuery();
        }

        c.Painel.SituacaoDasTentativas = HttpStatusCode.Unauthorized;
        Assert.False(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));

        Assert.Equal(0, fila.ContarCartasMortas());
        Assert.Equal(1L, fila.BacklogPorConector()["painel-tentativas"]);
        Assert.Contains("recusou a credencial", c.Estado.UltimaFalha, StringComparison.Ordinal);

        var envio = c.Painel.Pedidos.Last(p => p.Caminho.EndsWith("middleware-sync-events", StringComparison.Ordinal));
        Assert.Contains("\"device_id\":\"borda-01\"", envio.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"catraca\":\"inner-3\"", envio.Corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_internet_a_rodada_falha_informa_e_nao_derruba_nada()
    {
        using var c = new Cenario();
        c.Painel.Situacao = HttpStatusCode.ServiceUnavailable;

        Assert.False(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));
        Assert.Null(c.Estado.UltimoSucesso);
        Assert.Contains("cartões", c.Estado.UltimaFalha, StringComparison.Ordinal);

        c.Painel.Situacao = HttpStatusCode.OK;
        Assert.True(await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None));
        Assert.Null(c.Estado.UltimaFalha);
    }

    [Theory]
    [InlineData("http://painel.invalid/functions/v1/", "https")]
    [InlineData("https://painel.invalid/functions/v1", "terminar com '/'")]
    [InlineData("não é endereço", "https")]
    public void Configuracao_da_nuvem_errada_e_recusada_antes_de_subir(string endereco, string trecho)
    {
        var problemas = new ConfiguracaoDaNuvem(endereco, "borda-01").Validar();
        Assert.Contains(problemas, p => p.Contains(trecho, StringComparison.Ordinal));
    }

    [Fact]
    public void Perfil_de_cartao_desconhecido_e_recusado()
    {
        var problemas = new ConfiguracaoDaNuvem(Base, "borda-01", Perfil: "mifare-12").Validar();
        Assert.Contains(problemas, p => p.Contains("mifare-12", StringComparison.Ordinal));
    }

    [Fact]
    public void O_exemplo_de_configuracao_do_instalador_e_lido_e_passa_na_validacao_da_nuvem()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);

        var configuracao = ConfiguracaoDoSupervisor.Ler(Path.Combine(raiz.FullName, "installer", "workers.exemplo.json"));

        Assert.NotNull(configuracao.Nuvem);
        Assert.Empty(configuracao.Nuvem.Validar());
        Assert.Equal("raw", configuracao.Nuvem.Perfil);
        Assert.EndsWith("acesso.db", configuracao.CaminhoDoBanco, StringComparison.Ordinal);
    }
}
