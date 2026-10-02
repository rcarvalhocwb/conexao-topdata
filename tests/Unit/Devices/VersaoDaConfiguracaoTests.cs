using System.Reflection;
using Access.Application.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// A versão canônica da configuração (Etapa A.5 do docs/35): estável, sensível a tudo o que vai
/// para a catraca, cega ao que não vai, e sem o número do cartão master.
/// </summary>
/// <remarks>Dados sintéticos; o "master" é o <c>0000000101</c> das regras de teste.</remarks>
public sealed class VersaoDaConfiguracaoTests
{
    private const string MasterSintetico = "0000000101";

    /// <summary>
    /// A forma canônica do padrão de fábrica, escrita à mão. Mudar o formato é decisão (muda
    /// todas as versões publicadas): este teste existe para que ela não aconteça sem querer.
    /// </summary>
    private const string CanonicaDoPadrao =
        "xacess.configuracao-da-catraca.v1\n" +
        "padrao_cartao=1\n" +
        "quantidade_fixa_de_digitos=-\n" +
        "digitos_variaveis=-\n" +
        "tipo_de_leitor=8\n" +
        "leitor1=1\n" +
        "leitor2=1\n" +
        "acionamento1=2,5\n" +
        "acionamento2=0,0\n" +
        "regime=online\n" +
        "teclado=0,0\n" +
        "mudanca_automatica=0,10\n" +
        "wiegand_dois_leitores=-\n" +
        "registrar_acesso_negado=-\n" +
        "data_hora_no_evento_online=-\n" +
        "cartao_master=-\n" +
        "tipo_de_lista=-\n" +
        "formas_de_entrada_online=0,0,7,0,0\n" +
        "mensagem_padrao=19:Aproxime o ingresso\n" +
        "liberacao_da_entrada=Entrada\n";

    /// <summary>
    /// SHA-256 de <see cref="CanonicaDoPadrao"/>, calculado fora do .NET
    /// (<c>printf '…' | sha256sum</c>): a mesma versão em qualquer execução e em qualquer máquina.
    /// </summary>
    private const string VersaoDoPadrao = "1688bcc8e30188e56d1366cef4c28781d8a777e98531f7232fdc40ff28f08402";

    /// <summary>Todas as chaves técnicas ligadas e cada valor fora do padrão: tudo é "efetivo".</summary>
    private static DeviceConfiguration TudoLigado() => PadroesDeFabrica.TopFit4 with
    {
        EnviarDigitosVariaveis = true,
        EnviarDataHoraNoEventoOnLine = true,
        RegistrarAcessoNegado = 1,
        EnviarTipoDeLista = true,
        WiegandDoisLeitores = new WiegandDoisLeitores(false, true),
        EnviarWiegandDoisLeitores = true,
        FormasDeEntradaOnLine = new FormasDeEntradaOnLine(1, 0, 7, 0, 0),
        EnviarFormasDeEntradaOnLine = true,
    };

