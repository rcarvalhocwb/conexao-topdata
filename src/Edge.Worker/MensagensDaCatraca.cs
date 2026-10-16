using Access.Domain.Access;

namespace Edge.Worker;

/// <summary>
/// O que o display da catraca mostra ao negar: 32 caracteres, sem acento (a fonte do display não tem).
/// </summary>
/// <remarks>
/// Antes toda negação dizia "Acesso nao autorizado", e quem estava na frente da catraca não sabia o
/// que fazer. Agora os motivos que a pessoa resolve sozinha dizem o que fazer (docs/61).
/// </remarks>
public static class MensagensDaCatraca
{
    /// <summary>A negação genérica, a de sempre.</summary>
    public const string AcessoNaoAutorizado = "Acesso nao autorizado";

    /// <summary>Antes de abrir a urna, como o exemplo oficial ("DEPOSITE O CARTAO").</summary>
    public const string DepositeOCartao = "Deposite o cartao";

    /// <summary>A urna abriu e o cartão não caiu (origem 5 ou prazo).</summary>
    public const string CartaoNaoRecolhido = "Cartao nao caiu. Tente de novo";

    /// <summary>A catraca avisou urna cheia (origem 20).</summary>
    public const string UrnaCheia = "Urna cheia. Procure a equipe";

    /// <summary>O texto da negação para o motivo da decisão.</summary>
    /// <param name="motivo">O motivo da decisão.</param>
    public static string ParaNegacao(ReasonCode motivo)
    {
        if (motivo == ReasonCodes.ForaDaUrna)
        {
            return "Use a urna";
        }

        if (motivo == ReasonCodes.SomenteEquipe)
        {
            return "Leitor so para equipe";
        }

        if (motivo == ReasonCodes.CredencialNaUrna)
        {
            return "Use o leitor da frente";
        }

        if (motivo == ReasonCodes.EntradaDesligada)
        {
            return "Leitor desligado";
        }

        return AcessoNaoAutorizado;
    }
}
