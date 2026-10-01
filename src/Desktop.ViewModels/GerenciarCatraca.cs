using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>Um comando do histórico, pronto para a tela.</summary>
public sealed record LinhaDeComando(
    string Hora,
    int Inner,
    string Comando,
    string Detalhe,
    string Operador,
    string Situacao,
    Sinal Sinal,
    string Resultado)
{
    public static LinhaDeComando De(ComandoRegistrado c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var (situacao, sinal) = Textos.SituacaoDoComando(c.Situacao);

        return new LinhaDeComando(
            c.PedidoEm is null ? "—" : FusoDoEvento.NoEvento(c.PedidoEm.ToDateTimeOffset()).ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture),
            c.Inner,
            Textos.NomeDoComando(c.Tipo),
            c.Tipo switch
            {
                TipoDeComando.LiberacaoManual or TipoDeComando.LiberarSaida or TipoDeComando.LiberarDoisSentidos => c.Motivo,
                TipoDeComando.MensagemTemporaria => $"“{c.Texto}”",
                _ => string.Empty,
            },
            c.Operador,
            situacao,
            sinal,
            string.IsNullOrEmpty(c.Resultado) ? "—" : c.Resultado);
    }
}

/// <summary>
/// Gerenciar catraca (fase 4b/4c): o que o operador pode pedir a uma catraca, e o histórico
/// do que foi pedido.
/// </summary>
/// <remarks>
/// Cada pedido vai ao serviço, vira auditoria na base local e é executado pelo programa da
/// catraca quando ela estiver livre. A tela mostra o desfecho quando ele chega — nunca
/// presume que deu certo.
/// </remarks>
public sealed class GerenciarCatracaViewModel : TelaBase
{
    private IReadOnlyList<LinhaDeCatraca> _catracas = [];
    private LinhaDeCatraca? _selecionada;
    private int _catraca;
    private string _operador = string.Empty;
    private string _mensagemTemporaria = string.Empty;
    private int _duracao = 10;
    private string _motivo = string.Empty;
    private IReadOnlyList<LinhaDeComando> _historico = [];

