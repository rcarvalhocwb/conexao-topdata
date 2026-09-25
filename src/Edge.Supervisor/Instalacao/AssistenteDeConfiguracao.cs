using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Edge.Supervisor.Instalacao;

/// <summary>Uma catraca, como o instalador a informa.</summary>
/// <param name="Inner">Número do Inner configurado na catraca.</param>
/// <param name="Nome">Nome que aparece no painel ("Entrada 1").</param>
public sealed record CatracaInformada(int Inner, string Nome);

/// <summary>Tudo o que o assistente pergunta.</summary>
public sealed record DadosDaInstalacao
{
    public IReadOnlyList<CatracaInformada> Catracas { get; init; } = [];

    /// <summary>Porta TCP em que as catracas se conectam a este PC.</summary>
    public int Porta { get; init; } = 3570;

    public bool NuvemLigada { get; init; }

    public string BaseDaNuvem { get; init; } = string.Empty;

    public string Dispositivo { get; init; } = "borda-01";

    public string Perfil { get; init; } = "raw";

    public int IntervaloDeReusoSegundos { get; init; } = 240;

    public bool SomenteNaUrna { get; init; } = true;

    /// <summary>Segredo da nuvem. Nulo = manter o que já está no cofre.</summary>
    [JsonIgnore]
    public string? Segredo { get; init; }
}

/// <summary>Um item da verificação do ambiente.</summary>
/// <param name="Item">O que foi verificado, em português.</param>
/// <param name="Ok">Verdadeiro: ok; falso: impede a operação; nulo: conferir à mão.</param>
/// <param name="Orientacao">O que fazer.</param>
public sealed record ItemDoAmbiente(string Item, bool? Ok, string Orientacao);

/// <summary>
/// A lógica do assistente de configuração, separada da tela para poder ser testada fora do
/// Windows: valida o que o instalador informou, lê a configuração existente para edição e
/// grava a nova.
/// </summary>
/// <remarks>
/// O arquivo gravado é o mesmo <c>workers.json</c> que o serviço lê
/// (<see cref="ConfiguracaoDoSupervisor"/>); o segredo vai para o cofre, nunca para o arquivo.
/// </remarks>
public static class AssistenteDeConfiguracao
{
    /// <summary>Nome do único grupo que o assistente cria.</summary>
    public const string NomeDoGrupo = "catracas";

    /// <summary>Até quantas catracas um worker atende (ADR-0005).</summary>
    public const int CatracasPorGrupo = 20;

    private static readonly JsonSerializerOptions Escrita = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>O que impede gravar. Vazio = pode gravar.</summary>
    public static IReadOnlyList<string> Validar(DadosDaInstalacao dados)
    {
        ArgumentNullException.ThrowIfNull(dados);
        var problemas = new List<string>();

        if (dados.Catracas.Count == 0)
        {
            problemas.Add("Informe pelo menos uma catraca.");
        }

        if (dados.Catracas.Count > CatracasPorGrupo)
        {
            problemas.Add($"Um PC atende até {CatracasPorGrupo} catracas nesta versão.");
        }

        foreach (var catraca in dados.Catracas.Where(c => c.Inner is < 1 or > 99))
        {
            problemas.Add($"O número da catraca vai de 1 a 99 (recebido {catraca.Inner}).");
        }

        foreach (var repetido in dados.Catracas.GroupBy(c => c.Inner).Where(g => g.Count() > 1))
        {
            problemas.Add($"A catraca {repetido.Key} aparece mais de uma vez.");
        }

        foreach (var semNome in dados.Catracas.Where(c => string.IsNullOrWhiteSpace(c.Nome)))
        {
            problemas.Add($"Dê um nome à catraca {semNome.Inner}, como \"Entrada 1\".");
        }

        if (dados.Porta is < 1024 or > 65535)
        {
            problemas.Add("A porta vai de 1024 a 65535 (padrão 3570).");
        }

        if (dados.NuvemLigada)
        {
            problemas.AddRange(Nuvem(dados).Validar());

            if (dados.Segredo is { Length: > 0 and < 32 })
            {
                problemas.Add("O segredo da nuvem precisa ter pelo menos 32 caracteres.");
            }
        }

        return problemas;
    }

