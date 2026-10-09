using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Achado E4-2 do docs/41: o provedor com perfil <c>mifare-catraca4</c> cadastra o cartão completado com
/// zeros até 10 dígitos, e a leitura da catraca usa o perfil <c>raw</c>. Um cartão lido com menos dígitos
/// era negado como desconhecido, sem pista para o operador.
/// </summary>
public sealed class LeituraComZerosDoCadastroTests : IDisposable
{
    private static readonly DateTimeOffset Abertura = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repositorio;

    public LeituraComZerosDoCadastroTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("cartoes", "Cartões", PerfisDeLeitura.MifareCatraca4.Name, "local"), Abertura);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("site", "Site", CredentialNormalization.Raw.Name, "rest-site"), Abertura);
    }

    public void Dispose() => _banco.Dispose();

    private void Cadastrar(string provedor, string codigo, CredentialNormalization perfil)
    {
        var normalizado = PerfisDeLeitura.Normalizar(codigo, perfil)!;
        var resultado = _repositorio.Ingerir([new IngressoRecebido(provedor, "ref-" + codigo, codigo, normalizado, UsosMaximos: 1)], Abertura);
        Assert.Equal(1, resultado.Inseridos);
    }

    private ResultadoDoUso Ler(string lido) =>
        _repositorio.TentarUsar(PerfisDeLeitura.Normalizar(lido, PerfisDeLeitura.DaLeitura)!, "portao-1", "catraca-01", Abertura.AddHours(1)).Resultado;

    [Fact]
    public void Cartao_cadastrado_com_zeros_e_lido_sem_eles_entra_e_conta_uma_vez()
    {
        Cadastrar("cartoes", "78234567", PerfisDeLeitura.MifareCatraca4);

        var primeira = Ler("78234567");
        Assert.True(primeira.Liberou, primeira.Motivo.ToString());
        Assert.Equal("cartoes", primeira.ProvedorId);

        // O uso foi do ingresso cadastrado (0078234567): lido de novo, com ou sem zeros, já foi usado.
        Assert.Equal(MotivoDoUso.UsosEsgotados, Ler("78234567").Motivo);
        Assert.Equal(MotivoDoUso.UsosEsgotados, Ler("0078234567").Motivo);
    }

    [Fact]
    public void Codigo_de_provedor_raw_nunca_casa_por_preenchimento()
    {
        // QR do site que por acaso tem 10 dígitos com zeros na frente: só a leitura exata entra.
        Cadastrar("site", "0012345678", CredentialNormalization.Raw);

        Assert.Equal(MotivoDoUso.Desconhecido, Ler("12345678").Motivo);
        Assert.True(Ler("0012345678").Liberou);
    }

    [Fact]
    public void Leitura_exata_vem_antes_do_preenchimento()
    {
        Cadastrar("site", "12345678", CredentialNormalization.Raw);
        Cadastrar("cartoes", "12345678", PerfisDeLeitura.MifareCatraca4);

        var lido = Ler("12345678");

        Assert.True(lido.Liberou);
        Assert.Equal("site", lido.ProvedorId);
    }

    [Fact]
    public void Leitura_com_letras_ou_ja_com_dez_digitos_nao_e_preenchida()
    {
        Cadastrar("cartoes", "0000000012", PerfisDeLeitura.MifareCatraca4);

        Assert.Equal(MotivoDoUso.Desconhecido, Ler("A12").Motivo);
        Assert.True(Ler("12").Liberou);
    }
}
