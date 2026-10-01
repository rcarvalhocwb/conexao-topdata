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

    [Fact]
    public void O_contrato_declara_pacote_versionado()
    {
        var proto = Proto();

        Assert.Contains("package conexaotopdata.edge.v1;", proto, StringComparison.Ordinal);
        Assert.Contains("option csharp_namespace", proto, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parametrização por catraca (Etapa A.6 do docs/35): os dois RPCs existem, cada resposta
    /// tem onde devolver problema (nunca exceção para a tela) e o valor pode faltar ("herda"),
    /// que é diferente de vazio.
    /// </summary>
    [Fact]
    public void Parametrizacao_por_catraca_devolve_problemas_e_distingue_herdar_de_vazio()
    {
        var proto = Proto();

        Assert.Contains(
            "rpc ObterConfiguracaoDaCatraca(ObterConfiguracaoDaCatracaRequest) returns (ConfiguracaoDaCatraca);",
            proto,
            StringComparison.Ordinal);
        Assert.Contains(
            "rpc GravarConfiguracaoDaCatraca(GravarConfiguracaoDaCatracaRequest) returns (GravarConfiguracaoDaCatracaResponse);",
            proto,
            StringComparison.Ordinal);

        var mensagens = Mensagens().Matches(proto).ToDictionary(m => m.Groups["nome"].Value, m => m.Groups["corpo"].Value);
        Assert.Contains("repeated string problemas", mensagens["ConfiguracaoDaCatraca"], StringComparison.Ordinal);
        Assert.Contains("repeated string problemas", mensagens["GravarConfiguracaoDaCatracaResponse"], StringComparison.Ordinal);
        Assert.Contains("optional string valor_da_catraca", mensagens["CampoConfiguradoDaCatraca"], StringComparison.Ordinal);
        Assert.Contains("optional string valor", mensagens["ValorDaCatraca"], StringComparison.Ordinal);
        Assert.Contains("string versao_salva", mensagens["ConfiguracaoDaCatraca"], StringComparison.Ordinal);
        Assert.Contains("string versao_aplicada", mensagens["ConfiguracaoDaCatraca"], StringComparison.Ordinal);
    }

    [GeneratedRegex(@"message\s+(?<nome>\w+)\s*\{(?<corpo>[^}]*)\}", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Mensagens();

    [GeneratedRegex(@"enum\s+(?<nome>\w+)\s*\{(?<corpo>[^}]*)\}", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Enums();

    [GeneratedRegex(@"=\s*(?<numero>\d+)\s*;", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Campos();
}
