using System.Globalization;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;

namespace Desktop.ViewModels;

/// <summary>Uma opção de lista: o valor gravado e o nome que o operador lê.</summary>
public sealed record OpcaoDaLista(int Valor, string Nome);

/// <summary>Uma opção de lista com valor de texto (o do contrato) e o nome que o operador lê.</summary>
public sealed record OpcaoDeTexto(string Valor, string Nome);

/// <summary>Uma seção ainda bloqueada da tela: o que ela vai ter e do que depende.</summary>
public sealed record SecaoBloqueada(string Titulo, string Motivo);

/// <summary>
/// As seções das configurações do evento (docs/57 §2): Evento, Tipos de entrada, Cartões da
/// bilheteria, Padrão das catracas, as bloqueadas com o motivo e o Avançado. Cada seção salva sozinha.
/// </summary>
public sealed partial class ConfiguracoesViewModel
{
    private string _nomeDoEvento = string.Empty;
    private DateTime? _inicioDoEvento;
    private string _horaDeInicio = string.Empty;
    private DateTime? _fimDoEvento;
    private string _horaDeFim = string.Empty;
    private string _horaDeCorte = "00:00";
    private bool _recusarForaDoPeriodo;
    private string _eventoAlterado = string.Empty;
    private string _situacaoDoPeriodo = string.Empty;
    private IReadOnlyList<string> _problemasDoEvento = [];

    private IReadOnlyList<TipoDeEntrada> _tipos = [];
    private TipoDeEntrada? _tipoSelecionado;
    private string _codigoDoTipo = string.Empty;
    private string _nomeDoTipo = string.Empty;
    private string _ordemDoTipo = "0";
    private bool _tipoAtivo = true;
    private bool _tipoNovo = true;
    private IReadOnlyList<string> _problemasDoTipo = [];

    private bool _bilheteriaExiste;
    private bool _bilheteriaDaNuvem;
    private string _intervaloDeReuso = "4";
    private bool _somenteNaUrna = true;
    private string _resumoDaBilheteria = string.Empty;
    private IReadOnlyList<string> _problemasDaBilheteria = [];

    private IReadOnlyList<ChaveTecnica> _chavesTecnicas = [];

    // Números como no Brasil (2.243; 2,5) sem depender da cultura instalada no Windows.
    private static readonly NumberFormatInfo Numeros = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    /// <summary>Os tipos de leitor da catraca, com o nome (o mesmo texto da Parametrização).</summary>
    public static IReadOnlyList<OpcaoDaLista> TiposDeLeitor { get; } =
    [
        new(0, "0 · barras"),
        new(1, "1 · magnético"),
        new(2, "2 · proximidade Abatrack2"),
        new(3, "3 · Wiegand"),
        new(4, "4 · proximidade SmartCard serial"),
        new(5, "5 · leitor de código de barras (serial)"),
        new(6, "6 · Wiegand FC sem separador"),
        new(7, "7 · Wiegand FC com separador"),
        new(8, "8 · leitor de QR Code"),
    ];

    /// <summary>
    /// O que ainda não pode ser configurado, e do que depende (docs/57 §6). Aparece desabilitado:
    /// nada some, nada finge que existe.
    /// </summary>
    public static IReadOnlyList<SecaoBloqueada> SecoesBloqueadas { get; } =
    [
        new("Contingência (catraca sem o computador)",
            "Lista de ingressos gravada na catraca e troca automática para off-line. Aguardando as decisões D3, D4 e D8 do dono do produto e o ensaio na bancada (docs/34 §9)."),
        new("Alertas",
            "Limites para avisar catraca parada, negativas fora do normal, urna cheia e fila da nuvem parada, e como avisar (som, bandeja). Aguardando a definição com a operação (Fase 5, docs/29)."),
        new("Segurança da catraca",
            "Cartão master gerado e cifrado, e o servidor web da catraca desligado depois da instalação. Aguardando o ensaio na bancada (T13, T32)."),
        new("Mensagens do visor",
            "Mensagem de apresentação sem o número do cartão (LGPD), de catraca off-line e de acesso negado. Aguardando a Topdata (NOVO-INT-MSG-03, 04 e 05)."),
        new("Setor por catraca",
            "Qual catraca aceita qual setor. Aguardando decisão: os ingressos e cartões de hoje não têm setor."),
    ];

