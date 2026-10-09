using System.Diagnostics;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Credentials;
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
    /// <param name="qrNormalizado">Código já normalizado pelo perfil da leitura.</param>
    /// <param name="gateId">Portão.</param>
    /// <param name="deviceId">Equipamento.</param>
    /// <param name="agora">Instante da leitura.</param>
    /// <param name="leitor">
    /// Leitor 1 ou leitor 2 (fenda da urna), quando a leitura veio de um deles. É o que a
    /// regra "somente na urna" consulta.
    /// </param>
    /// <param name="origemBruta">
    /// A origem exatamente como a catraca a entregou, conhecida ou não (ADR-0018). Vai para
    /// a tentativa gravada, para o painel e o gêmeo saberem de onde veio a leitura
    /// (docs/35, Etapa 0.3). Não entra na decisão: quem decide é <paramref name="leitor"/>.
    /// Nulo quando quem chama não sabe.
    /// </param>
    (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
        string qrNormalizado,
        string gateId,
        string deviceId,
        DateTimeOffset agora,
        KnownEventOrigin? leitor,
        int? origemBruta);

    /// <summary>
    /// Tenta consumir um uso, gravando também como o giro desta leitura será liberado e contado
    /// (mapa de giro, D9 do docs/34 §9).
    /// </summary>
    /// <param name="qrNormalizado">Código já normalizado pelo perfil da leitura.</param>
    /// <param name="gateId">Portão.</param>
    /// <param name="deviceId">Equipamento.</param>
    /// <param name="agora">Instante da leitura.</param>
    /// <param name="leitor">Leitor 1 ou 2, quando a leitura veio de um deles.</param>
    /// <param name="origemBruta">A origem exatamente como a catraca a entregou.</param>
    /// <param name="giro">
    /// A regra que vale para a origem desta leitura na catraca: a função que o laço vai chamar e
    /// o rótulo. Nulo quando quem chama não sabe (bancada, ferramentas): grava como antes.
    /// </param>
    /// <remarks>
    /// Implementação padrão: ignora o giro e grava como antes. Quem guarda o rótulo (a base
    /// real) sobrepõe; dublês de teste continuam valendo sem mudança.
    /// </remarks>
    (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
        string qrNormalizado,
        string gateId,
        string deviceId,
        DateTimeOffset agora,
        KnownEventOrigin? leitor,
        int? origemBruta,
        GiroResolvido? giro) =>
        TentarUsar(qrNormalizado, gateId, deviceId, agora, leitor, origemBruta);

    /// <summary>Anexa a prova de giro (origem 6) a uma tentativa consumida.</summary>
    void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em);

    /// <summary>
    /// Anexa a prova de giro (origem 6) e o complemento bruto que a catraca mandou com ela.
    /// </summary>
    /// <remarks>
    /// O complemento da origem 6 talvez traga o sentido físico do giro (T14, NOVO-HIL-DIR-08):
    /// guardado como veio, nada se perde (ADR-0018), e o rótulo contado continua o do mapa.
    /// Implementação padrão: só a prova, como antes.
    /// </remarks>
    void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em, byte complementoDoGiro) =>
        ConfirmarPassagemFisica(tentativaId, em);

    /// <summary>
    /// Grava na tentativa consumida por que a liberação falhou (achado E1-05 do docs/41). O uso continua
    /// consumido; o operador vê a tentativa no painel e pode estorná-la.
    /// </summary>
    /// <remarks>Implementação padrão: não grava nada (dublês de teste).</remarks>
    /// <param name="tentativaId">A tentativa consumida.</param>
    /// <param name="em">Quando a falha aconteceu.</param>
    /// <param name="causa">O que aconteceu, em texto para o operador.</param>
    void RegistrarLiberacaoFalhou(Guid tentativaId, DateTimeOffset em, string causa)
    {
    }
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
    private readonly CredentialNormalization _perfilDaLeitura;
    private readonly Dictionary<string, Guid> _pendentes = new(StringComparer.Ordinal);
    private readonly List<(Guid Tentativa, DateTimeOffset Em, byte Complemento)> _confirmacoesAGravar = [];
    private readonly List<(Guid Tentativa, DateTimeOffset Em, string Causa)> _falhasDeLiberacaoAGravar = [];

    /// <summary>
    /// </summary>
    /// <param name="validador">A base de ingressos.</param>
    /// <param name="portaoDoEquipamento">
    /// Qual portão cada equipamento representa. Sem ele, o portão é o próprio equipamento
    /// — suficiente para bancada, insuficiente para relatório por portão.
    /// </param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="perfilDaLeitura">
    /// Perfil aplicado ao código lido. Nulo é <see cref="PerfisDeLeitura.DaLeitura"/>
    /// (<c>raw</c>), o que a operação usa hoje. Outro perfil só entra com a parametrização
    /// por catraca (docs/35, Etapa A), depois da bancada (docs/21, passo 3, linhas 8 a 12).
    /// </param>
    public DecisorDeIngresso(
        IValidadorDeIngressos validador,
        Func<string, string>? portaoDoEquipamento = null,
        TimeProvider? relogio = null,
        CredentialNormalization? perfilDaLeitura = null)
    {
        ArgumentNullException.ThrowIfNull(validador);
        _validador = validador;
        _portaoDoEquipamento = portaoDoEquipamento ?? (d => d);
        _relogio = relogio ?? TimeProvider.System;
        _perfilDaLeitura = perfilDaLeitura ?? PerfisDeLeitura.DaLeitura;
    }

    /// <summary>Giros confirmados desde a criação. Para o painel da bancada.</summary>
    public int PassagensConfirmadas { get; private set; }

    /// <summary>Autorizações que terminaram sem giro. Para o painel da bancada.</summary>
    public int AutorizacoesSemGiro { get; private set; }

    /// <summary>
    /// Giros já recebidos, e causas de liberação que falhou, cuja gravação na base ainda não deu certo
    /// (base ocupada, por exemplo).
    /// </summary>
    public int ConfirmacoesAGravar => _confirmacoesAGravar.Count + _falhasDeLiberacaoAGravar.Count;

    /// <summary>Tentativas de gravar uma confirmação de giro que falharam e serão repetidas.</summary>
    public int FalhasAoGravarConfirmacao { get; private set; }

    /// <summary>Decide sobre uma leitura. Nunca lança: falha vira negativa com motivo.</summary>
    public Decision Decidir(DeviceEvent evento) => Decidir(evento, giro: null);

    /// <summary>
    /// Decide sobre uma leitura, gravando junto como o giro dela será liberado e contado.
    /// Nunca lança: falha vira negativa com motivo.
    /// </summary>
    /// <param name="evento">A leitura.</param>
    /// <param name="giro">
    /// A regra do mapa de giro (D9) para a origem desta leitura na catraca, a mesma que o laço
    /// usa para escolher a função de liberação. Nulo grava como antes.
    /// </param>
    public Decision Decidir(DeviceEvent evento, GiroResolvido? giro)
    {
        ArgumentNullException.ThrowIfNull(evento);

        var inicio = Stopwatch.GetTimestamp();
        // A mesma função que normaliza o cadastro (docs/34 §2, F8). Com o perfil raw de
        // hoje, é o Trim de sempre.
        var credencial = PerfisDeLeitura.Normalizar(evento.RawCardData, _perfilDaLeitura);

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
                leitor,
                evento.Origin.Raw,
                giro);
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
                _confirmacoesAGravar.Add((tentativa, evento.ReceivedTime, evento.Complement));
                PassagensConfirmadas++;
            }

            GravarConfirmacoesPendentes();
            return;
        }

        if (evento.Origin.Known is KnownEventOrigin.FimTempoAcionamento
            && _pendentes.Remove(evento.Key.DeviceId))
        {
            // Autorizado e não girou: desistiu, travou, ou ninguém passou.
            AutorizacoesSemGiro++;
        }
    }

    /// <summary>
    /// Grava os giros recebidos que ainda não foram para a base, na ordem em que chegaram. Nunca
    /// lança: o que falhar fica para a próxima chamada.
    /// </summary>
    /// <remarks>
    /// Achado E1-04 do docs/41: a gravação do giro não tinha proteção. Uma exceção (base ocupada além
    /// do prazo, disco cheio) subia até o processo e o derrubava, com todas as catracas do worker, e o
    /// giro, que só existia em memória, se perdia. O laço chama este método a cada volta.
    /// </remarks>
    public void GravarConfirmacoesPendentes()
    {
        GravarFalhasDeLiberacao();

        while (_confirmacoesAGravar.Count > 0)
        {
            var (tentativa, em, complemento) = _confirmacoesAGravar[0];
            try
            {
                _validador.ConfirmarPassagemFisica(tentativa, em, complemento);
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                FalhasAoGravarConfirmacao++;
                return;
            }

            _confirmacoesAGravar.RemoveAt(0);
        }
    }

    /// <summary>
    /// Anota por que a liberação da tentativa pendente do equipamento falhou, para gravar na base. Não a
    /// encerra: quem encerra é <see cref="DescartarPendente"/>, chamado logo depois. Nunca lança.
    /// </summary>
    /// <param name="deviceId">O equipamento.</param>
    /// <param name="causa">O que aconteceu, em texto para o operador.</param>
    /// <remarks>
    /// Achado E1-05 do docs/41 (decisão do dono do produto: o ingresso continua consumido, com registro
    /// individual para o operador estornar pelo painel). Sem tentativa pendente (liberação manual, por
    /// exemplo), não há o que anotar.
    /// </remarks>
    public void RegistrarCausaSemGiro(string deviceId, string causa)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(causa);

        if (_pendentes.TryGetValue(deviceId, out var tentativa))
        {
            _falhasDeLiberacaoAGravar.Add((tentativa, _relogio.GetUtcNow(), causa));
            GravarFalhasDeLiberacao();
        }
    }

    private void GravarFalhasDeLiberacao()
    {
        while (_falhasDeLiberacaoAGravar.Count > 0)
        {
            var (tentativa, em, causa) = _falhasDeLiberacaoAGravar[0];
            try
            {
                _validador.RegistrarLiberacaoFalhou(tentativa, em, causa);
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                FalhasAoGravarConfirmacao++;
                return;
            }

            _falhasDeLiberacaoAGravar.RemoveAt(0);
        }
    }

    /// <summary>
    /// Encerra, sem giro, a tentativa pendente do equipamento. Chamado antes de uma
    /// liberação manual: o giro que vier depois é do operador, e confirmar com ele a
    /// passagem do último ingresso lido seria atribuir a entrada a outra pessoa.
    /// </summary>
    /// <remarks>
    /// Também é chamado quando o laço desiste de esperar o giro pelo prazo, sem origem 5 nem 6
    /// (C1, docs/36): o mesmo desfecho da origem 5 — uso sem passagem física. A regra do uso não
    /// muda: o ingresso já foi consumido na leitura, como sempre.
    /// </remarks>
    public void DescartarPendente(string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);

        if (_pendentes.Remove(deviceId))
        {
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
        MotivoDoUso.TipoInativo => ReasonCodes.TipoInativo,
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
