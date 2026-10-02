using Access.Domain.Devices;

namespace Access.Application.Devices;

/// <summary>De onde veio o pedido que libera o giro.</summary>
/// <remarks>
/// <para>
/// Mapa de giro (docs/34 §9, decisão D9 "nomenclatura de sentido é do sistema"). Cada origem
/// que libera a catraca tem a sua regra: qual função da DLL chamar e como o giro é contado.
/// </para>
/// <para>
/// O leitor 1 cobre a frente e o QR da tampa: as origens 2 (ORIGEM_LEITOR1) e 21
/// (ORIGEM_QRCODE) de <c>origens-evento.csv</c> chegam pelo mesmo lado da catraca. O leitor 2
/// é a fenda da urna (origem 3). O teclado é a origem 1. A liberação manual é o comando do
/// operador (fase 4b), sem leitura.
/// </para>
/// </remarks>
public enum OrigemDoGiro
{
    /// <summary>Leitor 1: cartão na frente (origem 2) ou QR da tampa (origem 21).</summary>
    Leitor1,

    /// <summary>Leitor 2, a fenda da urna (origem 3).</summary>
    Leitor2,

    /// <summary>Teclado (origem 1). Só faz sentido com o teclado habilitado na catraca.</summary>
    Teclado,

    /// <summary>Liberação manual pedida pelo operador no painel.</summary>
    LiberacaoManual,
}

/// <summary>Como o giro liberado é contado: o rótulo lógico, que é decisão do sistema.</summary>
/// <remarks>
/// "Entrada" e "saída" na DLL são o nome do sentido <b>físico</b> do braço, do ponto de vista da
/// catraca (EI-041 a EI-044). O que conta como entrada ou saída no evento depende de como a
/// catraca foi instalada, e é o sistema quem decide (D9, docs/34 §9). Este rótulo vai para o
/// painel, os acessos, os totais e a prestação de contas.
/// </remarks>
public enum SentidoContado
{
    /// <summary>Conta como entrada. O de sempre.</summary>
    Entrada,

    /// <summary>Conta como saída.</summary>
    Saida,
}

/// <summary>A regra de uma origem do mapa de giro.</summary>
/// <param name="Funcao">
/// A função da DLL que libera o giro desta origem (EI-041 a EI-044). Nula = a função do perfil
/// da catraca (<see cref="GatePhysicalProfile.FuncaoDeLiberacaoDaEntrada"/>, camada da A.3). Os
/// dois sentidos (EI-045) não entram aqui: continuam restritos à evacuação (D5).
/// </param>
/// <param name="ContaComo">Como o giro desta origem é contado.</param>
/// <param name="Texto">
/// O texto curto do giro ("Entrada liberada", "Saida liberada" ou personalizado), até
/// <see cref="MapaDeGiro.LimiteDoTexto"/> caracteres. Nulo = o texto padrão do rótulo.
/// </param>
public sealed record RegraDeGiro(FuncaoDeLiberacao? Funcao, SentidoContado ContaComo, string? Texto = null);

/// <summary>O que vale para uma origem: a regra do mapa, ou o padrão de hoje.</summary>
/// <param name="Origem">A origem.</param>
/// <param name="Funcao">A função da DLL que será chamada.</param>
/// <param name="ContaComo">Como o giro é contado.</param>
/// <param name="Texto">O texto curto do giro.</param>
/// <param name="DoMapa">Verdadeiro quando há regra gravada para esta origem; falso = padrão de hoje.</param>
public sealed record GiroResolvido(OrigemDoGiro Origem, FuncaoDeLiberacao Funcao, SentidoContado ContaComo, string Texto, bool DoMapa)
{
    /// <summary>O pedido ao adapter: um para um com a função (sem combinar nada, F1).</summary>
    public GateDirection Direcao => GatePhysicalProfile.Direcao(Funcao);
}

/// <summary>
/// O mapa de giro de uma catraca: para cada origem que libera, qual sentido físico liberar e
/// como contar o giro.
/// </summary>
/// <remarks>
/// <para>
/// Decisão D9 do dono do produto (docs/34 §9): "nomenclatura de sentido é do sistema". A DLL
/// libera por sentido físico, do ponto de vista da catraca; o que o evento chama de entrada ou
/// saída é nosso. O mapa existe para que "o cartão na urna gira para a esquerda e conta como
/// entrada" seja só configuração, sem esperar a Topdata.
/// </para>
/// <para>
/// <b>Vazio = comportamento de hoje:</b> toda origem chama a função do perfil
/// (<c>LiberarCatracaEntrada</c>, EI-041, no padrão) e conta como entrada. Os testes
/// congelados da A.1, A.2 e A.7 passam sem mudança.
/// </para>
/// <para>
/// O que continua do mundo físico — para que lado o braço gira com cada função <b>nesta</b>
/// instalação — é a conferência de comissionamento (docs/21, NOVO-HIL-DIR-11): o mapa vale
/// desde que é aplicado, e a catraca mostra o selo "sentido ainda não conferido nesta
/// instalação" até alguém girar uma vez e registrar.
/// </para>
/// <para>
/// Não é parâmetro do buffer de configuração: decide qual função de liberação o laço chama a
/// cada giro autorizado, como o <see cref="GatePhysicalProfile"/> de que faz parte.
/// </para>
/// </remarks>
public sealed record MapaDeGiro
{
    /// <summary>Tamanho do display da TopFit 4: duas linhas de 16 (FUN:57).</summary>
    public const int LimiteDoTexto = 32;

