using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>Base das telas: chama o serviço e transforma falha em mensagem visível.</summary>
public abstract class TelaBase : Notificavel, ITela
{
    private string _mensagem = string.Empty;
    private bool _ocupada;

    protected TelaBase(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        Cliente = cliente;
        Relogio = relogio ?? (() => DateTimeOffset.Now);
    }

    public abstract string Titulo { get; }

    /// <summary>Aviso para o operador: erro de comunicação, resultado de uma ação.</summary>
    public string Mensagem
    {
        get => _mensagem;
        protected set => Definir(ref _mensagem, value);
    }

    /// <summary>Chamada ao serviço em andamento.</summary>
    public bool Ocupada
    {
        get => _ocupada;
        private set => Definir(ref _ocupada, value);
    }

    protected EdgeControl.EdgeControlClient Cliente { get; }

    protected Func<DateTimeOffset> Relogio { get; }

    public abstract Task AtualizarAsync(CancellationToken cancelamento = default);

    /// <summary>Roda uma chamada; falha de comunicação vira mensagem, nunca exceção.</summary>
    protected async Task<bool> Tentar(Func<Task> chamada)
    {
        ArgumentNullException.ThrowIfNull(chamada);
        Ocupada = true;

        try
        {
            await chamada().ConfigureAwait(true);
            return true;
        }
        catch (RpcException erro)
        {
            Mensagem = MensagemDeFalha.Para(erro.StatusCode);
            return false;
        }
        finally
        {
            Ocupada = false;
        }
    }

    /// <summary>
    /// Lê o período da tela (data e hora opcionais, fuso do evento). Falha vira mensagem.
    /// </summary>
    protected bool LerPeriodo(
        DateTime? desde, string horaDesde, DateTime? ate, string horaAte,
        out DateTimeOffset? inicio, out DateTimeOffset? fim)
    {
        inicio = null;
        fim = null;

        if (!Periodo.TentarLerHora(horaDesde, out var hDesde) || !Periodo.TentarLerHora(horaAte, out var hAte))
        {
            Mensagem = "Hora no formato hh:mm, de 00:00 a 23:59 — ou deixe vazio.";
            return false;
        }

        var agora = Relogio();
        inicio = Periodo.Inicio(desde, hDesde, agora);
        fim = Periodo.Fim(ate, hAte, agora);

        if (inicio is { } i && fim is { } f && f < i)
        {
            Mensagem = "O fim do período é antes do começo.";
            return false;
        }

        return true;
    }

    /// <summary>Hoje, no relógio do evento.</summary>
    protected DateTime HojeNoEvento() => FusoDoEvento.NoEvento(Relogio()).Date;
}

/// <summary>Painel ao vivo: o evento num relance.</summary>
public sealed class PainelAoVivoViewModel : TelaBase
{
    /// <summary>Quantos acessos ficam na lista ao vivo.</summary>
    public const int AcessosNaTela = 100;

    private EstadoDoPainel _estado = EstadoDoPainel.Carregando();
    private IReadOnlyList<LinhaDeCatraca> _catracas = [];
    private string _resumo = string.Empty;
    private string _horaDoEvento = "—";
    private string _servicoResumo = "Conectando…";
    private Sinal _servicoSinal = Sinal.Neutro;
    private string _catracasResumo = "—";
    private Sinal _catracasSinal = Sinal.Neutro;
    private string _nuvemResumo = "—";
    private Sinal _nuvemSinal = Sinal.Neutro;
    private string _internet = string.Empty;
    private long _liberados;
    private long _negados;
    private long _giros;
    private long _ultimos5;
    private long _pendentes;

