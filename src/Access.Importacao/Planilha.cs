namespace Access.Importacao;

/// <summary>O que a célula era no arquivo — é disso que depende a regra dos zeros.</summary>
public enum TipoDaCelula
{
    /// <summary>Sem valor.</summary>
    Vazia,

    /// <summary>Texto: todo campo de CSV, e a célula de texto do .xlsx (compartilhada ou em linha).</summary>
    Texto,

    /// <summary>Número do .xlsx. O Excel pode já ter tirado zeros à esquerda (docs/26 §3).</summary>
    Numero,

    /// <summary>Fórmula do .xlsx. Não se importa valor calculado.</summary>
    Formula,

    /// <summary>Verdadeiro ou falso do .xlsx.</summary>
    Booleano,

    /// <summary>Erro do Excel (<c>#N/D</c>, <c>#VALOR!</c>).</summary>
    Erro,
}

/// <summary>Uma célula lida.</summary>
/// <remarks>O texto pode ser um código de cartão: <see cref="ToString"/> não o mostra.</remarks>
public readonly record struct Celula(TipoDaCelula Tipo, string Texto)
{
    /// <summary>Célula sem valor.</summary>
    public static Celula Vazia { get; } = new(TipoDaCelula.Vazia, string.Empty);

    /// <summary>Verdadeiro quando não há nada além de espaços.</summary>
    public bool EstaVazia => Tipo == TipoDaCelula.Vazia || string.IsNullOrWhiteSpace(Texto);

    /// <summary>Só o tipo e o tamanho: a célula pode trazer um código de cartão.</summary>
    public override string ToString() => $"{Tipo}({Texto.Length})";
}

/// <summary>Uma linha de dados, com o número que o Excel (ou o editor de texto) mostra.</summary>
/// <param name="Numero">Linha física, contando o cabeçalho como 1.</param>
/// <param name="Celulas">As células, na ordem das colunas.</param>
public sealed record LinhaDaPlanilha(int Numero, IReadOnlyList<Celula> Celulas)
{
    /// <summary>A célula da coluna, ou vazia se a linha é mais curta.</summary>
    public Celula this[int coluna] => coluna >= 0 && coluna < Celulas.Count ? Celulas[coluna] : Celula.Vazia;

    /// <summary>Só o número da linha e quantas células: o conteúdo pode ter código de cartão.</summary>
    public override string ToString() => $"Linha {Numero} ({Celulas.Count} células)";
}

/// <summary>Uma tabela lida: o cabeçalho e as linhas não vazias depois dele.</summary>
/// <param name="Cabecalho">Os nomes das colunas, como vieram.</param>
/// <param name="Linhas">As linhas com algum conteúdo.</param>
/// <param name="LinhasVazias">Linhas vazias entre dados, ignoradas e contadas (as do fim não contam).</param>
/// <param name="Sistema1904">O .xlsx usa o sistema de datas de 1904 (Mac antigo).</param>
public sealed record Planilha(
    IReadOnlyList<string> Cabecalho,
    IReadOnlyList<LinhaDaPlanilha> Linhas,
    int LinhasVazias,
    bool Sistema1904 = false);

/// <summary>O arquivo lido, antes de qualquer regra de cartão.</summary>
/// <param name="Formato"><c>csv</c> ou <c>xlsx</c>, como em <c>import_batch.file_format</c>.</param>
/// <param name="Bytes">Tamanho do arquivo.</param>
/// <param name="Sha256">SHA-256 do arquivo, em hexadecimal minúsculo (reconhece o mesmo arquivo de novo).</param>
/// <param name="Cartoes">A aba Cartões (ou o CSV de cartões).</param>
/// <param name="Tipos">A aba Tipos, quando o .xlsx a tem.</param>
/// <param name="Problemas">Motivos para recusar o arquivo inteiro. Vazio = arquivo legível.</param>
/// <param name="Avisos">Avisos do arquivo, que não impedem a prévia.</param>
public sealed record ArquivoLido(
    string Formato,
    long Bytes,
    string Sha256,
    Planilha? Cartoes,
    Planilha? Tipos,
    IReadOnlyList<string> Problemas,
    IReadOnlyList<string> Avisos)
{
    /// <summary>Verdadeiro quando o arquivo inteiro foi recusado.</summary>
    public bool Recusado => Problemas.Count > 0;

    internal static ArquivoLido Recusa(string formato, long bytes, string sha256, string problema) =>
        new(formato, bytes, sha256, null, null, [problema], []);
}
