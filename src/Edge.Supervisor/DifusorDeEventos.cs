using System.Threading.Channels;
using Contracts.Edge.V1;

namespace Edge.Supervisor;

/// <summary>
/// Entrega cada evento a <b>todos</b> os painéis conectados.
/// </summary>
/// <remarks>
/// <para>
/// Um <see cref="Channel{T}"/> sozinho entrega cada item a um leitor só: com dois painéis
/// abertos (portaria e supervisão), cada um veria metade dos acessos. Aqui cada assinante
/// tem o seu canal.
/// </para>
/// <para>
/// Os últimos eventos ficam guardados e são entregues a quem acabou de se conectar: o
/// painel que abre no meio do evento já mostra os acessos recentes, e um evento publicado
/// no instante em que o painel está se conectando não se perde.
/// </para>
/// <para>
/// Painel lento não segura ninguém: o canal dele é limitado e descarta o mais antigo.
/// </para>
/// </remarks>
public sealed class DifusorDeEventos
{
    /// <summary>Quantos eventos um painel recém-conectado recebe de uma vez.</summary>
    public const int Guardados = 50;

    private const int CapacidadePorAssinante = 1_000;

    private readonly Lock _trava = new();
    private readonly List<Channel<EventoDeAcesso>> _assinantes = [];
    private readonly Queue<EventoDeAcesso> _recentes = new();

    public DifusorDeEventos() => Writer = new Escritor(this);

    /// <summary>Por onde se publica. Nunca bloqueia e nunca recusa.</summary>
    public ChannelWriter<EventoDeAcesso> Writer { get; }

    /// <summary>Quantos painéis estão ouvindo.</summary>
    public int Assinantes
    {
        get
        {
            lock (_trava)
            {
                return _assinantes.Count;
            }
        }
    }

    /// <summary>Passa a receber os eventos. Descarte o retorno para parar.</summary>
    public Assinatura Assinar()
    {
        var canal = Channel.CreateBounded<EventoDeAcesso>(new BoundedChannelOptions(CapacidadePorAssinante)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_trava)
        {
            foreach (var evento in _recentes)
            {
                canal.Writer.TryWrite(evento);
            }

            _assinantes.Add(canal);
        }

        return new Assinatura(this, canal);
    }

    private void Publicar(EventoDeAcesso evento)
    {
        lock (_trava)
        {
            _recentes.Enqueue(evento);
            while (_recentes.Count > Guardados)
            {
                _recentes.Dequeue();
            }

            foreach (var canal in _assinantes)
            {
                canal.Writer.TryWrite(evento);
            }
        }
    }

    private void Remover(Channel<EventoDeAcesso> canal)
    {
        lock (_trava)
        {
            _assinantes.Remove(canal);
        }

        canal.Writer.TryComplete();
    }

    /// <summary>Um painel ouvindo.</summary>
    public sealed class Assinatura : IDisposable
    {
        private readonly DifusorDeEventos _dono;
        private readonly Channel<EventoDeAcesso> _canal;

        internal Assinatura(DifusorDeEventos dono, Channel<EventoDeAcesso> canal)
        {
            _dono = dono;
            _canal = canal;
        }

        public ChannelReader<EventoDeAcesso> Leitor => _canal.Reader;

        public void Dispose() => _dono.Remover(_canal);
    }

    private sealed class Escritor(DifusorDeEventos dono) : ChannelWriter<EventoDeAcesso>
    {
        public override bool TryWrite(EventoDeAcesso item)
        {
            ArgumentNullException.ThrowIfNull(item);
            dono.Publicar(item);
            return true;
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
    }
}
