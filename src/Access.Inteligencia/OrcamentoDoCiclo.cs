namespace Access.Inteligencia;

/// <summary>
/// O orçamento de um ciclo do Analisador: quanto ele pode durar e o que acontece quando passa.
/// </summary>
/// <remarks>
/// <para>
/// A camada roda no mesmo PC que decide o giro. Um ciclo que demora não pode virar uma fila de
/// ciclos atrasados disputando CPU com o serviço: se um ciclo estourar o orçamento, o seguinte é
/// <b>pulado</b> e o estouro é contado no Diagnóstico (docs/36-anexos/02 §3.4, "Orçamento").
/// </para>
/// <para>
/// Função pura: a duração vem de quem mediu; aqui só se decide. É o que permite provar a regra sem
/// relógio de verdade.
/// </para>
/// </remarks>
/// <param name="Limite">A duração máxima de um ciclo (p95 ≤ 50 ms no ciclo de 1 s, §3.4).</param>
public sealed record OrcamentoDoCiclo(TimeSpan Limite)
{
    /// <summary>O orçamento do ciclo curto (1 s), do §3.4.</summary>
    public static OrcamentoDoCiclo CicloCurto { get; } = new(TimeSpan.FromMilliseconds(50));

    /// <summary>Verdadeiro quando o ciclo que durou <paramref name="duracao"/> passou do limite.</summary>
    public bool Estourou(TimeSpan duracao) => duracao > Limite;
}