    // ---------- Evento ----------

    public string NomeDoEvento { get => _nomeDoEvento; set => Definir(ref _nomeDoEvento, value ?? string.Empty); }

    public DateTime? InicioDoEvento { get => _inicioDoEvento; set => Definir(ref _inicioDoEvento, value); }

    public string HoraDeInicio { get => _horaDeInicio; set => Definir(ref _horaDeInicio, value ?? string.Empty); }

    public DateTime? FimDoEvento { get => _fimDoEvento; set => Definir(ref _fimDoEvento, value); }

    public string HoraDeFim { get => _horaDeFim; set => Definir(ref _horaDeFim, value ?? string.Empty); }

    /// <summary>Hora em que um dia de operação vira o seguinte, "HH:MM", horário de Brasília.</summary>
    public string HoraDeCorte { get => _horaDeCorte; set => Definir(ref _horaDeCorte, value ?? string.Empty); }

    public bool RecusarForaDoPeriodo { get => _recusarForaDoPeriodo; set => Definir(ref _recusarForaDoPeriodo, value); }

    /// <summary>"Alterado por … em …", ou que o evento ainda não foi definido.</summary>
    public string EventoAlterado { get => _eventoAlterado; private set => Definir(ref _eventoAlterado, value); }

    /// <summary>Onde o agora cai no período gravado: antes, durante, depois ou sem período.</summary>
    public string SituacaoDoPeriodo { get => _situacaoDoPeriodo; private set => Definir(ref _situacaoDoPeriodo, value); }

    public IReadOnlyList<string> ProblemasDoEvento { get => _problemasDoEvento; private set => Definir(ref _problemasDoEvento, value); }

    public ComandoAssincrono SalvarEvento { get; private set; } = null!;

    // ---------- Tipos de entrada ----------

    public IReadOnlyList<TipoDeEntrada> TiposDeEntrada { get => _tipos; private set => Definir(ref _tipos, value); }

    /// <summary>O tipo escolhido na lista: os campos abaixo passam a ser dele.</summary>
    public TipoDeEntrada? TipoSelecionado
    {
        get => _tipoSelecionado;
        set
        {
            if (Definir(ref _tipoSelecionado, value) && value is not null)
            {
                CodigoDoTipo = value.Codigo;
                NomeDoTipo = value.Nome;
                OrdemDoTipo = value.Ordem.ToString(CultureInfo.InvariantCulture);
                TipoAtivo = value.Ativo;
                TipoNovo = false;
                ProblemasDoTipo = [];
            }
        }
    }

    public string CodigoDoTipo { get => _codigoDoTipo; set => Definir(ref _codigoDoTipo, value ?? string.Empty); }

    public string NomeDoTipo { get => _nomeDoTipo; set => Definir(ref _nomeDoTipo, value ?? string.Empty); }

    public string OrdemDoTipo { get => _ordemDoTipo; set => Definir(ref _ordemDoTipo, value ?? string.Empty); }

    /// <summary>Desativado: a catraca nega todos os cartões do tipo, com o motivo "tipo de entrada desativado".</summary>
    public bool TipoAtivo { get => _tipoAtivo; set => Definir(ref _tipoAtivo, value); }

    /// <summary>Criando um tipo: só aí o código pode ser digitado. Depois de criado, ele não muda.</summary>
    public bool TipoNovo { get => _tipoNovo; private set => Definir(ref _tipoNovo, value); }

    public IReadOnlyList<string> ProblemasDoTipo { get => _problemasDoTipo; private set => Definir(ref _problemasDoTipo, value); }

