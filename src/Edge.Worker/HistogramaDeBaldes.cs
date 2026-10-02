using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Edge.Worker;

/// <summary>
/// Histograma de latência em baldes fixos, log-espaçados, que se somam entre minutos e catracas
/// sem guardar amostras (§4.2 dos docs/36-anexos/02). Determinístico, para teste.
/// </summary>
/// <remarks>
/// Baldes: 1, 2, 5, 10, 20, 50, 100, 200, 500 ms para latências rápidas, e
/// 1, 1,5, 2, 3, 4, 6, 8, 12 s para as longas. Incrementar O(log n), somar O(1).
/// </remarks>
public sealed class HistogramaDeBaldes
{
    // Limites dos baldes em milissegundos: 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 1500, 2000, 3000, 4000, 6000, 8000, 12000
    private static readonly int[] Limites = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 1500, 2000, 3000, 4000, 6000, 8000, 12000];

    private readonly long[] _contadores = new long[Limites.Length + 1]; // +1 para "acima do último"

    /// <summary>Incrementa o balde apropriado para esta latência.</summary>
    public void Registrar(long milissegundos)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milissegundos);

        int indice = 0;
        for (; indice < Limites.Length; indice++)
        {
            if (milissegundos <= Limites[indice])
            {
                break;
            }
        }

        Interlocked.Increment(ref _contadores[indice]);
    }

    /// <summary>Total de amostras.</summary>
    public long Total()
    {
        long total = 0;
        for (int i = 0; i < _contadores.Length; i++)
        {
            total += Interlocked.Read(ref _contadores[i]);
        }

        return total;
    }

    /// <summary>Percentil aproximado por interpolação no balde (sem guardar amostras).</summary>
    /// <param name="percentil">0..100.</param>
    public double ObterPercentil(double percentil)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percentil, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentil, 100.0);

        long total = Total();
        if (total == 0)
        {
            return 0;
        }

        long meta = (long)Math.Ceiling(total * percentil / 100.0);
        long acumulado = 0;

        for (int i = 0; i < _contadores.Length; i++)
        {
            long antes = acumulado;
            acumulado += Interlocked.Read(ref _contadores[i]);

            if (acumulado >= meta)
            {
                if (i == 0)
                {
                    return Math.Min(Limites[0], meta); // primeiro balde: de 0 a Limites[0]
                }

                // Interpolação linear entre Limites[i-1] e Limites[i]
                double limite_ant = Limites[i - 1];
                double limite_novo = Limites[i];
                long amostra_neste = acumulado - antes;
                double fracao = amostra_neste > 0 ? (double)(meta - antes) / amostra_neste : 0;
                return limite_ant + fracao * (limite_novo - limite_ant);
            }
        }

        // Acima do último balde
        return Limites[^1];
    }

    /// <summary>Serializa para JSON como dicionário {balde_em_ms: contagem}.</summary>
    public string SerializarParaJson()
    {
        var dic = new Dictionary<string, long>(Limites.Length + 1);

        for (int i = 0; i < Limites.Length; i++)
        {
            dic[Limites[i].ToString(CultureInfo.InvariantCulture)] = Interlocked.Read(ref _contadores[i]);
        }

        dic[">12000"] = Interlocked.Read(ref _contadores[Limites.Length]);

        return JsonSerializer.Serialize(dic);
    }

    /// <summary>Desserializa de JSON.</summary>
    public static HistogramaDeBaldes DessSerializarDeJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var histograma = new HistogramaDeBaldes();
        using var doc = JsonDocument.Parse(json);

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.TryGetInt64(out var valor))
            {
                if (prop.Name == ">12000")
                {
                    Interlocked.Add(ref histograma._contadores[Limites.Length], valor);
                }
                else if (int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limite))
                {
                    int indice = Array.IndexOf(Limites, limite);
                    if (indice >= 0)
                    {
                        Interlocked.Add(ref histograma._contadores[indice], valor);
                    }
                }
            }
        }

        return histograma;
    }

    /// <summary>Soma este histograma com outro (thread-safe).</summary>
    public void Somar(HistogramaDeBaldes outro)
    {
        ArgumentNullException.ThrowIfNull(outro);

        for (int i = 0; i < _contadores.Length; i++)
        {
            long valor = Interlocked.Read(ref outro._contadores[i]);
            if (valor > 0)
            {
                Interlocked.Add(ref _contadores[i], valor);
            }
        }
    }

    /// <summary>Zera todos os contadores.</summary>
    public void Limpar()
    {
        for (int i = 0; i < _contadores.Length; i++)
        {
            Interlocked.Exchange(ref _contadores[i], 0);
        }
    }
}
