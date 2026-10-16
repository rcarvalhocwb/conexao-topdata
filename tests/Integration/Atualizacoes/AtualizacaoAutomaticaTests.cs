using System.Net;
using System.Security.Cryptography;
using System.Text;
using Access.Infrastructure.SQLite;
using Edge.Supervisor;
using Edge.Supervisor.Atualizacoes;

namespace Integration.Tests.Atualizacoes;

/// <summary>
/// Atualização automática (docs/60 §2), com um GitHub falso: ficha assinada, MSI conferido, e instalação só
/// ao finalizar o painel ou de madrugada, com as catracas paradas. O msiexec não roda: um dublê anota.
/// </summary>
public sealed class AtualizacaoAutomaticaTests : IDisposable
{
    private const string Repositorio = "dono/repo";
    private const string Msi = "RayzerXAcess-0.1.300.msi";

    // 15/10/2026 12:00 de Brasília.
    private static readonly DateTimeOffset Meiodia = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "xacess-atualizacao-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _bytesDoMsi = RandomNumberGenerator.GetBytes(4096);
    private readonly GitHubFalso _github = new();
    private readonly InstaladorFalso _instalador = new();
    private readonly RelogioManual _relogio = new(Meiodia);
    private readonly List<string> _registro = [];
    private DateTimeOffset? _ultimaLeitura = Meiodia.AddMinutes(-1);
    private int _antesDeInstalar;

    public AtualizacaoAutomaticaTests()
    {
        _banco.Migrar();
        Publicar(new FichaDaVersao(FichaDaVersao.NomeDoProduto, "producao", new Version(0, 1, 300), Msi,
            Convert.ToHexStringLower(SHA256.HashData(_bytesDoMsi)), _bytesDoMsi.Length));
    }

    public void Dispose()
    {
        _chave.Dispose();
        _banco.Dispose();
        if (Directory.Exists(_pasta))
        {
            Directory.Delete(_pasta, recursive: true);
        }
    }

    private void Publicar(FichaDaVersao ficha, bool estragarAssinatura = false)
    {
        var json = Encoding.UTF8.GetBytes(ficha.ParaJson());
        var assinatura = _chave.SignData(json, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (estragarAssinatura)
        {
            assinatura[0] ^= 0xFF;
        }

        _github.Arquivos[FichaDaVersao.Arquivo] = json;
        _github.Arquivos[FichaDaVersao.ArquivoDaAssinatura] = Encoding.ASCII.GetBytes(Convert.ToBase64String(assinatura));
        _github.Arquivos[Msi] = _bytesDoMsi;
    }

    private AtualizacaoAutomatica Criar(string canal = "producao", ControleDeLicenca? licenca = null) => new(
        new OpcoesDaAtualizacao(
            Repositorio, canal, new Version(0, 1, 210), _pasta,
            [Convert.ToBase64String(_chave.ExportSubjectPublicKeyInfo())], TimeSpan.Zero, TimeSpan.FromHours(6)),
        new ConfiguracoesDaBorda(_banco.Fabrica),
        licenca,
        () => _ultimaLeitura,
        _registro.Add,
        _instalador,
        () => _antesDeInstalar++,
        _github,
        _relogio);

    [Fact]
    public async Task Baixa_confere_e_so_instala_de_madrugada_com_as_catracas_paradas_e_aviso_ao_painel()
    {
        using var atualizacao = Criar();

        await atualizacao.VerificarAsync(CancellationToken.None);

        var situacao = atualizacao.Situacao();
        Assert.Equal("0.1.300", situacao.VersaoDisponivel);
        Assert.Contains("baixada", situacao.Situacao, StringComparison.Ordinal);
        Assert.Equal(_bytesDoMsi, await File.ReadAllBytesAsync(Path.Combine(_pasta, "0.1.300", Msi)));

        // Meio-dia, catraca lendo: não instala.
        Assert.False(atualizacao.TentarInstalar());

        // 03:10 de Brasília, sem leitura há uma hora: primeiro avisa o painel, dois minutos depois instala.
        _relogio.Agora = new DateTimeOffset(2026, 10, 16, 6, 10, 0, TimeSpan.Zero);
        _ultimaLeitura = _relogio.Agora.AddHours(-1);
        Assert.False(atualizacao.TentarInstalar());
        Assert.True(atualizacao.Iminente(_relogio.Agora));
        Assert.Empty(_instalador.Chamadas);

        _relogio.Agora = _relogio.Agora.AddMinutes(2);
        Assert.True(atualizacao.TentarInstalar());

        var (msi, versao) = Assert.Single(_instalador.Chamadas);
        Assert.Equal(Path.Combine(_pasta, "0.1.300", Msi), msi);
        Assert.Equal(new Version(0, 1, 300), versao);
        Assert.Equal(1, _antesDeInstalar);

        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        Assert.StartsWith("0.1.300|", configuracoes.LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso), StringComparison.Ordinal);
        Assert.Equal("1", configuracoes.LerValor(ConfiguracoesDaBorda.PrefixoTentativasDeAtualizacao + "0.1.300"));
    }

