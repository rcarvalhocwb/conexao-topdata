using Access.Inteligencia;

namespace Unit.Tests.Inteligencia;

/// <summary>
/// Testes para saúde v2 e alertas v2 (Etapa I.5 do docs/36).
/// </summary>
public sealed class SaudeV2Tests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Uma_catraca_com_todos_sinais_normais_tem_indice_100()
    {
        var sinais = new List<SinalDeSaude>
        {
            new(TipoDeSinal.Comunicacao, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Giro, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Leitura, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Relogio, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Configuracao, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.GiroSemPedido, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Laco, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
        };

        var indice = SaudeDaCatraca.CalcularIndice(sinais);
        Assert.Equal(100, indice);
    }

    [Fact]
    public void Uma_catraca_com_comunicacao_em_acao_tem_indice_menor()
    {
        var sinais = new List<SinalDeSaude>
        {
            new(TipoDeSinal.Comunicacao, NivelDeAlerta.Acao, "Falha crítica", 5, 1, 10, Agora, Agora, ""),
            new(TipoDeSinal.Giro, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Leitura, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Relogio, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Configuracao, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.GiroSemPedido, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
            new(TipoDeSinal.Laco, NivelDeAlerta.Normal, "Normal", 0, 0, 10, Agora, Agora, ""),
        };

        var indice = SaudeDaCatraca.CalcularIndice(sinais);
        // Comunicação tem peso 25; ação = penalidade 1, então deduz 25 de 100 → 75
        Assert.Equal(75, indice);
    }

    [Fact]
    public void Alerta_pode_ser_renovado_com_novas_evidencias()
    {
        var alerta = new Alerta(
            "alerta-1",
            RegraDeAlerta.ComunicacaoInstavel,
            Catraca: 1,
            Portao: null,
            Nivel: NivelDeAlerta.Atencao,
            Texto: "3 reconexões",
            Conta: "3 reconexões / 15 min",
            AbertoDo: Agora,
            AtualizadoEm: Agora);

        var renovado = alerta.Renovar(Agora.AddMinutes(5), "4 reconexões", "4 reconexões / 15 min");
        
        Assert.Equal("4 reconexões", renovado.Texto);
        Assert.Equal(Agora.AddMinutes(5), renovado.AtualizadoEm);
    }

    [Fact]
    public void Alerta_pode_ser_marcado_como_ciente()
    {
        var alerta = new Alerta(
            "alerta-1",
            RegraDeAlerta.ComunicacaoInstavel,
            Catraca: 1,
            Portao: null,
            Nivel: NivelDeAlerta.Atencao,
            Texto: "3 reconexões",
            Conta: "3 reconexões / 15 min",
            AbertoDo: Agora,
            AtualizadoEm: Agora);

        var ciente = alerta.MarcarComoCiente("João", Agora.AddMinutes(1));
        
        Assert.Equal("João", ciente.CientePor);
        Assert.Equal(Agora.AddMinutes(1), ciente.CienteEm);
    }

    [Fact]
    public void RegraA6_dispara_com_3_reconexoes_em_15min()
    {
        var regra = new RegraA6Comunicacao(
            Catraca: 1,
            ReconexoesEm15Min: 3,
            ZDosErros: 1.0,
            P95Latencia: 100,
            MedianaLatenciaVizinhas: 50);

        Assert.True(regra.Dispara());
    }

    [Fact]
    public void RegraA6_dispara_com_z_erros_maior_ou_igual_4()
    {
        var regra = new RegraA6Comunicacao(
            Catraca: 1,
            ReconexoesEm15Min: 1,
            ZDosErros: 4.0,
            P95Latencia: 100,
            MedianaLatenciaVizinhas: 50);

        Assert.True(regra.Dispara());
    }

    [Fact]
    public void RegraA6_nao_dispara_sem_criterio()
    {
        var regra = new RegraA6Comunicacao(
            Catraca: 1,
            ReconexoesEm15Min: 1,
            ZDosErros: 1.0,
            P95Latencia: 100,
            MedianaLatenciaVizinhas: 50);

        Assert.False(regra.Dispara());
    }

    [Fact]
    public void RegraA12_dispara_com_multiplas_catracas_caidas()
    {
        var regra = new RegraA12QuedaSimultanea(new[] { 1, 2, 4 });
        
        Assert.True(regra.Dispara());
        Assert.Contains("caíram", regra.TextoParaOperador);
    }
}
