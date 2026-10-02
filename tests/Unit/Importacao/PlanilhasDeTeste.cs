using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Unit.Tests.Importacao;

/// <summary>
/// Gera, em memória, os CSV e .xlsx dos testes. Nenhum arquivo de usuário é lido; todo
/// código é sintético (<c>9999…</c>, <c>0000000101</c>).
/// </summary>
internal static class PlanilhasDeTeste
{
    public const string CabecalhoCsv = "codigo;tipo;titular;situacao;validade_inicio;validade_fim;usos_maximos;observacao";

    public static readonly string[] CabecalhoXlsx =
        ["codigo", "tipo", "titular", "situacao", "validade_inicio", "validade_fim", "usos_maximos", "observacao"];

    /// <summary>Como a célula é gravada no .xlsx.</summary>
    public enum Tipo
    {
        Compartilhado,
        EmLinha,
        Numero,
        Formula,
        VazioComEstilo,
    }

    /// <summary>Uma célula a gerar.</summary>
    public readonly record struct C(string Texto, Tipo Tipo = Tipo.Compartilhado)
    {
        public static implicit operator C(string texto) => new(texto);
    }

    public static MemoryStream Csv(string conteudo, bool bom = true)
    {
        var corpo = Encoding.UTF8.GetBytes(conteudo);
        return new MemoryStream(bom ? [0xEF, 0xBB, 0xBF, .. corpo] : corpo);
    }

    public static string LinhasCrlf(params string[] linhas) => string.Join("\r\n", linhas) + "\r\n";

    /// <summary>Um .xlsx com as abas dadas. Linha nula = linha que não existe no XML (buraco).</summary>
    public static MemoryStream Xlsx(
        IReadOnlyList<(string Nome, IReadOnlyList<IReadOnlyList<C>?> Linhas)> abas,
        bool sistema1904 = false,
        bool comMacro = false,
        long bytesDeLixo = 0)
    {
        var compartilhadas = new List<string>();
        var indice = new Dictionary<string, int>(StringComparer.Ordinal);

        int Compartilhar(string texto)
        {
            if (!indice.TryGetValue(texto, out var i))
            {
                i = compartilhadas.Count;
                compartilhadas.Add(texto);
                indice[texto] = i;
            }

            return i;
        }

        var memoria = new MemoryStream();
        using (var pacote = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            var planilhas = new List<string>();
            for (var a = 0; a < abas.Count; a++)
            {
                var xml = new StringBuilder(
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                for (var l = 0; l < abas[a].Linhas.Count; l++)
                {
                    if (abas[a].Linhas[l] is not { } linha)
                    {
                        continue;
                    }

                    var numero = l + 1;
                    xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{numero}\">");
                    for (var c = 0; c < linha.Count; c++)
                    {
                        var referencia = $"{(char)('A' + c)}{numero}";
                        var celula = linha[c];
                        if (celula.Tipo != Tipo.VazioComEstilo && celula.Texto.Length == 0)
                        {
                            continue;
                        }

                        var valor = SecurityElement.Escape(celula.Texto);
                        xml.Append(celula.Tipo switch
                        {
                            Tipo.Compartilhado => $"<c r=\"{referencia}\" t=\"s\"><v>{Compartilhar(celula.Texto)}</v></c>",
                            Tipo.EmLinha => $"<c r=\"{referencia}\" t=\"inlineStr\"><is><t>{valor}</t></is></c>",
                            Tipo.Numero => $"<c r=\"{referencia}\"><v>{valor}</v></c>",
                            Tipo.Formula => $"<c r=\"{referencia}\" t=\"str\"><f>A1</f><v>{valor}</v></c>",
                            _ => $"<c r=\"{referencia}\" s=\"3\" t=\"n\" />",
                        });
                    }

                    xml.Append("</row>");
                }

                xml.Append("</sheetData></worksheet>");
                planilhas.Add(xml.ToString());
            }

            Escrever(pacote, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"></Types>");

            var pasta = new StringBuilder(
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            pasta.Append(sistema1904 ? "<workbookPr date1904=\"1\" />" : "<workbookPr />");
            pasta.Append("<sheets>");
            var relacoes = new StringBuilder(
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (var a = 0; a < abas.Count; a++)
            {
                pasta.Append(CultureInfo.InvariantCulture,
                    $"<sheet name=\"{SecurityElement.Escape(abas[a].Nome)}\" sheetId=\"{a + 1}\" r:id=\"rId{a + 1}\" />");

                // Metade com alvo absoluto (como o modelo instalado), metade relativo (como o Excel).
                var alvo = a % 2 == 0 ? $"/xl/worksheets/sheet{a + 1}.xml" : $"worksheets/sheet{a + 1}.xml";
                relacoes.Append(CultureInfo.InvariantCulture,
                    $"<Relationship Id=\"rId{a + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"{alvo}\" />");
                Escrever(pacote, $"xl/worksheets/sheet{a + 1}.xml", planilhas[a]);
            }

            pasta.Append("</sheets></workbook>");
            relacoes.Append("</Relationships>");
            Escrever(pacote, "xl/workbook.xml", pasta.ToString());
            Escrever(pacote, "xl/_rels/workbook.xml.rels", relacoes.ToString());

            var sst = new StringBuilder("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            foreach (var texto in compartilhadas)
            {
                sst.Append(CultureInfo.InvariantCulture, $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(texto)}</t></si>");
            }

            sst.Append("</sst>");
            Escrever(pacote, "xl/sharedStrings.xml", sst.ToString());

            if (comMacro)
            {
                Escrever(pacote, "xl/vbaProject.bin", "macro");
            }

            if (bytesDeLixo > 0)
            {
                using var lixo = pacote.CreateEntry("xl/media/lixo.bin", CompressionLevel.Fastest).Open();
                var bloco = new byte[1024 * 1024];
                for (long escrito = 0; escrito < bytesDeLixo; escrito += bloco.Length)
                {
                    lixo.Write(bloco);
                }
            }
        }

        memoria.Position = 0;
        return memoria;
    }

    /// <summary>Um .xlsx só com a aba Cartões (cabeçalho do modelo + as linhas dadas).</summary>
    public static MemoryStream XlsxDeCartoes(params IReadOnlyList<C>[] linhas) =>
        Xlsx([("Cartões", [Cabecalho(), .. linhas])]);

    public static IReadOnlyList<C> Cabecalho() => [.. CabecalhoXlsx.Select(n => new C(n))];

    private static void Escrever(ZipArchive pacote, string caminho, string conteudo)
    {
        using var fluxo = pacote.CreateEntry(caminho).Open();
        fluxo.Write(Encoding.UTF8.GetBytes(conteudo));
    }
}