    /// <summary>Uma mudança por campo que entra na versão, a partir de <see cref="TudoLigado"/>.</summary>
    private static readonly Dictionary<string, Func<DeviceConfiguration, DeviceConfiguration>> Mudancas = new(StringComparer.Ordinal)
    {
        [nameof(DeviceConfiguration.PadraoCartao)] = c => c with { PadraoCartao = 0 },
        [nameof(DeviceConfiguration.QuantidadeFixaDeDigitos)] = c => c with { QuantidadeFixaDeDigitos = 10 },
        [nameof(DeviceConfiguration.QuantidadesVariaveisDeDigitos)] = c => c with
        {
            QuantidadesVariaveisDeDigitos = [.. Enumerable.Range(4, 12).Select(n => (byte)n)],
        },
        [nameof(DeviceConfiguration.EnviarDigitosVariaveis)] = c => c with { EnviarDigitosVariaveis = false },
        [nameof(DeviceConfiguration.TipoDeLeitor)] = c => c with { TipoDeLeitor = 5 },
        [nameof(DeviceConfiguration.OperacaoDoLeitor1)] = c => c with { OperacaoDoLeitor1 = 2 },
        [nameof(DeviceConfiguration.OperacaoDoLeitor2)] = c => c with { OperacaoDoLeitor2 = 0 },
        [nameof(DeviceConfiguration.FuncaoDoAcionamento1)] = c => c with { FuncaoDoAcionamento1 = 3 },
        [nameof(DeviceConfiguration.TempoDoAcionamento1)] = c => c with { TempoDoAcionamento1 = 6 },
        [nameof(DeviceConfiguration.FuncaoDoAcionamento2)] = c => c with { FuncaoDoAcionamento2 = 3 },
        [nameof(DeviceConfiguration.TempoDoAcionamento2)] = c => c with { TempoDoAcionamento2 = 5 },
        [nameof(DeviceConfiguration.Online)] = c => c with { Online = false },
        [nameof(DeviceConfiguration.TecladoHabilitado)] = c => c with { TecladoHabilitado = true },
        [nameof(DeviceConfiguration.EcoDoTeclado)] = c => c with { EcoDoTeclado = 1 },
        [nameof(DeviceConfiguration.MudancaAutomatica)] = c => c with { MudancaAutomatica = 1 },
        [nameof(DeviceConfiguration.TempoDaMudancaAutomatica)] = c => c with { TempoDaMudancaAutomatica = 11 },
        [nameof(DeviceConfiguration.MensagemPadrao)] = c => c with { MensagemPadrao = "Aproxime o ingresso." },
        [nameof(DeviceConfiguration.PerfilFisico)] = c => c with { PerfilFisico = new GatePhysicalProfile(FuncaoDeLiberacao.EntradaInvertida) },
        [nameof(DeviceConfiguration.DataHoraNoEventoOnLine)] = c => c with { DataHoraNoEventoOnLine = false },
        [nameof(DeviceConfiguration.EnviarDataHoraNoEventoOnLine)] = c => c with { EnviarDataHoraNoEventoOnLine = false },
        [nameof(DeviceConfiguration.RegistrarAcessoNegado)] = c => c with { RegistrarAcessoNegado = 2 },
        [nameof(DeviceConfiguration.TipoDeLista)] = c => c with { TipoDeLista = 1 },
        [nameof(DeviceConfiguration.EnviarTipoDeLista)] = c => c with { EnviarTipoDeLista = false },
        [nameof(DeviceConfiguration.WiegandDoisLeitores)] = c => c with { WiegandDoisLeitores = new WiegandDoisLeitores(true, true) },
        [nameof(DeviceConfiguration.EnviarWiegandDoisLeitores)] = c => c with { EnviarWiegandDoisLeitores = false },
        [nameof(DeviceConfiguration.FormasDeEntradaOnLine)] = c => c with { FormasDeEntradaOnLine = new FormasDeEntradaOnLine(1, 0, 10, 0, 0) },
        [nameof(DeviceConfiguration.EnviarFormasDeEntradaOnLine)] = c => c with { EnviarFormasDeEntradaOnLine = false },
        [nameof(DeviceConfiguration.CartaoMaster)] = c => c with { CartaoMaster = new CodigoDoCartaoMaster(MasterSintetico) },
    };

