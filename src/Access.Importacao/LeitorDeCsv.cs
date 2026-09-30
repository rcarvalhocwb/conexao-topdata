using System.Globalization;
using System.Text;

namespace Access.Importacao;

/// <summary>
/// Lê o CSV do modelo (docs/26): separador <c>;</c>, UTF-8 com ou sem BOM, CRLF, LF ou CR,
/// campos entre aspas (RFC 4180).
/// </summary>
/// <remarks>
/// <para>
/// Todo campo sai como <see cref="TipoDaCelula.Texto"/>: o CSV não tem tipo, e nada aqui
/// converte para número — é o que preserva os zeros à esquerda (ADR-0008). A notação
/// científica que o Excel deixa num CSV salvo por cima é recusada depois, pela regra do
/// código (<see cref="PreviaDaImportacao"/>).
/// </para>
/// <para>
/// Só UTF-8. Um arquivo em outra codificação é recusado inteiro, com a instrução de como
/// salvar, em vez de ser adivinhado: um acento trocado no tipo vira "tipo inexistente" em
/// milhares de linhas.
/// </para>
/// </remarks>
public static class LeitorDeCsv
{
    private const char Separador = ';';

    /// <summary>Lê o arquivo inteiro.</summary>
    public static ArquivoLido Ler(Stream arquivo)
    {
        ArgumentNullException.ThrowIfNull(arquivo);

        var conteudo = Apoio.LerAteOLimite(arquivo, LimitesDaImportacao.BytesDoArquivo);
        if (conteudo is null)
        {
            return ArquivoLido.Recusa("csv", LimitesDaImportacao.BytesDoArquivo, string.Empty,
                $"O arquivo passa de {LimitesDaImportacao.BytesDoArquivo / (1024 * 1024)} MB. Divida-o em partes.");
        }

        var sha = Apoio.Sha256(conteudo);
        var bytes = conteudo.LongLength;

        if (conteudo.Length >= 2 && ((conteudo[0] == 0xFF && conteudo[1] == 0xFE) || (conteudo[0] == 0xFE && conteudo[1] == 0xFF)))
        {
            return ArquivoLido.Recusa("csv", bytes, sha,
                "O arquivo está em UTF-16 (\"Texto Unicode\"). No Excel, salve como \"CSV UTF-8\".");
        }

        var inicio = conteudo.Length >= 3 && conteudo[0] == 0xEF && conteudo[1] == 0xBB && conteudo[2] == 0xBF ? 3 : 0;

        string texto;
        try
        {
            texto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(conteudo, inicio, conteudo.Length - inicio);
        }
        catch (DecoderFallbackException)
        {
            return ArquivoLido.Recusa("csv", bytes, sha,
                "O arquivo não está em UTF-8. No Excel, salve como \"CSV UTF-8 (delimitado por vírgulas)\".");
        }

        var registros = new List<(int Linha, List<string> Campos)>();
        var problema = Separar(texto, registros);
        if (problema is not null)
        {
            return ArquivoLido.Recusa("csv", bytes, sha, problema);
        }

        // A dica que o Excel entende ("sep=;") não é cabeçalho.
        if (registros.Count > 0 && registros[0].Campos.Count == 1
            && string.Equals(registros[0].Campos[0].Trim(), "sep=;", StringComparison.OrdinalIgnoreCase))
        {
            registros.RemoveAt(0);
        }

        var cabecalho = registros.FindIndex(r => r.Campos.Exists(c => !string.IsNullOrWhiteSpace(c)));
        if (cabecalho < 0)
        {
            return ArquivoLido.Recusa("csv", bytes, sha, "O arquivo está vazio.");
        }

        var nomes = registros[cabecalho].Campos;
        if (nomes.Count == 1 && (nomes[0].Contains(',', StringComparison.Ordinal) || nomes[0].Contains('\t', StringComparison.Ordinal)))
        {
            return ArquivoLido.Recusa("csv", bytes, sha,
                "O separador das colunas não é ponto e vírgula (;). Salve como \"CSV UTF-8\" no Excel em português.");
        }

        var linhas = new List<LinhaDaPlanilha>();
        var vazias = 0;
        var vaziasPendentes = 0;
        for (var i = cabecalho + 1; i < registros.Count; i++)
        {
            var (numero, campos) = registros[i];
            if (campos.TrueForAll(string.IsNullOrWhiteSpace))
            {
                vaziasPendentes++;
                continue;
            }

            vazias += vaziasPendentes;
            vaziasPendentes = 0;

            if (linhas.Count >= LimitesDaImportacao.LinhasMaximas)
            {
                return ArquivoLido.Recusa("csv", bytes, sha, string.Create(CultureInfo.InvariantCulture,
                    $"O arquivo passa de {LimitesDaImportacao.LinhasMaximas} linhas. Divida-o em partes."));
            }

            linhas.Add(new LinhaDaPlanilha(
                numero,
                [.. campos.Take(LimitesDaImportacao.ColunasMaximas)
                    .Select(c => c.Length == 0 ? Celula.Vazia : new Celula(TipoDaCelula.Texto, c))]));
        }

        var planilha = new Planilha([.. nomes.Select(n => n.Trim())], linhas, vazias);
        return new ArquivoLido("csv", bytes, sha, planilha, null, [], []);
    }

    /// <summary>
    /// Separa o texto em registros. Um campo entre aspas pode ter <c>;</c>, aspas dobradas e
    /// quebra de linha; o número de cada registro é a linha física em que ele começa.
    /// </summary>
    /// <returns>Nulo, ou o motivo para recusar o arquivo.</returns>
    private static string? Separar(string texto, List<(int Linha, List<string> Campos)> registros)
    {
        var linha = 1;
        var inicioDoRegistro = 1;
        var campos = new List<string>();
        var campo = new StringBuilder();
        var entreAspas = false;
        var linhaDasAspas = 0;
        var i = 0;

        void FecharCampo()
        {
            campos.Add(campo.ToString());
            campo.Clear();
        }

        void FecharRegistro()
        {
            FecharCampo();
            registros.Add((inicioDoRegistro, campos));
            campos = [];
        }

        while (i < texto.Length)
        {
            var c = texto[i];

            if (entreAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"')
                    {
                        campo.Append('"');
                        i += 2;
                        continue;
                    }

                    entreAspas = false;
                    i++;
                    continue;
                }

                if (c == '\n' || (c == '\r' && (i + 1 >= texto.Length || texto[i + 1] != '\n')))
                {
                    linha++;
                }

                campo.Append(c);
                i++;
                continue;
            }

            switch (c)
            {
                case '"' when campo.Length == 0:
                    entreAspas = true;
                    linhaDasAspas = linha;
                    i++;
                    break;
                case Separador:
                    FecharCampo();
                    i++;
                    break;
                case '\r' or '\n':
                    FecharRegistro();
                    i += c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n' ? 2 : 1;
                    linha++;
                    inicioDoRegistro = linha;
                    break;
                default:
                    campo.Append(c);
                    i++;
                    break;
            }
        }

        if (entreAspas)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Aspas abertas na linha {linhaDasAspas} e nunca fechadas.");
        }

        // A última linha sem quebra no fim também é registro.
        if (campo.Length > 0 || campos.Count > 0)
        {
            FecharRegistro();
        }

        return null;
    }
}
