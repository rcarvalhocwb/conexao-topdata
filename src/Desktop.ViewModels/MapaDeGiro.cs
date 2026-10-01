using System.Globalization;
using Contracts.Edge.V1;
using Desktop.ViewModels.GemeoDigital;

namespace Desktop.ViewModels;

/// <summary>Uma opção de lista do mapa de giro (função ou contagem).</summary>
/// <param name="Valor">O número do enum do contrato.</param>
/// <param name="Nome">Como aparece na lista.</param>
public sealed record OpcaoDoGiro(int Valor, string Nome);

/// <summary>Uma origem do mapa de giro, como a tela a edita.</summary>
/// <remarks>
/// "Padrão" = a origem segue a função da catraca (Parametrização) e conta como entrada, como
/// sempre. Desmarcado, o operador escolhe a função da DLL (o sentido físico, com seta no gêmeo),
/// como o giro conta e, se quiser, o texto curto.
/// </remarks>
public sealed class LinhaDoMapaDeGiro : Notificavel
{
    /// <summary>A função "da catraca": a regra não escolhe função e segue a da Parametrização.</summary>
    public const int FuncaoDaCatraca = 0;

    private readonly Action _aoMudar;
    private readonly FuncaoDoGiro _funcaoDoPerfil;
    private bool _herda;
    private OpcaoDoGiro _funcao;
    private OpcaoDoGiro _contaComo;
    private string _texto;
    private bool _emFoco;

    internal LinhaDoMapaDeGiro(RegraDoMapaDeGiro regra, FuncaoDoGiro funcaoDoPerfil, Action aoMudar)
    {
        Regra = regra;
        _funcaoDoPerfil = funcaoDoPerfil;
        _aoMudar = aoMudar;

        OpcoesDeFuncao =
        [
            new(FuncaoDaCatraca, $"A da catraca ({NomeDaFuncao(funcaoDoPerfil)})"),
            .. new[] { FuncaoDoGiro.Entrada, FuncaoDoGiro.Saida, FuncaoDoGiro.EntradaInvertida, FuncaoDoGiro.SaidaInvertida }
                .Select(f => new OpcaoDoGiro((int)f, NomeDaFuncao(f))),
        ];

        _herda = !regra.Definida;
        _funcao = regra.Definida && !regra.FuncaoDoPerfil
            ? OpcoesDeFuncao.First(o => o.Valor == (int)regra.Funcao)
            : OpcoesDeFuncao[0];
        _contaComo = Contagens.First(o => o.Valor == (int)(regra.ContaComo is ContagemDoGiro.Saida ? ContagemDoGiro.Saida : ContagemDoGiro.Entrada));
        _texto = regra.TextoPersonalizado ? regra.Texto : string.Empty;
    }

    /// <summary>Como o serviço devolveu (o salvo).</summary>
    public RegraDoMapaDeGiro Regra { get; }

    public Contracts.Edge.V1.OrigemDoGiro Origem => Regra.Origem;

    /// <summary>Nome da origem em português de operador.</summary>
    public string Nome => Origem switch
    {
        Contracts.Edge.V1.OrigemDoGiro.Leitor1 => "Leitor 1 (frente e QR)",
        Contracts.Edge.V1.OrigemDoGiro.Leitor2 => "Leitor 2 (urna)",
        Contracts.Edge.V1.OrigemDoGiro.Teclado => "Teclado",
        Contracts.Edge.V1.OrigemDoGiro.LiberacaoManual => "Liberação manual do painel",
        _ => "Origem desconhecida",
    };

    public IReadOnlyList<OpcaoDoGiro> OpcoesDeFuncao { get; }

    private static readonly IReadOnlyList<OpcaoDoGiro> Contagens =
    [
        new((int)ContagemDoGiro.Entrada, "Entrada"),
        new((int)ContagemDoGiro.Saida, "Saída"),
    ];

