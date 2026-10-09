using System.Globalization;
using Access.Domain.Access;
using Access.Domain.Tempo;
using Access.Domain.Ticketing;

namespace Access.Inteligencia;

/// <summary>O uso anterior do mesmo ingresso, como a base o gravou.</summary>
/// <param name="Em">Quando foi liberado.</param>
/// <param name="Catraca">Em que catraca (número do equipamento).</param>
/// <param name="Girou">Se houve giro confirmado pelo sensor depois da liberação.</param>
public sealed record UsoAnterior(DateTimeOffset Em, int Catraca, bool Girou);

/// <summary>
/// O que a base sabe em volta de uma negação — e só isso. Não tem campo para código, máscara,
/// nome nem categoria: a explicação não tem como carregar dado pessoal (docs/36-anexos/02 §6;
/// invariante I6, §3.1). Categoria fica de fora de propósito: PCD, idoso e criança são
/// categorias sensíveis (docs/34-anexos/03).
/// </summary>
/// <param name="Em">Quando a leitura foi negada.</param>
/// <param name="Catraca">Em que catraca.</param>
/// <param name="UltimoUso">O último uso liberado do mesmo ingresso antes desta leitura; nulo se não houver ou se o código é desconhecido.</param>
/// <param name="IntervaloDeReuso">O intervalo mínimo entre dois usos do cartão, do canal de venda; nulo ou zero se não houver.</param>
/// <param name="IdadeDaBase">Há quanto tempo a base deste PC recebeu a última atualização da nuvem; nulo se a nuvem não está configurada ou nunca sincronizou.</param>
public sealed record ContextoDaNegativa(
    DateTimeOffset Em,
    int Catraca,
    UsoAnterior? UltimoUso = null,
    TimeSpan? IntervaloDeReuso = null,
    TimeSpan? IdadeDaBase = null);

/// <summary>A explicação de uma negação para o operador (docs/36-anexos/03 §2.3, T4).</summary>
/// <param name="OQueAconteceu">O que aconteceu, em uma ou duas frases.</param>
/// <param name="OQueDizer">A frase para dizer à pessoa na catraca.</param>
/// <param name="OQueFazer">O que o operador confere ou faz.</param>
/// <param name="TemTextoProprio">Falso só para motivo que esta versão não conhece (cai no texto genérico).</param>
public sealed record ExplicacaoDaNegativa(string OQueAconteceu, string OQueDizer, string OQueFazer, bool TemTextoProprio = true);

/// <summary>
/// "Por que negou" (IN-06, Etapa I.2 do docs/36): para cada motivo de negação, o que aconteceu,
/// o que dizer à pessoa e o que fazer — em português de portaria, sem termo do SDK e sem dado
/// pessoal ou código.
/// </summary>
/// <remarks>
/// <para>
/// Função pura e determinística: mesmo motivo e mesmo contexto dão o mesmo texto, byte a byte
/// (invariante I5). Não precisa do Analisador nem da chave <see cref="ChavesDaInteligencia.Ligada"/>:
/// funciona com a camada desligada, sobre o que a decisão já gravou (docs/36-anexos/02 §3.4,
/// "sob pedido"; §9, I.2 não toca no worker).
/// </para>
/// <para>
/// Aceita o motivo como a tentativa o grava (o nome de <see cref="MotivoDoUso"/>, coluna
/// <c>reason</c>) e como o relatório o codifica (<see cref="ReasonCodes"/>). Todo motivo de
/// negação dos dois catálogos tem texto próprio — um teste por reflexão reprova motivo novo sem
/// texto. A explicação é para o operador, no painel; o display da catraca continua com a
/// mensagem genérica (docs/36-anexos/02 IN-06, risco "oráculo para fraude").
/// </para>
/// </remarks>
public static class PorQueNegou
{
    /// <summary>Os códigos de <see cref="ReasonCodes"/> que não são negação.</summary>
    public static IReadOnlySet<string> CodigosQueNaoNegam { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ReasonCodes.Autorizado,
        ReasonCodes.AutorizadoPorListaLocal,
        ReasonCodes.LiberacaoManual,
        ReasonCodes.AutorizadoSemConfirmacao,
    };

