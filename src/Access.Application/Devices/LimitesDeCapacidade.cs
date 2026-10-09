namespace Access.Application.Devices;

/// <summary>Placa do equipamento, que determina a capacidade da lista de acesso.</summary>
/// <remarks>
/// A placa antiga é identificável fisicamente: tem <b>um único relé</b> de acionamento
/// externo, enquanto Inner Plus e Inner Net têm dois. Ver
/// docs/compatibility-matrix/limites-de-capacidade.csv.
/// </remarks>
public enum PlacaDoEquipamento
{
    /// <summary>Produção atual: Controle Catraca e linha Inner Acesso.</summary>
    ControleCatracaOuInnerAcesso,

    /// <summary>Inner Plus e Inner Net, descontinuados.</summary>
    InnerPlusOuInnerNet,

    /// <summary>Placa Inner anterior a 1999, com um único relé.</summary>
    InnerAntiga,
}

/// <summary>Resultado da avaliação de um plano de carga.</summary>
/// <param name="Cabe">Verdadeiro quando o plano cabe no equipamento.</param>
/// <param name="Maximo">Capacidade do equipamento para esta placa e tamanho de cartão.</param>
/// <param name="Planejado">O que se pretendia carregar.</param>
/// <param name="Mensagem">Texto acionável, no idioma do operador.</param>
public sealed record AvaliacaoDeCapacidade(bool Cabe, int Maximo, int Planejado, string Mensagem)
{
    /// <summary>Quanto passa da capacidade. Zero quando cabe.</summary>
    public int Excedente => Math.Max(0, Planejado - Maximo);
}

/// <summary>
/// Tetos de fábrica do equipamento, publicados pela Topdata.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque descobrir o estouro no <b>envio</b> é o pior momento: a gravação da lista
/// sobrescreve tudo e trava a catraca enquanto roda. No meio do pico de um evento, isso é
/// uma pista parada. A avaliação acontece antes, com o plano na mão.
/// </para>
/// <para>
/// Os números vêm de <c>docs/compatibility-matrix/limites-de-capacidade.csv</c>, e um teste
/// de contrato reprova o build se código e matriz divergirem.
/// </para>
/// </remarks>
public static class LimitesDeCapacidade
{
    /// <summary>Marcações que um equipamento da linha Inner Acesso guarda.</summary>
    public const int MarcacoesPorEquipamento = 30_000;

    /// <summary>Equipamentos atendidos por uma instância da DLL.</summary>
    public const int EquipamentosPorInstanciaDaDll = 30;

    /// <summary>Capacidade da produção atual, salvo cartão de 16 dígitos.</summary>
    public const int ListaNaProducaoAtual = 15_000;

    /// <summary>Capacidade da produção atual com cartão de 16 dígitos.</summary>
    public const int ListaNaProducaoAtualCom16Digitos = 14_900;

    // Placas descontinuadas: a capacidade cai conforme o cartão cresce, porque cada
    // registro ocupa mais espaço.
    private static readonly (int Digitos, int Maximo)[] InnerPlusOuNet =
    [
        (4, 15_000), (6, 11_250), (8, 9_000), (10, 7_500), (12, 6_425), (14, 5_625), (16, 5_000),
    ];

    private static readonly (int Digitos, int Maximo)[] Antiga =
    [
        (4, 3_000), (6, 1_500), (8, 2_250), (10, 1_800), (12, 1_500), (14, 1_125),
    ];

