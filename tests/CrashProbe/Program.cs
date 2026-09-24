using System.Globalization;
using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;

// Uso: CrashProbe <caminhoDoBanco> <quantidadeDeEventos>
// Grava os eventos, imprime "PRONTO" e espera ser morto.

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: CrashProbe <banco> <quantidade>");
    return 2;
}

var caminho = args[0];
var quantidade = int.Parse(args[1], CultureInfo.InvariantCulture);

var fabrica = new SqliteConnectionFactory(caminho);
new Migrator(fabrica).Aplicar();
var diario = new AccessJournal(fabrica);

var agora = DateTimeOffset.UtcNow;

for (var i = 1; i <= quantidade; i++)
{
    var evento = DeviceEvent.Create(
        new DeviceEventKey("catraca-08", "boot-crash", i),
        EventOrigin.From(KnownEventOrigin.Leitor1),
        agora.AddMilliseconds(i),
        $"crash-{i}",
        rawCardData: $"000{i:D7}");

    var decisao = new Decision(
        DecisionOutcome.Allowed,
        ReasonCodes.Autorizado,
        DegradationTier.T1SemInternet,
        TimeSpan.FromMilliseconds(12),
        []);

    diario.Registrar(
        evento,
        decisao,
        new IssuedCommand("LiberarCatracaEntrada", null, $"cmd-crash-{i}"),
        [new OutboxItem("acesso", evento.EventId.ToString(), "{}", 5, "rest", $"out-crash-{i}")]);
}

Console.WriteLine("PRONTO");
Console.Out.Flush();

// Fica vivo até levar SIGKILL. Sem finalizador, sem flush extra, sem despedida:
// é exatamente o corte abrupto que o teste quer provocar.
await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
return 0;