    // O motivo como a tentativa grava (MotivoDoUso) → o código estável do relatório. É a mesma
    // tradução de DecisorDeIngresso.CodigoPara (Access.Application, que esta camada não
    // referencia); um teste confere as duas, motivo a motivo.
    private static readonly Dictionary<string, string> CodigoDoMotivo = new(StringComparer.Ordinal)
    {
        [nameof(MotivoDoUso.Desconhecido)] = ReasonCodes.CredencialDesconhecida,
        [nameof(MotivoDoUso.UsosEsgotados)] = ReasonCodes.UsosEsgotados,
        [nameof(MotivoDoUso.CadastradoNoLote)] = ReasonCodes.CadastradoNoLote,
        [nameof(MotivoDoUso.Cancelado)] = ReasonCodes.IngressoCancelado,
        [nameof(MotivoDoUso.Bloqueado)] = ReasonCodes.CredencialBloqueada,
        [nameof(MotivoDoUso.ForaDaJanela)] = ReasonCodes.ForaDaJanela,
        [nameof(MotivoDoUso.ProvedorDesabilitado)] = ReasonCodes.ProvedorDesabilitado,
        [nameof(MotivoDoUso.EmIntervaloDeReuso)] = ReasonCodes.IntervaloDeReuso,
        [nameof(MotivoDoUso.VendaAnteriorNaoUsada)] = ReasonCodes.VendaAnteriorNaoUsada,
        [nameof(MotivoDoUso.ForaDaUrna)] = ReasonCodes.ForaDaUrna,
        [nameof(MotivoDoUso.TipoInativo)] = ReasonCodes.TipoInativo,
        [nameof(MotivoDoUso.PessoaBloqueada)] = ReasonCodes.PessoaBloqueada,
        [nameof(MotivoDoUso.PessoaInativa)] = ReasonCodes.PessoaInativa,
        [nameof(MotivoDoUso.ForaDoHorario)] = ReasonCodes.ForaDoHorario,
        [nameof(MotivoDoUso.PortaoNaoPermitido)] = ReasonCodes.PortaoNaoPermitido,
        [nameof(MotivoDoUso.CredencialInativa)] = ReasonCodes.CredencialInativa,
        [nameof(MotivoDoUso.ForaDaValidade)] = ReasonCodes.PessoaForaDaValidade,
        [nameof(MotivoDoUso.LimiteDiario)] = ReasonCodes.LimiteDiario,
        [nameof(MotivoDoUso.CatracaFechada)] = ReasonCodes.CatracaFechada,
    };

    private const string ProcureOAtendimento = "Procure o atendimento, por favor.";
    private const string UseOutraCatraca = "Use outra catraca, por favor.";
    private const string TenteDeNovo = "Tente de novo, por favor.";