    public PainelAoVivoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        PorQue = new PainelPorQue(cliente);
    }

    /// <summary>"Por quê?" de cada negação da lista ao vivo (Etapa I.2 do docs/36).</summary>
    public PainelPorQue PorQue { get; }

    public override string Titulo => "Painel ao vivo";

    public EstadoDoPainel Estado { get => _estado; private set => Definir(ref _estado, value); }

    public IReadOnlyList<LinhaDeCatraca> Catracas { get => _catracas; private set => Definir(ref _catracas, value); }

    public ObservableCollection<LinhaDeAcesso> UltimosAcessos { get; } = [];

    public long Liberados { get => _liberados; private set => Definir(ref _liberados, value); }

    public long Negados { get => _negados; private set => Definir(ref _negados, value); }

    public long Giros { get => _giros; private set => Definir(ref _giros, value); }

    public long LiberadosUltimos5Minutos { get => _ultimos5; private set => Definir(ref _ultimos5, value); }

    public long PendentesDeEnvio { get => _pendentes; private set => Definir(ref _pendentes, value); }

    public string Resumo { get => _resumo; private set => Definir(ref _resumo, value); }

    public string Internet { get => _internet; private set => Definir(ref _internet, value); }

    /// <summary>Barra operacional: o serviço local ("Operacional", "Sem resposta").</summary>
    public string ServicoResumo { get => _servicoResumo; private set => Definir(ref _servicoResumo, value); }

    public Sinal ServicoSinal { get => _servicoSinal; private set => Definir(ref _servicoSinal, value); }

    /// <summary>Barra operacional: "2/2 online".</summary>
    public string CatracasResumo { get => _catracasResumo; private set => Definir(ref _catracasResumo, value); }

    public Sinal CatracasSinal { get => _catracasSinal; private set => Definir(ref _catracasSinal, value); }

    /// <summary>Barra operacional: a nuvem ("Online", "Offline", "Sem sincronização").</summary>
    public string NuvemResumo { get => _nuvemResumo; private set => Definir(ref _nuvemResumo, value); }

    public Sinal NuvemSinal { get => _nuvemSinal; private set => Definir(ref _nuvemSinal, value); }

    /// <summary>Hora da última atualização, no relógio do evento (Brasília).</summary>
    public string HoraDoEvento { get => _horaDoEvento; private set => Definir(ref _horaDoEvento, value); }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        var ok = await Tentar(async () =>
        {
            var estado = await Cliente.ObterEstadoAsync(new ObterEstadoRequest(), cancellationToken: cancelamento);
            var lista = await Cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento);
            var agora = Relogio();

            Estado = EstadoDoPainel.De(estado, agora);
            HoraDoEvento = Textos.Hora(agora);
            Liberados = estado.Liberados;
            Negados = estado.Negados;
            Giros = estado.Giros;
            LiberadosUltimos5Minutos = estado.LiberadosUltimos5Minutos;
            PendentesDeEnvio = estado.OutboxPendente;
            Catracas = [.. lista.Equipamentos.Select(e => Linha(e, agora))];
            Resumo = string.Create(
                CultureInfo.CurrentCulture,
                $"{estado.EquipamentosConectados} de {estado.EquipamentosCadastrados} catraca(s) atendendo");
            Internet = estado.InternetDisponivel
                ? $"Nuvem: sincronizado {Textos.Ha(estado.UltimaSincronizacao?.ToDateTimeOffset(), agora)}"
                : estado.UltimaSincronizacao is null
                    ? "Nuvem: sem sincronização — o acesso segue pela lista local deste computador"
                    : $"Nuvem: sem internet desde {Textos.Ha(estado.UltimaSincronizacao.ToDateTimeOffset(), agora)} — o acesso segue pela lista local deste computador";
            Mensagem = string.Empty;

            ServicoResumo = "Operacional";
            ServicoSinal = Sinal.Bom;
            (CatracasResumo, CatracasSinal) = (estado.EquipamentosCadastrados, estado.EquipamentosConectados) switch
            {
                (0, _) => ("Nenhuma cadastrada", Sinal.Neutro),
                (var total, var conectadas) when conectadas >= total => (string.Create(CultureInfo.InvariantCulture, $"{conectadas}/{total} online"), Sinal.Bom),
                (var total, 0) => (string.Create(CultureInfo.InvariantCulture, $"0/{total} online"), Sinal.Problema),
                (var total, var conectadas) => (string.Create(CultureInfo.InvariantCulture, $"{conectadas}/{total} online"), Sinal.Atencao),
            };
            (NuvemResumo, NuvemSinal) = estado.InternetDisponivel
                ? ("Online", Sinal.Bom)
                : estado.UltimaSincronizacao is null
                    ? ("Sem sincronização", Sinal.Neutro)
                    : ("Offline — catracas seguem", Sinal.Atencao);

            // Lista vazia com acesso já contado: completa pelo que está gravado, sem esperar o
            // fluxo ao vivo conectar (ou sem ele, como na captura das telas).
            if (UltimosAcessos.Count == 0 && estado.Liberados + estado.Negados > 0)
            {
                var recentes = await Cliente.ListarAcessosAsync(
                    new ListarAcessosRequest { Limite = AcessosNaTela }, cancellationToken: cancelamento);
                Completar([.. recentes.Acessos.Select(LinhaDeAcesso.De)]);
            }
        }).ConfigureAwait(true);

        if (!ok)
        {
            Estado = Estado.ComFalhaDeComunicacao(Mensagem, Relogio(), MensagemDeFalha.TokenSemPermissao);
            ServicoResumo = "Sem resposta";
            ServicoSinal = Sinal.Problema;
        }
    }

    /// <summary>
    /// Recebe os acessos ao vivo até o cancelamento. Se a conexão cair, espera e reconecta.
    /// </summary>
    /// <param name="despachar">Leva a atualização da lista para a thread da tela.</param>
    /// <param name="cancelamento">Fecha quando a janela fecha.</param>
    public async Task AcompanharAsync(Action<Action> despachar, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(despachar);

        while (!cancelamento.IsCancellationRequested)
        {
            try
            {
                using var fluxo = Cliente.AcompanharEventos(new AcompanharEventosRequest(), cancellationToken: cancelamento);

                // O fluxo só traz o que acontece daqui para frente. Sem isto, o que passou antes de
                // a tela abrir (ou enquanto o serviço reiniciava) entrava nos contadores e não na
                // lista. O fluxo já está aberto: o que chegar enquanto a lista carrega fica na fila
                // dele e o Acrescentar descarta o que a lista já trouxe.
                var recentes = await Cliente.ListarAcessosAsync(
                    new ListarAcessosRequest { Limite = AcessosNaTela }, cancellationToken: cancelamento);
                var historico = recentes.Acessos.Select(LinhaDeAcesso.De).ToList();
                despachar(() => Repor(historico));

                while (await fluxo.ResponseStream.MoveNext(cancelamento).ConfigureAwait(false))
                {
                    var linha = LinhaDeAcesso.De(fluxo.ResponseStream.Current);
                    despachar(() => Acrescentar(linha));
                }
            }
            catch (RpcException) when (cancelamento.IsCancellationRequested)
            {
                // A janela fechou: o gRPC avisa o cancelamento como RpcException, e não como
                // OperationCanceledException. Deixar escapar derrubava o aplicativo ao fechar.
                return;
            }
            catch (RpcException)
            {
                // Serviço reiniciando: tenta de novo em instantes. A catraca não depende disto.
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancelamento).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Troca a lista pelos acessos gravados, do mais recente ao mais antigo. Chamado a cada
    /// conexão com o serviço, antes dos acessos ao vivo.
    /// </summary>
    public void Repor(IReadOnlyList<LinhaDeAcesso> historico)
    {
        ArgumentNullException.ThrowIfNull(historico);
        UltimosAcessos.Clear();

        foreach (var linha in historico.Take(AcessosNaTela))
        {
            UltimosAcessos.Add(linha);
        }
    }

    /// <summary>
    /// Acrescenta no fim os acessos gravados que a lista ainda não tem, do mais recente ao mais
    /// antigo. Não apaga nada: um acesso ao vivo que chegou no meio continua no topo.
    /// </summary>
    public void Completar(IReadOnlyList<LinhaDeAcesso> historico)
    {
        ArgumentNullException.ThrowIfNull(historico);

        foreach (var linha in historico)
        {
            if (UltimosAcessos.Count >= AcessosNaTela)
            {
                break;
            }

            if (linha.EventoId.Length == 0 || UltimosAcessos.All(l => !string.Equals(l.EventoId, linha.EventoId, StringComparison.Ordinal)))
            {
                UltimosAcessos.Add(linha);
            }
        }
    }

    /// <summary>Põe um acesso no topo da lista, mantendo o tamanho. Ignora o que já está nela.</summary>
    public void Acrescentar(LinhaDeAcesso linha)
    {
        ArgumentNullException.ThrowIfNull(linha);

        if (linha.EventoId.Length > 0 && UltimosAcessos.Any(l => string.Equals(l.EventoId, linha.EventoId, StringComparison.Ordinal)))
        {
            return;
        }

        UltimosAcessos.Insert(0, linha);

        while (UltimosAcessos.Count > AcessosNaTela)
        {
            UltimosAcessos.RemoveAt(UltimosAcessos.Count - 1);
        }
    }

    internal static LinhaDeCatraca Linha(Equipamento e, DateTimeOffset agora)
    {
        var (texto, sinal) = Textos.SituacaoDaCatraca(e);
        var (relogio, relogioDivergente) = Textos.Relogio(e, agora);

        return new LinhaDeCatraca(
            e.Inner,
            string.IsNullOrWhiteSpace(e.NomeDoGate) ? $"Catraca {e.Inner:D2}" : e.NomeDoGate,
            texto,
            sinal,
            e.UltimaDecisao switch
            {
                "liberado" => "Liberou",
                "negado" => "Negou",
                _ => "—",
            },
            e.UltimaDecisao is { Length: > 0 } ? Textos.Ha(e.UltimoEvento?.ToDateTimeOffset(), agora) : "—",
            string.IsNullOrWhiteSpace(e.Firmware) ? "—" : e.Firmware,
            e.Worker,
            e.Porta,
            e.TentativasDeReconexao,
            relogio,
            relogioDivergente,
            e.Simulacao);
    }
}

/// <summary>Catracas: situação detalhada de cada uma.</summary>
public sealed class CatracasViewModel : TelaBase
{
    private IReadOnlyList<LinhaDeCatraca> _catracas = [];

    public CatracasViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
    }

    public override string Titulo => "Catracas";

    public IReadOnlyList<LinhaDeCatraca> Catracas { get => _catracas; private set => Definir(ref _catracas, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            var lista = await Cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento);
            var agora = Relogio();
            Catracas = [.. lista.Equipamentos.Select(e => PainelAoVivoViewModel.Linha(e, agora))];
            Mensagem = Catracas.Count == 0 ? "Nenhuma catraca cadastrada na instalação." : string.Empty;
        });
}

