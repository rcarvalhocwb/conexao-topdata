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

    /// <summary>Catracas simuladas, sem EasyInner.dll nem catraca física.</summary>
    public bool Simulacao { get; init; }

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

    /// <summary>
    /// DLLs de apoio da EasyInner.dll, na lista do manual de desenvolvimento. A <c>Inner.dll</c> é a da
    /// versão 6.x: sem ela, <c>AbrirPortaComunicacao</c> devolve 11 e a catraca nunca conecta (docs/61).
    /// As demais só são copiadas quando estão na mesma pasta da EasyInner.dll escolhida.
    /// </summary>
    public static readonly IReadOnlyList<string> DllsDeApoio =
        ["Inner.dll", "Inner2K.dll", "InnerTCP.dll", "InnerTCPLib.dll", "InnerPendrive.dll"];

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
            NomesDasCatracas: dados.Catracas.ToDictionary(c => c.Inner, c => c.Nome.Trim()),
            Simulacao: dados.Simulacao);
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
            Simulacao = c.Simulacao,
        };
    }

    /// <summary>
    /// Verifica o que o PC precisa ter. As sondas são injetadas para o teste não depender
    /// do Windows em que roda.
    /// </summary>
    /// <param name="existeArquivo">Se um arquivo existe.</param>
    /// <param name="net35Instalado">Se o .NET Framework 3.5 está habilitado; nulo se não dá para saber.</param>
    /// <param name="pastaDoWorker">Pasta onde o worker foi instalado.</param>
    /// <param name="simulacao">Modo simulação: a EasyInner.dll não é necessária.</param>
    /// <param name="firewallLiberado">
    /// Se a regra de entrada das catracas existe (<see cref="FirewallDasCatracas"/>); nulo se
    /// não deu para conferir.
    /// </param>
    public static IReadOnlyList<ItemDoAmbiente> VerificarAmbiente(
        Func<string, bool> existeArquivo,
        bool? net35Instalado,
        string pastaDoWorker,
        bool simulacao = false,
        bool? firewallLiberado = null)
    {
        ArgumentNullException.ThrowIfNull(existeArquivo);

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // Onde a EasyInner.dll de 32 bits pode estar para o worker x86 achá-la.
        var locaisDaDll = new[]
        {
            Path.Combine(pastaDoWorker, "EasyInner.dll"),
            Path.Combine(windows, "SysWOW64", "EasyInner.dll"),
            Path.Combine(windows, "System32", "EasyInner.dll"),
        };
        var dll = locaisDaDll.FirstOrDefault(existeArquivo);

        // A Inner.dll (apoio da versão 6.x) segue os mesmos lugares da EasyInner.dll.
        var dllDeApoio = new[]
        {
            Path.Combine(pastaDoWorker, "Inner.dll"),
            Path.Combine(windows, "SysWOW64", "Inner.dll"),
            Path.Combine(windows, "System32", "Inner.dll"),
        }.FirstOrDefault(existeArquivo);

        return
        [
            new ItemDoAmbiente(
                "SDK da Topdata (EasyInner.dll)",
                dll is not null || simulacao,
                simulacao && dll is null
                    ? "Não é necessário no modo simulação. Para usar catraca física, instale o SDK Inner Acesso."
                    : dll is not null
                    ? $"Encontrada em {dll}."
                    : "Não encontrada. Instale o SDK Inner Acesso da Topdata, ou clique em \"Localizar EasyInner.dll\" " +
                      "para copiá-la de onde estiver (pasta do SDK, pendrive)."),
            new ItemDoAmbiente(
                "DLL de apoio Inner.dll (versão 6.x)",
                simulacao || dllDeApoio is not null,
                simulacao && dllDeApoio is null
                    ? "Não é necessária no modo simulação."
                    : dllDeApoio is not null
                    ? $"Encontrada em {dllDeApoio}."
                    : "Não encontrada. Sem ela a catraca não conecta (retorno 11 ao abrir a porta). " +
                      "Clique em \"Localizar EasyInner.dll\" e escolha a da pasta \"Porta Fixa\" do SDK: " +
                      "ela copia a Inner.dll junto."),
            new ItemDoAmbiente(
                ".NET Framework 3.5",
                net35Instalado,
                net35Instalado switch
                {
                    true => "Habilitado.",
                    false => "Clique em \"Habilitar .NET Framework 3.5\". Sem ele a EasyInner.dll falha com retorno 8.",
                    _ => "Não foi possível verificar; clique em \"Habilitar .NET Framework 3.5\" por garantia.",
                }),
            new ItemDoAmbiente(
                ".NET 10",
                true,
                "Vem junto com o instalador; não é preciso instalar nada."),
            new ItemDoAmbiente(
                "Firewall do Windows (entrada das catracas)",
                simulacao ? true : firewallLiberado,
                simulacao
                    ? "Não é necessário no modo simulação."
                    : firewallLiberado switch
                    {
                        true => "Liberado para as catracas da rede local.",
                        false => "Bloqueado: as catracas não vão conseguir conectar. Clique em \"Liberar no firewall\".",
                        _ => "Não foi possível conferir. Clique em \"Liberar no firewall\" por garantia.",
                    }),
        ];
    }

    /// <summary>
    /// Copia a EasyInner.dll escolhida pelo instalador para a pasta do programa das catracas.
    /// </summary>
    /// <remarks>
    /// Recusa DLL de 64 bits antes de copiar: o programa das catracas é de 32 bits, e a DLL
    /// errada só apareceria na primeira conexão, como retorno 8.
    /// </remarks>
    /// <returns>O caminho da cópia.</returns>
    /// <exception cref="ArgumentException">Não é a EasyInner.dll de 32 bits.</exception>
    /// <exception cref="IOException">
    /// A da pasta é outra e está em uso pelo programa das catracas (pare a operação antes; ver
    /// <see cref="EasyInnerJaInstalada"/>).
    /// </exception>
    public static string CopiarEasyInner(string origem, string pastaDoWorker)
    {
        ConferirEasyInner(origem);

        Directory.CreateDirectory(pastaDoWorker);
        var destino = Path.Combine(pastaDoWorker, "EasyInner.dll");
        File.Copy(origem, destino, overwrite: true);
        return destino;
    }

    /// <summary>
    /// Copia da pasta da EasyInner.dll escolhida as DLLs de apoio que estiverem lá (<see cref="DllsDeApoio"/>).
    /// </summary>
    /// <remarks>
    /// Nenhuma DLL de apoio de 64 bits passa: o programa das catracas é de 32 bits, e a DLL errada só
    /// apareceria na primeira abertura de porta. Ausente na pasta de origem não é erro: a conferência do
    /// ambiente mostra a falta.
    /// </remarks>
    /// <returns>Os nomes copiados.</returns>
    /// <exception cref="ArgumentException">Uma das DLLs de apoio não é de 32 bits.</exception>
    public static IReadOnlyList<string> CopiarDllsDeApoio(string origem, string pastaDoWorker)
    {
        ConferirEasyInner(origem);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDoWorker);

        var copiadas = new List<string>();
        foreach (var nome in DllsDeApoioNaOrigem(origem))
        {
            var fonte = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(origem))!, nome);
            ConferirDe32Bits(fonte, nome);
            Directory.CreateDirectory(pastaDoWorker);
            File.Copy(fonte, Path.Combine(pastaDoWorker, nome), overwrite: true);
            copiadas.Add(nome);
        }

        return copiadas;
    }

    /// <summary>
    /// As DLLs de apoio que a pasta da origem tem e que a pasta do worker ainda não tem igual; é o que falta
    /// copiar. Vazia quando a EasyInner.dll e as de apoio já estão lá (o programa segue sem cópia).
    /// </summary>
    public static IReadOnlyList<string> DllsDeApoioPendentes(string origem, string pastaDoWorker)
    {
        ConferirEasyInner(origem);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDoWorker);

        var pastaDaOrigem = Path.GetDirectoryName(Path.GetFullPath(origem))!;
        return DllsDeApoioNaOrigem(origem)
            .Where(nome => !MesmoConteudo(Path.Combine(pastaDaOrigem, nome), Path.Combine(pastaDoWorker, nome)))
            .ToList();
    }

    private static IEnumerable<string> DllsDeApoioNaOrigem(string origem)
    {
        var pastaDaOrigem = Path.GetDirectoryName(Path.GetFullPath(origem))!;
        return DllsDeApoio.Where(nome => File.Exists(Path.Combine(pastaDaOrigem, nome)));
    }

    private static void ConferirDe32Bits(string arquivo, string nome)
    {
        if (MaquinaDoPe(arquivo) is not 0x014C)
        {
            throw new ArgumentException(
                $"A {nome} desta pasta não é de 32 bits. Use a do SDK Inner Acesso para Windows 32 bits.", arquivo);
        }
    }

    /// <summary>Verdadeiro quando os dois arquivos existem e têm o mesmo conteúdo (SHA-256).</summary>
    private static bool MesmoConteudo(string a, string b)
    {
        if (!File.Exists(a) || !File.Exists(b))
        {
            return false;
        }

        if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return fa.Length == fb.Length
            && System.Security.Cryptography.SHA256.HashData(fa).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(fb));
    }

    /// <summary>
    /// Verdadeiro quando a pasta das catracas já tem exatamente esta EasyInner.dll (mesmo conteúdo).
    /// </summary>
    /// <remarks>
    /// A DLL fica presa enquanto o programa das catracas roda: sobrescrever a mesma DLL dava
    /// "The process cannot access the file" (bancada de 15/10/2026), quando não havia nada a fazer.
    /// </remarks>
    /// <exception cref="ArgumentException">Não é a EasyInner.dll de 32 bits.</exception>
    public static bool EasyInnerJaInstalada(string origem, string pastaDoWorker)
    {
        ConferirEasyInner(origem);
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDoWorker);

        var destino = Path.Combine(pastaDoWorker, "EasyInner.dll");
        if (!File.Exists(destino))
        {
            return false;
        }

        if (string.Equals(Path.GetFullPath(origem), Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        using var a = new FileStream(origem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var b = new FileStream(destino, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return a.Length == b.Length
            && System.Security.Cryptography.SHA256.HashData(a).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(b));
    }

    private static void ConferirEasyInner(string origem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origem);

        if (!string.Equals(Path.GetFileName(origem), "EasyInner.dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Escolha o arquivo EasyInner.dll.", nameof(origem));
        }

        if (MaquinaDoPe(origem) is not 0x014C)
        {
            throw new ArgumentException(
                "Esta EasyInner.dll não é de 32 bits. Use a do SDK Inner Acesso para Windows 32 bits.", nameof(origem));
        }
    }

    /// <summary>O campo "Machine" do cabeçalho PE; nulo se o arquivo não é um executável do Windows.</summary>
    public static ushort? MaquinaDoPe(string arquivo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arquivo);

        using var fluxo = File.OpenRead(arquivo);
        using var leitor = new BinaryReader(fluxo);

        if (fluxo.Length < 0x40 || leitor.ReadUInt16() != 0x5A4D)
        {
            return null;
        }

        fluxo.Position = 0x3C;
        var inicio = leitor.ReadInt32();
        if (inicio <= 0 || inicio + 6 > fluxo.Length)
        {
            return null;
        }

        fluxo.Position = inicio;
        if (leitor.ReadUInt32() != 0x00004550)
        {
            return null;
        }

        return leitor.ReadUInt16();
    }

    private static ConfiguracaoDaNuvem Nuvem(DadosDaInstalacao dados) =>
        new(
            Base: dados.BaseDaNuvem.Trim(),
            Dispositivo: dados.Dispositivo.Trim(),
            Perfil: dados.Perfil,
            IntervaloDeReusoSegundos: dados.IntervaloDeReusoSegundos,
            SomenteNaUrna: dados.SomenteNaUrna);
}