    // Textos fixos dos motivos que não dependem de contexto: (o que aconteceu, o que dizer, o que fazer).
    private static readonly Dictionary<string, (string Aconteceu, string Dizer, string Fazer)> Fixos = new(StringComparer.Ordinal)
    {
        [ReasonCodes.PessoaBloqueada] = (
            "A pessoa está bloqueada no cadastro deste local.",
            ProcureOAtendimento,
            "Confira o motivo do bloqueio na ficha da pessoa (Pessoas) antes de qualquer liberação manual."),
        [ReasonCodes.PessoaInativa] = (
            "O cadastro desta pessoa está inativo.",
            ProcureOAtendimento,
            "Confira na ficha da pessoa se o cadastro deve ser reativado."),
        [ReasonCodes.ForaDoHorario] = (
            "A pessoa está fora dos horários permitidos para ela.",
            "Seu acesso não vale neste horário. " + ProcureOAtendimento,
            "Confira a tabela de horário do perfil ou da pessoa (Horários) e o relógio desta catraca."),
        [ReasonCodes.LimiteDiario] = (
            "A pessoa já usou as entradas permitidas hoje.",
            "Suas entradas de hoje já foram usadas. " + ProcureOAtendimento,
            "Confira o limite diário do perfil ou da pessoa."),
        [ReasonCodes.PortaoNaoPermitido] = (
            "A pessoa não tem permissão nesta catraca.",
            "Seu acesso não vale nesta entrada. " + ProcureOAtendimento,
            "Confira as catracas permitidas no perfil ou na ficha da pessoa."),
        [ReasonCodes.CredencialInativa] = (
            "O cartão, QR ou senha desta pessoa está bloqueado, perdido ou devolvido.",
            ProcureOAtendimento,
            "Confira a credencial na ficha da pessoa; se ela foi perdida, cadastre uma nova."),
        [ReasonCodes.PessoaForaDaValidade] = (
            "O cadastro ou a credencial desta pessoa está fora da validade (visita encerrada ou ainda não começou).",
            "Seu acesso não vale nesta data. " + ProcureOAtendimento,
            "Confira as datas de validade na ficha da pessoa e o relógio desta catraca."),
        [ReasonCodes.CatracaFechada] = (
            "Esta catraca está fechada pelo operador: ninguém passa até reabrir.",
            UseOutraCatraca,
            "Reabra a catraca em Gerenciar catraca quando for o caso."),
        [ReasonCodes.CredencialBloqueada] = (
            "Este ingresso foi bloqueado pela operação do evento.",
            ProcureOAtendimento,
            "Confira o motivo do bloqueio com o responsável pelo cadastro antes de qualquer liberação manual."),
        [ReasonCodes.CredencialComprimentoInvalido] = (
            "O código lido não tem o tamanho dos ingressos deste evento.",
            "Não conseguimos ler este ingresso. Tente de novo ou procure a bilheteria.",
            "Se acontecer várias vezes seguidas, confira o leitor desta catraca e a tela do celular da pessoa (brilho, trincado)."),
        [ReasonCodes.ForaDaJanela] = (
            "O ingresso não vale neste horário: está fora do período de validade dele.",
            "Este ingresso não vale neste horário. " + ProcureOAtendimento,
            "Confira a data e o horário do ingresso e o relógio desta catraca (tela Catracas)."),
        [ReasonCodes.CadastradoNoLote] = (
            "Este cartão foi lido na urna durante o cadastro de um lote. Ele entrou no lote; ninguém passou.",
            "Cartão cadastrado para o lote. Não é uma passagem.",
            "Nada a fazer. Confira o lote na tela de cadastro de cartões, se precisar."),
        [ReasonCodes.SetorNaoPermitido] = (
            "O ingresso não vale para o setor desta catraca.",
            "Este ingresso é de outro setor. Procure a entrada indicada nele.",
            "Indique à pessoa a entrada do setor dela."),
        [ReasonCodes.GateNaoPermitido] = (
            "O ingresso não vale nesta entrada.",
            "Este ingresso é de outra entrada. Procure a entrada indicada nele.",
            "Indique à pessoa a entrada certa."),
        [ReasonCodes.LotacaoAtingida] = (
            "A lotação definida para o local foi atingida.",
            "A lotação foi atingida. Aguarde a orientação da equipe, por favor.",
            "Siga o plano de lotação do evento."),
        [ReasonCodes.AntiPassback] = (
            "Este ingresso já tem uma entrada registrada sem a saída correspondente.",
            ProcureOAtendimento,
            "Confira se a pessoa saiu por outra passagem."),
        [ReasonCodes.BloqueioEmergencial] = (
            "As entradas estão bloqueadas por emergência.",
            "As entradas estão suspensas. Aguarde a orientação da equipe, por favor.",
            "Siga o plano de emergência; as entradas só voltam por decisão do responsável."),
        [ReasonCodes.ReplaySuprimido] = (
            "A mesma leitura chegou repetida e a repetição foi descartada, para não contar duas vezes.",
            TenteDeNovo,
            "Caso isolado, nada a fazer. Se repetir, abra o Diagnóstico."),
        [ReasonCodes.IngressoJaReservado] = (
            "O ingresso estava sendo usado em outra catraca no mesmo instante.",
            "Aguarde um instante e tente de novo, por favor.",
            "Se repetir, confira se o mesmo ingresso foi apresentado em duas catracas."),
        [ReasonCodes.CredencialConcorrente] = (
            "O mesmo ingresso foi lido em duas catracas quase ao mesmo tempo.",
            ProcureOAtendimento,
            "Confira se o ingresso foi copiado e apresentado por duas pessoas."),
        [ReasonCodes.UrnaCheia] = (
            "A urna desta catraca está cheia e não recolhe mais cartões.",
            UseOutraCatraca,
            "Esvazie a urna ou oriente a fila para outra catraca."),
        [ReasonCodes.RecolhimentoNaoConfirmado] = (
            "A catraca não confirmou que recolheu o cartão na urna.",
            ProcureOAtendimento,
            "Confira se o cartão ficou preso na fenda da urna."),
        [ReasonCodes.CartaoPresoSuspeita] = (
            "Há suspeita de cartão preso na fenda da urna.",
            ProcureOAtendimento,
            "Confira a fenda da urna desta catraca."),
        [ReasonCodes.RecolhimentoOrfao] = (
            "A urna recolheu um cartão sem uma leitura correspondente.",
            ProcureOAtendimento,
            "Confira a urna e as últimas passagens desta catraca."),
        [ReasonCodes.DesistenciaAntesDoRecolhimento] = (
            "O cartão foi retirado da fenda antes de a urna recolher.",
            "Coloque o cartão na fenda da urna e solte, por favor.",
            "Mostre à pessoa como soltar o cartão na fenda."),
        [ReasonCodes.GiroReverso] = (
            "A catraca girou no sentido contrário ao liberado.",
            "Passe no sentido indicado, por favor.",
            "Confira o sentido de instalação desta catraca."),
        [ReasonCodes.IngressoCancelado] = (
            "A venda deste ingresso foi cancelada por quem vendeu.",
            "Este ingresso foi cancelado. Procure a bilheteria ou o canal onde comprou.",
            "A catraca nunca aceita ingresso cancelado; oriente a pessoa à bilheteria."),
        [ReasonCodes.ProvedorDesabilitado] = (
            "Os ingressos deste canal de venda estão desabilitados neste PC.",
            "Procure a bilheteria, por favor.",
            "Se este canal de venda deveria valer, peça ao responsável para habilitá-lo (a tela Sincronização lista os canais)."),
        [ReasonCodes.VendaAnteriorNaoUsada] = (
            "O cartão tem uma venda anterior que ainda não foi usada; uma venda nova não pode apagá-la.",
            "Procure a bilheteria, por favor.",
            "Na bilheteria: use primeiro a venda anterior, ou devolva-a, antes de vender de novo."),
        [ReasonCodes.ForaDaUrna] = (
            "Cartão da bilheteria lido no leitor da frente; ele só vale na fenda da urna.",
            "Coloque o cartão na fenda da urna, por favor.",
            "Mostre a fenda da urna à pessoa: o cartão fica na catraca."),
        [ReasonCodes.TipoInativo] = (
            "O tipo de entrada deste ingresso foi desativado pela operação.",
            "Este tipo de entrada não está valendo agora. " + ProcureOAtendimento,
            "Se o tipo deveria valer, peça ao responsável para reativá-lo no cadastro de tipos de entrada."),
        [ReasonCodes.TempoDeDecisaoEsgotado] = (
            "O PC demorou demais para decidir e a leitura foi negada por segurança.",
            TenteDeNovo,
            "Se repetir, abra o Diagnóstico: o PC pode estar sobrecarregado."),
        [ReasonCodes.EquipamentoEmManutencao] = (
            "Esta catraca está em manutenção.",
            UseOutraCatraca,
            "Oriente a fila para outra catraca."),
        [ReasonCodes.EquipamentoNaoHomologado] = (
            "Esta catraca não está homologada para operar com este sistema.",
            UseOutraCatraca,
            "Avise o suporte: esta catraca precisa ser conferida antes de operar."),
        [ReasonCodes.FalhaNaBaseLocal] = (
            "A base deste PC não respondeu e a leitura foi negada por segurança: na dúvida, a catraca nunca libera.",
            TenteDeNovo,
            "Se repetir, abra o Diagnóstico e chame o suporte."),
        [ReasonCodes.MotivoNaoMapeado] = (
            "A leitura foi negada por um motivo que esta versão não sabe explicar.",
            ProcureOAtendimento,
            "Anote a hora e a catraca e avise o suporte."),
    };