/// <summary>Acessos: histórico com filtros.</summary>
public sealed class AcessosViewModel : TelaBase
{
    private IReadOnlyList<LinhaDeAcesso> _linhas = [];
    private bool _haMais;
    private string _catraca = string.Empty;
    private int _resultado;
    private string _categoria = string.Empty;
    private DateTime? _desde;
    private DateTime? _ate;
    private string _horaDesde = string.Empty;
    private string _horaAte = string.Empty;

    public AcessosViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Buscar = new ComandoAssincrono(() => AtualizarAsync());
        PorQue = new PainelPorQue(cliente);
    }

    /// <summary>"Por quê?" de cada negação da lista (Etapa I.2 do docs/36).</summary>
    public PainelPorQue PorQue { get; }

    public override string Titulo => "Acessos";

    public ComandoAssincrono Buscar { get; }

    /// <summary>Número da catraca; vazio = todas.</summary>
    public string Catraca { get => _catraca; set => Definir(ref _catraca, value ?? string.Empty); }

    /// <summary>Opções do filtro de resultado, na ordem de <see cref="Resultado"/>.</summary>
    public IReadOnlyList<string> OpcoesDeResultado { get; } = ["Todos", "Liberados", "Negados"];

    /// <summary>0 todos, 1 liberados, 2 negados.</summary>
    public int Resultado { get => _resultado; set => Definir(ref _resultado, Math.Clamp(value, 0, 2)); }

    public string Categoria { get => _categoria; set => Definir(ref _categoria, value ?? string.Empty); }

    /// <summary>Primeiro dia do período, no fuso do evento. Vazio = sem limite.</summary>
    public DateTime? Desde { get => _desde; set => Definir(ref _desde, value); }

    /// <summary>"hh:mm"; vazio = começo do dia.</summary>
    public string HoraDesde { get => _horaDesde; set => Definir(ref _horaDesde, value ?? string.Empty); }

    /// <summary>Último dia do período, inclusive. Vazio = sem limite.</summary>
    public DateTime? Ate { get => _ate; set => Definir(ref _ate, value); }

    /// <summary>"hh:mm", inclusive; vazio = fim do dia.</summary>
    public string HoraAte { get => _horaAte; set => Definir(ref _horaAte, value ?? string.Empty); }

    public IReadOnlyList<LinhaDeAcesso> Linhas { get => _linhas; private set => Definir(ref _linhas, value); }

    public bool HaMais { get => _haMais; private set => Definir(ref _haMais, value); }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        int inner = 0;
        if (!string.IsNullOrWhiteSpace(Catraca)
            && !int.TryParse(Catraca, NumberStyles.None, CultureInfo.InvariantCulture, out inner))
        {
            Mensagem = "O número da catraca precisa ser um número inteiro.";
            return;
        }

        if (!LerPeriodo(Desde, HoraDesde, Ate, HoraAte, out var inicio, out var fim))
        {
            return;
        }

        await Tentar(async () =>
        {
            var pedido = new ListarAcessosRequest
            {
                Inner = inner,
                Resultado = (FiltroDeResultado)Resultado,
                Categoria = Categoria.Trim(),
                Limite = 500,
            };

            if (inicio is { } desde)
            {
                pedido.Desde = Timestamp.FromDateTimeOffset(desde);
            }

            if (fim is { } ate)
            {
                pedido.Ate = Timestamp.FromDateTimeOffset(ate);
            }

            var resposta = await Cliente.ListarAcessosAsync(pedido, cancellationToken: cancelamento);
            Linhas = [.. resposta.Acessos.Select(LinhaDeAcesso.De)];
            HaMais = resposta.HaMais;
            Mensagem = Linhas.Count == 0
                ? "Nenhum acesso com esses filtros."
                : HaMais ? "Mostrando os 500 mais recentes; refine os filtros para ver o resto." : string.Empty;
        }).ConfigureAwait(true);
    }
}

/// <summary>Consulta de um código: o operador digita, o sistema diz a situação.</summary>
public sealed class ConsultaViewModel : TelaBase
{
    private string _codigo = string.Empty;
    private bool _encontrado;
    private IReadOnlyList<ParDeTexto> _detalhes = [];
    private IReadOnlyList<LinhaDeAcesso> _historico = [];

    public ConsultaViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Consultar = new ComandoAssincrono(ConsultarAsync, () => !string.IsNullOrWhiteSpace(Codigo));
    }

    public override string Titulo => "Consultar código";

    public ComandoAssincrono Consultar { get; }

    /// <summary>
    /// O que o operador digitou ou leu no leitor do balcão. É apagado assim que a consulta
    /// sai: a tela não guarda número de cartão.
    /// </summary>
    public string Codigo
    {
        get => _codigo;
        set
        {
            if (Definir(ref _codigo, value ?? string.Empty))
            {
                Consultar.ReavaliarDisponibilidade();
            }
        }
    }

    public bool Encontrado { get => _encontrado; private set => Definir(ref _encontrado, value); }

    public IReadOnlyList<ParDeTexto> Detalhes { get => _detalhes; private set => Definir(ref _detalhes, value); }

    public IReadOnlyList<LinhaDeAcesso> Historico { get => _historico; private set => Definir(ref _historico, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) => Task.CompletedTask;

    private async Task ConsultarAsync()
    {
        var codigo = Codigo.Trim();
        Codigo = string.Empty;

        await Tentar(async () =>
        {
            var r = await Cliente.ConsultarCodigoAsync(new ConsultarCodigoRequest { Codigo = codigo });
            var agora = Relogio();

            Encontrado = r.Encontrado;
            Historico = [.. r.Historico.Select(LinhaDeAcesso.De)];

            if (!r.Encontrado)
            {
                Detalhes = [];
                Mensagem = "Código não cadastrado. A catraca negaria este código.";
                return;
            }

            Detalhes =
            [
                new ParDeTexto("Código", r.CodigoMascarado),
                new ParDeTexto("Origem", r.Provedor),
                new ParDeTexto("Categoria", string.IsNullOrWhiteSpace(r.Categoria) ? "—" : r.Categoria),
                new ParDeTexto("Situação", r.Situacao switch
                {
                    "valido" => "Válido",
                    "consumido" => "Já utilizado",
                    "cancelado" => "Cancelado",
                    "bloqueado" => "Bloqueado",
                    _ => r.Situacao,
                }),
                new ParDeTexto("Usos", r.UsosMaximos == 0
                    ? string.Create(CultureInfo.CurrentCulture, $"{r.UsosFeitos} (sem limite)")
                    : string.Create(CultureInfo.CurrentCulture, $"{r.UsosFeitos} de {r.UsosMaximos}")),
                new ParDeTexto("Último uso", Textos.Ha(r.UltimoUso?.ToDateTimeOffset(), agora)),
            ];
            Mensagem = string.Empty;
        }).ConfigureAwait(true);
    }
}

