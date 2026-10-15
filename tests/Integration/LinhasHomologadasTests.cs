using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// A chave técnica das linhas de firmware aceitas (achado E1-06 do docs/41): vazia vale o padrão da versão;
/// lista legível manda; ilegível volta ao padrão com o problema para o registro.
/// </summary>
public sealed class LinhasHomologadasTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 15, 23, 0, 0, TimeSpan.Zero);
    private readonly BancoTemporario _banco = new();

    public LinhasHomologadasTests() => _banco.Migrar();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public void Sem_chave_vale_o_padrao_e_lista_legivel_manda()
    {
        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        Assert.Equal((null, null), configuracoes.LinhasHomologadas());

        configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveLinhasHomologadas, "14, 16;19 21", Agora);
        var (linhas, problema) = configuracoes.LinhasHomologadas();

        Assert.Null(problema);
        Assert.Equal(new byte[] { 14, 16, 19, 21 }, linhas!.Order());
        Assert.Contains((byte)19, Edge.Worker.DevicePump.LinhasHomologadasPadrao);
    }

    [Theory]
    [InlineData("catraca fit")]
    [InlineData("0")]
    [InlineData("300")]
    public void Lista_ilegivel_volta_ao_padrao_com_o_problema(string valor)
    {
        var configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveLinhasHomologadas, valor, Agora);

        var (linhas, problema) = configuracoes.LinhasHomologadas();

        Assert.Null(linhas);
        Assert.Contains("vale o padrão da versão", problema, StringComparison.Ordinal);
    }
}