    /// <summary>
    /// Todos os motivos que têm texto próprio: os nomes de <see cref="MotivoDoUso"/> e os códigos de
    /// <see cref="ReasonCodes"/> que negam.
    /// </summary>
    public static IReadOnlyCollection<string> MotivosComTexto { get; } =
    [
        .. CodigoDoMotivo.Keys,
        .. Fixos.Keys,
        ReasonCodes.CredencialDesconhecida,
        ReasonCodes.UsosEsgotados,
        ReasonCodes.IntervaloDeReuso,
    ];

    /// <summary>Explica uma negação. Nunca lança por motivo desconhecido: cai no texto genérico.</summary>
    /// <param name="motivo">O motivo gravado na tentativa (nome de <see cref="MotivoDoUso"/>) ou um código de <see cref="ReasonCodes"/>.</param>
    /// <param name="contexto">O que a base sabe em volta da negação.</param>
    public static ExplicacaoDaNegativa ExplicarNegativa(string? motivo, ContextoDaNegativa contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var codigo = motivo is null ? string.Empty : CodigoDoMotivo.GetValueOrDefault(motivo, motivo);

        if (codigo == ReasonCodes.CredencialDesconhecida)
        {
            return Desconhecido(contexto);
        }

        if (codigo == ReasonCodes.UsosEsgotados)
        {
            return UsosEsgotados(contexto);
        }

        if (codigo == ReasonCodes.IntervaloDeReuso)
        {
            return EmIntervaloDeReuso(contexto);
        }

        if (Fixos.TryGetValue(codigo, out var fixo))
        {
            return new ExplicacaoDaNegativa(fixo.Aconteceu, fixo.Dizer, fixo.Fazer);
        }

        var generico = Fixos[ReasonCodes.MotivoNaoMapeado];
        return new ExplicacaoDaNegativa(generico.Aconteceu, generico.Dizer, generico.Fazer, TemTextoProprio: false);
    }

