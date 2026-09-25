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
            Mensagem = erro.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? "Sem resposta do serviço local. As catracas continuam funcionando; confira se o serviço está iniciado."
                : $"O serviço recusou o pedido ({erro.StatusCode}).";
            return false;
        }
        finally
        {
            Ocupada = false;
        }
    }

    protected static Timestamp? Instante(DateTime? local) =>
        local is { } l ? Timestamp.FromDateTimeOffset(new DateTimeOffset(DateTime.SpecifyKind(l, DateTimeKind.Local))) : null;
}

/// <summary>Painel ao vivo: o evento num relance.</summary>
public sealed class PainelAoVivoViewModel : TelaBase
{
    /// <summary>Quantos acessos ficam na lista ao vivo.</summary>
    public const int AcessosNaTela = 100;

    private EstadoDoPainel _estado = EstadoDoPainel.Carregando();
    private IReadOnlyList<LinhaDeCatraca> _catracas = [];
    private string _resumo = string.Empty;
    private string _internet = string.Empty;
    private long _liberados;
    private long _negados;
    private long _giros;
    private long _ultimos5;
    private long _pendentes;

    public PainelAoVivoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
    }

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

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        var ok = await Tentar(async () =>
        {
            var estado = await Cliente.ObterEstadoAsync(new ObterEstadoRequest(), cancellationToken: cancelamento);
            var lista = await Cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento);
            var agora = Relogio();

            Estado = EstadoDoPainel.De(estado, agora);
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
                    ? "Nuvem: sem sincronização — as catracas funcionam normalmente"
                    : $"Nuvem: sem internet desde {Textos.Ha(estado.UltimaSincronizacao.ToDateTimeOffset(), agora)} — as catracas funcionam normalmente";
            Mensagem = string.Empty;
        }).ConfigureAwait(true);

        if (!ok)
        {
            Estado = Estado.ComFalhaDeComunicacao(Mensagem, Relogio());
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

    /// <summary>Põe um acesso no topo da lista, mantendo o tamanho.</summary>
    public void Acrescentar(LinhaDeAcesso linha)
    {
        UltimosAcessos.Insert(0, linha);

        while (UltimosAcessos.Count > AcessosNaTela)
        {
            UltimosAcessos.RemoveAt(UltimosAcessos.Count - 1);
        }
    }

    internal static LinhaDeCatraca Linha(Equipamento e, DateTimeOffset agora)
    {
        var (texto, sinal) = Textos.SituacaoDaCatraca(e);

        return new LinhaDeCatraca(
            e.Inner,
            string.IsNullOrWhiteSpace(e.NomeDoGate) ? $"Catraca {e.Inner}" : e.NomeDoGate,
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
            e.TentativasDeReconexao);
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

    public AcessosViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Buscar = new ComandoAssincrono(() => AtualizarAsync());
    }

    public override string Titulo => "Acessos";

    public ComandoAssincrono Buscar { get; }

    /// <summary>Número da catraca; vazio = todas.</summary>
    public string Catraca { get => _catraca; set => Definir(ref _catraca, value ?? string.Empty); }

    /// <summary>Opções do filtro de resultado, na ordem de <see cref="Resultado"/>.</summary>
    public IReadOnlyList<string> OpcoesDeResultado { get; } = ["Todos", "Liberados", "Negados"];

    /// <summary>0 todos, 1 liberados, 2 negados.</summary>
    public int Resultado { get => _resultado; set => Definir(ref _resultado, Math.Clamp(value, 0, 2)); }

    public string Categoria { get => _categoria; set => Definir(ref _categoria, value ?? string.Empty); }

    public DateTime? Desde { get => _desde; set => Definir(ref _desde, value); }

    public DateTime? Ate { get => _ate; set => Definir(ref _ate, value); }

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

        await Tentar(async () =>
        {
            var pedido = new ListarAcessosRequest
            {
                Inner = inner,
                Resultado = (FiltroDeResultado)Resultado,
                Categoria = Categoria.Trim(),
                Limite = 500,
            };

            if (Instante(Desde) is { } desde)
            {
                pedido.Desde = desde;
            }

            if (Instante(Ate) is { } ate)
            {
                pedido.Ate = ate;
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
    private IReadOnlyList<(string Rotulo, string Valor)> _detalhes = [];
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

    public IReadOnlyList<(string Rotulo, string Valor)> Detalhes { get => _detalhes; private set => Definir(ref _detalhes, value); }

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
                ("Código", r.CodigoMascarado),
                ("Origem", r.Provedor),
                ("Categoria", string.IsNullOrWhiteSpace(r.Categoria) ? "—" : r.Categoria),
                ("Situação", r.Situacao switch
                {
                    "valido" => "Válido",
                    "consumido" => "Já utilizado",
                    "cancelado" => "Cancelado",
                    "bloqueado" => "Bloqueado",
                    _ => r.Situacao,
                }),
                ("Usos", r.UsosMaximos == 0
                    ? string.Create(CultureInfo.CurrentCulture, $"{r.UsosFeitos} (sem limite)")
                    : string.Create(CultureInfo.CurrentCulture, $"{r.UsosFeitos} de {r.UsosMaximos}")),
                ("Último uso", Textos.Ha(r.UltimoUso?.ToDateTimeOffset(), agora)),
            ];
            Mensagem = string.Empty;
        }).ConfigureAwait(true);
    }
}

