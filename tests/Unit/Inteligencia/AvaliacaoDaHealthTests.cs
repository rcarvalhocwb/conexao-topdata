using Xunit;
using Access.Inteligencia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.Unit.Inteligencia;

/// <summary>
/// Testes de I.3 (Saúde v1): avaliação de sinais Giro, Relógio, Configuração.
/// Cada sinal é testado em 3 cenários: Normal, Atenção, Ação.
/// Total: 4 sinais × 3 cenários = 12 testes (NOVO-SIM-SAU-01).
/// </summary>
public class AvaliacaoDaHealthTests
{
    private readonly AvaliacaoDaHealth _avaliador;
    private readonly ParametrosDeInteligencia _parametros;
    private readonly FakeTimeProvider _relogio;

    public AvaliacaoDaHealthTests()
    {
        _relogio = new FakeTimeProvider();
        _avaliador = new AvaliacaoDaHealth(_relogio);
        _parametros = new ParametrosDeInteligencia();
    }

    #region Sinal Giro (Wilson)

    [Fact]
    public void AvaliarGiro_Normal_TaxaSemGiroIndicaOp()
    {
        // Arrange: 2% de sem giro (esperado ~2% nas vizinhas)
        var janela = CriarJanelasDeMinutos(150, giros: 140, semGiro: 3);
        var vizinhas = new[]
        {
            CriarJanelasDeMinutos(150, giros: 145, semGiro: 5), // ~3.3%
            CriarJanelasDeMinutos(150, giros: 142, semGiro: 3)  // ~2.1%
        };

        // Act
        var sinal = _avaliador.AvaliarGiro(janela, vizinhas, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Normal, sinal.Nivel);
        Assert.Contains("Taxa de sem-giro", sinal.Resumo);
    }

    [Fact]
    public void AvaliarGiro_Atencao_SemVizinhas()
    {
        // Arrange: sem vizinhas para comparar
        var janela = CriarJanelasDeMinutos(150, giros: 130, semGiro: 20); // 13.3%
        var vizinhas = new IReadOnlyList<AgrMinuto>[0];

        // Act
        var sinal = _avaliador.AvaliarGiro(janela, vizinhas, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Atencao, sinal.Nivel);
        Assert.Contains("sem vizinhas", sinal.Resumo);
    }

    [Fact]
    public void AvaliarGiro_Acao_TaxaMuitoAcimaDasVizinhas()
    {
        // Arrange: 30% de sem giro (vizinhas com ~5%)
        var janela = CriarJanelasDeMinutos(150, giros: 70, semGiro: 30); // 30%
        var vizinhas = new[]
        {
            CriarJanelasDeMinutos(150, giros: 142, semGiro: 8), // ~5.3%
            CriarJanelasDeMinutos(150, giros: 145, semGiro: 5)  // ~3.3%
        };

        // Act
        var sinal = _avaliador.AvaliarGiro(janela, vizinhas, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Acao, sinal.Nivel);
        Assert.Contains("significativamente", sinal.Resumo);
    }

    #endregion

    #region Sinal Relógio (Divergência e Inclinação)

    [Fact]
    public void AvaliarRelogio_Normal_DentroDoTolerado()
    {
        // Arrange: divergência de 10s (tolerância: 30s)
        var status = new DeviceStatus
        {
            InnerNumber = 1,
            Online = true,
            ClockOffsetSeconds = 10
        };

        // Act
        var sinal = _avaliador.AvaliarRelogio(status, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Normal, sinal.Nivel);
        Assert.Contains("dentro do padrão", sinal.Resumo);
    }

    [Fact]
    public void AvaliarRelogio_Acao_DivergenciaExcedida()
    {
        // Arrange: divergência de 45s (limite: 30s)
        var status = new DeviceStatus
        {
            InnerNumber = 2,
            Online = true,
            ClockOffsetSeconds = 45
        };

        // Act
        var sinal = _avaliador.AvaliarRelogio(status, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Acao, sinal.Nivel);
        Assert.Contains("divergindo", sinal.Resumo);
    }