    /// <summary>
    /// A liberação que terminou sem giro confirmado (não é negação; docs/36-anexos/03 T4, última
    /// linha da tabela).
    /// </summary>
    public static ExplicacaoDaNegativa ExplicarLiberadoSemGiro(ContextoDaNegativa contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return new ExplicacaoDaNegativa(
            $"Liberado às {Hora(contexto.Em)} na {Catraca(contexto.Catraca)}; o sensor não confirmou o giro. Não conta como entrada.",
            "Se ainda não passou, procure o atendimento para uma nova liberação.",
            "Se a pessoa diz que passou, confira a catraca: braço preso, desistência ou giro que o sensor não registrou.");
    }

    /// <summary>A liberação com giro confirmado: não há o que explicar, e o texto diz isso.</summary>
    public static ExplicacaoDaNegativa ExplicarLiberadoComGiro(ContextoDaNegativa contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return new ExplicacaoDaNegativa(
            $"Liberado às {Hora(contexto.Em)} na {Catraca(contexto.Catraca)}, com giro confirmado pelo sensor. Não houve negação.",
            "A entrada foi registrada.",
            "Nada a fazer.");
    }

    /// <summary>
    /// Onde a leitura foi feita, pela origem gravada na tentativa (migração 010), sem termo do SDK.
    /// Vazio quando não foi gravada ou não é de leitura.
    /// </summary>
    public static string OndeFoiLido(int? origemBruta) => origemBruta switch
    {
        2 => "leitor da frente",
        3 => "fenda da urna",
        21 => "leitor de QR",
        _ => string.Empty,
    };