    public GerenciarCatracaViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null, TimeSpan? esperaPeloResultado = null)
        : base(cliente, relogio)
    {
        EsperaPeloResultado = esperaPeloResultado ?? TimeSpan.FromSeconds(1.5);
        AcertarRelogio = new ComandoAssincrono(() => PedirAsync(TipoDeComando.AcertarRelogio), TemCatracaEOperador);
        EnviarMensagem = new ComandoAssincrono(
            () => PedirAsync(TipoDeComando.MensagemTemporaria),
            () => TemCatracaEOperador() && MensagemTemporaria.Trim().Length is > 0 and <= 32);
        LiberarManualmente = new ComandoAssincrono(
            () => PedirAsync(TipoDeComando.LiberacaoManual),
            () => TemCatracaEOperador() && Motivo.Trim().Length >= 5);
        RefazerConexao = new ComandoAssincrono(() => PedirAsync(TipoDeComando.ReiniciarConexao), TemCatracaEOperador);
    }

    public override string Titulo => "Gerenciar catraca";

    /// <summary>Quanto esperar o programa da catraca executar antes de buscar o desfecho.</summary>
    public TimeSpan EsperaPeloResultado { get; }

    public ComandoAssincrono AcertarRelogio { get; }

    public ComandoAssincrono EnviarMensagem { get; }

    public ComandoAssincrono LiberarManualmente { get; }

    public ComandoAssincrono RefazerConexao { get; }

    public IReadOnlyList<LinhaDeCatraca> Catracas { get => _catracas; private set => Definir(ref _catracas, value); }

    /// <summary>Número da catraca escolhida; 0 enquanto nenhuma foi escolhida.</summary>
    public int Catraca
    {
        get => _catraca;
        set
        {
            if (Definir(ref _catraca, value))
            {
                Selecionada = Catracas.FirstOrDefault(c => c.Inner == value);
                Historico = [];
                Reavaliar();
            }
        }
    }

    /// <summary>A situação da catraca escolhida.</summary>
    public LinhaDeCatraca? Selecionada { get => _selecionada; private set => Definir(ref _selecionada, value); }

    /// <summary>Quem está pedindo. Não há login: fica registrado como foi digitado.</summary>
    public string Operador
    {
        get => _operador;
        set
        {
            if (Definir(ref _operador, value ?? string.Empty))
            {
                Reavaliar();
            }
        }
    }

    public string MensagemTemporaria
    {
        get => _mensagemTemporaria;
        set
        {
            if (Definir(ref _mensagemTemporaria, value ?? string.Empty))
            {
                Reavaliar();
            }
        }
    }

    /// <summary>Segundos que a mensagem fica no display (1 a 60).</summary>
    public int DuracaoDaMensagem { get => _duracao; set => Definir(ref _duracao, value); }

    /// <summary>Por que liberar sem ingresso. Obrigatório.</summary>
    public string Motivo
    {
        get => _motivo;
        set
        {
            if (Definir(ref _motivo, value ?? string.Empty))
            {
                Reavaliar();
            }
        }
    }

    public IReadOnlyList<LinhaDeComando> Historico { get => _historico; private set => Definir(ref _historico, value); }

    /// <summary>
    /// O que a catraca talvez faça, mas o sistema ainda não faz: aparece desabilitado, com o
    /// motivo, para ninguém achar que existe.
    /// </summary>
    public IReadOnlyList<ParDeTexto> AguardandoConfirmacao { get; } =
    [
        new("Bip curto e longo", "Aguardando bancada: documentado no manual (4.6.2), ainda não ensaiado (INT-UX-03). O serviço recusa até lá."),
        new("Acionar relés avulsos", "Aguardando confirmação da Topdata: o que cada relé faz na TopFit 4."),
        new("Recolher cartão na urna", "Aguardando confirmação da Topdata: a função do relé 2 não está documentada (docs/21 §8)."),
        new("Liberar nos dois sentidos / trocar o sentido", "Aguardando decisão D5 do dono do produto (evacuação) e a bancada (HIL-DIR-07). O serviço recusa até lá."),
    ];

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            var lista = await Cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento);
            var agora = Relogio();
            Catracas = [.. lista.Equipamentos.Select(e => PainelAoVivoViewModel.Linha(e, agora))];

            if (Catraca == 0 && Catracas.Count > 0)
            {
                Catraca = Catracas[0].Inner;
            }

            Selecionada = Catracas.FirstOrDefault(c => c.Inner == Catraca);

            if (Catraca > 0)
            {
                var comandos = await Cliente.ListarComandosAsync(
                    new ListarComandosRequest { Inner = Catraca, Limite = 50 }, cancellationToken: cancelamento);
                Historico = [.. comandos.Comandos.Select(LinhaDeComando.De)];
            }

            if (Catracas.Count == 0)
            {
                Mensagem = "Nenhuma catraca cadastrada na instalação.";
            }
        });

    private bool TemCatracaEOperador() => Catraca > 0 && Operador.Trim().Length >= 2;

    private void Reavaliar()
    {
        AcertarRelogio.ReavaliarDisponibilidade();
        EnviarMensagem.ReavaliarDisponibilidade();
        LiberarManualmente.ReavaliarDisponibilidade();
        RefazerConexao.ReavaliarDisponibilidade();
    }

    private async Task PedirAsync(TipoDeComando tipo)
    {
        var inner = Catraca;

        await Tentar(async () =>
        {
            var resposta = await Cliente.EnviarComandoAsync(new EnviarComandoRequest
            {
                Inner = inner,
                Tipo = tipo,
                Operador = Operador.Trim(),
                Texto = tipo is TipoDeComando.MensagemTemporaria ? MensagemTemporaria.Trim() : string.Empty,
                DuracaoSegundos = tipo is TipoDeComando.MensagemTemporaria ? DuracaoDaMensagem : 0,
                Motivo = tipo is TipoDeComando.LiberacaoManual ? Motivo.Trim() : string.Empty,
            });

            if (!resposta.Aceito)
            {
                Mensagem = "Não foi pedido: " + string.Join(" ", resposta.Problemas);
                return;
            }

            // O motivo é de uma liberação só: não fica preenchido para a próxima.
            if (tipo is TipoDeComando.LiberacaoManual)
            {
                Motivo = string.Empty;
            }

            Mensagem = $"{Textos.NomeDoComando(tipo)}: pedido à catraca {inner}. Acompanhe o resultado no histórico.";
            await Task.Delay(EsperaPeloResultado);
            var comandos = await Cliente.ListarComandosAsync(new ListarComandosRequest { Inner = inner, Limite = 50 });
            Historico = [.. comandos.Comandos.Select(LinhaDeComando.De)];

            if (Historico.Count > 0 && Historico[0].Situacao is not ("Aguardando a catraca" or "Na fila da catraca"))
            {
                Mensagem = $"{Historico[0].Comando} na catraca {inner}: {Historico[0].Situacao.ToLowerInvariant()} — {Historico[0].Resultado}";
            }
        }).ConfigureAwait(true);
    }
}
