using System.Globalization;
using System.Text.Json;
using Access.Application.Devices;
using Access.Domain.Credentials;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Edge.Worker;
using Simulator;

namespace CrashProbe;

/// <summary>
/// Cobaia da coleta de bilhetes (Etapa A.9 do docs/35, CHAOS-REC-01 simulado): o laço de
/// verdade (<see cref="DevicePump"/>) coleta de uma catraca simulada e grava na base; num ponto
/// escolhido, imprime "PRONTO" e espera o SIGKILL.
/// </summary>
/// <remarks>
/// <para>
/// A catraca é outro equipamento: ela não morre com o worker. Por isso a memória de bilhetes do
/// simulador é guardada num arquivo depois de cada passo do laço, e a próxima execução a
/// restaura — o que a catraca tinha, inclusive o bilhete devolvido e ainda não confirmado.
/// </para>
/// <para>
/// Uso: <c>CrashProbe --bilhetes &lt;banco&gt; &lt;memoria.json&gt; &lt;quantidade&gt;
/// [--cair-antes-de-gravar N | --cair-depois-de-gravar N] [--remove-ao-devolver]</c>.
/// Sem ponto de queda, coleta até a memória esvaziar, imprime "PRONTO" e espera.
/// </para>
/// </remarks>
internal static class ColetaDaCobaia
{
    /// <summary>Chave sintética da impressão, conhecida pelo teste para conferir os códigos.</summary>
    public static readonly byte[] ChaveDeTeste = [.. Enumerable.Repeat((byte)7, 32)];

    public static async Task<int> Executar(string[] args)
    {
        if (args.Length < 3)
        {
            await Console.Error.WriteLineAsync("Uso: CrashProbe --bilhetes <banco> <memoria.json> <quantidade> [...]").ConfigureAwait(false);
            return 2;
        }

        var banco = args[0];
        var arquivoDaMemoria = args[1];
        var quantidade = int.Parse(args[2], CultureInfo.InvariantCulture);
        var cairAntes = Valor(args, "--cair-antes-de-gravar");
        var cairDepois = Valor(args, "--cair-depois-de-gravar");

        var fabrica = new SqliteConnectionFactory(banco);
        new Migrator(fabrica).Aplicar();
        var gravador = new GravadorQueCai(new BilhetesColetados(fabrica, new ImpressaoDeCodigo(ChaveDeTeste)), cairAntes, cairDepois);

        using var simulador = new InnerSimulator(() => DateTimeOffset.UtcNow);
        var catracaSimulada = simulador.Dispositivo(1);
        catracaSimulada.ConfirmaNaProximaColeta = Array.IndexOf(args, "--remove-ao-devolver") < 0;

        if (File.Exists(arquivoDaMemoria))
        {
            catracaSimulada.RestaurarBilhetes(Ler(arquivoDaMemoria));
        }
        else
        {
            var marcado = new DateTimeOffset(2026, 12, 6, 18, 0, 0, TimeSpan.FromHours(-3));
            catracaSimulada.ComBilhetes([.. Enumerable.Range(1, quantidade)
                .Select(i => new Bilhete(10, marcado.AddMinutes(i), Codigo(i)))]);
            Salvar(arquivoDaMemoria, catracaSimulada.ExportarBilhetes());
        }

        simulador.AbrirPorta(3570);

        var concluido = false;
        var bomba = new DevicePump(
            simulador,
            aoConcluirComando: (_, _, _) => concluido = true,
            gravadorDeBilhetes: gravador);
        var catraca = new DeviceSlot(1, PadroesDeFabrica.TopFit4);

        for (var i = 0; i < 50 && catraca.Maquina.Current is not DeviceState.Polling; i++)
        {
            Passo(bomba, catraca, catracaSimulada, arquivoDaMemoria);
        }

        var (comando, problemas) = ComandoDeCatraca.Criar(1, TipoDeComando.ColetarBilhetes, "cobaia", DateTimeOffset.UtcNow);
        if (comando is null)
        {
            await Console.Error.WriteLineAsync(string.Join(" ", problemas)).ConfigureAwait(false);
            return 2;
        }

        catraca.Enfileirar(comando);
        for (var i = 0; i < (quantidade * 4) + 50 && !concluido; i++)
        {
            Passo(bomba, catraca, catracaSimulada, arquivoDaMemoria);
        }

        Console.WriteLine(concluido ? "PRONTO" : "NAO CONCLUIU");
        Console.Out.Flush();
        await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
        return 0;
    }

    public static string Codigo(int i) => string.Create(CultureInfo.InvariantCulture, $"9999{i:D10}");

    // A catraca guarda a memória dela a cada passo do laço: é o que sobrevive à queda do worker.
    private static void Passo(DevicePump bomba, DeviceSlot catraca, SimulatedDevice catracaSimulada, string arquivo)
    {
        bomba.Passo(catraca, TimeSpan.Zero);
        Salvar(arquivo, catracaSimulada.ExportarBilhetes());
    }

    private static int? Valor(string[] args, string nome)
    {
        var indice = Array.IndexOf(args, nome);
        return indice >= 0 && indice + 1 < args.Length ? int.Parse(args[indice + 1], CultureInfo.InvariantCulture) : null;
    }

    private static MemoriaDeBilhetes Ler(string arquivo) =>
        JsonSerializer.Deserialize<MemoriaDeBilhetes>(File.ReadAllText(arquivo))
            ?? throw new InvalidDataException("memória da catraca simulada ilegível");

    // Troca atômica: a queda nunca deixa a "catraca" com meia memória.
    private static void Salvar(string arquivo, MemoriaDeBilhetes memoria)
    {
        var temporario = arquivo + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(memoria));
        File.Move(temporario, arquivo, overwrite: true);
    }

    /// <summary>A base de verdade, com o ponto de queda: antes ou depois da N-ésima gravação.</summary>
    private sealed class GravadorQueCai(IGravadorDeBilhetes base_, int? cairAntes, int? cairDepois) : IGravadorDeBilhetes
    {
        private int _chamadas;

        public DesfechoDaGravacaoDoBilhete Gravar(BilheteColetado bilhete)
        {
            _chamadas++;
            if (_chamadas == cairAntes)
            {
                Esperar();
            }

            var desfecho = base_.Gravar(bilhete);

            if (_chamadas == cairDepois)
            {
                Esperar();
            }

            return desfecho;
        }

        // O bilhete saiu da catraca (antes) ou está na base e a catraca ainda não foi chamada de
        // novo (depois). Fica aqui até o SIGKILL.
        private static void Esperar()
        {
            Console.WriteLine("PRONTO");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
