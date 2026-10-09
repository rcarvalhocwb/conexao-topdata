using System.Globalization;
using Contracts.Edge.V1;
using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>Um ingresso consumido sem giro confirmado, na lista do operador.</summary>
/// <param name="Id">A tentativa.</param>
/// <param name="Hora">Dia e hora da leitura, no horário do evento.</param>
/// <param name="Catraca">O equipamento.</param>
/// <param name="Codigo">O código mascarado.</param>
/// <param name="Categoria">Inteira, meia etc., ou "—".</param>
/// <param name="Causa">Por que não houve passagem.</param>
public sealed record LinhaSemPassagem(string Id, string Hora, string Catraca, string Codigo, string Categoria, string Causa)
{
    /// <summary>O que aparece quando a liberação saiu e a catraca não mandou o giro.</summary>
    public const string LiberouSemGiro = "Liberou e a catraca não confirmou o giro";

    public static LinhaSemPassagem De(UsoSemPassagem uso)
    {
        ArgumentNullException.ThrowIfNull(uso);
        return new LinhaSemPassagem(
            uso.TentativaId,
            uso.Em is null ? "—" : FusoDoEvento.NoEvento(uso.Em.ToDateTimeOffset()).ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture),
            uso.Catraca,
            uso.CodigoMascarado,
            string.IsNullOrWhiteSpace(uso.Categoria) ? "—" : uso.Categoria,
            string.IsNullOrWhiteSpace(uso.FalhaDaLiberacao) ? LiberouSemGiro : "Liberação falhou: " + uso.FalhaDaLiberacao);
    }
}

/// <summary>
/// Usos sem passagem e o estorno (achado E1-05 do docs/41). O ingresso consumido cuja liberação falhou
/// continua consumido; o usuário logado confere com a pessoa e estorna, com motivo, em dois passos.
/// </summary>
public sealed class PainelDeUsosSemPassagem : Notificavel
{
    private readonly EdgeControl.EdgeControlClient _cliente;
    private IReadOnlyList<LinhaSemPassagem> _linhas = [];
    private LinhaSemPassagem? _selecionada;
    private string _motivo = string.Empty;
    private string _alcance = string.Empty;
    private string _mensagem = string.Empty;
    private bool _confirmando;

    public PainelDeUsosSemPassagem(EdgeControl.EdgeControlClient cliente)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        _cliente = cliente;
        Atualizar = new ComandoAssincrono(() => AtualizarAsync());
        PrepararEstorno = new ComandoAssincrono(() =>
        {
            Confirmando = true;
            return Task.CompletedTask;
        }, () => !Confirmando && Selecionada is not null && Motivo.Trim().Length >= 3);
        ConfirmarEstorno = new ComandoAssincrono(EstornarAsync, () => Confirmando);
        CancelarEstorno = new ComandoAssincrono(() =>
        {
            Confirmando = false;
            return Task.CompletedTask;
        }, () => Confirmando);
    }

    public ComandoAssincrono Atualizar { get; }

    /// <summary>Primeiro passo: abre a confirmação.</summary>
    public ComandoAssincrono PrepararEstorno { get; }

    /// <summary>Segundo passo: estorna.</summary>
    public ComandoAssincrono ConfirmarEstorno { get; }

    public ComandoAssincrono CancelarEstorno { get; }

    public IReadOnlyList<LinhaSemPassagem> Linhas
    {
        get => _linhas;
        private set
        {
            if (Definir(ref _linhas, value))
            {
                Avisar(nameof(Titulo));
                Avisar(nameof(TemUsos));
            }
        }
    }

    /// <summary>Há algum uso sem passagem, ou um resultado a mostrar: a seção só aparece então.</summary>
    public bool TemUsos => Linhas.Count > 0 || Mensagem.Length > 0;

    /// <summary>O título da seção, com a contagem.</summary>
    public string Titulo => $"Usos sem passagem ({Linhas.Count}): consumidos sem giro confirmado";

    public LinhaSemPassagem? Selecionada
    {
        get => _selecionada;
        set
        {
            if (Definir(ref _selecionada, value))
            {
                Confirmando = false;
                Reavaliar();
            }
        }
    }

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

    /// <summary>O que o estorno faz e não faz, vindo do serviço.</summary>
    public string Alcance { get => _alcance; private set => Definir(ref _alcance, value); }

    public string Mensagem
    {
        get => _mensagem;
        private set
        {
            if (Definir(ref _mensagem, value))
            {
                Avisar(nameof(TemUsos));
            }
        }
    }

    /// <summary>A confirmação está aberta.</summary>
    public bool Confirmando
    {
        get => _confirmando;
        private set
        {
            if (Definir(ref _confirmando, value))
            {
                Avisar(nameof(TextoDaConfirmacao));
                Reavaliar();
            }
        }
    }

    public string TextoDaConfirmacao => Selecionada is { } s
        ? $"Estornar o uso de {s.Codigo} ({s.Hora}, {s.Catraca})? {Alcance}"
        : string.Empty;

    public async Task AtualizarAsync(CancellationToken cancelamento = default)
    {
        try
        {
            var resposta = await _cliente.ListarUsosSemPassagemAsync(new ListarUsosSemPassagemRequest(), cancellationToken: cancelamento);
            Alcance = resposta.AlcanceDoEstorno;
            var selecionada = Selecionada?.Id;
            Linhas = [.. resposta.Usos.Select(LinhaSemPassagem.De)];
            Selecionada = Linhas.FirstOrDefault(l => l.Id == selecionada);
        }
        catch (RpcException erro)
        {
            Mensagem = MensagemDeFalha.Para(erro);
        }
    }

    private async Task EstornarAsync()
    {
        if (Selecionada is not { } linha)
        {
            Confirmando = false;
            return;
        }

        try
        {
            var r = await _cliente.EstornarUsoAsync(new EstornarUsoRequest { TentativaId = linha.Id, Motivo = Motivo.Trim() });
            Mensagem = r.Estornado
                ? $"Uso de {linha.Codigo} estornado: o ingresso volta a valer nesta borda."
                : string.Join(" ", r.Problemas);
            if (r.Estornado)
            {
                Motivo = string.Empty;
            }
        }
        catch (RpcException erro)
        {
            Mensagem = MensagemDeFalha.Para(erro);
        }

        Confirmando = false;
        await AtualizarAsync().ConfigureAwait(true);
    }

    private void Reavaliar()
    {
        PrepararEstorno.ReavaliarDisponibilidade();
        ConfirmarEstorno.ReavaliarDisponibilidade();
        CancelarEstorno.ReavaliarDisponibilidade();
    }
}
