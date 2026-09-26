using System.Diagnostics;
using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Domain.Ticketing;

namespace Access.Application.Ingressos;

/// <summary>
/// A base de ingressos, vista por quem decide na catraca.
/// </summary>
/// <remarks>
/// Existe como porta para que a decisão seja testada sem SQLite. A implementação real é
/// <c>RepositorioDeIngressos</c>.
/// </remarks>
public interface IValidadorDeIngressos
{
    /// <summary>Tenta consumir um uso. Grava a tentativa, qualquer que seja o desfecho.</summary>
    (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
        string qrNormalizado,
        string gateId,
        string deviceId,
        DateTimeOffset agora,
        KnownEventOrigin? leitor);

    /// <summary>Anexa a prova de giro (origem 6) a uma tentativa consumida.</summary>
    void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em);
}

/// <summary>
/// Liga a leitura da catraca à base de ingressos: transforma o evento numa decisão, e o
/// giro na confirmação da passagem.
/// </summary>
/// <remarks>
/// <para>
/// É a peça que faltava para um teste físico ter sentido. Sem ela, o laço que fala com a
/// catraca recebia a leitura e parava — de propósito: liberar por omissão seria política
/// de segurança tomada por engano.
/// </para>
/// <para>
/// <b>Um equipamento, uma tentativa pendente.</b> A leitura consome o ingresso e deixa a
/// tentativa pendente; o giro (origem 6) do mesmo equipamento a confirma; o fim do tempo
/// de acionamento (origem 5) a encerra sem giro — e ela aparece na prestação de contas
/// como uso sem passagem física, que é exatamente o que ela é.
/// </para>
/// <para>
/// Não é seguro entre threads, e não precisa ser: o laço de cada worker chama numa thread
/// só, como a DLL exige.
/// </para>
/// </remarks>
public sealed class DecisorDeIngresso
{
    private readonly IValidadorDeIngressos _validador;
    private readonly Func<string, string> _portaoDoEquipamento;
    private readonly TimeProvider _relogio;
    private readonly Dictionary<string, Guid> _pendentes = new(StringComparer.Ordinal);

    /// <summary>
    /// </summary>
    /// <param name="validador">A base de ingressos.</param>
    /// <param name="portaoDoEquipamento">
    /// Qual portão cada equipamento representa. Sem ele, o portão é o próprio equipamento
    /// — suficiente para bancada, insuficiente para relatório por portão.
    /// </param>
    /// <param name="relogio">Relógio.</param>
    public DecisorDeIngresso(
        IValidadorDeIngressos validador,
        Func<string, string>? portaoDoEquipamento = null,
        TimeProvider? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(validador);
        _validador = validador;
        _portaoDoEquipamento = portaoDoEquipamento ?? (d => d);
        _relogio = relogio ?? TimeProvider.System;
    }

    /// <summary>Giros confirmados desde a criação. Para o painel da bancada.</summary>
    public int PassagensConfirmadas { get; private set; }

    /// <summary>Autorizações que terminaram sem giro. Para o painel da bancada.</summary>
    public int AutorizacoesSemGiro { get; private set; }

    /// <summary>Decide sobre uma leitura. Nunca lança: falha vira negativa com motivo.</summary>
    public Decision Decidir(DeviceEvent evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        var inicio = Stopwatch.GetTimestamp();
        var credencial = evento.RawCardData?.Trim();

        if (string.IsNullOrEmpty(credencial))
        {
            return Negar(ReasonCodes.CredencialDesconhecida, "leitura sem credencial", inicio);
        }

        var leitor = evento.Origin.Known is KnownEventOrigin.Leitor1 or KnownEventOrigin.Leitor2
            ? evento.Origin.Known
            : null;

        ResultadoDoUso resultado;
        Guid tentativa;
        try
        {
            (resultado, tentativa) = _validador.TentarUsar(
                credencial,
                _portaoDoEquipamento(evento.Key.DeviceId),
                evento.Key.DeviceId,
                _relogio.GetUtcNow(),
                leitor);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Base local travada ou corrompida. Nega: liberar sem saber seria escolher
            // fail-safe no lugar do cliente, e a pergunta B4 continua sem resposta.
            return Negar(ReasonCodes.FalhaNaBaseLocal, erro.GetType().Name, inicio);
        }

        if (!resultado.Liberou)
        {
            return Negar(CodigoPara(resultado.Motivo), resultado.Motivo.ToString(), inicio);
        }

        // Uma leitura nova antes do giro da anterior: a anterior terminou sem passagem.
        if (_pendentes.Remove(evento.Key.DeviceId))
        {
            AutorizacoesSemGiro++;
        }

        _pendentes[evento.Key.DeviceId] = tentativa;

        return new Decision(
            DecisionOutcome.Allowed,
            ReasonCodes.Autorizado,
            DegradationTier.T1SemInternet,
            Stopwatch.GetElapsedTime(inicio),
            [new RuleTrace("ingresso", true, $"{resultado.ProvedorId}/{resultado.Categoria ?? "-"}")]);
    }

    /// <summary>
    /// Recebe todo evento do equipamento, para fechar o ciclo da passagem.
    /// </summary>
    public void AoReceberEvento(DeviceEvent evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (evento.Origin.ConfirmaPassagemFisica)
        {
            if (_pendentes.Remove(evento.Key.DeviceId, out var tentativa))
            {
                _validador.ConfirmarPassagemFisica(tentativa, evento.ReceivedTime);
                PassagensConfirmadas++;
            }

            return;
        }

        if (evento.Origin.Known is KnownEventOrigin.FimTempoAcionamento
            && _pendentes.Remove(evento.Key.DeviceId))
        {
            // Autorizado e não girou: desistiu, travou, ou ninguém passou.
            AutorizacoesSemGiro++;
        }
    }

    /// <summary>Tradução estável de motivo de uso para código de relatório.</summary>
    public static ReasonCode CodigoPara(MotivoDoUso motivo) => motivo switch
    {
        MotivoDoUso.Consumido => ReasonCodes.Autorizado,
        MotivoDoUso.Desconhecido => ReasonCodes.CredencialDesconhecida,
        MotivoDoUso.UsosEsgotados => ReasonCodes.UsosEsgotados,
        MotivoDoUso.Cancelado => ReasonCodes.IngressoCancelado,
        MotivoDoUso.Bloqueado => ReasonCodes.CredencialBloqueada,
        MotivoDoUso.ForaDaJanela => ReasonCodes.ForaDaJanela,
        MotivoDoUso.ProvedorDesabilitado => ReasonCodes.ProvedorDesabilitado,
        MotivoDoUso.EmIntervaloDeReuso => ReasonCodes.IntervaloDeReuso,
        MotivoDoUso.VendaAnteriorNaoUsada => ReasonCodes.VendaAnteriorNaoUsada,
        MotivoDoUso.ForaDaUrna => ReasonCodes.ForaDaUrna,
        _ => ReasonCodes.MotivoNaoMapeado,
    };

    private static Decision Negar(ReasonCode motivo, string detalhe, long inicio) =>
        new(
            DecisionOutcome.Denied,
            motivo,
            DegradationTier.T1SemInternet,
            Stopwatch.GetElapsedTime(inicio),
            [new RuleTrace("ingresso", false, detalhe)]);
}