    /// <summary>Entrada ou saída: o rótulo do giro.</summary>
    public IReadOnlyList<OpcaoDoGiro> OpcoesDeContagem { get; } = Contagens;

    /// <summary>Segue o padrão (função da catraca, conta como entrada).</summary>
    public bool Herda
    {
        get => _herda;
        set
        {
            if (Definir(ref _herda, value))
            {
                Mudou();
            }
        }
    }

    public bool Editavel => !Herda;

    public OpcaoDoGiro FuncaoEscolhida
    {
        get => _funcao;
        set
        {
            if (value is not null && Definir(ref _funcao, value))
            {
                Mudou();
            }
        }
    }

    public OpcaoDoGiro ContaComoEscolhida
    {
        get => _contaComo;
        set
        {
            if (value is not null && Definir(ref _contaComo, value))
            {
                Mudou();
            }
        }
    }

    /// <summary>Texto curto do giro; vazio = o padrão do rótulo.</summary>
    public string Texto
    {
        get => _texto;
        set
        {
            if (Definir(ref _texto, value ?? string.Empty))
            {
                Mudou();
            }
        }
    }

    /// <summary>A origem que o clique no gêmeo trouxe (a urna leva à linha do leitor 2).</summary>
    public bool EmFoco { get => _emFoco; internal set => Definir(ref _emFoco, value); }

    /// <summary>A função que vale, como está na tela (a da catraca quando segue o perfil).</summary>
    public FuncaoDoGiro FuncaoEfetiva => Herda || FuncaoEscolhida.Valor == FuncaoDaCatraca ? _funcaoDoPerfil : (FuncaoDoGiro)FuncaoEscolhida.Valor;

    public ContagemDoGiro ContaComoEfetiva => Herda ? ContagemDoGiro.Entrada : (ContagemDoGiro)ContaComoEscolhida.Valor;

    /// <summary>O texto que vai para o display, como está na tela.</summary>
    public string TextoEfetivo => !Herda && Texto.Trim().Length > 0
        ? Texto.Trim()
        : ContaComoEfetiva is ContagemDoGiro.Saida ? "Saida liberada" : "Entrada liberada";

    /// <summary>O sentido do braço que a seta do gêmeo mostra para a função efetiva.</summary>
    public SentidoDoGiro Seta => SetaDa(FuncaoEfetiva);

    /// <summary>"Gira para dentro" ou "para fora", previsto pelo nome da função.</summary>
    public string TextoDaSeta => (Seta is SentidoDoGiro.Entrada ? "Braço gira no sentido de entrada da catraca" : "Braço gira no sentido de saída da catraca") +
        " (previsto pelo nome da função; confira girando uma vez)";

    public string ResumoDoRotulo => ContaComoEfetiva is ContagemDoGiro.Saida ? "Conta como saída" : "Conta como entrada";

    public IReadOnlyList<string> AvisosDoTexto => Herda || Texto.Length == 0 ? [] : Display2x16.Avisos(Texto);

    /// <summary>Erro no campo; vazio quando a linha pode ser salva.</summary>
    public string Erro => !Herda && Texto.Length > 32 ? "O texto do giro tem até 32 caracteres (duas linhas de 16 no display)." : string.Empty;

    /// <summary>A conferência é desta função, salva: só vale para o que está gravado.</summary>
    public SituacaoDaConferencia Conferencia => Regra.Conferencia;

    public string SeloDaConferencia => Conferencia switch
    {
        SituacaoDaConferencia.ComoEsperado => "Sentido conferido nesta instalação",
        SituacaoDaConferencia.AoContrario => "Na conferência o braço girou ao contrário: troque a função",
        _ => "Sentido ainda não conferido nesta instalação",
    };

    public Sinal SinalDaConferencia => Conferencia switch
    {
        SituacaoDaConferencia.ComoEsperado => Sinal.Bom,
        SituacaoDaConferencia.AoContrario => Sinal.Problema,
        _ => Sinal.Atencao,
    };