    /// <summary>O mapa sem regra nenhuma: tudo como hoje.</summary>
    public static MapaDeGiro Vazio { get; } = new();

    /// <summary>Todas as origens, na ordem da tela.</summary>
    public static IReadOnlyList<OrigemDoGiro> Origens { get; } =
        [OrigemDoGiro.Leitor1, OrigemDoGiro.Leitor2, OrigemDoGiro.Teclado, OrigemDoGiro.LiberacaoManual];

    /// <summary>Leitor 1 (frente e QR). Nulo = padrão.</summary>
    public RegraDeGiro? Leitor1 { get; init; }

    /// <summary>Leitor 2, a fenda da urna. Nulo = padrão.</summary>
    public RegraDeGiro? Leitor2 { get; init; }

    /// <summary>Teclado. Nulo = padrão.</summary>
    public RegraDeGiro? Teclado { get; init; }

    /// <summary>Liberação manual do painel. Nulo = padrão.</summary>
    public RegraDeGiro? LiberacaoManual { get; init; }

    /// <summary>Nenhuma origem com regra.</summary>
    public bool EstaVazio => Leitor1 is null && Leitor2 is null && Teclado is null && LiberacaoManual is null;

    /// <summary>A regra gravada para a origem; nula = padrão.</summary>
    public RegraDeGiro? Regra(OrigemDoGiro origem) => origem switch
    {
        OrigemDoGiro.Leitor1 => Leitor1,
        OrigemDoGiro.Leitor2 => Leitor2,
        OrigemDoGiro.Teclado => Teclado,
        OrigemDoGiro.LiberacaoManual => LiberacaoManual,
        _ => null,
    };

    /// <summary>O mesmo mapa com a regra da origem trocada (nula = volta ao padrão).</summary>
    public MapaDeGiro Com(OrigemDoGiro origem, RegraDeGiro? regra) => origem switch
    {
        OrigemDoGiro.Leitor1 => this with { Leitor1 = regra },
        OrigemDoGiro.Leitor2 => this with { Leitor2 = regra },
        OrigemDoGiro.Teclado => this with { Teclado = regra },
        OrigemDoGiro.LiberacaoManual => this with { LiberacaoManual = regra },
        _ => throw new ArgumentOutOfRangeException(nameof(origem), origem, "Origem do giro desconhecida."),
    };

    /// <summary>
    /// A origem do mapa para uma leitura da catraca; nula quando a origem não é leitura
    /// (giro, sensor, urna cheia, tecla de função, desconhecida).
    /// </summary>
    public static OrigemDoGiro? DaLeitura(EventOrigin origem) => origem.Known switch
    {
        KnownEventOrigin.Leitor1 or KnownEventOrigin.QrCode => OrigemDoGiro.Leitor1,
        KnownEventOrigin.Leitor2 => OrigemDoGiro.Leitor2,
        KnownEventOrigin.Teclado => OrigemDoGiro.Teclado,
        _ => null,
    };

    /// <summary>
    /// O texto padrão do rótulo, sem acento: o display pode não ter (como "Acesso nao autorizado").
    /// </summary>
    public static string TextoPadrao(SentidoContado contaComo) =>
        contaComo is SentidoContado.Saida ? "Saida liberada" : "Entrada liberada";

    /// <summary>Problemas que impedem usar o mapa. Vazia quando serve.</summary>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        foreach (var origem in Origens)
        {
            if (Regra(origem) is not { } regra)
            {
                continue;
            }

            var nome = Nome(origem);

            if (regra.Funcao is { } funcao && !Enum.IsDefined(funcao))
            {
                problemas.Add(
                    $"Mapa de giro, {nome}: a função deve ser Entrada, EntradaInvertida, Saida ou SaidaInvertida; " +
                    $"recebido {(int)funcao}. Os dois sentidos ficam só para a evacuação (D5).");
            }

            if (!Enum.IsDefined(regra.ContaComo))
            {
                problemas.Add($"Mapa de giro, {nome}: conta como Entrada ou Saída; recebido {(int)regra.ContaComo}.");
            }

            if (regra.Texto is { } texto
                && (string.IsNullOrWhiteSpace(texto) || texto.Length > LimiteDoTexto || texto.Any(char.IsControl)))
            {
                problemas.Add(
                    $"Mapa de giro, {nome}: o texto do giro tem de 1 a {LimiteDoTexto} caracteres, sem quebra de linha.");
            }
        }

        return problemas;
    }

    /// <summary>Nome da origem em português de operador.</summary>
    public static string Nome(OrigemDoGiro origem) => origem switch
    {
        OrigemDoGiro.Leitor1 => "leitor 1 (frente e QR)",
        OrigemDoGiro.Leitor2 => "leitor 2 (urna)",
        OrigemDoGiro.Teclado => "teclado",
        OrigemDoGiro.LiberacaoManual => "liberação manual do painel",
        _ => "origem desconhecida",
    };
}
