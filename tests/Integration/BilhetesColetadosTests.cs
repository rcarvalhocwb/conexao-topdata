using Access.Application.Devices;
using Access.Domain.Credentials;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// Etapa A.9 do docs/35: a tabela dos bilhetes coletados (<c>collected_ticket</c>, migração
/// 015) e quem grava nela. Só-INSERT, sem código em claro, deduplicação pelo conteúdo e pelo
/// tipo 128. Códigos sintéticos.
/// </summary>
public sealed class BilhetesColetadosTests : IDisposable
{
    private static readonly DateTimeOffset Marcado = new(2026, 12, 6, 18, 30, 0, TimeSpan.FromHours(-3));
    private static readonly DateTimeOffset Coletado = new(2026, 12, 6, 22, 0, 0, TimeSpan.Zero);
    private static readonly ImpressaoDeCodigo Impressao = new(Enumerable.Repeat((byte)7, 32).ToArray());

    private readonly BancoTemporario _banco = new();
    private readonly BilhetesColetados _bilhetes;
    private readonly Guid _coleta = Guid.CreateVersion7(Coletado);
    private int _ordem;

    public BilhetesColetadosTests()
    {
        _banco.Migrar();
        _bilhetes = new BilhetesColetados(_banco.Fabrica, Impressao);
    }

    public void Dispose() => _banco.Dispose();

    private DesfechoDaGravacaoDoBilhete Gravar(byte tipo, string codigo, DateTimeOffset? quando = null, int inner = 1) =>
        _bilhetes.Gravar(new BilheteColetado(inner, new Bilhete(tipo, quando ?? Marcado, codigo), _coleta, ++_ordem, Coletado));

    [Fact]
    public void Grava_mascara_e_impressao_e_nunca_o_codigo()
    {
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000101"));

        var registro = Assert.Single(_bilhetes.Listar(1));
        Assert.Equal(10, registro.Tipo);
        Assert.False(registro.Repetido);
        Assert.Equal("cred:****01(14)", registro.CodigoMascarado);
        Assert.Equal(Impressao.De("99990000000101"), registro.Impressao);
        Assert.Equal(Marcado, registro.MarcadoEm);
        Assert.Equal(_coleta, registro.ColetaId);
        Assert.Equal(1, registro.Ordem);

        // Nenhuma coluna guarda o código, nem o arquivo da base.
        using var conexao = _banco.Fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText = "SELECT * FROM collected_ticket;";
        using var leitor = sql.ExecuteReader();
        Assert.True(leitor.Read());
        for (var i = 0; i < leitor.FieldCount; i++)
        {
            Assert.DoesNotContain("9999000000", Convert.ToString(leitor.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Bilhete_nao_mostra_o_codigo_inteiro_no_texto()
    {
        var bilhete = new Bilhete(10, Marcado, "99990000000101");
        Assert.DoesNotContain("99990000000101", bilhete.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("99990000000101", new BilheteColetado(1, bilhete, _coleta, 1, Coletado).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void O_mesmo_bilhete_duas_vezes_grava_uma()
    {
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000101"));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Repetido, Gravar(10, "99990000000101"));

        Assert.Single(_bilhetes.Listar(1));
    }

    [Fact]
    public void Tipo_128_com_o_original_na_base_e_repetido()
    {
        Gravar(10, "99990000000101");

        Assert.Equal(DesfechoDaGravacaoDoBilhete.Repetido, Gravar(Bilhete.TipoRepetido, "99990000000101"));

        var registro = Assert.Single(_bilhetes.Listar(1));
        Assert.Equal(10, registro.Tipo);
    }

    /// <summary>O worker caiu antes de gravar o original: o 128 é a única cópia e fica.</summary>
    [Fact]
    public void Tipo_128_sem_o_original_e_gravado_como_veio()
    {
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(Bilhete.TipoRepetido, "99990000000101"));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Repetido, Gravar(Bilhete.TipoRepetido, "99990000000101"));

        var registro = Assert.Single(_bilhetes.Listar(1));
        Assert.Equal(Bilhete.TipoRepetido, registro.Tipo);
        Assert.True(registro.Repetido);
    }

    /// <summary>
    /// Por que a impressão, e não só a máscara: dois cartões diferentes com os mesmos dois
    /// últimos dígitos, no mesmo minuto, são duas marcações.
    /// </summary>
    [Fact]
    public void Codigos_diferentes_com_a_mesma_mascara_no_mesmo_minuto_sao_duas_marcacoes()
    {
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000101"));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000201"));

        var registros = _bilhetes.Listar(1);
        Assert.Equal(2, registros.Count);
        Assert.All(registros, r => Assert.Equal("cred:****01(14)", r.CodigoMascarado));
    }

    [Fact]
    public void Tipo_minuto_e_catraca_diferentes_sao_marcacoes_diferentes()
    {
        Gravar(10, "99990000000101");
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(12, "99990000000101"));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000101", Marcado.AddMinutes(1)));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000101", inner: 2));

        Assert.Equal(4, _bilhetes.Listar().Count);
    }

