using System.Globalization;
using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Devices;

namespace Edge.Worker.Bancada;

/// <summary>
/// Configuração da TopFit 4 para o ensaio de bancada.
/// </summary>
/// <remarks>
/// <para>
/// Cada valor tem fonte, e o que não tem está desligado em vez de adivinhado:
/// </para>
/// <list type="bullet">
/// <item>Cartão <b>padrão livre</b> e <b>dígitos variáveis de 4 a 16</b> — passo a passo da
/// Topdata para cadastrar a Catraca 4 com QR.</item>
/// <item>Acionamento 1 = <b>2, "registro entrada"</b> — matriz de funções, SDK 6.0.2.0.</item>
/// <item>Tipo de leitor <b>8</b> por padrão (QR por letras). A Topdata fala em "serial barcode",
/// que no SDK pode ser o <b>5</b> — o ensaio decide. <c>A_CONFIRMAR</c>.</item>
/// <item><b>Relé 2 (urna) desligado.</b> A função que faz a urna recolher o cartão não está
/// documentada. Na bancada, o leitor da urna lê e o sistema decide; o recolhimento é o
/// fluxo da Fase 3, ainda não construído.</item>
/// <item>Mudança automática on-line/off-line <b>desligada</b>: na bancada, a catraca só
/// funciona com o sistema rodando, para que tudo que acontece passe por ele.</item>
/// </list>
/// </remarks>
public static class ConfiguracaoDeBancada
{
    public static DeviceConfiguration TopFit4(byte tipoDeLeitor = 8, bool leitorDaUrna = true) => new()
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
        PerfilFisico = new GatePhysicalProfile(SentidoInvertido: false),
    };
}

/// <summary>
/// O laço de verdade contra catracas de verdade, decidindo pela base local, mostrando tudo.
/// </summary>
/// <remarks>
/// <para>
/// É o ensaio que responde se um ingresso da nossa base faz uma TopFit 4 girar. Usa o
/// mesmo laço, a mesma máquina de estados e a mesma base da operação — não é uma
/// simulação do fluxo, é o fluxo com a tela ligada.
/// </para>
/// <para>
/// <b>Mostra o código lido inteiro.</b> A regra do projeto é nunca registrar o número
/// completo de uma credencial; aqui ela é suspensa de propósito e só na tela, porque
/// comparar o número que a catraca entrega com o que o balcão lê é o objetivo do ensaio.
/// Nada disto vai para arquivo. <b>Usar só com cartões e ingressos de teste.</b>
/// </para>
/// </remarks>
public sealed class SessaoDeBancada
{
    private readonly DeviceGroupLoop _laco;
    private readonly DecisorDeIngresso _decisor;
    private readonly Action<string> _escrever;
    private readonly Func<DateTimeOffset> _relogio;

    public SessaoDeBancada(
        ITopdataInnerAdapter adapter,
        IEnumerable<int> inners,
        DeviceConfiguration configuracao,
        DecisorDeIngresso decisor,
        Action<string> escrever,
        Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(inners);
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(decisor);
        ArgumentNullException.ThrowIfNull(escrever);

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            throw new ArgumentException(
                "Configuração de bancada inválida: " + string.Join(" ", problemas),
                nameof(configuracao));
        }

        _decisor = decisor;
        _escrever = escrever;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);

        var bomba = new DevicePump(
            adapter,
            _relogio,
            decidir: DecidirMostrando,
            aoReceberEvento: ReceberMostrando);

        _laco = new DeviceGroupLoop(
            adapter,
            inners.Select(i => new DeviceSlot(i, configuracao, _relogio)),
            new Watchdog(TimeSpan.FromSeconds(30), _relogio),
            bomba);
    }

    /// <summary>Os equipamentos desta sessão.</summary>
    public IReadOnlyList<DeviceSlot> Dispositivos => _laco.Dispositivos;

    public AdapterResult Iniciar(int porta) => _laco.Iniciar(porta);

    /// <summary>Uma volta pelos equipamentos. Mostra tudo menos o silêncio.</summary>
    public void UmaVolta()
    {
        foreach (var (inner, acao) in _laco.UmaVolta())
        {
            if (!string.Equals(acao, "sem eventos", StringComparison.Ordinal))
            {
                _escrever($"{Hora()} inner {inner}: {acao}");
            }
        }
    }

    /// <summary>Gira até ser mandado parar.</summary>
    public void Executar(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            UmaVolta();
        }
    }

    public AdapterResult Encerrar() => _laco.Encerrar();

    /// <summary>Linha de resumo para o fim do ensaio.</summary>
    public string Resumo() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"giros confirmados: {_decisor.PassagensConfirmadas} · autorizados sem giro: {_decisor.AutorizacoesSemGiro}");

    private Decision DecidirMostrando(DeviceEvent evento)
    {
        var decisao = _decisor.Decidir(evento);
        var detalhe = decisao.Trace.Count > 0 ? decisao.Trace[0].Detail : null;

        _escrever(string.Create(
            CultureInfo.InvariantCulture,
            $"{Hora()} {evento.Key.DeviceId}: {(decisao.ShouldRelease ? "LIBERADO" : "NEGADO  ")} {decisao.Reason} ({detalhe}) em {decisao.Elapsed.TotalMilliseconds:F0} ms"));

        return decisao;
    }

    private void ReceberMostrando(DeviceEvent evento)
    {
        // A informação que o ensaio existe para colher: de qual leitor veio a leitura e
        // exatamente o que ele entregou. Colchetes para que espaço nas pontas apareça.
        var codigo = evento.RawCardData is null ? "" : $" código=[{evento.RawCardData}] ({evento.RawCardData.Length} caracteres)";
        _escrever($"{Hora()} {evento.Key.DeviceId}: origem {evento.Origin}{codigo}");

        _decisor.AoReceberEvento(evento);
    }

    private string Hora() => _relogio().ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
