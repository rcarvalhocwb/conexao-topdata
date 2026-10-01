using System.Collections;
using System.Reflection;
using Access.Application.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// A camada da catraca no <see cref="MontadorDaConfiguracao"/> (Etapa A.3 do docs/35): para
/// cada campo sobreponível, a catraca vence o evento, que vence a fábrica, e nenhum outro campo
/// muda.
/// </summary>
/// <remarks>Dados sintéticos; nenhum valor de equipamento real.</remarks>
public sealed class CamadaDaCatracaTests
{
    /// <summary>
    /// Um campo sobreponível: como o evento o muda (quando muda), como a catraca o muda, como
    /// ler o resultado e o valor esperado em cada camada.
    /// </summary>
    private sealed record Campo(
        string Propriedade,
        SobreposicoesDoEvento? Evento,
        SobreposicoesDaCatraca Catraca,
        Func<DeviceConfiguration, object> Ler,
        object DaFabrica,
        object? DoEvento,
        object DaCatraca);

    private static readonly Dictionary<string, Campo> Campos = new(StringComparer.Ordinal)
    {
        ["tipo-de-leitor"] = new(
            nameof(DeviceConfiguration.TipoDeLeitor),
            new SobreposicoesDoEvento { TipoDeLeitor = 5 },
            new SobreposicoesDaCatraca { TipoDeLeitor = 3 },
            c => c.TipoDeLeitor, (byte)8, (byte)5, (byte)3),
        ["leitor-1"] = new(
            nameof(DeviceConfiguration.OperacaoDoLeitor1),
            null,
            new SobreposicoesDaCatraca { OperacaoDoLeitor1 = 3 },
            c => c.OperacaoDoLeitor1, (byte)1, null, (byte)3),
        ["leitor-2-urna"] = new(
            nameof(DeviceConfiguration.OperacaoDoLeitor2),
            new SobreposicoesDoEvento { LeitorDaUrna = false },
            new SobreposicoesDaCatraca { OperacaoDoLeitor2 = 2 },
            c => c.OperacaoDoLeitor2, (byte)1, (byte)0, (byte)2),
        ["tempo-rele-1"] = new(
            nameof(DeviceConfiguration.TempoDoAcionamento1),
            new SobreposicoesDoEvento { TempoDeAcionamento = 12 },
            new SobreposicoesDaCatraca { TempoDoAcionamento1 = 7 },
            c => c.TempoDoAcionamento1, (byte)5, (byte)12, (byte)7),
        ["funcao-de-liberacao"] = new(
            nameof(DeviceConfiguration.PerfilFisico),
            null,
            new SobreposicoesDaCatraca { FuncaoDeLiberacaoDaEntrada = FuncaoDeLiberacao.SaidaInvertida },
            c => c.PerfilFisico.FuncaoDeLiberacaoDaEntrada,
            FuncaoDeLiberacao.Entrada, null, FuncaoDeLiberacao.SaidaInvertida),
        ["mensagem"] = new(
            nameof(DeviceConfiguration.MensagemPadrao),
            new SobreposicoesDoEvento { MensagemPadrao = "Evento sintetico" },
            new SobreposicoesDaCatraca { MensagemPadrao = "Catraca sintetica 2" },
            c => c.MensagemPadrao, "Aproxime o ingresso", "Evento sintetico", "Catraca sintetica 2"),
        ["wiegand"] = new(
            nameof(DeviceConfiguration.WiegandDoisLeitores),
            null,
            new SobreposicoesDaCatraca { WiegandDoisLeitores = new WiegandDoisLeitores(true, true) },
            c => c.WiegandDoisLeitores,
            new WiegandDoisLeitores(false, false), null, new WiegandDoisLeitores(true, true)),
        ["formas-de-entrada"] = new(
            nameof(DeviceConfiguration.FormasDeEntradaOnLine),
            null,
            new SobreposicoesDaCatraca { FormasDeEntradaOnLine = new FormasDeEntradaOnLine(0, 0, 3, 0, 0) },
            c => c.FormasDeEntradaOnLine,
            FormasDeEntradaOnLine.DeHoje, null, new FormasDeEntradaOnLine(0, 0, 3, 0, 0)),
    };

    public static TheoryData<string> NomesDosCampos() => [.. Campos.Keys];

