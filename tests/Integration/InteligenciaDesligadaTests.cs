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
}
