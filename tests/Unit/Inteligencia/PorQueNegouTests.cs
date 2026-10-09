using System.Reflection;
using System.Text.RegularExpressions;
using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Ticketing;
using Access.Inteligencia;

namespace Unit.Tests.Inteligencia;

/// <summary>
/// "Por que negou" (IN-06, Etapa I.2 do docs/36) como função pura: todo motivo tem texto próprio,
/// nenhum texto tem termo do SDK nem dado pessoal, e o contexto aparece certo (docs/36-anexos/03
/// §2.3, T4; §5.2, critério do NOVO-SIM-NEG-01).
/// </summary>
public sealed partial class PorQueNegouTests
{
    // 22:31 em UTC = 19:31 em Brasília.
    private static readonly DateTimeOffset UsoAnteriorEm = new(2026, 10, 2, 22, 31, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Agora = new(2026, 10, 2, 22, 44, 7, TimeSpan.Zero);

    private static readonly string[] CodigosDeLiberacao =
        ["AUTORIZADO", "AUTORIZADO_LISTA_LOCAL", "AUTORIZADO_SEM_CONFIRMACAO", "LIBERACAO_MANUAL"];

    private static readonly ContextoDaNegativa SemContexto = new(Agora, 2);

    private static readonly ContextoDaNegativa ContextoCompleto = new(
        Agora, 2, new UsoAnterior(UsoAnteriorEm, 4, Girou: true), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(3));

    /// <summary>Todos os motivos de negação dos dois catálogos: o gravado e o do relatório.</summary>
    public static TheoryData<string> MotivosDeNegacao()
    {
        var dados = new TheoryData<string>();

        foreach (var motivo in Enum.GetValues<MotivoDoUso>().Where(m => m != MotivoDoUso.Consumido))
        {
            dados.Add(motivo.ToString());
        }

        foreach (var codigo in CodigosDoCatalogo().Where(c => !PorQueNegou.CodigosQueNaoNegam.Contains(c)))
        {
            dados.Add(codigo);
        }

        return dados;
    }

    private static IEnumerable<string> CodigosDoCatalogo() =>
        typeof(ReasonCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(ReasonCode))
            .Select(f => ((ReasonCode)f.GetValue(null)!).Value);

    [Theory]
    [MemberData(nameof(MotivosDeNegacao))]
    public void Todo_motivo_de_negacao_tem_texto_proprio(string motivo)
    {
        foreach (var contexto in new[] { SemContexto, ContextoCompleto })
        {
            var e = PorQueNegou.ExplicarNegativa(motivo, contexto);

            Assert.True(e.TemTextoProprio, $"{motivo} sem texto próprio: classifique-o em PorQueNegou.");
            Assert.False(string.IsNullOrWhiteSpace(e.OQueAconteceu));
            Assert.False(string.IsNullOrWhiteSpace(e.OQueDizer));
            Assert.False(string.IsNullOrWhiteSpace(e.OQueFazer));
            Assert.Contains(motivo, PorQueNegou.MotivosComTexto);
        }
    }

    /// <summary>
    /// Os códigos que não negam são exatamente estes. Código novo no catálogo cai em
    /// <see cref="Todo_motivo_de_negacao_tem_texto_proprio"/> até ganhar texto ou ser classificado aqui.
    /// </summary>
    [Fact]
    public void Os_codigos_que_nao_negam_sao_os_de_liberacao()
    {
        Assert.Equal(CodigosDeLiberacao, PorQueNegou.CodigosQueNaoNegam.Order(StringComparer.Ordinal));
        Assert.All(PorQueNegou.CodigosQueNaoNegam, c => Assert.Contains(c, CodigosDoCatalogo()));
    }

    /// <summary>O motivo gravado e o código do relatório dão o mesmo texto (a tradução é a do decisor).</summary>
    [Theory]
    [MemberData(nameof(MotivosDoUso))]
    public void Motivo_gravado_e_codigo_do_relatorio_explicam_igual(MotivoDoUso motivo) =>
        Assert.Equal(
            PorQueNegou.ExplicarNegativa(DecisorDeIngresso.CodigoPara(motivo).Value, ContextoCompleto),
            PorQueNegou.ExplicarNegativa(motivo.ToString(), ContextoCompleto));

    public static TheoryData<MotivoDoUso> MotivosDoUso() => [.. Enum.GetValues<MotivoDoUso>().Where(m => m != MotivoDoUso.Consumido)];

    [Theory]
    [InlineData("MotivoQueNaoExiste")]
    [InlineData("")]
    [InlineData(null)]
    public void Motivo_desconhecido_cai_no_texto_generico_sem_lancar(string? motivo)
    {
        var e = PorQueNegou.ExplicarNegativa(motivo, SemContexto);

        Assert.False(e.TemTextoProprio);
        Assert.Contains("não sabe explicar", e.OQueAconteceu, StringComparison.Ordinal);
        Assert.Equal("Anote a hora e a catraca e avise o suporte.", e.OQueFazer);
    }

    /// <summary>Português de portaria: nenhum termo do SDK nem nome de motivo do código (03 §2.1, item 5).</summary>
    [Theory]
    [MemberData(nameof(MotivosDeNegacao))]
    public void Nenhum_texto_tem_termo_do_sdk_nem_nome_de_codigo(string motivo)
    {
        foreach (var contexto in new[] { SemContexto, ContextoCompleto, ContextoCompleto with { UltimoUso = new UsoAnterior(UsoAnteriorEm, 4, false) } })
        {
            var e = PorQueNegou.ExplicarNegativa(motivo, contexto);
            var texto = $"{e.OQueAconteceu} {e.OQueDizer} {e.OQueFazer}";

            Assert.DoesNotMatch(TermoDoSdk(), texto);
            Assert.DoesNotMatch(NomeDeCodigo(), texto);
            Assert.DoesNotContain(motivo, texto, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// LGPD (docs/36-anexos/02 §6): o contexto não tem campo de texto — não há por onde entrar
    /// código, máscara, nome ou categoria na explicação.
    /// </summary>
    [Fact]
    public void O_contexto_nao_tem_por_onde_levar_dado_pessoal()
    {
        foreach (var tipo in new[] { typeof(ContextoDaNegativa), typeof(UsoAnterior) })
        {
            Assert.All(tipo.GetProperties(), p => Assert.NotEqual(typeof(string), p.PropertyType));
        }
    }

    [Fact]
    public void Ja_usado_diz_quando_e_por_qual_catraca_e_se_girou()
    {
        var comGiro = PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.UsosEsgotados), ContextoCompleto);
        Assert.Equal("Este ingresso já entrou às 19:31:00 pela catraca 04, com giro confirmado pelo sensor.", comGiro.OQueAconteceu);
        Assert.Equal("Este ingresso já foi usado. Procure o atendimento, por favor.", comGiro.OQueDizer);

        var semGiro = PorQueNegou.ExplicarNegativa(
            nameof(MotivoDoUso.UsosEsgotados), ContextoCompleto with { UltimoUso = new UsoAnterior(UsoAnteriorEm, 4, Girou: false) });
        Assert.Equal("Este ingresso foi liberado às 19:31:00 pela catraca 04, mas o sensor não confirmou o giro.", semGiro.OQueAconteceu);
        Assert.Contains("liberação manual com o motivo", semGiro.OQueFazer, StringComparison.Ordinal);

        Assert.Equal(
            "Este ingresso já foi usado todas as vezes que podia.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.UsosEsgotados), SemContexto).OQueAconteceu);
    }

    [Fact]
    public void Intervalo_de_reuso_diz_quando_o_cartao_volta_a_valer()
    {
        Assert.Equal(
            "Este cartão foi usado às 19:31:00 pela catraca 04 e só volta a valer às 19:46:00.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.EmIntervaloDeReuso), ContextoCompleto).OQueAconteceu);
        Assert.Equal(
            "Este cartão foi usado há pouco e só volta a valer depois do intervalo mínimo entre dois usos.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.EmIntervaloDeReuso), SemContexto).OQueAconteceu);
    }

    [Fact]
    public void Desconhecido_diz_a_idade_da_base_quando_a_nuvem_esta_configurada()
    {
        Assert.Equal(
            "Este código não está na base deste PC. A base recebeu a última atualização da nuvem há 3 min.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.Desconhecido), ContextoCompleto).OQueAconteceu);
        Assert.Equal(
            "Este código não está na base deste PC.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.Desconhecido), SemContexto).OQueAconteceu);
    }

    [Fact]
    public void Tipo_inativo_da_B2_e_fora_da_urna_tem_o_texto_do_desenho()
    {
        Assert.Equal(
            "O tipo de entrada deste ingresso foi desativado pela operação.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.TipoInativo), SemContexto).OQueAconteceu);
        Assert.Equal(
            "Cartão da bilheteria lido no leitor da frente; ele só vale na fenda da urna.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.ForaDaUrna), SemContexto).OQueAconteceu);
        Assert.Equal(
            "Coloque o cartão na fenda da urna, por favor.",
            PorQueNegou.ExplicarNegativa(nameof(MotivoDoUso.ForaDaUrna), SemContexto).OQueDizer);
    }

    [Fact]
    public void Liberado_sem_giro_e_com_giro_tambem_se_explicam()
    {
        Assert.Equal(
            "Liberado às 19:44:07 na catraca 02; o sensor não confirmou o giro. Não conta como entrada.",
            PorQueNegou.ExplicarLiberadoSemGiro(SemContexto).OQueAconteceu);
        Assert.Contains("Não houve negação", PorQueNegou.ExplicarLiberadoComGiro(SemContexto).OQueAconteceu, StringComparison.Ordinal);
    }

    /// <summary>Invariante I5: mesma entrada, mesmo texto, byte a byte.</summary>
    [Theory]
    [MemberData(nameof(MotivosDeNegacao))]
    public void Mesma_entrada_mesmo_texto(string motivo) =>
        Assert.Equal(PorQueNegou.ExplicarNegativa(motivo, ContextoCompleto), PorQueNegou.ExplicarNegativa(motivo, ContextoCompleto with { }));

    [Theory]
    [InlineData(2, "leitor da frente")]
    [InlineData(3, "fenda da urna")]
    [InlineData(21, "leitor de QR")]
    [InlineData(6, "")]
    [InlineData(null, "")]
    public void Onde_foi_lido_sem_termo_do_sdk(int? origem, string esperado) =>
        Assert.Equal(esperado, PorQueNegou.OndeFoiLido(origem));

    [GeneratedRegex(@"\b(origem|retorno|inner|polling|dll|sdk|easyinner|complemento|reasoncode|ei-\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TermoDoSdk();

    // Identificador de código: MAIUSCULAS_COM_SUBLINHADO ou PalavrasColadasEmCamelo.
    [GeneratedRegex(@"\b([A-Z]{2,}_[A-Z_]+|[A-Z][a-z]+[A-Z][a-zA-Z]+)\b")]
    private static partial Regex NomeDeCodigo();
}