/// <summary>Sincronização com a nuvem e com os sites de ingresso.</summary>
public sealed class SincronizacaoViewModel : TelaBase
{
    private IReadOnlyList<(string Rotulo, string Valor)> _situacao = [];
    private IReadOnlyList<ProvedorCadastrado> _provedores = [];
    private Sinal _sinal;

    public SincronizacaoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
    }

    public override string Titulo => "Sincronização";

    public IReadOnlyList<(string Rotulo, string Valor)> Situacao { get => _situacao; private set => Definir(ref _situacao, value); }

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
                ("Nuvem", r.Configurada ? "Configurada" : "Não configurada nesta instalação"),
                ("Última sincronização", Textos.Ha(ultima, agora)),
                ("Última falha", string.IsNullOrWhiteSpace(r.UltimaFalha) ? "—" : r.UltimaFalha),
                ("Aguardando envio", string.Create(CultureInfo.CurrentCulture, $"{r.Pendentes}")),
                ("Mais antigo na fila", r.Pendentes == 0 ? "—" : Textos.Ha(agora.AddSeconds(-r.IdadeDoMaisAntigoSegundos), agora)),
                ("Recusados pela nuvem", string.Create(CultureInfo.CurrentCulture, $"{r.CartasMortas}")),
            ];
            Provedores = [.. r.Provedores];
            Mensagem = string.Empty;
        });
}

/// <summary>Prestação de contas: o que entrou, por onde, de que categoria, e o que foi negado.</summary>
public sealed class ContasViewModel : TelaBase
{
    private DateTime? _desde;
    private DateTime? _ate;
    private PrestacaoDeContas? _contas;