/// <summary>Sincronização com a nuvem e com os sites de ingresso.</summary>
public sealed class SincronizacaoViewModel : TelaBase
{
    private IReadOnlyList<ParDeTexto> _situacao = [];
    private IReadOnlyList<ProvedorCadastrado> _provedores = [];
    private Sinal _sinal;

    public SincronizacaoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
    }

    public override string Titulo => "Sincronização";

    public IReadOnlyList<ParDeTexto> Situacao { get => _situacao; private set => Definir(ref _situacao, value); }

    public IReadOnlyList<ProvedorCadastrado> Provedores { get => _provedores; private set => Definir(ref _provedores, value); }

    public Sinal Sinal { get => _sinal; private set => Definir(ref _sinal, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            var r = await Cliente.ObterSincronizacaoAsync(new ObterSincronizacaoRequest(), cancellationToken: cancelamento);
            var agora = Relogio();
            var ultima = r.UltimoSucesso?.ToDateTimeOffset();

            Sinal = !r.Configurada
                ? Sinal.Neutro
                : r.CartasMortas > 0 || !string.IsNullOrEmpty(r.UltimaFalha)
                    ? Sinal.Atencao
                    : Sinal.Bom;

            Situacao =
            [
                new ParDeTexto("Nuvem", r.Configurada ? "Configurada" : "Não configurada nesta instalação"),
                new ParDeTexto("Última sincronização", Textos.Ha(ultima, agora)),
                new ParDeTexto("Última falha", string.IsNullOrWhiteSpace(r.UltimaFalha) ? "—" : r.UltimaFalha),
                new ParDeTexto("Aguardando envio", string.Create(CultureInfo.CurrentCulture, $"{r.Pendentes}")),
                new ParDeTexto("Mais antigo na fila", r.Pendentes == 0 ? "—" : Textos.Ha(agora.AddSeconds(-r.IdadeDoMaisAntigoSegundos), agora)),
                new ParDeTexto("Recusados pela nuvem", string.Create(CultureInfo.CurrentCulture, $"{r.CartasMortas}")),
            ];
            Provedores = [.. r.Provedores];
            Mensagem = string.Empty;
        });
}

/// <summary>Prestação de contas: o que entrou, por onde, de que categoria, e o que foi negado.</summary>
/// <remarks>
/// <para>
/// Defeitos corrigidos em 01/10 ("os relatórios não estão funcionais", docs/29): "De" vazio
/// contava só as últimas 24 h, sem dizer; o CSV não dizia o período; exportar para um arquivo
/// aberto no Excel estourava como erro inesperado; o nome do arquivo usava o fuso do Windows.
/// </para>
/// <para>
/// O que existe é o resumo do período (por categoria, catraca, hora e motivo de negativa) e o
/// CSV dele. Os relatórios R1–R8 do docs/25, o PDF e o corte fechado com código de conferência
/// são da fase 6: aparecem em <see cref="AindaNaoDisponivel"/>, desabilitados e com o motivo.
/// </para>
/// </remarks>
public sealed class ContasViewModel : TelaBase
{
    private DateTime? _desde;
    private DateTime? _ate;
    private string _horaDesde = string.Empty;
    private string _horaAte = string.Empty;
    private PrestacaoDeContas? _contas;

