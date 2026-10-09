using Access.Inteligencia;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// A camada inteligente nasce desligada numa instalação nova (docs/36: ligá-la por padrão é a I.11,
/// depois dos ensaios de carga, caos e soak).
/// </summary>
/// <remarks>
/// Antes desta correção, o serviço gravava <c>inteligencia.ligada = 1</c> em toda partida, e o
/// Analisador rodava numa instalação que ninguém tinha decidido ligar.
/// </remarks>
public sealed class InteligenciaDesligadaTests
{
    [Fact]
    public void Base_nova_nao_tem_a_chave_da_camada_e_ela_fica_desligada()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        var valor = new LeituraSomenteDaOperacao(banco.Caminho).ValorDaChave(ChavesDaInteligencia.Ligada);

        Assert.Null(valor);
        Assert.False(ChavesDaInteligencia.EstaLigada(valor));
    }

    [Fact]
    public void Base_nova_nao_tem_a_chave_do_coletor_do_worker()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();

        var valor = new LeituraSomenteDaOperacao(banco.Caminho).ValorDaChave(ChavesDaInteligencia.ColetorLigado);

        Assert.Null(valor);
        Assert.False(ChavesDaInteligencia.EstaLigada(valor));
    }

    [Fact]
    public void Base_herdada_de_build_antigo_com_a_camada_ligada_fica_desligada_depois_de_atualizar()
    {
        // Achado E7-2 do docs/41: builds entre d6fd80c e 3a6cc94 gravavam as duas chaves com "1".
        using var banco = new BancoTemporario();
        banco.Migrar();
        using (var conexao = banco.Fabrica.Abrir())
        using (var sql = conexao.CreateCommand())
        {
            sql.CommandText =
                """
                INSERT INTO edge_setting (key, value, updated_at) VALUES
                    ('inteligencia.ligada', '1', '2026-10-01T00:00:00Z'),
                    ('inteligencia.coletor', '1', '2026-10-01T00:00:00Z');
                DELETE FROM schema_version WHERE nome LIKE '018%';
                """;
            sql.ExecuteNonQuery();
        }

        var aplicadas = new Migrator(banco.Fabrica).Aplicar();

        Assert.Contains(aplicadas, n => n.StartsWith("018", StringComparison.Ordinal));
        var leitura = new LeituraSomenteDaOperacao(banco.Caminho);
        Assert.Null(leitura.ValorDaChave(ChavesDaInteligencia.Ligada));
        Assert.Null(leitura.ValorDaChave(ChavesDaInteligencia.ColetorLigado));
    }
}
