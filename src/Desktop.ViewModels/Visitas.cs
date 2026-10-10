using System.Globalization;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;

namespace Desktop.ViewModels;

/// <summary>Uma visita na agenda; documento e credencial aparecem só mascarados.</summary>
public sealed record LinhaDaVisita(string Id, string Nome, string Anfitriao, string Motivo, string Janela,
    string Situacao, string Chegada, string Saida, string Codigo, string Documento, bool PodeReceber, bool PodeEncerrar);

/// <summary>P6: agendar sem credencial, conferir a chegada e encerrar na saída pelo serviço local.</summary>
public sealed class VisitasViewModel : TelaBase
{
    private IReadOnlyList<LinhaDaVisita> _visitas = [];
    private IReadOnlyList<OpcaoDeLista> _anfitrioes = [];
    private LinhaDaVisita? _selecionada;
    private string _filtro = string.Empty;
    private string _buscaAnfitriao = string.Empty;
    private string _anfitriaoId = string.Empty;
    private string _nome = string.Empty;
    private string _motivo = string.Empty;
    private DateTime? _dataDe;
    private DateTime? _dataAte;
    private string _horaDe = string.Empty;
    private string _horaAte = string.Empty;
    private string _tipoDocumento = "rg";
    private string _documento = string.Empty;
    private bool _documentoConferido;
    private string _tipoCredencial = "cartao";
    private string _codigo = string.Empty;
    private bool _saidaConfirmada;
    private int _aba;
    private int _sessao;
    private bool _podeVer = true;
    private bool _podeAgendar = true;
    private bool _podeReceber = true;
    private bool _podeEncerrar = true;

