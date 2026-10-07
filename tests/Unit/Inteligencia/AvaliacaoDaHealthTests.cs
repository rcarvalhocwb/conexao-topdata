using Access.Inteligencia;

namespace Tests.Unit.Inteligencia;

/// <summary>
/// Testes de I.3 (Saúde v1): avaliação de sinais Giro, Relógio, Configuração, Comunicação.
/// Cada sinal é testado nos cenários relevantes (Normal, Atenção, Ação, SemDados) (NOVO-SIM-SAU-01).
/// </summary>
public sealed class AvaliacaoDaHealthTests
{
    private readonly AvaliacaoDaHealth _avaliador;
    private readonly FakeTimeProvider _relogio;

    public AvaliacaoDaHealthTests()
    {
        _relogio = new FakeTimeProvider();
        _avaliador = new AvaliacaoDaHealth(_relogio);
    }

    #region Sinal Giro (Wilson)

    [Fact]
    public void AvaliarGiro_Normal_TaxaProximaDasVizinhas()
    {
        // 2% de sem-giro, vizinhas em ~3% (dentro da margem de 10%), amostras suficientes.
        var sinal = _avaliador.AvaliarGiro(taxaSemGiro: 0.02, amostras: 150, taxaVizinhas: 0.03);

        Assert.Equal(NivelDeSinal.Normal, sinal.Nivel);
        Assert.Contains("Taxa de sem-giro", sinal.Resumo);
    }

    [Fact]
    public void AvaliarGiro_Atencao_SemVizinhas()
    {
        // Sem taxa das vizinhas para comparar.
        var sinal = _avaliador.AvaliarGiro(taxaSemGiro: 0.133, amostras: 150, taxaVizinhas: null);

        Assert.Equal(NivelDeSinal.Atencao, sinal.Nivel);
        Assert.Contains("sem vizinhas", sinal.Resumo);
    }

    [Fact]
    public void AvaliarGiro_Aprendendo_PoucasAmostras()
    {
        // Menos de 30 giros: ainda coletando dados.
        var sinal = _avaliador.AvaliarGiro(taxaSemGiro: 0.10, amostras: 10, taxaVizinhas: 0.03);

        Assert.Equal(NivelDeSinal.Aprendendo, sinal.Nivel);
    }

    [Fact]
    public void AvaliarGiro_Acao_TaxaMuitoAcimaDasVizinhas()
    {
        // 30% de sem-giro contra vizinhas em ~5%.
        var sinal = _avaliador.AvaliarGiro(taxaSemGiro: 0.30, amostras: 150, taxaVizinhas: 0.05);

        Assert.Equal(NivelDeSinal.Acao, sinal.Nivel);
        Assert.Contains("significativamente", sinal.Resumo);
    }

    #endregion

    #region Sinal Relógio (Divergência)

    [Fact]
    public void AvaliarRelogio_Normal_DentroDoTolerado()
    {
        var sinal = _avaliador.AvaliarRelogio(divergenciaSegundos: 10);

        Assert.Equal(NivelDeSinal.Normal, sinal.Nivel);
        Assert.Contains("dentro do padrão", sinal.Resumo);
    }

    [Fact]
    public void AvaliarRelogio_Acao_DivergenciaExcedida()
    {
        var sinal = _avaliador.AvaliarRelogio(divergenciaSegundos: 45);

        Assert.Equal(NivelDeSinal.Acao, sinal.Nivel);
        Assert.Contains("divergindo", sinal.Resumo);
    }

    [Fact]
    public void AvaliarRelogio_SemDados_DivergenciaNula()
    {
        var sinal = _avaliador.AvaliarRelogio(divergenciaSegundos: null);

        Assert.Equal(NivelDeSinal.SemDados, sinal.Nivel);
    }

    #endregion

    #region Sinal Configuração (Salva ≠ Aplicada)

    [Fact]
    public void AvaliarConfiguracao_Normal_Aplicada()
    {
        var sinal = _avaliador.AvaliarConfiguracao(savedVersion: "v2", appliedVersion: "v2", firmwareMudou: false);

        Assert.Equal(NivelDeSinal.Normal, sinal.Nivel);
        Assert.Contains("em dia", sinal.Resumo);
    }

    [Fact]
    public void AvaliarConfiguracao_Acao_NaoAplicadaPorMaisDe2min()
    {
        _relogio.SetUtcNow(new DateTimeOffset(2026, 10, 02, 14, 30, 0, TimeSpan.Zero));

        var sinal = _avaliador.AvaliarConfiguracao(
            savedVersion: "v3",
            appliedVersion: "v2",
            firmwareMudou: false,
            ultimaMudanca: new DateTimeOffset(2026, 10, 02, 14, 27, 0, TimeSpan.Zero)); // -3 min

        Assert.Equal(NivelDeSinal.Acao, sinal.Nivel);
        Assert.Contains("não está aplicada", sinal.Resumo);
    }

    [Fact]
    public void AvaliarConfiguracao_Atencao_FirmwareMudou()
    {
        var sinal = _avaliador.AvaliarConfiguracao(savedVersion: "v2", appliedVersion: "v2", firmwareMudou: true);

        Assert.Equal(NivelDeSinal.Atencao, sinal.Nivel);
        Assert.Contains("Firmware mudou", sinal.Resumo);
    }

    #endregion

    #region Sinal Comunicação (Vazio em I.3)

    [Fact]
    public void AvaliarComunicacao_SemDados_EmI3()
    {
        var sinal = _avaliador.AvaliarComunicacao();

        Assert.Equal(NivelDeSinal.SemDados, sinal.Nivel);
        Assert.Contains("será avaliada em I.5", sinal.Resumo);
    }

    #endregion
}

/// <summary>
/// TimeProvider fake para testes.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

    public override long GetTimestamp()
    {
        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return (long)(_utcNow - epoch).TotalMilliseconds * 10000; // ticks
    }
}
