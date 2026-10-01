using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>As abas da Parametrização. A de instalação só existe no modo técnico.</summary>
public enum AbaDaParametrizacao
{
    Leitura,
    Liberacao,
    Display,
    Instalacao,

    /// <summary>Mapa de giro (D9, docs/34 §9): por origem, a função que libera e como conta.</summary>
    Giro,
}

/// <summary>Uma opção de um campo de lista.</summary>
/// <param name="Valor">O valor no formato do contrato (ver <c>CampoDaCatraca</c> no proto).</param>
/// <param name="Nome">Como aparece na lista, no modo atual.</param>
/// <param name="Disponivel">Falso quando aguarda confirmação: aparece, mas não pode ser escolhida.</param>
public sealed record OpcaoDoCampo(string Valor, string Nome, bool Disponivel = true);

/// <summary>Uma linha da lista "o que muda (atual → novo)".</summary>
public sealed record LinhaDeMudanca(string Campo, string Atual, string Novo);

/// <summary>
/// O que a tela sabe de cada campo sem perguntar ao serviço: rótulos, aba, se é técnico, e os
/// nomes de cada valor.
/// </summary>
/// <remarks>
/// <para>
/// Os nomes vêm da matriz de funções (<c>docs/compatibility-matrix/funcoes-easyinner.csv</c>):
/// tipo de leitor de FUN:14 (EI-013, SDK 6.0.2.0, que corrige o manual), operação dos leitores
/// de FUN:15/16 (EI-014/015), funções de liberação de FUN:42-45 (EI-041 a EI-044), Wiegand de
/// FUN:25 (EI-024), formas de entrada de FUN:33 (EI-032). Esses nomes são do SDK e só aparecem
/// no modo técnico; o modo guiado usa as palavras do operador (GLOSSARIO, docs/34 §7, P13).
/// </para>
/// <para>
/// O que pode ou não ser escolhido não é decidido aqui: vem do serviço
/// (<c>valores_aguardando</c> e <c>situacao</c>), que também recusa a gravação.
/// </para>
/// </remarks>
internal sealed record DescricaoDoCampo(
    CampoDaCatraca Campo,
    AbaDaParametrizacao Aba,
    bool SoTecnico,
    string RotuloGuiado,
    string RotuloTecnico,
    IReadOnlyList<(string Valor, string Tecnico, string? Guiado)>? Opcoes,
    string AvisoGuiado = "")
{
    /// <summary>Os oito campos que a catraca pode sobrepor, na ordem da tela.</summary>
    public static IReadOnlyList<DescricaoDoCampo> Todos { get; } =
    [
        new(
            CampoDaCatraca.TipoDeLeitor,
            AbaDaParametrizacao.Leitura,
            SoTecnico: false,
            "Leitor de ingressos",
            "Tipo de leitor (ConfigurarTipoLeitor, EI-013)",
            [
                ("0", "0 · barras", null),
                ("1", "1 · magnético", null),
                ("2", "2 · proximidade Abatrack2", null),
                ("3", "3 · Wiegand", null),
                ("4", "4 · proximidade SmartCard serial", null),
                ("5", "5 · barras serial", "Leitor de código de barras"),
                ("6", "6 · Wiegand FC sem separador", null),
                ("7", "7 · Wiegand FC com separador", null),
                ("8", "8 · QR Code por letras", "Leitor de QR Code"),
            ],
            AvisoGuiado: "Qual dos dois leitores lê o QR desta catraca ainda está sendo confirmado na bancada. Use o de QR Code; troque só se o QR não for lido."),
        new(
            CampoDaCatraca.OperacaoDoLeitor2,
            AbaDaParametrizacao.Leitura,
            SoTecnico: false,
            "Leitor da urna (o cartão entra pela fenda)",
            "Leitor 2 — urna (ConfigurarLeitor2, EI-015)",
            Leitores(guiado: true)),
        new(
            CampoDaCatraca.OperacaoDoLeitor1,
            AbaDaParametrizacao.Leitura,
            SoTecnico: true,
            "Leitor da frente",
            "Leitor 1 — frente (ConfigurarLeitor1, EI-014)",
            Leitores(guiado: false)),
        new(
            CampoDaCatraca.TempoDoAcionamento1,
            AbaDaParametrizacao.Liberacao,
            SoTecnico: false,
            "Segundos que a catraca fica liberada esperando o giro (1 a 50)",
            "Tempo do relé 1, em segundos (ConfigurarAcionamento1, EI-016)",
            null),
        new(
            CampoDaCatraca.FuncaoDeLiberacaoDaEntrada,
            AbaDaParametrizacao.Liberacao,
            SoTecnico: true,
            "Sentido da liberação",
            "Função de liberação da entrada (comissionamento)",
            [
                ("Entrada", "LiberarCatracaEntrada (EI-041)", null),
                ("EntradaInvertida", "LiberarCatracaEntradaInvertida (EI-043)", null),
                ("Saida", "LiberarCatracaSaida (EI-042)", null),
                ("SaidaInvertida", "LiberarCatracaSaidaInvertida (EI-044)", null),
            ]),
        new(
            CampoDaCatraca.MensagemPadrao,
            AbaDaParametrizacao.Display,
            SoTecnico: false,
            "Mensagem no visor da catraca (até 32 letras)",
            "Mensagem padrão (EnviarMensagemPadraoOnLine, EI-056)",
            null),
        new(
            CampoDaCatraca.WiegandDoisLeitores,
            AbaDaParametrizacao.Instalacao,
            SoTecnico: true,
            "Dois leitores Wiegand",
            "Wiegand com dois leitores (ConfigurarWiegandDoisLeitores, EI-024)",
            [
                ("0,0", "Habilita 0 · ExibirMensagem 0", null),
                ("0,1", "Habilita 0 · ExibirMensagem 1", null),
                ("1,0", "Habilita 1 · ExibirMensagem 0", null),
                ("1,1", "Habilita 1 · ExibirMensagem 1", null),
            ]),
        new(
            CampoDaCatraca.FormasDeEntradaOnLine,
            AbaDaParametrizacao.Instalacao,
            SoTecnico: true,
            "Rearme do leitor",
            "Formas de entrada on-line: dígitos, eco, forma, tempo, cursor (EnviarFormasEntradasOnLine, EI-032)",
            null),
    ];

    public bool EhLista => Opcoes is not null;

    /// <summary>
    /// FUN:15/16. No guiado, o leitor da urna é ligado (1, somente entrada) ou desligado (0),
    /// como sempre foi nas Configurações do evento.
    /// </summary>
    private static (string, string, string?)[] Leitores(bool guiado) =>
    [
        ("0", "0 · desabilitado", guiado ? "Desligado" : null),
        ("1", "1 · somente entrada", guiado ? "Ligado" : null),
        ("2", "2 · somente saída", null),
        ("3", "3 · entrada e saída", null),
        ("4", "4 · entrada e saída invertida", null),
    ];
}