    public VisitasViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null) : base(cliente, relogio)
    {
        Atualizar = new ComandoAssincrono(() => AtualizarAsync(), () => PodeVer);
        BuscarAnfitrioes = new ComandoAssincrono(() => LerAnfitrioesAsync(CancellationToken.None), () => PodeVer);
        NovaVisita = new ComandoAssincrono(() => { LimparFormulario(); Selecionada = null; Aba = 0; return Task.CompletedTask; }, () => PodeAgendar);
        Agendar = new ComandoAssincrono(AgendarAsync, () => PodeAgendar && Nome.Trim().Length > 0 && AnfitriaoId.Length > 0);
        Receber = new ComandoAssincrono(ReceberAsync, () => PodeReceber && Selecionada is { PodeReceber: true }
            && Codigo.Trim().Length > 0 && (DocumentoConferido || Documento.Trim().Length == 0));
        Encerrar = new ComandoAssincrono(EncerrarAsync, () => PodeEncerrar && Selecionada is { PodeEncerrar: true } && SaidaConfirmada);
        LimparFormulario();
    }

    public override string Titulo => "Visitas";
    public ComandoAssincrono Atualizar { get; }
    public ComandoAssincrono BuscarAnfitrioes { get; }
    public ComandoAssincrono NovaVisita { get; }
    public ComandoAssincrono Agendar { get; }
    public ComandoAssincrono Receber { get; }
    public ComandoAssincrono Encerrar { get; }
    public bool PodeVer => _podeVer;
    public bool PodeAgendar => _podeAgendar;
    public bool PodeReceber => _podeReceber;
    public bool PodeEncerrar => _podeEncerrar;
    public bool TemSelecao => Selecionada is not null;
    public IReadOnlyList<LinhaDaVisita> Visitas { get => _visitas; private set => Definir(ref _visitas, value); }
    public IReadOnlyList<OpcaoDeLista> Anfitrioes { get => _anfitrioes; private set => Definir(ref _anfitrioes, value); }
    public IReadOnlyList<OpcaoDeLista> Situacoes { get; } =
        [new("", "Todas"), new("agendada", "Agendadas"), new("em_visita", "Em visita"), new("encerrada", "Encerradas"), new("expirada", "Expiradas")];
    public IReadOnlyList<OpcaoDeLista> TiposDeDocumento { get; } =
        [new("rg", "RG"), new("cpf", "CPF"), new("cnh", "CNH"), new("passaporte", "Passaporte"), new("rne", "RNE"), new("outro", "Outro")];
    public IReadOnlyList<OpcaoDeLista> TiposDeCredencial { get; } =
        [new("cartao", "Crachá/cartão"), new("qr", "QR"), new("senha", "Teclado")];

    public LinhaDaVisita? Selecionada
    {
        get => _selecionada;
        set
        {
            var trocou = _selecionada?.Id != value?.Id;
            if (Definir(ref _selecionada, value))
            {
                if (trocou)
                {
                    LimparChegada();
                    if (value is not null) { Aba = 1; }
                }

                Avisar(nameof(TemSelecao));
                Reavaliar();
            }
        }
    }

    public string Filtro { get => _filtro; set => Definir(ref _filtro, value ?? string.Empty); }
    public string BuscaAnfitriao { get => _buscaAnfitriao; set => Definir(ref _buscaAnfitriao, value); }
    public string AnfitriaoId { get => _anfitriaoId; set { Definir(ref _anfitriaoId, value ?? string.Empty); Reavaliar(); } }
    public string Nome { get => _nome; set { Definir(ref _nome, value); Reavaliar(); } }
    public string Motivo { get => _motivo; set => Definir(ref _motivo, value); }
    public DateTime? DataDe { get => _dataDe; set => Definir(ref _dataDe, value); }
    public DateTime? DataAte { get => _dataAte; set => Definir(ref _dataAte, value); }
    public string HoraDe { get => _horaDe; set => Definir(ref _horaDe, value); }
    public string HoraAte { get => _horaAte; set => Definir(ref _horaAte, value); }
    public string TipoDoDocumento
    {
        get => _tipoDocumento;
        set { if (Definir(ref _tipoDocumento, value ?? "rg")) { DocumentoConferido = false; } }
    }
    public string Documento { get => _documento; set { Definir(ref _documento, value); DocumentoConferido = false; Reavaliar(); } }
    public bool DocumentoConferido { get => _documentoConferido; set { Definir(ref _documentoConferido, value); Reavaliar(); } }
    public string TipoDaCredencial { get => _tipoCredencial; set => Definir(ref _tipoCredencial, value ?? "cartao"); }
    public string Codigo { get => _codigo; set { Definir(ref _codigo, value); Reavaliar(); } }
    public bool SaidaConfirmada { get => _saidaConfirmada; set { Definir(ref _saidaConfirmada, value); Reavaliar(); } }
    public int Aba { get => _aba; set => Definir(ref _aba, value); }

    /// <summary>Ao sair/trocar a sessão, descarta dados e respostas pendentes da sessão anterior.</summary>
    public void DefinirPermissoes(bool ver, bool agendar, bool receber, bool encerrar)
    {
        _sessao++;
        _podeVer = ver;
        _podeAgendar = agendar && ver;
        _podeReceber = receber && ver;
        _podeEncerrar = encerrar && ver;
        Visitas = [];
        Anfitrioes = [];
        Selecionada = null;
        BuscaAnfitriao = string.Empty;
        Filtro = string.Empty;
        Mensagem = string.Empty;
        LimparFormulario();
        LimparChegada();
        Aba = 0;
        Avisar(nameof(PodeVer)); Avisar(nameof(PodeAgendar)); Avisar(nameof(PodeReceber)); Avisar(nameof(PodeEncerrar));
        Reavaliar();
    }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        if (!PodeVer) { return; }
        var sessao = _sessao;
        await Tentar(async () =>
        {
            var r = await Cliente.ListarVisitasAsync(new ListarVisitasRequest { Situacao = Filtro, Limite = 200 }, cancellationToken: cancelamento);
            if (sessao != _sessao || !PodeVer) { return; }
            var id = Selecionada?.Id;
            Visitas = [.. r.Visitas.Select(v => new LinhaDaVisita(v.Id, v.Nome, v.Anfitriao, v.Motivo,
                Data(v.De) + " — " + Data(v.Ate), Rotulo(v.Situacao), Data(v.Chegada), Data(v.Saida),
                v.CodigoMascarado, v.DocumentoMascarado, v.Situacao == "agendada", v.Chegada is not null && v.Saida is null))];
            Selecionada = Visitas.FirstOrDefault(v => v.Id == id);
        }).ConfigureAwait(true);
        if (sessao == _sessao) { await LerAnfitrioesAsync(cancelamento).ConfigureAwait(true); }
    }

    private async Task LerAnfitrioesAsync(CancellationToken cancelamento)
    {
        if (!PodeVer) { return; }
        var sessao = _sessao;
        await Tentar(async () =>
        {
            var r = await Cliente.BuscarAnfitrioesDeVisitaAsync(new BuscarAnfitrioesDeVisitaRequest { Texto = BuscaAnfitriao, Limite = 200 }, cancellationToken: cancelamento);
            if (sessao != _sessao || !PodeVer) { return; }
            Anfitrioes = [.. r.Anfitrioes.OrderBy(a => a.Nome, StringComparer.OrdinalIgnoreCase).Select(a => new OpcaoDeLista(a.Id, a.Nome))];
        }).ConfigureAwait(true);
    }

    private async Task AgendarAsync()
    {
        if (DataDe is null || DataAte is null || !Periodo.TentarLerHora(HoraDe, out var de) || de is null
            || !Periodo.TentarLerHora(HoraAte, out var ate) || ate is null)
        {
            Mensagem = "Informe as duas datas e horas (hh:mm), no horário de Brasília.";
            return;
        }

        var inicio = FusoDoEvento.DoEvento(DataDe.Value.Date + de.Value);
        var fim = FusoDoEvento.DoEvento(DataAte.Value.Date + ate.Value);
        if (fim <= inicio) { Mensagem = "O fim da janela precisa vir depois do início."; return; }
        var sessao = _sessao;
        string? id = null;
        await Tentar(async () =>
        {
            var r = await Cliente.AgendarVisitaAsync(new AgendarVisitaRequest
            {
                Nome = Nome, AnfitriaoId = AnfitriaoId, Motivo = Motivo,
                De = Timestamp.FromDateTimeOffset(inicio), Ate = Timestamp.FromDateTimeOffset(fim),
            });
            if (sessao != _sessao) { return; }
            Mensagem = r.Gravado ? "Visita agendada. O acesso só será autorizado após a chegada conferida." : string.Join(" ", r.Problemas);
            if (r.Gravado) { id = r.Id; LimparFormulario(); Filtro = string.Empty; }
        }).ConfigureAwait(true);
        if (sessao != _sessao) { return; }
        await AtualizarAsync().ConfigureAwait(true);
        if (sessao == _sessao && id is not null) { Selecionada = Visitas.FirstOrDefault(v => v.Id == id); }
    }

    private async Task ReceberAsync()
    {
        if (Selecionada is not { } visita) { return; }
        var sessao = _sessao;
        await Tentar(async () =>
        {
            var r = await Cliente.ReceberVisitaAsync(new ReceberVisitaRequest
            {
                Id = visita.Id, TipoDoDocumento = TipoDoDocumento, Documento = Documento, DocumentoConferido = DocumentoConferido,
                TipoDoCodigo = TipoDaCredencial, Codigo = Codigo,
            });
            if (sessao != _sessao) { return; }
            Mensagem = r.Gravado ? "Chegada registrada. Credencial válida somente até o fim da janela agendada." : string.Join(" ", r.Problemas);
            if (r.Gravado) { LimparChegada(); }
        }).ConfigureAwait(true);
        if (sessao == _sessao) { await AtualizarAsync().ConfigureAwait(true); }
    }

    private async Task EncerrarAsync()
    {
        if (Selecionada is not { } visita) { return; }
        var sessao = _sessao;
        await Tentar(async () =>
        {
            var r = await Cliente.EncerrarVisitaAsync(new EncerrarVisitaRequest { Id = visita.Id });
            if (sessao != _sessao) { return; }
            Mensagem = r.Gravado ? "Saída registrada. A credencial desta visita não autoriza mais acesso." : string.Join(" ", r.Problemas);
            if (r.Gravado) { SaidaConfirmada = false; }
        }).ConfigureAwait(true);
        if (sessao == _sessao) { await AtualizarAsync().ConfigureAwait(true); }
    }

    private void LimparFormulario()
    {
        Nome = string.Empty; Motivo = string.Empty; AnfitriaoId = string.Empty;
        var agora = FusoDoEvento.NoEvento(Relogio());
        DataDe = agora.Date; HoraDe = agora.ToString("HH:mm", CultureInfo.InvariantCulture);
        DataAte = agora.AddHours(1).Date; HoraAte = agora.AddHours(1).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private void LimparChegada()
    {
        Documento = string.Empty; DocumentoConferido = false; Codigo = string.Empty; SaidaConfirmada = false;
    }

    private void Reavaliar()
    {
        Agendar?.ReavaliarDisponibilidade(); Receber?.ReavaliarDisponibilidade(); Encerrar?.ReavaliarDisponibilidade();
        Atualizar?.ReavaliarDisponibilidade(); BuscarAnfitrioes?.ReavaliarDisponibilidade(); NovaVisita?.ReavaliarDisponibilidade();
    }

    private static string Data(Timestamp? valor) => valor is null ? string.Empty
        : FusoDoEvento.NoEvento(valor.ToDateTimeOffset()).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Rotulo(string valor) => valor switch
    {
        "agendada" => "Agendada", "em_visita" => "Em visita", "encerrada" => "Encerrada", "expirada" => "Expirada", _ => valor,
    };
}