    private static DeviceConfiguration Montar(SobreposicoesDoEvento? evento, SobreposicoesDaCatraca catraca) =>
        MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento ?? SobreposicoesDoEvento.Nenhuma, catraca);

    /// <summary>Compara todas as propriedades públicas, menos as ignoradas, e diz qual divergiu.</summary>
    private static void CampoACampo(DeviceConfiguration esperado, DeviceConfiguration obtido, params string[] ignorar)
    {
        foreach (var propriedade in typeof(DeviceConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ignorar.Contains(propriedade.Name))
            {
                continue;
            }

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

    [Theory]
    [MemberData(nameof(NomesDosCampos))]
    public void Catraca_vence_o_evento_que_vence_a_fabrica(string nome)
    {
        var campo = Campos[nome];

        Assert.Equal(campo.DaFabrica, campo.Ler(Montar(null, SobreposicoesDaCatraca.Nenhuma)));

        if (campo.Evento is not null)
        {
            Assert.Equal(campo.DoEvento, campo.Ler(Montar(campo.Evento, SobreposicoesDaCatraca.Nenhuma)));
        }

        Assert.Equal(campo.DaCatraca, campo.Ler(Montar(campo.Evento, campo.Catraca)));
        Assert.Equal(campo.DaCatraca, campo.Ler(Montar(null, campo.Catraca)));

        // Os valores de teste são distintos entre as camadas: senão a prova não prova nada.
        Assert.NotEqual(campo.DaFabrica, campo.DaCatraca);
        Assert.NotEqual(campo.DoEvento, campo.DaCatraca);
    }

    /// <summary>Sobrepor um campo não mexe em nenhum outro.</summary>
    [Theory]
    [MemberData(nameof(NomesDosCampos))]
    public void Sobrepor_um_campo_nao_muda_os_outros(string nome)
    {
        var campo = Campos[nome];

        CampoACampo(
            Montar(campo.Evento, SobreposicoesDaCatraca.Nenhuma),
            Montar(campo.Evento, campo.Catraca),
            campo.Propriedade);
    }

    /// <summary>Com a catraca vazia, o resultado é, campo a campo, o da camada do evento.</summary>
    [Theory]
    [MemberData(nameof(NomesDosCampos))]
    public void Catraca_vazia_herda_tudo_do_evento(string nome)
    {
        var evento = Campos[nome].Evento ?? SobreposicoesDoEvento.Nenhuma;

        CampoACampo(Montar(evento, SobreposicoesDaCatraca.Nenhuma), Montar(evento, new SobreposicoesDaCatraca()));
    }

    /// <summary>
    /// A camada da catraca tem só os campos que o docs/34 §4.1 ("Para a A.3") permite: nenhuma
    /// chave técnica, nada de <c>RegistrarAcessoNegado</c>, <c>DataHoraNoEventoOnLine</c>,
    /// <c>TipoDeLista</c> e, sobretudo, nada de cartão master.
    /// </summary>
    [Fact]
    public void Catraca_so_sobrepoe_os_campos_do_equipamento()
    {
        var nomes = typeof(SobreposicoesDaCatraca)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                nameof(SobreposicoesDaCatraca.FormasDeEntradaOnLine),
                nameof(SobreposicoesDaCatraca.FuncaoDeLiberacaoDaEntrada),
                nameof(SobreposicoesDaCatraca.MensagemPadrao),
                nameof(SobreposicoesDaCatraca.OperacaoDoLeitor1),
                nameof(SobreposicoesDaCatraca.OperacaoDoLeitor2),
                nameof(SobreposicoesDaCatraca.TempoDoAcionamento1),
                nameof(SobreposicoesDaCatraca.TipoDeLeitor),
                nameof(SobreposicoesDaCatraca.WiegandDoisLeitores),
            }.Order(StringComparer.Ordinal),
            nomes);

        Assert.Equal(Campos.Count, nomes.Count());
    }

    /// <summary>
    /// Wiegand e formas de entrada por catraca mudam o modelo, não a chave: continuam indo à
    /// DLL só com a chave técnica do evento, que a catraca não liga.
    /// </summary>
    [Fact]
    public void Catraca_nao_liga_chave_tecnica()
    {
        var catraca = new SobreposicoesDaCatraca
        {
            WiegandDoisLeitores = new WiegandDoisLeitores(true, false),
            FormasDeEntradaOnLine = new FormasDeEntradaOnLine(0, 0, 3, 0, 0),
        };

        var montada = Montar(null, catraca);

        Assert.False(montada.EnviarWiegandDoisLeitores);
        Assert.False(montada.EnviarFormasDeEntradaOnLine);
        Assert.Null(montada.CartaoMaster);
    }
}
