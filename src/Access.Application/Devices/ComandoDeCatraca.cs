namespace Access.Application.Devices;

/// <summary>O que o operador pode pedir a uma catraca pelo painel (fase 4b e Etapa A.8).</summary>
/// <remarks>
/// <para>
/// Os cinco primeiros usam função documentada e já exercitada pelo laço. Os da Etapa A.8 do
/// docs/35 (bip, liberar saída, liberar nos dois sentidos) usam função com linha na matriz e
/// assinatura do SDK, mas cada um tem a sua chave técnica em <c>edge_setting</c>, desligada:
/// com ela desligada, o serviço recusa antes de enfileirar (<see cref="ComandoDeCatraca.RecusasDoServico"/>).
/// Relés avulsos, urna e troca de sentido continuam de fora (docs/21, docs/32).
/// </para>
/// <para>
/// Os valores novos entram no fim: a tabela <c>operator_command</c> guarda o nome, e o proto
/// numera a partir do próximo livre, sem reaproveitar.
/// </para>
/// <para>
/// Entrar e sair de manutenção <b>não</b> está aqui: a matriz não tem função da DLL para isso
/// (o estado <c>Manutencao</c> existe só na máquina do worker), e o que a catraca faz quando o
/// worker para de atendê-la é <c>A_CONFIRMAR_COM_TOPDATA</c> (docs/34 §11, Etapa A.8).
/// </para>
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

    /// <summary>
    /// Bip curto (<c>AcionarBipCurto</c>, EI-048). Manual: nunca acoplado à decisão. Chave
    /// <c>comando.bip_curto</c>, desligada até INT-UX-03.
    /// </summary>
    BipCurto,

    /// <summary>
    /// Bip longo (<c>AcionarBipLongo</c>, EI-049). Manual: nunca acoplado à decisão. Chave
    /// <c>comando.bip_longo</c>, desligada até INT-UX-03.
    /// </summary>
    BipLongo,

    /// <summary>
    /// Libera um giro no sentido de saída, sem ingresso (<see cref="GatePhysicalProfile.LiberacaoDaSaida"/>,
    /// EI-042 ou EI-044 conforme o perfil). Exige motivo. Chave <c>comando.liberar_saida</c>,
    /// desligada até HIL-DIR-04.
    /// </summary>
    LiberarSaida,

    /// <summary>
    /// Libera nos dois sentidos (<c>LiberarCatracaDoisSentidos</c>, EI-045). Só evacuação:
    /// permite carona. Exige motivo e confirmação digitada. Chave
    /// <c>comando.liberar_dois_sentidos</c>, desligada, e recusado de todo jeito enquanto a
    /// decisão D5 do dono do produto não for tomada (docs/34 §9).
    /// </summary>
    LiberarDoisSentidos,
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
/// <param name="Motivo">
/// Por que, em <see cref="TipoDeComando.LiberacaoManual"/>, <see cref="TipoDeComando.LiberarSaida"/>
/// e <see cref="TipoDeComando.LiberarDoisSentidos"/>.
/// </param>
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
    /// O texto que o operador digita para confirmar a liberação nos dois sentidos, com o número
    /// da catraca: confirma a ação e o alvo, sem inventar segunda pessoa (não há login; anexo 03
    /// do docs/34, "confirmação digitada").
    /// </summary>
    public static string ConfirmacaoDosDoisSentidos(int inner) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"EVACUAR {inner}");

    /// <summary>
    /// Validade de cada tipo. As liberações são curtas de propósito: se a catraca ficou
    /// ocupada, quem pediu provavelmente já foi embora, e liberar depois solta o giro para
    /// qualquer um.
    /// </summary>
    public static TimeSpan ValidadePara(TipoDeComando tipo) =>
        EhLiberacao(tipo) ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(60);

    /// <summary>Libera o giro: entrada manual, saída ou dois sentidos. Todos exigem motivo.</summary>
    public static bool EhLiberacao(TipoDeComando tipo) =>
        tipo is TipoDeComando.LiberacaoManual or TipoDeComando.LiberarSaida or TipoDeComando.LiberarDoisSentidos;

    /// <summary>
    /// A chave técnica em <c>edge_setting</c> que libera o tipo, ou <c>null</c> para os da fase
    /// 4b, que não têm chave. Ligada só com o valor <c>1</c>; ausente ou outro valor = desligada.
    /// </summary>
    /// <remarks>Uma chave por comando (Etapa A.8 do docs/35); o ensaio que liga cada uma está em <see cref="EnsaioQueLiga"/>.</remarks>
    public static string? ChaveTecnica(TipoDeComando tipo) => tipo switch
    {
        TipoDeComando.BipCurto => "comando.bip_curto",
        TipoDeComando.BipLongo => "comando.bip_longo",
        TipoDeComando.LiberarSaida => "comando.liberar_saida",
        TipoDeComando.LiberarDoisSentidos => "comando.liberar_dois_sentidos",
        _ => null,
    };

    /// <summary>O ensaio do docs/21 §6D que liga a chave do tipo (ids da matriz de funções).</summary>
    public static string? EnsaioQueLiga(TipoDeComando tipo) => tipo switch
    {
        TipoDeComando.BipCurto or TipoDeComando.BipLongo => "INT-UX-03",
        TipoDeComando.LiberarSaida => "HIL-DIR-04",
        TipoDeComando.LiberarDoisSentidos => "HIL-DIR-07",
        _ => null,
    };

    /// <summary>
    /// A mensagem de recusa enquanto a decisão D5 (fail-safe × fail-secure e evacuação,
    /// docs/34 §9) não for tomada pelo dono do produto.
    /// </summary>
    public const string AguardandoDecisaoD5 = "Aguardando decisão D5 do dono do produto";

    /// <summary>
    /// Por que o serviço recusa este tipo agora, antes de enfileirar; vazio = pode seguir para a
    /// validação do pedido.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Liberar nos dois sentidos permite carona: é decisão de segurança do evento, não técnica.
    /// Enquanto a D5 não for tomada, o pedido é recusado <b>mesmo com a chave ligada</b> — a
    /// chave é a bancada (HIL-DIR-07), a D5 é do dono, e ligar exige as duas (docs/34 §9 e §11).
    /// Quando a D5 for tomada, a mudança é aqui, num PR com a decisão registrada.
    /// </para>
    /// </remarks>
    /// <param name="tipo">O que foi pedido.</param>
    /// <param name="chaveLigada">A chave técnica do tipo (<see cref="ChaveTecnica"/>) está ligada.</param>
    public static IReadOnlyList<string> RecusasDoServico(TipoDeComando tipo, bool chaveLigada)
    {
        var recusas = new List<string>();

        if (tipo is TipoDeComando.LiberarDoisSentidos)
        {
            recusas.Add($"{AguardandoDecisaoD5}: liberar nos dois sentidos permite carona, e fail-safe × fail-secure e evacuação ainda não foram decididos (docs/34 §9).");
        }

        if (ChaveTecnica(tipo) is { } chave && !chaveLigada)
        {
            recusas.Add($"Comando desligado nesta instalação: a chave técnica {chave} fica desligada até o ensaio de bancada {EnsaioQueLiga(tipo)} (docs/21 §6D).");
        }

        return recusas;
    }

    /// <summary>Monta um pedido válido, ou devolve os problemas.</summary>
    public static (ComandoDeCatraca? Comando, IReadOnlyList<string> Problemas) Criar(
        int inner,
        TipoDeComando tipo,
        string? operador,
        DateTimeOffset agora,
        string? texto = null,
        int duracaoSegundos = 10,
        string? motivo = null,
        string? confirmacao = null)
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

        if (tipo is TipoDeComando.LiberarSaida && (porque is null || porque.Length is < 5 or > 200))
        {
            problemas.Add("A liberação de saída exige o motivo (5 a 200 caracteres).");
        }

        if (tipo is TipoDeComando.LiberarDoisSentidos)
        {
            if (porque is null || porque.Length is < 5 or > 200)
            {
                problemas.Add("Liberar nos dois sentidos exige o motivo da evacuação (5 a 200 caracteres).");
            }

            // A confirmação não é gravada: só existe para o pedido não sair por um clique.
            if (!string.Equals(confirmacao?.Trim(), ConfirmacaoDosDoisSentidos(inner), StringComparison.OrdinalIgnoreCase))
            {
                problemas.Add($"Liberar nos dois sentidos é só para evacuação: digite {ConfirmacaoDosDoisSentidos(inner)} para confirmar.");
            }
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
            EhLiberacao(tipo) ? porque : null,
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
