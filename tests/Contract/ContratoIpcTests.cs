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
    [InlineData("password")]
    [InlineData("template")]
    [InlineData("foto")]
    [InlineData("image")]
    public void O_contrato_nao_tem_campo_para_dado_sensivel(string proibido) =>
        Assert.DoesNotContain(proibido, Proto(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Senha só existe no sentido painel → serviço, nos pedidos de login e de gestão de usuários
    /// (ADR-0026). Nenhuma resposta tem campo de senha: o serviço nunca devolve senha nem hash. O único
    /// campo de resposta com a palavra é o aviso <c>trocar_senha</c>, que é um sim/não.
    /// </summary>
    [Fact]
    public void Senha_so_vai_do_painel_ao_servico_nos_pedidos_de_login()
    {
        string[] pedidosPermitidos = ["EntrarRequest", "TrocarSenhaRequest", "GravarUsuarioRequest", "RedefinirSenhaRequest"];
        var problemas = new List<string>();

        foreach (System.Text.RegularExpressions.Match mensagem in System.Text.RegularExpressions.Regex.Matches(
            Proto(), @"message\s+(\w+)\s*\{(.*?)\n\}", System.Text.RegularExpressions.RegexOptions.Singleline))
        {
            var nome = mensagem.Groups[1].Value;
            foreach (System.Text.RegularExpressions.Match campo in System.Text.RegularExpressions.Regex.Matches(
                mensagem.Groups[2].Value, @"^\s*(?:repeated\s+)?[\w.]+\s+(\w+)\s*=\s*\d+", System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                var nomeDoCampo = campo.Groups[1].Value;
                if (!nomeDoCampo.Contains("senha", StringComparison.OrdinalIgnoreCase) || nomeDoCampo == "trocar_senha")
                {
                    continue;
                }

                if (!pedidosPermitidos.Contains(nome))
                {
                    problemas.Add($"{nome}.{nomeDoCampo}");
                }
            }
        }

        Assert.Empty(problemas);
    }

    /// <summary>
    /// O código de acesso em claro só existe nos pedidos (painel → serviço): consultar, simular e dar uma
    /// credencial a uma pessoa (docs/43). Nas respostas, só a máscara; os outros campos "codigo" são de
    /// permissão e de campo do formulário, não de acesso.
    /// </summary>
    [Fact]
    public void Codigo_em_claro_so_existe_nos_pedidos()
    {
        string[] permitidos = ["codigo_mascarado", "PermissaoDoCatalogo.codigo", "CampoDoFormulario.codigo"];
        var problemas = new List<string>();
        foreach (Match mensagem in Regex.Matches(Proto(), @"message\s+(\w+)\s*\{(.*?)\n\}", RegexOptions.Singleline))
        {
            var nome = mensagem.Groups[1].Value;
            if (nome.EndsWith("Request", StringComparison.Ordinal))
            {
                continue;
            }

            // Só texto: "codigos" de ProvedorCadastrado, por exemplo, é uma contagem.
            foreach (Match campo in Regex.Matches(mensagem.Groups[2].Value, @"^\s*(?:repeated\s+)?string\s+(\w+)\s*=\s*\d+", RegexOptions.Multiline))
            {
                var nomeDoCampo = campo.Groups[1].Value;
                if (nomeDoCampo.Contains("codigo", StringComparison.Ordinal)
                    && !permitidos.Contains(nomeDoCampo) && !permitidos.Contains($"{nome}.{nomeDoCampo}"))
                {
                    problemas.Add($"{nome}.{nomeDoCampo}");
                }
            }
        }

        Assert.Empty(problemas);
    }

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

    [GeneratedRegex(@"(?<nome>[A-Z_]+)\s*=\s*(?<numero>\d+)\s*;", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ValoresDeEnum();

    [GeneratedRegex(@"([a-z])([A-Z])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex MaiusculaSeguida();
}
