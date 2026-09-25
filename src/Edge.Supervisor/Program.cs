using System.Globalization;
using Access.Infrastructure.SQLite;
using Contracts;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Serviço local: supervisiona os workers e atende o painel pelo IPC.
//
// NÃO carrega a EasyInner.dll e NÃO fala com equipamento. Quem faz isso é o worker x86,
// em processo separado, justamente para que este aqui sobreviva à morte dele.
// Ver docs/ADR/ADR-0001 e ADR-0004.

// Gravar o segredo da nuvem no cofre, lendo da entrada padrão para que ele não apareça
// na linha de comando nem no histórico do terminal. Usado pelo instalador.
if (args.Contains("--gravar-segredo-da-nuvem"))
{
    if (!OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("O cofre de segredos usa DPAPI e só existe no Windows.");
        return 1;
    }

    var segredo = Console.In.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(segredo) || segredo.Length < 32)
    {
        Console.Error.WriteLine("Segredo vazio ou curto demais (mínimo de 32 caracteres).");
        return 1;
    }

    new CofreDpapi(PastaDosSegredos()).Gravar(CabecalhoDeSegredo.NomeDoSegredo, segredo);
    Console.WriteLine("Segredo da nuvem gravado no cofre.");
    return 0;
}

var caminhoDaConfig = Environment.GetEnvironmentVariable("EDGE_CONFIG")
    ?? Path.Combine(AppContext.BaseDirectory, "workers.json");

if (!File.Exists(caminhoDaConfig))
{
    Console.Error.WriteLine(
        $"Configuração não encontrada em {caminhoDaConfig}. " +
        "Descreva os grupos em workers.json ou aponte EDGE_CONFIG para o arquivo. " +
        "Ver installer/README.md.");
    return 1;
}

ConfiguracaoDoSupervisor configuracao;

try
{
    configuracao = ConfiguracaoDoSupervisor.Ler(caminhoDaConfig);
}
catch (Exception erro) when (erro is IOException or InvalidDataException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Não foi possível ler {caminhoDaConfig}: {erro.Message}");
    return 1;
}

var problemas = configuracao.Validar();

if (problemas.Count > 0)
{
    Console.Error.WriteLine($"{problemas.Count} problema(s) na configuração:");

    foreach (var problema in problemas)
    {
        Console.Error.WriteLine($"  - {problema}");
    }

    return 1;
}

var token = Environment.GetEnvironmentVariable("EDGE_TOKEN");

if (string.IsNullOrWhiteSpace(token))
{
    // Sem token, qualquer processo da mesma conta alcançaria o serviço: a ACL do pipe
    // protege por identidade de usuário, não por processo.
    Console.Error.WriteLine(
        "EDGE_TOKEN não definido. Rode installer/instalar-dev.ps1 e exporte o token gerado.");
    return 1;
}

var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO")
    ?? configuracao.Endereco
    ?? TransporteLocal.EnderecoPadrao();

// A base local é o canal entre os workers e este serviço (ADR-0024). O serviço aplica as
// migrações ANTES de subir qualquer worker, para que nenhum deles corra para aplicá-las
// ao mesmo tempo.
var caminhoDoBanco = configuracao.CaminhoDoBanco;
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminhoDoBanco))!);
var fabrica = new SqliteConnectionFactory(caminhoDoBanco);
new Migrator(fabrica).Aplicar();

var workers = configuracao.Grupos
    .Select(g => new ProcessoDeWorker(
        g.Nome,
        g.Porta,
        g.Inners,
        g.Executavel,
        argumentosExtras: ["--banco", caminhoDoBanco, "--worker", g.Nome]))
    .ToList();

var supervisor = new WorkerSupervisor(workers);
var operacao = new Operacao(fabrica);
var nuvem = new EstadoDaNuvem();
var registro = new Edge.Worker.Operacao.RegistroEmArquivo(
    Path.Combine(configuracao.PastaDeDados, "registros"), "servico");

void Registrar(string linha)
{
    registro.Escrever(linha);
    Console.WriteLine(linha);
}

SincronizacaoComANuvem? sincronizacao = null;
if (configuracao.Nuvem is { } configuracaoDaNuvem)
{
    ICofreDeSegredos cofre = OperatingSystem.IsWindows()
        ? new CofreDpapi(PastaDosSegredos())
        : new CofreEmMemoria();

    // Montar antes de subir os workers: é aqui que o espelho das tentativas é ligado na
    // configuração que eles leem ao subir.
    sincronizacao = SincronizacaoComANuvem.Montar(configuracaoDaNuvem, fabrica, cofre, nuvem, Registrar);

    if (cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo) is null)
    {
        Registrar("nuvem: nenhum segredo gravado; as requisições saem sem credencial (docs/22, seção 8.1).");
    }
}

var construtor = WebApplication.CreateBuilder(args);
construtor.WebHost.ConfigureKestrel(opcoes => TransporteLocal.Escutar(opcoes, endereco));
construtor.Services.AddSingleton(supervisor);
construtor.Services.AddSingleton(fabrica);
construtor.Services.AddSingleton(operacao);
construtor.Services.AddSingleton(nuvem);
var configuracoesDaBorda = new ConfiguracoesDaBorda(fabrica);
construtor.Services.AddSingleton(configuracoesDaBorda);
construtor.Services.AddSingleton(_ => new EdgeControlService(
    supervisor,
    operacao: operacao,
    nuvem: nuvem,
    consultas: new ConsultasDaOperacao(fabrica),
    configuracoes: configuracoesDaBorda,
    pastaDeDados: configuracao.PastaDeDados));
construtor.Services.AddGrpc(o => o.Interceptors.Add<InterceptadorDeToken>(token));
construtor.Services.AddHostedService<LacoDeSupervisao>();
construtor.Services.AddHostedService<AcompanhamentoDaOperacao>();

if (sincronizacao is not null)
{
    construtor.Services.AddHostedService(_ => sincronizacao);
}

var aplicacao = construtor.Build();
aplicacao.MapGrpcService<EdgeControlService>();

Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"Serviço local em {endereco} · {workers.Count} grupo(s) · {workers.Sum(w => w.Inners.Count)} equipamento(s) · base {caminhoDoBanco}"));

await aplicacao.RunAsync().ConfigureAwait(false);
return 0;

static string PastaDosSegredos() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ConexaoTopdata",
    "segredos");
