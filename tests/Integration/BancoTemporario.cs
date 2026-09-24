using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>Banco SQLite descartável, num diretório próprio.</summary>
internal sealed class BancoTemporario : IDisposable
{
    private readonly string _diretorio;

    public BancoTemporario()
    {
        _diretorio = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_diretorio);
        Caminho = Path.Combine(_diretorio, "acesso.db");
        Fabrica = new SqliteConnectionFactory(Caminho);
    }

    public string Caminho { get; }

    public SqliteConnectionFactory Fabrica { get; }

    public AccessJournal Migrar()
    {
        new Migrator(Fabrica).Aplicar();
        return new AccessJournal(Fabrica);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_diretorio, recursive: true);
        }
        catch (IOException)
        {
            // Sobra de arquivo temporário não é motivo para reprovar um teste.
        }
    }
}