/// <summary>
/// Um campo da Parametrização: o que está salvo para a catraca, o que ela herdaria, o que o
/// operador está mudando, a validação e o selo.
/// </summary>
public sealed class CampoDaParametrizacao : Notificavel
{
    private readonly DescricaoDoCampo _descricao;
    private readonly Func<bool> _modoTecnico;
    private readonly Action _aoMudar;
    private readonly IReadOnlyList<string> _aguardando;
    private bool _herda;
    private string _valor;

    internal CampoDaParametrizacao(DescricaoDoCampo descricao, CampoConfiguradoDaCatraca servidor, Func<bool> modoTecnico, Action aoMudar)
    {
        _descricao = descricao;
        _modoTecnico = modoTecnico;
        _aoMudar = aoMudar;
        _aguardando = [.. servidor.ValoresAguardando];
        ValorSalvo = servidor.HasValorDaCatraca ? servidor.ValorDaCatraca : null;
        ValorHerdado = servidor.ValorHerdado;
        ValorEfetivo = servidor.ValorEfetivo;
        Origem = servidor.Origem;
        Situacao = servidor.Situacao;
        MotivoDaSituacao = servidor.Motivo;
        MotivoDosValoresAguardando = servidor.MotivoDosValoresAguardando;
        AvisoTecnico = servidor.Aviso;
        _herda = ValorSalvo is null;
        _valor = ValorSalvo ?? ValorHerdado;
    }

    public CampoDaCatraca Campo => _descricao.Campo;

    public AbaDaParametrizacao Aba => _descricao.Aba;

    /// <summary>Só aparece no modo técnico.</summary>
    public bool SoTecnico => _descricao.SoTecnico;

    public string Rotulo => _modoTecnico() ? _descricao.RotuloTecnico : _descricao.RotuloGuiado;

    /// <summary>O que a catraca tem gravado para si; nulo = herda do evento.</summary>
    public string? ValorSalvo { get; }

    /// <summary>O que vale se a catraca não definir (fábrica + evento).</summary>
    public string ValorHerdado { get; }

    /// <summary>O que vale hoje, com as três camadas.</summary>
    public string ValorEfetivo { get; }

    public OrigemDoValor Origem { get; }

    public SituacaoDoCampo Situacao { get; }

