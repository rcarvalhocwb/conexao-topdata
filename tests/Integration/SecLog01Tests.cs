using System.Text.Json;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker;
using Shared.Observability;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// SEC-LOG-01 — roda o fluxo real e varre tudo que ele escreveu.
/// </summary>
/// <remarks>
/// <para>
/// Diferente da varredura estática da CI, que olha o código-fonte, este teste olha a
/// <b>saída</b>. É a diferença entre "ninguém escreveu uma chamada perigosa" e "nada
/// sensível saiu". Critério CA-10 de docs/00-entendimento-e-escopo.md.
/// </para>
/// <para>
/// O teste <b>precisa</b> empurrar o dado sensível para dentro do log, como um
/// desenvolvedor faria ao diagnosticar um problema de leitura. Uma primeira versão
/// deste arquivo só observava o log do laço — que não carrega número de cartão — e por
/// isso passava mesmo com a redação desligada. Um teste que não pode falhar não prova
/// nada.
/// </para>
/// </remarks>
public sealed class SecLog01Tests
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

    /// <summary>Valores que não podem sair, em nenhuma forma.</summary>
    private static readonly string[] Segredos =
    [
        "0001234567",
        "9876543210987654",
        "123456",
        "/9j/4AAQSkZJRgABAQAAAQABAAD",
    ];

    private static DeviceConfiguration Configuracao() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 3,
        OperacaoDoLeitor1 = 3,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 2,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Bem-vindo",
        PerfilFisico = new GatePhysicalProfile(SentidoInvertido: false),
    };

    [Fact]
    public void Operacao_completa_nao_deixa_dado_sensivel_no_log()
    {
        var destino = new DestinoEmMemoria();
        var log = new LogEstruturado(destino, "worker-1", () => Inicio);

        using var simulador = new InnerSimulator(() => Inicio);
        var slots = new[] { new DeviceSlot(1, Configuracao(), () => Inicio) };
        var laco = new DeviceGroupLoop(
            simulador,
            slots,
            new Watchdog(TimeSpan.FromSeconds(30), () => Inicio),
            new DevicePump(
                simulador,
                () => Inicio,
                decidir: _ => new Decision(
                    DecisionOutcome.Allowed,
                    ReasonCodes.Autorizado,
                    DegradationTier.T1SemInternet,
                    TimeSpan.FromMilliseconds(9),
                    [])),
            log: log);

        laco.Iniciar(3570);

        // Cartões reais passando pelo fluxo, mais a foto em Base64 que o leitor facial
        // envia quando o rosto não está cadastrado.
        simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0001234567"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "9876543210987654"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.QrCode), "123456"),
            new ScriptedEvent(EventOrigin.FromRaw(14), "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQAAAQABAAD"));

        for (var i = 0; i < 40; i++)
        {
            laco.UmaVolta();

            // O caminho realista do vazamento: alguém logando o evento para diagnosticar
            // uma leitura que não funcionou. É exatamente aqui que o número de cartão
            // chegaria ao disco se a redação não existisse.
            foreach (var evento in slots[0].EventosPendentes)
            {
                log.Depuracao(
                    $"evento {evento.Origin} com conteúdo {evento.RawCardData}",
                    evento.CorrelationId,
                    new Dictionary<string, object?>
                    {
                        ["inner"] = 1,
                        ["cartao"] = evento.RawCardData,
                        ["origem"] = evento.Origin.Raw,
                    });
            }

            slots[0].EventosPendentes.Clear();
        }

        Assert.NotEmpty(destino.Linhas);

        // Sanidade: o teste só tem valor se o dado sensível realmente passou por aqui.
        Assert.Contains(destino.Linhas, l => l.Contains("\"cartao\"", StringComparison.Ordinal));

        var tudo = string.Join('\n', destino.Linhas);

        foreach (var segredo in Segredos)
        {
            Assert.DoesNotContain(segredo, tudo, StringComparison.Ordinal);
        }

        // A heurística roda só sobre os campos de conteúdo: um UUID de correlação tem
        // cerca de 15% de chance de conter uma corrida de 6 dígitos e pareceria um
        // cartão, tornando o teste intermitente sem apontar vazamento nenhum.
        var suspeitas = CamposDeConteudo(destino.Linhas)
            .Where(c => RedatorDeDadoSensivel.ParecemHaverDadosSensiveis(c.Valor))
            .Select(c => $"{c.Campo} = {c.Valor}")
            .ToList();

        Assert.True(
            suspeitas.Count == 0,
            $"sobrou algo com cara de dado sensível no log: {string.Join(" | ", suspeitas)}");
    }

    /// <summary>
    /// A verificação do verificador: se a redação for removida, este teste precisa
    /// acusar. Sem isso, ele poderia estar passando por não haver nada no log.
    /// </summary>
    /// <summary>
    /// Identificadores gerados pelo sistema, que nunca carregam dado do titular e não
    /// devem ser redigidos — senão o log perde a serventia.
    /// </summary>
    /// <remarks>
    /// Precisam ficar de fora da varredura heurística: um UUID tem cerca de 15% de
    /// chance de conter uma corrida de 6 dígitos, o que o faria parecer um cartão.
    /// A busca pelos segredos exatos continua valendo sobre o texto inteiro.
    /// </remarks>
    private static readonly string[] CamposDeIdentificacao = ["t", "nivel", "componente", "correlationId"];

    /// <summary>Campos de conteúdo de cada linha de log, já sem os identificadores.</summary>
    private static IEnumerable<(string Campo, string? Valor)> CamposDeConteudo(IEnumerable<string> linhas)
    {
        foreach (var linha in linhas)
        {
            using var documento = JsonDocument.Parse(linha);
            foreach (var propriedade in documento.RootElement.EnumerateObject())
            {
                if (!CamposDeIdentificacao.Contains(propriedade.Name, StringComparer.Ordinal))
                {
                    yield return (propriedade.Name, propriedade.Value.ToString());
                }
            }
        }
    }

    [Fact]
    public void O_proprio_teste_acusaria_um_vazamento()
    {
        var destino = new DestinoEmMemoria();

        // Escreve sem passar pelo redator, de propósito.
        destino.Escrever("""{"mensagem":"cartao 0001234567 lido"}""");

        var tudo = string.Join('\n', destino.Linhas);

        Assert.Contains("0001234567", tudo, StringComparison.Ordinal);
        Assert.True(RedatorDeDadoSensivel.ParecemHaverDadosSensiveis(tudo));
    }

    [Fact]
    public void O_log_do_laco_continua_util_para_diagnostico()
    {
        var destino = new DestinoEmMemoria();
        var log = new LogEstruturado(destino, "worker-1", () => Inicio);

        using var simulador = new InnerSimulator(() => Inicio);
        var laco = new DeviceGroupLoop(
            simulador,
            [new DeviceSlot(8, Configuracao(), () => Inicio)],
            new Watchdog(TimeSpan.FromSeconds(30), () => Inicio),
            new DevicePump(simulador, () => Inicio),
            log: log);

        laco.Iniciar(3570);
        for (var i = 0; i < 12; i++)
        {
            laco.UmaVolta();
        }

        var tudo = string.Join('\n', destino.Linhas);

        // Redigir tudo seria fácil e inútil: o log precisa continuar dizendo o que houve.
        Assert.Contains("Polling", tudo, StringComparison.Ordinal);
        Assert.Contains("\"inner\":\"8\"", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain(RedatorDeDadoSensivel.Marca, tudo, StringComparison.Ordinal);
    }
}
