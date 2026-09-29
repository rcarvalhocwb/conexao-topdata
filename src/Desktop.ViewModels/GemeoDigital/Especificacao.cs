using System.Text.Json;

namespace Desktop.ViewModels.GemeoDigital;

/// <summary>Quem fez e qual variante é o equipamento desenhado.</summary>
public sealed record IdentificacaoDoEquipamento
{
    public required string Fabricante { get; init; }

    public required string Modelo { get; init; }

    public required string Variante { get; init; }
}

/// <summary>Medidas usadas para desenhar a catraca, em milímetros.</summary>
/// <remarks>
/// Aproximadas. A origem está em <see cref="EspecificacaoDaFit4.FonteDasMedidas"/>, e é
/// ela que diz se já foram conferidas numa catraca de verdade.
/// </remarks>
public sealed record MedidasDaFit4
{
    public required double LarguraDoCorpo { get; init; }

    public required double ProfundidadeDoCorpo { get; init; }

    /// <summary>Do piso até o topo da tampa, sem leitor facial.</summary>
    public required double AlturaDaTampa { get; init; }

    /// <summary>Altura da cabeça (a tampa com os leitores), medida de cima para baixo.</summary>
    public required double AlturaDaCabeca { get; init; }

    /// <summary>Do piso até o topo do leitor facial, na variante que o tem.</summary>
    public required double AlturaComLeitorFacial { get; init; }

    /// <summary>Do piso até o centro do cubo dos braços.</summary>
    public required double AlturaDoEixo { get; init; }

    public required double ComprimentoDoBraco { get; init; }

    public required double DiametroDoBraco { get; init; }
}

/// <summary>Quais peças opcionais a variante tem.</summary>
public sealed record PecasPresentes
{
    public required bool LeitorQr { get; init; }

    public required bool LeitorDeProximidade { get; init; }

    public required bool Teclado { get; init; }

    public required bool Urna { get; init; }

    public required bool LeitorFacial { get; init; }
}

/// <summary>O display da tampa.</summary>
public sealed record DisplayDaFit4
{
    public required int Linhas { get; init; }

    public required int Colunas { get; init; }
}

/// <summary>O mecanismo de giro.</summary>
public sealed record RotorDaFit4
{
    public required int Bracos { get; init; }

    /// <summary>Inclinação do eixo em relação ao piso.</summary>
    public required double InclinacaoDoEixoGraus { get; init; }

    /// <summary>Quanto dura, no desenho, um terço de volta.</summary>
    public required int DuracaoDoGiroMs { get; init; }
}

/// <summary>
/// A "planta" da TopFit 4 que o gêmeo digital desenha: medidas, peças da variante, display
/// e rotor. Vem de um JSON embutido (<c>GemeoDigital/fit4.json</c>), e não de números
/// espalhados pelo código.
/// </summary>
public sealed record EspecificacaoDaFit4
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public required int Versao { get; init; }

    public required IdentificacaoDoEquipamento Equipamento { get; init; }

    /// <summary>
    /// De onde vieram as medidas. Começa com <c>A_CONFIRMAR</c> enquanto ninguém mediu uma
    /// catraca de verdade.
    /// </summary>
    public required string FonteDasMedidas { get; init; }

    public required MedidasDaFit4 MedidasMm { get; init; }

    public required PecasPresentes Pecas { get; init; }

    public required DisplayDaFit4 Display { get; init; }

    public required RotorDaFit4 Rotor { get; init; }

    /// <summary>Verdadeiro enquanto as medidas não foram conferidas numa catraca.</summary>
    public bool MedidasAConfirmar => FonteDasMedidas.StartsWith("A_CONFIRMAR", StringComparison.Ordinal);

    /// <summary>A especificação embutida no programa.</summary>
    public static EspecificacaoDaFit4 Padrao { get; } = CarregarEmbutida();

    /// <summary>Lê uma especificação. Falha de formato ou de limite vira exceção com o motivo.</summary>
    public static EspecificacaoDaFit4 Ler(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        EspecificacaoDaFit4? lida;
        try
        {
            lida = JsonSerializer.Deserialize<EspecificacaoDaFit4>(json, Opcoes);
        }
        catch (JsonException erro)
        {
            throw new FormatException($"Especificação da catraca ilegível: {erro.Message}", erro);
        }

        if (lida is null)
        {
            throw new FormatException("Especificação da catraca vazia.");
        }

        var problemas = lida.Validar();
        if (problemas.Count > 0)
        {
            throw new FormatException("Especificação da catraca inválida: " + string.Join(" ", problemas));
        }

        return lida;
    }

    /// <summary>Confere os limites. Lista vazia quando está tudo certo.</summary>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();
        var m = MedidasMm;

        if (Versao != 1)
        {
            problemas.Add($"Versão {Versao} desconhecida; esperada 1.");
        }

        if (m.LarguraDoCorpo is < 100 or > 1000 || m.ProfundidadeDoCorpo is < 100 or > 1000)
        {
            problemas.Add("Largura e profundidade do corpo vão de 100 a 1000 mm.");
        }

        if (m.AlturaDaTampa is < 500 or > 2000)
        {
            problemas.Add("A altura da tampa vai de 500 a 2000 mm.");
        }

        if (m.AlturaDaCabeca < 80 || m.AlturaDaCabeca > m.AlturaDaTampa / 2)
        {
            problemas.Add("A cabeça precisa ter ao menos 80 mm e no máximo metade da altura da tampa.");
        }

        if (m.AlturaDoEixo <= 0 || m.AlturaDoEixo >= m.AlturaDaTampa)
        {
            problemas.Add("O eixo dos braços precisa ficar abaixo do topo da tampa.");
        }

        if (m.AlturaComLeitorFacial < m.AlturaDaTampa)
        {
            problemas.Add("A altura com leitor facial não pode ser menor que a da tampa.");
        }

        if (m.ComprimentoDoBraco is < 100 or > 1000 || m.DiametroDoBraco is < 10 or > 100)
        {
            problemas.Add("Braço: comprimento de 100 a 1000 mm, diâmetro de 10 a 100 mm.");
        }

        // O display da linha 4 tem 2 x 16. A mensagem padrão tem até 32 caracteres (docs/32):
        // um display de outro tamanho quebraria a prévia sem ninguém notar.
        if (Display.Linhas * Display.Colunas != 32)
        {
            problemas.Add($"Display de {Display.Linhas} x {Display.Colunas}: a mensagem da catraca tem 32 caracteres.");
        }

        if (Rotor.Bracos != 3)
        {
            problemas.Add("A TopFit 4 tem três braços.");
        }

        // Com o eixo deitado (0) ou em pé (90) não existe tripé: os braços não conseguem ter
        // um deles na horizontal.
        if (Rotor.InclinacaoDoEixoGraus is < 10 or > 80)
        {
            problemas.Add("A inclinação do eixo vai de 10 a 80 graus.");
        }

        if (Rotor.DuracaoDoGiroMs is < 200 or > 5000)
        {
            problemas.Add("A duração do giro no desenho vai de 200 a 5000 ms.");
        }

        return problemas;
    }

    private static EspecificacaoDaFit4 CarregarEmbutida()
    {
        var assembly = typeof(EspecificacaoDaFit4).Assembly;
        var nome = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("GemeoDigital.fit4.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("fit4.json não foi embutido no Desktop.ViewModels.");

        using var fluxo = assembly.GetManifestResourceStream(nome)
            ?? throw new InvalidOperationException("fit4.json não pôde ser aberto.");
        using var leitor = new StreamReader(fluxo);
        return Ler(leitor.ReadToEnd());
    }
}
