using Access.Inteligencia;

namespace Unit.Tests.Inteligencia;

/// <summary>
/// Testes das 5 regras de alerta da Etapa I.4 (docs/36-anexos/02 §5.2).
/// Cada regra tem: caso que dispara + caso no limite que não dispara (NOVO-SIM-ALR-01).
/// </summary>
public sealed class RegrasDeAlertaTests
{
    /// <summary>A1: Leitor calado. Dispara com 0 leituras e ≥20 vizinhas lendo.</summary>
    [Fact]
    public void A1_LeitorCalado_Dispara_Quando_Sem_Leituras_E_Vizinhas_Leem()
    {
        var resultado = RegrasDeAlerta.A1LeitorCalado(
            leituras: 0,
            leiturasDasVizinhas: 20,
            vizinhasEmOperacao: 2
        );

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Acao, resultado.Nivel);
        Assert.Contains("sem leituras", resultado.Texto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20", resultado.Conta);
    }

    /// <summary>A1: Não dispara com menos de 20 leituras nas vizinhas.</summary>
    [Fact]
    public void A1_LeitorCalado_Nao_Dispara_Sem_Minimo_De_Vizinhas()
    {
        var resultado = RegrasDeAlerta.A1LeitorCalado(
            leituras: 0,
            leiturasDasVizinhas: 19, // Um a menos do limite
            vizinhasEmOperacao: 2
        );

        Assert.Null(resultado);
    }

    /// <summary>A1: Não dispara com menos de 2 vizinhas em operação.</summary>
    [Fact]
    public void A1_LeitorCalado_Nao_Dispara_Com_Uma_Vizinha()
    {
        var resultado = RegrasDeAlerta.A1LeitorCalado(
            leituras: 0,
            leiturasDasVizinhas: 20,
            vizinhasEmOperacao: 1
        );

        Assert.Null(resultado);
    }

