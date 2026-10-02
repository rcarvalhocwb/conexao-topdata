using Access.Application.Devices;

namespace Unit.Tests;

/// <summary>
/// Regras entre campos do docs/34 §4.2 e faixas dos campos da Etapa A.2 (docs/35), cada uma
/// com um caso válido e um inválido.
/// </summary>
/// <remarks>
/// <para>
/// As regras que dependem de algo que a <see cref="DeviceConfiguration"/> não tem estão fora
/// daqui, de propósito, e registradas no docs/34 §4.2: a 4 (urna recolhendo, Etapa A.11), a
/// metade da 5 sobre o intervalo do <c>PingOnLine</c> (Etapa A.7, T24), a 12 (número do Inner,
/// já validado no assistente e nos comandos) e a 13 (modelo não ensaiado, já no laço, ADR-0010).
/// As regras 9 e 11 são alertas: como erro, recusariam a configuração de hoje.
/// </para>
/// <para>Dados sintéticos: nenhum número de cartão real.</para>
/// </remarks>
public sealed class RegrasEntreCamposTests
{
    /// <summary>Uma configuração válida, com leitor Wiegand (3) para não acionar a regra 2 sem querer.</summary>
    private static DeviceConfiguration Valida() => new()
    {
        PadraoCartao = 1,
        TipoDeLeitor = 3,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 0,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Aproxime o ingresso",
        PerfilFisico = GatePhysicalProfile.Padrao,
    };

    private static byte[] De4a16 => [.. Enumerable.Range(4, 13).Select(n => (byte)n)];

    [Fact]
    public void A_configuracao_de_referencia_e_valida_e_sem_alertas()
    {
        Assert.Empty(Valida().Validar());
        Assert.Empty(Valida().Alertas());
    }

