using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Access.Importacao;

/// <summary>
/// Lê o .xlsx do modelo (docs/26) só com a biblioteca padrão: o pacote com
/// <see cref="ZipArchive"/> e as partes com <see cref="XmlReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// O ponto do leitor é <b>não perder o tipo da célula</b>. No .xlsx dá para provar que o
/// código foi guardado como número (o Excel pode ter tirado os zeros à esquerda) — e isso
/// é erro, mesmo que o valor "pareça certo" (docs/26 §3; docs/34-anexos/03 §3.4 e §3.5).
/// Texto compartilhado (<c>t="s"</c>) e texto em linha (<c>t="inlineStr"</c>) passam como
/// vieram.
/// </para>
/// <para>
/// O arquivo vem de fora e não é confiável: o pacote tem limite de tamanho descompactado
/// e de entradas, macro e vínculo externo são recusados, e o XML não resolve DTD nem
/// entidade externa.
/// </para>
/// <para>
/// Abas por nome: "Cartões" (ou "Cartoes") e "Tipos". "Exemplo" e "Instruções" nunca são
/// lidas (docs/26 §5). Uma pasta com uma aba só é lida como a de cartões, com aviso.
/// </para>
/// </remarks>
public static class LeitorDeXlsx
{
    // Relações no OOXML de transição (o que o Excel salva) e no estrito.
    private const string Relacoes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelacoesEstrito = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private static readonly XmlReaderSettings Seguro = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = true,
    };

    /// <summary>Lê o pacote inteiro.</summary>
    /// <param name="arquivo">O .xlsx.</param>
    /// <param name="abaPrincipal">
    /// O nome da aba de dados: "Cartões" no cadastro de cartões, "Pessoas" no de pessoas (docs/43 P5).
    /// Volta em <see cref="ArquivoLido.Cartoes"/> nos dois casos.
    /// </param>
    public static ArquivoLido Ler(Stream arquivo, string abaPrincipal = "Cartões")
    {
        ArgumentNullException.ThrowIfNull(arquivo);

        var conteudo = Apoio.LerAteOLimite(arquivo, LimitesDaImportacao.BytesDoArquivo);
        if (conteudo is null)
        {
            return ArquivoLido.Recusa("xlsx", LimitesDaImportacao.BytesDoArquivo, string.Empty,
                $"O arquivo passa de {LimitesDaImportacao.BytesDoArquivo / (1024 * 1024)} MB. Divida-o em partes.");
        }

        var sha = Apoio.Sha256(conteudo);
        try
        {
            using var pacote = new ZipArchive(new MemoryStream(conteudo, writable: false), ZipArchiveMode.Read);
            return LerPacote(pacote, conteudo.LongLength, sha, abaPrincipal);
        }
        catch (PacoteGrandeDemais)
        {
            return ArquivoLido.Recusa("xlsx", conteudo.LongLength, sha, "O pacote do .xlsx é grande demais depois de descompactado.");
        }
        catch (Exception e) when (e is InvalidDataException or XmlException or FormatException or OverflowException)
        {
            return ArquivoLido.Recusa("xlsx", conteudo.LongLength, sha,
                "O arquivo não é uma planilha .xlsx legível. Abra no Excel e salve de novo como \"Pasta de Trabalho do Excel (.xlsx)\".");
        }
    }

    private static ArquivoLido LerPacote(ZipArchive pacote, long bytes, string sha, string abaPrincipal)
    {
        if (pacote.Entries.Count > LimitesDaImportacao.EntradasDoPacote
            || pacote.Entries.Sum(e => e.Length) > LimitesDaImportacao.BytesDescompactados)
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha, "O pacote do .xlsx é grande demais depois de descompactado.");
        }

        if (pacote.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha, "A planilha tem macro. Salve como .xlsx (sem macro).");
        }

        if (pacote.Entries.Any(e => e.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)))
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha,
                "A planilha tem vínculo com outro arquivo. Copie e cole só os valores numa planilha nova.");
        }

        var orcamento = new Orcamento(LimitesDaImportacao.BytesDescompactados);

        if (Parte(pacote, "xl/workbook.xml") is not { } pastaDeTrabalho)
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha, "O arquivo não é uma planilha .xlsx (falta a pasta de trabalho).");
        }

        var (abas, sistema1904) = LerPastaDeTrabalho(pastaDeTrabalho, orcamento);
        var alvos = LerRelacoes(Parte(pacote, "xl/_rels/workbook.xml.rels"), orcamento);
        var compartilhadas = Parte(pacote, "xl/sharedStrings.xml") is { } sst ? LerTextosCompartilhados(sst, orcamento) : [];

        var avisos = new List<string>();
        var ignoradas = new HashSet<string>(StringComparer.Ordinal) { "exemplo", "instrucoes" };

        var chaveDaPrincipal = Apoio.Chave(abaPrincipal);
        var cartoes = abas.Where(a => Apoio.Chave(a.Nome) == chaveDaPrincipal).ToList();
        var tipos = abas.Where(a => Apoio.Chave(a.Nome) is "tipos").ToList();
        if (cartoes.Count > 1 || tipos.Count > 1)
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha, $"A planilha tem mais de uma aba {abaPrincipal} ou Tipos.");
        }

        if (cartoes.Count == 0)
        {
            var candidatas = abas.Where(a => !ignoradas.Contains(Apoio.Chave(a.Nome)) && Apoio.Chave(a.Nome) != "tipos").ToList();
            if (candidatas.Count != 1)
            {
                return ArquivoLido.Recusa("xlsx", bytes, sha,
                    chaveDaPrincipal == "cartoes"
                        ? "A planilha não tem a aba \"Cartões\". Use o modelo (docs/26) ou renomeie a aba dos cartões."
                        : $"A planilha não tem a aba \"{abaPrincipal}\". Use o modelo ou renomeie a aba.");
            }

            avisos.Add($"A planilha não tem a aba \"{abaPrincipal}\"; foi lida a aba \"{candidatas[0].Nome}\".");
            cartoes = candidatas;
        }

        Planilha? LerAbaDoPacote((string Nome, string Id) aba)
        {
            if (!alvos.TryGetValue(aba.Id, out var alvo) || Parte(pacote, alvo) is not { } parte)
            {
                return null;
            }

            return LerAba(parte, compartilhadas, sistema1904, orcamento);
        }

        var planilhaDeCartoes = LerAbaDoPacote(cartoes[0]);
        if (planilhaDeCartoes is null)
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha, $"A aba {abaPrincipal} está vazia ou ilegível.");
        }

        if (planilhaDeCartoes.Linhas.Count > LimitesDaImportacao.LinhasMaximas)
        {
            return ArquivoLido.Recusa("xlsx", bytes, sha, string.Create(CultureInfo.InvariantCulture,
                $"A planilha passa de {LimitesDaImportacao.LinhasMaximas} linhas. Divida-a em partes."));
        }

        var planilhaDeTipos = tipos.Count == 1 ? LerAbaDoPacote(tipos[0]) : null;
        return new ArquivoLido("xlsx", bytes, sha, planilhaDeCartoes, planilhaDeTipos, [], avisos);
    }

    private static ZipArchiveEntry? Parte(ZipArchive pacote, string caminho) =>
        pacote.GetEntry(caminho) ?? pacote.Entries.FirstOrDefault(e => string.Equals(e.FullName, caminho, StringComparison.OrdinalIgnoreCase));

    private static XmlReader Abrir(ZipArchiveEntry parte, Orcamento orcamento) =>
        XmlReader.Create(new FluxoLimitado(parte.Open(), orcamento), Seguro);

    private static (List<(string Nome, string Id)> Abas, bool Sistema1904) LerPastaDeTrabalho(ZipArchiveEntry parte, Orcamento orcamento)
    {
        var abas = new List<(string, string)>();
        var sistema1904 = false;
        using var leitor = Abrir(parte, orcamento);
        while (leitor.Read())
        {
            if (leitor.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (leitor.LocalName == "workbookPr")
            {
                var valor = leitor.GetAttribute("date1904");
                sistema1904 = valor is "1" or "true";
            }
            else if (leitor.LocalName == "sheet")
            {
                abas.Add((
                    leitor.GetAttribute("name") ?? string.Empty,
                    leitor.GetAttribute("id", Relacoes) ?? leitor.GetAttribute("id", RelacoesEstrito) ?? string.Empty));
            }
        }

        return (abas, sistema1904);
    }

    private static Dictionary<string, string> LerRelacoes(ZipArchiveEntry? parte, Orcamento orcamento)
    {
        var alvos = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parte is null)
        {
            return alvos;
        }

        using var leitor = Abrir(parte, orcamento);
        while (leitor.Read())
        {
            if (leitor.NodeType == XmlNodeType.Element && leitor.LocalName == "Relationship"
                && leitor.GetAttribute("Id") is { } id && leitor.GetAttribute("Target") is { } alvo)
            {
                // Alvo absoluto ("/xl/worksheets/sheet1.xml") ou relativo a xl/ ("worksheets/sheet1.xml").
                alvos[id] = alvo.StartsWith('/') ? alvo[1..] : "xl/" + alvo;
            }
        }

        return alvos;
    }

    private static List<string> LerTextosCompartilhados(ZipArchiveEntry parte, Orcamento orcamento)
    {
        var textos = new List<string>();
        using var leitor = Abrir(parte, orcamento);
        leitor.MoveToContent();
        while (!leitor.EOF)
        {
            if (leitor.NodeType == XmlNodeType.Element && leitor.LocalName == "si")
            {
                textos.Add(LerTextoRico(leitor));
                continue;
            }

            leitor.Read();
        }

        return textos;
    }

    /// <summary>
    /// Junta os <c>&lt;t&gt;</c> de um <c>&lt;si&gt;</c> ou <c>&lt;is&gt;</c> (texto com
    /// formatação vem em pedaços), sem a leitura fonética (<c>&lt;rPh&gt;</c>). Termina depois
    /// do elemento.
    /// </summary>
    private static string LerTextoRico(XmlReader leitor)
    {
        if (leitor.IsEmptyElement)
        {
            leitor.Read();
            return string.Empty;
        }

        var texto = new StringBuilder();
        var profundidade = leitor.Depth;
        leitor.Read();
        while (!(leitor.NodeType == XmlNodeType.EndElement && leitor.Depth == profundidade) && !leitor.EOF)
        {
            if (leitor.NodeType == XmlNodeType.Element && leitor.LocalName == "rPh")
            {
                leitor.Skip();
                continue;
            }

            if (leitor.NodeType == XmlNodeType.Element && leitor.LocalName == "t")
            {
                texto.Append(leitor.ReadElementContentAsString());
                continue;
            }

            leitor.Read();
        }

        leitor.Read();
        return texto.ToString();
    }

    private static Planilha? LerAba(ZipArchiveEntry parte, List<string> compartilhadas, bool sistema1904, Orcamento orcamento)
    {
        var linhas = new List<LinhaDaPlanilha>();
        using (var leitor = Abrir(parte, orcamento))
        {
            leitor.MoveToContent();
            var numero = 0;
            Dictionary<int, Celula>? celulas = null;
            var proximaColuna = 0;

            void FecharLinha()
            {
                if (celulas is { Count: > 0 })
                {
                    var largura = Math.Min(celulas.Keys.Max() + 1, LimitesDaImportacao.ColunasMaximas);
                    var lista = new Celula[largura];
                    for (var i = 0; i < largura; i++)
                    {
                        lista[i] = celulas.TryGetValue(i, out var c) ? c : Celula.Vazia;
                    }

                    linhas.Add(new LinhaDaPlanilha(numero, lista));
                }

                celulas = null;
            }

            while (!leitor.EOF)
            {
                if (leitor.NodeType == XmlNodeType.Element && leitor.LocalName == "row")
                {
                    numero = int.TryParse(leitor.GetAttribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : numero + 1;
                    celulas = new Dictionary<int, Celula>();
                    proximaColuna = 0;
                    if (leitor.IsEmptyElement)
                    {
                        FecharLinha();
                    }

                    leitor.Read();
                    continue;
                }

                if (leitor.NodeType == XmlNodeType.EndElement && leitor.LocalName == "row")
                {
                    FecharLinha();
                    leitor.Read();
                    continue;
                }

                if (leitor.NodeType == XmlNodeType.Element && leitor.LocalName == "c" && celulas is not null)
                {
                    var coluna = Coluna(leitor.GetAttribute("r")) ?? proximaColuna;
                    proximaColuna = coluna + 1;
                    var tipo = leitor.GetAttribute("t");
                    var celula = leitor.IsEmptyElement ? Celula.Vazia : LerCelula(leitor, tipo, compartilhadas);
                    if (coluna < LimitesDaImportacao.ColunasMaximas && celula.Tipo != TipoDaCelula.Vazia)
                    {
                        celulas[coluna] = celula;
                    }

                    leitor.Read();
                    continue;
                }

                leitor.Read();
            }
        }

        // Primeira linha com conteúdo = cabeçalho. As linhas vazias no meio são contadas; as
        // do fim (o modelo formata 5.000 linhas vazias) não.
        var indiceDoCabecalho = linhas.FindIndex(l => l.Celulas.Any(c => !c.EstaVazia));
        if (indiceDoCabecalho < 0)
        {
            return null;
        }

        var cabecalho = linhas[indiceDoCabecalho];
        var dados = new List<LinhaDaPlanilha>();
        var vazias = 0;
        var anterior = cabecalho.Numero;
        foreach (var linha in linhas.Skip(indiceDoCabecalho + 1))
        {
            if (linha.Celulas.All(c => c.EstaVazia))
            {
                continue;
            }

            vazias += linha.Numero - anterior - 1;
            anterior = linha.Numero;
            dados.Add(linha);
        }

        return new Planilha([.. cabecalho.Celulas.Select(c => c.Texto.Trim())], dados, vazias, sistema1904);
    }

    /// <summary>Lê de <c>&lt;c&gt;</c> até <c>&lt;/c&gt;</c> (o leitor para no fim do elemento).</summary>
    private static Celula LerCelula(XmlReader leitor, string? tipo, List<string> compartilhadas)
    {
        var profundidade = leitor.Depth;
        string? valor = null;
        string? emLinha = null;
        var formula = false;

        leitor.Read();
        while (!(leitor.NodeType == XmlNodeType.EndElement && leitor.Depth == profundidade) && !leitor.EOF)
        {
            if (leitor.NodeType == XmlNodeType.Element)
            {
                switch (leitor.LocalName)
                {
                    case "v":
                        valor = leitor.ReadElementContentAsString();
                        continue;
                    case "f":
                        formula = true;
                        leitor.Skip();
                        continue;
                    case "is":
                        emLinha = LerTextoRico(leitor);
                        continue;
                    default:
                        leitor.Skip();
                        continue;
                }
            }

            leitor.Read();
        }

        if (formula)
        {
            return new Celula(TipoDaCelula.Formula, valor ?? string.Empty);
        }

        return tipo switch
        {
            "s" => int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < compartilhadas.Count
                ? Texto(compartilhadas[i])
                : throw new FormatException("Índice de texto compartilhado inválido."),
            "inlineStr" => Texto(emLinha ?? string.Empty),
            "str" or "d" => Texto(valor ?? string.Empty),
            "b" => new Celula(TipoDaCelula.Booleano, valor ?? string.Empty),
            "e" => new Celula(TipoDaCelula.Erro, valor ?? string.Empty),
            _ => string.IsNullOrEmpty(valor) ? Celula.Vazia : new Celula(TipoDaCelula.Numero, valor),
        };
    }

    private static Celula Texto(string texto) => texto.Length == 0 ? Celula.Vazia : new Celula(TipoDaCelula.Texto, texto);

    /// <summary>Índice da coluna a partir da referência ("C12" → 2).</summary>
    private static int? Coluna(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia))
        {
            return null;
        }

        var coluna = 0;
        var letras = 0;
        foreach (var c in referencia)
        {
            if (c is >= 'A' and <= 'Z')
            {
                coluna = (coluna * 26) + (c - 'A' + 1);
                letras++;
            }
            else
            {
                break;
            }
        }

        return letras is 0 or > 3 ? null : coluna - 1;
    }

    /// <summary>O pacote descompactado passou do limite (bomba de compressão ou arquivo absurdo).</summary>
    private sealed class PacoteGrandeDemais : Exception
    {
        public PacoteGrandeDemais()
            : base("O pacote do .xlsx passa do limite depois de descompactado.")
        {
        }
    }

    /// <summary>Bytes descompactados que o pacote inteiro ainda pode entregar.</summary>
    private sealed class Orcamento(long limite)
    {
        public long Restante { get; set; } = limite;
    }

    /// <summary>
    /// Conta o que sai de cada parte descompactada; passou do orçamento, para. O tamanho
    /// declarado no cabeçalho do zip pode mentir.
    /// </summary>
    private sealed class FluxoLimitado(Stream interno, Orcamento orcamento) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var lidos = interno.Read(buffer, offset, count);
            orcamento.Restante -= lidos;
            return orcamento.Restante < 0 ? throw new PacoteGrandeDemais() : lidos;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                interno.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
