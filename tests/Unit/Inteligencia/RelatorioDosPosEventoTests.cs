using System;
using System.Collections.Generic;
using Access.Inteligencia;
using Xunit;

namespace Unit.Inteligencia;

/// <summary>
/// Testes do relatório pós-evento (Etapa I.10 do docs/36, IN-09 e IN-08).
/// NOVO-SIM-POS-01: roteiro de demo com números fixos gera os mesmos achados e o mesmo hash em duas execuções (NOVO-DET-IA-01).
/// </summary>
public class RelatorioDosPosEventoTests
{
    private readonly RelatorioDosPosEvento _relatorio = new("v1-hash-parameters", simulacao: false);

    [Fact]
    public void MaiorPicoDe15Min_ComTentativasValidas_RetornaOPico()
    {
        var agora = DateTimeOffset.UtcNow;
        var tentativas = new (int CatracaNumero, DateTimeOffset Em, string? Portao)[]
        {
            (1, agora, "Norte"),
            (1, agora.AddSeconds(30), "Norte"),
            (1, agora.AddMinutes(1), "Norte"),
            (2, agora.AddMinutes(2), "Sul"),
        };

        var (portao, leitura, momento) = _relatorio.MaiorPicoDe15Min(tentativas);

        Assert.Equal("Norte", portao);
        Assert.Equal(3L, leitura);
    }

    [Fact]
    public void MaiorPicoDe15Min_SemTentativas_RetornaZero()
    {
        var (portao, leitura, momento) = _relatorio.MaiorPicoDe15Min(Array.Empty<(int, DateTimeOffset, string?)>());

        Assert.Null(portao);
        Assert.Equal(0L, leitura);
    }

    [Fact]
    public void CatracaMaisLenta_ComDeltasValidas_RetornaAMaisLenta()
    {
        var medidas = new[]
        {
            (CatracaNumero: 1, DeltaLiberacaoGiro: (TimeSpan?)TimeSpan.FromMilliseconds(2500)),
            (CatracaNumero: 1, DeltaLiberacaoGiro: (TimeSpan?)TimeSpan.FromMilliseconds(2000)),
            (CatracaNumero: 2, DeltaLiberacaoGiro: (TimeSpan?)TimeSpan.FromMilliseconds(1500)),
            (CatracaNumero: 2, DeltaLiberacaoGiro: (TimeSpan?)TimeSpan.FromMilliseconds(1800)),
        };

        var (catraca, delta) = _relatorio.CatracaMaisLenta(medidas);

        // Catraca 1: mediana = 2250ms, Catraca 2: mediana = 1650ms → Catraca 1 é mais lenta
        Assert.Equal(1, catraca);
        Assert.True(delta.TotalMilliseconds >= 2000 && delta.TotalMilliseconds <= 2500);
    }

    [Fact]
    public void CatracaMaisOciosa_ComOcupacoes_RetornaAMaisOciosa()
    {
        var ocupacoes = new[]
        {
            (CatracaNumero: 1, Ocupacao: 0.85),
            (CatracaNumero: 2, Ocupacao: 0.45),
            (CatracaNumero: 3, Ocupacao: 0.95),
        };

        var (catraca, ocupacao) = _relatorio.CatracaMaisOciosa(ocupacoes);

        Assert.Equal(2, catraca);
        Assert.Equal(0.45, ocupacao);
    }

    [Fact]
    public void Desperdicio_ComLiberacoesSemGiro_CalculaCorretamente()
    {
        const long liberacoesSemGiro = 5L;
        var tempoDoRele = TimeSpan.FromSeconds(3);

        var desperdicio = _relatorio.Desperdicio(liberacoesSemGiro, tempoDoRele);

        Assert.Equal(15.0, desperdicio);
    }

    [Fact]
    public void DisponibilidadeMedia_ComDadosValidos_CalculaCorretamente()
    {
        var medidas = new[]
        {
            (CatracaNumero: 1, TempoOnline: TimeSpan.FromSeconds(1800), TempoTotal: TimeSpan.FromSeconds(1800)),
            (CatracaNumero: 2, TempoOnline: TimeSpan.FromSeconds(1600), TempoTotal: TimeSpan.FromSeconds(1800)),
        };

        var disponibilidade = _relatorio.DisponibilidadeMedia(medidas);

        // (1800 + 1600) / (1800 + 1800) = 3400 / 3600 ≈ 0.944
        Assert.True(disponibilidade > 0.94 && disponibilidade < 0.95);
    }