    // ── Regra 1: Variável ⇒ conjunto não vazio; Fixo ⇒ 4–16 ─────────────────────────────

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(16)]
    public void Regra_1_fixo_de_4_a_16_e_valido(byte digitos)
    {
        Assert.Empty((Valida() with { ModoDeDigitos = ModoDeDigitos.Fixo, QuantidadeFixaDeDigitos = digitos }).Validar());
    }

    [Theory]
    [InlineData(null)]
    [InlineData((byte)1)]
    [InlineData((byte)3)]
    [InlineData((byte)17)]
    public void Regra_1_fixo_sem_quantidade_ou_fora_de_4_a_16_e_recusado(byte? digitos)
    {
        var problemas = (Valida() with { ModoDeDigitos = ModoDeDigitos.Fixo, QuantidadeFixaDeDigitos = digitos }).Validar();
        Assert.Contains(problemas, p => p.Contains("regra 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Regra_1_fixo_com_envio_de_digitos_variaveis_e_recusado()
    {
        var problemas = (Valida() with
        {
            ModoDeDigitos = ModoDeDigitos.Fixo,
            QuantidadeFixaDeDigitos = 10,
            QuantidadesVariaveisDeDigitos = [10],
            EnviarDigitosVariaveis = true,
        }).Validar();
        Assert.Contains(problemas, p => p.Contains("dígitos fixos", StringComparison.Ordinal));
    }

    [Fact]
    public void Regra_1_variavel_com_tamanhos_e_valido()
    {
        Assert.Empty((Valida() with { ModoDeDigitos = ModoDeDigitos.Variavel, QuantidadesVariaveisDeDigitos = [10] }).Validar());
    }

    [Fact]
    public void Regra_1_variavel_sem_tamanho_e_recusado()
    {
        var problemas = (Valida() with { ModoDeDigitos = ModoDeDigitos.Variavel, QuantidadesVariaveisDeDigitos = [] }).Validar();
        Assert.Contains(problemas, p => p.Contains("regra 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Regra_1_variavel_com_quantidade_fixa_junto_e_recusado()
    {
        var problemas = (Valida() with
        {
            ModoDeDigitos = ModoDeDigitos.Variavel,
            QuantidadesVariaveisDeDigitos = [10],
            QuantidadeFixaDeDigitos = 10,
        }).Validar();
        Assert.Contains(problemas, p => p.Contains("QuantidadeFixaDeDigitos fica vazia", StringComparison.Ordinal));
    }

    /// <summary>Não declarado é o de antes: nenhuma regra nova se aplica (a configuração de hoje).</summary>
    [Fact]
    public void Modo_nao_declarado_mantem_as_regras_de_antes()
    {
        var hoje = Valida() with { QuantidadeFixaDeDigitos = 10, QuantidadesVariaveisDeDigitos = [] };
        Assert.Null(hoje.ModoDeDigitos);
        Assert.Empty(hoje.Validar());
    }

    [Fact]
    public void Modo_fora_do_enum_e_recusado()
    {
        var problemas = (Valida() with { ModoDeDigitos = (ModoDeDigitos)7 }).Validar();
        Assert.Contains(problemas, p => p.Contains("ModoDeDigitos", StringComparison.Ordinal));
    }

    // ── Regra 2: padrão Livre com QR ⇒ os tamanhos cobrem todo QR aceito ────────────────

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void Regra_2_livre_com_leitor_de_qr_e_tamanhos_de_4_a_16_e_valido(byte tipoDeLeitor)
    {
        var configuracao = Valida() with
        {
            TipoDeLeitor = tipoDeLeitor,
            ModoDeDigitos = ModoDeDigitos.Variavel,
            QuantidadesVariaveisDeDigitos = De4a16,
        };
        Assert.Empty(configuracao.Validar());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void Regra_2_livre_com_leitor_de_qr_sem_cobrir_4_a_16_e_recusado(byte tipoDeLeitor)
    {
        var problemas = (Valida() with
        {
            TipoDeLeitor = tipoDeLeitor,
            ModoDeDigitos = ModoDeDigitos.Variavel,
            QuantidadesVariaveisDeDigitos = [10, 12],
        }).Validar();
        Assert.Contains(problemas, p => p.Contains("regra 2", StringComparison.Ordinal) && p.Contains("16", StringComparison.Ordinal));
    }

    /// <summary>A regra é de QR: com leitor Wiegand ou padrão Topdata ela não se aplica.</summary>
    [Fact]
    public void Regra_2_nao_se_aplica_sem_qr_ou_sem_padrao_livre()
    {
        Assert.Empty((Valida() with { ModoDeDigitos = ModoDeDigitos.Variavel, QuantidadesVariaveisDeDigitos = [10] }).Validar());
        Assert.Empty((Valida() with
        {
            PadraoCartao = 0,
            TipoDeLeitor = 8,
            ModoDeDigitos = ModoDeDigitos.Variavel,
            QuantidadesVariaveisDeDigitos = [10],
        }).Validar());
    }

    // ── Regra 3: relé 2 ≠ 0 ⇒ leitor 2 ≠ 0 (já existia; aqui o caso válido) ─────────────

    [Fact]
    public void Regra_3_rele_da_urna_com_leitor_dois_e_valido()
    {
        Assert.Empty((Valida() with { FuncaoDoAcionamento2 = 3, OperacaoDoLeitor2 = 1 }).Validar());
        Assert.NotEmpty((Valida() with { FuncaoDoAcionamento2 = 3, OperacaoDoLeitor2 = 0 }).Validar());
    }

    // ── Regra 5: mudança automática 2 ⇒ regime on-line (a metade que é da catraca) ──────

    [Fact]
    public void Regra_5_mudanca_dois_on_line_e_valida_e_off_line_e_recusada()
    {
        Assert.Empty((Valida() with { MudancaAutomatica = 2, Online = true }).Validar());
        Assert.Contains(
            (Valida() with { MudancaAutomatica = 2, Online = false }).Validar(),
            p => p.Contains("MudancaAutomatica", StringComparison.Ordinal));
    }

    // ── Regra 6: exibir data ⇒ mensagem ≤ 16 ────────────────────────────────────────────

    public static TheoryData<string> CamposDeMensagem() =>
    [
        nameof(DeviceConfiguration.MensagemDeApresentacaoDaEntrada),
        nameof(DeviceConfiguration.MensagemDeApresentacaoDaSaida),
        nameof(DeviceConfiguration.MensagemPadraoOffLine),
        nameof(DeviceConfiguration.MensagemDeEntradaOffLine),
        nameof(DeviceConfiguration.MensagemDeSaidaOffLine),
    ];

    private static DeviceConfiguration ComMensagem(string campo, MensagemDoDisplay mensagem) => campo switch
    {
        nameof(DeviceConfiguration.MensagemDeApresentacaoDaEntrada) => Valida() with { MensagemDeApresentacaoDaEntrada = mensagem },
        nameof(DeviceConfiguration.MensagemDeApresentacaoDaSaida) => Valida() with { MensagemDeApresentacaoDaSaida = mensagem },
        nameof(DeviceConfiguration.MensagemPadraoOffLine) => Valida() with { MensagemPadraoOffLine = mensagem },
        nameof(DeviceConfiguration.MensagemDeEntradaOffLine) => Valida() with { MensagemDeEntradaOffLine = mensagem },
        nameof(DeviceConfiguration.MensagemDeSaidaOffLine) => Valida() with { MensagemDeSaidaOffLine = mensagem },
        _ => throw new ArgumentOutOfRangeException(nameof(campo), campo, "campo desconhecido"),
    };

    [Theory]
    [MemberData(nameof(CamposDeMensagem))]
    public void Regra_6_com_data_ate_16_e_valido_e_17_e_recusado(string campo)
    {
        Assert.Empty(ComMensagem(campo, new MensagemDoDisplay(new string('A', 16), ExibirData: true)).Validar());

        var problemas = ComMensagem(campo, new MensagemDoDisplay(new string('A', 17), ExibirData: true)).Validar();
        Assert.Contains(problemas, p => p.Contains(campo, StringComparison.Ordinal) && p.Contains("regra 6", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CamposDeMensagem))]
    public void Regra_6_sem_data_vai_ate_32(string campo)
    {
        Assert.Empty(ComMensagem(campo, new MensagemDoDisplay(new string('A', 32))).Validar());
        Assert.Contains(
            ComMensagem(campo, new MensagemDoDisplay(new string('A', 33))).Validar(),
            p => p.Contains(campo, StringComparison.Ordinal));
    }

    // ── Regras 7 e 8: lista ─────────────────────────────────────────────────────────────

    [Fact]
    public void Regra_7_sem_lista_e_valido_e_lista_branca_sem_lista_gravada_e_recusada()
    {
        Assert.Empty((Valida() with { TipoDeLista = 0, EnviarTipoDeLista = true }).Validar());

        var problemas = (Valida() with { TipoDeLista = 1 }).Validar();
        Assert.Contains(problemas, p => p.Contains("regra 7", StringComparison.Ordinal));
        Assert.DoesNotContain(problemas, p => p.Contains("regra 8", StringComparison.Ordinal));
    }

    [Fact]
    public void Regra_8_lista_negra_sem_decisao_d5_e_recusada()
    {
        var problemas = (Valida() with { TipoDeLista = 2 }).Validar();
        Assert.Contains(problemas, p => p.Contains("regra 8", StringComparison.Ordinal) && p.Contains("D5", StringComparison.Ordinal));
    }

    [Fact]
    public void Tipo_de_lista_fora_da_faixa_e_recusado()
    {
        Assert.Contains((Valida() with { TipoDeLista = 3 }).Validar(), p => p.Contains("FUN:34", StringComparison.Ordinal));
    }

    // ── Regra 9 (alerta): tipo de leitor 8 com recepção numérica ────────────────────────

    [Fact]
    public void Regra_9_leitor_oito_da_alerta_sem_recusar()
    {
        var oito = Valida() with { TipoDeLeitor = 8 };

        Assert.Empty(oito.Validar());
        Assert.Contains(oito.Alertas(), a => a.Contains("T25", StringComparison.Ordinal));
        Assert.Empty((Valida() with { TipoDeLeitor = 5 }).Alertas());
    }

    // ── Regra 10: forma de entrada na faixa documentada (a coerência é T26) ─────────────

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(100)]
    [InlineData(105)]
    public void Regra_10_forma_de_entrada_documentada_e_valida(byte forma)
    {
        Assert.Empty((Valida() with { FormasDeEntradaOnLine = FormasDeEntradaOnLine.DeHoje with { FormaEntrada = forma } }).Validar());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(99)]
    [InlineData(106)]
    public void Regra_10_forma_de_entrada_fora_da_matriz_e_recusada(byte forma)
    {
        var problemas = (Valida() with { FormasDeEntradaOnLine = FormasDeEntradaOnLine.DeHoje with { FormaEntrada = forma } }).Validar();
        Assert.Contains(problemas, p => p.Contains("FormaEntrada", StringComparison.Ordinal));
    }

    // ── Regra 11 (alerta): tempo do relé 1 menor que a espera pelo giro ─────────────────

    [Fact]
    public void Regra_11_tempo_do_rele_ate_a_espera_do_giro_da_alerta_sem_recusar()
    {
        Assert.Empty((Valida() with { TempoDoAcionamento1 = 7 }).Alertas());

        var longo = Valida() with { TempoDoAcionamento1 = 8 };
        Assert.Empty(longo.Validar());
        Assert.Contains(longo.Alertas(), a => a.Contains("TempoDoAcionamento1", StringComparison.Ordinal));
    }

    // ── Faixas dos campos novos ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Registro_de_acesso_negado_de_0_a_3_e_valido(byte tipo)
    {
        Assert.Empty((Valida() with { RegistrarAcessoNegado = tipo }).Validar());
    }

    [Fact]
    public void Registro_de_acesso_negado_acima_de_3_e_recusado()
    {
        Assert.Contains((Valida() with { RegistrarAcessoNegado = 4 }).Validar(), p => p.Contains("RegistrarAcessoNegado", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0000000101")] // zeros à esquerda contam: é texto
    [InlineData("99990000000101")] // 14 dígitos
    public void Cartao_master_com_ate_14_digitos_no_padrao_livre_e_valido(string codigo)
    {
        Assert.Empty((Valida() with { CartaoMaster = new CodigoDoCartaoMaster(codigo) }).Validar());
    }

    [Theory]
    [InlineData("")]
    [InlineData("999900000001011")] // 15
    [InlineData("9999A000")]
    [InlineData(" 99990001")]
    public void Cartao_master_fora_do_formato_e_recusado_sem_mostrar_o_numero(string codigo)
    {
        var problemas = (Valida() with { CartaoMaster = new CodigoDoCartaoMaster(codigo) }).Validar();

        Assert.Contains(problemas, p => p.Contains("cartão master", StringComparison.Ordinal));
        if (codigo.Length > 0)
        {
            Assert.DoesNotContain(problemas, p => p.Contains(codigo.Trim(), StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Cartao_master_com_padrao_topdata_e_recusado()
    {
        var problemas = (Valida() with { PadraoCartao = 0, CartaoMaster = new CodigoDoCartaoMaster("99990000000101") }).Validar();
        Assert.Contains(problemas, p => p.Contains("Livre", StringComparison.Ordinal));
    }

    /// <summary>
    /// A <see cref="DeviceConfiguration"/> é um <c>record</c> e imprime os campos: o número do
    /// master não pode sair por aí (regra de logs do docs/35).
    /// </summary>
    [Fact]
    public void Cartao_master_nunca_aparece_no_texto_da_configuracao()
    {
        const string Codigo = "99990000000101";
        var configuracao = Valida() with { CartaoMaster = new CodigoDoCartaoMaster(Codigo) };

        Assert.DoesNotContain(Codigo, configuracao.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Codigo, configuracao.CartaoMaster!.ToString(), StringComparison.Ordinal);
        Assert.Equal(Codigo, configuracao.CartaoMaster.RevelarParaADll());
        Assert.Equal(new CodigoDoCartaoMaster(Codigo), configuracao.CartaoMaster);
    }

    // ── Padrões: nada da Etapa A.2 chega ligado ─────────────────────────────────────────

    /// <summary>Toda chave nasce desligada, e o que não tem fonte nasce vazio.</summary>
    [Fact]
    public void Campos_da_etapa_a2_nascem_sem_enviar_nada()
    {
        foreach (var configuracao in new[] { Valida(), PadroesDeFabrica.TopFit4 })
        {
            Assert.False(configuracao.EnviarDataHoraNoEventoOnLine);
            Assert.False(configuracao.EnviarTipoDeLista);
            Assert.False(configuracao.EnviarWiegandDoisLeitores);
            Assert.False(configuracao.EnviarFormasDeEntradaOnLine);
            Assert.Null(configuracao.RegistrarAcessoNegado);
            Assert.Null(configuracao.CartaoMaster);
            Assert.Null(configuracao.ModoDeDigitos);
            Assert.Null(configuracao.WebServerDesabilitado);
            Assert.Null(configuracao.MensagemDeApresentacaoDaEntrada);
            Assert.Null(configuracao.MensagemDeApresentacaoDaSaida);
            Assert.Null(configuracao.MensagemPadraoOffLine);
            Assert.Null(configuracao.MensagemDeEntradaOffLine);
            Assert.Null(configuracao.MensagemDeSaidaOffLine);

            // Os valores propostos para quando a chave ligar (anexo 01 §3.1).
            Assert.True(configuracao.DataHoraNoEventoOnLine);
            Assert.Equal(0, configuracao.TipoDeLista);
            Assert.Equal(new WiegandDoisLeitores(false, false), configuracao.WiegandDoisLeitores);
            Assert.Equal(new FormasDeEntradaOnLine(0, 0, 7, 0, 0), configuracao.FormasDeEntradaOnLine);
        }

        Assert.Empty(PadroesDeFabrica.TopFit4.Validar());
    }
}
