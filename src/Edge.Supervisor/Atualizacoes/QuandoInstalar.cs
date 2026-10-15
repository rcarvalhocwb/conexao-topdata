namespace Edge.Supervisor.Atualizacoes;

/// <summary>Por que a atualização vai ser instalada agora.</summary>
public enum MotivoDaInstalacao
{
    /// <summary>O operador finalizou o painel e as catracas estão paradas.</summary>
    SistemaFinalizado,

    /// <summary>Madrugada, com as catracas paradas há tempo.</summary>
    Madrugada,
}

/// <summary>
/// Quando a atualização pode ser instalada (docs/60 §2.3, decisão do dono em 15/10/2026): ao finalizar o
/// sistema ou de madrugada, sempre com as catracas paradas. Instalar para as catracas por 1 a 2 minutos.
/// </summary>
public static class QuandoInstalar
{
    /// <summary>Sem leitura há tanto tempo, finalizar o painel instala na hora.</summary>
    public static readonly TimeSpan OciosaAoFinalizar = TimeSpan.FromMinutes(5);

    /// <summary>Sem leitura há tanto tempo, a madrugada instala.</summary>
    public static readonly TimeSpan OciosaDeMadrugada = TimeSpan.FromMinutes(30);

    /// <summary>O aviso de "finalizado" vale por tanto tempo (o operador pode reabrir o painel).</summary>
    public static readonly TimeSpan ValidadeDoFinalizado = TimeSpan.FromHours(1);

    /// <summary>A janela da madrugada, no horário de Brasília: das 03:00 às 05:00.</summary>
    public static readonly TimeOnly InicioDaMadrugada = new(3, 0);

    /// <summary>Fim da janela da madrugada (exclusivo).</summary>
    public static readonly TimeOnly FimDaMadrugada = new(5, 0);

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    /// <summary>Decide se instala agora, e por quê; nulo se ainda não.</summary>
    /// <param name="agora">Agora.</param>
    /// <param name="ultimaLeitura">A última leitura de qualquer catraca; nulo se nunca houve.</param>
    /// <param name="painelFinalizadoEm">Quando o operador finalizou o painel; nulo se não finalizou.</param>
    /// <param name="licencaBloqueada">Licença vencida além da tolerância: atualização não instala.</param>
    public static MotivoDaInstalacao? Decidir(
        DateTimeOffset agora,
        DateTimeOffset? ultimaLeitura,
        DateTimeOffset? painelFinalizadoEm,
        bool licencaBloqueada)
    {
        if (licencaBloqueada)
        {
            return null;
        }

        var parada = ultimaLeitura is { } leitura ? agora - leitura : TimeSpan.MaxValue;

        if (painelFinalizadoEm is { } finalizado && agora - finalizado <= ValidadeDoFinalizado && agora >= finalizado
            && parada >= OciosaAoFinalizar)
        {
            return MotivoDaInstalacao.SistemaFinalizado;
        }

        var hora = TimeOnly.FromDateTime(agora.ToOffset(Brasilia).DateTime);
        return hora >= InicioDaMadrugada && hora < FimDaMadrugada && parada >= OciosaDeMadrugada
            ? MotivoDaInstalacao.Madrugada
            : null;
    }
}