    private static IEnumerable<string> Propriedades() =>
        typeof(DeviceConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

    [Fact]
    public void A_forma_canonica_do_padrao_e_a_escrita_a_mao()
    {
        Assert.Equal(CanonicaDoPadrao, VersaoDaConfiguracao.FormaCanonica(PadroesDeFabrica.TopFit4));
    }

    [Fact]
    public void A_versao_do_padrao_e_o_sha256_calculado_fora_do_dotnet()
    {
        var versao = VersaoDaConfiguracao.Calcular(PadroesDeFabrica.TopFit4);

        Assert.Equal(VersaoDoPadrao, versao);
        Assert.Equal(VersaoDaConfiguracao.Comprimento, versao.Length);
        Assert.Matches("^[0-9a-f]{64}$", versao);
    }

    /// <summary>ADR-0020 item 3, para a versão: nenhum campo novo escapa sem uma decisão.</summary>
    [Fact]
    public void Todo_campo_da_configuracao_esta_na_versao_ou_fora_dela_com_motivo()
    {
        var dentro = VersaoDaConfiguracao.CamposNaVersao;
        var fora = VersaoDaConfiguracao.CamposForaDaVersao;

        Assert.Empty(dentro.Intersect(fora.Keys));
        Assert.Equal(Propriedades().Order(StringComparer.Ordinal), dentro.Concat(fora.Keys).Order(StringComparer.Ordinal));
        Assert.All(fora, f => Assert.False(string.IsNullOrWhiteSpace(f.Value), f.Key));
    }

    [Fact]
    public void Cada_campo_que_entra_na_versao_tem_uma_mudanca_neste_teste()
    {
        Assert.Equal(
            VersaoDaConfiguracao.CamposNaVersao.Order(StringComparer.Ordinal),
            Mudancas.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>Configuração diferente no que vai para a catraca = versão diferente, campo a campo.</summary>
    [Fact]
    public void Mudar_qualquer_campo_enviado_muda_a_versao_e_nenhuma_mudanca_colide_com_outra()
    {
        var base_ = TudoLigado();
        var versoes = new Dictionary<string, string>(StringComparer.Ordinal) { ["(base)"] = VersaoDaConfiguracao.Calcular(base_) };

        foreach (var (campo, mudar) in Mudancas)
        {
            var versao = VersaoDaConfiguracao.Calcular(mudar(base_));
            var repetida = versoes.FirstOrDefault(v => v.Value == versao).Key;
            Assert.True(repetida is null, $"{campo} deu a mesma versão que {repetida}");
            versoes[campo] = versao;
        }
    }

    [Fact]
    public void O_que_nao_e_enviado_nao_muda_a_versao()
    {
        var base_ = PadroesDeFabrica.TopFit4;
        var versao = VersaoDaConfiguracao.Calcular(base_);

        var mudada = base_ with
        {
            ModoDeDigitos = ModoDeDigitos.Variavel,
            WebServerDesabilitado = true,
            MensagemDeApresentacaoDaEntrada = new MensagemDoDisplay("Bem-vindo"),
            MensagemDeApresentacaoDaSaida = new MensagemDoDisplay("Volte sempre"),
            MensagemPadraoOffLine = new MensagemDoDisplay("Sem sistema"),
            MensagemDeEntradaOffLine = new MensagemDoDisplay("Entrada"),
            MensagemDeSaidaOffLine = new MensagemDoDisplay("Saida"),
        };

        Assert.Equal(versao, VersaoDaConfiguracao.Calcular(mudada));
    }

    /// <summary>
    /// Atrás de chave desligada, o valor não chega à catraca: mudá-lo não muda a versão; ligar a
    /// chave muda.
    /// </summary>
    [Fact]
    public void Valor_atras_de_chave_desligada_nao_muda_a_versao_e_ligar_a_chave_muda()
    {
        var desligado = PadroesDeFabrica.TopFit4;
        var versao = VersaoDaConfiguracao.Calcular(desligado);

        var valoresMudados = desligado with
        {
            QuantidadesVariaveisDeDigitos = [10],
            DataHoraNoEventoOnLine = false,
            TipoDeLista = 1,
            WiegandDoisLeitores = new WiegandDoisLeitores(true, true),
            FormasDeEntradaOnLine = new FormasDeEntradaOnLine(1, 1, 10, 1, 1),
        };
        Assert.Equal(versao, VersaoDaConfiguracao.Calcular(valoresMudados));

        Assert.NotEqual(versao, VersaoDaConfiguracao.Calcular(desligado with { EnviarDigitosVariaveis = true }));
        Assert.NotEqual(versao, VersaoDaConfiguracao.Calcular(desligado with { EnviarDataHoraNoEventoOnLine = true }));
        Assert.NotEqual(versao, VersaoDaConfiguracao.Calcular(desligado with { EnviarTipoDeLista = true }));
        Assert.NotEqual(versao, VersaoDaConfiguracao.Calcular(desligado with { EnviarWiegandDoisLeitores = true }));
        Assert.NotEqual(versao, VersaoDaConfiguracao.Calcular(desligado with { RegistrarAcessoNegado = 0 }));
    }

    /// <summary>
    /// Com a chave das formas de entrada ligada e os valores de sempre, a catraca recebe o mesmo
    /// rearme de antes: a versão é a mesma.
    /// </summary>
    [Fact]
    public void Formas_de_entrada_entram_pelo_valor_efetivo()
    {
        var desligada = PadroesDeFabrica.TopFit4;
        var ligadaComOsDeSempre = desligada with { EnviarFormasDeEntradaOnLine = true, FormasDeEntradaOnLine = FormasDeEntradaOnLine.DeHoje };

        Assert.Equal(VersaoDaConfiguracao.Calcular(desligada), VersaoDaConfiguracao.Calcular(ligadaComOsDeSempre));
    }

    /// <summary>
    /// Mesma configuração = mesma versão, por qualquer caminho de montagem: inicializador em
    /// outra ordem, listas novas, <c>with</c> em outra ordem, o montador.
    /// </summary>
    [Fact]
    public void A_mesma_configuracao_montada_de_jeitos_diferentes_tem_a_mesma_versao()
    {
        var padrao = PadroesDeFabrica.TopFit4 with { EnviarDigitosVariaveis = true };

        var outraOrdem = new DeviceConfiguration
        {
            PerfilFisico = new GatePhysicalProfile(FuncaoDeLiberacao.Entrada),
            MensagemPadrao = new string("Aproxime o ingresso".ToCharArray()),
            TempoDaMudancaAutomatica = 10,
            MudancaAutomatica = 0,
            EcoDoTeclado = 0,
            TecladoHabilitado = false,
            Online = true,
            TempoDoAcionamento2 = 0,
            FuncaoDoAcionamento2 = 0,
            TempoDoAcionamento1 = 5,
            FuncaoDoAcionamento1 = 2,
            OperacaoDoLeitor2 = 1,
            OperacaoDoLeitor1 = 1,
            TipoDeLeitor = 8,
            EnviarDigitosVariaveis = true,
            QuantidadesVariaveisDeDigitos = new List<byte> { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            PadraoCartao = 1,
        };

        // A igualdade do record diz "diferente" (a lista é outra instância); a versão não.
        Assert.NotEqual(padrao, outraOrdem);
        Assert.Equal(VersaoDaConfiguracao.Calcular(padrao), VersaoDaConfiguracao.Calcular(outraOrdem));

        var umaOrdem = PadroesDeFabrica.TopFit4 with { TipoDeLeitor = 5 } with { TempoDoAcionamento1 = 7 };
        var outra = PadroesDeFabrica.TopFit4 with { TempoDoAcionamento1 = 7 } with { TipoDeLeitor = 5 };
        Assert.Equal(VersaoDaConfiguracao.Calcular(umaOrdem), VersaoDaConfiguracao.Calcular(outra));

        var montada = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma);
        Assert.Equal(VersaoDaConfiguracao.Calcular(PadroesDeFabrica.TopFit4), VersaoDaConfiguracao.Calcular(montada));
    }

    /// <summary>O adapter manda cada tamanho uma vez (Distinct): repetir na lista não muda o envio.</summary>
    [Fact]
    public void Tamanho_repetido_na_lista_e_o_mesmo_envio()
    {
        var semRepetir = PadroesDeFabrica.TopFit4 with { EnviarDigitosVariaveis = true, QuantidadesVariaveisDeDigitos = [4, 10] };
        var repetindo = semRepetir with { QuantidadesVariaveisDeDigitos = [4, 4, 10, 4] };

        Assert.Equal(VersaoDaConfiguracao.Calcular(semRepetir), VersaoDaConfiguracao.Calcular(repetindo));
    }

    /// <summary>
    /// Se a ordem das chamadas de <c>InserirQuantidadeDigitoVariavel</c> importa não está
    /// documentado (T30): na dúvida, a versão diz "diferente", nunca "igual" sobre outro envio.
    /// </summary>
    [Fact]
    public void Tamanhos_em_outra_ordem_sao_outro_envio()
    {
        var crescente = PadroesDeFabrica.TopFit4 with { EnviarDigitosVariaveis = true, QuantidadesVariaveisDeDigitos = [4, 10] };
        var decrescente = crescente with { QuantidadesVariaveisDeDigitos = [10, 4] };

        Assert.NotEqual(VersaoDaConfiguracao.Calcular(crescente), VersaoDaConfiguracao.Calcular(decrescente));
    }

    /// <summary>
    /// O número do master nunca entra: nem na forma canônica, nem de modo que a versão permita
    /// adivinhá-lo (dois masters diferentes dão a mesma versão). Só a existência entra.
    /// </summary>
    [Fact]
    public void O_numero_do_cartao_master_fica_fora_so_a_existencia_entra()
    {
        var padrao = PadroesDeFabrica.TopFit4 with { PadraoCartao = 1 };
        var comUm = padrao with { CartaoMaster = new CodigoDoCartaoMaster(MasterSintetico) };
        var comOutro = padrao with { CartaoMaster = new CodigoDoCartaoMaster("99999999999999") };

        Assert.DoesNotContain(MasterSintetico, VersaoDaConfiguracao.FormaCanonica(comUm), StringComparison.Ordinal);
        Assert.Contains("cartao_master=definido\n", VersaoDaConfiguracao.FormaCanonica(comUm), StringComparison.Ordinal);
        Assert.Equal(VersaoDaConfiguracao.Calcular(comUm), VersaoDaConfiguracao.Calcular(comOutro));
        Assert.NotEqual(VersaoDaConfiguracao.Calcular(padrao), VersaoDaConfiguracao.Calcular(comUm));
    }

    /// <summary>
    /// Texto com os separadores da forma canônica não forja outro campo: o comprimento vai na
    /// frente.
    /// </summary>
    [Fact]
    public void Mensagem_com_separadores_nao_se_confunde_com_outro_campo()
    {
        var saida = PadroesDeFabrica.TopFit4 with
        {
            MensagemPadrao = "A",
            PerfilFisico = new GatePhysicalProfile(FuncaoDeLiberacao.Saida),
        };
        var forjada = PadroesDeFabrica.TopFit4 with { MensagemPadrao = "A\nliberacao_da_entrada=Saida" };

        Assert.NotEqual(VersaoDaConfiguracao.Calcular(saida), VersaoDaConfiguracao.Calcular(forjada));
        Assert.Contains("mensagem_padrao=28:A\nliberacao_da_entrada=Saida\nliberacao_da_entrada=Entrada\n",
            VersaoDaConfiguracao.FormaCanonica(forjada), StringComparison.Ordinal);
    }

    [Fact]
    public void Calcular_nao_aceita_nulo() =>
        Assert.Throws<ArgumentNullException>(() => VersaoDaConfiguracao.Calcular(null!));
}
