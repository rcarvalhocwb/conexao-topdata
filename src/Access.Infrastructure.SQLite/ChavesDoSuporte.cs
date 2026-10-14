namespace Access.Infrastructure.SQLite;

/// <summary>
/// As chaves <b>públicas</b> do suporte Rayzer que podem assinar uma recuperação de senha (docs/58).
/// </summary>
/// <remarks>
/// <para>
/// Cada item é a chave pública ECDSA P-256 em base64 (SubjectPublicKeyInfo), como a ferramenta
/// <c>Rayzer.Suporte gerar-chaves</c> imprime. Chave pública pode estar no repositório público: ela só
/// confere assinaturas, não assina. A chave <b>privada</b> fica só com o desenvolvedor, fora do
/// repositório e do instalador.
/// </para>
/// <para>
/// Lista vazia: a recuperação pelo suporte fica desligada e a tela diz isso. Para trocar de chave
/// (perda ou vazamento da privada), acrescente a nova, publique, e depois tire a antiga.
/// </para>
/// </remarks>
public static class ChavesDoSuporte
{
    /// <summary>As chaves públicas aceitas.</summary>
    public static IReadOnlyList<string> Publicas { get; } = [];
}
