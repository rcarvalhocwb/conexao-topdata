using Access.Application.Devices;

namespace Edge.Worker;

/// <summary>
/// A frase que o operador lê quando <c>AbrirPortaComunicacao</c> não abre a porta das catracas.
/// </summary>
/// <remarks>
/// Antes, 2 e 3 viravam "A porta não abriu", que não diz o que fazer. Um worker que caiu sem fechar a
/// porta deixa o retorno 3 ("já aberta") para o próximo, e só um operador que sabe onde olhar acha
/// o outro programa. A frase diz qual é o caso e o que fazer.
/// </remarks>
public static class DiagnosticoDaPorta
{
    /// <summary>A frase para o retorno, ou nulo quando o caso não tem frase própria.</summary>
    public static string? Mensagem(AdapterStatus status, int porta) => status switch
    {
        AdapterStatus.PortaJaAberta =>
            $"A porta {porta} já está aberta: outro programa das catracas, ou um worker que não fechou a " +
            "porta, está usando. Feche o outro programa (ou reinicie o serviço do Rayzer XAcess) e tente de novo.",

        AdapterStatus.ErroDeComunicacao =>
            $"Não foi possível abrir a porta {porta} para as catracas. Confira se outro programa usa essa " +
            "porta e se a rede do PC está funcionando.",

        _ => null,
    };
}
