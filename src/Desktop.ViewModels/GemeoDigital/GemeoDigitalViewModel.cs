using System.Diagnostics;
using System.Globalization;
using Contracts.Edge.V1;
using Desktop.ViewModels.GemeoDigital;
using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>
/// Gêmeo digital da TopFit 4: a catraca em 3D, com as peças explicadas, cenários de
/// demonstração, o espelho ao vivo de uma catraca real e, ao lado do desenho, a configuração
/// da catraca inteira (docs/33 §9).
/// </summary>
/// <remarks>
/// <para>
/// Duas coisas diferentes, e a tela nunca deixa confundir uma com a outra:
/// </para>
/// <list type="bullet">
/// <item><b>O desenho</b> tem dois modos. <b>Demonstração:</b> os cenários acontecem só no
/// desenho; nada vai ao serviço, nada vai à catraca. <b>Ao vivo:</b> o desenho segue os eventos
/// da catraca escolhida e mostra a configuração que ela confirmou (Etapa A.5).</item>
/// <item><b>A configuração</b> (<see cref="Central"/>) é real nos dois modos: clicar numa peça
/// abre os parâmetros dela; "Salvar" grava e "Aplicar nesta catraca…", com confirmação, envia
/// pelo mesmo pedido da Parametrização. Os pedidos imediatos (mensagem temporária, relógio,
/// reconexão) são os da "Gerenciar catraca", com o nome registrado.</item>
/// </list>
/// <para>
/// "Rodar na catraca simulada" só existe com o serviço em modo simulação e usa o mesmo pedido
/// da tela Simulador — recusado pelo serviço fora dela (docs/23).
/// </para>
/// </remarks>
public sealed class GemeoDigitalViewModel : TelaBase
{
    /// <summary>Evento mais velho que isto, ao assinar o fluxo, é histórico: não anima.</summary>
    private static readonly TimeSpan ToleranciaDoHistorico = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan LimiteNaDemonstracao = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan PausaDepoisDoRoteiro = TimeSpan.FromSeconds(1.5);

    private readonly Func<TimeSpan> _relogioDaCena;
    private readonly CenaDaCatraca _cena;
    private ExecucaoDeRoteiro? _execucao;
    private DateTimeOffset? _assinaturaDesde;
    private IReadOnlyList<Equipamento> _equipamentos = [];

    private IReadOnlyList<LinhaDeCatraca> _catracas = [];
    private LinhaDeCatraca? _selecionada;
    private int _catraca;
    private bool _servicoEmSimulacao;
    private bool _modoAoVivo;
    private IReadOnlyList<FichaDaPeca> _pecas = [];
    private FichaDaPeca? _pecaSelecionada;
    private FichaDaPeca? _pecaApontada;
    private PecaDaCatraca? _pecaEmDestaque;
    private int _versaoDoFoco;
    private bool _mostrarLeitorFacial;
    private bool _pecasSeparadas;
    private VistaDaCamera _vista;
    private int _versaoDaVista;
    private RoteiroDeDemonstracao? _roteiroEmAndamento;
    private IReadOnlyList<LinhaDaNarracao> _narracao = [];
    private string _estadoTexto = string.Empty;
    private Sinal _estadoSinal = Sinal.Neutro;
    private string _contadores = string.Empty;
    private string _textoDaPrevia = string.Empty;
    private IReadOnlyList<string> _avisosDaPrevia = [];
    private string _previaLinha1 = new(' ', Display2x16.Colunas);
    private string _previaLinha2 = new(' ', Display2x16.Colunas);
    private string _mensagemPadrao = "Aproxime o ingresso";
    private IReadOnlyList<ParDeTexto> _resumoDaConfiguracao = [];
    private bool _urnaLigada = true;
    private string _ultimoEventoAoVivo = "Nenhum evento desde que a tela abriu.";
    private (EstadoDaCena Estado, LeitorDaCena Leitor, bool Urna, int Giros, int Liberacoes, int Negacoes, int SemGiro) _ultimoResumo;
    private ConfiguracaoDoEvento? _configuracaoDoEvento;

