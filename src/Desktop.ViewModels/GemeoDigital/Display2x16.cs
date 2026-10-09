namespace Desktop.ViewModels.GemeoDigital;

/// <summary>
/// Como um texto de até 32 caracteres aparece no display de 2 linhas × 16 da catraca.
/// </summary>
/// <remarks>
/// A mensagem vai à catraca como um texto só, de até 32 caracteres (docs/32). O display
/// mostra os 16 primeiros na linha de cima e os 16 seguintes na de baixo, sem quebrar por
/// palavra. Por isso "Aproxime o ingresso" aparece como "Aproxime o ingre" / "sso": a prévia
/// mostra isso ao operador antes de ele gravar, e sugere onde pôr espaços.
/// </remarks>
public static class Display2x16
{
    public const int Colunas = 16;

    public const int Linhas = 2;

    public const int Capacidade = Colunas * Linhas;

    /// <summary>
    /// O que a catraca mostra hoje quando nega um acesso, por 3 segundos (ver o laço da
    /// catraca, <c>ExibirNegado</c>). Sem acento de propósito: o display pode não ter.
    /// </summary>
    public const string MensagemDeNegacao = "Acesso nao autorizado";

    /// <summary>Quanto tempo a mensagem de negação fica no display.</summary>
    public static TimeSpan ExibicaoDaNegacao { get; } = TimeSpan.FromSeconds(3);

    /// <summary>As duas linhas, cada uma com exatamente 16 caracteres.</summary>
    public static (string Linha1, string Linha2) Formatar(string? texto)
    {
        var t = Limpar(texto);
        var linha1 = t.Length > Colunas ? t[..Colunas] : t;
        var linha2 = t.Length > Colunas ? t[Colunas..Math.Min(t.Length, Capacidade)] : string.Empty;
        return (linha1.PadRight(Colunas), linha2.PadRight(Colunas));
    }

    /// <summary>
    /// O que pode sair diferente do esperado no display. Lista vazia quando está tudo bem.
    /// </summary>
    public static IReadOnlyList<string> Avisos(string? texto)
    {
        var avisos = new List<string>();
        var t = texto ?? string.Empty;

        if (t.Length > Capacidade)
        {
            avisos.Add($"Tem {t.Length} caracteres; o display mostra só os {Capacidade} primeiros.");
        }

        var limpo = Limpar(t);
        if (limpo.Length > Colunas && limpo[Colunas - 1] != ' ' && limpo[Colunas] != ' ')
        {
            var inicio = limpo.LastIndexOf(' ', Colunas - 1) + 1;
            var fim = limpo.IndexOf(' ', Colunas);
            var palavra = limpo[inicio..(fim < 0 ? limpo.Length : fim)];
            avisos.Add(
                $"A palavra \"{palavra}\" fica cortada entre as duas linhas. " +
                $"Para ela começar na linha de baixo, ponha {Colunas - inicio} espaço(s) antes dela.");
        }

        if (t.Any(c => c > 127))
        {
            avisos.Add("Tem acento ou símbolo: o display pode não mostrar. Ainda a confirmar na bancada.");
        }

        return avisos;
    }

    // Quebra de linha e tabulação viram espaço: o display não tem como mostrá-las.
    private static string Limpar(string? texto) =>
        (texto ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}
