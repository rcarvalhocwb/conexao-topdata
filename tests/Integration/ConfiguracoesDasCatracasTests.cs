using System.Collections;
using System.Reflection;
using Access.Application.Devices;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// Configuração por catraca (Etapa A.3 do docs/35; migração 012): ausente herda, grava e lê,
/// limpar volta a herdar, histórico só-INSERT, e valor inválido volta como problema sem gravar.
/// </summary>
/// <remarks>Dados sintéticos; catracas de número baixo, nenhum equipamento real.</remarks>
public sealed class ConfiguracoesDasCatracasTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Uma catraca que sobrepõe tudo o que pode, com valores válidos.</summary>
    private static SobreposicoesDaCatraca Completa() => new()
    {
        TipoDeLeitor = 5,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        TempoDoAcionamento1 = 7,
        FuncaoDeLiberacaoDaEntrada = FuncaoDeLiberacao.EntradaInvertida,
        MensagemPadrao = "Portao sintetico 2",
        WiegandDoisLeitores = new WiegandDoisLeitores(true, false),
        FormasDeEntradaOnLine = new FormasDeEntradaOnLine(0, 0, 3, 0, 0),
    };

    private static (BancoTemporario Banco, ConfiguracoesDasCatracas Catracas) Preparar()
    {
        var banco = new BancoTemporario();
        banco.Migrar();
        return (banco, new ConfiguracoesDasCatracas(banco.Fabrica));
    }

    private static DeviceConfiguration Montar(BancoTemporario banco, SobreposicoesDaCatraca catraca) =>
        MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4, new ConfiguracoesDaBorda(banco.Fabrica).Ler().Configuracao.ParaACatraca(), catraca);

    private static void Executar(BancoTemporario banco, string sql)
    {
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }

    private static void CampoACampo(DeviceConfiguration esperado, DeviceConfiguration obtido)
    {
        foreach (var propriedade in typeof(DeviceConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var a = propriedade.GetValue(esperado);
            var b = propriedade.GetValue(obtido);

            if (a is IEnumerable listaA and not string && b is IEnumerable listaB and not string)
            {
                Assert.True(listaA.Cast<object>().SequenceEqual(listaB.Cast<object>()), propriedade.Name);
            }
            else
            {
                Assert.True(Equals(a, b), $"{propriedade.Name}: {a} != {b}");
            }
        }
    }

    [Fact]
    public void Catraca_ausente_herda_o_evento()
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;
        new ConfiguracoesDaBorda(banco.Fabrica).Gravar(
            new ConfiguracaoDaOperacao(TipoDeLeitor: 5, TempoDeAcionamento: 9, MensagemPadrao: "Evento sintetico"), Agora, "teste");

        var (lida, problemas) = catracas.Ler(3);

        Assert.Same(SobreposicoesDaCatraca.Nenhuma, lida);
        Assert.Empty(problemas);
        Assert.Empty(catracas.Listar());
        CampoACampo(Montar(banco, SobreposicoesDaCatraca.Nenhuma), Montar(banco, lida));
        Assert.Equal(5, Montar(banco, lida).TipoDeLeitor);
    }

    [Fact]
    public void Grava_e_le_com_quem_e_quando()
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;

        Assert.Empty(catracas.Gravar(2, Completa(), Agora, "  Operador sintetico  "));

        var (lida, problemas) = catracas.Ler(2);
        Assert.Empty(problemas);
        Assert.Equal(Completa(), lida);

        var registro = Assert.Single(catracas.Listar());
        Assert.Equal(2, registro.Inner);
        Assert.Equal(1, registro.Revisao);
        Assert.Equal("Operador sintetico", registro.AlteradoPor);
        Assert.Equal(Agora, registro.AlteradoEm);
        Assert.Empty(registro.Problemas);

        // A outra catraca continua herdando.
        Assert.Same(SobreposicoesDaCatraca.Nenhuma, catracas.Ler(1).Sobreposicoes);

        var montada = Montar(banco, lida);
        Assert.Equal(5, montada.TipoDeLeitor);
        Assert.Equal(FuncaoDeLiberacao.EntradaInvertida, montada.PerfilFisico.FuncaoDeLiberacaoDaEntrada);
        Assert.Empty(montada.Validar());
    }

    [Fact]
    public void Limpar_um_campo_volta_a_herdar()
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;

        Assert.Empty(catracas.Gravar(2, Completa(), Agora, "teste"));
        Assert.Empty(catracas.Gravar(2, Completa() with { TipoDeLeitor = null, WiegandDoisLeitores = null }, Agora.AddMinutes(1), "teste"));

        var (lida, _) = catracas.Ler(2);
        Assert.Null(lida.TipoDeLeitor);
        Assert.Null(lida.WiegandDoisLeitores);
        Assert.Equal("Portao sintetico 2", lida.MensagemPadrao);

        var montada = Montar(banco, lida);
        var doEvento = Montar(banco, SobreposicoesDaCatraca.Nenhuma);
        Assert.Equal(doEvento.TipoDeLeitor, montada.TipoDeLeitor);
        Assert.Equal(doEvento.WiegandDoisLeitores, montada.WiegandDoisLeitores);

        // Tudo nulo: a catraca volta a herdar tudo, e a linha fica (com o histórico).
        Assert.Empty(catracas.Gravar(2, new SobreposicoesDaCatraca(), Agora.AddMinutes(2), "teste"));
        CampoACampo(doEvento, Montar(banco, catracas.Ler(2).Sobreposicoes));
        Assert.Equal(3, Assert.Single(catracas.Listar()).Revisao);
    }

    [Fact]
    public void Cada_gravacao_gera_um_retrato_no_historico()
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;

        Assert.Empty(catracas.Gravar(2, new SobreposicoesDaCatraca { TipoDeLeitor = 3 }, Agora, "primeiro"));
        Assert.Empty(catracas.Gravar(2, new SobreposicoesDaCatraca { TipoDeLeitor = 5, MensagemPadrao = "Sintetica" }, Agora.AddMinutes(1), "segundo"));
        Assert.Empty(catracas.Gravar(2, new SobreposicoesDaCatraca { MensagemPadrao = "Sintetica" }, Agora.AddMinutes(2), "terceiro"));
        Assert.Empty(catracas.Gravar(4, Completa(), Agora, "outra"));

        var historico = catracas.Historico(2);

        Assert.Equal([1, 2, 3], historico.Select(h => h.Revisao));
        Assert.Equal(["primeiro", "segundo", "terceiro"], historico.Select(h => h.AlteradoPor));
        Assert.Equal(new SobreposicoesDaCatraca { TipoDeLeitor = 3 }, historico[0].Sobreposicoes);
        Assert.Equal(new SobreposicoesDaCatraca { TipoDeLeitor = 5, MensagemPadrao = "Sintetica" }, historico[1].Sobreposicoes);
        Assert.Equal(new SobreposicoesDaCatraca { MensagemPadrao = "Sintetica" }, historico[2].Sobreposicoes);
        Assert.Equal(Agora.AddMinutes(1), historico[1].AlteradoEm);
        Assert.Equal(Completa(), Assert.Single(catracas.Historico(4)).Sobreposicoes);
    }

    [Theory]
    [InlineData("UPDATE device_config_history SET reader_type = 1;")]
    [InlineData("UPDATE device_config_history SET updated_by = 'outro';")]
    [InlineData("DELETE FROM device_config_history;")]
    [InlineData("DELETE FROM device_config;")]
    [InlineData("UPDATE device_config SET reader_type = 1;")]
    [InlineData("UPDATE device_config SET inner_number = 9, revision = revision + 1;")]
    [InlineData("INSERT INTO device_config (inner_number, revision, updated_by, updated_at) VALUES (7, 5, 'x', '2026-09-30T12:00:00Z');")]
    public void Historico_nao_muda_nem_se_apaga_e_nada_escapa_dele(string sql)
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;
        Assert.Empty(catracas.Gravar(2, Completa(), Agora, "teste"));

        Assert.Throws<SqliteException>(() => Executar(banco, sql));

        Assert.Equal(Completa(), Assert.Single(catracas.Historico(2)).Sobreposicoes);
        Assert.Equal(Completa(), catracas.Ler(2).Sobreposicoes);
        Assert.Empty(catracas.Historico(7));
    }

    /// <summary>Casos inválidos, cada um com a catraca que o provoca.</summary>
    public static TheoryData<string> Invalidos() =>
    [
        "tipo-9", "leitor1-5", "leitor2-5", "tempo-0", "tempo-51", "mensagem-33", "mensagem-branca",
        "forma-8", "funcao-fora-do-enum", "inner-0", "inner-100", "sem-quem",
    ];

    private static (int Inner, SobreposicoesDaCatraca Catraca, string Quem) Invalido(string caso) => caso switch
    {
        "tipo-9" => (2, new SobreposicoesDaCatraca { TipoDeLeitor = 9 }, "teste"),
        "leitor1-5" => (2, new SobreposicoesDaCatraca { OperacaoDoLeitor1 = 5 }, "teste"),
        "leitor2-5" => (2, new SobreposicoesDaCatraca { OperacaoDoLeitor2 = 5 }, "teste"),
        "tempo-0" => (2, new SobreposicoesDaCatraca { TempoDoAcionamento1 = 0 }, "teste"),
        "tempo-51" => (2, new SobreposicoesDaCatraca { TempoDoAcionamento1 = 51 }, "teste"),
        "mensagem-33" => (2, new SobreposicoesDaCatraca { MensagemPadrao = new string('X', 33) }, "teste"),
        "mensagem-branca" => (2, new SobreposicoesDaCatraca { MensagemPadrao = "   " }, "teste"),
        "forma-8" => (2, new SobreposicoesDaCatraca { FormasDeEntradaOnLine = new FormasDeEntradaOnLine(0, 0, 8, 0, 0) }, "teste"),
        // Só o Validar() do resultado montado recusa este: prova que a gravação passa por ele.
        "funcao-fora-do-enum" => (2, new SobreposicoesDaCatraca { FuncaoDeLiberacaoDaEntrada = (FuncaoDeLiberacao)9 }, "teste"),
        "inner-0" => (0, Completa(), "teste"),
        "inner-100" => (100, Completa(), "teste"),
        "sem-quem" => (2, Completa(), " "),
        _ => throw new ArgumentOutOfRangeException(nameof(caso), caso, "caso desconhecido"),
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public void Valor_invalido_volta_como_problema_e_nada_e_gravado(string caso)
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;
        var (inner, catraca, quem) = Invalido(caso);

        var problemas = catracas.Gravar(inner, catraca, Agora, quem);

        Assert.NotEmpty(problemas);
        Assert.Empty(catracas.Listar());
        Assert.Empty(catracas.Historico(inner));

        // Sobre uma gravação válida, a inválida não muda nada nem abre revisão. (Com o número
        // da catraca fora da faixa não há gravação válida anterior possível.)
        if (inner is < ConfiguracoesDasCatracas.InnerMinimo or > ConfiguracoesDasCatracas.InnerMaximo)
        {
            return;
        }

        Assert.Empty(catracas.Gravar(2, Completa(), Agora, "teste"));
        Assert.NotEmpty(catracas.Gravar(2, catraca, Agora.AddMinutes(1), quem));
        Assert.Equal(1, Assert.Single(catracas.Listar()).Revisao);
        Assert.Equal(Completa(), catracas.Ler(2).Sobreposicoes);
    }

    /// <summary>
    /// Valor que só entra passando por cima dos CHECK vira problema na leitura, e o campo
    /// volta a herdar do evento; os outros campos da linha continuam valendo.
    /// </summary>
    [Fact]
    public void Valor_ilegivel_na_base_vira_problema_e_herda()
    {
        var (banco, catracas) = Preparar();
        using var _ = banco;
        Assert.Empty(catracas.Gravar(2, Completa(), Agora, "teste"));

        using (var conexao = banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                PRAGMA ignore_check_constraints = ON;
                UPDATE device_config
                SET reader_type = 42, release_function = 'Lateral', entry_form = 8, wiegand_show_message = NULL,
                    updated_at = 'ontem', revision = revision + 1;
                """;
            comando.ExecuteNonQuery();
        }

        var (lida, problemas) = catracas.Ler(2);

        Assert.Equal(5, problemas.Count);
        Assert.All(problemas, p => Assert.StartsWith("Catraca 2:", p, StringComparison.Ordinal));
        Assert.Null(lida.TipoDeLeitor);
        Assert.Null(lida.FuncaoDeLiberacaoDaEntrada);
        Assert.Null(lida.FormasDeEntradaOnLine);
        Assert.Null(lida.WiegandDoisLeitores);
        Assert.Equal("Portao sintetico 2", lida.MensagemPadrao);
        Assert.Equal((byte)7, lida.TempoDoAcionamento1);

        var registro = Assert.Single(catracas.Listar());
        Assert.Equal(5, registro.Problemas.Count);
        Assert.Equal(2, catracas.Historico(2).Count);
        Assert.Empty(Montar(banco, lida).Validar());
    }
}