    public string DetalheDaConferencia => Conferencia is SituacaoDaConferencia.NaoConferida or SituacaoDaConferencia.NaoEspecificado || Regra.ConferidaEm is null
        ? $"{NomeDaFuncao(Regra.Funcao)}: ninguém girou ainda com esta função nesta catraca."
        : $"{NomeDaFuncao(Regra.Funcao)}: conferida por {Regra.ConferidaPor} em {FusoDoEvento.NoEvento(Regra.ConferidaEm.ToDateTimeOffset()).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}.";

    public string Aviso => Regra.Aviso;

    /// <summary>Mudou em relação ao salvo.</summary>
    public bool Alterada => !string.Equals(Descrever(salvo: true), Descrever(salvo: false), StringComparison.Ordinal);

    /// <summary>A regra que vai para o serviço.</summary>
    public RegraDeGiroPedida Pedido() => Herda
        ? new RegraDeGiroPedida { Origem = Origem, Herdar = true }
        : new RegraDeGiroPedida
        {
            Origem = Origem,
            Funcao = FuncaoEscolhida.Valor == FuncaoDaCatraca ? FuncaoDoGiro.NaoEspecificado : (FuncaoDoGiro)FuncaoEscolhida.Valor,
            ContaComo = (ContagemDoGiro)ContaComoEscolhida.Valor,
            Texto = Texto.Trim(),
        };

    /// <summary>Atual (salvo) ou novo (tela), em português, para "o que muda".</summary>
    public string Descrever(bool salvo)
    {
        if (salvo)
        {
            return !Regra.Definida
                ? "padrão (a função da catraca, conta como entrada)"
                : $"{(Regra.FuncaoDoPerfil ? "a da catraca" : NomeDaFuncao(Regra.Funcao))}, conta como {(Regra.ContaComo is ContagemDoGiro.Saida ? "saída" : "entrada")}" +
                  (Regra.TextoPersonalizado ? $", \"{Regra.Texto}\"" : string.Empty);
        }

        return Herda
            ? "padrão (a função da catraca, conta como entrada)"
            : $"{(FuncaoEscolhida.Valor == FuncaoDaCatraca ? "a da catraca" : NomeDaFuncao((FuncaoDoGiro)FuncaoEscolhida.Valor))}, conta como {(ContaComoEfetiva is ContagemDoGiro.Saida ? "saída" : "entrada")}" +
              (Texto.Trim().Length > 0 ? $", \"{Texto.Trim()}\"" : string.Empty);
    }

    /// <summary>Nome da função para o operador, com o número da matriz.</summary>
    public static string NomeDaFuncao(FuncaoDoGiro funcao) => funcao switch
    {
        FuncaoDoGiro.Entrada => "Liberar entrada (EI-041)",
        FuncaoDoGiro.Saida => "Liberar saída (EI-042)",
        FuncaoDoGiro.EntradaInvertida => "Liberar entrada invertida (EI-043)",
        FuncaoDoGiro.SaidaInvertida => "Liberar saída invertida (EI-044)",
        _ => "Função desconhecida",
    };

    /// <summary>
    /// Para que lado o braço gira com cada função, previsto pelo nome: entrada e saída invertida
    /// para dentro; saída e entrada invertida para fora. Qual delas serve a cada instalação é o
    /// que a conferência de comissionamento confirma (NOVO-HIL-DIR-11).
    /// </summary>
    public static SentidoDoGiro SetaDa(FuncaoDoGiro funcao) =>
        funcao is FuncaoDoGiro.Saida or FuncaoDoGiro.EntradaInvertida ? SentidoDoGiro.Saida : SentidoDoGiro.Entrada;