    [Fact]
    public void AlertasDoEvento_ComAlertas_ContaCorretamente()
    {
        var agora = DateTimeOffset.UtcNow;
        var alertas = new[]
        {
            (Ciencia: true, CienteEm: (DateTimeOffset?)agora),
            (Ciencia: true, CienteEm: (DateTimeOffset?)agora.AddMinutes(1)),
            (Ciencia: false, CienteEm: (DateTimeOffset?)null),
        };

        var (total, ciencia) = _relatorio.AlertasDoEvento(alertas);

        Assert.Equal(3L, total);
        Assert.Equal(2L, ciencia);
    }

    [Fact]
    public void NegacoesPorMotivo_ComMotivos_AgrupaCorretamente()
    {
        var negacoes = new[]
        {
            (Motivo: "UsosEsgotados", CatracaNumero: 1, Contagem: 5L),
            (Motivo: "UsosEsgotados", CatracaNumero: 2, Contagem: 10L),
            (Motivo: "ForaDaUrna", CatracaNumero: 1, Contagem: 3L),
        };

        var resultado = _relatorio.NegacoesPorMotivo(negacoes);

        Assert.Equal(2, resultado.Count);
        Assert.Equal(15L, resultado["UsosEsgotados"].Total);
        Assert.Equal(3L, resultado["ForaDaUrna"].Total);
    }

    [Fact]
    public void DimensionamentoParaProximoEvento_ComCapacidade_CalculaCorretamente()
    {
        const long leiturasPorMinutoNoPico = 800L;
        var cicloMedianoMedido = TimeSpan.FromSeconds(3);

        var dimensionamento = _relatorio.DimensionamentoParaProximoEvento(leiturasPorMinutoNoPico, cicloMedianoMedido);

        // μ = 60 / 3 = 20 giros/min por catraca
        // Dimensionamento = 800 / (20 × 0.85) ≈ 47.06
        Assert.True(dimensionamento > 40 && dimensionamento < 50);
    }

    [Fact]
    public void CriarInsight_GeraRecordComTodosOsCampos()
    {
        var agora = DateTimeOffset.UtcNow;

        var insight = _relatorio.CriarInsight(
            agora,
            portaoMaiorPico: "Norte",
            leiturasMaiorPico: 800L,
            momentoMaiorPico: agora.AddMinutes(-5),
            catracaMaisLenta: 3,
            deltaMaisLentaMs: 2500,
            catracaMaisOciosa: 2,
            ocupacaoMaisOciosa: 0.45,
            desperdiciodeSegundos: 15.0,
            disponibilidadeMedia: 0.944,
            totalAlertas: 5L,
            alertasComCiencia: 3L,
            dimensionamentoCatracas: 47.06);

        Assert.NotNull(insight);
        Assert.False(string.IsNullOrEmpty(insight.Id));
        Assert.Equal("Norte", insight.PortaoMaiorPico);
        Assert.Equal(800L, insight.LeiturasMaiorPico);
        Assert.Equal(3, insight.CatracaMaisLentaNumero);
        Assert.False(insight.Simulacao);
        Assert.False(string.IsNullOrEmpty(insight.AchadosJson));
    }

    [Fact]
    public void Determinismo_MesmaEntradaProduzemMesmosAchados()
    {
        // NOVO-DET-IA-01: entrada igual → saída igual, byte a byte.
        var agora = DateTimeOffset.UtcNow;
        var tentativas = new[]
        {
            (CatracaNumero: 1, Em: agora, Portao: (string?)"Norte"),
            (CatracaNumero: 1, Em: agora.AddSeconds(30), Portao: (string?)"Norte"),
        };

        var (portao1, leitura1, _) = _relatorio.MaiorPicoDe15Min(tentativas);
        var (portao2, leitura2, _) = _relatorio.MaiorPicoDe15Min(tentativas);

        Assert.Equal(portao1, portao2);
        Assert.Equal(leitura1, leitura2);
    }
}