    /// <summary>Por que não chega à catraca (do serviço, com o ensaio).</summary>
    public string MotivoDaSituacao { get; }

    public string MotivoDosValoresAguardando { get; }

    public string AvisoTecnico { get; }

    /// <summary>
    /// O campo chega à catraca e pode ser mudado. Falso: aparece desabilitado, com o selo
    /// "Aguardando confirmação" e o motivo (docs/35, regra inegociável).
    /// </summary>
    public bool Disponivel => Situacao is SituacaoDoCampo.Enviado;

    /// <summary>Campo de lista (ComboBox) ou de texto.</summary>
    public bool EhLista => _descricao.EhLista;

    public bool EhTexto => !_descricao.EhLista;

    /// <summary>"Usar o padrão do evento": a catraca não define o campo.</summary>
    public bool Herda
    {
        get => _herda;
        set
        {
            if (!Disponivel || !Definir(ref _herda, value))
            {
                return;
            }

            if (value)
            {
                _valor = ValorHerdado;
                Avisar(nameof(Valor));
                Avisar(nameof(Escolhida));
            }

            Avisar(nameof(Editavel));
            Mudou();
        }
    }

    /// <summary>Texto da caixa "herdar": o que vale no evento.</summary>
    public string TextoHerdar => $"Usar o padrão do evento ({Nome(ValorHerdado)})";

    /// <summary>O editor aceita digitação ou escolha.</summary>
    public bool Editavel => Disponivel && !Herda;

    /// <summary>O valor no editor, no formato do contrato.</summary>
    public string Valor
    {
        get => _valor;
        set
        {
            if (Definir(ref _valor, value ?? string.Empty))
            {
                Avisar(nameof(Escolhida));
                Mudou();
            }
        }
    }

    /// <summary>As opções da lista no modo atual (no guiado, só as que têm nome de operador).</summary>
    public IReadOnlyList<OpcaoDoCampo> Opcoes
    {
        get
        {
            if (_descricao.Opcoes is not { } todas)
            {
                return [];
            }

            var tecnico = _modoTecnico();
            var visiveis = todas
                .Where(o => tecnico || o.Guiado is not null || o.Valor == Valor || o.Valor == ValorHerdado)
                .Select(o => new OpcaoDoCampo(o.Valor, NomeDaOpcao(o, tecnico), !_aguardando.Contains(o.Valor)))
                .ToList();
            return visiveis;
        }
    }

    /// <summary>A opção escolhida na lista.</summary>
    public OpcaoDoCampo? Escolhida
    {
        get => Opcoes.FirstOrDefault(o => o.Valor == Valor);
        set
        {
            if (value is not null)
            {
                Valor = value.Valor;
            }
        }
    }

    /// <summary>O que a gravação manda para este campo: nulo = herda.</summary>
    public string? ValorNovo => Herda ? null : Normalizar(Valor);

    /// <summary>Mudou em relação ao que está salvo para a catraca.</summary>
    public bool Alterado => !string.Equals(ValorNovo, ValorSalvo, StringComparison.Ordinal);

    /// <summary>A validação no campo, antes de salvar. Vazia = sem erro.</summary>
    public string Erro => Herda || !Alterado ? string.Empty : Validar();

    /// <summary>De onde vem o valor que vale hoje.</summary>
    public string TextoDaOrigem => Origem switch
    {
        OrigemDoValor.Catraca => "Definido nesta catraca",
        OrigemDoValor.Evento => "Vem do evento",
        _ => "Padrão do sistema",
    };

    /// <summary>
    /// Os motivos do selo "Aguardando confirmação" deste campo, no modo atual. Vazia = sem selo.
    /// </summary>
    public IReadOnlyList<string> Selos
    {
        get
        {
            var tecnico = _modoTecnico();
            var selos = new List<string>();

            if (!Disponivel)
            {
                selos.Add(MotivoDaSituacao);
            }

            if (tecnico && _aguardando.Count > 0)
            {
                selos.Add(MotivoDosValoresAguardando);
            }

            var aviso = tecnico ? AvisoTecnico : _descricao.AvisoGuiado;
            if (aviso.Length > 0)
            {
                selos.Add(aviso);
            }

            return selos;
        }
    }

    /// <summary>Como um valor aparece na lista "o que muda"; nulo = herda.</summary>
    public string Descrever(string? valor) => valor is null ? $"padrão do evento ({Nome(ValorHerdado)})" : Nome(valor);

