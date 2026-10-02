using Access.Domain.Devices;

namespace Simulator;

/// <summary>Uma leitura para o simulador entregar.</summary>
/// <param name="Inner">Catraca.</param>
/// <param name="Codigo">O que o leitor "leu".</param>
/// <param name="NaUrna">Leitor 2 (fenda da urna) em vez do leitor 1 (frente).</param>
/// <param name="Girar">Se liberada, a pessoa gira a catraca.</param>
public sealed record LeituraParaSimular(int Inner, string Codigo, bool NaUrna, bool Girar);

/// <summary>
/// Leva leituras pedidas de fora (o painel, no modo simulação) para o simulador, e gira a
/// catraca simulada quando ela foi liberada e a leitura pediu giro.
/// </summary>
/// <remarks>
/// Roda no mesmo laço do worker, entre uma volta e outra: o simulador não é thread-safe e
/// não precisa ser. O giro só é roteirizado depois que a catraca pediu liberação — como na
/// vida real, ninguém gira catraca travada.
/// </remarks>
public sealed class ConducaoDeLeituras
{
    private readonly InnerSimulator _simulador;
    private readonly Func<IReadOnlyCollection<int>, IReadOnlyList<LeituraParaSimular>> _retirar;
    private readonly IReadOnlyCollection<int> _inners;
    private readonly TimeSpan _intervalo;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly List<(int Inner, int LiberacoesAntes, DateTimeOffset Desde)> _esperandoLiberacao = [];
    private DateTimeOffset _ultimaBusca = DateTimeOffset.MinValue;

    public ConducaoDeLeituras(
        InnerSimulator simulador,
        Func<IReadOnlyCollection<int>, IReadOnlyList<LeituraParaSimular>> retirar,
        IReadOnlyCollection<int> inners,
        TimeSpan? intervalo = null,
        Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(simulador);
        ArgumentNullException.ThrowIfNull(retirar);
        ArgumentNullException.ThrowIfNull(inners);

        _simulador = simulador;
        _retirar = retirar;
        _inners = inners;
        _intervalo = intervalo ?? TimeSpan.FromMilliseconds(200);
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Quanto tempo esperar a liberação antes de desistir do giro.</summary>
    public static TimeSpan EsperaPelaLiberacao { get; } = TimeSpan.FromSeconds(10);

    /// <summary>Uma passada: busca leituras novas e gira o que foi liberado.</summary>
    /// <returns>Quantas leituras novas foram entregues ao simulador.</returns>
    public int UmaVolta()
    {
        var agora = _relogio();
        GirarOQueFoiLiberado(agora);

        if (agora - _ultimaBusca < _intervalo)
        {
            return 0;
        }

        _ultimaBusca = agora;
        var leituras = _retirar(_inners);

        foreach (var leitura in leituras)
        {
            var dispositivo = _simulador.Dispositivo(leitura.Inner);

            if (leitura.Girar)
            {
                _esperandoLiberacao.Add((leitura.Inner, Liberacoes(dispositivo), agora));
            }

            dispositivo.Roteirizar(new ScriptedEvent(
                EventOrigin.From(leitura.NaUrna ? KnownEventOrigin.Leitor2 : KnownEventOrigin.Leitor1),
                leitura.Codigo));
        }

        return leituras.Count;
    }

    private void GirarOQueFoiLiberado(DateTimeOffset agora)
    {
        for (var i = _esperandoLiberacao.Count - 1; i >= 0; i--)
        {
            var (inner, antes, desde) = _esperandoLiberacao[i];
            var dispositivo = _simulador.Dispositivo(inner);

            if (Liberacoes(dispositivo) > antes)
            {
                dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
                _esperandoLiberacao.RemoveAt(i);
            }
            else if (agora - desde > EsperaPelaLiberacao)
            {
                // Foi negada: não há giro a simular.
                _esperandoLiberacao.RemoveAt(i);
            }
        }
    }

    private static int Liberacoes(SimulatedDevice dispositivo) => dispositivo.LiberacoesPedidas.Values.Sum();
}
