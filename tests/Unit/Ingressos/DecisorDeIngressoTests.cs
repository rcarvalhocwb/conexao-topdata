using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Credentials;
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

        public List<int?> Origens { get; } = [];

        public List<Guid> Confirmadas { get; } = [];

        public Guid UltimaTentativa { get; private set; }

        public (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
            string qrNormalizado, string gateId, string deviceId, DateTimeOffset agora, KnownEventOrigin? leitor,
            int? origemBruta)
        {
            if (Explodir is not null)
            {
                throw Explodir();
            }

            Tentativas.Add((qrNormalizado, leitor));
            Origens.Add(origemBruta);
            UltimaTentativa = Guid.NewGuid();
            return (Responder(qrNormalizado), UltimaTentativa);
        }

        /// <summary>Quantas gravações de giro ainda vão falhar (base ocupada, disco cheio).</summary>
        public int FalhasAoConfirmar { get; set; }

        public void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em)
        {
            if (FalhasAoConfirmar > 0)
            {
                FalhasAoConfirmar--;
                throw new InvalidOperationException("database is locked");
            }

            Confirmadas.Add(tentativaId);
        }
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

    /// <summary>
    /// Achado E1-04 do docs/41: a exceção ao gravar o giro subia até o processo do worker e o
    /// derrubava, com todas as catracas, e o giro (só em memória) se perdia.
    /// </summary>
    [Fact]
    public void Giro_que_a_base_recusa_nao_lanca_e_e_gravado_quando_a_base_volta()
    {
        var validador = new ValidadorFalso { FalhasAoConfirmar = 2 };
        var decisor = new DecisorDeIngresso(validador);
        decisor.Decidir(Evento(KnownEventOrigin.Leitor1, "1000000001"));
        var tentativa = validador.UltimaTentativa;

        var erro = Record.Exception(() => decisor.AoReceberEvento(Evento(KnownEventOrigin.GiroConfirmado)));

        Assert.Null(erro);
        Assert.Empty(validador.Confirmadas);
        Assert.Equal(1, decisor.ConfirmacoesAGravar);
        Assert.Equal(1, decisor.PassagensConfirmadas);

        decisor.GravarConfirmacoesPendentes();
        Assert.Equal(1, decisor.ConfirmacoesAGravar);

        decisor.GravarConfirmacoesPendentes();
        Assert.Equal([tentativa], validador.Confirmadas);
        Assert.Equal(0, decisor.ConfirmacoesAGravar);
        Assert.Equal(2, decisor.FalhasAoGravarConfirmacao);
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

    /// <summary>
    /// A origem bruta vai para a base qualquer que seja: é o que o painel e o gêmeo mostram
    /// (docs/35, Etapa 0.3). A regra da urna continua olhando só o leitor conhecido.
    /// </summary>
    [Fact]
    public void A_origem_bruta_chega_ate_a_base_mesmo_quando_nao_e_um_dos_leitores()
    {
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        decisor.Decidir(Evento(KnownEventOrigin.Leitor2, "0012345678"));
        decisor.Decidir(Evento(KnownEventOrigin.QrCode, "9999000101"));
        decisor.Decidir(DeviceEvent.Create(
            new DeviceEventKey("inner-1", "boot", Interlocked.Increment(ref _seq)),
            EventOrigin.FromRaw(11),
            Agora,
            "corr",
            rawCardData: "9999000102"));

        Assert.Equal([3, 21, 11], validador.Origens);
        Assert.Equal([KnownEventOrigin.Leitor2, null, null], validador.Tentativas.Select(t => t.Leitor));
    }

    /// <summary>
    /// Leitura e cadastro passam pela mesma normalização (docs/34 §2, F8): com cada perfil,
    /// o texto que chega à base é o mesmo que o cadastro gravou, quando a leitura vem no
    /// formato que o perfil espera. Números fictícios.
    /// </summary>
    [Theory]
    [InlineData("raw", "9999000101", "9999000101")]
    [InlineData("raw", "0000000101", " 0000000101 ")]
    [InlineData("qr-catraca4", "00AB12cd", "00AB12cd")]
    [InlineData("qr-catraca4", "1234", "1234 ")]
    [InlineData("mifare-catraca4", "99994567", "0099994567")]
    [InlineData("mifare-catraca4", "0099994567", "99994567")]
    [InlineData("mifare-catraca4", "0000000101", "0000000101")]
    public void Leitura_e_cadastro_do_mesmo_cartao_casam_com_cada_perfil(string nome, string cadastrado, string lido)
    {
        var perfil = PerfisDeLeitura.Todos[nome];
        var cadastro = PerfisDeLeitura.Normalizar(cadastrado, perfil);
        var validador = new ValidadorFalso();

        new DecisorDeIngresso(validador, perfilDaLeitura: perfil).Decidir(Evento(KnownEventOrigin.Leitor2, lido));

        Assert.Equal(cadastro, validador.Tentativas[0].Qr);
    }

    /// <summary>
    /// Sem perfil informado, a leitura é a de sempre (só tira espaços): nada muda na
    /// operação até a parametrização por catraca. Um cartão cadastrado com o perfil de 10
    /// dígitos casa com a catraca que entrega os 10 dígitos — o formato que o perfil espera.
    /// </summary>
    [Fact]
    public void Sem_perfil_informado_a_leitura_e_comparada_como_sempre_foi()
    {
        var validador = new ValidadorFalso();
        var decisor = new DecisorDeIngresso(validador);

        decisor.Decidir(Evento(KnownEventOrigin.Leitor1, " 0000000101 "));
        decisor.Decidir(Evento(KnownEventOrigin.Leitor2, "0099994567"));

        Assert.Equal("0000000101", validador.Tentativas[0].Qr);
        Assert.Equal(PerfisDeLeitura.Normalizar("99994567", PerfisDeLeitura.MifareCatraca4), validador.Tentativas[1].Qr);
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
    [InlineData(MotivoDoUso.TipoInativo, "TIPO_INATIVO")]
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
