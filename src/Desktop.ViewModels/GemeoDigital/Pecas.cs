namespace Desktop.ViewModels.GemeoDigital;

/// <summary>As peças da TopFit 4 que o gêmeo digital desenha e que o operador pode escolher.</summary>
/// <remarks>
/// O nome de cada valor é também o nome do objeto no modelo 3D. Quando o modelo desenhado
/// por código for trocado por um arquivo feito no Blender, os objetos do arquivo precisam
/// ter estes mesmos nomes. Ver docs/33-gemeo-digital.md, seção 5.
/// </remarks>
public enum PecaDaCatraca
{
    /// <summary>Chapa de fixação no piso.</summary>
    Base,

    /// <summary>Coluna metálica que sustenta a cabeça da catraca.</summary>
    Coluna,

    /// <summary>Tampa da cabeça, onde ficam display, teclado e leitores.</summary>
    Tampa,

    /// <summary>Mecanismo de giro com os três braços de aço inox.</summary>
    Rotor,

    /// <summary>Display de duas linhas de 16 caracteres, com luz de fundo.</summary>
    Display,

    /// <summary>Teclado numérico de 16 teclas.</summary>
    Teclado,

    /// <summary>Leitor de QR Code e código de barras, integrado na tampa.</summary>
    LeitorQr,

    /// <summary>Leitor de cartão por aproximação da frente (leitor 1).</summary>
    LeitorDeProximidade,

    /// <summary>Fenda da urna e o leitor dela (leitor 2).</summary>
    Urna,

    /// <summary>Pictograma luminoso de passagem liberada.</summary>
    SinalLiberado,

    /// <summary>Pictograma luminoso de passagem bloqueada.</summary>
    SinalBloqueado,

    /// <summary>Leitor facial sobre haste. Existe na variante Facial; fora do escopo hoje.</summary>
    LeitorFacial,
}

/// <summary>O quanto uma função da peça existe de verdade no Rayzer XAcess hoje.</summary>
public enum SituacaoDaFuncao
{
    /// <summary>O sistema faz, e o gêmeo mostra o que acontece.</summary>
    Disponivel,

    /// <summary>
    /// Documentado ou prometido, mas ainda não confirmado pela Topdata ou pela bancada. O
    /// gêmeo mostra a peça, mas não finge que a função existe.
    /// </summary>
    AguardandoConfirmacao,

    /// <summary>O equipamento tem, mas esta instalação não usa.</summary>
    NaoUsadaNestaInstalacao,

    /// <summary>Fora do escopo atual do produto (decisão registrada).</summary>
    ForaDoEscopo,
}

/// <summary>Uma função de uma peça, com a situação dela e o porquê em português simples.</summary>
public sealed record FuncaoDaPeca(string Nome, SituacaoDaFuncao Situacao, string Explicacao)
{
    /// <summary>Selo curto para a tela.</summary>
    public string Selo => Situacao switch
    {
        SituacaoDaFuncao.Disponivel => "Disponível",
        SituacaoDaFuncao.AguardandoConfirmacao => "Aguardando confirmação",
        SituacaoDaFuncao.NaoUsadaNestaInstalacao => "Não usada aqui",
        _ => "Fora do escopo",
    };

    /// <summary>Cor do selo, pelo mesmo sinal do resto do painel.</summary>
    public Sinal Sinal => Situacao switch
    {
        SituacaoDaFuncao.Disponivel => Sinal.Bom,
        SituacaoDaFuncao.AguardandoConfirmacao => Sinal.Atencao,
        _ => Sinal.Neutro,
    };
}

/// <summary>O que a tela mostra quando o operador escolhe uma peça.</summary>
/// <param name="Peca">A peça.</param>
/// <param name="Nome">Nome em português de operador.</param>
/// <param name="OQueFaz">Uma ou duas frases sobre a peça.</param>
/// <param name="Funcoes">O que dá para fazer com ela, e se existe de verdade.</param>
/// <param name="OndeMexer">Em que tela do painel se mexe nisso, quando se mexe.</param>
public sealed record FichaDaPeca(
    PecaDaCatraca Peca,
    string Nome,
    string OQueFaz,
    IReadOnlyList<FuncaoDaPeca> Funcoes,
    string OndeMexer);

