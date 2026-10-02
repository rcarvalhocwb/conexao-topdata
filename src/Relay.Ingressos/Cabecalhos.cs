namespace Relay.Ingressos;

/// <summary>Quais cabeçalhos da entrega podem ser preservados.</summary>
/// <remarks>
/// Os cabeçalhos são guardados porque são prova de quem entregou e quando — vale numa
/// discussão de prestação de contas. Mas guardar <b>tudo</b> significa guardar a
/// credencial que o provedor usou, em texto, num banco que alguém vai abrir para
/// diagnosticar. O filtro é por isso, e é por conter e não por lista fechada: cabeçalho
/// novo com nome de segredo aparece sozinho, e o padrão precisa ser recusar.
/// </remarks>
internal static class Cabecalhos
{
    private static readonly string[] Proibidos =
    [
        "authorization",
        "cookie",
        "token",
        "secret",
        "api-key",
        "apikey",
        "password",
        "senha",
    ];

    internal static bool EhSensivel(string cabecalho) =>
        !string.IsNullOrEmpty(cabecalho)
        && Array.Exists(Proibidos, p => cabecalho.Contains(p, StringComparison.OrdinalIgnoreCase));
}
