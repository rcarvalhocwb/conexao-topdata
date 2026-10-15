using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Access.Infrastructure.SQLite;

/// <summary>O que uma licença diz (docs/60 §1.1).</summary>
/// <param name="Numero">Número da licença, para o suporte achar (ex.: <c>L-20261015-3F2A</c>).</param>
/// <param name="Cliente">Para quem foi emitida; aparece no painel.</param>
/// <param name="Instalacao">O identificador da instalação, ou <c>*</c> para qualquer computador.</param>
/// <param name="VenceEm">Vale até o fim deste dia, no horário de Brasília.</param>
/// <param name="EmitidaEm">Quando a Rayzer emitiu.</param>
public sealed record DadosDaLicenca(string Numero, string Cliente, string Instalacao, DateOnly VenceEm, DateTimeOffset EmitidaEm);

/// <summary>A situação da licença neste computador (docs/60 §1.2).</summary>
public enum SituacaoDaLicencaLocal
{
    /// <summary>A versão não tem chave pública: a licença não é exigida.</summary>
    NaoExigida,

    /// <summary>Faltam mais de <see cref="Licencas.AvisoEmDias"/> dias.</summary>
    Valida,

    /// <summary>Faltam <see cref="Licencas.AvisoEmDias"/> dias ou menos.</summary>
    VenceEmBreve,

    /// <summary>Venceu (ou não há licença) há até <see cref="Licencas.ToleranciaEmDias"/> dias: tudo funciona.</summary>
    EmTolerancia,

    /// <summary>Passou da tolerância: o administrativo para; as catracas nunca.</summary>
    Bloqueada,
}

/// <summary>A licença avaliada agora.</summary>
/// <param name="Situacao">A situação.</param>
/// <param name="Dados">A licença instalada e válida para esta instalação; nulo se não há.</param>
/// <param name="Dias">
/// Em <see cref="SituacaoDaLicencaLocal.Valida"/> e <see cref="SituacaoDaLicencaLocal.VenceEmBreve"/>: dias até
/// vencer (0 = vence hoje). Em tolerância: dias que faltam para bloquear. Bloqueada: dias desde o bloqueio.
/// </param>
/// <param name="Mensagem">A frase para o operador.</param>
/// <param name="Problema">Por que o texto instalado não vale (assinatura, outra instalação); nulo se vale.</param>
public sealed record EstadoDaLicenca(SituacaoDaLicencaLocal Situacao, DadosDaLicenca? Dados, int Dias, string Mensagem, string? Problema = null)
{
    /// <summary>Verdadeiro quando o administrativo deve ser recusado.</summary>
    public bool Bloqueada => Situacao is SituacaoDaLicencaLocal.Bloqueada;

    /// <summary>Verdadeiro quando não há licença válida instalada.</summary>
    public bool SemLicenca => Dados is null && Situacao is not SituacaoDaLicencaLocal.NaoExigida;
}

/// <summary>
/// Licença do XAcess: um texto assinado pela Rayzer com a chave do suporte, conferido aqui, sem internet
/// (docs/60). <c>XAL1.&lt;dados&gt;.&lt;assinatura&gt;</c>, os dois em base64url; os dados são um JSON.
/// </summary>
/// <remarks>
/// <para>
/// A assinatura cobre <c>XAL1.&lt;dados&gt;</c>: trocar o prefixo ou um byte dos dados invalida. Quem assina
/// é a chave do suporte (<see cref="ChavesDoSuporte"/>), que fica só na máquina do suporte.
/// </para>
/// <para>
/// Nada aqui para catraca: a licença só decide o administrativo (relatórios, atualizações, mudar o evento).
/// </para>
/// </remarks>
public static class Licencas
{
    /// <summary>Prefixo da versão 1 do formato.</summary>
    public const string Prefixo = "XAL1";

    /// <summary>A partir de quantos dias antes do vencimento o painel avisa.</summary>
    public const int AvisoEmDias = 30;

    /// <summary>Dias depois do vencimento (ou sem licença) em que tudo ainda funciona. Decisão do dono, 15/10/2026.</summary>
    public const int ToleranciaEmDias = 15;