    /// <summary>Capacidade da lista de acesso para esta placa e tamanho de cartão.</summary>
    /// <param name="placa">Placa do equipamento.</param>
    /// <param name="digitos">Dígitos do cartão, de 1 a 16.</param>
    /// <returns>
    /// O máximo de usuários. Para placas descontinuadas, usa a faixa documentada
    /// imediatamente igual ou superior ao tamanho informado — nunca uma mais generosa.
    /// </returns>
    public static int MaximoDeUsuarios(PlacaDoEquipamento placa, int digitos)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digitos, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digitos, 16);

        return placa switch
        {
            PlacaDoEquipamento.ControleCatracaOuInnerAcesso =>
                digitos == 16 ? ListaNaProducaoAtualCom16Digitos : ListaNaProducaoAtual,

            PlacaDoEquipamento.InnerPlusOuInnerNet => MenorCapacidadeQueCobre(InnerPlusOuNet, digitos),

            PlacaDoEquipamento.InnerAntiga => MenorCapacidadeQueCobre(Antiga, digitos),

            _ => throw new ArgumentOutOfRangeException(nameof(placa), placa, "Placa desconhecida."),
        };
    }

    /// <summary>Avalia se um plano de carga cabe, antes de qualquer envio.</summary>
    public static AvaliacaoDeCapacidade AvaliarListaDeAcesso(
        int usuariosPlanejados,
        PlacaDoEquipamento placa,
        int digitos)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usuariosPlanejados);

        var maximo = MaximoDeUsuarios(placa, digitos);

        if (usuariosPlanejados <= maximo)
        {
            return new AvaliacaoDeCapacidade(
                true,
                maximo,
                usuariosPlanejados,
                $"Cabem {usuariosPlanejados} de {maximo} posições. " +
                $"Folga de {maximo - usuariosPlanejados}.");
        }

        return new AvaliacaoDeCapacidade(
            false,
            maximo,
            usuariosPlanejados,
            $"A lista não cabe: {usuariosPlanejados} usuários para {maximo} posições, " +
            $"excedendo em {usuariosPlanejados - maximo}. " +
            "Com lista branca, quem sobra fica sem validação local e é recusado quando a rede cai. " +
            "A alternativa é inverter para lista negra, carregando só quem NÃO pode entrar — " +
            "o que faz a catraca deixar passar desconhecidos durante a queda. " +
            "A escolha é do cliente, por escrito (pergunta B4).");
    }

    /// <summary>
    /// Projeta o uso da memória de marcações de um equipamento.
    /// </summary>
    /// <param name="passagensPorEquipamento">Passagens esperadas naquele equipamento.</param>
    /// <param name="marcacoesPorPassagem">
    /// Quantas marcações uma passagem gera em off-line. <b>Não confirmado pela Topdata</b>:
    /// a leitura, o giro e o eventual fim de tempo de acionamento são origens distintas, mas
    /// não está documentado quantas viram marcação. Planeje pelo pior caso.
    /// </param>
    public static AvaliacaoDeCapacidade AvaliarMarcacoes(
        int passagensPorEquipamento,
        int marcacoesPorPassagem)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(passagensPorEquipamento);
        ArgumentOutOfRangeException.ThrowIfLessThan(marcacoesPorPassagem, 1);

        var previstas = (long)passagensPorEquipamento * marcacoesPorPassagem;
        var cabe = previstas <= MarcacoesPorEquipamento;

        var planejado = previstas > int.MaxValue ? int.MaxValue : (int)previstas;

        return new AvaliacaoDeCapacidade(
            cabe,
            MarcacoesPorEquipamento,
            planejado,
            cabe
                ? $"{previstas} marcações previstas de {MarcacoesPorEquipamento} " +
                  $"({previstas * 100 / MarcacoesPorEquipamento}% da memória)."
                : $"A memória de marcações estoura: {previstas} previstas para " +
                  $"{MarcacoesPorEquipamento} posições. A coleta precisa rodar continuamente " +
                  "durante o evento, não no fechamento.");
    }

    private static int MenorCapacidadeQueCobre((int Digitos, int Maximo)[] tabela, int digitos)
    {
        // Sem linha para o tamanho exato, vale a faixa documentada mais próxima acima —
        // arredondar para baixo inventaria capacidade que a Topdata não publicou.
        foreach (var (d, maximo) in tabela)
        {
            if (digitos <= d)
            {
                return maximo;
            }
        }

        // Acima da maior faixa documentada: a placa antiga não publica valor para 16 dígitos.
        return 0;
    }
}
