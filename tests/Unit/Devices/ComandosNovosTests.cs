using Access.Application.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// Etapa A.8 do docs/35: as regras puras dos comandos novos — chave por comando, recusa pela
/// decisão D5, motivo e confirmação, validade e a função de saída pelo perfil.
/// </summary>
public sealed class ComandosNovosTests
{
    private static readonly DateTimeOffset Agora = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private static readonly TipoDeComando[] Novos =
        [TipoDeComando.BipCurto, TipoDeComando.BipLongo, TipoDeComando.LiberarSaida, TipoDeComando.LiberarDoisSentidos];

    private static readonly TipoDeComando[] DaFase4b =
    [
        TipoDeComando.AcertarRelogio,
        TipoDeComando.MensagemTemporaria,
        TipoDeComando.LiberacaoManual,
        TipoDeComando.ReiniciarConexao,
        TipoDeComando.AplicarConfiguracao,
    ];

    [Fact]
    public void Cada_comando_novo_tem_a_sua_chave_e_o_seu_ensaio()
    {
        var chaves = Novos.Select(ComandoDeCatraca.ChaveTecnica).ToList();

        Assert.All(chaves, Assert.NotNull);
        Assert.Equal(Novos.Length, chaves.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Novos, t => Assert.NotNull(ComandoDeCatraca.EnsaioQueLiga(t)));
    }

    /// <summary>Os comandos da fase 4b não ganharam chave: nada do que já funciona muda.</summary>
    [Fact]
    public void Os_comandos_de_antes_nao_tem_chave_nem_recusa()
    {
        Assert.All(DaFase4b, t =>
        {
            Assert.Null(ComandoDeCatraca.ChaveTecnica(t));
            Assert.Empty(ComandoDeCatraca.RecusasDoServico(t, chaveLigada: false));
        });
    }

    [Theory]
    [InlineData(TipoDeComando.BipCurto)]
    [InlineData(TipoDeComando.BipLongo)]
    [InlineData(TipoDeComando.LiberarSaida)]
    public void Chave_desligada_recusa_e_ligada_deixa_seguir(TipoDeComando tipo)
    {
        var recusa = Assert.Single(ComandoDeCatraca.RecusasDoServico(tipo, chaveLigada: false));
        Assert.Contains(ComandoDeCatraca.ChaveTecnica(tipo)!, recusa, StringComparison.Ordinal);
        Assert.Contains(ComandoDeCatraca.EnsaioQueLiga(tipo)!, recusa, StringComparison.Ordinal);

        Assert.Empty(ComandoDeCatraca.RecusasDoServico(tipo, chaveLigada: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dois_sentidos_e_recusado_pela_d5_com_ou_sem_a_chave(bool chaveLigada)
    {
        var recusas = ComandoDeCatraca.RecusasDoServico(TipoDeComando.LiberarDoisSentidos, chaveLigada);

        Assert.StartsWith("Aguardando decisão D5 do dono do produto", recusas[0], StringComparison.Ordinal);
        Assert.Equal(chaveLigada ? 1 : 2, recusas.Count);
    }

    [Fact]
    public void Liberar_saida_exige_motivo_e_guarda_o_motivo()
    {
        Assert.Contains(
            "A liberação de saída exige o motivo (5 a 200 caracteres).",
            ComandoDeCatraca.Criar(1, TipoDeComando.LiberarSaida, "Ana", Agora, motivo: "ok").Problemas);

        var (comando, _) = ComandoDeCatraca.Criar(1, TipoDeComando.LiberarSaida, "Ana", Agora, motivo: "Saída acompanhada");
        Assert.Equal("Saída acompanhada", comando!.Motivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EVACUAR")]
    [InlineData("EVACUAR 2")]
    [InlineData("sim")]
    public void Dois_sentidos_sem_a_confirmacao_digitada_da_catraca_certa_e_recusado(string? confirmacao)
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberarDoisSentidos, "Ana", Agora, motivo: "Evacuação do setor A", confirmacao: confirmacao);

        Assert.Null(comando);
        Assert.Contains("Liberar nos dois sentidos é só para evacuação: digite EVACUAR 1 para confirmar.", problemas);
    }

    [Theory]
    [InlineData("EVACUAR 1")]
    [InlineData("  evacuar 1 ")]
    public void Dois_sentidos_com_motivo_e_confirmacao_vira_pedido(string confirmacao)
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.LiberarDoisSentidos, "Ana", Agora, motivo: "Evacuação do setor A", confirmacao: confirmacao);

        Assert.Empty(problemas);
        Assert.Equal("Evacuação do setor A", comando!.Motivo);
    }

    [Fact]
    public void Dois_sentidos_exige_o_motivo_da_evacuacao()
    {
        Assert.Contains(
            "Liberar nos dois sentidos exige o motivo da evacuação (5 a 200 caracteres).",
            ComandoDeCatraca.Criar(1, TipoDeComando.LiberarDoisSentidos, "Ana", Agora, confirmacao: "EVACUAR 1").Problemas);
    }

    /// <summary>Liberações valem 15 s, como a manual; o bip, 60 s, como a mensagem.</summary>
    [Theory]
    [InlineData(TipoDeComando.BipCurto, 60)]
    [InlineData(TipoDeComando.BipLongo, 60)]
    [InlineData(TipoDeComando.LiberarSaida, 15)]
    [InlineData(TipoDeComando.LiberarDoisSentidos, 15)]
    [InlineData(TipoDeComando.LiberacaoManual, 15)]
    [InlineData(TipoDeComando.MensagemTemporaria, 60)]
    public void Validade_de_cada_tipo(TipoDeComando tipo, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), ComandoDeCatraca.ValidadePara(tipo));

    /// <summary>O bip não leva motivo nem texto: é só o Inner.</summary>
    [Fact]
    public void Bip_nao_guarda_texto_nem_motivo()
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(
            1, TipoDeComando.BipCurto, "Ana", Agora, texto: "ignorado", motivo: "ignorado");

        Assert.Empty(problemas);
        Assert.Null(comando!.Texto);
        Assert.Null(comando.Motivo);
        Assert.Equal(Agora + TimeSpan.FromSeconds(60), comando.ExpiraEm);
    }

    /// <summary>A saída é a outra função do par da matriz (EI-041/042 direta, EI-043/044 invertida).</summary>
    [Theory]
    [InlineData(FuncaoDeLiberacao.Entrada, GateDirection.Saida)]
    [InlineData(FuncaoDeLiberacao.EntradaInvertida, GateDirection.SaidaInvertida)]
    [InlineData(FuncaoDeLiberacao.Saida, GateDirection.Entrada)]
    [InlineData(FuncaoDeLiberacao.SaidaInvertida, GateDirection.EntradaInvertida)]
    public void Saida_e_a_outra_funcao_do_par(FuncaoDeLiberacao entrada, GateDirection saida)
    {
        var perfil = new GatePhysicalProfile(entrada);

        Assert.Equal(saida, perfil.LiberacaoDaSaida);
        Assert.NotEqual(perfil.LiberacaoDaEntrada, perfil.LiberacaoDaSaida);
        Assert.NotEqual(GateDirection.DoisSentidos, perfil.LiberacaoDaSaida);
    }
}
