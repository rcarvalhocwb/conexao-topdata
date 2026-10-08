using Grpc.Core;

namespace Desktop.ViewModels;

/// <summary>
/// O que o operador lê quando o painel não consegue falar com o serviço local.
/// </summary>
/// <remarks>
/// <para>
/// A causa só é afirmada quando é certa. Se o arquivo do token existe e esta conta não pode lê-lo,
/// a conta não está no grupo dos operadores: é isso que o operador precisa ouvir, e não "o Windows
/// tenta reiniciá-lo" (que não resolve nada e manda o operador fazer a coisa errada).
/// </para>
/// <para>
/// <see cref="TokenSemPermissao"/> é definido uma vez, na partida do painel, a partir do arquivo do
/// token. Não se compara texto de erro: ele muda com o idioma do Windows.
/// </para>
/// </remarks>
public static class MensagemDeFalha
{
    /// <summary>Verdadeiro quando o token existe mas esta conta não pode lê-lo.</summary>
    public static bool TokenSemPermissao { get; set; }

    public const string SemPermissao =
        "Sua conta não tem permissão para falar com o serviço. Peça ao administrador do computador para incluir você " +
        "no grupo \"ConexaoTopdata Operadores\" e entre de novo no Windows.";

    public const string SemResposta =
        "Sem resposta do serviço local. Sem ele, as catracas não recebem comandos deste computador. " +
        "O Windows tenta reiniciá-lo sozinho; se não voltar em um minuto, chame o suporte.";

    /// <summary>A frase para uma falha de chamada ao serviço.</summary>
    public static string Para(StatusCode codigo) =>
        TokenSemPermissao
            ? SemPermissao
            : codigo is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? SemResposta
                : $"O serviço recusou o pedido ({codigo}).";
}