    /// <summary>Nome de um valor, no modo atual.</summary>
    public string Nome(string valor)
    {
        if (_descricao.Opcoes is { } todas)
        {
            var tecnico = _modoTecnico();
            return todas.FirstOrDefault(o => o.Valor == valor) is { Valor: not null } o
                ? NomeDaOpcao(o, tecnico)
                : valor;
        }

        return Campo switch
        {
            CampoDaCatraca.TempoDoAcionamento1 => $"{valor} s",
            CampoDaCatraca.MensagemPadrao => $"“{valor}”",
            _ => valor,
        };
    }

    /// <summary>A tela trocou de modo: rótulos, nomes e selos mudam.</summary>
    internal void TrocouDeModo()
    {
        Avisar(nameof(Rotulo));
        Avisar(nameof(Opcoes));
        Avisar(nameof(Escolhida));
        Avisar(nameof(Selos));
        Avisar(nameof(TextoHerdar));
    }

    private string NomeDaOpcao((string Valor, string Tecnico, string? Guiado) opcao, bool tecnico)
    {
        var nome = tecnico ? opcao.Tecnico : opcao.Guiado ?? "Outro (definido no modo técnico)";
        return _aguardando.Contains(opcao.Valor) ? $"{nome} — aguardando confirmação" : nome;
    }

    private void Mudou()
    {
        Avisar(nameof(ValorNovo));
        Avisar(nameof(Alterado));
        Avisar(nameof(Erro));
        _aoMudar();
    }

