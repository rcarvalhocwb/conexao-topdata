namespace Desktop.ViewModels;

/// <summary>
/// "Novidades desta versão": o que mudou para quem fica na portaria, mostrado uma vez quando o
/// sistema é atualizado por cima de uma instalação (observação do teste de 10/2026).
/// </summary>
/// <remarks>
/// <para>
/// O número da <see cref="Edicao"/> sobe só quando a atualização tem algo a contar ao operador —
/// não a cada build. Assim o aviso não reaparece a cada 0.1.N sem novidade, e um texto escrito
/// em português de portaria (nada de código interno) explica o que muda no dia a dia.
/// </para>
/// <para>
/// Quem decide mostrar é a <see cref="NovidadesViewModel"/>, comparando esta edição com a última
/// que o operador já viu (gravada por usuário em <c>%LOCALAPPDATA%</c>).
/// </para>
/// </remarks>
public static class Novidades
{
    /// <summary>
    /// A edição do texto abaixo. Suba de um ao trocar as <see cref="Itens"/> por uma atualização
    /// que o operador precisa conhecer; mantenha ao corrigir só build, versão ou coisa interna.
    /// </summary>
    public const int Edicao = 1;

    /// <summary>Título do aviso.</summary>
    public const string Titulo = "Novidades desta versão";

    /// <summary>A frase de abertura, logo abaixo do título e da versão.</summary>
    public const string Abertura =
        "Você atualizou o Rayzer XAcess. Em poucas palavras, o que muda para você na portaria:";

    /// <summary>
    /// Os itens, do ponto de vista de quem opera — o que a pessoa vê e faz, não o que mexemos por
    /// dentro.
    /// </summary>
    public static IReadOnlyList<string> Itens { get; } =
    [
        "Configuração da catraca num lugar só: clique numa peça do desenho da catraca para ver e " +
        "ajustar o que ela faz, e use \"Aplicar nesta catraca\" para mandar tudo de uma vez.",

        "\"Por quê?\" em cada acesso negado: o painel explica o que aconteceu, o que dizer para a " +
        "pessoa e o que fazer — na tela de Acessos e no Painel ao vivo.",

        "Relógio da catraca conferido sozinho, com aviso na hora em que ele sai do horário certo.",

        "Mais comandos por catraca: liberar a passagem na mão com o motivo, mandar um recado no " +
        "visor e refazer a conexão, tudo com registro de quem fez.",

        "Prestação de contas e relatórios mostram o período certinho e dizem \"ainda não " +
        "disponível\" em vez de abrir uma tela vazia.",

        "O Painel ao vivo já traz os acessos que aconteceram antes de você abrir a tela, sem " +
        "repetir nenhum.",
    ];
}
