namespace Access.Application.Devices;

/// <summary>O que o operador pode pedir a uma catraca pelo painel (fase 4b).</summary>
/// <remarks>
/// Só entra aqui o que usa função documentada e já exercitada pelo laço. Bip, relés
/// avulsos, urna e troca de sentido ficam de fora até a bancada (docs/21, docs/32).
/// </remarks>
public enum TipoDeComando
{
    /// <summary>Acerta o relógio agora (<c>EnviarRelogio</c>, EI-008).</summary>
    AcertarRelogio,

    /// <summary>Mensagem temporária no display (<c>EnviarMensagemTemporariaOnLine</c>, EI-057).</summary>
    MensagemTemporaria,

    /// <summary>Libera um giro no sentido de entrada, sem ingresso. Exige motivo.</summary>
    LiberacaoManual,

    /// <summary>Derruba e refaz a conexão, com a configuração completa de novo.</summary>
    ReiniciarConexao,

    /// <summary>Relê a configuração do evento e reconecta para enviá-la (ADR-0020).</summary>
    AplicarConfiguracao,
}

/// <summary>Em que pé está um comando. A tabela guarda o texto em minúsculas.</summary>
public enum SituacaoDoComando
{
    /// <summary>Gravado, esperando o worker da catraca.</summary>
    Pendente,

    /// <summary>O worker pegou e vai executar quando a catraca estiver livre.</summary>
    Recebido,

    /// <summary>Executado.</summary>
    Concluido,

    /// <summary>A catraca recusou ou a comunicação falhou.</summary>
    Falhou,

    /// <summary>Não foi executado a tempo — a catraca ocupada, fora do ar, ou o worker parado.</summary>
    Expirado,
}

/// <summary>Um pedido do operador para uma catraca. Imutável: vira auditoria.</summary>
/// <param name="Id">Identificador (UUID v7).</param>
/// <param name="Inner">Número da catraca.</param>
/// <param name="Tipo">O que fazer.</param>
/// <param name="Texto">A mensagem, em <see cref="TipoDeComando.MensagemTemporaria"/>.</param>
/// <param name="DuracaoSegundos">Quanto a mensagem fica no display.</param>
/// <param name="Motivo">Por que, em <see cref="TipoDeComando.LiberacaoManual"/>.</param>
/// <param name="Operador">
/// Nome informado por quem pediu. Não há login (docs/27 §11): é o que a pessoa digitou,
/// não uma identidade verificada.
/// </param>
/// <param name="PedidoEm">Quando foi pedido.</param>
/// <param name="ExpiraEm">Depois disto, não executa mais.</param>
public sealed record ComandoDeCatraca(
    Guid Id,
    int Inner,
    TipoDeComando Tipo,
    string? Texto,
    int DuracaoSegundos,
    string? Motivo,
    string Operador,
    DateTimeOffset PedidoEm,
    DateTimeOffset ExpiraEm)
{
    /// <summary>Tamanho do display (manual 4.6.3).</summary>
    public const int LimiteDaMensagem = 32;

    /// <summary>
    /// Validade de cada tipo. A liberação manual é curta de propósito: se a catraca ficou
    /// ocupada, quem pediu provavelmente já foi embora, e liberar depois solta o giro para
    /// qualquer um.
    /// </summary>
    public static TimeSpan ValidadePara(TipoDeComando tipo) =>
        tipo is TipoDeComando.LiberacaoManual ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(60);

    /// <summary>Monta um pedido válido, ou devolve os problemas.</summary>
    public static (ComandoDeCatraca? Comando, IReadOnlyList<string> Problemas) Criar(
        int inner,
        TipoDeComando tipo,
        string? operador,
        DateTimeOffset agora,
        string? texto = null,
        int duracaoSegundos = 10,
        string? motivo = null)
    {
        var problemas = new List<string>();
        var quem = operador?.Trim() ?? string.Empty;
        var porque = motivo?.Trim();
        var mensagem = texto?.Trim();

        if (inner is < 1 or > 99)
        {
            problemas.Add("A catraca vai de 1 a 99.");
        }

        if (!Enum.IsDefined(tipo))
        {
            problemas.Add("Comando desconhecido.");
        }

        if (quem.Length is < 2 or > 80)
        {
            problemas.Add("Informe o nome de quem está pedindo (2 a 80 caracteres).");
        }

        if (tipo is TipoDeComando.MensagemTemporaria)
        {
            if (string.IsNullOrEmpty(mensagem) || mensagem.Length > LimiteDaMensagem)
            {
                problemas.Add($"A mensagem precisa ter de 1 a {LimiteDaMensagem} caracteres.");
            }

            if (duracaoSegundos is < 1 or > 60)
            {
                problemas.Add("A mensagem fica de 1 a 60 segundos no display.");
            }
        }

        if (tipo is TipoDeComando.LiberacaoManual && (porque is null || porque.Length is < 5 or > 200))
        {
            problemas.Add("A liberação manual exige o motivo (5 a 200 caracteres).");
        }

        if (problemas.Count > 0)
        {
            return (null, problemas);
        }

        return (new ComandoDeCatraca(
            Guid.CreateVersion7(agora),
            inner,
            tipo,
            tipo is TipoDeComando.MensagemTemporaria ? mensagem : null,
            tipo is TipoDeComando.MensagemTemporaria ? duracaoSegundos : 0,
            tipo is TipoDeComando.LiberacaoManual ? porque : null,
            quem,
            agora,
            agora + ValidadePara(tipo)), []);
    }
}

/// <summary>
/// A fila de comandos, vista pelo worker. A implementação é a base local (ADR-0024): o
/// serviço grava, o worker lê, executa e responde.
/// </summary>
public interface IFilaDeComandos
{
    /// <summary>Pedidos ainda não pegos por nenhum worker, destas catracas, em ordem.</summary>
    IReadOnlyList<ComandoDeCatraca> Pendentes(IReadOnlyCollection<int> inners);

    /// <summary>Marca como pego. Falso se outro já pegou, ou se expirou antes.</summary>
    bool Receber(Guid id, DateTimeOffset agora);

    /// <summary>Registra o desfecho.</summary>
    void Concluir(Guid id, SituacaoDoComando situacao, string resultado, DateTimeOffset agora);
}
