using Contracts;

namespace Integration.Tests;

/// <summary>
/// A detecção de token ilegível (base da mensagem "falta o grupo dos operadores"). O caso de
/// permissão negada de verdade só se reproduz numa conta comum no Windows; aqui, sob root, a
/// leitura nunca é negada, então só se afirma o que o teste consegue provar.
/// </summary>
public sealed class TokenIlegivelTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    public TokenIlegivelTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Arquivo_ausente_nao_e_token_ilegivel()
    {
        Assert.False(InstalacaoLocal.TokenIlegivel(Path.Combine(_pasta, "token-que-nao-existe")));
    }

    [Fact]
    public void Arquivo_legivel_nao_e_token_ilegivel()
    {
        var arquivo = Path.Combine(_pasta, "token");
        File.WriteAllText(arquivo, "segredo-de-teste");

        Assert.False(InstalacaoLocal.TokenIlegivel(arquivo));
    }
}
