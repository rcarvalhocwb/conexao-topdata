namespace Sync.Core;

/// <summary>
/// Classes de prioridade da outbox, de 0 (mais urgente) a 9 (menos urgente).
/// </summary>
/// <remarks>
/// <para>
/// A prioridade não é enfeite: depois de oito horas sem internet, a fila pode ter
/// centenas de milhares de linhas. Quando a conexão volta, o que precisa subir primeiro
/// é a revogação de um cartão roubado — não o histórico de passagem da manhã inteira.
/// Drenar por ordem de chegada faria a informação mais importante esperar a menos
/// importante terminar.
/// </para>
/// <para>
/// Os números são estáveis: entram em relatório e em integração de cliente. Inserir uma
/// classe nova entre duas existentes quebra comparações históricas, então o espaço foi
/// dimensionado com folga desde o início.
/// </para>
/// </remarks>
public static class PrioridadeDeSincronizacao
{
    /// <summary>Pânico, evacuação, liberação geral. Precede qualquer outra coisa.</summary>
    public const int BloqueioEmergencial = 0;

    /// <summary>Cartão perdido, roubado, demissão, morador que saiu. Segurança imediata.</summary>
    public const int RevogacaoDeCredencial = 1;

    /// <summary>Urna cheia, equipamento fora do ar, porta arrombada. Alguém precisa ir lá.</summary>
    public const int Alarme = 2;

    /// <summary>Giro confirmado. É o que alimenta contagem de lotação e painel ao vivo.</summary>
    public const int PassagemFisica = 3;

    /// <summary>Autorização e negativa. O histórico de quem tentou entrar.</summary>
    public const int DecisaoDeAcesso = 4;

    /// <summary>Entrega, recolhimento e reemissão de cartão. Cadeia de custódia.</summary>
    public const int MovimentoDeCredencial = 5;

    /// <summary>Inventário de equipamento, versão de firmware, mudança de configuração.</summary>
    public const int InventarioEConfiguracao = 6;

    /// <summary>Trilha de auditoria. Importa muito, urge pouco.</summary>
    public const int Auditoria = 7;

    /// <summary>Latência, contadores, saúde. Útil agregado, inútil atrasado.</summary>
    public const int Metrica = 8;

    /// <summary>Tudo que só existe para consulta posterior.</summary>
    public const int Historico = 9;

    /// <summary>Menor valor válido.</summary>
    public const int Minima = BloqueioEmergencial;

    /// <summary>Maior valor válido.</summary>
    public const int Maxima = Historico;

    /// <summary>Rejeita prioridade fora da faixa, no lugar de deixar a fila ordenar errado.</summary>
    /// <param name="prioridade">Valor a validar.</param>
    /// <param name="nomeDoParametro">Preenchido pelo compilador.</param>
    public static void Exigir(int prioridade, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(prioridade))] string? nomeDoParametro = null)
    {
        if (prioridade is < Minima or > Maxima)
        {
            throw new ArgumentOutOfRangeException(
                nomeDoParametro,
                prioridade,
                $"Prioridade de sincronização vai de {Minima} a {Maxima}. Ver docs/15-integracao-e-sincronizacao.md");
        }
    }
}
