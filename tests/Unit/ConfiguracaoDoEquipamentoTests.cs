using Access.Application.Devices;

namespace Unit.Tests;

/// <summary>
/// Faixas de configuração, conferidas contra os enums do SDK oficial.
/// </summary>
/// <remarks>
/// <para>
/// Escritos depois de o pacote de exemplos chegar. A validação existia desde a Fase 1 e
/// <b>nunca tinha teste direto</b>: só era exercitada de lado pelo simulador, que usa uma
/// configuração válida e portanto nunca tocava nos limites.
/// </para>
/// <para>
/// Duas faixas estavam erradas, as duas por o manual ser mais estreito que o SDK. A pior
/// rejeitava o leitor de QR Code — o de um evento com ingresso impresso.
/// </para>
/// </remarks>
public sealed class ConfiguracaoDoEquipamentoTests
{
    private static DeviceConfiguration Valida() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
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
        MudancaAutomatica = 1,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "ENTRADA - PISTA A",
        PerfilFisico = new GatePhysicalProfile(SentidoInvertido: false),
    };

    [Fact]
    public void A_configuracao_de_referencia_e_valida()
    {
        Assert.Empty(Valida().Validar());
    }

    /// <summary>
    /// O valor 8 é QR Code por letras, e é o leitor de um evento com ingresso impresso.
    /// </summary>
    /// <remarks>
    /// Este é o teste que faltava. O manual parava em 7, então a validação rejeitava o 8 e
    /// teria bloqueado a configuração no dia do evento.
    /// </remarks>
    [Theory]
    [InlineData(0)] // código de barras
    [InlineData(2)] // proximidade Abatrack II
    [InlineData(3)] // Wiegand
    [InlineData(7)] // Wiegand FC com separador — o manual chamava de "TTL Serial ASCII"
    [InlineData(8)] // QR Code por letras
    public void Todo_tipo_de_leitor_do_sdk_e_aceito(byte tipo)
    {
        Assert.Empty((Valida() with { TipoDeLeitor = tipo }).Validar());
    }

    [Fact]
    public void Tipo_de_leitor_acima_do_sdk_e_recusado()
    {
        var problemas = (Valida() with { TipoDeLeitor = 9 }).Validar();
        Assert.Contains(problemas, p => p.Contains("TipoDeLeitor", StringComparison.Ordinal));
    }

    /// <summary>
    /// Os valores 6 a 9 sinalizam estados da catraca e não constam do manual.
    /// </summary>
    [Theory]
    [InlineData(4)] // sirene
    [InlineData(5)] // revista
    [InlineData(6)] // catraca com saída liberada
    [InlineData(7)] // catraca com entrada liberada
    [InlineData(8)] // catraca liberada nos dois sentidos
    [InlineData(9)] // dois sentidos, com marcação de registro
    public void Toda_funcao_de_acionamento_do_sdk_e_aceita(byte funcao)
    {
        Assert.Empty((Valida() with { FuncaoDoAcionamento1 = funcao }).Validar());
    }

    [Fact]
    public void Funcao_de_acionamento_acima_do_sdk_e_recusada()
    {
        var problemas = (Valida() with { FuncaoDoAcionamento1 = 10 }).Validar();
        Assert.Contains(problemas, p => p.Contains("FuncaoDoAcionamento1", StringComparison.Ordinal));
    }

    /// <summary>Sem leitor 2 não há como receber o cartão na fenda da urna.</summary>
    [Fact]
    public void Rele_da_urna_sem_leitor_dois_e_recusado()
    {
        var problemas = (Valida() with { FuncaoDoAcionamento2 = 1, OperacaoDoLeitor2 = 0 }).Validar();
        Assert.Contains(problemas, p => p.Contains("leitor 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Mensagem_acima_de_trinta_e_dois_caracteres_e_recusada()
    {
        var problemas = (Valida() with { MensagemPadrao = new string('A', 33) }).Validar();
        Assert.Contains(problemas, p => p.Contains("MensagemPadrao", StringComparison.Ordinal));
    }

    [Fact]
    public void Tempo_de_acionamento_acima_de_cinquenta_segundos_e_recusado()
    {
        var problemas = (Valida() with { TempoDoAcionamento1 = 51 }).Validar();
        Assert.Contains(problemas, p => p.Contains("TempoDoAcionamento1", StringComparison.Ordinal));
    }

    /// <summary>Modo 2 da mudança automática depende de PingOnline periódico.</summary>
    [Fact]
    public void Mudanca_automatica_com_ping_exige_modo_online()
    {
        var problemas = (Valida() with { MudancaAutomatica = 2, Online = false }).Validar();
        Assert.Contains(problemas, p => p.Contains("MudancaAutomatica", StringComparison.Ordinal));
    }
}
