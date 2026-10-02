using Access.Inteligencia;

namespace Unit.Tests.Inteligencia;

/// <summary>
/// Testes para ritmo e portões (Etapa I.6 do docs/36, IN-04, IN-04b).
/// </summary>
public sealed class FluidezTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Calcula_ciclo_como_mediana_de_intervalos()
    {
        var intervalos = new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 };
        
        var ciclo = AvaliacaoDaFluidez.CalcularCiclo(intervalos);
        
        Assert.Equal(3.0, ciclo);
    }

    [Fact]
    public void Calcula_capacidade_a_partir_do_ciclo()
    {
        // Ciclo de 3 segundos → 60/3 = 20 leituras/min
        var capacidade = AvaliacaoDaFluidez.CalcularCapacidade(3.0);
        
        Assert.Equal(20.0, capacidade);
    }

    [Fact]
    public void Calcula_ocupacao_como_fracao_do_tempo()
    {
        var ciclo = 3.0;
        var intervalos = new List<double> { 2.0, 3.0, 2.5, 3.0 }; // Total ocupado = min(2,3) + min(3,3) + min(2.5,3) + min(3,3) = 11.5
        var janela = 60.0; // 1 minuto

        var ocupacao = AvaliacaoDaFluidez.CalcularOcupacao(ciclo, intervalos, janela);
        
        // 11.5 / 60 ≈ 0.192
        Assert.True(ocupacao > 0.19 && ocupacao < 0.20);
    }

    [Fact]
    public void Calcula_tempo_para_escoar_demanda_conhecida()
    {
        // 8200 ingressos, capacidade total 4 × 20 = 80 leituras/min = 4800/h
        var tempoHoras = AvaliacaoDaFluidez.CalcularTempoEscoar(8200, 80.0);
        
        // 8200 / 80 = 102.5 minutos ≈ 1.71 horas
        Assert.True(tempoHoras > 1.7 && tempoHoras < 1.72);
    }

    [Fact]
    public void Recomenda_orientar_fila_quando_uma_alta_e_outra_baixa()
    {
        var rec = AvaliacaoDaFluidez.RecomendarOrientar(0.95, 3, 0.5, 4);
        
        Assert.NotNull(rec);
        Assert.Equal("orientar_fila", rec!.Tipo);
    }

    [Fact]
    public void Nao_recomenda_orientar_quando_ambas_ocupadas()
    {
        var rec = AvaliacaoDaFluidez.RecomendarOrientar(0.95, 3, 0.85, 4);
        
        Assert.Null(rec);
    }

    [Fact]
    public void Recomenda_abrir_catraca_quando_ocupacao_media_alta()
    {
        var rec = AvaliacaoDaFluidez.RecomendarAbrir(0.87, "Portão Norte");
        
        Assert.NotNull(rec);
        Assert.Equal("abrir_catraca", rec!.Tipo);
    }

    [Fact]
    public void Nao_recomenda_abrir_catraca_quando_ocupacao_baixa()
    {
        var rec = AvaliacaoDaFluidez.RecomendarAbrir(0.80, "Portão Norte");
        
        Assert.Null(rec);
    }

    [Fact]
    public void Recomenda_dedicar_ao_leitor_quando_ciclos_muito_diferentes()
    {
        // Ciclo QR = 3.1s, ciclo cartão = 5.4s
        // Razão = 5.4 / 3.1 ≈ 1.74 > 1.4
        var rec = AvaliacaoDaFluidez.RecomendarDedicar(2, "QR", 3.1, "Cartão", 5.4);
        
        Assert.NotNull(rec);
        Assert.Equal("dedicar_leitor", rec!.Tipo);
    }

    [Fact]
    public void Nao_recomenda_dedicar_quando_ciclos_similares()
    {
        // Ciclo leitor1 = 3.0s, ciclo leitor2 = 3.2s
        // Razão = 3.2 / 3.0 ≈ 1.067 < 1.4
        var rec = AvaliacaoDaFluidez.RecomendarDedicar(2, "Leitor1", 3.0, "Leitor2", 3.2);
        
        Assert.Null(rec);
    }

    [Fact]
    public void Ritmo_sem_dados_retorna_valores_negativos()
    {
        var ciclo = AvaliacaoDaFluidez.CalcularCiclo(new List<double>());
        var capacidade = AvaliacaoDaFluidez.CalcularCapacidade(ciclo);
        
        Assert.Equal(-1, ciclo);
        Assert.Equal(-1, capacidade);
    }
}