    private void Mudou()
    {
        Avisar(nameof(Editavel));
        Avisar(nameof(FuncaoEfetiva));
        Avisar(nameof(ContaComoEfetiva));
        Avisar(nameof(TextoEfetivo));
        Avisar(nameof(Seta));
        Avisar(nameof(TextoDaSeta));
        Avisar(nameof(ResumoDoRotulo));
        Avisar(nameof(AvisosDoTexto));
        Avisar(nameof(Erro));
        Avisar(nameof(Alterada));
        _aoMudar();
    }
}

/// <summary>
/// "Giro desta catraca": o mapa de giro (decisão D9 do dono do produto, docs/34 §9) de uma
/// catraca, para o painel do gêmeo e para a aba Giro da Parametrização.
/// </summary>
/// <remarks>
/// <para>
/// Para cada origem que libera — leitor 1, urna, teclado, liberação manual — qual função da DLL
/// chamar (o sentido físico, com a seta no gêmeo) e como o giro conta. Mesmo padrão da
/// Parametrização (Etapa A.6): o que muda (atual → novo), salvar com o nome digitado, aplicar em
/// dois passos com o comando "Aplicar configuração" daquela catraca, e "aplicada" só com a versão
/// aceita igual à salva.
/// </para>
/// <para>
/// A conferência de comissionamento: depois de aplicar, alguém passa um cartão de teste pela
/// origem (ou usa a liberação manual), olha o braço e diz aqui se girou para o lado da seta. Fica
/// registrado com o nome e a hora; até lá, a catraca mostra "sentido ainda não conferido nesta
/// instalação" — e o mapa vale assim mesmo.
/// </para>
/// <para>
/// A pré-visualização só anima o desenho (evento <see cref="PreVisualizacaoPedida"/>); nada vai à
/// catraca.
/// </para>
/// </remarks>
public sealed class MapaDeGiroViewModel : TelaBase
{
    private int _catraca;
    private string _operador = string.Empty;
    private IReadOnlyList<LinhaDoMapaDeGiro> _linhas = [];
    private IReadOnlyList<LinhaDeMudanca> _mudancas = [];
    private IReadOnlyList<string> _problemas = [];
    private IReadOnlyList<string> _avisosDoSalvo = [];
    private string _versaoSalva = string.Empty;
    private string _versaoAplicada = string.Empty;
    private string _situacaoNaCatraca = "—";
    private Sinal _sinalDaSituacao = Sinal.Neutro;
    private ComandoRegistrado? _ultimaAplicacao;
    private bool _confirmandoAplicacao;
    private Contracts.Edge.V1.OrigemDoGiro? _foco;
    private string _alteradoPor = string.Empty;