/// <summary>
/// As fichas das peças da TopFit 4, com o que cada uma faz <b>neste sistema</b>.
/// </summary>
/// <remarks>
/// A regra é a mesma da tela "Gerenciar catraca" (docs/32, seção 5): o que depende da
/// Topdata ou da bancada aparece com o selo "Aguardando confirmação" e o motivo. Um gêmeo
/// digital que mostrasse a urna recolhendo cartão ensinaria o operador a esperar uma coisa
/// que a catraca desta instalação ainda não faz.
/// </remarks>
public static class CatalogoDaFit4
{
    private static readonly Dictionary<PecaDaCatraca, FichaDaPeca> Fichas = Montar();

    /// <summary>Todas as peças, na ordem em que aparecem na lista da tela.</summary>
    public static IReadOnlyList<FichaDaPeca> Pecas { get; } =
    [
        Fichas[PecaDaCatraca.Rotor],
        Fichas[PecaDaCatraca.Display],
        Fichas[PecaDaCatraca.LeitorQr],
        Fichas[PecaDaCatraca.LeitorDeProximidade],
        Fichas[PecaDaCatraca.Urna],
        Fichas[PecaDaCatraca.Teclado],
        Fichas[PecaDaCatraca.SinalLiberado],
        Fichas[PecaDaCatraca.SinalBloqueado],
        Fichas[PecaDaCatraca.LeitorFacial],
        Fichas[PecaDaCatraca.Tampa],
        Fichas[PecaDaCatraca.Coluna],
        Fichas[PecaDaCatraca.Base],
    ];

    /// <summary>A ficha de uma peça.</summary>
    public static FichaDaPeca De(PecaDaCatraca peca) => Fichas[peca];

