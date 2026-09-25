using System.Globalization;
using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;

// Uso: CrashProbe <caminhoDoBanco> <quantidadeDeEventos>
// Grava os eventos, imprime "PRONTO" e espera ser morto.
//
// Uso: CrashProbe ... --tagarela <marcador>
// Escreve muito mais do que cabe no buffer de um pipe e, se não travar, cria o arquivo
// marcador. É como o supervisor prova que lê a saída do worker enquanto ele roda.

var tagarela = Array.IndexOf(args, "--tagarela");
if (tagarela >= 0 && tagarela + 1 < args.Length)
{
    var linha = new string('x', 200);
    for (var n = 0; n < 20_000; n++)
    {
        Console.WriteLine($"{n} {linha}");
        Console.Error.WriteLine($"{n} {linha}");
    }

    File.WriteAllText(args[tagarela + 1], "ok");
    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
    return 0;
}

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