    [Fact]
    public async Task Finalizar_o_painel_com_as_catracas_paradas_instala_na_hora()
    {
        using var atualizacao = Criar();
        await atualizacao.VerificarAsync(CancellationToken.None);

        // Leitura há 2 minutos: finalizar não basta.
        _ultimaLeitura = Meiodia.AddMinutes(-2);
        Assert.False(atualizacao.PainelFinalizado(Meiodia));
        Assert.False(atualizacao.TentarInstalar());

        _ultimaLeitura = Meiodia.AddMinutes(-10);
        Assert.True(atualizacao.PainelFinalizado(Meiodia));
        Assert.True(atualizacao.TentarInstalar());
        Assert.Single(_instalador.Chamadas);
    }

    [Fact]
    public async Task Instalacao_que_nao_para_o_servico_em_30_minutos_e_dada_como_falha_e_o_aviso_ao_painel_acaba()
    {
        using var atualizacao = Criar();
        await atualizacao.VerificarAsync(CancellationToken.None);
        _ultimaLeitura = Meiodia.AddMinutes(-10);
        atualizacao.PainelFinalizado(Meiodia);
        Assert.True(atualizacao.TentarInstalar());
        Assert.True(atualizacao.Iminente(Meiodia));

        // O msiexec falhou e o serviço seguiu rodando: o painel não pode ficar fechando para sempre.
        Assert.False(atualizacao.Iminente(Meiodia.AddMinutes(11)));

        _relogio.Agora = Meiodia.AddMinutes(31);
        Assert.False(atualizacao.TentarInstalar());
        Assert.Contains("não terminou", new ConfiguracoesDaBorda(_banco.Fabrica).LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoResultado), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ficha_com_assinatura_errada_e_recusada_e_nada_e_baixado()
    {
        Publicar(new FichaDaVersao(FichaDaVersao.NomeDoProduto, "producao", new Version(0, 1, 300), Msi,
            Convert.ToHexStringLower(SHA256.HashData(_bytesDoMsi)), _bytesDoMsi.Length), estragarAssinatura: true);
        using var atualizacao = Criar();

        await atualizacao.VerificarAsync(CancellationToken.None);

        Assert.Contains("recusada", atualizacao.Situacao().Situacao, StringComparison.Ordinal);
        Assert.DoesNotContain(_github.Pedidos, p => p.EndsWith(Msi, StringComparison.Ordinal));
        Assert.Null(atualizacao.Situacao().VersaoDisponivel);
    }

    [Fact]
    public async Task Msi_que_nao_bate_com_a_ficha_nao_fica_pronto()
    {
        _github.Arquivos[Msi] = RandomNumberGenerator.GetBytes(4096);
        using var atualizacao = Criar();

        await atualizacao.VerificarAsync(CancellationToken.None);

        Assert.Contains("não confere", atualizacao.Situacao().Situacao, StringComparison.Ordinal);
        _relogio.Agora = new DateTimeOffset(2026, 10, 16, 6, 10, 0, TimeSpan.Zero);
        _ultimaLeitura = null;
        Assert.False(atualizacao.TentarInstalar());
        Assert.False(File.Exists(Path.Combine(_pasta, "0.1.300", Msi)));
    }

    [Fact]
    public async Task Canal_de_teste_nao_se_atualiza_e_nem_pergunta_ao_GitHub()
    {
        using var atualizacao = Criar(canal: "teste");

        await atualizacao.VerificarAsync(CancellationToken.None);

        Assert.False(atualizacao.Situacao().Ligada);
        Assert.Contains("canal de teste", atualizacao.Situacao().Situacao, StringComparison.Ordinal);
        Assert.Empty(_github.Pedidos);
    }

    [Fact]
    public async Task Licenca_bloqueada_nao_instala()
    {
        using var chaveDoSuporte = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        var licenca = new ControleDeLicenca(configuracoes, () => _relogio.GetUtcNow(), [Convert.ToBase64String(chaveDoSuporte.ExportSubjectPublicKeyInfo())]);
        configuracoes.PrimeiraPartidaDaLicenca(Meiodia.AddDays(-40));
        using var atualizacao = Criar(licenca: licenca);
        await atualizacao.VerificarAsync(CancellationToken.None);

        _ultimaLeitura = null;
        Assert.False(atualizacao.PainelFinalizado(Meiodia));
        Assert.False(atualizacao.TentarInstalar());
        Assert.Empty(_instalador.Chamadas);
    }

    [Fact]
    public void Depois_de_subir_registra_a_atualizacao_que_deu_certo_ou_a_que_nao_terminou()
    {
        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso, $"0.1.210|{Meiodia.AddMinutes(-3):O}", Meiodia);
        using (var atualizacao = Criar())
        {
            atualizacao.ConferirAtualizacaoAnterior();
        }

        Assert.StartsWith("Atualizado para a versão 0.1.210", configuracoes.LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoResultado), StringComparison.Ordinal);
        Assert.Equal(string.Empty, configuracoes.LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso));

        configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso, $"0.1.300|{Meiodia.AddHours(-1):O}", Meiodia);
        using (var atualizacao = Criar())
        {
            atualizacao.ConferirAtualizacaoAnterior();
        }

        Assert.Contains("não terminou", configuracoes.LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoResultado), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versao_igual_ou_mais_velha_fica_em_dia()
    {
        Publicar(new FichaDaVersao(FichaDaVersao.NomeDoProduto, "producao", new Version(0, 1, 210), Msi,
            Convert.ToHexStringLower(SHA256.HashData(_bytesDoMsi)), _bytesDoMsi.Length));
        using var atualizacao = Criar();

        await atualizacao.VerificarAsync(CancellationToken.None);

        Assert.StartsWith("Em dia", atualizacao.Situacao().Situacao, StringComparison.Ordinal);
        Assert.DoesNotContain(_github.Pedidos, p => p.EndsWith(Msi, StringComparison.Ordinal));
    }

    private sealed class GitHubFalso : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Arquivos { get; } = new(StringComparer.Ordinal);

        public List<string> Pedidos { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Pedidos.Add(url);

            if (url == $"https://api.github.com/repos/{Repositorio}/releases/latest")
            {
                var ativos = string.Join(",", Arquivos.Keys.Select(n => $$"""{"name":"{{n}}","browser_download_url":"https://downloads.invalid/{{n}}"}"""));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"tag_name":"v0.1.300","assets":[{{ativos}}]}""") });
            }

            var nome = url["https://downloads.invalid/".Length..];
            return Task.FromResult(Arquivos.TryGetValue(nome, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class InstaladorFalso : IInstaladorDoWindows
    {
        public List<(string Msi, Version Versao)> Chamadas { get; } = [];

        public void Instalar(string msi, string pastaDeTrabalho, Version versao) => Chamadas.Add((msi, versao));
    }

    private sealed class RelogioManual(DateTimeOffset agora) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = agora;

        public override DateTimeOffset GetUtcNow() => Agora;
    }
}
