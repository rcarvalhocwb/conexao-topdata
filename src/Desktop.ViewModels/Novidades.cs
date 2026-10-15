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
    public const int Edicao = 5;

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
        "Botão \"Testar conexão com a nuvem\" na tela Sincronização: fala com a nuvem na hora (baixa " +
        "a lista de cartões e envia as passagens da fila) e mostra o resultado e o que fazer.",

        "A lista de cartões vem inteira da nuvem a cada sincronização, mesmo acima de 1.000 cartões.",

        "Boletim do dia (R1): uma nota lembra que ele conta as meias validadas na catraca; a cota " +
        "legal de meia-entrada é apurada pelo sistema de venda.",

        "Prestação de contas com relatórios: R1 boletim do dia por tipo (com reentradas, pico e " +
        "negados por motivo), R2 por hora e tipo, R3 por catraca e R7 consolidado do evento. " +
        "Imprima ou salve em PDF (\"Imprimir / PDF\"), com página numerada e código de conferência.",

        "A nuvem não aparece mais como \"Offline\" quando está conectada: se a lista de cartões " +
        "vier incompleta, o painel diz isso e o que fazer (tela Sincronização).",

        "Login: o olho mostra a senha enquanto você segura o botão, e \"Esqueci minha senha\" " +
        "explica como recuperar (pelo administrador ou, para o único administrador, pelo suporte Rayzer).",

        "Configurações do evento em seis seções, sempre na mesma ordem: Evento (nome, período e hora " +
        "de corte), Tipos de entrada, Cartões da bilheteria, Padrão das catracas, o que ainda não dá " +
        "para configurar (com o motivo) e Avançado. Cada seção tem o próprio botão de salvar.",

        "Sem nuvem (ou antes da primeira sincronização), os cartões já validados podem ser carregados " +
        "de uma vez dos arquivos do sistema anterior (carregar-cartoes.ps1, como administrador).",

        "Período do evento: com a opção ligada, a catraca recusa ingressos lidos antes do início ou " +
        "depois do fim, com o motivo \"fora do período do evento\". A equipe credenciada não é afetada.",

        "\"Gerenciar catraca\" agora fica dentro de \"Configuração da catraca\": a mesma tela tem o " +
        "desenho, a configuração e, embaixo, os comandos (liberar um giro, recado no visor, relógio, " +
        "fechar a catraca) com o histórico. No cartão da catraca, o botão passou a se chamar \"Configurar\".",

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