    /// <summary>"Qualquer instalação".</summary>
    public const string QualquerInstalacao = "*";

    // Brasília sem horário de verão desde 2019: o vencimento é o fim do dia aqui.
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Monta e assina uma licença (ferramenta do suporte; o serviço nunca tem a chave privada).</summary>
    public static string Emitir(DadosDaLicenca dados, ECDsa chavePrivada)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(chavePrivada);

        var corpo = new CorpoDaLicenca
        {
            V = 1,
            Numero = dados.Numero,
            Cliente = dados.Cliente,
            Instalacao = dados.Instalacao,
            VenceEm = dados.VenceEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EmitidaEm = dados.EmitidaEm.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

        var assinado = $"{Prefixo}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(corpo, Json))}";
        var assinatura = chavePrivada.SignData(Encoding.ASCII.GetBytes(assinado), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{assinado}.{Base64Url(assinatura)}";
    }

    /// <summary>
    /// Lê e confere uma licença: formato, assinatura de uma das chaves e instalação. Devolve nulo e o motivo se
    /// não vale. Não olha o vencimento: isso é <see cref="Avaliar"/>.
    /// </summary>
    public static DadosDaLicenca? Ler(string? texto, string instalacao, IEnumerable<string> chavesPublicas, out string? problema)
    {
        ArgumentNullException.ThrowIfNull(chavesPublicas);
        problema = null;

        var partes = (texto ?? string.Empty).Trim().Split('.');
        if (partes.Length != 3 || partes[0] != Prefixo
            || DeBase64Url(partes[1]) is not { } bytes || DeBase64Url(partes[2]) is not { Length: 64 } assinatura)
        {
            problema = "O texto colado não é uma licença do Rayzer XAcess. Copie de novo, inteiro.";
            return null;
        }

        if (!AssinaturaConfere(Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}"), assinatura, chavesPublicas))
        {
            problema = "A assinatura da licença não confere. Ela foi alterada ou não é da Rayzer.";
            return null;
        }

        CorpoDaLicenca? corpo;
        try
        {
            corpo = JsonSerializer.Deserialize<CorpoDaLicenca>(bytes, Json);
        }
        catch (JsonException)
        {
            corpo = null;
        }

        if (corpo is not { V: 1 }
            || string.IsNullOrWhiteSpace(corpo.Cliente) || string.IsNullOrWhiteSpace(corpo.Instalacao)
            || !DateOnly.TryParseExact(corpo.VenceEm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var vence)
            || !DateTimeOffset.TryParse(corpo.EmitidaEm, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var emitida))
        {
            problema = "A licença está num formato que esta versão não conhece. Atualize o sistema ou peça outra à Rayzer.";
            return null;
        }

        if (corpo.Instalacao != QualquerInstalacao && !string.Equals(corpo.Instalacao, instalacao, StringComparison.OrdinalIgnoreCase))
        {
            problema = "Esta licença é de outro computador. Mande à Rayzer o identificador desta instalação (tela Diagnóstico).";
            return null;
        }

        return new DadosDaLicenca(corpo.Numero ?? string.Empty, corpo.Cliente.Trim(), corpo.Instalacao, vence, emitida);
    }

    /// <summary>A situação agora (docs/60 §1.2).</summary>
    /// <param name="texto">A licença instalada; nulo se não há.</param>
    /// <param name="instalacao">O identificador desta instalação.</param>
    /// <param name="agora">Agora.</param>
    /// <param name="primeiraPartida">Desde quando a licença é exigida aqui: conta a tolerância de quem não tem licença.</param>
    /// <param name="chavesPublicas">As chaves aceitas; padrão, as do suporte.</param>
    public static EstadoDaLicenca Avaliar(
        string? texto,
        string instalacao,
        DateTimeOffset agora,
        DateTimeOffset primeiraPartida,
        IReadOnlyList<string>? chavesPublicas = null)
    {
        var chaves = chavesPublicas ?? ChavesDoSuporte.Publicas;
        if (chaves.Count == 0)
        {
            return new EstadoDaLicenca(SituacaoDaLicencaLocal.NaoExigida, null, 0, "Esta versão não exige licença.");
        }

        var hoje = Dia(agora);
        string? problema = null;
        var dados = string.IsNullOrWhiteSpace(texto) ? null : Ler(texto, instalacao, chaves, out problema);

        if (dados is null)
        {
            var desde = hoje.DayNumber - Dia(primeiraPartida).DayNumber;
            var restam = ToleranciaEmDias - desde;
            var aviso = problema is null ? "Este computador ainda não tem licença." : "A licença instalada não vale aqui.";
            return restam >= 0
                ? new EstadoDaLicenca(SituacaoDaLicencaLocal.EmTolerancia, null, restam,
                    $"{aviso} Faltam {Dias(restam)} para bloquear relatórios, atualizações e mudanças no evento. As catracas não param.", problema)
                : new EstadoDaLicenca(SituacaoDaLicencaLocal.Bloqueada, null, -restam,
                    $"{aviso} Relatórios, atualizações e mudanças no evento estão bloqueados até instalar a licença. As catracas seguem funcionando.", problema);
        }

        var faltam = dados.VenceEm.DayNumber - hoje.DayNumber;
        var data = dados.VenceEm.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        if (faltam > AvisoEmDias)
        {
            return new EstadoDaLicenca(SituacaoDaLicencaLocal.Valida, dados, faltam, $"Licença válida até {data} ({dados.Cliente}).");
        }

        if (faltam >= 0)
        {
            var quando = faltam == 0 ? "vence hoje" : $"vence em {Dias(faltam)}, em {data}";
            return new EstadoDaLicenca(SituacaoDaLicencaLocal.VenceEmBreve, dados, faltam,
                $"A licença {quando}. Peça a renovação à Rayzer.");
        }

        var tolerancia = ToleranciaEmDias + faltam;
        return tolerancia >= 0
            ? new EstadoDaLicenca(SituacaoDaLicencaLocal.EmTolerancia, dados, tolerancia,
                $"A licença venceu em {data}. Faltam {Dias(tolerancia)} para bloquear relatórios, atualizações e mudanças no evento. As catracas não param.")
            : new EstadoDaLicenca(SituacaoDaLicencaLocal.Bloqueada, dados, -tolerancia,
                $"A licença venceu em {data}. Relatórios, atualizações e mudanças no evento estão bloqueados até a renovação. As catracas seguem funcionando.");
    }

    /// <summary>Confere uma assinatura ECDSA P-256 (IEEE P1363) contra qualquer das chaves públicas em base64.</summary>
    public static bool AssinaturaConfere(byte[] dados, byte[] assinatura, IEnumerable<string> chavesPublicas)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentNullException.ThrowIfNull(assinatura);
        ArgumentNullException.ThrowIfNull(chavesPublicas);

        foreach (var chave in chavesPublicas)
        {
            using var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(chave), out _);
            }
            catch (Exception erro) when (erro is FormatException or CryptographicException)
            {
                continue;
            }

            if (ecdsa.VerifyData(dados, assinatura, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return true;
            }
        }

        return false;
    }

    private static DateOnly Dia(DateTimeOffset instante) => DateOnly.FromDateTime(instante.ToOffset(Brasilia).DateTime);

    private static string Dias(int n) => n == 1 ? "1 dia" : $"{n} dias";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? DeBase64Url(string texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        var b64 = texto.Replace('-', '+').Replace('_', '/');
        b64 += (b64.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class CorpoDaLicenca
    {
        [JsonPropertyName("v")]
        public int V { get; set; }

        [JsonPropertyName("numero")]
        public string? Numero { get; set; }

        [JsonPropertyName("cliente")]
        public string? Cliente { get; set; }

        [JsonPropertyName("instalacao")]
        public string? Instalacao { get; set; }

        [JsonPropertyName("vence_em")]
        public string? VenceEm { get; set; }

        [JsonPropertyName("emitida_em")]
        public string? EmitidaEm { get; set; }
    }
}