    public MapaDeGiroViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Salvar = new ComandoAssincrono(SalvarAsync, () => PodeSalvar);
        Descartar = new ComandoAssincrono(() => CarregarAsync(Catraca), () => Mudancas.Count > 0);
        PedirAplicacao = new ComandoAssincrono(
            () =>
            {
                ConfirmandoAplicacao = true;
                return Task.CompletedTask;
            },
            () => PodeAplicar && !ConfirmandoAplicacao);
        ConfirmarAplicacao = new ComandoAssincrono(AplicarAsync, () => PodeAplicar && ConfirmandoAplicacao);
        CancelarAplicacao = new ComandoAssincrono(
            () =>
            {
                ConfirmandoAplicacao = false;
                return Task.CompletedTask;
            },
            () => ConfirmandoAplicacao);
        ConferirComoEsperado = new ComandoComParametro(p => p is LinhaDoMapaDeGiro l ? ConferirAsync(l, comoEsperado: true) : Task.CompletedTask);
        ConferirAoContrario = new ComandoComParametro(p => p is LinhaDoMapaDeGiro l ? ConferirAsync(l, comoEsperado: false) : Task.CompletedTask);
        PreVisualizar = new ComandoComParametro(p =>
        {
            if (p is LinhaDoMapaDeGiro linha)
            {
                Focar(linha.Origem);
                PreVisualizacaoPedida?.Invoke(linha);
            }

            return Task.CompletedTask;
        });
    }

    public override string Titulo => "Giro desta catraca";

    /// <summary>Pede ao gêmeo para animar o sentido da linha (só no desenho).</summary>
    public event Action<LinhaDoMapaDeGiro>? PreVisualizacaoPedida;

    /// <summary>Há um gêmeo para animar (no painel do gêmeo, sim; na Parametrização, não).</summary>
    public bool PreVisualizacaoDisponivel => PreVisualizacaoPedida is not null;

    public ComandoAssincrono Salvar { get; }

    public ComandoAssincrono Descartar { get; }

    public ComandoAssincrono PedirAplicacao { get; }

    public ComandoAssincrono ConfirmarAplicacao { get; }

    public ComandoAssincrono CancelarAplicacao { get; }

    /// <summary>Parâmetro: a linha. "O braço girou para o lado da seta."</summary>
    public ComandoComParametro ConferirComoEsperado { get; }

    /// <summary>Parâmetro: a linha. "O braço girou para o outro lado."</summary>
    public ComandoComParametro ConferirAoContrario { get; }

    /// <summary>Parâmetro: a linha. Anima o sentido no gêmeo; nada vai à catraca.</summary>
    public ComandoComParametro PreVisualizar { get; }

    /// <summary>A catraca carregada; 0 antes de carregar.</summary>
    public int Catraca { get => _catraca; private set => Definir(ref _catraca, value); }

    /// <summary>Quem grava, aplica ou confere. Não há login: fica registrado como foi digitado.</summary>
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

    public IReadOnlyList<LinhaDoMapaDeGiro> Linhas { get => _linhas; private set => Definir(ref _linhas, value); }

    /// <summary>A linha em foco (a urna leva ao leitor 2); a primeira quando nada foi escolhido.</summary>
    public LinhaDoMapaDeGiro? LinhaEmFoco => Linhas.FirstOrDefault(l => l.EmFoco) ?? (Linhas.Count > 0 ? Linhas[0] : null);

    public IReadOnlyList<LinhaDeMudanca> Mudancas { get => _mudancas; private set => Definir(ref _mudancas, value); }

    public string ResumoDasMudancas => Mudancas.Count switch
    {
        0 => "Nenhuma alteração no giro.",
        1 => "1 alteração não salva.",
        var n => $"{n} alterações não salvas.",
    };

    public IReadOnlyList<string> Problemas { get => _problemas; private set => Definir(ref _problemas, value); }

    public IReadOnlyList<string> AvisosDoSalvo { get => _avisosDoSalvo; private set => Definir(ref _avisosDoSalvo, value); }

    public string SituacaoNaCatraca { get => _situacaoNaCatraca; private set => Definir(ref _situacaoNaCatraca, value); }

    public Sinal SinalDaSituacao { get => _sinalDaSituacao; private set => Definir(ref _sinalDaSituacao, value); }

    /// <summary>Quem salvou o mapa por último.</summary>
    public string AlteradoPor { get => _alteradoPor; private set => Definir(ref _alteradoPor, value); }

    /// <summary>
    /// O selo da catraca: alguma regra gravada usa função que ninguém conferiu girando nesta
    /// instalação (ou que girou ao contrário). Vazio quando está tudo conferido ou sem mapa.
    /// </summary>
    public string SeloDaCatraca =>
        Linhas.Where(l => l.Regra.Definida).Any(l => l.Conferencia is SituacaoDaConferencia.AoContrario)
            ? "Na conferência, o braço girou ao contrário numa regra deste mapa"
            : Linhas.Where(l => l.Regra.Definida).Any(l => l.Conferencia is not SituacaoDaConferencia.ComoEsperado)
                ? "Sentido ainda não conferido nesta instalação"
                : string.Empty;

    public bool ConfirmandoAplicacao
    {
        get => _confirmandoAplicacao;
        private set
        {
            if (Definir(ref _confirmandoAplicacao, value))
            {
                Reavaliar();
            }
        }
    }

    public string TextoDaConfirmacao =>
        $"Aplicar o mapa de giro salvo na catraca {Catraca}? Ela reconecta para receber e fica alguns segundos sem atender. Depois, confira girando uma vez cada regra.";

    public string MotivoParaNaoAplicar =>
        Catraca == 0 ? "Escolha a catraca."
        : Mudancas.Count > 0 ? "Salve as alterações antes de aplicar: o que vai para a catraca é o que está salvo."
        : _versaoSalva.Length == 0 ? "O que está salvo não pode ser aplicado (veja os avisos)."
        : _ultimaAplicacao?.Situacao is SituacaoDoComando.Pendente or SituacaoDoComando.Recebido ? "Já há um pedido de aplicar em andamento."
        : !OperadorInformado ? "Informe o seu nome."
        : string.Empty;

    private bool OperadorInformado => Operador.Trim().Length >= 2;

    private bool PodeSalvar => Catraca > 0 && OperadorInformado && Mudancas.Count > 0 && Linhas.All(l => l.Erro.Length == 0);

    private bool PodeAplicar => MotivoParaNaoAplicar.Length == 0;

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Catraca == 0 ? Task.CompletedTask : CarregarAsync(Catraca, cancelamento);

    /// <summary>Carrega o mapa da catraca (troca de catraca joga fora o que não foi salvo).</summary>
    public Task<bool> CarregarAsync(int inner, CancellationToken cancelamento = default) =>
        Tentar(async () =>
        {
            Catraca = inner;
            ConfirmandoAplicacao = false;
            if (inner <= 0)
            {
                Linhas = [];
                CalcularMudancas();
                return;
            }

            var mapa = await Cliente.ObterMapaDeGiroAsync(new ObterMapaDeGiroRequest { Inner = inner }, cancellationToken: cancelamento);
            var comandos = await Cliente.ListarComandosAsync(new ListarComandosRequest { Inner = inner, Limite = 50 }, cancellationToken: cancelamento);
            if (inner != Catraca)
            {
                return;
            }

            Linhas = [.. mapa.Regras.Select(r => new LinhaDoMapaDeGiro(r, mapa.FuncaoDoPerfil, CalcularMudancas))];
            AvisosDoSalvo = [.. mapa.Problemas];
            AlteradoPor = mapa.AlteradoPor;
            _versaoSalva = mapa.VersaoSalva;
            _versaoAplicada = mapa.VersaoAplicada;
            _ultimaAplicacao = comandos.Comandos.FirstOrDefault(c => c.Tipo is TipoDeComando.AplicarConfiguracao);
            (SituacaoNaCatraca, SinalDaSituacao) = ParametrizacaoViewModel.Situacao(_versaoSalva, _versaoAplicada, _ultimaAplicacao);
            Problemas = [];
            AplicarFoco();
            CalcularMudancas();
            Avisar(nameof(SeloDaCatraca));
        });

    /// <summary>Põe a linha da origem em foco (o clique na urna leva ao leitor 2).</summary>
    public void Focar(Contracts.Edge.V1.OrigemDoGiro? origem)
    {
        _foco = origem;
        AplicarFoco();
    }

    private void AplicarFoco()
    {
        foreach (var linha in Linhas)
        {
            linha.EmFoco = _foco is { } f && linha.Origem == f;
        }

        Avisar(nameof(LinhaEmFoco));
    }

    private void CalcularMudancas()
    {
        Mudancas = [.. Linhas.Where(l => l.Alterada).Select(l => new LinhaDeMudanca(l.Nome, l.Descrever(salvo: true), l.Descrever(salvo: false)))];
        Avisar(nameof(ResumoDasMudancas));
        Avisar(nameof(LinhaEmFoco));
        Reavaliar();
    }

    private void Reavaliar()
    {
        Avisar(nameof(MotivoParaNaoAplicar));
        Avisar(nameof(TextoDaConfirmacao));
        Salvar.ReavaliarDisponibilidade();
        Descartar.ReavaliarDisponibilidade();
        PedirAplicacao.ReavaliarDisponibilidade();
        ConfirmarAplicacao.ReavaliarDisponibilidade();
        CancelarAplicacao.ReavaliarDisponibilidade();
    }

    private async Task SalvarAsync()
    {
        var inner = Catraca;
        var gravado = false;

        await Tentar(async () =>
        {
            var pedido = new GravarMapaDeGiroRequest { Inner = inner, Operador = Operador.Trim() };
            pedido.Regras.AddRange(Linhas.Select(l => l.Pedido()));

            var r = await Cliente.GravarMapaDeGiroAsync(pedido);
            Problemas = [.. r.Problemas];
            gravado = r.Gravado;
            Mensagem = r.Gravado
                ? $"Mapa de giro salvo para a catraca {inner}. Ela só passa a usar depois de \"Aplicar nesta catraca\"."
                : "Não foi salvo. Corrija os itens indicados.";
        }).ConfigureAwait(true);

        if (gravado && inner == Catraca)
        {
            var mensagem = Mensagem;
            await CarregarAsync(inner).ConfigureAwait(true);
            Mensagem = mensagem;
        }
    }

    private async Task AplicarAsync()
    {
        var inner = Catraca;
        ConfirmandoAplicacao = false;

        await Tentar(async () =>
        {
            var r = await Cliente.EnviarComandoAsync(new EnviarComandoRequest
            {
                Inner = inner,
                Tipo = TipoDeComando.AplicarConfiguracao,
                Operador = Operador.Trim(),
            });

            Problemas = [.. r.Problemas];
            Mensagem = r.Aceito
                ? $"Pedido à catraca {inner}: ela reconecta e fica alguns segundos sem atender. Depois, gire uma vez por regra e confirme o lado aqui."
                : "Não foi pedido. Corrija os itens indicados.";
        }).ConfigureAwait(true);

        if (inner == Catraca)
        {
            var mensagem = Mensagem;
            await CarregarAsync(inner).ConfigureAwait(true);
            Mensagem = mensagem;
        }
    }

    private async Task ConferirAsync(LinhaDoMapaDeGiro linha, bool comoEsperado)
    {
        var inner = Catraca;
        if (!OperadorInformado)
        {
            Mensagem = "Informe o seu nome: ele fica registrado com a conferência.";
            return;
        }

        if (linha.Alterada)
        {
            Mensagem = "Salve e aplique esta regra antes de conferir: a conferência vale para a função que a catraca está usando.";
            return;
        }

        var registrada = false;
        await Tentar(async () =>
        {
            var r = await Cliente.RegistrarConferenciaDoGiroAsync(new RegistrarConferenciaDoGiroRequest
            {
                Inner = inner,
                Funcao = linha.Regra.Funcao,
                ComoEsperado = comoEsperado,
                Operador = Operador.Trim(),
            });

            Problemas = [.. r.Problemas];
            registrada = r.Registrada;
            Mensagem = !r.Registrada
                ? "A conferência não foi registrada. Corrija os itens indicados."
                : comoEsperado
                    ? $"Conferido: {LinhaDoMapaDeGiro.NomeDaFuncao(linha.Regra.Funcao)} gira para o lado da seta nesta catraca."
                    : $"Registrado: {LinhaDoMapaDeGiro.NomeDaFuncao(linha.Regra.Funcao)} girou ao contrário. Troque a função desta regra.";
        }).ConfigureAwait(true);

        if (registrada && inner == Catraca)
        {
            var mensagem = Mensagem;
            await CarregarAsync(inner).ConfigureAwait(true);
            Mensagem = mensagem;
        }
    }
}
