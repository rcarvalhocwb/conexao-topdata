using System.Diagnostics;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// Achado E3-04 do docs/41: com outro escritor segurando a base, a decisão da catraca esperava até o
/// <c>DefaultTimeout</c> do Microsoft.Data.Sqlite (30 s quando não definido), e não o <c>busy_timeout</c>
/// documentado. Catraca parada meio minuto com fila na frente.
/// </summary>
public sealed class EsperaPorTravaTests : IDisposable
{
    private readonly BancoTemporario _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public void Com_a_base_travada_por_outro_escritor_o_uso_desiste_na_espera_configurada_e_nao_em_30_s()
    {
        _banco.Migrar();
        var fabrica = new SqliteConnectionFactory(_banco.Caminho, TimeSpan.FromSeconds(1));
        var repositorio = new RepositorioDeIngressos(fabrica);

        using var outroEscritor = fabrica.Abrir();
        using var trava = outroEscritor.BeginTransaction();
        SqliteConnectionFactory.Executar(outroEscritor, "CREATE TABLE IF NOT EXISTS segura_a_trava (x INTEGER);");

        var relogio = Stopwatch.StartNew();
        var erro = Assert.Throws<SqliteException>(() =>
            repositorio.TentarUsar("ZET-1", "portao-1", "catraca-01", DateTimeOffset.UtcNow));
        relogio.Stop();

        Assert.Equal(5, erro.SqliteErrorCode); // SQLITE_BUSY
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), $"esperou {relogio.Elapsed.TotalSeconds:0.0} s");
        Assert.True(relogio.Elapsed >= TimeSpan.FromSeconds(0.9), $"desistiu cedo demais: {relogio.Elapsed.TotalSeconds:0.0} s");
    }

    [Fact]
    public void A_espera_padrao_e_a_documentada()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), SqliteConnectionFactory.EsperaPadraoPorTrava);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteConnectionFactory(_banco.Caminho, TimeSpan.FromMilliseconds(100)));
    }
}