    public ContasViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Gerar = new ComandoAssincrono(() => AtualizarAsync());
        _desde = HojeNoEvento();
    }

    public override string Titulo => "Prestação de contas";

    public ComandoAssincrono Gerar { get; }

    /// <summary>Primeiro dia do período, no fuso do evento. Começa em hoje; vazio = desde o começo.</summary>
    public DateTime? Desde { get => _desde; set => Definir(ref _desde, value); }

    /// <summary>"hh:mm"; vazio = começo do dia.</summary>
    public string HoraDesde { get => _horaDesde; set => Definir(ref _horaDesde, value ?? string.Empty); }

    /// <summary>Último dia, inclusive. Vazio = até agora.</summary>
    public DateTime? Ate { get => _ate; set => Definir(ref _ate, value); }

    /// <summary>"hh:mm", inclusive; vazio = fim do dia.</summary>
    public string HoraAte { get => _horaAte; set => Definir(ref _horaAte, value ?? string.Empty); }

    public PrestacaoDeContas? Contas
    {
        get => _contas;
        private set
        {
            if (Definir(ref _contas, value))
            {
                Avisar(nameof(PodeExportar));
                Avisar(nameof(Periodo));
                Avisar(nameof(Entradas));
                Avisar(nameof(Saidas));
                Avisar(nameof(PercentualDosNegados));
            }
        }
    }

    /// <summary>Só há o que exportar depois de gerar.</summary>
    public bool PodeExportar => Contas is not null;

    /// <summary>
    /// O período que os números na tela cobrem, como o serviço aplicou — e não o que está
    /// digitado nos campos, que pode ter mudado depois de gerar.
    /// </summary>
    public string Periodo => Contas is { } c ? TextoDoPeriodo(c) : string.Empty;

    /// <summary>Total de entradas do período (para teste de binding).</summary>
    public long Entradas => Contas?.Entradas ?? 0;

    /// <summary>Total de saídas do período (para teste de binding).</summary>
    public long Saidas => Contas?.Saidas ?? 0;

    /// <summary>Percentual de negações (agregado, para teste de binding).</summary>
    public long PercentualDosNegados => Contas is { Liberados: > 0, Negados: > 0 }
        ? Contas.Negados * 100 / (Contas.Liberados + Contas.Negados)
        : 0;

    /// <summary>
    /// O que a prestação de contas ainda não tem (docs/25; fase 6 do docs/29): aparece
    /// desabilitado, com o motivo, para a tela não parecer ter o que não tem.
    /// </summary>
    public IReadOnlyList<ParDeTexto> AindaNaoDisponivel { get; } =
    [
        new("R1 · Boletim do dia (PDF)", "Fase 6. Depende da hora de corte do dia de operação (E9) e da regra da meia-entrada (E10), e dos tipos cadastrados (fase 3). Hoje: o resumo \"Por categoria\" abaixo."),
        new("R2 · Fluxo por hora e por tipo", "Fase 6. Hoje: \"Por hora\" abaixo, no total, sem separar por tipo."),
        new("R3 · Por catraca e disponibilidade", "Fase 6. Hoje: \"Por catraca\" abaixo, sem o tempo fora do ar."),
        new("R4 · Por origem do ingresso", "Fase 6: validados por provedor (bilheteria, venda online, cortesia)."),
        new("R5 · Conciliação (cadastrado × usado)", "Fase 6: depende do cadastro de cartões (fase 3)."),
        new("R6 · Ocorrências e auditoria", "Fase 6: negativas repetidas, liberações manuais e comandos. As liberações manuais ainda não entram nos números."),
        new("R7 · Consolidado do evento", "Fase 6: depende da hora de corte do dia de operação (E9)."),
        new("R8 · Saúde da operação", "Fase 6: quedas de catraca, do serviço e da sincronização, com duração."),
        new("Fechar o corte com código de conferência", "Fase 6: números congelados com SHA-256 (docs/25 §4)."),
    ];

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        if (!LerPeriodo(Desde, HoraDesde, Ate, HoraAte, out var inicio, out var fim))
        {
            return;
        }

        await Tentar(async () =>
        {
            // O período vai sempre explícito: sem o início, o serviço assumiria as últimas 24 h,
            // e "De" vazio quer dizer "desde o começo", como na tela de Acessos.
            var pedido = new ObterPrestacaoDeContasRequest
            {
                Desde = Timestamp.FromDateTimeOffset(inicio ?? DateTimeOffset.UnixEpoch),
                Ate = Timestamp.FromDateTimeOffset(fim ?? Relogio()),
            };

            Contas = await Cliente.ObterPrestacaoDeContasAsync(pedido, cancellationToken: cancelamento);
            Mensagem = Contas.Liberados + Contas.Negados == 0 ? "Nenhuma tentativa no período." : string.Empty;
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// A prestação em CSV para Excel em português: separador ponto e vírgula, UTF-8 com BOM.
    /// </summary>
    public string ParaCsv()
    {
        if (Contas is not { } c)
        {
            return string.Empty;
        }

        var csv = new StringBuilder();
        var cultura = CultureInfo.InvariantCulture;

        csv.AppendLine(string.Create(cultura, $"Prestação de contas;gerada em {FusoDoEvento.NoEvento(c.GeradaEm.ToDateTimeOffset()):dd/MM/yyyy HH:mm:ss} (horário de Brasília)"));
        csv.AppendLine(string.Create(cultura, $"Período;{TextoDoPeriodo(c)}"));
        csv.AppendLine(string.Create(cultura, $"Liberados;{c.Liberados}"));
        csv.AppendLine(string.Create(cultura, $"Com giro confirmado;{c.Giros}"));
        csv.AppendLine(string.Create(cultura, $"Negados;{c.Negados}"));

        // Mapa de giro (D9): o giro conta pelo rótulo; sem regra no mapa, como entrada.
        csv.AppendLine(string.Create(cultura, $"Entradas (giros pelo mapa de giro);{c.Entradas}"));
        csv.AppendLine(string.Create(cultura, $"Saídas (giros pelo mapa de giro);{c.Saidas}"));
        csv.AppendLine();
        csv.AppendLine("Categoria;Liberados;Com giro");
        foreach (var l in c.PorCategoria)
        {
            csv.AppendLine(string.Create(cultura, $"{Celula(l.Categoria)};{l.Liberados};{l.Giros}"));
        }

        csv.AppendLine();
        csv.AppendLine("Catraca;Liberados;Com giro;Negados;Entradas;Saídas");
        foreach (var l in c.PorCatraca)
        {
            csv.AppendLine(string.Create(cultura, $"{l.Inner};{l.Liberados};{l.Giros};{l.Negados};{l.Entradas};{l.Saidas}"));
        }

        csv.AppendLine();
        csv.AppendLine("Hora;Liberados;Negados");
        foreach (var l in c.PorHora)
        {
            csv.AppendLine(string.Create(cultura, $"{FusoDoEvento.NoEvento(l.Hora.ToDateTimeOffset()):dd/MM/yyyy HH}:00;{l.Liberados};{l.Negados}"));
        }

        csv.AppendLine();
        csv.AppendLine("Motivo da negativa;Quantidade");
        foreach (var l in c.Negativas)
        {
            csv.AppendLine(string.Create(cultura, $"{Celula(l.Mensagem)};{l.Quantidade}"));
        }

        return csv.ToString();
    }

    /// <summary>
    /// O nome sugerido para o arquivo, com a hora do evento (Brasília) — e não a do Windows,
    /// que pode estar em outro fuso.
    /// </summary>
    public string NomeDoArquivoSugerido() =>
        string.Create(CultureInfo.InvariantCulture, $"prestacao-de-contas-{FusoDoEvento.NoEvento(Relogio()):yyyy-MM-dd-HHmm}.csv");

    /// <summary>Grava o CSV no caminho escolhido pelo operador. Falha vira mensagem, nunca exceção.</summary>
    public async Task ExportarAsync(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);

        if (Contas is null)
        {
            Mensagem = "Gere a prestação de contas antes de exportar.";
            return;
        }

        try
        {
            await File.WriteAllTextAsync(caminho, ParaCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)).ConfigureAwait(true);
            Mensagem = $"Exportado para {caminho}";
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            // O caso comum: o arquivo anterior está aberto no Excel, que o tranca.
            Mensagem = $"Não foi possível gravar o arquivo ({erro.Message}). Se ele estiver aberto no Excel, feche e exporte de novo, ou escolha outro nome.";
        }
    }

    private static string TextoDoPeriodo(PrestacaoDeContas c)
    {
        var cultura = CultureInfo.InvariantCulture;
        var ate = c.PeriodoAte is null ? "—" : FusoDoEvento.NoEvento(c.PeriodoAte.ToDateTimeOffset()).ToString("dd/MM/yyyy HH:mm", cultura);

        if (c.PeriodoDesde is null || c.PeriodoDesde.ToDateTimeOffset() <= DateTimeOffset.UnixEpoch)
        {
            return $"desde o começo até {ate} (horário de Brasília)";
        }

        var desde = FusoDoEvento.NoEvento(c.PeriodoDesde.ToDateTimeOffset()).ToString("dd/MM/yyyy HH:mm", cultura);
        return $"de {desde} a {ate} (horário de Brasília)";
    }

    // Célula que começa com =, +, - ou @ vira fórmula no Excel: um motivo vindo de fora
    // não pode virar comando na planilha de quem abre.
    private static string Celula(string texto)
    {
        var t = texto.Replace(";", ",", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return t.Length > 0 && t[0] is '=' or '+' or '-' or '@' ? "'" + t : t;
    }
}

/// <summary>Configurações do evento que valem para todas as catracas.</summary>
public sealed class ConfiguracoesViewModel : TelaBase
{
    private int _tipoDeLeitor = 8;
    private bool _leitorDaUrna = true;
    private int _tempo = 5;
    private string _mensagemPadrao = string.Empty;
    private int _espera = 10;

    /// <summary>
    /// A espera pelo giro que o serviço tem gravada. É a única que o "Aplicar agora" não
    /// leva; a tela precisa saber se ela mudou para avisar que só vale ao reiniciar.
    /// </summary>
    private int _esperaGravada = 10;
    private bool _nuvemLigada;
    private IReadOnlyList<string> _problemas = [];