    /// <summary>A2: Comunicação. Dispara com ≥3 reconexões em 15 min.</summary>
    [Fact]
    public void A2_Comunicacao_Dispara_Com_3_Reconexoes()
    {
        var resultado = RegrasDeAlerta.A2Comunicacao(reconexoes: 3);

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Atencao, resultado.Nivel);
        Assert.Contains("3", resultado.Texto);
    }

    /// <summary>A2: Dispara com mais de 3 reconexões.</summary>
    [Fact]
    public void A2_Comunicacao_Dispara_Com_Mais_De_3_Reconexoes()
    {
        var resultado = RegrasDeAlerta.A2Comunicacao(reconexoes: 5);

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Atencao, resultado.Nivel);
        Assert.Contains("5", resultado.Texto);
    }

    /// <summary>A2: Não dispara com 2 reconexões (no limite, mas não dispara).</summary>
    [Fact]
    public void A2_Comunicacao_Nao_Dispara_Com_2_Reconexoes()
    {
        var resultado = RegrasDeAlerta.A2Comunicacao(reconexoes: 2);

        Assert.Null(resultado);
    }

    /// <summary>A3: Relógio. Dispara com divergência > 30s.</summary>
    [Fact]
    public void A3_Relogio_Dispara_Com_Divergencia_31_Segundos()
    {
        var resultado = RegrasDeAlerta.A3Relogio(divergenciaSegundos: 31, inclinacaoSegundosPorHora: null);

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Atencao, resultado.Nivel);
        Assert.Contains("31", resultado.Texto);
    }

    /// <summary>A3: Não dispara com divergência de exatamente 30s.</summary>
    [Fact]
    public void A3_Relogio_Nao_Dispara_Com_30_Segundos()
    {
        var resultado = RegrasDeAlerta.A3Relogio(divergenciaSegundos: 30, inclinacaoSegundosPorHora: null);

        Assert.Null(resultado);
    }

    /// <summary>A3: Dispara com inclinação > 2s/h.</summary>
    [Fact]
    public void A3_Relogio_Dispara_Com_Inclinacao_2_1()
    {
        var resultado = RegrasDeAlerta.A3Relogio(divergenciaSegundos: null, inclinacaoSegundosPorHora: 2.1);

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Atencao, resultado.Nivel);
        Assert.Contains("2,1", resultado.Texto.Replace(".", ","));
    }

    /// <summary>A3: Não dispara com inclinação de exatamente 2s/h.</summary>
    [Fact]
    public void A3_Relogio_Nao_Dispara_Com_2_Segundos_Por_Hora()
    {
        var resultado = RegrasDeAlerta.A3Relogio(divergenciaSegundos: null, inclinacaoSegundosPorHora: 2.0);

        Assert.Null(resultado);
    }

    /// <summary>A4: Configuração. Dispara quando versão salva != aplicada por > 2 min.</summary>
    [Fact]
    public void A4_Configuracao_Dispara_Quando_Nao_Aplicada_Por_3_Minutos()
    {
        var resultado = RegrasDeAlerta.A4Configuracao(
            versaoSalva: "abc123",
            versaoAplicada: "def456",
            idadeDasAplicacaoMinutos: 3
        );

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Atencao, resultado.Nivel);
        Assert.Contains("não está com a configuração salva", resultado.Texto);
    }

    /// <summary>A4: Não dispara quando a idade é exatamente 2 min.</summary>
    [Fact]
    public void A4_Configuracao_Nao_Dispara_Com_2_Minutos_De_Idade()
    {
        var resultado = RegrasDeAlerta.A4Configuracao(
            versaoSalva: "abc123",
            versaoAplicada: "def456",
            idadeDasAplicacaoMinutos: 2
        );

        Assert.Null(resultado);
    }

    /// <summary>A4: Não dispara quando as versões são iguais.</summary>
    [Fact]
    public void A4_Configuracao_Nao_Dispara_Quando_Versoes_Sao_Iguais()
    {
        var resultado = RegrasDeAlerta.A4Configuracao(
            versaoSalva: "abc123",
            versaoAplicada: "abc123",
            idadeDasAplicacaoMinutos: 10
        );

        Assert.Null(resultado);
    }

    /// <summary>A5: Desconhecidos. Dispara com ≥10 em 5min + sincronização > 5min sem sucesso.</summary>
    [Fact]
    public void A5_Desconhecidos_Dispara_Com_10_E_Sincronizacao_Parada()
    {
        var resultado = RegrasDeAlerta.A5Desconhecidos(
            desconhecidosEm5Min: 10,
            idadeSincronizacaoMinutos: 6
        );

        Assert.NotNull(resultado);
        Assert.Equal(NivelDeAlerta.Atencao, resultado.Nivel);
        Assert.Contains("10", resultado.Texto);
        Assert.Contains("6", resultado.Texto);
    }

    /// <summary>A5: Não dispara com 9 desconhecidos (no limite).</summary>
    [Fact]
    public void A5_Desconhecidos_Nao_Dispara_Com_9_Codigos()
    {
        var resultado = RegrasDeAlerta.A5Desconhecidos(
            desconhecidosEm5Min: 9,
            idadeSincronizacaoMinutos: 6
        );

        Assert.Null(resultado);
    }

    /// <summary>A5: Não dispara com sincronização de exatamente 5 min.</summary>
    [Fact]
    public void A5_Desconhecidos_Nao_Dispara_Com_Sincronizacao_5_Minutos()
    {
        var resultado = RegrasDeAlerta.A5Desconhecidos(
            desconhecidosEm5Min: 10,
            idadeSincronizacaoMinutos: 5
        );

        Assert.Null(resultado);
    }

    /// <summary>A5: Não dispara sem sincronização recente (mais de 5 min, mas não dispara sem ≥10 desconhecidos).</summary>
    [Fact]
    public void A5_Desconhecidos_Nao_Dispara_Com_Poucos_Desconhecidos()
    {
        var resultado = RegrasDeAlerta.A5Desconhecidos(
            desconhecidosEm5Min: 5,
            idadeSincronizacaoMinutos: 10
        );

        Assert.Null(resultado);
    }
}
