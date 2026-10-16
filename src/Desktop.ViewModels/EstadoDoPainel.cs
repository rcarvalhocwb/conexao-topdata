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

    /// <summary>
    /// A tela mostra o botão "Abrir assistente de configuração": instalação sem
    /// configuração, ou serviço que nunca respondeu (logo depois de instalar).
    /// </summary>
    public bool SugerirAssistente { get; init; }

    /// <summary>Instalação em modo simulação: a tela mostra o selo o tempo todo.</summary>
    public bool Simulacao { get; init; }

    /// <summary>
    /// A atualização automática vai instalar em instantes (docs/60 §2.3): o painel deixa o relançador e fecha,
    /// para o instalador não precisar derrubá-lo.
    /// </summary>
    public bool AtualizacaoIminente { get; init; }

    public int WorkersAtivos { get; init; }

    /// <summary>Catracas físicas atendendo (P0-01). Nunca inclui simuladas.</summary>
    public int EquipamentosConectados { get; init; }

    /// <summary>Catracas simuladas ativas, separadas das físicas.</summary>
    public int CatracasSimuladasAtivas { get; init; }

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
                "Catraca sem comunicação com este computador — siga o procedimento de contingência do evento"),

            NivelDeDegradacao.T3Isolado => (
                SaudeDoPainel.Acao,
                "Catracas isoladas — verifique a rede do local"),

            _ => (SaudeDoPainel.Atencao, "Estado desconhecido informado pelo serviço"),
        };

        // As catracas funcionam, mas a nuvem precisa de alguém: "tudo funcionando" esconderia isso.
        if (saude is SaudeDoPainel.Normal && resposta.SituacaoDaNuvem is SituacaoDaNuvem.ListaIncompleta
                or SituacaoDaNuvem.FalhaDeAutenticacao or SituacaoDaNuvem.SegredoAusente)
        {
            saude = SaudeDoPainel.Atencao;
            mensagem = "Catracas funcionando — a nuvem precisa de atenção (veja Sincronização)";
        }

        // A licença nunca para catraca (docs/60): aviso na barra só quando tudo o mais está bem.
        if (saude is SaudeDoPainel.Normal && AvisoDaLicenca(resposta.SituacaoDaLicenca, resposta.DiasDaLicenca) is { } aviso)
        {
            saude = SaudeDoPainel.Atencao;
            mensagem = aviso;
        }

        if (resposta.SemConfiguracao)
        {
            saude = SaudeDoPainel.Acao;
            mensagem = "Instalação ainda não configurada — abra o Assistente de configuração";
        }
        else if (resposta.EquipamentosConectados == 0 && resposta.CatracasSimuladasAtivas == 0)
        {
            saude = SaudeDoPainel.Acao;

            // Com o motivo do worker que caiu, a barra diz o que falta (ex.: a DLL); sem ele, o de antes.
            mensagem = string.IsNullOrWhiteSpace(resposta.MotivoDaCatraca)
                ? "Nenhuma catraca conectada — verifique a rede e a alimentação"
                : $"Nenhuma catraca conectada: o programa das catracas não está atendendo ({resposta.MotivoDaCatraca})";
        }

        if (resposta.Simulacao)
        {
            // Impossível confundir com operação real: vem antes de qualquer outra frase.
            mensagem = "MODO SIMULAÇÃO (sem catraca física) — " + mensagem;

            // Simulação é tom de Atenção (docs/27), nunca de sucesso: a barra saía verde,
            // com "✓", ao lado do cartão laranja de simulação — "tudo certo" numa instalação
            // que não aciona catraca nenhuma (docs/34 §7.1, P11). Ação continua Ação.
            if (saude is SaudeDoPainel.Normal)
            {
                saude = SaudeDoPainel.Atencao;
            }
        }

        return new EstadoDoPainel
        {
            Saude = saude,
            Mensagem = mensagem,
            SugerirAssistente = resposta.SemConfiguracao,
            Simulacao = resposta.Simulacao,
            AtualizacaoIminente = resposta.AtualizacaoIminente,
            Detalhe = string.Create(
                CultureInfo.InvariantCulture,
                $"versão {resposta.Versao} · nível {resposta.Nivel} · outbox {resposta.OutboxPendente}"),
            WorkersAtivos = resposta.WorkersAtivos,
            EquipamentosConectados = resposta.EquipamentosConectados,
            CatracasSimuladasAtivas = resposta.CatracasSimuladasAtivas,
            OutboxPendente = resposta.OutboxPendente,
            AtualizadoEm = em,
        };
    }

    /// <summary>A frase da barra para a licença; nulo quando não há o que avisar.</summary>
    public static string? AvisoDaLicenca(SituacaoDaLicenca situacao, int dias) => situacao switch
    {
        SituacaoDaLicenca.VenceEmBreve => dias == 0
            ? "Catracas funcionando — a licença vence hoje (veja Diagnóstico)"
            : $"Catracas funcionando — a licença vence em {Dias(dias)} (veja Diagnóstico)",
        SituacaoDaLicenca.EmTolerancia =>
            $"Catracas funcionando — licença vencida: renove em até {Dias(dias)} (veja Diagnóstico)",
        SituacaoDaLicenca.Bloqueada =>
            "Catracas funcionando — licença vencida: relatórios e atualizações bloqueados (veja Diagnóstico)",
        _ => null,
    };

    private static string Dias(int n) => n == 1 ? "1 dia" : string.Create(CultureInfo.InvariantCulture, $"{n} dias");

    /// <summary>
    /// Estado quando o serviço local não respondeu.
    /// </summary>
    /// <remarks>
    /// O aplicativo continua aberto e mostrando o que sabia. Fechar ou travar a tela
    /// porque o serviço caiu tiraria do operador justamente a informação de que algo
    /// caiu.
    /// </remarks>
    public EstadoDoPainel ComFalhaDeComunicacao(string detalhe, DateTimeOffset agora, bool semPermissao = false)
    {
        var idade = AtualizadoEm is { } quando ? agora - quando : (TimeSpan?)null;

        // Sem permissão, a causa é certa e a ação também: o resto da mensagem não ajuda.
        var mensagem = semPermissao
            ? MensagemDeFalha.SemPermissao
            : idade is { } tempo
                ? $"Sem resposta do serviço local — mostrando dados de {Descrever(tempo)} atrás"
                : "Sem resposta do serviço local — o Windows tenta reiniciá-lo sozinho; se não voltar em um minuto, chame o suporte";

        return this with
        {
            Saude = SaudeDoPainel.Acao,
            Mensagem = mensagem,
            Detalhe = detalhe,
            Desatualizado = true,
            SugerirAssistente = idade is null || SugerirAssistente,
        };
    }

    private static string Descrever(TimeSpan tempo) => tempo.TotalSeconds switch
    {
        < 60 => $"{tempo.TotalSeconds:F0} s",
        < 3600 => $"{tempo.TotalMinutes:F0} min",
        _ => $"{tempo.TotalHours:F0} h",
    };
}