    private static Dictionary<PecaDaCatraca, FichaDaPeca> Montar()
    {
        const string gerenciar = "Gerenciar catraca";
        const string configuracoes = "Configurações";
        const string nenhuma = "Não há ajuste pelo painel.";

        var lista = new[]
        {
            new FichaDaPeca(
                PecaDaCatraca.Rotor,
                "Braços e mecanismo de giro",
                "Três braços de aço inox. A cada passagem o conjunto gira um terço de volta e trava de novo. " +
                "Só o giro lido pelo sensor da catraca conta como passagem de verdade.",
                [
                    new("Liberar um giro de entrada", SituacaoDaFuncao.Disponivel,
                        "Pelo ingresso válido, ou pela liberação manual com motivo."),
                    new("Confirmar a passagem pelo sensor de giro", SituacaoDaFuncao.Disponivel,
                        "A passagem só é contada quando a catraca avisa o giro (origem 6). Liberado sem giro não conta."),
                    new("Liberar nos dois sentidos / trocar o sentido", SituacaoDaFuncao.AguardandoConfirmacao,
                        "Permite carona; depende da Topdata e da decisão sobre evacuação (B4)."),
                    new("Queda dos braços em emergência", SituacaoDaFuncao.AguardandoConfirmacao,
                        "Não documentada para a TopFit 4 desta instalação. O gêmeo não simula."),
                ],
                gerenciar),
            new FichaDaPeca(
                PecaDaCatraca.Display,
                "Display",
                "Duas linhas de 16 caracteres, com luz de fundo. Mostra a mensagem padrão quando a catraca está livre.",
                [
                    new("Mensagem padrão", SituacaoDaFuncao.Disponivel,
                        "Até 32 caracteres. Vale para todas as catracas; entra na próxima conexão."),
                    new("Mensagem temporária", SituacaoDaFuncao.Disponivel,
                        "Até 32 caracteres, de 1 a 60 segundos, numa catraca."),
                    new("Letras com acento", SituacaoDaFuncao.AguardandoConfirmacao,
                        "O display pode não mostrar acentos. Confirmar na bancada."),
                ],
                $"{configuracoes} (mensagem padrão) e {gerenciar} (mensagem temporária)"),
            new FichaDaPeca(
                PecaDaCatraca.LeitorQr,
                "Leitor de QR Code",
                "Integrado na tampa. Lê o QR impresso ou na tela do celular, por aproximação.",
                [
                    new("Ler o QR do ingresso", SituacaoDaFuncao.Disponivel,
                        "O código vai para a decisão de acesso da borda, a mesma da operação."),
                    new("QR de 4 a 16 caracteres", SituacaoDaFuncao.Disponivel,
                        "Limite da placa da catraca, não do leitor. Ingresso fora disso é recusado na importação."),
                ],
                configuracoes),
            new FichaDaPeca(
                PecaDaCatraca.LeitorDeProximidade,
                "Leitor de cartão da frente",
                "Leitor 1, por aproximação. Um cartão lido aqui é aceito ou recusado conforme a regra do provedor.",
                [
                    new("Ler cartão na frente", SituacaoDaFuncao.Disponivel,
                        "Cartão da bilheteria com a regra \"só na urna\" é recusado aqui, de propósito."),
                ],
                configuracoes),
            new FichaDaPeca(
                PecaDaCatraca.Urna,
                "Urna",
                "Fenda com leitor próprio (leitor 2). O cartão da bilheteria é lido na fenda.",
                [
                    new("Ler o cartão na fenda", SituacaoDaFuncao.Disponivel,
                        "O leitor da urna decide como qualquer leitor. Ligado ou desligado em Configurações."),
                    new("Recolher o cartão", SituacaoDaFuncao.AguardandoConfirmacao,
                        "A função do relé 2 não está documentada pela Topdata (docs/21, seção 8). A urna ainda não engole o cartão."),
                    new("Aviso de urna cheia", SituacaoDaFuncao.AguardandoConfirmacao,
                        "A origem 20 é recebida e guardada, mas o aviso na tela ainda não foi ensaiado na bancada."),
                ],
                configuracoes),
            new FichaDaPeca(
                PecaDaCatraca.Teclado,
                "Teclado",
                "Teclado numérico de 16 teclas.",
                [
                    new("Digitar o código", SituacaoDaFuncao.NaoUsadaNestaInstalacao,
                        "Nesta instalação o acesso é por QR e cartão."),
                ],
                nenhuma),
            new FichaDaPeca(
                PecaDaCatraca.SinalLiberado,
                "Sinal de liberado",
                "Pictograma luminoso na tampa. Quem acende é a própria catraca, quando libera o giro.",
                [
                    new("Acender ao liberar", SituacaoDaFuncao.Disponivel,
                        "Automático da catraca. O gêmeo acende junto para mostrar o momento."),
                ],
                nenhuma),
            new FichaDaPeca(
                PecaDaCatraca.SinalBloqueado,
                "Sinal de bloqueado",
                "Pictograma luminoso na tampa, aceso quando a catraca recusa.",
                [
                    new("Acender ao negar", SituacaoDaFuncao.Disponivel,
                        "Automático da catraca, junto com a mensagem de negação no display."),
                ],
                nenhuma),
            new FichaDaPeca(
                PecaDaCatraca.LeitorFacial,
                "Leitor facial",
                "Existe na variante Facial da TopFit 4, sobre uma haste. Usa outro SDK (WebSocket), separado da EasyInner.",
                [
                    new("Reconhecimento facial", SituacaoDaFuncao.ForaDoEscopo,
                        "Fase 5, só com base legal definida (B9). Ver docs/13 e ADR-0011."),
                ],
                nenhuma),
            new FichaDaPeca(
                PecaDaCatraca.Tampa,
                "Tampa",
                "Cabeça da catraca, onde ficam display, teclado, leitores e sinais.",
                [],
                nenhuma),
            new FichaDaPeca(
                PecaDaCatraca.Coluna,
                "Coluna",
                "Pedestal metálico. Por dentro passam a placa de controle e os cabos.",
                [],
                nenhuma),
            new FichaDaPeca(
                PecaDaCatraca.Base,
                "Base",
                "Chapa parafusada no piso.",
                [],
                nenhuma),
        };

        return lista.ToDictionary(f => f.Peca);
    }
}
