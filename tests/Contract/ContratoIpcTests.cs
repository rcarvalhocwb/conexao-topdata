using System.Text.RegularExpressions;

namespace Contract.Tests;

/// <summary>
/// Trava o contrato IPC contra mudanças que quebrariam clientes ou vazariam dado.
/// </summary>
public sealed partial class ContratoIpcTests
{
    private static string Proto() => File.ReadAllText(
        Path.Combine(RepositorioDeMatriz.RaizDoRepositorio, "src", "Contracts", "Protos", "edge_control.proto"));

    /// <summary>
    /// Um contrato sem o campo é mais forte que a convenção de não preenchê-lo: não
    /// existe caminho pelo qual a interface gráfica receba o número do cartão.
    /// Ver docs/ADR/ADR-0008 e ADR-0014.
    /// </summary>
    [Theory]
    [InlineData("cartao")]
    [InlineData("card")]
    [InlineData("credencial =")]
    [InlineData("senha")]
    [InlineData("password")]
    [InlineData("template")]
    [InlineData("foto")]
    [InlineData("image")]
    public void O_contrato_nao_tem_campo_para_dado_sensivel(string proibido) =>
        Assert.DoesNotContain(proibido, Proto(), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void A_credencial_so_aparece_mascarada()
    {
        var proto = Proto();

        Assert.Contains("credencial_mascarada", proto, StringComparison.Ordinal);
    }

    /// <summary>
    /// Número de campo repetido dentro de uma mensagem corrompe a desserialização de
    /// forma silenciosa — o cliente lê um valor no lugar de outro.
    /// </summary>
    [Fact]
    public void Nenhuma_mensagem_repete_numero_de_campo()
    {
        var problemas = new List<string>();

        foreach (var mensagem in Mensagens().Matches(Proto()).Cast<Match>())
        {
            var nome = mensagem.Groups["nome"].Value;
            var corpo = mensagem.Groups["corpo"].Value;

            var numeros = Campos().Matches(corpo)
                .Cast<Match>()
                .Select(m => int.Parse(m.Groups["numero"].Value, provider: null))
                .ToList();

            var repetidos = numeros.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repetidos.Count > 0)
            {
                problemas.Add($"{nome}: {string.Join(", ", repetidos)}");
            }
        }

        Assert.True(problemas.Count == 0, $"Números de campo repetidos: {string.Join(" | ", problemas)}");
    }

    /// <summary>
    /// O valor 0 de um enum protobuf é o padrão implícito de quem não preencheu. Se ele
    /// tiver significado de negócio, um campo esquecido vira uma decisão tomada por
    /// omissão — aqui, potencialmente "acesso permitido".
    /// </summary>
    [Fact]
    public void Todo_enum_reserva_o_zero_para_nao_especificado()
    {
        var semZeroSeguro = Enums().Matches(Proto())
            .Cast<Match>()
            .Where(m => !m.Groups["corpo"].Value.Contains("NAO_ESPECIFICADO = 0", StringComparison.Ordinal))
            .Select(m => m.Groups["nome"].Value)
            .ToList();

        Assert.True(
            semZeroSeguro.Count == 0,
            $"Enums sem valor 0 neutro: {string.Join(", ", semZeroSeguro)}");
    }

    /// <summary>
    /// Números de <c>TipoDeComando</c> nunca mudam nem são reaproveitados: o histórico de comandos
    /// (<c>operator_command</c>) é lido por painéis de versões diferentes. Os da Etapa A.8 entram a
    /// partir do 6 (docs/35).
    /// </summary>
    [Fact]
    public void Os_tipos_de_comando_nunca_mudam_de_numero()
    {
        var corpo = Enums().Matches(Proto()).Cast<Match>().Single(m => m.Groups["nome"].Value == "TipoDeComando").Groups["corpo"].Value;
        var valores = ValoresDeEnum().Matches(corpo).Cast<Match>()
            .ToDictionary(m => m.Groups["nome"].Value, m => int.Parse(m.Groups["numero"].Value, provider: null), StringComparer.Ordinal);

        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["TIPO_DE_COMANDO_NAO_ESPECIFICADO"] = 0,
                ["TIPO_DE_COMANDO_ACERTAR_RELOGIO"] = 1,
                ["TIPO_DE_COMANDO_MENSAGEM_TEMPORARIA"] = 2,
                ["TIPO_DE_COMANDO_LIBERACAO_MANUAL"] = 3,
                ["TIPO_DE_COMANDO_REINICIAR_CONEXAO"] = 4,
                ["TIPO_DE_COMANDO_APLICAR_CONFIGURACAO"] = 5,
                ["TIPO_DE_COMANDO_BIP_CURTO"] = 6,
                ["TIPO_DE_COMANDO_BIP_LONGO"] = 7,
                ["TIPO_DE_COMANDO_LIBERAR_SAIDA"] = 8,
                ["TIPO_DE_COMANDO_LIBERAR_DOIS_SENTIDOS"] = 9,
                ["TIPO_DE_COMANDO_COLETAR_BILHETES"] = 10,
            },
            valores);
    }

    /// <summary>Todo comando que o worker sabe executar tem um valor no contrato.</summary>
    [Fact]
    public void Todo_tipo_de_comando_do_dominio_esta_no_contrato()
    {
        var proto = Proto();
        var faltando = Enum.GetNames<Access.Application.Devices.TipoDeComando>()
            .Select(n => "TIPO_DE_COMANDO_" + MaiusculaSeguida().Replace(n, "$1_$2").ToUpperInvariant())
            .Where(n => !proto.Contains(n + " =", StringComparison.Ordinal))
            .ToList();

        Assert.True(faltando.Count == 0, $"Sem valor no contrato: {string.Join(", ", faltando)}");
    }

    [Fact]
    public void O_contrato_declara_pacote_versionado()
    {
        var proto = Proto();

        Assert.Contains("package conexaotopdata.edge.v1;", proto, StringComparison.Ordinal);
        Assert.Contains("option csharp_namespace", proto, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"message\s+(?<nome>\w+)\s*\{(?<corpo>[^}]*)\}", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Mensagens();

    [GeneratedRegex(@"enum\s+(?<nome>\w+)\s*\{(?<corpo>[^}]*)\}", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Enums();

    [GeneratedRegex(@"=\s*(?<numero>\d+)\s*;", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Campos();

    [GeneratedRegex(@"(?<nome>[A-Z_]+)\s*=\s*(?<numero>\d+)\s*;", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ValoresDeEnum();

    [GeneratedRegex(@"([a-z])([A-Z])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MaiusculaSeguida();
}
