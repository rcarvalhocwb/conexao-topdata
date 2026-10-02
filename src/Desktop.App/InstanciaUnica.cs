using System.Threading;

namespace Desktop.App;

/// <summary>
/// Um painel por sessão do Windows. Abrir o atalho com o painel já na bandeja traz a janela
/// dele, em vez de abrir outro (e outro ícone perto do relógio).
/// </summary>
internal sealed class InstanciaUnica : IDisposable
{
    private const string NomeDoMutex = @"Local\RayzerXAcess.Painel";
    private const string NomeDoSinal = @"Local\RayzerXAcess.Painel.Mostrar";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _sinal;
    private RegisteredWaitHandle? _espera;

    private InstanciaUnica(Mutex mutex, EventWaitHandle sinal)
    {
        _mutex = mutex;
        _sinal = sinal;
    }

    /// <summary>
    /// Tenta ser a primeira instância. Se já houver outra, pede a ela que se mostre e
    /// devolve nulo: quem chamou deve sair.
    /// </summary>
    public static InstanciaUnica? Tentar()
    {
        var mutex = new Mutex(initiallyOwned: true, NomeDoMutex, out var primeira);
        var sinal = new EventWaitHandle(false, EventResetMode.AutoReset, NomeDoSinal);

        if (!primeira)
        {
            sinal.Set();
            sinal.Dispose();
            mutex.Dispose();
            return null;
        }

        return new InstanciaUnica(mutex, sinal);
    }

    /// <summary>Chamado (numa thread do pool) toda vez que outra instância pede para mostrar.</summary>
    public void AoPedirParaMostrar(Action acao)
    {
        ArgumentNullException.ThrowIfNull(acao);
        _espera = ThreadPool.RegisterWaitForSingleObject(_sinal, (_, _) => acao(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _espera?.Unregister(null);
        _sinal.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
