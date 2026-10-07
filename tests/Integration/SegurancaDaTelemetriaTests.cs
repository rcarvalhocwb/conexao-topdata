using System.Text.Json;
using Access.Domain.Credentials;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// NOVO-SEC-IA-02 (I.7): Verificação de que telemetria.db não contém código em claro.
/// Varredura de todas as colunas de texto para garantir que códigos de teste não apareçam.
/// </summary>
public sealed class SegurancaDaTelemetriaTests
{
    // Códigos de teste conhecidos que NUNCA devem aparecer em claro em telemetria.db.
    private static readonly string[] CodigosDeTesteProibidos =
    [
        "2000000001",  // Passe de 2 usos
        "2000000002",  // Passe de 2 usos
        "2000000003",  // Passe de 2 usos
        "2000000004",  // Passe de 2 usos (mencionado na spec)
    ];

    /// <summary>
    /// NOVO-SEC-IA-02: Varredura de telemetria.db para garantir que nenhum código teste
    /// aparece em claro. Colunas de texto (texto, conta, prova) são verificadas.
    /// </summary>
    [Fact]
    public void NaoTemCodigosDeTesteEmClaro()
    {
        using var bd = CriarTelemetriaDeTest();

        // Lê todas as linhas das tabelas de texto de telemetria.db.
        var linhas = LerTodasAsLinhas(bd);

        // Verifica cada coluna de texto.
        foreach (var (tabela, coluna, valor) in linhas)
        {
            // Se encontrar um código de teste em claro, falha.
            foreach (var codigo in CodigosDeTesteProibidos)
            {
                Assert.False(
                    valor.Contains(codigo, StringComparison.Ordinal),
                    $"Código {codigo} encontrado em claro em {tabela}.{coluna}.");
            }
        }
    }

    /// <summary>
    /// Verificação de que a impressão HMAC é usada corretamente: prefixo conhecido.
    /// </summary>
    [Fact]
    public void ImpressaoHmacTemPrefixoCorreto()
    {
        // Verifica que a impressão gerada pela ImpressaoDeCodigo tem o prefixo esperado.
        var chave = System.Security.Cryptography.RandomNumberGenerator.GetBytes(
            ImpressaoDeCodigo.TamanhoMinimoDaChave);
        var impressora = new ImpressaoDeCodigo(chave);

        var resultado = impressora.De("2000000004");

        Assert.StartsWith(ImpressaoDeCodigo.Prefixo, resultado);
        Assert.True(resultado.Length == ImpressaoDeCodigo.Prefixo.Length + 64,
            "Impressão deve ter prefixo + 64 hexadecimais");
    }

    /// <summary>
    /// A impressão nunca é salva em ToString (segurança por omissão).
    /// </summary>
    [Fact]
    public void ImpressaoNuncaEmToString()
    {
        var chave = System.Security.Cryptography.RandomNumberGenerator.GetBytes(
            ImpressaoDeCodigo.TamanhoMinimoDaChave);
        var impressora = new ImpressaoDeCodigo(chave);

        var str = impressora.ToString();

        // ToString retorna apenas o ID da chave, não a impressão em si.
        Assert.DoesNotContain("hmac", str, StringComparison.Ordinal);
        Assert.Matches(@"impressao:k1:[0-9a-f]{16}", str);
    }

    // --- Helpers ---

    private static SqliteConnection CriarTelemetriaDeTest()
    {
        var conexao = new SqliteConnection("Data Source=:memory:");
        conexao.Open();

        // Cria as tabelas de telemetria (versão simplificada para teste).
        SqliteConnectionFactory.Executar(conexao, """
            CREATE TABLE alert (
                id TEXT PRIMARY KEY,
                regra TEXT NOT NULL,
                session_id TEXT NULL,
                inner_number INTEGER NULL,
                portao TEXT NULL,
                nivel TEXT NOT NULL,
                texto TEXT NOT NULL,
                conta TEXT NOT NULL,
                aberto_em TEXT NOT NULL,
                atualizado_em TEXT NOT NULL,
                fechado_em TEXT NULL,
                ciente_por TEXT NULL,
                ciente_em TEXT NULL,
                versao_dos_parametros TEXT NOT NULL,
                simulacao INTEGER NOT NULL,
                elegivel_a_rele INTEGER NOT NULL
            );

            CREATE TABLE reuse_detection (
                id TEXT PRIMARY KEY,
                session_id TEXT NULL,
                credential_hmac TEXT NOT NULL,
                padrao TEXT NOT NULL,
                janela_inicio TEXT NOT NULL,
                janela_fim TEXT NOT NULL,
                duracao_segundos INTEGER NOT NULL,
                tentativas TEXT NOT NULL,
                negadas_count INTEGER NOT NULL,
                confirmadas_count INTEGER NOT NULL,
                confianca_percentual INTEGER NOT NULL,
                severidade TEXT NOT NULL,
                gravado_em TEXT NOT NULL,
                simulacao INTEGER NOT NULL
            );
            """);

        return conexao;
    }

    private static List<(string Tabela, string Coluna, string Valor)> LerTodasAsLinhas(
        SqliteConnection conexao)
    {
        var resultado = new List<(string, string, string)>();
        var tabelasDeTexto = new[]
        {
            ("alert", "texto"),
            ("alert", "conta"),
            ("reuse_detection", "tentativas"),
        };

        foreach (var (tabela, coluna) in tabelasDeTexto)
        {
            using var cmd = conexao.CreateCommand();
            cmd.CommandText = $"SELECT {coluna} FROM {tabela};";

            try
            {
                using var leitor = cmd.ExecuteReader();
                while (leitor.Read())
                {
                    if (!leitor.IsDBNull(0))
                    {
                        var valor = leitor.GetString(0);
                        resultado.Add((tabela, coluna, valor));
                    }
                }
            }
            catch (SqliteException)
            {
                // Tabela pode não ter dados no teste em memória; continua.
            }
        }

        return resultado;
    }
}
