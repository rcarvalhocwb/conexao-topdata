using System.Security.Cryptography;
using Access.Infrastructure.SQLite;
using Rayzer.Suporte;

namespace Unit.Tests.Licenca;

/// <summary>
/// Licença do XAcess (docs/60 §1): assinada pela Rayzer, conferida sem internet, com 30 dias de aviso e 15 de
/// tolerância. Nunca para catraca; só decide o administrativo. Chaves geradas no teste; cliente fictício.
/// </summary>
public sealed class LicencasTests : IDisposable
{
    private const string Instalacao = "0123456789abcdef0123456789abcdef";

    // 15/10/2026, 12:00 em Brasília.
    private static readonly DateTimeOffset Hoje = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    private readonly ECDsa _chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string[] _publicas;

    public LicencasTests() => _publicas = [Convert.ToBase64String(_chave.ExportSubjectPublicKeyInfo())];

    public void Dispose() => _chave.Dispose();

    private string Emitir(DateOnly vence, string instalacao = Instalacao) =>
        Licencas.Emitir(new DadosDaLicenca("L-1", "Evento de Teste", instalacao, vence, Hoje), _chave);

    private EstadoDaLicenca Avaliar(string? texto, DateTimeOffset? agora = null, DateTimeOffset? primeira = null) =>
        Licencas.Avaliar(texto, Instalacao, agora ?? Hoje, primeira ?? Hoje, _publicas);

    [Fact]
    public void Valida_com_mais_de_30_dias_e_avisa_dentro_dos_30()
    {
        var longe = Avaliar(Emitir(new DateOnly(2027, 1, 31)));
        Assert.Equal(SituacaoDaLicencaLocal.Valida, longe.Situacao);
        Assert.Equal("Evento de Teste", longe.Dados?.Cliente);
        Assert.Contains("31/01/2027", longe.Mensagem, StringComparison.Ordinal);

        var perto = Avaliar(Emitir(new DateOnly(2026, 10, 25)));
        Assert.Equal((SituacaoDaLicencaLocal.VenceEmBreve, 10), (perto.Situacao, perto.Dias));

        var hoje = Avaliar(Emitir(new DateOnly(2026, 10, 15)));
        Assert.Equal((SituacaoDaLicencaLocal.VenceEmBreve, 0), (hoje.Situacao, hoje.Dias));
        Assert.Contains("vence hoje", hoje.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Vale_ate_o_fim_do_dia_de_Brasilia()
    {
        var texto = Emitir(new DateOnly(2026, 10, 15));

        // 23:59 de Brasília do dia 15 = 02:59Z do dia 16: ainda vale.
        Assert.Equal(SituacaoDaLicencaLocal.VenceEmBreve, Avaliar(texto, new DateTimeOffset(2026, 10, 16, 2, 59, 0, TimeSpan.Zero)).Situacao);
        Assert.Equal(SituacaoDaLicencaLocal.EmTolerancia, Avaliar(texto, new DateTimeOffset(2026, 10, 16, 3, 1, 0, TimeSpan.Zero)).Situacao);
    }

    [Fact]
    public void Vencida_tem_15_dias_de_tolerancia_e_depois_bloqueia_o_administrativo()
    {
        var texto = Emitir(new DateOnly(2026, 10, 10));

        var tolerancia = Avaliar(texto);
        Assert.Equal((SituacaoDaLicencaLocal.EmTolerancia, 10), (tolerancia.Situacao, tolerancia.Dias));
        Assert.False(tolerancia.Bloqueada);
        Assert.Contains("As catracas não param", tolerancia.Mensagem, StringComparison.Ordinal);

        var ultimoDia = Avaliar(texto, Hoje.AddDays(10));
        Assert.Equal((SituacaoDaLicencaLocal.EmTolerancia, 0), (ultimoDia.Situacao, ultimoDia.Dias));

        var bloqueada = Avaliar(texto, Hoje.AddDays(11));
        Assert.True(bloqueada.Bloqueada);
        Assert.Contains("As catracas seguem funcionando", bloqueada.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_licenca_conta_15_dias_da_primeira_partida()
    {
        var inicio = Avaliar(null);
        Assert.Equal((SituacaoDaLicencaLocal.EmTolerancia, 15), (inicio.Situacao, inicio.Dias));
        Assert.True(inicio.SemLicenca);

        Assert.Equal(SituacaoDaLicencaLocal.EmTolerancia, Avaliar(null, Hoje.AddDays(15)).Situacao);
        Assert.Equal(SituacaoDaLicencaLocal.Bloqueada, Avaliar(null, Hoje.AddDays(16)).Situacao);
    }

    [Fact]
    public void Licenca_de_outro_computador_ou_alterada_nao_vale()
    {
        var outra = Avaliar(Emitir(new DateOnly(2027, 1, 31), instalacao: "ffffffffffffffffffffffffffffffff"));
        Assert.True(outra.SemLicenca);
        Assert.Contains("outro computador", outra.Problema, StringComparison.Ordinal);

        var qualquer = Avaliar(Emitir(new DateOnly(2027, 1, 31), instalacao: Licencas.QualquerInstalacao));
        Assert.Equal(SituacaoDaLicencaLocal.Valida, qualquer.Situacao);

        var texto = Emitir(new DateOnly(2027, 1, 31));
        var partes = texto.Split('.');
        var dados = partes[1].ToCharArray();
        dados[5] = dados[5] == 'A' ? 'B' : 'A';
        var alterada = Avaliar($"{partes[0]}.{new string(dados)}.{partes[2]}");
        Assert.Contains("assinatura", alterada.Problema, StringComparison.Ordinal);

        Assert.Contains("não é uma licença", Avaliar("qualquer coisa").Problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Licenca_de_outra_chave_nao_vale()
    {
        using var outraChave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var texto = Licencas.Emitir(new DadosDaLicenca("L-2", "Outro", Instalacao, new DateOnly(2027, 1, 31), Hoje), outraChave);

        Assert.Contains("assinatura", Avaliar(texto).Problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_chave_publica_a_licenca_nao_e_exigida()
    {
        var estado = Licencas.Avaliar(null, Instalacao, Hoje, Hoje, []);

        Assert.Equal(SituacaoDaLicencaLocal.NaoExigida, estado.Situacao);
        Assert.False(estado.Bloqueada);
        Assert.False(estado.SemLicenca);
    }

    [Fact]
    public void A_ferramenta_do_suporte_emite_uma_licenca_que_o_sistema_aceita()
    {
        var (privada, publica) = FerramentaDoSuporte.GerarChaves("senha-de-teste-forte");

        var texto = FerramentaDoSuporte.EmitirLicenca(privada, "senha-de-teste-forte", "Evento de Teste", Instalacao, new DateOnly(2027, 3, 1), Hoje);
        var estado = Licencas.Avaliar(texto, Instalacao, Hoje, Hoje, [publica]);

        Assert.Equal(SituacaoDaLicencaLocal.Valida, estado.Situacao);
        Assert.StartsWith("L-20261015-", estado.Dados?.Numero, StringComparison.Ordinal);
        Assert.StartsWith("XAL1.", texto, StringComparison.Ordinal);
        Assert.Equal(publica, FerramentaDoSuporte.PublicaDe(privada, "senha-de-teste-forte"));
    }
}
