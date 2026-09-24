using Access.Application.Devices;
using Access.Domain.Devices;

namespace Simulator;

/// <summary>
/// Simulador de um parque de equipamentos Inner, no lugar da EasyInner.dll.
/// </summary>
/// <remarks>
/// <para>
/// Reproduz as características que definem a arquitetura: as chamadas são
/// <b>sequenciais</b>, o simulador rejeita uso concorrente do mesmo jeito que a DLL não
/// é thread-safe, e a porta é aberta uma única vez por instância.
/// </para>
/// <para>
/// O relógio é injetável para que os testes sejam determinísticos — nada aqui chama
/// <c>DateTimeOffset.UtcNow</c> por conta própria.
/// </para>
/// </remarks>
public sealed class InnerSimulator : ITopdataInnerAdapter
{
    private readonly Dictionary<int, SimulatedDevice> _dispositivos = [];
    private readonly Func<DateTimeOffset> _relogio;
    private readonly Lock _porteiro = new();

    private bool _portaAberta;
    private bool _descartado;
    private int _emUso;

    public InnerSimulator(Func<DateTimeOffset>? relogio = null)
    {
        var inicio = new DateTimeOffset(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);
        _relogio = relogio ?? (() => inicio);
    }

    /// <summary>Porta em que este simulador "escuta". Um worker, uma porta (ADR-0021).</summary>
    public int Porta { get; private set; }

    /// <summary>Quantas chamadas nativas foram feitas. Útil para detectar polling excessivo.</summary>
    public int ChamadasNativas { get; private set; }

    /// <summary>Acessa (criando se preciso) um equipamento simulado, para roteirizar.</summary>
    public SimulatedDevice Dispositivo(int inner)
    {
        if (!_dispositivos.TryGetValue(inner, out var dispositivo))
        {
            dispositivo = new SimulatedDevice(inner);
            _dispositivos[inner] = dispositivo;
        }

        return dispositivo;
    }

