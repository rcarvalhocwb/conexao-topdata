namespace Access.Inteligencia;

/// <summary>
/// A saúde do próprio Analisador, como o Diagnóstico mostra (docs/36-anexos/02 §3.7, linha
/// "Diagnóstico": "a saúde do próprio Analisador").
/// </summary>
/// <remarks>
/// Imutável: o laço do Analisador troca a instância inteira a cada ciclo e o serviço lê a última
/// que existir, sem trava. Nenhum campo carrega código de ingresso, nome ou mensagem livre de
/// exceção — o erro vai só pelo tipo, que é o que o suporte precisa e não tem como conter dado
/// (invariante I6, §3.1).
/// </remarks>
/// <param name="Ligada">A chave <see cref="ChavesDaInteligencia.Ligada"/> estava ligada na última conferência.</param>
/// <param name="Rodando">O laço do Analisador existe neste serviço (subiu e não terminou).</param>
/// <param name="UltimoCicloEm">Quando terminou o último ciclo feito; nulo se nenhum.</param>
/// <param name="DuracaoDoUltimoCiclo">Quanto ele durou.</param>
/// <param name="Ciclos">Ciclos que terminaram bem desde a partida.</param>
/// <param name="Estouros">Ciclos que passaram do orçamento (o seguinte foi pulado).</param>
/// <param name="Pulados">Ciclos pulados por causa de um estouro.</param>
/// <param name="Falhas">Ciclos que terminaram em erro.</param>
/// <param name="UltimoErro">O tipo do último erro; vazio se nenhum.</param>
/// <param name="UltimoErroEm">Quando foi.</param>
/// <param name="TentativasLidas">Tentativas novas lidas da base desde a partida.</param>
/// <param name="Orcamento">O orçamento de cada ciclo.</param>
public sealed record SituacaoDoAnalisador(
    bool Ligada,
    bool Rodando,
    DateTimeOffset? UltimoCicloEm,
    TimeSpan DuracaoDoUltimoCiclo,
    long Ciclos,
    long Estouros,
    long Pulados,
    long Falhas,
    string UltimoErro,
    DateTimeOffset? UltimoErroEm,
    long TentativasLidas,
    TimeSpan Orcamento)
{
    /// <summary>Antes de o laço subir: desligada, parada, nada feito.</summary>
    public static SituacaoDoAnalisador Inicial(OrcamentoDoCiclo orcamento)
    {
        ArgumentNullException.ThrowIfNull(orcamento);
        return new(false, false, null, TimeSpan.Zero, 0, 0, 0, 0, string.Empty, null, 0, orcamento.Limite);
    }

    /// <summary>Um ciclo terminou bem.</summary>
    public SituacaoDoAnalisador ComCiclo(DateTimeOffset em, TimeSpan duracao, long tentativasNovas, bool estourou) =>
        this with
        {
            UltimoCicloEm = em,
            DuracaoDoUltimoCiclo = duracao,
            Ciclos = Ciclos + 1,
            Estouros = Estouros + (estourou ? 1 : 0),
            TentativasLidas = TentativasLidas + tentativasNovas,
        };

    /// <summary>Um ciclo terminou em erro. Só o tipo do erro é guardado, nunca a mensagem.</summary>
    public SituacaoDoAnalisador ComFalha(DateTimeOffset em, TimeSpan duracao, Exception erro, bool estourou)
    {
        ArgumentNullException.ThrowIfNull(erro);
        return this with
        {
            UltimoCicloEm = em,
            DuracaoDoUltimoCiclo = duracao,
            Falhas = Falhas + 1,
            Estouros = Estouros + (estourou ? 1 : 0),
            UltimoErro = erro.GetType().Name,
            UltimoErroEm = em,
        };
    }
}
