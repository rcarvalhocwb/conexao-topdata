using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>Como o painel apresenta a saúde geral.</summary>
public enum SaudeDoPainel
{
    /// <summary>Ainda não houve primeira resposta.</summary>
    Carregando,

    /// <summary>Operando normalmente.</summary>
    Normal,

    /// <summary>Operando com alguma degradação, porém sem ação imediata.</summary>
    Atencao,

    /// <summary>Exige ação do operador.</summary>
    Acao,
}

/// <summary>
/// Estado do painel, já traduzido para a linguagem do operador.
/// </summary>
/// <remarks>
/// Nenhum termo técnico chega à tela sem tradução, e nenhuma mensagem é um código.
/// "Catraca 08 sem comunicação há 22 s — operando com lista local; nenhuma ação
/// imediata" em vez de "Erro 1". Ver docs/GLOSSARIO.md e docs/10-interface.md.
/// </remarks>
public sealed record EstadoDoPainel
{
    public required SaudeDoPainel Saude { get; init; }

    /// <summary>Frase principal, acionável, em português.</summary>
    public required string Mensagem { get; init; }

    /// <summary>Detalhe técnico, para o modo avançado. Fica escondido por padrão.</summary>
    public string? Detalhe { get; init; }

    public int WorkersAtivos { get; init; }

    public int EquipamentosConectados { get; init; }

    public long OutboxPendente { get; init; }

    /// <summary>Quando o serviço local respondeu pela última vez.</summary>
    public DateTimeOffset? AtualizadoEm { get; init; }

    /// <summary>
    /// Verdadeiro quando os números na tela são de uma resposta anterior, porque a
    /// última tentativa falhou.
    /// </summary>
    public bool Desatualizado { get; init; }

    /// <summary>Estado inicial, antes da primeira resposta.</summary>
    public static EstadoDoPainel Carregando() => new()
    {
        Saude = SaudeDoPainel.Carregando,
        Mensagem = "Conectando ao serviço local…",
    };

    /// <summary>Traduz a resposta do serviço para a linguagem do operador.</summary>
    public static EstadoDoPainel De(ObterEstadoResponse resposta, DateTimeOffset em)
    {
        ArgumentNullException.ThrowIfNull(resposta);

        var (saude, mensagem) = resposta.Nivel switch
        {
            // Sem internet é o regime NORMAL de um evento, não um problema.
            // Anunciá-lo como falha treinaria o operador a ignorar alertas.
            NivelDeDegradacao.T1SemInternet => (
                SaudeDoPainel.Normal,
                "Operando sem internet — nenhuma ação necessária"),

            NivelDeDegradacao.T0Normal => (
                SaudeDoPainel.Normal,
                "Tudo funcionando"),

            NivelDeDegradacao.T2ListaLocal => (
                SaudeDoPainel.Atencao,
                "Catracas operando com a lista local — parte do público pode não estar nela"),

            NivelDeDegradacao.T3Isolado => (
                SaudeDoPainel.Acao,
                "Catracas isoladas — verifique a rede do local"),

            _ => (SaudeDoPainel.Atencao, "Estado desconhecido informado pelo serviço"),
        };

        if (resposta.EquipamentosConectados == 0)
        {
            saude = SaudeDoPainel.Acao;
            mensagem = "Nenhuma catraca conectada — verifique a rede e a alimentação";
        }

        return new EstadoDoPainel
        {
            Saude = saude,
            Mensagem = mensagem,
            Detalhe = string.Create(
                CultureInfo.InvariantCulture,
                $"versão {resposta.Versao} · nível {resposta.Nivel} · outbox {resposta.OutboxPendente}"),
            WorkersAtivos = resposta.WorkersAtivos,
            EquipamentosConectados = resposta.EquipamentosConectados,
            OutboxPendente = resposta.OutboxPendente,
            AtualizadoEm = em,
        };
    }

    /// <summary>
    /// Estado quando o serviço local não respondeu.
    /// </summary>
    /// <remarks>
    /// O aplicativo continua aberto e mostrando o que sabia. Fechar ou travar a tela
    /// porque o serviço caiu tiraria do operador justamente a informação de que algo
    /// caiu.
    /// </remarks>
    public EstadoDoPainel ComFalhaDeComunicacao(string detalhe, DateTimeOffset agora)
    {
        var idade = AtualizadoEm is { } quando ? agora - quando : (TimeSpan?)null;

        var mensagem = idade is { } tempo
            ? $"Sem resposta do serviço local — mostrando dados de {Descrever(tempo)} atrás"
            : "Sem resposta do serviço local — verifique se o serviço está em execução";

        return this with
        {
            Saude = SaudeDoPainel.Acao,
            Mensagem = mensagem,
            Detalhe = detalhe,
            Desatualizado = true,
        };
    }

    private static string Descrever(TimeSpan tempo) => tempo.TotalSeconds switch
    {
        < 60 => $"{tempo.TotalSeconds:F0} s",
        < 3600 => $"{tempo.TotalMinutes:F0} min",
        _ => $"{tempo.TotalHours:F0} h",
    };
}