    public AdapterResult AbrirPorta(int porta)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);

        if (_portaAberta)
        {
            throw new InvalidOperationException(
                "AbrirPortaComunicacao deve ser chamada uma única vez por worker, antes do laço. " +
                "Ver manual, seção 2.1.2.");
        }

        _portaAberta = true;
        Porta = porta;
        return Medir(0);
    }

    public AdapterResult FecharPorta()
    {
        _portaAberta = false;
        return Medir(0);
    }

    public AdapterResult TestarConexao(int inner) => ComDispositivo(inner, d => d.Desconectado ? 1 : 0);

    public AdapterResult Ping(int inner) => ComDispositivo(inner, d => d.Desconectado ? 1 : 0);

    public (AdapterResult Resultado, FirmwareInfo? Firmware) LerFirmware(int inner)
    {
        var dispositivo = Dispositivo(inner);
        var resultado = ComDispositivo(inner, d => d.Desconectado ? 1 : 0);
        return resultado.IsOk ? (resultado, dispositivo.Firmware) : (resultado, null);
    }

    public (AdapterResult Resultado, DateTimeOffset? Relogio) LerRelogio(int inner)
    {
        var dispositivo = Dispositivo(inner);
        var resultado = ComDispositivo(inner, d => d.Desconectado ? 1 : 0);
        return resultado.IsOk
            ? (resultado, _relogio() + dispositivo.DesvioDeRelogio)
            : (resultado, null);
    }

    public AdapterResult EnviarConfiguracaoCompleta(int inner, DeviceConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            // O equipamento real recusaria com 128/129; aqui o erro é explícito para
            // que o defeito apareça no teste, não no portão.
            throw new ArgumentException(
                $"Configuração inválida: {string.Join(" | ", problemas)}",
                nameof(configuracao));
        }

        return ComDispositivo(inner, d =>
        {
            if (d.Desconectado)
            {
                return 1;
            }

            d.ConfiguracoesRecebidas.Add(configuracao);
            return 0;
        });
    }

    public AdapterResult ConfigurarEntradasOnline(int inner) =>
        ComDispositivo(inner, d =>
        {
            if (d.Desconectado)
            {
                return 1;
            }

            d.RegistrarReabilitacaoDoLeitor();
            return 0;
        });

    public AdapterResult EnviarMensagemPadrao(int inner, string mensagem)
    {
        ArgumentNullException.ThrowIfNull(mensagem);

        if (mensagem.Length > 32)
        {
            throw new ArgumentException(
                $"O display comporta 32 caracteres; recebida mensagem com {mensagem.Length} (manual, 4.6.3).",
                nameof(mensagem));
        }

        return ComDispositivo(inner, d => d.Desconectado ? 1 : 0);
    }

    public (AdapterResult Resultado, DeviceEvent? Evento) AguardarEvento(int inner, TimeSpan limite)
    {
        var dispositivo = Dispositivo(inner);

        using (Entrar())
        {
            ChamadasNativas++;

            if (dispositivo.RetornoForcado is { } forcado)
            {
                return (AdapterResult.FromNative(forcado, TimeSpan.Zero), null);
            }

            if (dispositivo.LacoTravado)
            {
                // Fica preso SEGURANDO o acesso à DLL — é essa posse que impede qualquer
                // outra thread de entrar, e é ela que o watchdog do worker precisa
                // detectar. Devolver na hora não simularia nada.
                if (!dispositivo.EsperarDestravar(limite))
                {
                    throw new TimeoutException(
                        $"Inner {inner}: ReceberDadosOnLine travado — é o watchdog que precisa agir.");
                }

                return (new AdapterResult(AdapterStatus.SemEventos, 0, limite), null);
            }

            if (dispositivo.Desconectado)
            {
                return (new AdapterResult(AdapterStatus.ErroDeComunicacao, 1, TimeSpan.Zero), null);
            }

            var roteirizado = dispositivo.ProximoEvento();
            if (roteirizado is null)
            {
                return (new AdapterResult(AdapterStatus.SemEventos, 0, limite), null);
            }

            var recebidoEm = _relogio() + roteirizado.AtrasoAntes;

            var evento = DeviceEvent.Create(
                new DeviceEventKey(
                    $"inner-{inner}",
                    dispositivo.BootId,
                    dispositivo.ProximaSequencia()),
                roteirizado.Origem,
                recebidoEm,
                $"sim-{inner}-{Guid.CreateVersion7(recebidoEm)}",
                roteirizado.Complemento,
                roteirizado.Cartao,
                deviceTime: recebidoEm + dispositivo.DesvioDeRelogio);

            return (new AdapterResult(AdapterStatus.Ok, 0, TimeSpan.Zero), evento);
        }
    }

    public AdapterResult LiberarGiro(int inner, GateDirection direcao) =>
        ComDispositivo(inner, d =>
        {
            if (d.Desconectado)
            {
                return 1;
            }

            d.RegistrarLiberacao(direcao);
            return 0;
        });

    /// <summary>
    /// Aciona o relé da urna. Sem tempo: ele vem da configuração do equipamento.
    /// </summary>
    /// <remarks>
    /// A versão anterior recebia um <c>TimeSpan</c> e recusava acima de 50 s, seguindo o que
    /// o manual sugeria. O SDK mostrou que <c>AcionarRele2</c> recebe só o Inner — o limite
    /// de 50 s pertence a <c>ConfigurarAcionamento2</c>, e já é validado lá.
    /// </remarks>
    public AdapterResult AcionarReleDaUrna(int inner)
    {
        return ComDispositivo(inner, d =>
        {
            if (d.Desconectado)
            {
                return 1;
            }

            if (d.UrnaCheia)
            {
                // O equipamento sinaliza por evento, não por retorno de comando.
                d.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.UrnaCheia)));
                return 0;
            }

            d.RegistrarAcionamentoDaUrna();
            return 0;
        });
    }

    public (AdapterResult Resultado, Bilhete? Bilhete) ColetarBilhete(int inner)
    {
        var dispositivo = Dispositivo(inner);

        using (Entrar())
        {
            ChamadasNativas++;

            if (dispositivo.RetornoForcado is { } forcado)
            {
                return (AdapterResult.FromNative(forcado, TimeSpan.Zero), null);
            }

            if (dispositivo.Desconectado)
            {
                return (new AdapterResult(AdapterStatus.ErroDeComunicacao, 1, TimeSpan.Zero), null);
            }

            var bilhete = dispositivo.ProximoBilhete();

            // Devolver o bilhete já o REMOVE da memória — é assim na DLL, e é por isso
            // que o commit local precisa vir antes da próxima coleta (risco R-68).
            return bilhete is null
                ? (new AdapterResult(AdapterStatus.SemBilhetes, 0, TimeSpan.Zero), null)
                : (new AdapterResult(AdapterStatus.Ok, 0, TimeSpan.Zero), bilhete);
        }
    }

    public AdapterResult ExibirMensagemTemporaria(int inner, string mensagem, TimeSpan duracao)
    {
        ArgumentNullException.ThrowIfNull(mensagem);

        if (mensagem.Length > 32)
        {
            throw new ArgumentException(
                $"O display comporta 32 caracteres; recebida mensagem com {mensagem.Length} (manual, 4.6.3).",
                nameof(mensagem));
        }

        return ComDispositivo(inner, d => d.Desconectado ? 1 : 0);
    }

    public void Dispose()
    {
        _descartado = true;
        _portaAberta = false;

        foreach (var dispositivo in _dispositivos.Values)
        {
            dispositivo.Dispose();
        }

        _dispositivos.Clear();
    }

    private AdapterResult ComDispositivo(int inner, Func<SimulatedDevice, int> acao)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);

        if (!_portaAberta)
        {
            throw new InvalidOperationException(
                "A porta de comunicação precisa estar aberta antes de qualquer comando.");
        }

        var dispositivo = Dispositivo(inner);

        using (Entrar())
        {
            ChamadasNativas++;
            return dispositivo.RetornoForcado is { } forcado
                ? AdapterResult.FromNative(forcado, TimeSpan.Zero)
                : Medir(acao(dispositivo));
        }
    }

    private static AdapterResult Medir(int retorno) => AdapterResult.FromNative(retorno, TimeSpan.Zero);

    /// <summary>
    /// Impede uso concorrente, como a DLL real exige. Duas threads aqui é um defeito de
    /// quem chama, e ele precisa aparecer no teste.
    /// </summary>
    private Guarda Entrar()
    {
        if (Interlocked.CompareExchange(ref _emUso, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Chamada concorrente ao adapter. A EasyInner.dll não é thread-safe: " +
                "uma única thread por instância. Ver manual, seção 6.2.");
        }

        return new Guarda(this);
    }

    private readonly struct Guarda(InnerSimulator dono) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref dono._emUso, 0);
    }
}