    /// <summary>Relógio da catraca zerado: o adapter devolve MinValue; grava sem data, e deduplica.</summary>
    [Fact]
    public void Data_invalida_da_catraca_grava_sem_data_e_ainda_deduplica()
    {
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Gravado, Gravar(10, "99990000000101", DateTimeOffset.MinValue));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Repetido, Gravar(10, "99990000000101", DateTimeOffset.MinValue));
        Assert.Equal(DesfechoDaGravacaoDoBilhete.Repetido, Gravar(Bilhete.TipoRepetido, "99990000000101", DateTimeOffset.MinValue));

        Assert.Null(Assert.Single(_bilhetes.Listar(1)).MarcadoEm);
    }

    [Fact]
    public void A_tabela_e_so_insert()
    {
        Gravar(10, "99990000000101");

        using var conexao = _banco.Fabrica.Abrir();
        var mudar = Assert.Throws<SqliteException>(() => Executar(conexao, "UPDATE collected_ticket SET raw_type = 11;"));
        Assert.Contains("só-INSERT", mudar.Message, StringComparison.Ordinal);

        var apagar = Assert.Throws<SqliteException>(() => Executar(conexao, "DELETE FROM collected_ticket;"));
        Assert.Contains("só-INSERT", apagar.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("code_mask", "'99990000000101'")]
    [InlineData("code_hmac", "'99990000000101'")]
    [InlineData("marked_at", "'2026-12-06T21:30:59'")]
    [InlineData("raw_type", "256")]
    [InlineData("inner_number", "0")]
    [InlineData("repeated", "1")]
    public void A_base_recusa_o_que_esta_fora_do_formato(string coluna, string valor)
    {
        var valores = new Dictionary<string, string>
        {
            ["id"] = "'0190f000-0000-7000-8000-000000000001'",
            ["inner_number"] = "1",
            ["raw_type"] = "10",
            ["repeated"] = "0",
            ["marked_at"] = "'2026-12-06T21:30'",
            ["code_mask"] = "'cred:****01(14)'",
            ["code_hmac"] = $"'{Impressao.De("99990000000101")}'",
            ["code_key_id"] = $"'{Impressao.IdDaChave}'",
            ["collection_id"] = $"'{_coleta}'",
            ["collection_seq"] = "1",
            ["collected_at"] = "'2026-12-06T22:00:00.0000000+00:00'",
        };

        using var conexao = _banco.Fabrica.Abrir();

        // A linha de referência passa: o que reprova abaixo é só a coluna trocada.
        Executar(conexao, Insercao(valores));
        Executar(conexao, "SELECT 1;");

        valores["id"] = "'0190f000-0000-7000-8000-000000000002'";
        valores["marked_at"] = "'2026-12-06T21:31'";
        valores[coluna] = valor;
        Assert.Throws<SqliteException>(() => Executar(conexao, Insercao(valores)));
    }

    [Fact]
    public void A_tabela_e_strict()
    {
        using var conexao = _banco.Fabrica.Abrir();
        var strict = SqliteConnectionFactory.Escalar<long>(conexao, "SELECT strict FROM pragma_table_list WHERE name = 'collected_ticket';");
        Assert.Equal(1, strict);
    }

    private static string Insercao(Dictionary<string, string> valores) =>
        $"INSERT INTO collected_ticket ({string.Join(", ", valores.Keys)}) VALUES ({string.Join(", ", valores.Values)});";

    private static void Executar(SqliteConnection conexao, string texto)
    {
        using var sql = conexao.CreateCommand();
#pragma warning disable CA2100 // Texto montado no próprio teste, com valores fixos.
        sql.CommandText = texto;
#pragma warning restore CA2100
        sql.ExecuteNonQuery();
    }
}
