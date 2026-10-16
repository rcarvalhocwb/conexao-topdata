namespace Edge.Supervisor.Atualizacoes;

/// <summary>
/// As chaves <b>públicas</b> que podem assinar a ficha de uma versão estável (docs/60 §2.5).
/// </summary>
/// <remarks>
/// Cada item é a chave pública ECDSA P-256 em base64 (SubjectPublicKeyInfo), como a opção "Gerar a chave de
/// atualização" da <c>Rayzer.Suporte</c> grava em <c>atualizacao-publica.txt</c>. A privada fica só nos
/// segredos do repositório no GitHub, usada pelo workflow "Promover versão estável".
/// Lista vazia: a atualização automática fica desligada e o Diagnóstico diz isso.
/// </remarks>
public static class ChavesDeAtualizacao
{
    /// <summary>As chaves públicas aceitas.</summary>
    public static IReadOnlyList<string> Publicas { get; } = [];
}
