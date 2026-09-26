using System.Security.Cryptography;
using System.Text;

namespace Relay.Ingressos;

/// <summary>
/// Comparação de segredo sem vazar o tamanho da coincidência pelo tempo.
/// </summary>
/// <remarks>
/// A URL do webhook <b>é</b> a credencial — a tela de cadastro do provedor não oferece
/// campo de assinatura. Isso faz da comparação deste token a única barreira entre a base
/// de ingressos e qualquer um que descubra ou adivinhe o endereço, o que torna a
/// comparação em tempo constante obrigatória e não um preciosismo.
/// Ver docs/ADR/ADR-0022-rele-de-webhook.md
/// </remarks>
internal static class Segredos
{
    internal static bool Conferem(string? informado, string esperado)
    {
        if (string.IsNullOrEmpty(informado) || string.IsNullOrEmpty(esperado))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(informado),
            Encoding.UTF8.GetBytes(esperado));
    }

    /// <summary>
    /// Recusa segredo curto demais para ser levado a sério.
    /// </summary>
    /// <remarks>
    /// 32 caracteres porque o token vai na URL, e URL aparece em log de servidor, em
    /// histórico de navegador e em captura de tela — a rotação precisa ser possível, e a
    /// adivinhação, inviável.
    /// </remarks>
    internal static string Exigir(string? valor, string nomeDaVariavel)
    {
        if (string.IsNullOrWhiteSpace(valor) || valor.Length < 32)
        {
            throw new InvalidOperationException(
                $"A variável de ambiente {nomeDaVariavel} precisa ter ao menos 32 caracteres. " +
                "O relé não sobe sem ela: subir com segredo fraco é pior que não subir.");
        }

        return valor;
    }
}
