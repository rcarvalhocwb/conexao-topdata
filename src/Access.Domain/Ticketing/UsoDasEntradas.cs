namespace Access.Domain.Ticketing;

/// <summary>
/// Para que serve cada entrada da catraca (docs/61): o leitor da frente, a fenda da urna e o teclado.
/// </summary>
/// <remarks>
/// <para>
/// Pedido do dono do produto na bancada de 15/10/2026: "o leitor da frente só para cartões administrativos
/// ou de funcionários; a urna para os cartões do evento; o teclado desligado ou só para uma situação
/// específica". Os valores são texto porque vão para <c>edge_setting</c> e para o contrato do painel.
/// </para>
/// <para>
/// A urna não tem opção: ela <b>recolhe</b> o cartão, então só aceita cartão do evento. Um crachá de
/// pessoa na fenda é recusado (<see cref="MotivoDoUso.CredencialNaUrna"/>) para a urna não engoli-lo.
/// </para>
/// </remarks>
public static class UsoDasEntradas
{
    /// <summary>Leitor da frente: equipe e ingressos que não são só da urna (o padrão, como sempre foi).</summary>
    public const string EquipeEIngressos = "equipe_e_ingressos";

    /// <summary>Leitor da frente ou teclado: só a equipe (cadastro de pessoas).</summary>
    public const string SoEquipe = "equipe";

    /// <summary>Leitor da frente desligado.</summary>
    public const string Desligada = "desligada";

    /// <summary>Teclado desligado (o padrão).</summary>
    public const string TecladoDesligado = "desligado";

    /// <summary>Os usos aceitos para o leitor da frente.</summary>
    public static IReadOnlyList<string> DaFrente { get; } = [EquipeEIngressos, SoEquipe, Desligada];

    /// <summary>Os usos aceitos para o teclado.</summary>
    public static IReadOnlyList<string> DoTeclado { get; } = [TecladoDesligado, SoEquipe];
}
