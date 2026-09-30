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
    private static DeviceConfiguration Configuracao() => new()
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
        PerfilFisico = GatePhysicalProfile.Padrao,
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

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 19, 30, 45, TimeSpan.FromHours(-3)), relogio);
    }

    /// <summary>
    /// Acertar o relógio manda o horário de Brasília, com o ano em dois dígitos, qualquer que
    /// seja o fuso do instante pedido.
    /// </summary>
    [Fact]
    public void Acertar_relogio_manda_o_horario_de_brasilia_com_ano_de_dois_digitos()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        // 22:30:05 UTC = 19:30:05 em Brasília.
        var resultado = adapter.AcertarRelogio(3, new DateTimeOffset(2026, 9, 24, 22, 30, 5, TimeSpan.Zero));

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal(((byte)24, (byte)9, (byte)26, (byte)19, (byte)30, (byte)5), costura.DataEnviada);
    }

    /// <summary>O que se acerta é o que se lê de volta: o mesmo instante, sem as três horas de UTC.</summary>
    [Fact]
    public void Relogio_acertado_e_lido_de_volta_e_o_mesmo_instante()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);
        var instante = new DateTimeOffset(2026, 12, 31, 23, 59, 58, TimeSpan.FromHours(-3));

        adapter.AcertarRelogio(1, instante);
        costura.DataADevolver = costura.DataEnviada!.Value;
        var (_, lido) = adapter.LerRelogio(1);

        Assert.Equal(instante, lido);
    }

    /// <summary>Ano fora de 2000–2099 não cabe em dois dígitos: recusado antes de chegar à catraca.</summary>
    [Fact]
    public void Acertar_relogio_fora_do_seculo_e_recusado()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            adapter.AcertarRelogio(1, new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.FromHours(-3))));
        Assert.Null(costura.DataEnviada);
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

    /// <summary>
    /// A sequência nativa de uma configuração de hoje, sem a chave de dígitos variáveis.
    /// </summary>
    /// <remarks>
    /// Sem <c>EnviarMensagemPadraoOnLine</c> entre a montagem e o envio desde a Etapa 0.5:
    /// ela violava a ADR-0006 e tem o seu passo próprio no laço (F7, docs/34 §2).
    /// </remarks>
    private static readonly string[] SequenciaSemDigitosVariaveis =
    [
        "DefinirPadraoCartao",
        "DefinirQuantidadeDigitosCartao",
        "ConfigurarTipoLeitor",
        "ConfigurarLeitor1",
        "ConfigurarLeitor2",
        "ConfigurarAcionamento1",
        "ConfigurarAcionamento2",
        "ConfigurarInnerOnLine",
        "HabilitarTeclado",
        "HabilitarMudancaOnLineOffLine",
        "EnviarConfiguracoes",
    ];

    /// <summary>
    /// Sem a chave <c>catraca.enviar_digitos_variaveis</c>, nada muda: nem com tamanhos
    /// variáveis informados a função EI-012 é chamada (F2, docs/34 §2).
    /// </summary>
    [Fact]
    public void Sem_a_chave_os_digitos_variaveis_nao_sao_enviados_e_a_sequencia_e_a_de_hoje()
    {
        var semTamanhos = new CosturaFalsa();
        using (var adapter = new TopdataInnerAdapter(semTamanhos))
        {
            adapter.EnviarConfiguracaoCompleta(1, Configuracao());
        }

        var comTamanhos = new CosturaFalsa();
        using (var adapter = new TopdataInnerAdapter(comTamanhos))
        {
            adapter.EnviarConfiguracaoCompleta(1, Configuracao() with { QuantidadesVariaveisDeDigitos = [4, 10, 16] });
        }

        Assert.Equal(SequenciaSemDigitosVariaveis, semTamanhos.Chamadas);
        Assert.Equal(SequenciaSemDigitosVariaveis, comTamanhos.Chamadas);
        Assert.Empty(comTamanhos.DigitosVariaveis);
    }

    /// <summary>
    /// Com a chave, uma chamada por tamanho, junto das funções de cartão e antes de
    /// <c>EnviarConfiguracoes</c> — do contrário, não valeria (FUN:13, ADR-0006).
    /// </summary>
    [Fact]
    public void Com_a_chave_cada_tamanho_vai_numa_chamada_antes_do_envio()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        var configuracao = Configuracao() with
        {
            QuantidadeFixaDeDigitos = null,
            QuantidadesVariaveisDeDigitos = [4, 10, 16],
            EnviarDigitosVariaveis = true,
        };

        var resultado = adapter.EnviarConfiguracaoCompleta(1, configuracao);

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal([4, 10, 16], costura.DigitosVariaveis);
        Assert.Equal(
            [
                "DefinirPadraoCartao",
                "InserirQuantidadeDigitoVariavel",
                "InserirQuantidadeDigitoVariavel",
                "InserirQuantidadeDigitoVariavel",
                "ConfigurarTipoLeitor",
            ],
            costura.Chamadas.Take(5));
        Assert.Equal("EnviarConfiguracoes", costura.Chamadas[^1]);
    }

    /// <summary>As funções da costura que falam com uma catraca (primeiro parâmetro <c>inner</c>).</summary>
    private static readonly HashSet<string> FuncoesComInner = typeof(Topdata.EasyInner.Interop.IEasyInnerNative)
        .GetMethods()
        .Where(m => m.GetParameters() is [{ Name: "inner" }, ..])
        .Select(m => m.Name)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Entre a primeira função de montagem e <c>EnviarConfiguracoes</c>, nenhuma chamada fala
    /// com a catraca: o buffer é global da DLL e o envio o limpa (ADR-0006; F7, docs/34 §2).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nada_com_inner_entre_montar_e_enviar(bool digitosVariaveis)
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.EnviarConfiguracaoCompleta(1, Configuracao() with
        {
            QuantidadesVariaveisDeDigitos = [4, 16],
            EnviarDigitosVariaveis = digitosVariaveis,
        });

        Assert.Contains("EnviarConfiguracoes", FuncoesComInner);
        Assert.Contains("EnviarMensagemPadraoOnLine", FuncoesComInner);

        var inicio = costura.Chamadas.FindIndex(c => c.StartsWith("Definir", StringComparison.Ordinal)
                                                    || c.StartsWith("Configurar", StringComparison.Ordinal));
        var envio = costura.Chamadas.IndexOf("EnviarConfiguracoes");
        Assert.True(inicio >= 0 && envio > inicio);

        var noMeio = costura.Chamadas.Skip(inicio).Take(envio - inicio).Where(FuncoesComInner.Contains).ToList();
        Assert.True(noMeio.Count == 0, "Chamadas com Inner no meio da montagem: " + string.Join(", ", noMeio));
        Assert.DoesNotContain("EnviarMensagemPadraoOnLine", costura.Chamadas);
    }

    /// <summary>Tamanho repetido na lista não vira chamada repetida: é um por tamanho.</summary>
    [Fact]
    public void Tamanho_repetido_vai_uma_vez_so()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.EnviarConfiguracaoCompleta(1, Configuracao() with
        {
            QuantidadesVariaveisDeDigitos = [8, 8, 12],
            EnviarDigitosVariaveis = true,
        });

        Assert.Equal([8, 12], costura.DigitosVariaveis);
    }

    /// <summary>Tamanho recusado pela catraca impede o envio, como qualquer outro passo.</summary>
    [Fact]
    public void Tamanho_recusado_impede_o_envio()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["InserirQuantidadeDigitoVariavel"] = 1;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.EnviarConfiguracaoCompleta(1, Configuracao() with
        {
            QuantidadesVariaveisDeDigitos = [10],
            EnviarDigitosVariaveis = true,
        });

        Assert.NotEqual(AdapterStatus.Ok, resultado.Status);
        Assert.DoesNotContain("EnviarConfiguracoes", costura.Chamadas);
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
    /// Cada pedido de liberação chama exatamente uma função, sem consultar perfil nenhum.
    /// </summary>
    /// <remarks>
    /// Substitui o teste que enviava um perfil invertido ao adapter e pedia
    /// <c>GateDirection.Entrada</c>: ele só cobria o adapter e por isso não via o laço
    /// invertendo de novo (defeito F1, docs/34 §2). A escolha agora é do comissionamento, e
    /// o caminho inteiro está em <see cref="LiberacaoDePontaAPontaTests"/>.
    /// </remarks>
    [Theory]
    [InlineData(GateDirection.Entrada, "LiberarCatracaEntrada")]
    [InlineData(GateDirection.EntradaInvertida, "LiberarCatracaEntradaInvertida")]
    [InlineData(GateDirection.Saida, "LiberarCatracaSaida")]
    [InlineData(GateDirection.SaidaInvertida, "LiberarCatracaSaidaInvertida")]
    [InlineData(GateDirection.DoisSentidos, "LiberarCatracaDoisSentidos")]
    public void Cada_pedido_de_liberacao_chama_exatamente_uma_funcao(GateDirection direcao, string esperada)
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        adapter.EnviarConfiguracaoCompleta(1, Configuracao());
        costura.Chamadas.Clear();

        adapter.LiberarGiro(1, direcao);

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

    /// <summary>
    /// Retorno ≠ 0 de <c>ReceberDadosOnLine</c> não é "sem eventos": o bruto é preservado com
    /// o seu status e contado (defeito F6, docs/34 §2; ADR-0018).
    /// </summary>
    [Theory]
    [InlineData(1, AdapterStatus.Erro)]
    [InlineData(8, AdapterStatus.FalhaDeDependencia)]
    [InlineData(200, AdapterStatus.RetornoDesconhecido)]
    public void Retorno_diferente_de_zero_na_recepcao_nao_e_silencio(byte retorno, AdapterStatus esperado)
    {
        var costura = new CosturaFalsa
        {
            // Nem uma origem preenchida faz um retorno de erro virar evento.
            OrigemADevolver = (byte)KnownEventOrigin.QrCode,
            CartaoADevolver = "0000000101",
        };
        costura.Retornos["ReceberDadosOnLine"] = retorno;
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, evento) = adapter.AguardarEvento(1, TimeSpan.FromSeconds(1));
        adapter.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.Equal(esperado, resultado.Status);
        Assert.NotEqual(AdapterStatus.SemEventos, resultado.Status);
        Assert.Equal(retorno, resultado.NativeReturn);
        Assert.Equal("ReceberDadosOnLine", resultado.Funcao);
        Assert.Null(evento);
        Assert.Equal(2, adapter.ErrosDeRecepcao);
    }

    /// <summary>A hipótese documentada continua: retorno 0 com origem 0 é ausência de evento, e não conta.</summary>
    [Fact]
    public void Retorno_zero_com_origem_zero_continua_sendo_sem_eventos_e_nao_conta()
    {
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);

        var (resultado, _) = adapter.AguardarEvento(1, TimeSpan.FromSeconds(1));

        Assert.Equal(AdapterStatus.SemEventos, resultado.Status);
        Assert.Equal(0, adapter.ErrosDeRecepcao);
    }

    /// <summary>129 em <c>ConfigurarLeitor2</c> é recusa daquele passo, dita com o nome da função.</summary>
    [Fact]
    public void Passo_recusado_diz_a_funcao_e_o_motivo()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["ConfigurarLeitor2"] = 129;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.EnviarConfiguracaoCompleta(1, Configuracao());

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Equal("ConfigurarLeitor2", resultado.Funcao);
        Assert.Equal(129, resultado.NativeReturn);
        Assert.StartsWith("configuração recusada", resultado.Significado, StringComparison.Ordinal);
        Assert.DoesNotContain("EnviarConfiguracoes", costura.Chamadas);
    }

    /// <summary>O envio também diz de onde veio o retorno.</summary>
    [Fact]
    public void Retorno_do_envio_leva_o_nome_do_envio()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["EnviarConfiguracoes"] = 1;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.EnviarConfiguracaoCompleta(1, Configuracao());

        Assert.Equal(AdapterStatus.Erro, resultado.Status);
        Assert.Equal("EnviarConfiguracoes", resultado.Funcao);
    }

    [Fact]
    public void Tipo_de_conexao_invalido_e_configuracao_recusada()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["DefinirTipoConexao"] = 9;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.AbrirPorta(3570);

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Equal("DefinirTipoConexao", resultado.Funcao);
    }

    /// <summary>Retorno 3: a porta já estava aberta, em geral por um worker anterior que não a fechou.</summary>
    [Fact]
    public void Porta_ja_aberta_e_reconhecida()
    {
        var costura = new CosturaFalsa();
        costura.Retornos["AbrirPortaComunicacao"] = 3;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.AbrirPorta(3570);

        Assert.Equal(AdapterStatus.PortaJaAberta, resultado.Status);
        Assert.Equal("porta já aberta", resultado.Significado);
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
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 19, 30, 45, TimeSpan.FromHours(-3)), evento.DeviceTime);
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
