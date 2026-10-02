namespace Edge.Supervisor;

/// <summary>Situação de um worker vista pelo supervisor.</summary>
public enum SituacaoDoWorker
{
    /// <summary>Nunca iniciado.</summary>
    Parado,

    /// <summary>Vivo e batendo.</summary>
    Saudavel,

    /// <summary>Vivo, mas sem batimento: o laço travou.</summary>
    SemBatimento,

    /// <summary>Morreu.</summary>
    Morto,

    /// <summary>Reiniciou demais em pouco tempo; parou de ser reiniciado.</summary>
    Quarentena,
}

/// <summary>
/// Um worker sob supervisão.
/// </summary>
/// <remarks>
/// Abstrai se o worker é um processo separado ou roda no mesmo processo. Em produção é
/// um processo x86 próprio, com sua porta TCP (ADR-0021); nos testes, um dublê. O
/// supervisor não precisa saber a diferença.
/// </remarks>
public interface IWorkerHost : IDisposable
{
    /// <summary>Nome do grupo, normalmente ligado à área física atendida.</summary>
    string Nome { get; }

    /// <summary>Porta TCP em que este worker escuta. Uma por worker.</summary>
    int Porta { get; }

    /// <summary>Equipamentos atendidos por este worker.</summary>
    IReadOnlyList<int> Inners { get; }

    /// <summary>Verdadeiro enquanto o processo está de pé.</summary>
    bool EstaVivo { get; }

    /// <summary>Verdadeiro enquanto o laço deu sinal de vida dentro da tolerância.</summary>
    bool EstaSaudavel { get; }

    /// <summary>Diagnóstico legível, para log e para o pacote de suporte.</summary>
    string Diagnostico { get; }

    /// <summary>Sobe o worker.</summary>
    void Iniciar();

    /// <summary>
    /// Mata o worker sem cerimônia.
    /// </summary>
    /// <remarks>
    /// Encerramento gentil não funciona com a DLL travada: a thread não volta para
    /// atender pedido de parada. Matar é o único caminho confiável.
    /// </remarks>
    void Matar();
}