    public ComandoAssincrono NovoTipo { get; private set; } = null!;

    public ComandoAssincrono SalvarTipo { get; private set; } = null!;

    // ---------- Cartões da bilheteria ----------

    public bool BilheteriaExiste { get => _bilheteriaExiste; private set => Definir(ref _bilheteriaExiste, value); }

    /// <summary>Com a nuvem ligada, as regras vêm dela e não mudam aqui (ADR-0025).</summary>
    public bool BilheteriaDaNuvem { get => _bilheteriaDaNuvem; private set => Definir(ref _bilheteriaDaNuvem, value); }

    /// <summary>As regras podem ser mudadas aqui: a bilheteria existe e a nuvem não manda nelas.</summary>
    public bool PodeMudarBilheteria => BilheteriaExiste && !BilheteriaDaNuvem;

    /// <summary>Minutos entre dois usos do mesmo cartão (0 a 120).</summary>
    public string IntervaloDeReuso { get => _intervaloDeReuso; set => Definir(ref _intervaloDeReuso, value ?? string.Empty); }

    public bool SomenteNaUrna { get => _somenteNaUrna; set => Definir(ref _somenteNaUrna, value); }

    /// <summary>Quantos cartões, quantos desativados, a última carga e de onde vêm as regras.</summary>
    public string ResumoDaBilheteria { get => _resumoDaBilheteria; private set => Definir(ref _resumoDaBilheteria, value); }

    public IReadOnlyList<string> ProblemasDaBilheteria { get => _problemasDaBilheteria; private set => Definir(ref _problemasDaBilheteria, value); }

    public ComandoAssincrono SalvarBilheteria { get; private set; } = null!;

    // ---------- Avançado ----------

    /// <summary>As chaves técnicas, só leitura, com o ensaio que libera cada uma.</summary>
    public IReadOnlyList<ChaveTecnica> ChavesTecnicas { get => _chavesTecnicas; private set => Definir(ref _chavesTecnicas, value); }

    private void IniciarSecoes()
    {
        SalvarEvento = new ComandoAssincrono(SalvarEventoAsync);
        NovoTipo = new ComandoAssincrono(() =>
        {
            _tipoSelecionado = null;
            Avisar(nameof(TipoSelecionado));
            CodigoDoTipo = string.Empty;
            NomeDoTipo = string.Empty;
            OrdemDoTipo = ((TiposDeEntrada.Count == 0 ? 0 : TiposDeEntrada.Max(t => t.Ordem)) + 10).ToString(CultureInfo.InvariantCulture);
            TipoAtivo = true;
            TipoNovo = true;
            ProblemasDoTipo = [];
            return Task.CompletedTask;
        });
        SalvarTipo = new ComandoAssincrono(SalvarTipoAsync);
        SalvarBilheteria = new ComandoAssincrono(SalvarBilheteriaAsync, () => PodeMudarBilheteria);
    }

    private async Task AtualizarSecoesAsync(CancellationToken cancelamento)
    {
        var evento = await Cliente.ObterEventoAsync(new ObterEventoRequest(), cancellationToken: cancelamento);
        MostrarEvento(evento);

        await CarregarTiposAsync(cancelamento).ConfigureAwait(true);

        var bilheteria = await Cliente.ObterBilheteriaLocalAsync(new ObterBilheteriaLocalRequest(), cancellationToken: cancelamento);
        MostrarBilheteria(bilheteria);

        var chaves = await Cliente.ListarChavesTecnicasAsync(new ListarChavesTecnicasRequest(), cancellationToken: cancelamento);
        ChavesTecnicas = [.. chaves.Chaves];
    }