    public ConfiguracoesViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Salvar = new ComandoAssincrono(SalvarAsync);
        // Dois passos: "Aplicar agora" mexe em todas as catracas de uma vez; o primeiro clique só
        // pede a confirmação. A confirmação só se encerra quando o pedido é aceito.
        PrepararAplicacao = new ComandoAssincrono(() =>
        {
            ConfirmandoAplicacao = true;
            return Task.CompletedTask;
        }, () => !ConfirmandoAplicacao);
        AplicarAgora = new ComandoAssincrono(AplicarAgoraAsync, () => ConfirmandoAplicacao);
        CancelarAplicacao = new ComandoAssincrono(() =>
        {
            ConfirmandoAplicacao = false;
            return Task.CompletedTask;
        }, () => ConfirmandoAplicacao);
    }

    private bool _confirmandoAplicacao;

    /// <summary>A aplicação em todas as catracas espera a confirmação do operador.</summary>
    public bool ConfirmandoAplicacao
    {
        get => _confirmandoAplicacao;
        private set
        {
            if (Definir(ref _confirmandoAplicacao, value))
            {
                Avisar(nameof(TextoDaConfirmacaoDaAplicacao));

                // O botão do WPF só reconsulta CanExecute quando o comando avisa. Sem isto, "Confirmar"
                // e "Cancelar" nasciam desabilitados e "Aplicar agora" não voltava (achado E6-1).
                PrepararAplicacao.ReavaliarDisponibilidade();
                AplicarAgora.ReavaliarDisponibilidade();
                CancelarAplicacao.ReavaliarDisponibilidade();
            }
        }
    }

    /// <summary>O que a confirmação diz ao operador antes de mandar o pedido a todas as catracas.</summary>
    public string TextoDaConfirmacaoDaAplicacao => ConfirmandoAplicacao
        ? "Aplicar a configuração salva em todas as catracas desta instalação? Cada uma reconecta e fica alguns segundos sem atender."
        : string.Empty;

    /// <summary>Primeiro passo: abre a confirmação de "Aplicar agora".</summary>
    public ComandoAssincrono PrepararAplicacao { get; }

    /// <summary>Desfaz a confirmação pendente sem enviar nada.</summary>
    public ComandoAssincrono CancelarAplicacao { get; }

    /// <summary>
    /// Segundo passo: pede a cada catraca que reconecte com a configuração gravada. Cada uma
    /// fica alguns segundos sem atender enquanto reconecta.
    /// </summary>
    public ComandoAssincrono AplicarAgora { get; }

    public override string Titulo => "Configurações";

    public ComandoAssincrono Salvar { get; }

    /// <summary>Quem está operando, gravado junto da mudança.</summary>
    public string Operador { get; set; } = string.Empty;

    public int TipoDeLeitor { get => _tipoDeLeitor; set => Definir(ref _tipoDeLeitor, value); }

    public bool LeitorDaUrna { get => _leitorDaUrna; set => Definir(ref _leitorDaUrna, value); }

    public int TempoDeAcionamento { get => _tempo; set => Definir(ref _tempo, value); }

    public string MensagemPadrao { get => _mensagemPadrao; set => Definir(ref _mensagemPadrao, value ?? string.Empty); }

    public int EsperaPeloGiro { get => _espera; set => Definir(ref _espera, value); }

    public bool NuvemLigada { get => _nuvemLigada; private set => Definir(ref _nuvemLigada, value); }

    public IReadOnlyList<string> Problemas { get => _problemas; private set => Definir(ref _problemas, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            var c = await Cliente.ObterConfiguracaoAsync(new ObterConfiguracaoRequest(), cancellationToken: cancelamento);
            TipoDeLeitor = c.TipoDeLeitor;
            LeitorDaUrna = c.LeitorDaUrna;
            TempoDeAcionamento = c.TempoDeAcionamentoSegundos;
            MensagemPadrao = c.MensagemPadrao;
            EsperaPeloGiro = c.EsperaPeloGiroSegundos;
            _esperaGravada = c.EsperaPeloGiroSegundos;
            NuvemLigada = c.NuvemLigada;
            Problemas = [];
        });

    private async Task SalvarAsync() =>
        await Tentar(async () =>
        {
            var r = await Cliente.GravarConfiguracaoAsync(new GravarConfiguracaoRequest
            {
                Operador = Operador,
                Configuracao = new ConfiguracaoDoEvento
                {
                    TipoDeLeitor = TipoDeLeitor,
                    LeitorDaUrna = LeitorDaUrna,
                    TempoDeAcionamentoSegundos = TempoDeAcionamento,
                    MensagemPadrao = MensagemPadrao,
                    EsperaPeloGiroSegundos = EsperaPeloGiro,
                },
            });

            Problemas = [.. r.Problemas];

            // exige_reinicio quer dizer "há o que aplicar" (nome anterior ao "Aplicar
            // agora", docs/32). O "Aplicar agora" leva leitor, urna, tempo e mensagem sem
            // reiniciar o serviço; só a espera pelo giro fica para o próximo início.
            var esperaMudou = r.Gravada && EsperaPeloGiro != _esperaGravada;
            Mensagem = !r.Gravada
                ? "Não foi gravado. Corrija os itens abaixo."
                : r.ExigeReinicio
                    ? "Gravado. Use \"Aplicar agora nas catracas\" para as catracas passarem a usar a nova configuração, sem reiniciar o serviço."
                      + (esperaMudou ? " A espera pelo giro só muda quando o serviço reiniciar." : string.Empty)
                    : "Nada mudou.";

            if (r.Gravada)
            {
                _esperaGravada = EsperaPeloGiro;
            }
        }).ConfigureAwait(true);

    private async Task AplicarAgoraAsync() =>
        await Tentar(async () =>
        {
            var r = await Cliente.EnviarComandoAsync(new EnviarComandoRequest
            {
                Inner = 0,
                Tipo = TipoDeComando.AplicarConfiguracao,
                Operador = Operador.Trim(),
            });

            Problemas = [.. r.Problemas];
            Mensagem = r.Aceito
                ? $"Pedido a {r.Ids.Count} catraca(s). Cada uma reconecta e fica alguns segundos sem atender. Acompanhe em Gerenciar catraca."
                : "Não foi pedido. Corrija os itens abaixo.";

            // Só some a confirmação quando o pedido saiu: um nome faltando não obriga a começar de novo.
            if (r.Aceito)
            {
                ConfirmandoAplicacao = false;
            }
        }).ConfigureAwait(true);
}

/// <summary>Diagnóstico: o que o suporte precisa ver.</summary>
public sealed class DiagnosticoViewModel : TelaBase
{
    private Diagnostico? _diagnostico;
    private byte[]? _pacote;
    private string _nomeDoPacote = string.Empty;
    private string _analisadorResumo = string.Empty;
    private Sinal _analisadorSinal = Sinal.Neutro;
    private IReadOnlyList<ParDeTexto> _analisador = [];

