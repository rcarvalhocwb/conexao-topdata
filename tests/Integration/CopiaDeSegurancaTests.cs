using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// A cópia de <c>acesso.db</c> (auditoria A05): consistente, verificada, com retenção, e nunca
/// tomada por backup quando está corrompida.
/// </summary>
public sealed class CopiaDeSegurancaTests : IDisposable
{
    private readonly BancoTemporario _banco = new();
    private readonly string _pastaDeCopias;

    public CopiaDeSegurancaTests()
    {
        _pastaDeCopias = Path.Combine(Path.GetDirectoryName(_banco.Caminho)!, "copias");
        _banco.Migrar();
    }

    public void Dispose()
    {
        _banco.Dispose();
    }

    [Fact]
    public void A_copia_abre_com_integridade_e_preserva_os_dados()
    {
        Executar("INSERT INTO edge_setting (key, value, updated_at, updated_by) VALUES ('teste.copia', 'valor-13', '2026-10-08T00:00:00Z', 'teste');");
        var copia = new CopiaDeSeguranca(_banco.Fabrica, _pastaDeCopias);

        var caminho = copia.Criar(DateTimeOffset.Parse("2026-10-08T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(File.Exists(caminho));
        using var conexao = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = caminho, Mode = SqliteOpenMode.ReadOnly }.ToString());
        conexao.Open();
        Assert.Equal("ok", Escalar(conexao, "PRAGMA integrity_check;"));
        Assert.Equal("valor-13", Escalar(conexao, "SELECT value FROM edge_setting WHERE key = 'teste.copia';"));
    }

    [Fact]
    public void Criar_duas_copias_no_mesmo_instante_nao_se_sobrescrevem()
    {
        var copia = new CopiaDeSeguranca(_banco.Fabrica, _pastaDeCopias);
        var agora = DateTimeOffset.Parse("2026-10-08T12:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);

        copia.Criar(agora);

        Assert.Throws<InvalidOperationException>(() => copia.Criar(agora));
    }

    [Fact]
    public void Limpar_mantem_so_as_copias_mais_recentes()
    {
        var copia = new CopiaDeSeguranca(_banco.Fabrica, _pastaDeCopias);
        var inicio = DateTimeOffset.Parse("2026-10-08T00:00:00+00:00", System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 0; i < 5; i++)
        {
            copia.Criar(inicio.AddHours(i));
        }

        var apagadas = copia.Limpar(manter: 2);

        Assert.Equal(3, apagadas);
        var restantes = copia.Listar();
        Assert.Equal(2, restantes.Count);
        Assert.Contains("20261008-040000", restantes[0], StringComparison.Ordinal);
        Assert.Contains("20261008-030000", restantes[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Limpar_sem_nada_para_apagar_nao_falha()
    {
        var copia = new CopiaDeSeguranca(_banco.Fabrica, _pastaDeCopias);

        Assert.Equal(0, copia.Limpar(manter: 14));
        Assert.Empty(copia.Listar());
    }

    [Fact]
    public void Manter_zero_copias_e_recusado()
    {
        var copia = new CopiaDeSeguranca(_banco.Fabrica, _pastaDeCopias);

        Assert.Throws<ArgumentOutOfRangeException>(() => copia.Limpar(manter: 0));
    }

    private void Executar(string sql)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static object? Escalar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return comando.ExecuteScalar();
    }

    /// <summary>
    /// Achado E10-7 do docs/41: a migração rodava antes de qualquer cópia, e voltar de versão exigia a
    /// cópia de até 6 h antes. O serviço pergunta o que falta numa base já migrada e copia antes.
    /// </summary>
    [Fact]
    public void Pendentes_so_aparecem_numa_base_ja_migrada_que_esta_atras()
    {
        using var nova = new BancoTemporario();
        Assert.Empty(new Migrator(nova.Fabrica).PendentesNumaBaseExistente());

        // A base deste teste já está migrada: nada falta.
        var migrador = new Migrator(_banco.Fabrica);
        Assert.Empty(migrador.PendentesNumaBaseExistente());

        // Uma versão mais nova traz uma migração que esta base não tem.
        var ultima = Migrator.Disponiveis()[^1];
        Executar($"DELETE FROM schema_version WHERE nome = '{ultima}';");

        Assert.Equal([ultima], migrador.PendentesNumaBaseExistente());
    }
}