    private void MostrarEvento(DadosDoEvento evento)
    {
        NomeDoEvento = evento.Nome;
        (InicioDoEvento, HoraDeInicio) = DataEHora(evento.Inicio);
        (FimDoEvento, HoraDeFim) = DataEHora(evento.Fim);
        HoraDeCorte = string.IsNullOrEmpty(evento.HoraDeCorte) ? "00:00" : evento.HoraDeCorte;
        RecusarForaDoPeriodo = evento.RecusarForaDoPeriodo;
        EventoAlterado = evento.AlteradoEm is null || string.IsNullOrEmpty(evento.AlteradoPor)
            ? "O evento ainda não foi definido nesta instalação."
            : $"Alterado por {evento.AlteradoPor} em {FusoDoEvento.NoEvento(evento.AlteradoEm.ToDateTimeOffset()):dd/MM/yyyy HH:mm}.";
        SituacaoDoPeriodo = Situacao(evento.Inicio?.ToDateTimeOffset(), evento.Fim?.ToDateTimeOffset(), evento.RecusarForaDoPeriodo, Relogio());
    }

    private static (DateTime? Dia, string Hora) DataEHora(Timestamp? instante)
    {
        if (instante is null)
        {
            return (null, string.Empty);
        }

        var noEvento = FusoDoEvento.NoEvento(instante.ToDateTimeOffset());
        return (noEvento.Date, noEvento.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>Onde o instante cai no período, em português de portaria.</summary>
    public static string Situacao(DateTimeOffset? inicio, DateTimeOffset? fim, bool recusar, DateTimeOffset agora)
    {
        if (inicio is null && fim is null)
        {
            return "Sem período definido: a catraca aceita ingressos a qualquer hora.";
        }

        var efeito = recusar ? "a catraca recusa ingressos" : "a catraca aceita ingressos mesmo assim (a recusa está desligada)";
        if (inicio is { } i && agora < i)
        {
            return $"Agora: antes do início do evento — {efeito}.";
        }

        if (fim is { } f && agora > f)
        {
            return $"Agora: depois do fim do evento — {efeito}.";
        }

        return "Agora: dentro do período do evento.";
    }

    private async Task SalvarEventoAsync() =>
        await Tentar(async () =>
        {
            if (!Periodo.TentarLerHora(HoraDeInicio, out var hInicio) || !Periodo.TentarLerHora(HoraDeFim, out var hFim))
            {
                ProblemasDoEvento = ["Hora no formato HH:MM, de 00:00 a 23:59 — ou deixe vazio."];
                return;
            }

            if (!Periodo.TentarLerHora(HoraDeCorte, out var corte) || corte is null)
            {
                ProblemasDoEvento = ["A hora de corte vai de 00:00 a 23:59, no formato HH:MM."];
                return;
            }

            var agora = Relogio();
            var evento = new DadosDoEvento
            {
                Nome = NomeDoEvento.Trim(),
                HoraDeCorte = corte.Value.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                RecusarForaDoPeriodo = RecusarForaDoPeriodo,
            };

            // Sem hora, o início vale do começo do dia e o fim até o fim do dia.
            if (Periodo.Inicio(InicioDoEvento, InicioDoEvento is null ? null : hInicio, agora) is { } inicio)
            {
                evento.Inicio = Timestamp.FromDateTimeOffset(inicio);
            }

            if (Periodo.Fim(FimDoEvento, FimDoEvento is null ? null : hFim, agora) is { } fim)
            {
                evento.Fim = Timestamp.FromDateTimeOffset(fim);
            }

            var r = await Cliente.GravarEventoAsync(new GravarEventoRequest { Evento = evento });
            ProblemasDoEvento = [.. r.Problemas];
            if (r.Gravada)
            {
                MostrarEvento(await Cliente.ObterEventoAsync(new ObterEventoRequest()));
                Mensagem = "Evento gravado.";
            }
        }).ConfigureAwait(true);

    private async Task CarregarTiposAsync(CancellationToken cancelamento = default)
    {
        var tipos = await Cliente.ListarTiposDeEntradaAsync(new ListarTiposDeEntradaRequest(), cancellationToken: cancelamento);
        var escolhido = _tipoSelecionado?.Codigo;
        TiposDeEntrada = [.. tipos.Tipos];
        if (escolhido is not null)
        {
            _tipoSelecionado = TiposDeEntrada.FirstOrDefault(t => t.Codigo == escolhido);
            Avisar(nameof(TipoSelecionado));
        }
    }

    private async Task SalvarTipoAsync() =>
        await Tentar(async () =>
        {
            if (!int.TryParse(OrdemDoTipo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ordem))
            {
                ProblemasDoTipo = ["A ordem é um número de 0 a 9999."];
                return;
            }

            var codigo = CodigoDoTipo.Trim().ToUpperInvariant();
            var r = await Cliente.GravarTipoDeEntradaAsync(new GravarTipoDeEntradaRequest
            {
                Tipo = new TipoDeEntrada { Codigo = codigo, Nome = NomeDoTipo.Trim(), Ordem = ordem, Ativo = TipoAtivo },
            });

            ProblemasDoTipo = [.. r.Problemas];
            if (r.Gravada)
            {
                _tipoSelecionado = new TipoDeEntrada { Codigo = codigo };
                await CarregarTiposAsync().ConfigureAwait(true);
                TipoNovo = false;
                Mensagem = TipoAtivo
                    ? $"Tipo {codigo} gravado."
                    : $"Tipo {codigo} gravado e desativado: a catraca nega os cartões dele a partir de agora.";
            }
        }).ConfigureAwait(true);

    private void MostrarBilheteria(RegrasDaBilheteria b)
    {
        BilheteriaExiste = b.Existe;
        BilheteriaDaNuvem = b.DefinidaPelaNuvem;
        Avisar(nameof(PodeMudarBilheteria));
        SalvarBilheteria.ReavaliarDisponibilidade();

        if (!b.Existe)
        {
            ResumoDaBilheteria = "A bilheteria local ainda não existe nesta base: ela é criada quando o serviço sobe.";
            return;
        }

        IntervaloDeReuso = (b.IntervaloDeReusoSegundos / 60.0).ToString("0.##", Numeros);
        SomenteNaUrna = b.SomenteNaUrna;

        var resumo = $"{b.Ingressos.ToString("N0", Numeros)} cartão(ões) na base, {b.Desativados.ToString("N0", Numeros)} desativado(s).";
        if (b.UltimaCargaEm is { } carga)
        {
            resumo += $" Última carga de arquivos: {FusoDoEvento.NoEvento(carga.ToDateTimeOffset()):dd/MM/yyyy HH:mm}, {b.UltimaCargaQuantidade.ToString("N0", Numeros)} cartão(ões).";
        }

        if (b.DefinidaPelaNuvem)
        {
            resumo += " Com a nuvem ligada, as regras vêm dela e não mudam por aqui.";
        }

        ResumoDaBilheteria = resumo;
    }

    private async Task SalvarBilheteriaAsync() =>
        await Tentar(async () =>
        {
            if (!double.TryParse(IntervaloDeReuso.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var minutos)
                || minutos is < 0 or > 120)
            {
                ProblemasDaBilheteria = ["O intervalo entre usos vai de 0 a 120 minutos."];
                return;
            }

            var r = await Cliente.GravarBilheteriaLocalAsync(new GravarBilheteriaLocalRequest
            {
                IntervaloDeReusoSegundos = (int)Math.Round(minutos * 60),
                SomenteNaUrna = SomenteNaUrna,
            });

            ProblemasDaBilheteria = [.. r.Problemas];
            if (r.Gravada)
            {
                MostrarBilheteria(await Cliente.ObterBilheteriaLocalAsync(new ObterBilheteriaLocalRequest()));
                Mensagem = "Regras dos cartões da bilheteria gravadas. Valem na próxima leitura, sem reiniciar.";
            }
        }).ConfigureAwait(true);
}
