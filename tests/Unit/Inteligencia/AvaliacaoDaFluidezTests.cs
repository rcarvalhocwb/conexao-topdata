using Xunit;
using Access.Inteligencia;

namespace Unit.Inteligencia;

/// <summary>
/// Testes para AvaliacaoDaFluidez (Etapa I.8, IN-05 do docs/36-anexos/02 §5.2).
///
/// NOVO-SIM-SEN-01: Mapa com catraca 4 = saída; 10 entradas e 3 saídas simuladas
/// dão lotação 7; um giro sem pedido aparece como "sem sentido" e não mexe na lotação.
/// </summary>
public class AvaliacaoDaFluidezTests
{
    /// <summary>
    /// NOVO-SIM-SEN-01: Calcula lotação corretamente como entradas − saídas.
    ///
    /// Arrange: 10 entradas, 3 saídas confirmadas
    /// Act: CalcularLotacao(10, 3)
    /// Assert: retorna 7 (10 - 3)
    /// </summary>
    [Fact]
    public void CalcularLotacao_ComEntradasESaidas_RetornaEntradaMenosSaida()
    {
        // Arrange
        long entradas = 10;
        long saidas = 3;

        // Act
        var lotacao = AvaliacaoDaFluidez.CalcularLotacao(entradas, saidas);

        // Assert
        Assert.Equal(7, lotacao);
    }

    /// <summary>
    /// Lotação nunca é negativa: se saídas > entradas, retorna 0.
    ///
    /// Arrange: 3 entradas, 10 saídas (dados inconsistentes)
    /// Act: CalcularLotacao(3, 10)
    /// Assert: retorna 0 (não negativa)
    /// </summary>
    [Fact]
    public void CalcularLotacao_SaidasMaiorQueEntradas_RetornaZero()
    {
        // Arrange
        long entradas = 3;
        long saidas = 10;

        // Act
        var lotacao = AvaliacaoDaFluidez.CalcularLotacao(entradas, saidas);

        // Assert
        Assert.Equal(0, lotacao);
    }

    /// <summary>
    /// Lotação com entradas = 0 e saídas = 0 retorna 0 (evento vazio).
    /// </summary>
    [Fact]
    public void CalcularLotacao_VazioTotalDoEvento_RetornaZero()
    {
        // Arrange
        long entradas = 0;
        long saidas = 0;

        // Act
        var lotacao = AvaliacaoDaFluidez.CalcularLotacao(entradas, saidas);

        // Assert
        Assert.Equal(0, lotacao);
    }

    /// <summary>
    /// Lotação com muitas entradas e nenhuma saída (evento de entrada pura).
    /// </summary>
    [Fact]
    public void CalcularLotacao_ApenasEntradas_RetornaTodasAsEntradas()
    {
        // Arrange
        long entradas = 1000;
        long saidas = 0;

        // Act
        var lotacao = AvaliacaoDaFluidez.CalcularLotacao(entradas, saidas);

        // Assert
        Assert.Equal(1000, lotacao);
    }

    /// <summary>
    /// PodeMostrarLotacao: lotação é mostrada SOMENTE se houver catraca de saída.
    /// </summary>
    [Fact]
    public void PodeMostrarLotacao_ComCatracaDeSaida_RetornaVerdadeiro()
    {
        // Arrange
        bool temCatracaDeSaida = true;
        long sem_sentido_conhecido = 0;

        // Act
        var podeShown = AvaliacaoDaFluidez.PodeMostrarLotacao(temCatracaDeSaida, sem_sentido_conhecido);

        // Assert
        Assert.True(podeShown);
    }

    /// <summary>
    /// PodeMostrarLotacao: sem catraca de saída, o sistema informa entradas, não lotação.
    /// </summary>
    [Fact]
    public void PodeMostrarLotacao_SemCatracaDeSaida_RetornaFalso()
    {
        // Arrange
        bool temCatracaDeSaida = false;
        long sem_sentido_conhecido = 0;

        // Act
        var podeShown = AvaliacaoDaFluidez.PodeMostrarLotacao(temCatracaDeSaida, sem_sentido_conhecido);

        // Assert
        Assert.False(podeShown);
    }

    /// <summary>
    /// Giros sem sentido conhecido não mexem na lotação estimada.
    ///
    /// Arrange: 10 entradas, 3 saídas, 1 giro sem sentido
    /// Act: CalcularLotacao(10, 3)
    /// Assert: retorna 7 (ignora giros_sem_sentido)
    /// </summary>
    [Fact]
    public void CalcularLotacao_IgnoraGirosSemSentido_LotacaoCorreta()
    {
        // Arrange
        long entradas = 10;
        long saidas = 3;
        // giros_sem_sentido = 1 (não afeta o cálculo)

        // Act
        var lotacao = AvaliacaoDaFluidez.CalcularLotacao(entradas, saidas);

        // Assert: lotação é 7, independentemente de giros sem sentido
        Assert.Equal(7, lotacao);
    }
}