    public ContasViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Gerar = new ComandoAssincrono(() => AtualizarAsync());
        var hoje = Relogio().LocalDateTime.Date;
        _desde = hoje;
    }

    public override string Titulo => "Prestação de contas";

    public ComandoAssincrono Gerar { get; }

    public DateTime? Desde { get => _desde; set => Definir(ref _desde, value); }

    /// <summary>Vazio = até agora.</summary>
    public DateTime? Ate { get => _ate; set => Definir(ref _ate, value); }

    public PrestacaoDeContas? Contas { get => _contas; private set => Definir(ref _contas, value); }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        if (Desde is { } d && Ate is { } a && a < d)
        {
            Mensagem = "O fim do período é antes do começo.";
            return;
        }

        await Tentar(async () =>
        {
            var pedido = new ObterPrestacaoDeContasRequest();

            if (Instante(Desde) is { } desde)
            {
                pedido.Desde = desde;
            }

            pedido.Ate = Instante(Ate) ?? Timestamp.FromDateTimeOffset(Relogio());
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

        csv.AppendLine(string.Create(cultura, $"Prestação de contas;gerada em {c.GeradaEm.ToDateTimeOffset().ToLocalTime():dd/MM/yyyy HH:mm:ss}"));
        csv.AppendLine(string.Create(cultura, $"Liberados;{c.Liberados}"));
        csv.AppendLine(string.Create(cultura, $"Com giro confirmado;{c.Giros}"));
        csv.AppendLine(string.Create(cultura, $"Negados;{c.Negados}"));
        csv.AppendLine();
        csv.AppendLine("Categoria;Liberados;Com giro");
        foreach (var l in c.PorCategoria)
        {
            csv.AppendLine(string.Create(cultura, $"{Celula(l.Categoria)};{l.Liberados};{l.Giros}"));
        }

        csv.AppendLine();
        csv.AppendLine("Catraca;Liberados;Com giro;Negados");
        foreach (var l in c.PorCatraca)
        {
            csv.AppendLine(string.Create(cultura, $"{l.Inner};{l.Liberados};{l.Giros};{l.Negados}"));
        }

        csv.AppendLine();
        csv.AppendLine("Hora;Liberados;Negados");
        foreach (var l in c.PorHora)
        {
            csv.AppendLine(string.Create(cultura, $"{l.Hora.ToDateTimeOffset().ToLocalTime():dd/MM/yyyy HH}:00;{l.Liberados};{l.Negados}"));
        }

        csv.AppendLine();
        csv.AppendLine("Motivo da negativa;Quantidade");
        foreach (var l in c.Negativas)
        {
            csv.AppendLine(string.Create(cultura, $"{Celula(l.Mensagem)};{l.Quantidade}"));
        }

        return csv.ToString();
    }

    /// <summary>Grava o CSV no caminho escolhido pelo operador.</summary>
    public async Task ExportarAsync(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);

        if (Contas is null)
        {
            Mensagem = "Gere a prestação de contas antes de exportar.";
            return;
        }

        await File.WriteAllTextAsync(caminho, ParaCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)).ConfigureAwait(true);
        Mensagem = $"Exportado para {caminho}";
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
    private bool _nuvemLigada;
    private IReadOnlyList<string> _problemas = [];

    public ConfiguracoesViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Salvar = new ComandoAssincrono(SalvarAsync);
    }

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
            Mensagem = !r.Gravada
                ? "Não foi gravado. Corrija os itens abaixo."
                : r.ExigeReinicio
                    ? "Gravado. As catracas passam a usar a nova configuração quando o serviço for reiniciado."
                    : "Nada mudou.";
        }).ConfigureAwait(true);
}

/// <summary>Diagnóstico: o que o suporte precisa ver.</summary>
public sealed class DiagnosticoViewModel : TelaBase
{
    private Diagnostico? _diagnostico;

    public DiagnosticoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
    }

    public override string Titulo => "Diagnóstico";

    public Diagnostico? Diagnostico { get => _diagnostico; private set => Definir(ref _diagnostico, value); }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            Diagnostico = await Cliente.ObterDiagnosticoAsync(new ObterDiagnosticoRequest(), cancellationToken: cancelamento);
            Mensagem = string.Empty;
        });
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
            new ConfiguracoesViewModel(cliente, relogio),
            new DiagnosticoViewModel(cliente, relogio),
        ];
        _telaAtual = Painel;
    }

    /// <summary>O painel ao vivo também alimenta o cabeçalho, em qualquer tela.</summary>
    public PainelAoVivoViewModel Painel { get; }

    public IReadOnlyList<ITela> Telas { get; }

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

        if (TelaAtual is CatracasViewModel or SincronizacaoViewModel or DiagnosticoViewModel)
        {
            await TelaAtual.AtualizarAsync(cancelamento).ConfigureAwait(true);
        }
    }
}
