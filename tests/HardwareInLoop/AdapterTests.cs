using Access.Application.Devices;
using Access.Domain.Devices;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// Tradução do adapter, exercitada sem a DLL.
/// </summary>
/// <remarks>
/// O que se verifica aqui é a parte que <b>não</b> depende de hardware: mapeamento de
/// retorno, ordem das chamadas, montagem de data, escolha do sentido de giro e leitura do
/// buffer do cartão. O que só a bancada responde está marcado <c>A_CONFIRMAR</c> no adapter.
/// </remarks>
public sealed class AdapterTests
{
    private static DeviceConfiguration Configuracao(bool sentidoInvertido = false) => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 1,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "ENTRADA - PISTA A",
        PerfilFisico = new GatePhysicalProfile(sentidoInvertido),
    };

    /// <summary>O tipo de conexão precisa ser definido antes de abrir a porta.</summary>
    [Fact]
    public void Abrir_porta_define_o_tipo_de_conexao_antes()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.AbrirPorta(3570);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal(["DefinirTipoConexao", "AbrirPortaComunicacao"], costura.Chamadas);
    }

    /// <summary>Se o tipo de conexão falha, não se abre porta nenhuma.</summary>
    [Fact]
    public void Tipo_de_conexao_recusado_impede_a_abertura()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["DefinirTipoConexao"] = 9;

        using var adapter = new TopdataInnerAdapter(costura);
        var resultado = adapter.AbrirPorta(3570);

        Assert.NotEqual(AdapterStatus.Ok, resultado.Status);
        Assert.DoesNotContain("AbrirPortaComunicacao", costura.Chamadas);
    }

    /// <summary>O retorno 8 é GPF, e a mensagem para o operador depende de reconhecê-lo.</summary>
    [Fact]
    public void Retorno_oito_vira_falha_de_dependencia()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["AbrirPortaComunicacao"] = 8;

        using var adapter = new TopdataInnerAdapter(costura);

        Assert.Equal(AdapterStatus.FalhaDeDependencia, adapter.AbrirPorta(3570).Status);
    }

    [Fact]
    public void Firmware_e_montado_a_partir_dos_seis_campos()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, firmware) = adapter.LerFirmware(1);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.NotNull(firmware);
        Assert.Equal("5.20.1", firmware.Versao);
        Assert.Equal((byte)4, firmware.Linha);
        Assert.True(firmware.TemBiometria);
    }

    [Fact]
    public void Firmware_com_erro_nao_devolve_dado_inventado()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["ReceberVersaoFirmware"] = 1;

        using var adapter = new TopdataInnerAdapter(costura);
        var (_, firmware) = adapter.LerFirmware(1);

        Assert.Null(firmware);
    }

    /// <summary>O equipamento devolve o ano com dois dígitos.</summary>
    [Fact]
    public void Relogio_monta_o_ano_de_dois_digitos_como_dois_mil_e_algo()
    {
        var costura = new CosturaFalsa { DataADevolver = (24, 9, 26, 19, 30, 45) };
        using var adapter = new TopdataInnerAdapter(costura);

        var (_, relogio) = adapter.LerRelogio(1);

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 19, 30, 45, TimeSpan.Zero), relogio);
    }

    /// <summary>
    /// Relógio zerado não pode derrubar o laço.
    /// </summary>
    /// <remarks>
    /// Um equipamento recém-ligado pode devolver dia 0, mês 0. Lançar aqui perderia o
    /// evento inteiro por causa do carimbo de hora.
    /// </remarks>
    [Fact]
    public void Relogio_invalido_nao_lanca()
    {
        var costura = new CosturaFalsa { DataADevolver = (0, 0, 0, 0, 0, 0) };
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, relogio) = adapter.LerRelogio(1);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal(DateTimeOffset.MinValue, relogio);
    }

    /// <summary>
    /// Configuração inválida nem chega a ser montada.
    /// </summary>
    /// <remarks>
    /// Enviar pela metade é pior que não enviar: <c>EnviarConfiguracoes</c> completa o resto
    /// com os padrões da DLL e sobrescreve em silêncio o que o técnico ajustou.
    /// </remarks>
    [Fact]
    public void Configuracao_invalida_nao_toca_no_equipamento()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        var invalida = Configuracao() with { TipoDeLeitor = 99 };

        Assert.Throws<ArgumentException>(() => adapter.EnviarConfiguracaoCompleta(1, invalida));
        Assert.Empty(costura.Chamadas);
    }

    [Fact]
    public void Configuracao_valida_termina_em_enviar_configuracoes()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.EnviarConfiguracaoCompleta(1, Configuracao());

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal("EnviarConfiguracoes", costura.Chamadas[^1]);
        Assert.Contains("ConfigurarTipoLeitor", costura.Chamadas);
    }

    /// <summary>Um passo que falha interrompe a montagem.</summary>
    [Fact]
    public void Passo_que_falha_impede_o_envio()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["ConfigurarLeitor1"] = 1;

        using var adapter = new TopdataInnerAdapter(costura);
        var resultado = adapter.EnviarConfiguracaoCompleta(1, Configuracao());

        Assert.NotEqual(AdapterStatus.Ok, resultado.Status);
        Assert.DoesNotContain("EnviarConfiguracoes", costura.Chamadas);
    }

    /// <summary>
    /// O sentido invertido vem do comissionamento, e liberar para o lado errado trava a fila.
    /// </summary>
    [Theory]
    [InlineData(false, "LiberarCatracaEntrada")]
    [InlineData(true, "LiberarCatracaEntradaInvertida")]
    public void Sentido_do_giro_segue_o_perfil_fisico(bool invertido, string esperada)
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.EnviarConfiguracaoCompleta(1, Configuracao(invertido));
        costura.Chamadas.Clear();

        adapter.LiberarGiro(1, GateDirection.Entrada);

        Assert.Equal([esperada], costura.Chamadas);
    }

    /// <summary>Sem configuração enviada, assume-se o sentido não invertido.</summary>
    [Fact]
    public void Sem_perfil_conhecido_usa_o_sentido_direto()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.LiberarGiro(7, GateDirection.Entrada);

        Assert.Equal(["LiberarCatracaEntrada"], costura.Chamadas);
    }

    /// <summary>Origem zero não existe na tabela: é ausência de evento.</summary>
    [Fact]
    public void Sem_evento_a_origem_vem_zerada()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, evento) = adapter.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.Equal(AdapterStatus.SemEventos, resultado.Status);
        Assert.Null(evento);
    }

    [Fact]
    public void Evento_traz_origem_credencial_e_hora_do_equipamento()
    {
        var costura = new CosturaFalsa
        {
            OrigemADevolver = (byte)KnownEventOrigin.GiroConfirmado,
            CartaoADevolver = "0012345678",
        };

        using var adapter = new TopdataInnerAdapter(costura);
        var (resultado, evento) = adapter.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.NotNull(evento);
        Assert.True(evento.Origin.ConfirmaPassagemFisica);
        Assert.Equal("0012345678", evento.RawCardData);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 19, 30, 45, TimeSpan.Zero), evento.DeviceTime);
    }

    /// <summary>Zeros à esquerda precisam sobreviver à leitura do buffer.</summary>
    [Fact]
    public void Zeros_a_esquerda_da_credencial_sobrevivem()
    {
        var costura = new CosturaFalsa
        {
            OrigemADevolver = (byte)KnownEventOrigin.Teclado,
            CartaoADevolver = "00000042",
        };

        using var adapter = new TopdataInnerAdapter(costura);
        var (_, evento) = adapter.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.Equal("00000042", evento!.RawCardData);
    }

    [Fact]
    public void Bilhete_sem_cartao_significa_memoria_vazia()
    {
        var costura = new CosturaFalsa { CartaoADevolver = string.Empty };
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, bilhete) = adapter.ColetarBilhete(1);

        Assert.Equal(AdapterStatus.SemBilhetes, resultado.Status);
        Assert.Null(bilhete);
    }

    /// <summary>O bilhete off-line não tem segundos — gravar zero é honesto.</summary>
    [Fact]
    public void Bilhete_traz_cartao_e_hora_sem_segundos()
    {
        var costura = new CosturaFalsa { CartaoADevolver = "0099887766" };
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, bilhete) = adapter.ColetarBilhete(1);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.NotNull(bilhete);
        Assert.Equal("0099887766", bilhete.Cartao);
        Assert.Equal(0, bilhete.Quando.Second);
    }

    /// <summary>O display tem 32 caracteres; passar disso corta, não estoura.</summary>
    [Fact]
    public void Mensagem_longa_e_cortada_no_limite_do_display()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.EnviarMensagemPadrao(1, new string('X', 80));

        Assert.Equal(32, costura.UltimaMensagem.Length);
    }

    /// <summary>O tempo da mensagem é um byte: acima de 255 s, satura.</summary>
    [Fact]
    public void Tempo_de_mensagem_satura_em_duzentos_e_cinquenta_e_cinco()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.ExibirMensagemTemporaria(1, "TESTE", TimeSpan.FromHours(1));

        Assert.Equal(255, costura.UltimoTempoDeMensagem);
    }

    /// <summary>Não fechar a porta vaza o socket e a próxima abertura dá retorno 3.</summary>
    [Fact]
    public void Descartar_fecha_a_porta_que_ficou_aberta()
    {
        var costura = new CosturaFalsa();
        var adapter = new TopdataInnerAdapter(costura);

        adapter.AbrirPorta(3570);
        adapter.Dispose();

        Assert.Contains("FecharPortaComunicacao", costura.Chamadas);
    }

    [Fact]
    public void Descartar_sem_porta_aberta_nao_fecha_nada()
    {
        var costura = new CosturaFalsa();
        var adapter = new TopdataInnerAdapter(costura);

        adapter.Dispose();

        Assert.DoesNotContain("FecharPortaComunicacao", costura.Chamadas);
    }

    [Fact]
    public void Descartar_duas_vezes_nao_fecha_duas_vezes()
    {
        var costura = new CosturaFalsa();
        var adapter = new TopdataInnerAdapter(costura);

        adapter.AbrirPorta(3570);
        adapter.Dispose();
        adapter.Dispose();

        Assert.Single(costura.Chamadas, c => c == "FecharPortaComunicacao");
    }
}
