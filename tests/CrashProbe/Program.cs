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

// Uso: CrashProbe --bilhetes <banco> <memoria.json> <quantidade> [--cair-antes-de-gravar N |
// --cair-depois-de-gravar N] [--remove-ao-devolver]
// Coleta bilhetes de uma catraca simulada pelo laço de verdade e cai no ponto pedido (Etapa A.9).
if (args.Length > 0 && args[0] == "--bilhetes")
{
    return await CrashProbe.ColetaDaCobaia.Executar(args[1..]).ConfigureAwait(false);
}

// Uso: CrashProbe --ingressos <banco> <quantidade>
// O caminho real de produção (achado E9-1 do docs/41): ingere N ingressos de uso único, e para cada
// um faz TentarUsar e ConfirmarPassagemFisica, com o espelho da nuvem ligado (outbox), imprimindo
// "OK n" depois de cada passagem gravada. O teste mata o processo no meio, com SIGKILL.
if (args.Length > 0 && args[0] == "--ingressos")
{
    var banco = args[1];
    var total = int.Parse(args[2], CultureInfo.InvariantCulture);
    var fabricaDosIngressos = new SqliteConnectionFactory(banco);
    new Migrator(fabricaDosIngressos).Aplicar();
    var repositorio = new RepositorioDeIngressos(
        fabricaDosIngressos, new EspelhoDeTentativas("painel-tentativas", TimeSpan.Zero));
    var inicio = DateTimeOffset.UtcNow;
    repositorio.RegistrarProvedor(new Access.Domain.Ticketing.ProvedorDeIngresso("zet", "Zet", "raw", ""), inicio);
    repositorio.Ingerir(
        [.. Enumerable.Range(1, total).Select(i =>
            new Access.Domain.Ticketing.IngressoRecebido("zet", $"T{i}", $"{i:D10}", $"{i:D10}"))],
        inicio.AddMinutes(-1));

    Console.WriteLine("INGERIDO");
    Console.Out.Flush();

    for (var i = 1; i <= total; i++)
    {
        var agoraDoGiro = DateTimeOffset.UtcNow;
        var (resultado, tentativa) = repositorio.TentarUsar($"{i:D10}", "portao-1", "inner-1", agoraDoGiro);
        if (!resultado.Liberou)
        {
            Console.Error.WriteLine($"ingresso {i} negado: {resultado.Motivo}");
            return 3;
        }

        repositorio.ConfirmarPassagemFisica(tentativa, agoraDoGiro);
        Console.WriteLine($"OK {i}");
        Console.Out.Flush();
    }

    Console.WriteLine("PRONTO");
    Console.Out.Flush();
    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
    return 0;
}

// Uso: CrashProbe --esperar (em qualquer posição: o supervisor põe --porta e --inners antes)
// Só fica de pé, calado, até ser morto. É o "worker" dos testes de contenção (Job Object) e da
// faxina de órfãos do supervisor (docs/29, defeito de 01/10).
if (Array.IndexOf(args, "--esperar") >= 0)
{
    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
    return 0;
}

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
