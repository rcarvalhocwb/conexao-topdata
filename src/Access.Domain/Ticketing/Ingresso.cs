namespace Access.Domain.Ticketing;

/// <summary>Situação de um ingresso na base local.</summary>
public enum StatusDoIngresso
{
    /// <summary>Pode ser usado.</summary>
    Valido,

    /// <summary>Esgotou os usos permitidos.</summary>
    Consumido,

    /// <summary>O provedor cancelou — estorno, chargeback, troca.</summary>
    Cancelado,

    /// <summary>Bloqueado pela operação — fraude suspeita, duplicidade, ordem judicial.</summary>
    Bloqueado,
}

/// <summary>Por que uma tentativa de uso terminou como terminou.</summary>
/// <remarks>
/// Cada valor vira uma linha de uma classe de divergência na prestação de contas. Não é
/// enfeite de tela: é a diferença entre "o cliente não veio" e "o ingresso nunca chegou
/// na nossa base", que têm culpados diferentes e consequências financeiras diferentes.
/// </remarks>
public enum MotivoDoUso
{
    /// <summary>Consumido com sucesso.</summary>
    Consumido,

    /// <summary>
    /// O QR não existe na base local. <b>A classe mais grave.</b> É falsificação, ou é
    /// ingresso vendido que nunca chegou até aqui — e nesse caso o problema é nosso.
    /// </summary>
    Desconhecido,

    /// <summary>Já tinha esgotado os usos. Tentativa de reentrada ou de compartilhamento.</summary>
    UsosEsgotados,

    /// <summary>O provedor cancelou antes do uso.</summary>
    Cancelado,

    /// <summary>Bloqueado pela operação.</summary>
    Bloqueado,

    /// <summary>Fora da janela de validade do ingresso.</summary>
    ForaDaJanela,

    /// <summary>O provedor está desabilitado na configuração local.</summary>
    ProvedorDesabilitado,

    /// <summary>
    /// O cartão foi usado há menos tempo que o intervalo mínimo de reuso.
    /// </summary>
    /// <remarks>
    /// O ciclo físico de um cartão reutilizável — passar na catraca, voltar para a
    /// bilheteria, ser revendido — leva minutos. O mesmo cartão aparecendo de novo antes
    /// disso não é um cliente: é o cartão passado por cima da grade para quem está do lado
    /// de fora, ou um clone.
    /// </remarks>
    EmIntervaloDeReuso,

    /// <summary>
    /// Tentativa de revender um cartão cuja venda anterior ainda não foi usada.
    /// </summary>
    /// <remarks>
    /// Só existe na bilheteria. Sobrescrever uma venda paga e não usada apaga dinheiro.
    /// </remarks>
    VendaAnteriorNaoUsada,

    /// <summary>
    /// Cartão que só vale na fenda da urna, lido em outro leitor.
    /// </summary>
    /// <remarks>
    /// Aceitar no leitor da frente deixaria a pessoa passar com o cartão na mão — e daí
    /// para o outro lado da grade. A urna existe para que o cartão fique.
    /// </remarks>
    ForaDaUrna,
}

/// <summary>
/// Provedor de ingressos: uma bilheteria, um site, um sistema de vendas.
/// </summary>
/// <param name="Id">Identificador local e estável. Entra na prestação de contas.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="PerfilDeNormalizacao">
/// Perfil aplicado ao QR <b>deste</b> provedor. Cada um emite do seu jeito — um manda
/// com prefixo, outro em minúsculas, outro com hífen. A normalização é por provedor
/// justamente porque não existe um formato só.
/// </param>
/// <param name="Conector">Para onde vai o retorno de uso. Ver docs/15, §7.</param>
/// <param name="Habilitado">Provedor desabilitado não valida e não ingere.</param>
/// <param name="Reutilizavel">
/// Verdadeiro para a bilheteria local de cartão físico: o mesmo cartão é vendido, usado,
/// devolvido e vendido de novo. Falso para ingresso online, que nasce e morre uma vez.
/// </param>
/// <param name="IntervaloDeReuso">
/// Tempo mínimo entre dois usos do mesmo cartão. Zero desliga. A revenda <b>não</b>
/// zera este relógio — é isso que impede o cartão de ser passado por cima da grade e
/// revendido na hora.
/// </param>
/// <param name="SomenteNaUrna">
/// O ingresso só vale se lido na fenda da urna (leitor 2). Para o cartão da bilheteria,
/// que precisa ficar retido na entrada.
/// </param>
public sealed record ProvedorDeIngresso(
    string Id,
    string Nome,
    string PerfilDeNormalizacao,
    string Conector,
    bool Habilitado = true,
    bool Reutilizavel = false,
    TimeSpan IntervaloDeReuso = default,
    bool SomenteNaUrna = false);

/// <summary>Ingresso como o provedor o entregou, antes de virar linha no banco.</summary>
/// <param name="ProvedorId">De quem veio.</param>
/// <param name="ReferenciaExterna">Identificador do ingresso <b>no provedor</b>. É por ele que se presta contas.</param>
/// <param name="QrBruto">Exatamente como o provedor mandou. Preservado para auditoria.</param>
/// <param name="QrNormalizado">O que o leitor vai casar.</param>
/// <param name="Setor">Setor, pista, camarote. Nulo quando o evento não setoriza.</param>
/// <param name="ValidoDe">Início da janela. Nulo = sem limite inferior.</param>
/// <param name="ValidoAte">Fim da janela. Nulo = sem limite superior.</param>
/// <param name="UsosMaximos">1 para ingresso comum; mais para passe de vários dias ou reentrada.</param>
/// <param name="Cancelado">O provedor já entregou cancelado (estorno antes do evento).</param>
/// <param name="Categoria">
/// Inteira, meia, solidária — ou qualquer outra que aparecer. <b>É texto aberto de
/// propósito</b>: um tipo novo de entrada criado na véspera do evento não pode exigir uma
/// versão nova do sistema.
/// </param>
public sealed record IngressoRecebido(
    string ProvedorId,
    string ReferenciaExterna,
    string QrBruto,
    string QrNormalizado,
    string? Setor = null,
    DateTimeOffset? ValidoDe = null,
    DateTimeOffset? ValidoAte = null,
    int UsosMaximos = 1,
    bool Cancelado = false,
    string? Categoria = null);

/// <summary>
/// Resultado de uma tentativa de uso, já decidida.
/// </summary>
/// <param name="Motivo">Classe do resultado.</param>
/// <param name="IngressoId">Identificador local, quando o QR foi reconhecido.</param>
/// <param name="ProvedorId">De quem é o ingresso, quando reconhecido.</param>
/// <param name="Setor">Setor do ingresso, para conferir contra o portão.</param>
/// <param name="UsosRestantes">Quantos usos sobraram depois desta tentativa.</param>
/// <param name="Categoria">Categoria da venda que foi (ou seria) consumida.</param>
public sealed record ResultadoDoUso(
    MotivoDoUso Motivo,
    Guid? IngressoId = null,
    string? ProvedorId = null,
    string? Setor = null,
    int UsosRestantes = 0,
    string? Categoria = null)
{
    /// <summary>Verdadeiro quando o giro deve ser liberado.</summary>
    public bool Liberou => Motivo is MotivoDoUso.Consumido;
}
