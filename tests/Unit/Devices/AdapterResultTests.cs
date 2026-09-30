using Access.Application.Devices;

namespace Unit.Tests.Devices;

/// <summary>
/// Retorno nativo interpretado junto com a função que o devolveu (defeito F6, docs/34 §2).
/// </summary>
/// <remarks>
/// Antes, 2, 3, 9 e 128–130 caíam todos em <see cref="AdapterStatus.RetornoDesconhecido"/>,
/// embora a matriz FUN diga o que cada um quer dizer em cada função.
/// </remarks>
public sealed class AdapterResultTests
{
    [Fact]
    public void Retorno_129_em_configurar_leitor_2_e_configuracao_recusada()
    {
        var resultado = AdapterResult.FromNative(129, TimeSpan.Zero, "ConfigurarLeitor2");

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Equal(129, resultado.NativeReturn);
        Assert.Equal("ConfigurarLeitor2", resultado.Funcao);
        Assert.Equal("configuração recusada: ConfigurarLeitor2 devolveu 129 (operação inválida)", resultado.Significado);
        Assert.Contains("ConfigurarLeitor2", resultado.ToString(), StringComparison.Ordinal);
    }

    /// <summary>O mesmo número quer dizer outra coisa em outra função: com as palavras da matriz.</summary>
    [Theory]
    [InlineData("HabilitarTeclado", 128, "habilita inválido")]
    [InlineData("HabilitarTeclado", 129, "ecoar inválido")]
    [InlineData("HabilitarMudancaOnLineOffLine", 129, "tempo inválido")]
    [InlineData("DefinirPadraoCartao", 128, "padrão inválido")]
    [InlineData("DefinirQuantidadeDigitosCartao", 128, "quantidade inválida")]
    [InlineData("ConfigurarLeitor1", 128, "operação inválida")]
    [InlineData("InserirUsuarioListaAcesso", 130, "horário inválido")]
    [InlineData("DefinirTipoConexao", 9, "tipo de conexão inválido")]
    public void Recusa_documentada_diz_a_funcao_e_o_motivo(string funcao, int retorno, string motivo)
    {
        var resultado = AdapterResult.FromNative(retorno, TimeSpan.Zero, funcao);

        Assert.Equal(AdapterStatus.ConfiguracaoRecusada, resultado.Status);
        Assert.Contains(motivo, resultado.Significado, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, AdapterStatus.ErroDeComunicacao, "porta não aberta")]
    [InlineData(3, AdapterStatus.PortaJaAberta, "porta já aberta")]
    [InlineData(4, AdapterStatus.FalhaDeDependencia, "DLL de apoio ausente")]
    [InlineData(5, AdapterStatus.FalhaDeDependencia, "DLL de apoio ausente")]
    [InlineData(6, AdapterStatus.FalhaDeDependencia, "DLL de apoio ausente")]
    [InlineData(8, AdapterStatus.FalhaDeDependencia, null)]
    public void Retornos_de_abrir_a_porta_seguem_a_matriz(int retorno, AdapterStatus esperado, string? significado)
    {
        var resultado = AdapterResult.FromNative(retorno, TimeSpan.Zero, "AbrirPortaComunicacao");

        Assert.Equal(esperado, resultado.Status);
        Assert.Equal(significado, resultado.Significado);
        Assert.Equal(retorno, resultado.NativeReturn);
    }

    /// <summary>
    /// Nada é deduzido: 129 não é documentado para <c>ConfigurarLeitor1</c>, e 3 sem a função
    /// não é "porta já aberta". Continuam desconhecidos, com o bruto (ADR-0018).
    /// </summary>
    [Theory]
    [InlineData("ConfigurarLeitor1", 129)]
    [InlineData("ReceberDadosOnLine", 3)]
    [InlineData(null, 3)]
    [InlineData(null, 129)]
    public void Retorno_nao_documentado_para_a_funcao_continua_desconhecido(string? funcao, int retorno)
    {
        var resultado = AdapterResult.FromNative(retorno, TimeSpan.Zero, funcao);

        Assert.Equal(AdapterStatus.RetornoDesconhecido, resultado.Status);
        Assert.Equal(retorno, resultado.NativeReturn);
        Assert.Null(resultado.Significado);
    }

    /// <summary>O mapeamento geral não muda: 0, 1 e 8 valem para qualquer função.</summary>
    [Theory]
    [InlineData(0, AdapterStatus.Ok)]
    [InlineData(1, AdapterStatus.Erro)]
    [InlineData(8, AdapterStatus.FalhaDeDependencia)]
    public void Mapeamento_geral_continua_valendo(int retorno, AdapterStatus esperado)
    {
        Assert.Equal(esperado, AdapterResult.FromNative(retorno, TimeSpan.Zero).Status);
        Assert.Equal(esperado, AdapterResult.FromNative(retorno, TimeSpan.Zero, "ConfigurarLeitor2").Status);
    }
}
