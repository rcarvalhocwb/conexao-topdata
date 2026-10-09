using Access.Application.Devices;
using Topdata.EasyInner.Adapter;
using Topdata.EasyInner.Interop;

namespace HardwareInLoop.Tests;

/// <summary>
/// As chaves técnicas da Etapa A.2 (docs/35) no adapter: desligadas, nada novo chega à DLL;
/// ligadas, a chamada exata aparece no lugar certo da montagem.
/// </summary>
/// <remarks>
/// "Lugar certo" é depois das funções de montagem de sempre e antes de
/// <c>EnviarConfiguracoes</c>, porque o buffer é global da DLL e só vale com o envio
/// (ADR-0006; FUN:31). As formas de entrada vão no rearme do leitor, fora da montagem.
/// Dados sintéticos.
/// </remarks>
public sealed class ChavesDaConfiguracaoTests
{
    private const int Inner = 1;

    private const string MasterSintetico = "99990000000101";

    /// <summary>A mesma configuração de <c>AdapterTests</c>, com os campos novos nos padrões.</summary>
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

    /// <summary>A montagem de antes da Etapa A.2, com a quantidade fixa preenchida.</summary>
    private static readonly string[] MontagemDeSempre =
    [
        nameof(IEasyInnerNative.DefinirPadraoCartao),
        nameof(IEasyInnerNative.DefinirQuantidadeDigitosCartao),
        nameof(IEasyInnerNative.ConfigurarTipoLeitor),
        nameof(IEasyInnerNative.ConfigurarLeitor1),
        nameof(IEasyInnerNative.ConfigurarLeitor2),
        nameof(IEasyInnerNative.ConfigurarAcionamento1),
        nameof(IEasyInnerNative.ConfigurarAcionamento2),
        nameof(IEasyInnerNative.ConfigurarInnerOnLine),
        nameof(IEasyInnerNative.HabilitarTeclado),
        nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine),
    ];

    /// <summary>Uma chave: como ligá-la e a chamada exata que ela provoca.</summary>
    private sealed record Chave(
        string Nome,
        Func<DeviceConfiguration, DeviceConfiguration> ValoresNovos,
        Func<DeviceConfiguration, DeviceConfiguration> Ligar,
        string Funcao,
        object[] Argumentos);

    private static readonly Dictionary<string, Chave> Chaves = new(StringComparer.Ordinal)
    {
        ["catraca.enviar_wiegand_dois_leitores"] = new(
            "catraca.enviar_wiegand_dois_leitores",
            c => c with { WiegandDoisLeitores = new WiegandDoisLeitores(true, false) },
            c => c with { EnviarWiegandDoisLeitores = true },
            nameof(IEasyInnerNative.ConfigurarWiegandDoisLeitores), [(byte)1, (byte)0]),

        ["catraca.registrar_acesso_negado"] = new(
            "catraca.registrar_acesso_negado",
            c => c,
            c => c with { RegistrarAcessoNegado = 3 },
            nameof(IEasyInnerNative.RegistrarAcessoNegado), [(byte)3]),

        ["catraca.enviar_data_hora_no_evento"] = new(
            "catraca.enviar_data_hora_no_evento",
            c => c,
            c => c with { EnviarDataHoraNoEventoOnLine = true },
            nameof(IEasyInnerNative.ReceberDataHoraDadosOnLine), [(byte)1]),

        ["catraca.enviar_tipo_de_lista"] = new(
            "catraca.enviar_tipo_de_lista",
            c => c,
            c => c with { EnviarTipoDeLista = true },
            nameof(IEasyInnerNative.DefinirTipoListaAcesso), [(byte)0]),
    };

    public static TheoryData<string> NomesDasChaves()
    {
        var dados = new TheoryData<string>();
        foreach (var nome in Chaves.Keys)
        {
            dados.Add(nome);
        }

        return dados;
    }

    private static CosturaFalsa Enviar(DeviceConfiguration configuracao)
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);
        var resultado = adapter.EnviarConfiguracaoCompleta(Inner, configuracao);
        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        return costura;
    }

    /// <summary>
    /// Desligada, mesmo com o valor mudado, a montagem é a de sempre: a função nem é chamada.
    /// </summary>
    [Theory]
    [MemberData(nameof(NomesDasChaves))]
    public void Chave_desligada_nao_envia_e_a_sequencia_e_a_de_sempre(string nome)
    {
        var chave = Chaves[nome];
        var costura = Enviar(chave.ValoresNovos(Configuracao()));

        Assert.Equal([.. MontagemDeSempre, nameof(IEasyInnerNative.EnviarConfiguracoes)], costura.Chamadas);
        Assert.DoesNotContain(chave.Funcao, costura.Chamadas);
    }

    /// <summary>
    /// Ligada, a chamada exata (função e argumentos) aparece uma vez, depois da montagem de
    /// sempre e antes de <c>EnviarConfiguracoes</c>, e o resto da sequência não muda.
    /// </summary>
    [Theory]
    [MemberData(nameof(NomesDasChaves))]
    public void Chave_ligada_envia_a_chamada_exata_antes_do_envio(string nome)
    {
        var chave = Chaves[nome];
        var configuracao = chave.Ligar(chave.ValoresNovos(Configuracao()));
        Assert.Empty(configuracao.Validar());

        var costura = Enviar(configuracao);

        Assert.Equal(
            [.. MontagemDeSempre, chave.Funcao, nameof(IEasyInnerNative.EnviarConfiguracoes)],
            costura.Chamadas);
        var chamada = Assert.Single(costura.ChamadasComArgumentos, c => c.Funcao == chave.Funcao);
        Assert.Equal(chave.Argumentos, chamada.Argumentos);
    }

    /// <summary>
    /// O cartão master não tem chave em <c>edge_setting</c> (o número não pode morar lá): vai
    /// quando existe, e na Etapa A.2 nunca existe fora do teste.
    /// </summary>
    [Fact]
    public void Cartao_master_vai_so_quando_existe_e_nunca_aparece_no_resultado()
    {
        Assert.DoesNotContain(nameof(IEasyInnerNative.DefinirNumeroCartaoMaster), Enviar(Configuracao()).Chamadas);

        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);
        var resultado = adapter.EnviarConfiguracaoCompleta(
            Inner, Configuracao() with { CartaoMaster = new CodigoDoCartaoMaster(MasterSintetico) });

        Assert.Equal(AdapterStatus.Ok, resultado.Status);
        Assert.Equal(
            [.. MontagemDeSempre, nameof(IEasyInnerNative.DefinirNumeroCartaoMaster), nameof(IEasyInnerNative.EnviarConfiguracoes)],
            costura.Chamadas);
        Assert.Equal([MasterSintetico], Assert.Single(costura.ChamadasComArgumentos, c => c.Funcao == nameof(IEasyInnerNative.DefinirNumeroCartaoMaster)).Argumentos);
        Assert.DoesNotContain(MasterSintetico, resultado.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Recusado pela DLL, o master aparece como a função e o retorno — nunca o número.</summary>
    [Fact]
    public void Cartao_master_recusado_nao_mostra_o_numero_e_impede_o_envio()
    {
        var costura = new CosturaFalsa();
        costura.Retornos[nameof(IEasyInnerNative.DefinirNumeroCartaoMaster)] = 128;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.EnviarConfiguracaoCompleta(
            Inner, Configuracao() with { CartaoMaster = new CodigoDoCartaoMaster(MasterSintetico) });

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Contains("número inválido", resultado.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(MasterSintetico, resultado.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IEasyInnerNative.EnviarConfiguracoes), costura.Chamadas);
    }

    /// <summary>
    /// Todas ligadas: a ordem é a dos campos comuns do anexo 01 §3.3, e nada com Inner entra
    /// entre a primeira função de montagem e o envio (ADR-0006).
    /// </summary>
    [Fact]
    public void Todas_ligadas_seguem_a_ordem_do_anexo_e_nada_com_inner_entra_na_montagem()
    {
        var configuracao = Configuracao() with
        {
            EnviarWiegandDoisLeitores = true,
            RegistrarAcessoNegado = 1,
            EnviarDataHoraNoEventoOnLine = true,
            CartaoMaster = new CodigoDoCartaoMaster(MasterSintetico),
            EnviarTipoDeLista = true,
            EnviarFormasDeEntradaOnLine = true,
        };

        var costura = Enviar(configuracao);

        Assert.Equal(
            [
                .. MontagemDeSempre,
                nameof(IEasyInnerNative.ConfigurarWiegandDoisLeitores),
                nameof(IEasyInnerNative.RegistrarAcessoNegado),
                nameof(IEasyInnerNative.ReceberDataHoraDadosOnLine),
                nameof(IEasyInnerNative.DefinirNumeroCartaoMaster),
                nameof(IEasyInnerNative.DefinirTipoListaAcesso),
                nameof(IEasyInnerNative.EnviarConfiguracoes),
            ],
            costura.Chamadas);

        var comInner = typeof(IEasyInnerNative).GetMethods()
            .Where(m => m.GetParameters() is [{ Name: "inner" }, ..])
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(costura.Chamadas.Take(costura.Chamadas.Count - 1), comInner.Contains);
    }

    /// <summary>Uma recusa documentada de função nova é lida com a fonte dela (EI-021).</summary>
    [Fact]
    public void Registro_de_acesso_negado_recusado_diz_a_funcao_e_impede_o_envio()
    {
        var costura = new CosturaFalsa();
        costura.Retornos[nameof(IEasyInnerNative.RegistrarAcessoNegado)] = 128;
        using var adapter = new TopdataInnerAdapter(costura);

        var resultado = adapter.EnviarConfiguracaoCompleta(Inner, Configuracao() with { RegistrarAcessoNegado = 0 });

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Equal(nameof(IEasyInnerNative.RegistrarAcessoNegado), resultado.Funcao);
        Assert.DoesNotContain(nameof(IEasyInnerNative.EnviarConfiguracoes), costura.Chamadas);
    }

    /// <summary>
    /// O rearme do leitor: sem configuração ou com a chave desligada, as constantes de sempre;
    /// com a chave, os valores do modelo.
    /// </summary>
    [Fact]
    public void Formas_de_entrada_so_saem_do_modelo_com_a_chave()
    {
        var outras = new FormasDeEntradaOnLine(4, 1, 10, 5, 2);
        object[] deSempre = [Inner, (byte)0, (byte)0, (byte)7, (byte)0, (byte)0];

        object[] Rearmar(DeviceConfiguration? configuracao)
        {
            var costura = new CosturaFalsa();
            using var adapter = new TopdataInnerAdapter(costura);
            Assert.Equal(AdapterStatus.Ok, adapter.ConfigurarEntradasOnline(Inner, configuracao).Status);
            return Assert.Single(costura.ChamadasComArgumentos).Argumentos;
        }

        Assert.Equal(deSempre, Rearmar(null));
        Assert.Equal(deSempre, Rearmar(Configuracao() with { FormasDeEntradaOnLine = outras }));
        Assert.Equal(
            [Inner, (byte)4, (byte)1, (byte)10, (byte)5, (byte)2],
            Rearmar(Configuracao() with { FormasDeEntradaOnLine = outras, EnviarFormasDeEntradaOnLine = true }));
    }

    /// <summary>A validação recusa antes de qualquer chamada nativa: lista branca sem lista gravada.</summary>
    [Fact]
    public void Tipo_de_lista_invalido_e_recusado_antes_da_dll()
    {
        var costura = new CosturaFalsa();
        using var adapter = new TopdataInnerAdapter(costura);

        Assert.Throws<ArgumentException>(() =>
            adapter.EnviarConfiguracaoCompleta(Inner, Configuracao() with { TipoDeLista = 1, EnviarTipoDeLista = true }));
        Assert.Empty(costura.Chamadas);
    }
}
