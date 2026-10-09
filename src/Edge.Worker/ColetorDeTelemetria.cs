
namespace Edge.Worker;

/// <summary>Um evento/sinal enfileirado para descarregamento em <c>telemetria.db</c>.</summary>
public sealed record SinalDaOperacao(
    string Id,                                 // UUIDv7
    int Inner,
    TipoDeSinal Tipo,
    int? OrigemBruta = null,
    int? Complemento = null,
    string? EstadoAnterior = null,
    string? EstadoNovo = null,
    string? MotivoTransicao = null,
    int? RetornoNativo = null,
    string? Firmware = null,
    string? IdTentativaPendente = null,
    string? HoraDoEquipamento = null,
    DateTimeOffset? RecebidoEm = null);

/// <summary>Tipos de sinal gravados em <c>device_signal</c>.</summary>
public enum TipoDeSinal
{
    Origem,
    LeituraVazia,
    Transicao,
    LiberacaoRecusada,
    Conexao,
}

/// <summary>
/// Coletor mínimo: anel limitado (O(1), nunca bloqueia) que enfileira eventos sem código,
/// descarregados a cada 2 s entre voltas. Etapa I.1 dos docs/36.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe: incrementos de contadores e enfileiramento são operações atômicas.
/// Invariante I1: nenhuma chamada de disk I/O no passo da decisão, só enfileiramento.
/// Descarregamento: em <c>SessaoDeOperacao.UmaVolta</c>, depois de <c>PublicarSeFor()</c>,
/// com o mesmo try/catch de situação.
/// </para>
/// <para>
/// Sem o arquivo de telemetria, o coletor é um nulo: o worker roda como hoje, sem coleta.
/// </para>
/// </remarks>
public abstract class ColetorDeTelemetria
{
    /// <summary>Enfileira um sinal (origem, transição, etc).</summary>
    public abstract void Enfileirar(SinalDaOperacao sinal);

    /// <summary>Incrementa um contador de erro de recepção.</summary>
    public abstract void ContarErroDeRecepcao(int inner);

    /// <summary>Incrementa um contador de leitura vazia.</summary>
    public abstract void ContarLeituraVazia(int inner);

    /// <summary>Incrementa um contador de origem desconhecida.</summary>
    public abstract void ContarOrigemDesconhecida(int inner);

    /// <summary>Registra a latência da decisão (em ms).</summary>
    public abstract void RegistrarLatenciaDecisao(int inner, long milissegundos);

    /// <summary>Registra a latência de recepção de evento nativo (em ms).</summary>
    public abstract void RegistrarLatenciaRecepcao(int inner, long milissegundos);

    /// <summary>Registra a latência da volta do laço (em ms).</summary>
    public abstract void RegistrarLatenciaVolta(int inner, long milissegundos);

    /// <summary>Incrementa a contagem de voltas neste minuto.</summary>
    public abstract void ContarVoltaDoLaco(int inner);

    /// <summary>Incrementa a contagem de decisões neste minuto.</summary>
    public abstract void ContarDecisao(int inner);

    /// <summary>Registra o desvio do relógio da catraca (em segundos).</summary>
    public abstract void RegistrarDesvioDoRelogio(int inner, long segundos);

    /// <summary>Incrementa a contagem de reconexões neste minuto.</summary>
    public abstract void ContarReconexao(int inner);

    /// <summary>Incrementa a contagem de segundos em operação neste minuto.</summary>
    public abstract void RegistrarSegundoEmOperacao(int inner);

    /// <summary>
    /// Descarrega todos os eventos enfileirados para <c>telemetria.db</c> (operação O(n) fora do passo).
    /// Retorna quantos eventos foram descarregados e quantos foram descartados.
    /// </summary>
    public abstract (int Descarregados, int Descartados) Descarregar(string? sessionId, string worker);

    /// <summary>Um coletor nulo para quando não há arquivo de telemetria.</summary>
    public static readonly ColetorDeTelemetria Nulo = new ColetorNulo();

    private sealed class ColetorNulo : ColetorDeTelemetria
    {
        public override void Enfileirar(SinalDaOperacao sinal) { }
        public override void ContarErroDeRecepcao(int inner) { }
        public override void ContarLeituraVazia(int inner) { }
        public override void ContarOrigemDesconhecida(int inner) { }
        public override void RegistrarLatenciaDecisao(int inner, long milissegundos) { }
        public override void RegistrarLatenciaRecepcao(int inner, long milissegundos) { }
        public override void RegistrarLatenciaVolta(int inner, long milissegundos) { }
        public override void ContarVoltaDoLaco(int inner) { }
        public override void ContarDecisao(int inner) { }
        public override void RegistrarDesvioDoRelogio(int inner, long segundos) { }
        public override void ContarReconexao(int inner) { }
        public override void RegistrarSegundoEmOperacao(int inner) { }
        public override (int, int) Descarregar(string? sessionId, string worker) => (0, 0);
    }
}