    public DiagnosticoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        PedirPacote = new ComandoAssincrono(PedirPacoteAsync);
    }

    /// <summary>Pede ao serviço o zip de diagnóstico. A tela, depois, pergunta onde salvar.</summary>
    public ComandoAssincrono PedirPacote { get; }

    /// <summary>O zip pronto para salvar; nulo enquanto não foi pedido ou se o serviço não conseguiu montar.</summary>
    public byte[]? Pacote { get => _pacote; private set => Definir(ref _pacote, value); }

    /// <summary>Nome sugerido para o arquivo, com a data de Brasília.</summary>
    public string NomeDoPacote { get => _nomeDoPacote; private set => Definir(ref _nomeDoPacote, value); }

    private async Task PedirPacoteAsync() =>
        await Tentar(async () =>
        {
            var resposta = await Cliente.ObterPacoteDeDiagnosticoAsync(new ObterPacoteDeDiagnosticoRequest(), cancellationToken: default);
            if (!resposta.Gerado)
            {
                Pacote = null;
                Mensagem = resposta.Mensagem;
                return;
            }

            Pacote = resposta.Zip.ToByteArray();
            NomeDoPacote = resposta.NomeDoArquivo;
            Mensagem = "Pacote de diagnóstico pronto. Escolha onde salvar.";
        }).ConfigureAwait(true);

    /// <summary>Grava o pacote no caminho escolhido. Falha vira mensagem, nunca exceção.</summary>
    public void SalvarEm(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        if (Pacote is null)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(caminho, Pacote);
            Mensagem = $"Pacote salvo em {caminho}. Envie esse arquivo ao suporte.";
        }
        catch (IOException)
        {
            Mensagem = "Não foi possível salvar o arquivo. Verifique se ele não está aberto em outro programa.";
        }
        catch (UnauthorizedAccessException)
        {
            Mensagem = "Sem permissão para salvar nesse lugar. Escolha outra pasta.";
        }
    }

    public override string Titulo => "Diagnóstico";

    public Diagnostico? Diagnostico { get => _diagnostico; private set => Definir(ref _diagnostico, value); }

    /// <summary>
    /// A camada inteligente numa frase: "Desligada nesta instalação", "Funcionando", "Com erro"
    /// (Etapa I.0 do docs/36; docs/36-anexos/02 §3.7, linha "Diagnóstico").
    /// </summary>
    public string AnalisadorResumo { get => _analisadorResumo; private set => Definir(ref _analisadorResumo, value); }

    public Sinal AnalisadorSinal { get => _analisadorSinal; private set => Definir(ref _analisadorSinal, value); }

    /// <summary>A conta do Analisador: último ciclo, duração, orçamento, estouros, último erro.</summary>
    public IReadOnlyList<ParDeTexto> Analisador { get => _analisador; private set => Definir(ref _analisador, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            Diagnostico = await Cliente.ObterDiagnosticoAsync(new ObterDiagnosticoRequest(), cancellationToken: cancelamento);
            (AnalisadorResumo, AnalisadorSinal, Analisador) = Textos.SaudeDoAnalisador(Diagnostico.Analisador, Relogio());
            Mensagem = string.Empty;
        });
}

/// <summary>
/// Simulador de catraca: "passa" um código numa catraca simulada, como se o leitor tivesse
/// lido. Só funciona com a instalação em modo simulação.
/// </summary>
public sealed class SimuladorViewModel : TelaBase
{
    private string _catraca = "1";
    private string _codigo = string.Empty;
    private bool _naUrna;
    private bool _girar = true;
    private IReadOnlyList<LinhaDeAcesso> _resultados = [];

    public SimuladorViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null, TimeSpan? esperaPeloResultado = null)
        : base(cliente, relogio)
    {
        EsperaPeloResultado = esperaPeloResultado ?? TimeSpan.FromSeconds(1.5);
        Passar = new ComandoAssincrono(PassarAsync, () => !string.IsNullOrWhiteSpace(Codigo));
    }

    public override string Titulo => "Simulador";

    public ComandoAssincrono Passar { get; }

    /// <summary>Quanto esperar a catraca simulada decidir antes de buscar o resultado.</summary>
    public TimeSpan EsperaPeloResultado { get; }

    public string Catraca { get => _catraca; set => Definir(ref _catraca, value ?? string.Empty); }

    public string Codigo
    {
        get => _codigo;
        set
        {
            if (Definir(ref _codigo, value ?? string.Empty))
            {
                Passar.ReavaliarDisponibilidade();
            }
        }
    }

    public bool NaUrna { get => _naUrna; set => Definir(ref _naUrna, value); }

    public bool Girar { get => _girar; set => Definir(ref _girar, value); }

    /// <summary>As últimas tentativas da catraca escolhida, depois de passar.</summary>
    public IReadOnlyList<LinhaDeAcesso> Resultados { get => _resultados; private set => Definir(ref _resultados, value); }

    /// <summary>Códigos de teste carregados no modo simulação (installer/simulacao.exemplo.json).</summary>
    public IReadOnlyList<ParDeTexto> CodigosDeTeste { get; } =
    [
        new("1000000001", "QR online · inteira · 1 uso"),
        new("1000000002", "QR online · meia · 1 uso"),
        new("2000000004", "QR online · inteira · 2 usos"),
        new("0000000101", "Cartão da bilheteria · inteira · só na urna"),
        new("0000000102", "Cartão da bilheteria · meia · só na urna"),
        new("9999999999", "Código que não existe"),
    ];

    public override Task AtualizarAsync(CancellationToken cancelamento = default) => Task.CompletedTask;

    private async Task PassarAsync()
    {
        if (!int.TryParse(Catraca, NumberStyles.None, CultureInfo.InvariantCulture, out var inner) || inner < 1)
        {
            Mensagem = "Informe o número da catraca.";
            return;
        }

        await Tentar(async () =>
        {
            var resposta = await Cliente.SimularLeituraAsync(new SimularLeituraRequest
            {
                Inner = inner,
                Codigo = Codigo.Trim(),
                NaUrna = NaUrna,
                Girar = Girar,
            });

            Mensagem = resposta.Mensagem;

            if (!resposta.Aceita)
            {
                return;
            }

            // A catraca simulada decide no laço do worker; o resultado chega em instantes.
            await Task.Delay(EsperaPeloResultado);
            var acessos = await Cliente.ListarAcessosAsync(new ListarAcessosRequest { Inner = inner, Limite = 5 });
            Resultados = [.. acessos.Acessos.Select(LinhaDeAcesso.De)];
            Mensagem = Resultados.Count > 0
                ? $"Catraca {inner}: {Resultados[0].Mensagem}"
                : "A catraca simulada ainda não respondeu. Veja o Painel ao vivo.";
        }).ConfigureAwait(true);
    }
}

/// <summary>A janela: menu lateral, tela atual e cabeçalho de estado.</summary>
public sealed class JanelaViewModel : Notificavel
{
    private ITela _telaAtual;

    public JanelaViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(cliente);