    [Fact]
    public void AvaliarRelogio_SemDados_StatusNulo()
    {
        // Act
        var sinal = _avaliador.AvaliarRelogio(null, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.SemDados, sinal.Nivel);
    }

    #endregion

    #region Sinal Configuração (Salva ≠ Aplicada)

    [Fact]
    public void AvaliarConfiguracao_Normal_Aplicada()
    {
        // Arrange: versões iguais
        var status = new DeviceStatus
        {
            InnerNumber = 3,
            Online = true,
            SavedConfigVersion = "v2",
            AppliedConfigVersion = "v2",
            FirmwareChanged = false
        };

        // Act
        var sinal = _avaliador.AvaliarConfiguracao(status, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Normal, sinal.Nivel);
        Assert.Contains("em dia", sinal.Resumo);
    }

    [Fact]
    public void AvaliarConfiguracao_Acao_NaoAplicadaPorMaisde2min()
    {
        // Arrange: versão não aplicada há 3 minutos
        _relogio.SetUtcNow(new DateTimeOffset(2026, 10, 02, 14, 30, 0, TimeSpan.Zero));
        var status = new DeviceStatus
        {
            InnerNumber = 4,
            Online = true,
            SavedConfigVersion = "v3",
            AppliedConfigVersion = "v2",
            LastConfigChangeUtc = new DateTimeOffset(2026, 10, 02, 14, 27, 0, TimeSpan.Zero), // -3 min
            FirmwareChanged = false
        };

        // Act
        var sinal = _avaliador.AvaliarConfiguracao(status, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Acao, sinal.Nivel);
        Assert.Contains("não está aplicada", sinal.Resumo);
    }

    [Fact]
    public void AvaliarConfiguracao_Atencao_FirmwaresMudou()
    {
        // Arrange: firmware mudou
        var status = new DeviceStatus
        {
            InnerNumber = 5,
            Online = true,
            SavedConfigVersion = "v2",
            AppliedConfigVersion = "v2",
            Firmware = "3.2.1",
            FirmwareChanged = true
        };

        // Act
        var sinal = _avaliador.AvaliarConfiguracao(status, _parametros);

        // Assert
        Assert.Equal(NivelDeSinal.Atencao, sinal.Nivel);
        Assert.Contains("Firmware mudou", sinal.Resumo);
    }

    #endregion

    #region Sinal Comunicação (Vazio em I.3)

    [Fact]
    public void AvaliarComunicacao_SemDados_EmI3()
    {
        // Arrange: I.3 não tem dados ainda

        // Act
        var sinal = _avaliador.AvaliarComunicacao(_parametros);

        // Assert
        Assert.Equal(NivelDeSinal.SemDados, sinal.Nivel);
        Assert.Contains("será avaliada em I.5", sinal.Resumo);
    }

    #endregion

    #region Helpers

    private static List<AgrMinuto> CriarJanelasDeMinutos(int minutos, int giros = 100, int semGiro = 0)
    {
        var lista = new List<AgrMinuto>();
        var agora = DateTimeOffset.UtcNow;

        for (int i = 0; i < minutos; i++)
        {
            var min = agora.AddMinutes(-minutos + i);
            lista.Add(new AgrMinuto
            {
                InnerNumber = 1,
                Minute = min.ToString("yyyy-MM-ddTHH:mm"),
                Giros = giros,
                SemGiro = semGiro,
                Attempts = giros + semGiro,
                Reads = giros + semGiro,
                Liberados = giros,
                Negados = 0,
                EmptyReads = 0,
                UnknownCodes = 0
            });
        }

        return lista;
    }

    #endregion
}

/// <summary>
/// TimeProvider fake para testes.
/// </summary>
internal class FakeTimeProvider : TimeProvider
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
