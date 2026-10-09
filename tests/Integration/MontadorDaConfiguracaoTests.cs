using System.Collections;
using System.Reflection;
using Access.Application.Devices;
using Access.Infrastructure.SQLite;
using Edge.Worker.Bancada;

namespace Integration.Tests;

/// <summary>
/// O montador da Etapa A.1 (docs/35) dá, campo a campo, o mesmo que o código de antes.
/// </summary>
/// <remarks>
/// <para>
/// "Antes" está copiado aqui, literal, do commit <c>8c524ef</c> (topo da Etapa 0):
/// <c>ConfiguracaoDeBancada.TopFit4</c> (<c>src/Edge.Worker/Bancada/SessaoDeBancada.cs</c>) e
/// <c>ConfiguracaoDasCatracas</c> (<c>src/Edge.Worker.X86/Program.cs</c>). A cópia é
/// congelada de propósito: se o montador mudar um valor, este teste reprova, e a mudança
/// precisa ser uma decisão (Etapa A.2 em diante), não um efeito colateral.
/// </para>
/// <para>
/// A comparação é por reflexão sobre <b>todas</b> as propriedades públicas da
/// <see cref="DeviceConfiguration"/>: campo novo entra na comparação sozinho. Listas são
/// comparadas pelo conteúdo, porque a igualdade do record compara a referência.
/// </para>
/// </remarks>
public sealed class MontadorDaConfiguracaoTests
{
    /// <summary><c>ConfiguracaoDeBancada.TopFit4</c> como era no commit 8c524ef.</summary>
    private static DeviceConfiguration TopFit4DeAntes(byte tipoDeLeitor = 8, bool leitorDaUrna = true) => new()
    {
        PadraoCartao = 1,
        QuantidadesVariaveisDeDigitos = [.. Enumerable.Range(4, 13).Select(n => (byte)n)],
        TipoDeLeitor = tipoDeLeitor,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = leitorDaUrna ? (byte)1 : (byte)0,
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
        PerfilFisico = new GatePhysicalProfile(FuncaoDeLiberacao.Entrada),
    };

    /// <summary><c>Program.ConfiguracaoDasCatracas</c> do worker x86 como era no commit 8c524ef.</summary>
    private static DeviceConfiguration ConfiguracaoDasCatracasDeAntes(ConfiguracaoDaOperacao configuracao) =>
        TopFit4DeAntes(configuracao.TipoDeLeitor, configuracao.LeitorDaUrna) with
        {
            TempoDoAcionamento1 = configuracao.TempoDeAcionamento,
            MensagemPadrao = configuracao.MensagemPadrao,
            EnviarDigitosVariaveis = configuracao.EnviarDigitosVariaveis,
        };