        Painel = new PainelAoVivoViewModel(cliente, relogio);
        Telas =
        [
            Painel,
            new CatracasViewModel(cliente, relogio),
            new AcessosViewModel(cliente, relogio),
            new ConsultaViewModel(cliente, relogio),
            new SincronizacaoViewModel(cliente, relogio),
            new ContasViewModel(cliente, relogio),
            new GerenciarCatracaViewModel(cliente, relogio),
            new GemeoDigitalViewModel(cliente, relogio),
            new ConfiguracoesViewModel(cliente, relogio),
            new DiagnosticoViewModel(cliente, relogio),
            new SimuladorViewModel(cliente, relogio),
        ];
        _telaAtual = Painel;

        // U07: o Simulador só aparece com o modo simulação ligado no serviço. Numa instalação real,
        // a tela que passa QR de teste não fica ao alcance do operador do evento.
        Painel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PainelAoVivoViewModel.Estado))
            {
                return;
            }

            Avisar(nameof(TelasDoMenu));
            if (TelaAtual is SimuladorViewModel && !Painel.Estado.Simulacao)
            {
                TelaAtual = Painel;
            }
        };

        // Etapa A.6: o detalhe da catraca, aberto pela "Gerenciar catraca" (docs/34 §7.3).
        // Fica fora do menu: só faz sentido com uma catraca escolhida.
        Parametrizacao = new ParametrizacaoViewModel(cliente, relogio);
        Parametrizar = new ComandoComParametro(async parametro =>
        {
            if (parametro is not int inner || inner <= 0)
            {
                return;
            }

            // Quem já digitou o nome no Gerenciar ou no gêmeo não precisa digitar de novo.
            var gerenciar = Telas.OfType<GerenciarCatracaViewModel>().First();
            var gemeo = Telas.OfType<GemeoDigitalViewModel>().First();
            if (Parametrizacao.Operador.Length == 0)
            {
                Parametrizacao.Operador = gemeo.Central.Operador.Length > 0 ? gemeo.Central.Operador : gerenciar.Operador;
            }

            var mesmaTela = ReferenceEquals(TelaAtual, Parametrizacao);
            var mesmaCatraca = Parametrizacao.Catraca == inner;
            Parametrizacao.Catraca = inner;

            if (!mesmaTela)
            {
                TelaAtual = Parametrizacao;
            }
            else if (mesmaCatraca)
            {
                await Parametrizacao.AtualizarAsync().ConfigureAwait(true);
            }
        });
        // O gêmeo é a porta principal da configuração da catraca (docs/33 §9): a Parametrização
        // e a Gerenciar têm o atalho "Abrir no gêmeo"; o gêmeo tem "Ver em lista".
        AbrirNoGemeo = new ComandoComParametro(async parametro =>
        {
            if (parametro is not int inner || inner <= 0)
            {
                return;
            }

            var gemeo = Telas.OfType<GemeoDigitalViewModel>().First();
            if (gemeo.Central.Operador.Length == 0)
            {
                var gerenciar = Telas.OfType<GerenciarCatracaViewModel>().First();
                gemeo.Central.Operador = Parametrizacao.Operador.Length > 0 ? Parametrizacao.Operador : gerenciar.Operador;
            }

            gemeo.Catraca = inner;

            if (ReferenceEquals(TelaAtual, gemeo))
            {
                await gemeo.AtualizarAsync().ConfigureAwait(true);
            }
            else
            {
                TelaAtual = gemeo;
            }
        });
        AbrirDiagnostico = new ComandoAssincrono(() =>
        {
            TelaAtual = Telas.OfType<DiagnosticoViewModel>().First();
            return Task.CompletedTask;
        });
        Gerenciar = new ComandoComParametro(async parametro =>
        {
            if (parametro is not int inner)
            {
                return;
            }

            var gerenciar = Telas.OfType<GerenciarCatracaViewModel>().First();
            gerenciar.Catraca = inner;

            if (ReferenceEquals(TelaAtual, gerenciar))
            {
                await gerenciar.AtualizarAsync().ConfigureAwait(true);
            }
            else
            {
                TelaAtual = gerenciar;
            }
        });
        VerAcessos = new ComandoComParametro(async parametro =>
        {
            if (parametro is not int inner)
            {
                return;
            }

            var acessos = Telas.OfType<AcessosViewModel>().First();
            acessos.Catraca = inner.ToString(CultureInfo.InvariantCulture);

            if (ReferenceEquals(TelaAtual, acessos))
            {
                await acessos.AtualizarAsync().ConfigureAwait(true);
            }
            else
            {
                TelaAtual = acessos;
            }
        });
    }

    /// <summary>A Parametrização da catraca (Etapa A.6), aberta pela "Gerenciar catraca".</summary>
    public ParametrizacaoViewModel Parametrizacao { get; }

    /// <summary>Abre a Parametrização de uma catraca (parâmetro: o número do Inner).</summary>
    public ComandoComParametro Parametrizar { get; }

    /// <summary>Abre o gêmeo digital numa catraca, para configurá-la (parâmetro: o número do Inner).</summary>
    public ComandoComParametro AbrirNoGemeo { get; }

    /// <summary>A ação "Gerenciar" do cartão da catraca (parâmetro: o número do Inner).</summary>
    public ComandoComParametro Gerenciar { get; }

    /// <summary>A ação dos cartões de catraca: ir direto ao diagnóstico.</summary>
    public ComandoAssincrono AbrirDiagnostico { get; }

    /// <summary>A outra ação do cartão: os acessos daquela catraca (parâmetro: o número do Inner).</summary>
    public ComandoComParametro VerAcessos { get; }

    /// <summary>O painel ao vivo também alimenta o cabeçalho, em qualquer tela.</summary>
    public PainelAoVivoViewModel Painel { get; }

    public IReadOnlyList<ITela> Telas { get; }

    /// <summary>As telas do menu: o Simulador só entra com o modo simulação ligado (U07).</summary>
    public IReadOnlyList<ITela> TelasDoMenu =>
        [.. Telas.Where(t => t is not SimuladorViewModel || Painel.Estado.Simulacao)];

    public ITela TelaAtual
    {
        get => _telaAtual;
        set
        {
            if (value is not null && Definir(ref _telaAtual, value))
            {
                _ = value.AtualizarAsync();
            }
        }
    }

    /// <summary>
    /// Atualização periódica: o cabeçalho sempre; a tela atual só se ela mostra coisa que
    /// muda sozinha (formulários e consultas não são recarregados por cima do operador).
    /// </summary>
    public async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        await Painel.AtualizarAsync(cancelamento).ConfigureAwait(true);

        if (TelaAtual is CatracasViewModel or SincronizacaoViewModel or DiagnosticoViewModel or GerenciarCatracaViewModel
            or GemeoDigitalViewModel)
        {
            await TelaAtual.AtualizarAsync(cancelamento).ConfigureAwait(true);
        }
        else if (TelaAtual is ParametrizacaoViewModel parametrizacao)
        {
            // Só a situação na catraca e o histórico: os campos não mudam por cima do operador.
            await parametrizacao.AcompanharAsync(cancelamento).ConfigureAwait(true);
        }
    }
}