    public GemeoDigitalViewModel(
        EdgeControl.EdgeControlClient cliente,
        Func<DateTimeOffset>? relogio = null,
        Func<TimeSpan>? relogioDaCena = null,
        EspecificacaoDaFit4? especificacao = null,
        TimeSpan? esperaPeloResultado = null)
        : base(cliente, relogio)
    {
        var cronometro = Stopwatch.StartNew();
        _relogioDaCena = relogioDaCena ?? (() => cronometro.Elapsed);

        Especificacao = especificacao ?? EspecificacaoDaFit4.Padrao;
        Modelo = GeometriaFit4.Montar(Especificacao);
        _mostrarLeitorFacial = Especificacao.Pecas.LeitorFacial;
        _cena = new CenaDaCatraca(
            _mensagemPadrao,
            duracaoDoGiro: TimeSpan.FromMilliseconds(Especificacao.Rotor.DuracaoDoGiroMs),
            limiteDaLiberacao: LimiteNaDemonstracao);

        RodarRoteiro = new ComandoComParametro(p =>
        {
            if (p is RoteiroDeDemonstracao roteiro)
            {
                Rodar(roteiro);
            }

            return Task.CompletedTask;
        });
        PararRoteiro = new ComandoAssincrono(
            () =>
            {
                Parar();
                return Task.CompletedTask;
            },
            () => RoteiroEmAndamento is not null);
        TestarGiro = new ComandoAssincrono(
            () =>
            {
                Rodar(TesteDeGiro);
                return Task.CompletedTask;
            },
            () => !ModoAoVivo);
        MostrarPrevia = new ComandoAssincrono(
            () =>
            {
                _cena.MostrarMensagem(TextoDaPrevia, _relogioDaCena(), TimeSpan.FromSeconds(8));
                Escolher(PecaDaCatraca.Display);
                return Task.CompletedTask;
            },
            () => !ModoAoVivo && TextoDaPrevia.Length > 0);
        MudarVista = new ComandoComParametro(p =>
        {
            if (p is string nome && Enum.TryParse<VistaDaCamera>(nome, out var vista))
            {
                Vista = vista;
            }
            else if (p is VistaDaCamera direta)
            {
                Vista = direta;
            }

            return Task.CompletedTask;
        });
        RodarNaCatracaSimulada = new ComandoComParametro(p =>
            p is RoteiroDeDemonstracao roteiro ? RodarNaCatracaSimuladaAsync(roteiro) : Task.CompletedTask);

        // A configuração da catraca inteira (docs/33 §9): clicar numa peça abre os parâmetros dela.
        // Os braços e a urna levam ao mapa de giro (D9), cuja pré-visualização só anima o desenho.
        Central = new CentralDaCatracaViewModel(cliente, relogio, esperaPeloResultado);
        Giro.PreVisualizacaoPedida += PreVisualizarGiro;
        Giro.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MapaDeGiroViewModel.LinhaEmFoco) or nameof(MapaDeGiroViewModel.Linhas))
            {
                Avisar(nameof(SetaDoGiro));
            }
        };
        Central.PropertyChanged += (_, e) => AoMudarNaCentral(e.PropertyName);

        AtualizarPecas();
        TextoDaPrevia = _mensagemPadrao;
        AtualizarResumo(_cena.Quadro(_relogioDaCena()), forcar: true);
    }

    public override string Titulo => "Configuração da catraca";

    /// <summary>A planta da catraca desenhada.</summary>
    public EspecificacaoDaFit4 Especificacao { get; }

    /// <summary>O desenho, pronto para a tela copiar.</summary>
    public ModeloDaFit4 Modelo { get; }

    public ComandoComParametro RodarRoteiro { get; }

    public ComandoAssincrono PararRoteiro { get; }

    /// <summary>"Testar giro": libera e gira, só no desenho.</summary>
    public ComandoAssincrono TestarGiro { get; }

    /// <summary>Mostra a prévia no display desenhado. Não envia nada à catraca.</summary>
    public ComandoAssincrono MostrarPrevia { get; }

    /// <summary>Parâmetro: o nome de uma <see cref="VistaDaCamera"/>.</summary>
    public ComandoComParametro MudarVista { get; }

    /// <summary>Parâmetro: o cenário. Só com o serviço em modo simulação.</summary>
    public ComandoComParametro RodarNaCatracaSimulada { get; }

    public IReadOnlyList<RoteiroDeDemonstracao> Roteiros { get; } = GemeoDigital.Roteiros.Todos;

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
                if (ModoAoVivo)
                {
                    ComecarAoVivo();
                }

                // A configuração é desta catraca: trocar carrega a nova (o que não foi salvo na
                // outra se perde, como na Parametrização). Quem atualiza a tela espera a carga.
                if (value > 0 && Central.Catraca != value)
                {
                    _ = Central.CarregarAsync(value);
                }

                AtualizarSelo();
            }
        }
    }

    public LinhaDeCatraca? Selecionada { get => _selecionada; private set => Definir(ref _selecionada, value); }

    /// <summary>O serviço está em modo simulação: nenhuma catraca física é acionada.</summary>
    public bool ServicoEmSimulacao
    {
        get => _servicoEmSimulacao;
        private set
        {
            if (Definir(ref _servicoEmSimulacao, value))
            {
                AtualizarSelo();
            }
        }
    }

    /// <summary>Espelha os eventos da catraca escolhida.</summary>
    public bool ModoAoVivo
    {
        get => _modoAoVivo;
        set
        {
            if (!Definir(ref _modoAoVivo, value))
            {
                return;
            }

            Avisar(nameof(ModoDemonstracao));
            Avisar(nameof(TituloDaConfiguracao));

            if (value)
            {
                ComecarAoVivo();
            }
            else
            {
                Parar();
                _cena.LimiteDaLiberacao = LimiteNaDemonstracao;
                _cena.Reiniciar(_relogioDaCena());
            }

            Redesenhar();
            AtualizarSelo();
            TestarGiro.ReavaliarDisponibilidade();
            MostrarPrevia.ReavaliarDisponibilidade();
        }
    }

    /// <summary>O contrário de <see cref="ModoAoVivo"/>, para o botão de opção da tela.</summary>
    public bool ModoDemonstracao
    {
        get => !ModoAoVivo;
        set => ModoAoVivo = !value;
    }

    /// <summary>A faixa que diz, sempre, de onde vem o que se vê.</summary>
    /// <remarks>
    /// Fala do desenho e dos cenários. A configuração ao lado é real nos dois modos e diz isso
    /// no próprio painel (<see cref="CentralDaCatracaViewModel.AvisoDaConfiguracao"/>).
    /// </remarks>
    public string SeloDoModo => ModoAoVivo
        ? string.Create(CultureInfo.InvariantCulture, $"AO VIVO · CATRACA {Catraca:D2}{(ServicoEmSimulacao ? " (SIMULADA)" : string.Empty)} · o desenho segue a catraca")
        : "DEMONSTRAÇÃO · cenários só no desenho, nada vai para a catraca";

    public Sinal SeloDoModoSinal => ModoAoVivo ? Sinal.Bom : Sinal.Neutro;

    /// <summary>As peças da variante desenhada, na ordem da lista.</summary>
    public IReadOnlyList<FichaDaPeca> Pecas { get => _pecas; private set => Definir(ref _pecas, value); }

    /// <summary>A peça escolhida (lista ou clique no desenho).</summary>
    /// <remarks>
    /// Abre, ao lado do desenho, a configuração daquela peça (<see cref="Central"/>): os braços
    /// levam ao mapa de giro inteiro, a urna à linha do leitor 2. Pela lista de peças, o mesmo:
    /// o teclado chega onde o mouse chega.
    /// </remarks>
    public FichaDaPeca? PecaSelecionada
    {
        get => _pecaSelecionada;
        set
        {
            if (Definir(ref _pecaSelecionada, value) && value is not null)
            {
                VersaoDoFoco++;
                Central.Abrir(value.Peca);
            }
        }
    }

    /// <summary>A configuração da catraca escolhida: painel da peça, "o que muda", salvar e aplicar.</summary>
    public CentralDaCatracaViewModel Central { get; }

    /// <summary>"Giro desta catraca": o mapa de giro da catraca escolhida (o da configuração).</summary>
    public MapaDeGiroViewModel Giro => Central.Giro;

    /// <summary>O painel aberto é de uma peça que mostra o giro (braços ou urna).</summary>
    public bool PainelDoGiroAberto => Central.PainelAberto && Central.MostraGiro;

    /// <summary>Fecha o painel da peça (nome antigo, de quando só o giro tinha painel).</summary>
    public ComandoAssincrono FecharPainelDoGiro => Central.FecharPainel;

    /// <summary>O título do quadro da configuração que o desenho segue.</summary>
    public string TituloDaConfiguracao => ModoAoVivo ? "Na catraca agora" : "Configuração do evento (o desenho segue)";

    /// <summary>De onde vêm os valores que o desenho usa, em uma frase.</summary>
    public string TextoDaConfiguracao => !ModoAoVivo
        ? "Na demonstração, o desenho usa o padrão do evento. A configuração desta catraca fica no painel ao lado."
        : Central.AplicadaConhecida
            ? Central.TextoNaCatracaAgora
            : Central.TextoNaCatracaAgora + " Enquanto isso, o desenho usa o padrão do evento.";

    /// <summary>
    /// O sentido do braço que a seta do desenho mostra: o da linha em foco do mapa, com o painel
    /// aberto. Nulo = sem seta.
    /// </summary>
    private Task? _carregamentoDoGiro;

    public SentidoDoGiro? SetaDoGiro => PainelDoGiroAberto ? Giro.LinhaEmFoco?.Seta : null;

    /// <summary>Abre o painel do giro: a urna para o leitor 2; qualquer outra origem, os braços.</summary>
    public void AbrirGiro(Contracts.Edge.V1.OrigemDoGiro? foco) =>
        Escolher(foco is Contracts.Edge.V1.OrigemDoGiro.Leitor2 ? PecaDaCatraca.Urna : PecaDaCatraca.Rotor);

    /// <summary>Carrega o mapa da catraca escolhida e põe de novo a origem em foco.</summary>
    /// <remarks>
    /// Um carregamento por vez: abrir o painel, trocar de catraca e atualizar podem pedir ao
    /// mesmo tempo, e um carregamento que terminasse depois de o operador começar a mexer
    /// apagaria o que ele mudou. Quem pede durante um carregamento recebe o mesmo; se a catraca
    /// mudou nesse meio-tempo, o carregamento repete para a catraca nova antes de terminar.
    /// </remarks>
    public Task CarregarGiroAsync()
    {
        if (_carregamentoDoGiro is { IsCompleted: false } emCurso)
        {
            return emCurso;
        }

        _carregamentoDoGiro = CarregarGiroUmaVezAsync();
        return _carregamentoDoGiro;
    }

    private async Task CarregarGiroUmaVezAsync()
    {
        int alvo;
        do
        {
            alvo = Catraca;
            await Central.EsperarCargaAsync().ConfigureAwait(true);
            if (Central.Catraca != alvo)
            {
                await Central.CarregarAsync(alvo).ConfigureAwait(true);
            }
            else
            {
                await Giro.CarregarAsync(alvo).ConfigureAwait(true);
            }
        }
        while (Catraca > 0 && Catraca != alvo);

        Avisar(nameof(SetaDoGiro));
    }

    // Pré-visualização do sentido de uma regra: só no desenho, só na demonstração.
    private void PreVisualizarGiro(LinhaDoMapaDeGiro linha)
    {
        if (ModoAoVivo)
        {
            Mensagem = "A pré-visualização do giro roda na demonstração. Ao vivo, o desenho só segue a catraca.";
            return;
        }

        var sentido = linha.Seta is SentidoDoGiro.Entrada ? "de entrada" : "de saída";
        var conta = linha.ContaComoEfetiva is Contracts.Edge.V1.ContagemDoGiro.Saida ? "saída" : "entrada";
        Rodar(new RoteiroDeDemonstracao(
            $"Pré-visualização do giro: {linha.Nome}",
            "Só no desenho: nada foi enviado à catraca.",
            [
                new(TimeSpan.Zero, SinalDaCena.Liberado(),
                    $"PRÉ-VISUALIZAÇÃO · nada foi enviado. {linha.Nome}: {LinhaDoMapaDeGiro.NomeDaFuncao(linha.FuncaoEfetiva)}.",
                    PecaDaCatraca.Rotor),
                new(TimeSpan.FromSeconds(1.2), SinalDaCena.Giro(linha.Seta),
                    $"O braço gira no sentido {sentido} da catraca (a seta), e o giro conta como {conta}.", PecaDaCatraca.Rotor),
                new(TimeSpan.FromSeconds(2.6), null,
                    "Na catraca, confira girando uma vez: o lado em que o braço gira é o que a seta mostra?", PecaDaCatraca.Rotor),
            ]));
        _cena.MostrarMensagem(linha.TextoEfetivo, _relogioDaCena(), TimeSpan.FromSeconds(3));
    }

    /// <summary>Muda a cada escolha: é o aviso para a câmera ir até a peça.</summary>
    public int VersaoDoFoco { get => _versaoDoFoco; private set => Definir(ref _versaoDoFoco, value); }

    /// <summary>A peça sob o mouse.</summary>
    public FichaDaPeca? PecaApontada { get => _pecaApontada; private set => Definir(ref _pecaApontada, value); }

    /// <summary>A peça de que o passo atual do cenário fala.</summary>
    public PecaDaCatraca? PecaEmDestaque { get => _pecaEmDestaque; private set => Definir(ref _pecaEmDestaque, value); }

    /// <summary>Desenha o leitor facial (variante Facial; fora do escopo do produto hoje).</summary>
    public bool MostrarLeitorFacial
    {
        get => _mostrarLeitorFacial;
        set
        {
            if (Definir(ref _mostrarLeitorFacial, value))
            {
                AtualizarPecas();
            }
        }
    }

    /// <summary>Vista com as peças separadas, para ver o que fica por dentro.</summary>
    public bool PecasSeparadas { get => _pecasSeparadas; set => Definir(ref _pecasSeparadas, value); }

    public VistaDaCamera Vista
    {
        get => _vista;
        set
        {
            _vista = value;
            Avisar();
            VersaoDaVista++;
        }
    }

    /// <summary>Muda a cada pedido de vista, mesmo repetido: é o aviso para a câmera ir.</summary>
    public int VersaoDaVista { get => _versaoDaVista; private set => Definir(ref _versaoDaVista, value); }

    public RoteiroDeDemonstracao? RoteiroEmAndamento
    {
        get => _roteiroEmAndamento;
        private set
        {
            if (Definir(ref _roteiroEmAndamento, value))
            {
                PararRoteiro.ReavaliarDisponibilidade();
            }
        }
    }

    public IReadOnlyList<LinhaDaNarracao> Narracao { get => _narracao; private set => Definir(ref _narracao, value); }

    /// <summary>O que a catraca desenhada está fazendo, em português.</summary>
    public string EstadoTexto { get => _estadoTexto; private set => Definir(ref _estadoTexto, value); }

    public Sinal EstadoSinal { get => _estadoSinal; private set => Definir(ref _estadoSinal, value); }

    /// <summary>Giros, liberações e negações desde que a cena começou.</summary>
    public string Contadores { get => _contadores; private set => Definir(ref _contadores, value); }

    /// <summary>Texto para ver no display desenhado antes de gravar de verdade.</summary>
    public string TextoDaPrevia
    {
        get => _textoDaPrevia;
        set
        {
            if (Definir(ref _textoDaPrevia, value ?? string.Empty))
            {
                (PreviaLinha1, PreviaLinha2) = Display2x16.Formatar(_textoDaPrevia);
                AvisosDaPrevia = Display2x16.Avisos(_textoDaPrevia);
                MostrarPrevia.ReavaliarDisponibilidade();
            }
        }
    }

    public string PreviaLinha1 { get => _previaLinha1; private set => Definir(ref _previaLinha1, value); }

    public string PreviaLinha2 { get => _previaLinha2; private set => Definir(ref _previaLinha2, value); }

    public IReadOnlyList<string> AvisosDaPrevia { get => _avisosDaPrevia; private set => Definir(ref _avisosDaPrevia, value); }

    /// <summary>A mensagem padrão gravada no serviço.</summary>
    public string MensagemPadrao { get => _mensagemPadrao; private set => Definir(ref _mensagemPadrao, value); }

    /// <summary>A configuração em vigor, lida do serviço: o que muda no desenho.</summary>
    public IReadOnlyList<ParDeTexto> ResumoDaConfiguracao { get => _resumoDaConfiguracao; private set => Definir(ref _resumoDaConfiguracao, value); }

    /// <summary>Leitor da urna ligado na configuração. Desligado, a urna aparece apagada.</summary>
    public bool UrnaLigada { get => _urnaLigada; private set => Definir(ref _urnaLigada, value); }

    public bool MedidasAConfirmar => Especificacao.MedidasAConfirmar;

    public string AvisoDasMedidas => MedidasAConfirmar
        ? "Desenho ilustrativo: a disposição das peças segue a TopFit 4; as medidas ainda não foram conferidas numa catraca."
        : "Medidas conferidas numa TopFit 4.";

    public string UltimoEventoAoVivo { get => _ultimoEventoAoVivo; private set => Definir(ref _ultimoEventoAoVivo, value); }

    /// <summary>Estado atual da cena, para os testes e para o diagnóstico.</summary>
    public EstadoDaCena EstadoAtual => _cena.Estado;

    /// <summary>
    /// Um quadro: avança o cenário, aplica os passos vencidos e devolve o que desenhar.
    /// Chamado pela tela a cada quadro, na thread da tela.
    /// </summary>
    public QuadroDaCena Quadro()
    {
        var agora = _relogioDaCena();

        if (_execucao is { } execucao)
        {
            var vencidos = execucao.Avancar(agora);
            foreach (var passo in vencidos)
            {
                if (passo.Sinal is { } sinal)
                {
                    _cena.Aplicar(sinal, agora);
                }
            }

            if (vencidos.Count > 0)
            {
                PecaEmDestaque = vencidos[^1].Destaque;
                MontarNarracao();
            }

            if (execucao.Terminou && agora - execucao.Inicio >= execucao.Roteiro.Duracao + PausaDepoisDoRoteiro)
            {
                _execucao = null;
                RoteiroEmAndamento = null;
                PecaEmDestaque = null;
                MontarNarracao();
            }
        }

        var quadro = _cena.Quadro(agora);
        AtualizarResumo(quadro, forcar: false);
        return quadro;
    }

    /// <summary>A peça apontada pelo mouse; nulo quando o mouse sai.</summary>
    public void Apontar(PecaDaCatraca? peca) =>
        PecaApontada = peca is { } p ? CatalogoDaFit4.De(p) : null;

    /// <summary>Escolhe a peça (clique no desenho).</summary>
    public void Escolher(PecaDaCatraca peca)
    {
        PecaSelecionada = CatalogoDaFit4.De(peca);

        // Clicar de novo na mesma peça reabre o painel, se ele tiver sido fechado.
        Central.Abrir(peca);
    }

    /// <summary>Um evento vindo do serviço. Na thread da tela.</summary>
    public void AplicarEventoAoVivo(EventoDeAcesso evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (!ModoAoVivo || evento.Inner != Catraca)
        {
            return;
        }

        // Ao assinar, o serviço reentrega os últimos acessos: são histórico, não animam.
        if (_assinaturaDesde is { } desde
            && evento.RecebidoEm is { } recebido
            && recebido.ToDateTimeOffset() < desde - ToleranciaDoHistorico)
        {
            return;
        }

        var agora = _relogioDaCena();

        // Chegou evento da catraca: ela está falando, mesmo que a última consulta de saúde
        // ainda não saiba disso.
        _cena.Aplicar(SinalDaCena.Conectou(), agora);

        foreach (var sinal in TraducaoAoVivo.Sinais(evento))
        {
            _cena.Aplicar(sinal, agora);
        }

        var hora = evento.RecebidoEm is null
            ? "—"
            : FusoDoEvento.NoEvento(evento.RecebidoEm.ToDateTimeOffset()).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var mensagem = string.IsNullOrWhiteSpace(evento.MensagemAoOperador) ? evento.Motivo : evento.MensagemAoOperador;
        UltimoEventoAoVivo = $"{hora} · {mensagem}{(evento.PassagemConfirmada ? " · girou" : string.Empty)}";
    }

    /// <summary>
    /// Acompanha o fluxo ao vivo enquanto a tela estiver aberta. <paramref name="despachar"/>
    /// leva cada evento para a thread da tela.
    /// </summary>
    public async Task AcompanharAsync(Action<Action> despachar, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(despachar);
        _assinaturaDesde = Relogio();

        while (!cancelamento.IsCancellationRequested)
        {
            try
            {
                using var fluxo = Cliente.AcompanharEventos(new AcompanharEventosRequest(), cancellationToken: cancelamento);

                while (await fluxo.ResponseStream.MoveNext(cancelamento).ConfigureAwait(false))
                {
                    var evento = fluxo.ResponseStream.Current;
                    despachar(() => AplicarEventoAoVivo(evento));
                }
            }
            catch (RpcException) when (cancelamento.IsCancellationRequested)
            {
                return;
            }
            catch (RpcException)
            {
                // Serviço reiniciando: tenta de novo em instantes.
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

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            var estado = await Cliente.ObterEstadoAsync(new ObterEstadoRequest(), cancellationToken: cancelamento);
            ServicoEmSimulacao = estado.Simulacao;

            var lista = await Cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento);
            var agora = Relogio();
            _equipamentos = [.. lista.Equipamentos];
            Catracas = [.. lista.Equipamentos.Select(e => PainelAoVivoViewModel.Linha(e, agora))];

            // A lista nova esvazia a escolha do seletor, que tentava gravar "nenhuma" no número
            // (borda vermelha, seletor vazio). Avisar o número de novo faz o seletor reencontrá-la.
            Avisar(nameof(Catraca));

            if (Catraca == 0 && Catracas.Count > 0)
            {
                Catraca = Catracas[0].Inner;
            }

            Selecionada = Catracas.FirstOrDefault(c => c.Inner == Catraca);

            var configuracao = await Cliente.ObterConfiguracaoAsync(new ObterConfiguracaoRequest(), cancellationToken: cancelamento);
            AplicarConfiguracao(configuracao);

            if (ModoAoVivo && _equipamentos.FirstOrDefault(e => e.Inner == Catraca) is { } equipamento)
            {
                _cena.Aplicar(TraducaoAoVivo.Saude(equipamento), _relogioDaCena());
            }

            if (Catracas.Count == 0)
            {
                Mensagem = "Nenhuma catraca cadastrada: a demonstração funciona; o modo ao vivo precisa de uma catraca.";
            }

            // A configuração da catraca: a primeira vez, carrega; depois, só acompanha (a
            // situação na catraca e os pedidos), sem desfazer o que o operador está mudando.
            if (Catraca > 0)
            {
                await Central.EsperarCargaAsync();
                if (Central.Catraca != Catraca)
                {
                    await Central.CarregarAsync(Catraca, cancelamento);
                }
                else
                {
                    await Central.AcompanharAsync(cancelamento);
                }

                Avisar(nameof(SetaDoGiro));
            }
        });

    /// <summary>O que muda no desenho conforme a configuração do evento.</summary>
    public void AplicarConfiguracao(ConfiguracaoDoEvento configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        _configuracaoDoEvento = configuracao;

        if (ModoAoVivo && configuracao.EsperaPeloGiroSegundos > 0)
        {
            _cena.LimiteDaLiberacao = TimeSpan.FromSeconds(configuracao.EsperaPeloGiroSegundos);
        }

        Redesenhar();
    }

    /// <summary>
    /// A mensagem padrão, a urna e o quadro "configuração" do desenho. Na demonstração, o padrão
    /// do evento. Ao vivo, o que a catraca confirmou (versão aplicada igual à salva, Etapa A.5);
    /// sem essa confirmação, o padrão do evento, e o quadro diz que não se sabe o que ela usa.
    /// </summary>
    private void Redesenhar()
    {
        string? mensagem;
        bool urna;

        if (ModoAoVivo && Central.AplicadaConhecida && Central.MensagemPadraoAplicada is { } aplicada && Central.UrnaLigadaAplicada is { } urnaAplicada)
        {
            mensagem = aplicada;
            urna = urnaAplicada;
            ResumoDaConfiguracao = Central.NaCatracaAgora;
        }
        else if (_configuracaoDoEvento is { } evento)
        {
            mensagem = evento.MensagemPadrao;
            urna = evento.LeitorDaUrna;
            ResumoDaConfiguracao = ModoAoVivo
                ? []
                :
                [
                    new("Mensagem padrão", $"“{evento.MensagemPadrao}”"),
                    new("Leitor da urna", evento.LeitorDaUrna ? "Ligado" : "Desligado (a urna aparece apagada)"),
                    new("Tempo de acionamento", $"{evento.TempoDeAcionamentoSegundos} s"),
                    new("Espera pelo giro", $"{evento.EsperaPeloGiroSegundos} s"),
                    new("Tipo de leitor (técnico)", evento.TipoDeLeitor.ToString(CultureInfo.InvariantCulture)),
                ];
        }
        else
        {
            Avisar(nameof(TextoDaConfiguracao));
            return;
        }

        var mensagemMudou = !string.Equals(MensagemPadrao, mensagem, StringComparison.Ordinal);
        MensagemPadrao = mensagem;
        _cena.DefinirMensagemPadrao(mensagem);
        UrnaLigada = urna;

        if (mensagemMudou)
        {
            TextoDaPrevia = mensagem;
        }

        Avisar(nameof(TextoDaConfiguracao));
    }

    private void AoMudarNaCentral(string? propriedade)
    {
        switch (propriedade)
        {
            case nameof(CentralDaCatracaViewModel.PainelAberto) or nameof(CentralDaCatracaViewModel.Painel):
                Avisar(nameof(PainelDoGiroAberto));
                Avisar(nameof(SetaDoGiro));
                break;
            case nameof(CentralDaCatracaViewModel.AplicadaConhecida) or nameof(CentralDaCatracaViewModel.NaCatracaAgora):
                Redesenhar();
                break;
            case nameof(CentralDaCatracaViewModel.Mudancas):
                // Mudar a mensagem padrão no painel do display mostra o rascunho na prévia.
                if (Central.Parametrizacao.Campos.FirstOrDefault(c => c.Campo is Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao) is { Alterado: true, Herda: false } campo)
                {
                    TextoDaPrevia = campo.Valor;
                }

                break;
            default:
                break;
        }
    }

    // O "Testar giro" do painel da peça: libera e gira, sem leitura.
    private static readonly RoteiroDeDemonstracao TesteDeGiro = new(
        "Teste de giro",
        "Libera e gira um terço de volta, só no desenho.",
        [
            new(TimeSpan.Zero, SinalDaCena.Liberado(), "Liberada: braço solto.", PecaDaCatraca.Rotor),
            new(TimeSpan.FromSeconds(0.8), SinalDaCena.Giro(), "Um terço de volta, no sentido da entrada.", PecaDaCatraca.Rotor),
            new(TimeSpan.FromSeconds(2.0), null, "Travada de novo.", PecaDaCatraca.Rotor),
        ]);

    private void Rodar(RoteiroDeDemonstracao roteiro)
    {
        if (ModoAoVivo)
        {
            Mensagem = "Os cenários rodam na demonstração. Ao vivo, o desenho só segue a catraca.";
            return;
        }

        var agora = _relogioDaCena();
        _cena.Reiniciar(agora);
        _execucao = new ExecucaoDeRoteiro(roteiro, agora);
        RoteiroEmAndamento = roteiro;
        Mensagem = string.Empty;
        MontarNarracao();
    }

    private void Parar()
    {
        _execucao = null;
        RoteiroEmAndamento = null;
        PecaEmDestaque = null;
        Narracao = [];
    }

    private async Task RodarNaCatracaSimuladaAsync(RoteiroDeDemonstracao roteiro)
    {
        if (!ServicoEmSimulacao)
        {
            Mensagem = "Só com o serviço em modo simulação. Com catraca física, o gêmeo apenas observa.";
            return;
        }

        if (roteiro.CodigoDeTeste is not { } codigo)
        {
            Mensagem = $"\"{roteiro.Nome}\" não tem código de teste para a catraca simulada.";
            return;
        }

        if (Catraca < 1)
        {
            Mensagem = "Escolha a catraca.";
            return;
        }

        ModoAoVivo = true;

        await Tentar(async () =>
        {
            var resposta = await Cliente.SimularLeituraAsync(new SimularLeituraRequest
            {
                Inner = Catraca,
                Codigo = codigo,
                NaUrna = roteiro.NaUrna,
                Girar = roteiro.Gira,
            });

            Mensagem = resposta.Aceita
                ? $"Passado na catraca simulada {Catraca}: o desenho mostra o que o sistema decidir."
                : resposta.Mensagem;
        }).ConfigureAwait(true);
    }

    private void ComecarAoVivo()
    {
        _execucao = null;
        RoteiroEmAndamento = null;
        PecaEmDestaque = null;
        Narracao = [];

        var equipamento = _equipamentos.FirstOrDefault(e => e.Inner == Catraca);
        _cena.LimiteDaLiberacao = TimeSpan.FromSeconds(8);
        _cena.Reiniciar(_relogioDaCena(), comunicando: equipamento?.EmOperacao ?? false);
        UltimoEventoAoVivo = "Nenhum evento desde que o modo ao vivo começou.";
    }

    private void AtualizarPecas()
    {
        var presentes = Especificacao.Pecas;
        Pecas = [.. CatalogoDaFit4.Pecas.Where(f => f.Peca switch
        {
            PecaDaCatraca.Teclado => presentes.Teclado,
            PecaDaCatraca.LeitorQr => presentes.LeitorQr,
            PecaDaCatraca.LeitorDeProximidade => presentes.LeitorDeProximidade,
            PecaDaCatraca.Urna => presentes.Urna,
            PecaDaCatraca.LeitorFacial => MostrarLeitorFacial,
            _ => true,
        })];

        if (PecaSelecionada is { Peca: PecaDaCatraca.LeitorFacial } && !MostrarLeitorFacial)
        {
            PecaSelecionada = null;
        }
    }

    private void AtualizarSelo()
    {
        Avisar(nameof(SeloDoModo));
        Avisar(nameof(SeloDoModoSinal));
    }

    private void MontarNarracao()
    {
        if (_execucao is not { } execucao)
        {
            if (RoteiroEmAndamento is null)
            {
                Narracao = [];
            }

            return;
        }

        var atual = execucao.PassoAtual;
        Narracao = [.. execucao.Roteiro.Passos.Select((passo, i) => new LinhaDaNarracao(
            passo.Em.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " s",
            passo.Narracao,
            Atual: i == atual,
            Aconteceu: i <= atual))];
    }

    private void AtualizarResumo(QuadroDaCena quadro, bool forcar)
    {
        var resumo = (quadro.Estado, quadro.Leitor, quadro.UrnaCheia, _cena.Giros, _cena.Liberacoes, _cena.Negacoes, _cena.LiberacoesSemGiro);
        if (!forcar && resumo == _ultimoResumo)
        {
            return;
        }

        _ultimoResumo = resumo;

        var (texto, sinal) = quadro.Estado switch
        {
            EstadoDaCena.SemComunicacao => ("Sem comunicação com a catraca", Sinal.Problema),
            EstadoDaCena.Livre => ("Livre: travada, esperando a próxima leitura", Sinal.Neutro),
            EstadoDaCena.LendoCredencial => ("Lendo " + NomeDoLeitor(quadro.Leitor), Sinal.Neutro),
            EstadoDaCena.Decidindo => ("Decidindo o acesso", Sinal.Neutro),
            EstadoDaCena.Liberada => ("Liberada: esperando o giro", Sinal.Bom),
            EstadoDaCena.Girando => ("Girando: passagem em andamento", Sinal.Bom),
            _ => ("Negada: \"Acesso nao autorizado\" no display", Sinal.Problema),
        };

        EstadoTexto = quadro.UrnaCheia ? texto + " · urna cheia" : texto;
        EstadoSinal = quadro.UrnaCheia && sinal is not Sinal.Problema ? Sinal.Atencao : sinal;
        Contadores = $"{_cena.Giros} giro(s) · {_cena.Liberacoes} liberada(s) · {_cena.Negacoes} negada(s) · {_cena.LiberacoesSemGiro} sem giro";
    }

    private static string NomeDoLeitor(LeitorDaCena leitor) => leitor switch
    {
        LeitorDaCena.Qr => "o QR do celular",
        LeitorDaCena.CartaoNaFrente => "o cartão no leitor da frente",
        LeitorDaCena.CartaoNaUrna => "o cartão na fenda da urna",
        _ => "a credencial",
    };
}