    private string? Normalizar(string valor)
    {
        switch (Campo)
        {
            case CampoDaCatraca.TempoDoAcionamento1:
                return int.TryParse(valor.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString(CultureInfo.InvariantCulture)
                    : valor;
            case CampoDaCatraca.FormasDeEntradaOnLine:
                var partes = valor.Split(',').Select(p => p.Trim()).ToArray();
                return partes.All(p => byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    ? string.Join(',', partes.Select(p => byte.Parse(p, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)))
                    : valor;
            default:
                return valor;
        }
    }

    /// <summary>
    /// As faixas que a tela confere antes de salvar, com a mensagem do docs/34-anexos/04 §5.2.
    /// O serviço confere de novo (e as regras entre campos, <c>DeviceConfiguration.Validar</c>).
    /// </summary>
    private string Validar()
    {
        var valor = ValorNovo ?? string.Empty;

        if (_descricao.Opcoes is { } todas)
        {
            if (!todas.Any(o => o.Valor == valor))
            {
                return "Escolha uma opção da lista.";
            }

            return _aguardando.Contains(valor)
                ? "Aguardando confirmação: esta opção ainda não pode ser escolhida."
                : string.Empty;
        }

        switch (Campo)
        {
            case CampoDaCatraca.TempoDoAcionamento1:
                return int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos) && segundos is >= 1 and <= 50
                    ? string.Empty
                    : "O tempo vai de 1 a 50 segundos.";
            case CampoDaCatraca.MensagemPadrao:
                if (string.IsNullOrWhiteSpace(valor))
                {
                    return "Escreva a mensagem (até 32 letras).";
                }

                return valor.Length > 32 ? $"A mensagem tem {valor.Length} letras; o visor mostra 32." : string.Empty;
            case CampoDaCatraca.FormasDeEntradaOnLine:
                var partes = valor.Split(',');
                var numeros = new List<int>();
                foreach (var parte in partes)
                {
                    if (!byte.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                    {
                        break;
                    }

                    numeros.Add(b);
                }

                if (partes.Length != 5 || numeros.Count != 5)
                {
                    return "Cinco números de 0 a 255, separados por vírgula.";
                }

                // FUN:33: FormaEntrada 0-7, 10-14, 100-105.
                return numeros[2] is <= 7 or (>= 10 and <= 14) or (>= 100 and <= 105)
                    ? string.Empty
                    : "A forma de entrada (3º número) vai de 0 a 7, de 10 a 14 ou de 100 a 105 (FUN:33).";
            default:
                return string.Empty;
        }
    }
}

/// <summary>
/// Parametrização de uma catraca (Etapa A.6 do docs/35): o que ela sobrepõe ao evento, em
/// abas, com validação no campo, a lista "o que muda (atual → novo)" antes de salvar, e
/// aplicar com confirmação, com o resultado vindo do histórico de comandos.
/// </summary>
/// <remarks>
/// <para>
/// Entra pela "Gerenciar catraca" (é o detalhe da catraca, docs/34 §7.3), e não pelo menu.
/// Salvar e aplicar são passos separados e explícitos: salvar grava a camada da catraca;
/// aplicar pede o comando "Aplicar configuração" daquela catraca, que reconecta e fica alguns
/// segundos sem atender. Os dois exigem o nome digitado (não há login, docs/27 §11).
/// </para>
/// <para>
/// <b>"Aplicada" nunca é presumida.</b> A tela só diz "aplicada" quando a versão que a catraca
/// aceitou (publicada pelo worker, Etapa A.5) é igual à do salvo e o último pedido de aplicar
/// desta catraca, se houver, está concluído. Enquanto o pedido está na fila ou em curso, é
/// "aplicando", mesmo que as versões já coincidam.
/// </para>
/// <para>
/// Modo guiado × técnico: no guiado aparecem só os campos do operador, com as palavras dele;
/// no técnico, todos, com os nomes do SDK. O modo técnico é conveniência, não segurança (sem
/// login, docs/34-anexos/04 §4.1); o serviço recusa o que não está confirmado.
/// </para>
/// </remarks>
public sealed class ParametrizacaoViewModel : TelaBase
{
    private IReadOnlyList<LinhaDeCatraca> _catracas = [];
    private int _catraca;
    private string _operador = string.Empty;
    private bool _modoTecnico;
    private int _aba;
    private IReadOnlyList<CampoDaParametrizacao> _campos = [];
    private IReadOnlyList<LinhaDeMudanca> _mudancas = [];
    private IReadOnlyList<string> _problemas = [];
    private IReadOnlyList<string> _avisosDoSalvo = [];
    private string _versaoSalva = string.Empty;
    private string _versaoAplicada = string.Empty;
    private string _situacaoNaCatraca = "—";
    private Sinal _sinalDaSituacao = Sinal.Neutro;
    private string _detalheDaSituacao = string.Empty;
    private IReadOnlyList<LinhaDeComando> _aplicacoes = [];
    private ComandoRegistrado? _ultimaAplicacao;
    private bool _confirmandoAplicacao;
    private Google.Protobuf.WellKnownTypes.Timestamp? _alteradaEm;
    private string _alteradaPor = string.Empty;
    private Google.Protobuf.WellKnownTypes.Timestamp? _aplicadaEm;

    public ParametrizacaoViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null, TimeSpan? esperaPeloResultado = null)
        : base(cliente, relogio)
    {
        EsperaPeloResultado = esperaPeloResultado ?? TimeSpan.FromSeconds(1.5);
        Giro = new MapaDeGiroViewModel(cliente, relogio);
        Salvar = new ComandoAssincrono(SalvarAsync, () => PodeSalvar);
        Descartar = new ComandoAssincrono(() => CarregarAsync(CancellationToken.None), () => Mudancas.Count > 0);
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
    }

    public override string Titulo => "Parametrização";

    /// <summary>Quanto esperar o programa da catraca antes de buscar o desfecho.</summary>
    public TimeSpan EsperaPeloResultado { get; }

    /// <summary>A aba Giro: o mesmo mapa de giro do painel do gêmeo, para esta catraca.</summary>
    public MapaDeGiroViewModel Giro { get; }

    /// <summary>Grava a camada da catraca (não aplica).</summary>
    public ComandoAssincrono Salvar { get; }

    /// <summary>Volta ao que está salvo, jogando fora o que foi mudado na tela.</summary>
    public ComandoAssincrono Descartar { get; }

    /// <summary>Primeiro passo de aplicar: mostra a confirmação.</summary>
    public ComandoAssincrono PedirAplicacao { get; }

    /// <summary>Segundo passo: pede o "Aplicar configuração" desta catraca.</summary>
    public ComandoAssincrono ConfirmarAplicacao { get; }

    public ComandoAssincrono CancelarAplicacao { get; }

    public IReadOnlyList<LinhaDeCatraca> Catracas { get => _catracas; private set => Definir(ref _catracas, value); }

    /// <summary>Número da catraca; 0 enquanto nenhuma foi escolhida.</summary>
    public int Catraca
    {
        get => _catraca;
        set
        {
            if (!Definir(ref _catraca, value))
            {
                return;
            }

            ConfirmandoAplicacao = false;
            Campos = [];
            Reavaliar();

            // Com a tela já aberta (lista de catracas carregada), trocar de catraca carrega a
            // nova. Na primeira abertura quem carrega é a navegação.
            if (Catracas.Count > 0)
            {
                _ = CarregarAsync(CancellationToken.None);
            }
        }
    }

    /// <summary>Quem está gravando ou aplicando. Não há login: fica registrado como foi digitado.</summary>
    public string Operador
    {
        get => _operador;
        set
        {
            if (Definir(ref _operador, value ?? string.Empty))
            {
                Giro.Operador = _operador;
                Reavaliar();
            }
        }
    }

    /// <summary>
    /// Mostra os campos e os nomes técnicos. Conveniência, não segurança: o serviço recusa o
    /// que não está confirmado, em qualquer modo.
    /// </summary>
    public bool ModoTecnico
    {
        get => _modoTecnico;
        set
        {
            if (!Definir(ref _modoTecnico, value))
            {
                return;
            }

            if (!value && AbaSelecionada == (int)AbaDaParametrizacao.Instalacao)
            {
                AbaSelecionada = (int)AbaDaParametrizacao.Leitura;
            }

            foreach (var campo in _campos)
            {
                campo.TrocouDeModo();
            }

            AvisarListas();
            CalcularMudancas();
        }
    }

    /// <summary>Aba aberta (índice de <see cref="AbaDaParametrizacao"/>).</summary>
    public int AbaSelecionada { get => _aba; set => Definir(ref _aba, value); }

    /// <summary>Todos os campos da catraca, inclusive os técnicos.</summary>
    public IReadOnlyList<CampoDaParametrizacao> Campos
    {
        get => _campos;
        private set
        {
            if (Definir(ref _campos, value))
            {
                AvisarListas();
                CalcularMudancas();
            }
        }
    }

    public IReadOnlyList<CampoDaParametrizacao> CamposDaLeitura => Visiveis(AbaDaParametrizacao.Leitura);

    public IReadOnlyList<CampoDaParametrizacao> CamposDaLiberacao => Visiveis(AbaDaParametrizacao.Liberacao);

    public IReadOnlyList<CampoDaParametrizacao> CamposDoDisplay => Visiveis(AbaDaParametrizacao.Display);

    public IReadOnlyList<CampoDaParametrizacao> CamposDaInstalacao => Visiveis(AbaDaParametrizacao.Instalacao);

    /// <summary>"O que muda (atual → novo)": exatamente os campos alterados na tela.</summary>
    public IReadOnlyList<LinhaDeMudanca> Mudancas { get => _mudancas; private set => Definir(ref _mudancas, value); }

    public string ResumoDasMudancas => Mudancas.Count switch
    {
        0 => "Nenhuma alteração. Mude um campo para ver aqui o que muda.",
        1 => "1 alteração não salva.",
        var n => $"{n} alterações não salvas.",
    };

    /// <summary>O que o serviço recusou na última gravação ou no último pedido.</summary>
    public IReadOnlyList<string> Problemas { get => _problemas; private set => Definir(ref _problemas, value); }

    /// <summary>Problemas do que está salvo (valor ilegível, salvo que o worker recusaria).</summary>
    public IReadOnlyList<string> AvisosDoSalvo { get => _avisosDoSalvo; private set => Definir(ref _avisosDoSalvo, value); }

    /// <summary>Versão do salvo: o que o "Aplicar" enviaria. Vazia: não pode ser aplicado.</summary>
    public string VersaoSalva { get => _versaoSalva; private set => Definir(ref _versaoSalva, value); }

    /// <summary>Versão que a catraca aceitou. Vazia: nenhuma confirmada desde que o worker subiu.</summary>
    public string VersaoAplicada { get => _versaoAplicada; private set => Definir(ref _versaoAplicada, value); }

    /// <summary>"Aplicada", "Salva, não aplicada", "Aplicando…".</summary>
    public string SituacaoNaCatraca { get => _situacaoNaCatraca; private set => Definir(ref _situacaoNaCatraca, value); }

    public Sinal SinalDaSituacao { get => _sinalDaSituacao; private set => Definir(ref _sinalDaSituacao, value); }

    /// <summary>Quem salvou e quando; quando a catraca aceitou.</summary>
    public string DetalheDaSituacao { get => _detalheDaSituacao; private set => Definir(ref _detalheDaSituacao, value); }

    /// <summary>Os últimos pedidos de aplicar desta catraca, do histórico de comandos.</summary>
    public IReadOnlyList<LinhaDeComando> Aplicacoes { get => _aplicacoes; private set => Definir(ref _aplicacoes, value); }

    /// <summary>A confirmação de aplicar está aberta.</summary>
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
        $"Aplicar a configuração salva na catraca {Catraca}? Ela reconecta para receber e fica alguns segundos sem atender. Se estiver no pico, prefira aplicar depois.";

    /// <summary>Por que não dá para aplicar agora; vazio quando dá.</summary>
    public string MotivoParaNaoAplicar =>
        Catraca == 0 ? "Escolha a catraca."
        : Mudancas.Count > 0 ? "Salve as alterações antes de aplicar: o que vai para a catraca é o que está salvo."
        : VersaoSalva.Length == 0 ? "O que está salvo não pode ser aplicado (veja os avisos)."
        : AplicacaoEmAndamento ? "Já há um pedido de aplicar em andamento."
        : !OperadorInformado ? "Informe o seu nome."
        : string.Empty;

    private bool OperadorInformado => Operador.Trim().Length >= 2;

    private bool PodeSalvar => Catraca > 0 && OperadorInformado && Mudancas.Count > 0 && Campos.All(c => c.Erro.Length == 0);

    private bool PodeAplicar => MotivoParaNaoAplicar.Length == 0;

    private bool AplicacaoEmAndamento =>
        _ultimaAplicacao?.Situacao is SituacaoDoComando.Pendente or SituacaoDoComando.Recebido;

    /// <summary>
    /// A situação da configuração na catraca. "Aplicada" só com as versões iguais e o último
    /// pedido de aplicar (se houver) concluído; nunca presumida (docs/34-anexos/04 §5.1).
    /// </summary>
    /// <param name="versaoSalva">Do salvo (vazia: não pode ser aplicado).</param>
    /// <param name="versaoAplicada">A que a catraca aceitou (vazia: nenhuma ainda).</param>
    /// <param name="ultimaAplicacao">O último "Aplicar configuração" desta catraca, se houver.</param>
    public static (string Texto, Sinal Sinal) Situacao(string versaoSalva, string versaoAplicada, ComandoRegistrado? ultimaAplicacao)
    {
        ArgumentNullException.ThrowIfNull(versaoSalva);
        ArgumentNullException.ThrowIfNull(versaoAplicada);
        var pedido = ultimaAplicacao?.Situacao;

        if (versaoSalva.Length == 0)
        {
            return ("Salva, mas não pode ser aplicada", Sinal.Problema);
        }

        if (pedido is SituacaoDoComando.Pendente or SituacaoDoComando.Recebido)
        {
            return ("Aplicando: aguardando a catraca", Sinal.Atencao);
        }

        if (versaoAplicada.Length == 0)
        {
            return ("Salva; a catraca ainda não confirmou nenhuma configuração", Sinal.Atencao);
        }

        if (!string.Equals(versaoSalva, versaoAplicada, StringComparison.Ordinal))
        {
            return pedido switch
            {
                SituacaoDoComando.Falhou => ("Salva, não aplicada: o último pedido falhou", Sinal.Problema),
                SituacaoDoComando.Expirado => ("Salva, não aplicada: o último pedido não foi executado a tempo", Sinal.Atencao),
                _ => ("Salva, não aplicada", Sinal.Atencao),
            };
        }

        return pedido switch
        {
            SituacaoDoComando.Falhou or SituacaoDoComando.Expirado =>
                ("A catraca está com a configuração salva, mas o último pedido não foi concluído", Sinal.Atencao),
            _ => ("Aplicada: a catraca está com a configuração salva", Sinal.Bom),
        };
    }

    public override Task AtualizarAsync(CancellationToken cancelamento = default) => CarregarAsync(cancelamento);

    /// <summary>
    /// A atualização periódica: só a situação na catraca e o histórico de aplicar. Os campos
    /// não são recarregados por cima do que o operador está digitando.
    /// </summary>
    public Task AcompanharAsync(CancellationToken cancelamento = default) =>
        Catraca == 0 ? Task.CompletedTask : Tentar(() => AcompanharInternoAsync(Catraca, cancelamento));

    private IReadOnlyList<CampoDaParametrizacao> Visiveis(AbaDaParametrizacao aba) =>
        [.. _campos.Where(c => c.Aba == aba && (ModoTecnico || !c.SoTecnico))];

    private void AvisarListas()
    {
        Avisar(nameof(CamposDaLeitura));
        Avisar(nameof(CamposDaLiberacao));
        Avisar(nameof(CamposDoDisplay));
        Avisar(nameof(CamposDaInstalacao));
    }

    private void CalcularMudancas()
    {
        Mudancas = [.. _campos.Where(c => c.Alterado).Select(c => new LinhaDeMudanca(c.Rotulo, c.Descrever(c.ValorSalvo), c.Descrever(c.ValorNovo)))];
        Avisar(nameof(ResumoDasMudancas));
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

    private Task<bool> CarregarAsync(CancellationToken cancelamento) =>
        Tentar(async () =>
        {
            var lista = await Cliente.ListarEquipamentosAsync(new ListarEquipamentosRequest(), cancellationToken: cancelamento);
            var agora = Relogio();
            Catracas = [.. lista.Equipamentos.Select(e => PainelAoVivoViewModel.Linha(e, agora))];

            if (_catraca == 0 && Catracas.Count > 0)
            {
                _catraca = Catracas[0].Inner;
                Avisar(nameof(Catraca));
            }

            if (_catraca == 0)
            {
                Mensagem = "Nenhuma catraca cadastrada na instalação.";
                Campos = [];
                return;
            }

            var inner = _catraca;
            var c = await Cliente.ObterConfiguracaoDaCatracaAsync(new ObterConfiguracaoDaCatracaRequest { Inner = inner }, cancellationToken: cancelamento);
            if (inner != _catraca)
            {
                return;
            }

            Campos = [.. DescricaoDoCampo.Todos
                .Select(d => (d, c.Campos.FirstOrDefault(x => x.Campo == d.Campo)))
                .Where(par => par.Item2 is not null)
                .Select(par => new CampoDaParametrizacao(par.d, par.Item2!, () => ModoTecnico, CalcularMudancas))];
            Problemas = [];
            AplicarVersoes(c);
            await AcompanharInternoAsync(inner, cancelamento);
            await Giro.CarregarAsync(inner, cancelamento);
        });

    private void AplicarVersoes(ConfiguracaoDaCatraca c)
    {
        VersaoSalva = c.VersaoSalva;
        VersaoAplicada = c.VersaoAplicada;
        AvisosDoSalvo = [.. c.Problemas];
        _alteradaPor = c.AlteradaPor;
        _alteradaEm = c.AlteradaEm;
        _aplicadaEm = c.AplicadaEm;
    }

    private async Task AcompanharInternoAsync(int inner, CancellationToken cancelamento)
    {
        var c = await Cliente.ObterConfiguracaoDaCatracaAsync(new ObterConfiguracaoDaCatracaRequest { Inner = inner }, cancellationToken: cancelamento);
        var comandos = await Cliente.ListarComandosAsync(new ListarComandosRequest { Inner = inner, Limite = 50 }, cancellationToken: cancelamento);
        if (inner != _catraca)
        {
            return;
        }

        AplicarVersoes(c);
        var aplicacoes = comandos.Comandos.Where(x => x.Tipo is TipoDeComando.AplicarConfiguracao).ToList();
        _ultimaAplicacao = aplicacoes.FirstOrDefault();
        Aplicacoes = [.. aplicacoes.Take(5).Select(LinhaDeComando.De)];

        var (texto, sinal) = Situacao(VersaoSalva, VersaoAplicada, _ultimaAplicacao);
        SituacaoNaCatraca = texto;
        SinalDaSituacao = sinal;
        DetalheDaSituacao = Detalhe();
        Reavaliar();
    }

    private string Detalhe()
    {
        static string Quando(Google.Protobuf.WellKnownTypes.Timestamp t) =>
            FusoDoEvento.NoEvento(t.ToDateTimeOffset()).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

        var salva = _alteradaEm is null || _alteradaPor.Length == 0
            ? "Esta catraca usa só o padrão do evento"
            : $"Salva por {_alteradaPor} em {Quando(_alteradaEm)}";
        var aplicada = _aplicadaEm is null ? "a catraca ainda não confirmou recebimento" : $"recebida pela catraca em {Quando(_aplicadaEm)}";
        return $"{salva} · {aplicada}.";
    }

    private async Task SalvarAsync()
    {
        var inner = Catraca;

        await Tentar(async () =>
        {
            var pedido = new GravarConfiguracaoDaCatracaRequest { Inner = inner, Operador = Operador.Trim() };
            foreach (var campo in Campos)
            {
                var valor = new ValorDaCatraca { Campo = campo.Campo };
                if (campo.ValorNovo is { } novo)
                {
                    valor.Valor = novo;
                }

                pedido.Valores.Add(valor);
            }

            var r = await Cliente.GravarConfiguracaoDaCatracaAsync(pedido);
            Problemas = [.. r.Problemas];

            if (!r.Gravada)
            {
                Mensagem = "Não foi salvo. Corrija os itens indicados.";
                return;
            }

            Mensagem = $"Salvo para a catraca {inner}. Ela só passa a usar depois de \"Aplicar nesta catraca\".";
        }).ConfigureAwait(true);

        if (Problemas.Count == 0 && inner == Catraca)
        {
            var mensagem = Mensagem;
            await CarregarAsync(CancellationToken.None).ConfigureAwait(true);
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
            if (!r.Aceito)
            {
                Mensagem = "Não foi pedido. Corrija os itens indicados.";
                return;
            }

            Mensagem = $"Pedido à catraca {inner}: ela reconecta e fica alguns segundos sem atender. O resultado aparece abaixo, vindo do histórico.";
            await AcompanharInternoAsync(inner, CancellationToken.None);
            await Task.Delay(EsperaPeloResultado);
            await AcompanharInternoAsync(inner, CancellationToken.None);
        }).ConfigureAwait(true);
    }
}