    /// <summary>O que o worker x86 chama agora, em <c>Program.ConfiguracaoDasCatracas</c>.</summary>
    private static DeviceConfiguration ConfiguracaoDasCatracasAgora(ConfiguracaoDaOperacao configuracao) =>
        MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, configuracao.ParaACatraca(), SobreposicoesDaCatraca.Nenhuma);

    /// <summary>Configurações do evento com dados sintéticos, uma por caso.</summary>
    private static ConfiguracaoDaOperacao Evento(string caso) => caso switch
    {
        "padrao" => new ConfiguracaoDaOperacao(),
        "leitor-5" => new ConfiguracaoDaOperacao(TipoDeLeitor: 5),
        "leitor-8" => new ConfiguracaoDaOperacao(TipoDeLeitor: 8),
        "urna-ligada" => new ConfiguracaoDaOperacao(TipoDeLeitor: 5, LeitorDaUrna: true),
        "urna-desligada" => new ConfiguracaoDaOperacao(LeitorDaUrna: false),
        "tempo" => new ConfiguracaoDaOperacao(TempoDeAcionamento: 12),
        "tempo-minimo" => new ConfiguracaoDaOperacao(TempoDeAcionamento: 1),
        "tempo-maximo" => new ConfiguracaoDaOperacao(TempoDeAcionamento: 50),
        "mensagem" => new ConfiguracaoDaOperacao(MensagemPadrao: "Pista sintetica 9"),
        "mensagem-32" => new ConfiguracaoDaOperacao(MensagemPadrao: new string('X', 32)),
        "digitos-variaveis" => new ConfiguracaoDaOperacao(EnviarDigitosVariaveis: true),
        "acertar-relogio" => new ConfiguracaoDaOperacao(AcertarRelogioAoDivergir: true),
        "reconectar-e-nuvem" => new ConfiguracaoDaOperacao(
            ConectorDoEspelho: "conector-sintetico", EsperaPeloGiroSegundos: 30, ReconectarEmErroDeRecepcao: true),
        "tudo-mudado" => new ConfiguracaoDaOperacao(
            TipoDeLeitor: 5,
            LeitorDaUrna: false,
            TempoDeAcionamento: 20,
            MensagemPadrao: "Entrada sintetica B",
            ConectorDoEspelho: "conector-sintetico",
            EsperaPeloGiroSegundos: 25,
            AcertarRelogioAoDivergir: true,
            EnviarDigitosVariaveis: true,
            ReconectarEmErroDeRecepcao: true),
        _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "caso desconhecido"),
    };

    /// <summary>
    /// Compara todas as propriedades públicas, uma a uma, e diz qual divergiu.
    /// </summary>
    private static void CampoACampo(DeviceConfiguration esperado, DeviceConfiguration obtido)
    {
        var propriedades = typeof(DeviceConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(propriedades);

        foreach (var propriedade in propriedades)
        {
            var a = propriedade.GetValue(esperado);
            var b = propriedade.GetValue(obtido);

            if (a is IEnumerable listaA and not string && b is IEnumerable listaB and not string)
            {
                Assert.True(
                    listaA.Cast<object>().SequenceEqual(listaB.Cast<object>()),
                    $"{propriedade.Name}: [{string.Join(",", listaA.Cast<object>())}] != [{string.Join(",", listaB.Cast<object>())}]");
            }
            else
            {
                Assert.True(Equals(a, b), $"{propriedade.Name}: {a} != {b}");
            }
        }
    }

    [Theory]
    [InlineData("padrao")]
    [InlineData("leitor-5")]
    [InlineData("leitor-8")]
    [InlineData("urna-ligada")]
    [InlineData("urna-desligada")]
    [InlineData("tempo")]
    [InlineData("tempo-minimo")]
    [InlineData("tempo-maximo")]
    [InlineData("mensagem")]
    [InlineData("mensagem-32")]
    [InlineData("digitos-variaveis")]
    [InlineData("acertar-relogio")]
    [InlineData("reconectar-e-nuvem")]
    [InlineData("tudo-mudado")]
    public void Montador_da_o_mesmo_que_o_worker_montava_antes(string caso)
    {
        var evento = Evento(caso);

        CampoACampo(ConfiguracaoDasCatracasDeAntes(evento), ConfiguracaoDasCatracasAgora(evento));
    }

    [Theory]
    [InlineData((byte)8, true)]
    [InlineData((byte)8, false)]
    [InlineData((byte)5, true)]
    [InlineData((byte)5, false)]
    public void Fachada_da_bancada_da_o_mesmo_que_antes(byte tipoDeLeitor, bool leitorDaUrna)
    {
        CampoACampo(TopFit4DeAntes(tipoDeLeitor, leitorDaUrna), ConfiguracaoDeBancada.TopFit4(tipoDeLeitor, leitorDaUrna));
    }

    [Fact]
    public void Fachada_da_bancada_sem_argumentos_da_o_mesmo_que_antes()
    {
        CampoACampo(TopFit4DeAntes(), ConfiguracaoDeBancada.TopFit4());
    }

    /// <summary>Sem sobreposição nenhuma, sai o padrão de fábrica inteiro.</summary>
    [Fact]
    public void Sem_sobreposicoes_sai_o_padrao_de_fabrica()
    {
        var montada = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4, SobreposicoesDoEvento.Nenhuma, SobreposicoesDaCatraca.Nenhuma);

        CampoACampo(TopFit4DeAntes(), montada);
        CampoACampo(PadroesDeFabrica.TopFit4, montada);
    }

    /// <summary>
    /// O caminho do "Aplicar agora": gravar na base, reler e montar dá o mesmo que antes.
    /// </summary>
    [Fact]
    public void Configuracao_relida_da_base_monta_o_mesmo_que_antes()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var borda = new ConfiguracoesDaBorda(banco.Fabrica);

        borda.Gravar(Evento("tudo-mudado"), new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), "teste");
        var (relida, ilegiveis) = borda.Ler();

        Assert.Empty(ilegiveis);
        CampoACampo(ConfiguracaoDasCatracasDeAntes(relida), ConfiguracaoDasCatracasAgora(relida));
        Assert.Equal(5, ConfiguracaoDasCatracasAgora(relida).TipoDeLeitor);
    }

    /// <summary>
    /// Função pura: mesma entrada, mesma saída, e ninguém divide a lista de tamanhos com o
    /// padrão (a próxima montagem não herda o que uma anterior mudou).
    /// </summary>
    [Fact]
    public void Montar_e_puro_e_nao_divide_lista_com_o_padrao()
    {
        var evento = Evento("tudo-mudado").ParaACatraca();

        var primeira = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento, SobreposicoesDaCatraca.Nenhuma);
        var segunda = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento, SobreposicoesDaCatraca.Nenhuma);

        CampoACampo(primeira, segunda);
        Assert.NotSame(PadroesDeFabrica.TopFit4, PadroesDeFabrica.TopFit4);
        Assert.NotSame(primeira.QuantidadesVariaveisDeDigitos, segunda.QuantidadesVariaveisDeDigitos);
    }

    /// <summary>
    /// A camada do evento leva só o que vai para a catraca: nuvem, espera pelo giro e as chaves
    /// do relógio e da reconexão não mudam a configuração enviada.
    /// </summary>
    [Fact]
    public void O_que_nao_e_da_catraca_nao_muda_a_configuracao()
    {
        var padrao = ConfiguracaoDasCatracasAgora(new ConfiguracaoDaOperacao());

        CampoACampo(padrao, ConfiguracaoDasCatracasAgora(Evento("acertar-relogio")));
        CampoACampo(padrao, ConfiguracaoDasCatracasAgora(Evento("reconectar-e-nuvem")));
    }

    /// <summary>
    /// Nenhuma configuração do evento que passa em <c>ConfiguracaoDaOperacao.Validar</c> monta
    /// uma <see cref="DeviceConfiguration"/> que o adapter recusaria.
    /// </summary>
    [Theory]
    [InlineData("padrao")]
    [InlineData("leitor-5")]
    [InlineData("urna-desligada")]
    [InlineData("tempo-maximo")]
    [InlineData("mensagem-32")]
    [InlineData("digitos-variaveis")]
    [InlineData("tudo-mudado")]
    public void Evento_valido_monta_configuracao_valida(string caso)
    {
        var evento = Evento(caso);
        Assert.Empty(evento.Validar());

        Assert.Empty(ConfiguracaoDasCatracasAgora(evento).Validar());
    }
}
