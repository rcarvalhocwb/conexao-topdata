using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>
/// Como a situação técnica aparece para o operador: português de portaria, sem jargão.
/// </summary>
public static class Textos
{
    /// <summary>Situação de uma catraca em uma frase curta, e a cor dela.</summary>
    public static (string Texto, Sinal Sinal) SituacaoDaCatraca(Equipamento equipamento)
    {
        ArgumentNullException.ThrowIfNull(equipamento);

        if (equipamento.EmOperacao)
        {
            return ("Atendendo", Sinal.Bom);
        }

        var estado = equipamento.Estado ?? string.Empty;

        if (estado.StartsWith("sem notícia", StringComparison.Ordinal))
        {
            return ("Sem notícia do programa da catraca", Sinal.Problema);
        }

        return estado switch
        {
            "aguardando a catraca" => ("Aguardando a catraca conectar", Sinal.Neutro),
            "Discovering" or "Conectar" or "Reconectar" => ("Conectando…", Sinal.Atencao),
            "LendoIdentidade" or "VerificandoCompatibilidade" or "EnviarCfgOffline"
                or "EnviarConfigMudOnlineOffline" or "EnviarCfgOnline" or "ConfigurarEntradasOnline"
                or "EnviarMsgPadrao" or "SincronizandoDadosOffline" => ("Configurando…", Sinal.Atencao),
            "OfflineAutonomo" => ("Operando sozinha, sem o PC", Sinal.Atencao),
            "Degradado" => ("Com problema — veja o diagnóstico", Sinal.Problema),
            "Disabled" => ("Desligada", Sinal.Neutro),
            "Morto" => ("Programa da catraca parou", Sinal.Problema),
            "Quarentena" => ("Programa da catraca parou várias vezes", Sinal.Problema),
            "SemBatimento" => ("Programa da catraca não responde", Sinal.Problema),
            "Parado" => ("Parada", Sinal.Neutro),
            _ => (estado, Sinal.Atencao),
        };
    }

    /// <summary>"há 5 s", "há 3 min", "há 2 h" — ou "nunca".</summary>
    public static string Ha(DateTimeOffset? quando, DateTimeOffset agora)
    {
        if (quando is not { } q || q == DateTimeOffset.UnixEpoch)
        {
            return "nunca";
        }

        var passou = agora - q;

        if (passou < TimeSpan.Zero)
        {
            passou = TimeSpan.Zero;
        }

        return passou.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"há {passou.TotalSeconds:F0} s")
            : passou.TotalMinutes < 60
                ? string.Create(CultureInfo.InvariantCulture, $"há {passou.TotalMinutes:F0} min")
                : string.Create(CultureInfo.InvariantCulture, $"há {passou.TotalHours:F0} h");
    }

    /// <summary>Hora local no formato do painel.</summary>
    public static string Hora(DateTimeOffset quando) =>
        quando.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>Uma catraca, pronta para a tela.</summary>
public sealed record LinhaDeCatraca(
    int Inner,
    string Nome,
    string Situacao,
    Sinal Sinal,
    string UltimaDecisao,
    string UltimoEvento,
    string Firmware,
    string Grupo,
    int Porta,
    int Reconexoes);

/// <summary>Um acesso, pronto para a tela. O código já vem mascarado do serviço.</summary>
public sealed record LinhaDeAcesso(
    string Hora,
    int Inner,
    string Mensagem,
    bool Liberado,
    bool Girou,
    string Categoria,
    string Codigo,
    Sinal Sinal)
{
    public static LinhaDeAcesso De(EventoDeAcesso e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var liberado = e.Resultado == ResultadoDoAcesso.Permitido;

        return new LinhaDeAcesso(
            e.RecebidoEm is null ? string.Empty : Textos.Hora(e.RecebidoEm.ToDateTimeOffset()),
            e.Inner,
            e.MensagemAoOperador,
            liberado,
            e.PassagemConfirmada,
            e.Categoria,
            e.CredencialMascarada,
            liberado ? Sinal.Bom : Sinal.Problema);
    }
}
