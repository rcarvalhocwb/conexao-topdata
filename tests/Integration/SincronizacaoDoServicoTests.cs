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

        public List<(string Caminho, string? Autorizacao, string Corpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), corpo));

            if (Situacao != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(Situacao);
            }

            var resposta = request.RequestUri.AbsolutePath.EndsWith("middleware-sync-cards", StringComparison.Ordinal)
                ? $$"""{"success":true,"cards":[{"card_number":"{{Cartao}}","active":true,"max_uses":null,"admission_type":"meia"}],"removed_cards":[],"sync_timestamp":"2026-11-14T20:00:00.000+00:00"}"""
                : """{"success":true,"saved":1,"failed":0,"duplicates_ignored":0,"failed_events":[]}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resposta, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Cenario : IDisposable
    {
        public Cenario(ConfiguracaoDaNuvem? configuracao = null)
        {
            Banco.Migrar();
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

    [Fact]
    public async Task O_segredo_sai_do_cofre_para_o_cabecalho_e_nunca_para_o_registro()
    {
        using var c = new Cenario();
        const string Segredo = "segredo-de-teste-com-mais-de-32-caracteres";

        await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None);
        Assert.All(c.Painel.Pedidos, p => Assert.Null(p.Autorizacao));

        c.Cofre.Gravar(CabecalhoDeSegredo.NomeDoSegredo, Segredo);
        c.Painel.Pedidos.Clear();
        await c.Sincronizacao.UmaRodadaAsync(CancellationToken.None);

        Assert.NotEmpty(c.Painel.Pedidos);
        Assert.All(c.Painel.Pedidos, p => Assert.Equal($"Bearer {Segredo}", p.Autorizacao));
        Assert.DoesNotContain(c.Registro, l => l.Contains(Segredo, StringComparison.Ordinal));
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
