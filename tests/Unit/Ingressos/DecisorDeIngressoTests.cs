using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Domain.Ticketing;

namespace Unit.Tests.Ingressos;

/// <summary>
/// A ponte entre a leitura da catraca e a base de ingressos.
/// </summary>
public sealed class DecisorDeIngressoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private sealed class ValidadorFalso : IValidadorDeIngressos
    {
        public Func<string, ResultadoDoUso> Responder { get; set; } =
            _ => new ResultadoDoUso(MotivoDoUso.Consumido, Guid.NewGuid(), "zet", Categoria: "inteira");

        public Func<Exception>? Explodir { get; set; }

        public List<(string Qr, KnownEventOrigin? Leitor)> Tentativas { get; } = [];

        public List<Guid> Confirmadas { get; } = [];

        public Guid UltimaTentativa { get; private set; }

        public (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
            string qrNormalizado, string gateId, string deviceId, DateTimeOffset agora, KnownEventOrigin? leitor)
        {
            if (Explodir is not null)
            {
                throw Explodir();
            }

            Tentativas.Add((qrNormalizado, leitor));
            UltimaTentativa = Guid.NewGuid();
            return (Responder(qrNormalizado), UltimaTentativa);
        }

        public void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em) => Confirmadas.Add(tentativaId);
    }

    private static long _seq;

    private static DeviceEvent Evento(KnownEventOrigin origem, string? cartao = null, string dispositivo = "inner-1") =>
        DeviceEvent.Create(
            new DeviceEventKey(dispositivo, "boot", Interlocked.Increment(ref _seq)),
            EventOrigin.From(origem),
            Agora,
            "corr",
            rawCardData: cartao);

    [Fact]
    public void Ingresso_valido_libera_e_giro_confirma_a_passagem()
    {
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        var decisao = decisor.Decidir(Evento(KnownEventOrigin.Leitor1, "1000000001"));
        var tentativa = validador.UltimaTentativa;

        decisor.AoReceberEvento(Evento(KnownEventOrigin.GiroConfirmado));

        Assert.True(decisao.ShouldRelease);
        Assert.Equal(ReasonCodes.Autorizado, decisao.Reason);
        Assert.Equal([tentativa], validador.Confirmadas);
        Assert.Equal(1, decisor.PassagensConfirmadas);
    }

    [Fact]
    public void O_leitor_de_origem_chega_ate_a_base()
    {
        // É o que permite à base aplicar "cartão só na urna".
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        decisor.Decidir(Evento(KnownEventOrigin.Leitor2, "0012345678"));

        Assert.Equal(KnownEventOrigin.Leitor2, validador.Tentativas[0].Leitor);
    }

    [Fact]
    public void Fim_do_tempo_sem_giro_encerra_a_pendencia_sem_confirmar()
    {
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        decisor.Decidir(Evento(KnownEventOrigin.Leitor1, "1000000001"));
        decisor.AoReceberEvento(Evento(KnownEventOrigin.FimTempoAcionamento));
        decisor.AoReceberEvento(Evento(KnownEventOrigin.GiroConfirmado));

        Assert.Empty(validador.Confirmadas);
        Assert.Equal(1, decisor.AutorizacoesSemGiro);
    }

    [Fact]
    public void Giro_de_uma_catraca_nao_confirma_a_leitura_de_outra()
    {
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        decisor.Decidir(Evento(KnownEventOrigin.Leitor1, "1000000001", dispositivo: "inner-1"));
        decisor.AoReceberEvento(Evento(KnownEventOrigin.GiroConfirmado, dispositivo: "inner-2"));

        Assert.Empty(validador.Confirmadas);
    }

    [Fact]
    public void Leitura_sem_codigo_e_negada_sem_consultar_a_base()
    {
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        var decisao = decisor.Decidir(Evento(KnownEventOrigin.Leitor1, "   "));

        Assert.False(decisao.ShouldRelease);
        Assert.Empty(validador.Tentativas);
    }

    [Fact]
    public void Base_que_falha_nega_em_vez_de_liberar()
    {
        // Liberar sem saber seria escolher fail-safe no lugar do cliente (B4 em aberto).
        var validador = new ValidadorFalso { Explodir = () => new InvalidOperationException("database is locked") };
        var decisor = new DecisorDeIngresso(validador);

        var decisao = decisor.Decidir(Evento(KnownEventOrigin.Leitor1, "1000000001"));

        Assert.False(decisao.ShouldRelease);
        Assert.Equal(ReasonCodes.FalhaNaBaseLocal, decisao.Reason);
    }

    [Theory]
    [InlineData(MotivoDoUso.Desconhecido, "CREDENCIAL_DESCONHECIDA")]
    [InlineData(MotivoDoUso.UsosEsgotados, "USOS_ESGOTADOS")]
    [InlineData(MotivoDoUso.Cancelado, "INGRESSO_CANCELADO")]
    [InlineData(MotivoDoUso.ForaDaJanela, "CREDENCIAL_FORA_DA_JANELA")]
    [InlineData(MotivoDoUso.EmIntervaloDeReuso, "INTERVALO_DE_REUSO")]
    [InlineData(MotivoDoUso.ForaDaUrna, "FORA_DA_URNA")]
    [InlineData(MotivoDoUso.ProvedorDesabilitado, "PROVEDOR_DESABILITADO")]
    public void Cada_motivo_vira_um_codigo_proprio(MotivoDoUso motivo, string codigo)
    {
        var validador = new ValidadorFalso { Responder = _ => new ResultadoDoUso(motivo) };

        var decisao = new DecisorDeIngresso(validador).Decidir(Evento(KnownEventOrigin.Leitor1, "1000000001"));

        Assert.False(decisao.ShouldRelease);
        Assert.Equal(codigo, decisao.Reason.Value);
    }

    [Fact]
    public void Todo_motivo_do_catalogo_tem_codigo_proprio()
    {
        // Um motivo novo sem tradução cairia em MOTIVO_NAO_MAPEADO — visível no relatório,
        // mas é um esquecimento. Este teste obriga a traduzir.
        foreach (var motivo in Enum.GetValues<MotivoDoUso>())
        {
            Assert.NotEqual(ReasonCodes.MotivoNaoMapeado, DecisorDeIngresso.CodigoPara(motivo));
        }
    }
}
