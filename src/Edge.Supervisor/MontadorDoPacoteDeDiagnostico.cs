using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shared.Observability;

namespace Edge.Supervisor;

/// <summary>
/// Monta o zip que o suporte pede quando algo dá errado: registros recentes, versão, situação dos
/// programas das catracas e a configuração sem segredo.
/// </summary>
/// <remarks>
/// <para>
/// O que <b>não</b> entra: o banco (<c>acesso.db</c>, com número de cartão), o token, o cofre de
/// segredos e as cópias de segurança. Entra só o que foi escolhido à mão aqui; não existe varredura
/// de pasta que possa trazer um arquivo novo sem querer.
/// </para>
/// <para>
/// Cada linha de registro passa pelo <see cref="RedatorDeDadoSensivel"/>, e a configuração passa por
/// uma remoção de tudo que se chame segredo, senha, token ou chave, em qualquer nível.
/// </para>
/// </remarks>
public static class MontadorDoPacoteDeDiagnostico
{
    public const int DiasDeRegistro = 7;

    private static readonly string[] PalavrasSecretas = ["segredo", "secret", "token", "senha", "password", "chave", "key", "pwd"];

    /// <summary>Monta o zip em memória.</summary>
    public static byte[] Montar(string pastaDeDados, string versao, IEnumerable<string> linhasDoDiagnostico, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDeDados);
        ArgumentNullException.ThrowIfNull(linhasDoDiagnostico);

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escrever(zip, "diagnostico.txt", string.Join(Environment.NewLine, [
                $"Versão: {versao}",
                $"Gerado em: {agora:yyyy-MM-dd HH:mm:ss zzz}",
                string.Empty,
                .. linhasDoDiagnostico,
            ]));

            var registros = Path.Combine(pastaDeDados, "registros");
            if (Directory.Exists(registros))
            {
                var corte = agora.UtcDateTime.AddDays(-DiasDeRegistro);
                foreach (var arquivo in Directory.EnumerateFiles(registros, "*.log").OrderBy(f => f, StringComparer.Ordinal))
                {
                    if (File.GetLastWriteTimeUtc(arquivo) < corte)
                    {
                        continue;
                    }

                    var linhas = File.ReadAllLines(arquivo).Select(RedatorDeDadoSensivel.Redigir);
                    Escrever(zip, $"registros/{Path.GetFileName(arquivo)}", string.Join(Environment.NewLine, linhas));
                }
            }

            var configuracao = Path.Combine(pastaDeDados, "workers.json");
            if (File.Exists(configuracao))
            {
                Escrever(zip, "workers.json", SemSegredos(File.ReadAllText(configuracao)));
            }
        }

        return memoria.ToArray();
    }

    /// <summary>A configuração sem nenhuma chave que pareça segredo, em qualquer nível.</summary>
    public static string SemSegredos(string json)
    {
        var no = JsonNode.Parse(json);
        var limpo = no is null ? null : Limpar(no);
        return limpo?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
    }

    private static JsonNode? Limpar(JsonNode? no)
    {
        switch (no)
        {
            case JsonObject objeto:
                foreach (var chave in objeto.Select(p => p.Key).ToList())
                {
                    if (PalavrasSecretas.Any(p => chave.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        objeto.Remove(chave);
                    }
                    else if (objeto[chave] is { } filho)
                    {
                        // Limpa no próprio lugar: o nó já pertence ao objeto e não pode ser reatribuído.
                        Limpar(filho);
                    }
                }

                return objeto;
            case JsonArray lista:
                foreach (var item in lista)
                {
                    if (item is not null)
                    {
                        Limpar(item);
                    }
                }

                return lista;
            default:
                return no;
        }
    }

    private static void Escrever(ZipArchive zip, string nome, string conteudo)
    {
        var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal);
        using var escrita = new StreamWriter(entrada.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        escrita.Write(conteudo);
    }
}
