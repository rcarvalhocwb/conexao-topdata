using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>
/// Uma leitura recusada como desconhecida. O código não chega à tela: aparece só a máscara, quantas vezes
/// e quando; o cadastro usa o identificador da tentativa.
/// </summary>
public sealed record LinhaDeLeitura(string TentativaId, string Codigo, string Vezes, string Ultima);

/// <summary>
/// Cadastro de cartões na operação (migração 026): abre a sessão de lote por leitura na urna, mostra as
/// leituras recusadas como desconhecidas e cadastra uma delas pelo tipo e lote da sessão ou do formulário.
/// </summary>
/// <remarks>
/// <para>
/// Só aparece para quem tem a permissão <c>cartoes.cadastrar</c>; o serviço confere de novo em cada ação.
/// </para>
/// <para>
/// Durante a sessão, a leitura na urna só registra: ninguém passa e nenhum uso é consumido. Os cartões
/// lidos aqui entram no lote com o tipo escolhido e a autoria de quem abriu a sessão.
/// </para>
/// </remarks>
public sealed class CartoesNaoReconhecidosViewModel : TelaBase
{
    private IReadOnlyList<LinhaDeLeitura> _linhas = [];
    private LinhaDeLeitura? _selecionada;
    private bool _sessaoAberta;
    private string _sessaoResumo = "Nenhuma sessão de lote aberta.";
    private string _provedor = "balcao-local";
    private string _tipo = "INTEIRA";
    private string _lote = string.Empty;
    private string _usos = "1";

    public CartoesNaoReconhecidosViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null)
        : base(cliente, relogio)
    {
        Atualizar = new ComandoAssincrono(() => AtualizarAsync(CancellationToken.None));
        AbrirSessao = new ComandoAssincrono(AbrirSessaoAsync);
        FecharSessao = new ComandoAssincrono(FecharSessaoAsync);
        Cadastrar = new ComandoAssincrono(CadastrarSelecionadoAsync);
    }

    public override string Titulo => "Cartões não reconhecidos";

    public ComandoAssincrono Atualizar { get; }

    public ComandoAssincrono AbrirSessao { get; }

    public ComandoAssincrono FecharSessao { get; }

    public ComandoAssincrono Cadastrar { get; }

    /// <summary>Leituras recusadas como desconhecidas, pela máscara.</summary>
    public IReadOnlyList<LinhaDeLeitura> Linhas { get => _linhas; private set => Definir(ref _linhas, value); }

    public LinhaDeLeitura? Selecionada { get => _selecionada; set => Definir(ref _selecionada, value); }

    /// <summary>Se há uma sessão de lote aberta agora.</summary>
    public bool SessaoAberta { get => _sessaoAberta; private set => Definir(ref _sessaoAberta, value); }

    /// <summary>Resumo da sessão: o lote, o tipo, os usos e quantos cartões já entraram.</summary>
    public string SessaoResumo { get => _sessaoResumo; private set => Definir(ref _sessaoResumo, value); }

    /// <summary>Código do provedor de cartões (o balcão local, por padrão).</summary>
    public string Provedor { get => _provedor; set => Definir(ref _provedor, value); }

    /// <summary>Tipo de entrada: INTEIRA, MEIA, SOCIAL… Em maiúsculas, sem espaço.</summary>
    public string Tipo { get => _tipo; set => Definir(ref _tipo, value); }

    /// <summary>Nome do lote, de 1 a 40 letras (por exemplo, "Evento 14/11 · meia 2").</summary>
    public string Lote { get => _lote; set => Definir(ref _lote, value); }

    /// <summary>Usos por cartão, de 1 a 50.</summary>
    public string Usos { get => _usos; set => Definir(ref _usos, value); }

    public override async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        await Tentar(() => CarregarAsync(cancelamento));
    }

    private async Task CarregarAsync(CancellationToken cancelamento)
    {
        var sessao = await Cliente.ObterSessaoDeCadastroAsync(new ObterSessaoDeCadastroRequest(), cancellationToken: cancelamento);
        var lista = await Cliente.ListarLeiturasDesconhecidasAsync(
            new ListarLeiturasDesconhecidasRequest { Limite = 100 }, cancellationToken: cancelamento);

        AplicarSessao(sessao);
        var agora = Relogio();
        Linhas =
        [
            .. lista.Cartoes.Select(c => new LinhaDeLeitura(
                c.TentativaId,
                c.CodigoMascarado,
                c.Vezes.ToString(CultureInfo.InvariantCulture),
                Textos.Ha(c.UltimaVez.ToDateTimeOffset(), agora))),
        ];
    }

    public async Task AbrirSessaoAsync()
    {
        await Tentar(async () =>
        {
            var resposta = await Cliente.AbrirSessaoDeCadastroAsync(new AbrirSessaoDeCadastroRequest
            {
                ProvedorId = Provedor.Trim(),
                Categoria = Tipo.Trim(),
                Lote = Lote.Trim(),
                Usos = ParseUsos(),
            });
            AplicarSessao(resposta);
            Mensagem = TextoDaAbertura(resposta.Resultado);
            await CarregarAsync(CancellationToken.None);
        });
    }

    public async Task FecharSessaoAsync()
    {
        await Tentar(async () =>
        {
            var resposta = await Cliente.FecharSessaoDeCadastroAsync(new FecharSessaoDeCadastroRequest());
            AplicarSessao(resposta);
            Mensagem = resposta.Resultado == ResultadoDaSessaoDeCadastro.Fechada && resposta.Sessao is { } fechada
                ? $"Sessão fechada: {fechada.Cadastrados} cartão(ões) entraram no lote “{fechada.Lote}”."
                : "Não havia sessão de lote aberta.";
            await CarregarAsync(CancellationToken.None);
        });
    }

    public async Task CadastrarSelecionadoAsync()
    {
        if (Selecionada is not { } linha)
        {
            Mensagem = "Escolha uma leitura da lista para cadastrar.";
            return;
        }

        await Tentar(async () =>
        {
            var resposta = await Cliente.CadastrarLeituraDesconhecidaAsync(new CadastrarLeituraDesconhecidaRequest
            {
                TentativaId = linha.TentativaId,
                ProvedorId = Provedor.Trim(),
                Categoria = Tipo.Trim(),
                Lote = Lote.Trim(),
                Usos = ParseUsos(),
            });
            Mensagem = TextoDoCadastro(resposta.Resultado);
            Selecionada = null;
            await CarregarAsync(CancellationToken.None);
        });
    }

    private void AplicarSessao(ResultadoDaSessaoDeCadastroResponse resposta)
    {
        var aberta = resposta.Resultado is ResultadoDaSessaoDeCadastro.Aberta or ResultadoDaSessaoDeCadastro.JaAberta;
        SessaoAberta = aberta && resposta.Sessao is not null;
        SessaoResumo = SessaoAberta && resposta.Sessao is { } s
            ? $"Lote “{s.Lote}” · tipo {s.Categoria} · {s.Usos} uso(s) por cartão · {s.Cadastrados} cadastrado(s) até agora."
            : "Nenhuma sessão de lote aberta.";
    }

    private int ParseUsos() => int.TryParse(Usos, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string TextoDaAbertura(ResultadoDaSessaoDeCadastro resultado) => resultado switch
    {
        ResultadoDaSessaoDeCadastro.Aberta =>
            "Sessão aberta. Leia os cartões na urna: eles entram no lote, e ninguém passa.",
        ResultadoDaSessaoDeCadastro.JaAberta =>
            "Já existe uma sessão de lote aberta. Feche-a antes de abrir outra.",
        ResultadoDaSessaoDeCadastro.ProvedorDesconhecido or ResultadoDaSessaoDeCadastro.ProvedorNaoReutilizavel =>
            "O provedor informado não serve para cartões reutilizáveis. Confira o código do provedor.",
        _ => "Confira o tipo (em maiúsculas, como INTEIRA ou MEIA) e o lote (de 1 a 40 letras). Os usos vão de 1 a 50.",
    };

    private static string TextoDoCadastro(ResultadoDoRegistro resultado) => resultado switch
    {
        ResultadoDoRegistro.Cadastrado => "Cartão cadastrado no lote. Ele já pode passar.",
        ResultadoDoRegistro.JaCadastrado => "Este cartão já estava cadastrado.",
        ResultadoDoRegistro.TentativaNaoEncontrada => "Esta leitura não existe mais. A lista foi atualizada.",
        ResultadoDoRegistro.ProvedorDesconhecido or ResultadoDoRegistro.ProvedorNaoReutilizavel =>
            "O provedor informado não serve para cartões reutilizáveis. Confira o código do provedor.",
        _ => "Confira o tipo (em maiúsculas) e o lote antes de cadastrar.",
    };
}