    /// <summary>A configuração que o serviço vai ler.</summary>
    /// <param name="dados">O que o instalador informou.</param>
    /// <param name="executavelDoWorker">Caminho absoluto do Edge.Worker.X86.exe instalado.</param>
    public static ConfiguracaoDoSupervisor Montar(DadosDaInstalacao dados, string executavelDoWorker)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentException.ThrowIfNullOrWhiteSpace(executavelDoWorker);

        return new ConfiguracaoDoSupervisor(
            Endereco: null,
            Grupos: [new GrupoConfigurado(NomeDoGrupo, dados.Porta, [.. dados.Catracas.Select(c => c.Inner).Order()], executavelDoWorker)],
            Banco: null,
            Nuvem: dados.NuvemLigada ? Nuvem(dados) : null,
            NomesDasCatracas: dados.Catracas.ToDictionary(c => c.Inner, c => c.Nome.Trim()));
    }

    /// <summary>
    /// Grava a configuração e o segredo. Recusa o que não passa em <see cref="Validar"/>.
    /// </summary>
    /// <returns>O caminho do arquivo gravado.</returns>
    public static string Gravar(
        DadosDaInstalacao dados,
        string arquivoDeConfiguracao,
        string executavelDoWorker,
        ICofreDeSegredos cofre)
    {
        ArgumentNullException.ThrowIfNull(dados);
        ArgumentException.ThrowIfNullOrWhiteSpace(arquivoDeConfiguracao);
        ArgumentNullException.ThrowIfNull(cofre);

        var problemas = Validar(dados);
        if (problemas.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problemas), nameof(dados));
        }

        var configuracao = Montar(dados, executavelDoWorker);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivoDeConfiguracao))!);

        // Grava num temporário e troca: o serviço nunca lê um arquivo pela metade.
        var temporario = arquivoDeConfiguracao + ".novo";
        File.WriteAllText(temporario, JsonSerializer.Serialize(configuracao, Escrita));
        File.Move(temporario, arquivoDeConfiguracao, overwrite: true);

        if (dados.NuvemLigada && dados.Segredo is { Length: > 0 } segredo)
        {
            cofre.Gravar(CabecalhoDeSegredo.NomeDoSegredo, segredo);
        }

        return arquivoDeConfiguracao;
    }

    /// <summary>Lê a configuração existente para o assistente abrir já preenchido.</summary>
    public static DadosDaInstalacao Carregar(string arquivoDeConfiguracao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arquivoDeConfiguracao);

        if (!File.Exists(arquivoDeConfiguracao))
        {
            return new DadosDaInstalacao();
        }

        var c = ConfiguracaoDoSupervisor.Ler(arquivoDeConfiguracao);
        var nomes = c.NomesDasCatracas ?? new Dictionary<int, string>();
        var grupo = c.Grupos.Count > 0 ? c.Grupos[0] : null;

        return new DadosDaInstalacao
        {
            Catracas = [.. c.Grupos.SelectMany(g => g.Inners).Order()
                .Select(i => new CatracaInformada(i, nomes.GetValueOrDefault(i, string.Create(CultureInfo.InvariantCulture, $"Catraca {i}"))))],
            Porta = grupo?.Porta ?? 3570,
            NuvemLigada = c.Nuvem is not null,
            BaseDaNuvem = c.Nuvem?.Base ?? string.Empty,
            Dispositivo = c.Nuvem?.Dispositivo ?? "borda-01",
            Perfil = c.Nuvem?.Perfil ?? "raw",
            IntervaloDeReusoSegundos = c.Nuvem?.IntervaloDeReusoSegundos ?? 240,
            SomenteNaUrna = c.Nuvem?.SomenteNaUrna ?? true,
        };
    }

    /// <summary>
    /// Verifica o que o PC precisa ter. As sondas são injetadas para o teste não depender
    /// do Windows em que roda.
    /// </summary>
    /// <param name="existeArquivo">Se um arquivo existe.</param>
    /// <param name="subpastas">Nomes das subpastas de uma pasta (vazio se não existe).</param>
    /// <param name="net35Instalado">Se o .NET Framework 3.5 está habilitado; nulo se não dá para saber.</param>
    /// <param name="pastaDoWorker">Pasta onde o worker foi instalado.</param>
    public static IReadOnlyList<ItemDoAmbiente> VerificarAmbiente(
        Func<string, bool> existeArquivo,
        Func<string, IEnumerable<string>> subpastas,
        bool? net35Instalado,
        string pastaDoWorker)
    {
        ArgumentNullException.ThrowIfNull(existeArquivo);
        ArgumentNullException.ThrowIfNull(subpastas);

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programas = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programas86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // Onde a EasyInner.dll de 32 bits pode estar para o worker x86 achá-la.
        var locaisDaDll = new[]
        {
            Path.Combine(pastaDoWorker, "EasyInner.dll"),
            Path.Combine(windows, "SysWOW64", "EasyInner.dll"),
            Path.Combine(windows, "System32", "EasyInner.dll"),
        };
        var dll = locaisDaDll.FirstOrDefault(existeArquivo);

        bool TemVersao10(string pasta) =>
            subpastas(pasta).Any(v => v.StartsWith("10.", StringComparison.Ordinal));

        var runtimeX86 = TemVersao10(Path.Combine(programas86, "dotnet", "shared", "Microsoft.NETCore.App"));
        var aspnetX64 = TemVersao10(Path.Combine(programas, "dotnet", "shared", "Microsoft.AspNetCore.App"));
        var desktopX64 = TemVersao10(Path.Combine(programas, "dotnet", "shared", "Microsoft.WindowsDesktop.App"));

        return
        [
            new ItemDoAmbiente(
                "SDK da Topdata (EasyInner.dll)",
                dll is not null,
                dll is not null
                    ? $"Encontrada em {dll}."
                    : "Não encontrada. Instale o SDK Inner Acesso da Topdata; a DLL não vem com este instalador."),
            new ItemDoAmbiente(
                ".NET Framework 3.5",
                net35Instalado,
                net35Instalado switch
                {
                    true => "Habilitado.",
                    false => "Habilite em 'Ativar ou desativar recursos do Windows'. Sem ele a EasyInner.dll falha com retorno 8.",
                    _ => "Não foi possível verificar; confira em 'Ativar ou desativar recursos do Windows'.",
                }),
            new ItemDoAmbiente(
                ".NET 10 de 32 bits (programa das catracas)",
                runtimeX86,
                runtimeX86 ? "Instalado." : "Instale o \".NET 10 Runtime\" x86. O programa das catracas é de 32 bits por causa da EasyInner.dll."),
            new ItemDoAmbiente(
                "ASP.NET Core 10 de 64 bits (serviço)",
                aspnetX64,
                aspnetX64 ? "Instalado." : "Instale o \"ASP.NET Core Runtime 10\" x64."),
            new ItemDoAmbiente(
                ".NET Desktop 10 de 64 bits (painel)",
                desktopX64,
                desktopX64 ? "Instalado." : "Instale o \".NET Desktop Runtime 10\" x64."),
        ];
    }

    private static ConfiguracaoDaNuvem Nuvem(DadosDaInstalacao dados) =>
        new(
            Base: dados.BaseDaNuvem.Trim(),
            Dispositivo: dados.Dispositivo.Trim(),
            Perfil: dados.Perfil,
            IntervaloDeReusoSegundos: dados.IntervaloDeReusoSegundos,
            SomenteNaUrna: dados.SomenteNaUrna);
}