    private static ExplicacaoDaNegativa Desconhecido(ContextoDaNegativa contexto)
    {
        var base_ = contexto.IdadeDaBase is { } idade
            ? $" A base recebeu a última atualização da nuvem há {Duracao(idade)}."
            : string.Empty;

        return new ExplicacaoDaNegativa(
            "Este código não está na base deste PC." + base_,
            "Não encontramos este ingresso. Procure a bilheteria, por favor.",
            "Confira se o ingresso é deste evento e se a venda chegou ao PC (tela Sincronização). Muitos casos seguidos podem ser um lote de vendas que não chegou.");
    }

    private static ExplicacaoDaNegativa UsosEsgotados(ContextoDaNegativa contexto)
    {
        if (contexto.UltimoUso is not { } uso)
        {
            return new ExplicacaoDaNegativa(
                "Este ingresso já foi usado todas as vezes que podia.",
                "Este ingresso já foi usado. " + ProcureOAtendimento,
                "Se a pessoa diz que não entrou, confira na tela Consulta o histórico deste ingresso; nunca libere sem registrar o motivo.");
        }

        return uso.Girou
            ? new ExplicacaoDaNegativa(
                $"Este ingresso já entrou às {Hora(uso.Em)} pela {Catraca(uso.Catraca)}, com giro confirmado pelo sensor.",
                "Este ingresso já foi usado. " + ProcureOAtendimento,
                $"Se a pessoa diz que não entrou, confira com a equipe da {Catraca(uso.Catraca)}; nunca libere sem registrar o motivo.")
            : new ExplicacaoDaNegativa(
                $"Este ingresso foi liberado às {Hora(uso.Em)} pela {Catraca(uso.Catraca)}, mas o sensor não confirmou o giro.",
                "Este ingresso já foi usado. " + ProcureOAtendimento,
                $"A liberação anterior não teve giro confirmado: se a pessoa diz que não passou, confira a {Catraca(uso.Catraca)} e, se for o caso, use a liberação manual com o motivo.");
    }

    private static ExplicacaoDaNegativa EmIntervaloDeReuso(ContextoDaNegativa contexto)
    {
        var aconteceu = contexto is { UltimoUso: { } uso, IntervaloDeReuso: { } intervalo } && intervalo > TimeSpan.Zero
            ? $"Este cartão foi usado às {Hora(uso.Em)} pela {Catraca(uso.Catraca)} e só volta a valer às {Hora(uso.Em + intervalo)}."
            : "Este cartão foi usado há pouco e só volta a valer depois do intervalo mínimo entre dois usos.";

        return new ExplicacaoDaNegativa(
            aconteceu,
            "Aguarde alguns minutos para usar este cartão de novo, por favor.",
            "Cartão que volta antes do intervalo pode ter sido passado por cima da grade para outra pessoa. Observe a fila.");
    }

    private static string Hora(DateTimeOffset instante) =>
        HoraDeBrasilia.NoEvento(instante).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Catraca(int numero) =>
        string.Create(CultureInfo.InvariantCulture, $"catraca {numero:D2}");

    private static string Duracao(TimeSpan duracao) =>
        duracao < TimeSpan.FromMinutes(1)
            ? "menos de 1 min"
            : duracao < TimeSpan.FromHours(1)
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)duracao.TotalMinutes} min")
                : string.Create(CultureInfo.InvariantCulture, $"{(int)duracao.TotalHours} h {duracao.Minutes} min");
}
