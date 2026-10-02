using Access.Inteligencia;

namespace Unit.Tests.Inteligencia;

/// <summary>
/// NOVO-SIM-RUSO-01 (I.7): detecção de reuso de ingresso (IN-03, A4).
/// - 20 de 20 reusos detectados;
/// - 0 alertas com passe de 2 usos (exemplo: `2000000004`).
/// </summary>
public sealed class AvaliacaoDeReusoTests
{
    private static readonly DateTimeOffset BaseDeTempos = new(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Regra 1: UsosEsgotados em múltiplas catracas em ≤ 5 min.
    /// Um ingresso é negado em catraca 1 às 15:00, depois em catraca 2 às 15:02.
    /// </summary>
    [Fact]
    public void DetectaUsosEsgotadosEmMultiplasCatracas()
    {
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-001",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-001",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(2)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(3));

        Assert.True(resultado.Detectado);
        Assert.Equal("UsosEsgotados", resultado.Regra);
        Assert.Contains(1, resultado.Catracas!);
        Assert.Contains(2, resultado.Catracas!);
        Assert.NotNull(resultado.Prova);
    }

    /// <summary>
    /// Regra 1: EmIntervaloDeReuso em múltiplas catracas em ≤ 5 min.
    /// Um cartão com intervalo de reuso tenta em catraca 1 às 15:00, depois em catraca 2 às 15:01.
    /// </summary>
    [Fact]
    public void DetectaEmIntervaloDeReusoEmMultiplasCatracas()
    {
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-002",
                Impressao: null,
                Outcome: "negado",
                Motivo: "EmIntervaloDeReuso",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-002",
                Impressao: null,
                Outcome: "negado",
                Motivo: "EmIntervaloDeReuso",
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(1)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(2));

        Assert.True(resultado.Detectado);
        Assert.Equal("UsosEsgotados", resultado.Regra);
        Assert.Contains(1, resultado.Catracas!);
        Assert.Contains(2, resultado.Catracas!);
    }

    /// <summary>
    /// Regra 3: Código desconhecido tentado ≥ 5 vezes em ≥ 2 catracas em ≤ 10 min.
    /// Um QR desconhecido é tentado 5 vezes em catracas 1 e 2 em 10 min.
    /// </summary>
    [Fact]
    public void DetectaCodigoDesconhecidoCirculando()
    {
        var impressao = "hmac-sha256:abc123def456abc123def456abc123def456abc123def456abc123def456abc1";
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 2,
                Em: BaseDeTempos.AddSeconds(30)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(2)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(4)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(6)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(7));

        Assert.True(resultado.Detectado);
        Assert.Equal("CodigoDesconhecidoCirculando", resultado.Regra);
        Assert.Contains(1, resultado.Catracas!);
        Assert.Contains(2, resultado.Catracas!);
        Assert.Equal(2, resultado.Catracas!.Length);
        Assert.NotNull(resultado.Prova);
    }

    /// <summary>
    /// Não dispara: UsosEsgotados em apenas 1 catraca (sem reuso).
    /// </summary>
    [Fact]
    public void NaoDetectaUsosEsgotadosEmUmaCatraca()
    {
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-003",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-003",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(1)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(2));

        Assert.False(resultado.Detectado);
    }

    /// <summary>
    /// Não dispara: UsosEsgotados fora da janela de 5 min (ocorreu à 15:00, atual 15:10).
    /// </summary>
    [Fact]
    public void NaoDetectaReusoForaDoJanela()
    {
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-004",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-004",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(10)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(11));

        Assert.False(resultado.Detectado);
    }

    /// <summary>
    /// Passe de múltiplos usos: ingresso com max_uses = 2 que é consumido uma vez em cada
    /// catraca não dispara alerta (é uso legítimo, não reuso).
    /// O passe é idempotente: duas tentativas consumidas não são reuso porque a primeira consumiu
    /// um dos usos legítimos.
    /// </summary>
    [Fact]
    public void NaoDetectaPasseDeMulitipleUsosMaisUmaPorCatraca()
    {
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-passe-2",
                Impressao: null,
                Outcome: "consumido",
                Motivo: null,
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-passe-2",
                Impressao: null,
                Outcome: "consumido",
                Motivo: null,
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(1)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(2));

        Assert.False(resultado.Detectado);
    }

    /// <summary>
    /// Código desconhecido: 4 tentativas em 2 catracas em 10 min não dispara (precisa de ≥ 5).
    /// </summary>
    [Fact]
    public void NaoDetectaCodigoDesconhecidoComMenosDe5Tentativas()
    {
        var impressao = "hmac-sha256:xyz789uvw012xyz789uvw012xyz789uvw012xyz789uvw012xyz789uvw012xyz7";
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 2,
                Em: BaseDeTempos.AddSeconds(30)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(2)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(4)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(5));

        Assert.False(resultado.Detectado);
    }

    /// <summary>
    /// Código desconhecido: 5 tentativas em apenas 1 catraca não dispara (precisa de ≥ 2 catracas).
    /// </summary>
    [Fact]
    public void NaoDetectaCodigoDesconhecidoEmUmaCatraca()
    {
        var impressao = "hmac-sha256:pqr345stu678pqr345stu678pqr345stu678pqr345stu678pqr345stu678pqr3";
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddSeconds(30)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(2)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(4)),

            new RegistroDeTentativaDeReuso(
                TicketId: null,
                Impressao: impressao,
                Outcome: "negado",
                Motivo: "Desconhecido",
                InnerNumber: 1,
                Em: BaseDeTempos.AddMinutes(6)),
        };

        var resultado = AvaliacaoDeReuso.Avaliar(tentativas, BaseDeTempos.AddMinutes(7));

        Assert.False(resultado.Detectado);
    }

    /// <summary>
    /// Determinismo: mesma entrada, mesma saída, byte a byte.
    /// </summary>
    [Fact]
    public void TemDeterminismo()
    {
        var tentativas = new[]
        {
            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-determ",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 1,
                Em: BaseDeTempos),

            new RegistroDeTentativaDeReuso(
                TicketId: "ticket-determ",
                Impressao: null,
                Outcome: "negado",
                Motivo: "UsosEsgotados",
                InnerNumber: 2,
                Em: BaseDeTempos.AddMinutes(1)),
        };

        var agora = BaseDeTempos.AddMinutes(2);
        var resultado1 = AvaliacaoDeReuso.Avaliar(tentativas, agora);
        var resultado2 = AvaliacaoDeReuso.Avaliar(tentativas, agora);

        Assert.Equal(resultado1.Detectado, resultado2.Detectado);
        Assert.Equal(resultado1.Regra, resultado2.Regra);
        Assert.Equal(resultado1.Prova, resultado2.Prova);
    }
}
