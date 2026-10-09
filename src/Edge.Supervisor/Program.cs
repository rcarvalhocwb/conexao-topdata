using System.Globalization;
using Access.Infrastructure.SQLite;
using Contracts;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    new CofreDpapi(InstalacaoLocal.PastaDosSegredos).Gravar(CabecalhoDeSegredo.NomeDoSegredo, segredo);
    Console.WriteLine("Segredo da nuvem gravado no cofre.");
    return 0;
}

// Pasta da instalação (workers.json, token, cofre, banco) só para SYSTEM e Administradores (S04).
// Roda ANTES de ler workers.json e o token: o serviço roda como SYSTEM e inicia o executável que o
// workers.json indica, então não pode ler esses arquivos com a permissão herdada do ProgramData,
// em que um usuário comum cria arquivos (achado E8-2 do docs/41). Uma falha aqui não impede a subida:
// o motivo vai para o registro assim que ele abrir.
// Como SYSTEM, a restrição também troca o dono: quem criou a pasta ou o workers.json antes da
// instalação continuaria dono e reabriria a permissão depois.
var falhasDaRestricao = new List<string>();
System.Security.Principal.SecurityIdentifier? donoDoServico = null;
if (OperatingSystem.IsWindows())
{
    donoDoServico = SegurancaLocal.DonoQuandoRodaComoSistema();
    Directory.CreateDirectory(InstalacaoLocal.PastaDeDados);
    falhasDaRestricao.AddRange(SegurancaLocal.RestringirPastaDeDados(
        [new SegurancaLocal.AlvoDaRestricao(InstalacaoLocal.PastaDeDados, Pasta: true)], donoDoServico));
}

// Erros da partida, antes do registro do serviço abrir: rodando como serviço não há console, e o
// técnico do RB-01 abre registros\servico-AAAA-MM-DD.log. Vão para lá também (achado E10-6 do docs/41).
var registroDaPartida = new Edge.Worker.Operacao.RegistroEmArquivo(
    Path.Combine(InstalacaoLocal.PastaDeDados, "registros"), "servico");

void FalhaNaPartida(string linha)
{
    Console.Error.WriteLine(linha);
    registroDaPartida.Escrever("PARTIDA " + linha);
}

// O que continuar com dono desconhecido depois da troca foi preparado para impedi-la: o serviço não lê
// esses arquivos nem sobe workers (achado E8-2 do docs/41). Só como SYSTEM, que é como o serviço roda.
if (OperatingSystem.IsWindows() && donoDoServico is not null)
{
    var suspeitos = SegurancaLocal.ComDonoDesconhecido(
        [InstalacaoLocal.PastaDeDados, InstalacaoLocal.ArquivoDeConfiguracao, InstalacaoLocal.ArquivoDoToken]);
    if (suspeitos.Count > 0)
    {
        FalhaNaPartida(
            "Arquivos da instalação com dono que não é SYSTEM nem Administradores, e o serviço não conseguiu " +
            "corrigir. Alguém pode ter preparado a pasta para controlar o serviço. Como administrador, apague " +
            $"{InstalacaoLocal.PastaDeDados} (guarde antes acesso.db e a pasta copias) e rode o assistente de novo:");
        foreach (var suspeito in suspeitos)
        {
            FalhaNaPartida($"  - {suspeito}");
        }

        return 1;
    }
}

// Configuração: EDGE_CONFIG (desenvolvimento), a da pasta de dados (instalação), ou a
// que estiver ao lado do executável.
var caminhoDaConfig = Environment.GetEnvironmentVariable("EDGE_CONFIG")
    ?? (File.Exists(InstalacaoLocal.ArquivoDeConfiguracao)
        ? InstalacaoLocal.ArquivoDeConfiguracao
        : Path.Combine(AppContext.BaseDirectory, "workers.json"));

// Sem configuração o serviço SOBE mesmo assim, sem catracas: o painel abre e diz o que
// falta ("rode o assistente de configuração"), em vez de o serviço falhar em silêncio
// logo depois da instalação.
var semConfiguracao = !File.Exists(caminhoDaConfig);
ConfiguracaoDoSupervisor configuracao;

if (semConfiguracao)
{
    configuracao = new ConfiguracaoDoSupervisor(null, []);
    Console.Error.WriteLine(
        $"Instalação ainda não configurada ({caminhoDaConfig} não existe). " +
        "O serviço sobe sem catracas; rode o Assistente de configuração.");
}
else
{
    try
    {
        configuracao = ConfiguracaoDoSupervisor.Ler(caminhoDaConfig);
    }
    catch (Exception erro) when (erro is IOException or InvalidDataException or System.Text.Json.JsonException)
    {
        FalhaNaPartida($"Não foi possível ler {caminhoDaConfig}: {erro.Message}");
        return 1;
    }

    // EDGE_PASTA_DO_PROGRAMA: só para rodar do código-fonte, com o worker em outra pasta de build.
    var problemas = configuracao.Validar(Environment.GetEnvironmentVariable("EDGE_PASTA_DO_PROGRAMA"));

    if (problemas.Count > 0)
    {
        FalhaNaPartida($"{problemas.Count} problema(s) na configuração ({caminhoDaConfig}):");

        foreach (var problema in problemas)
        {
            FalhaNaPartida($"  - {problema}");
        }

        return 1;
    }
}

// Token de sessão: além da ACL do named pipe, que protege por identidade de usuário e não
// separa processos da mesma conta. Gerado na primeira subida e reaproveitado depois.
var token = SegurancaLocal.GarantirToken(InstalacaoLocal.ArquivoDoToken, donoDoServico);

var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO")
    ?? configuracao.Endereco
    ?? TransporteLocal.EnderecoPadrao();

// A base local é o canal entre os workers e este serviço (ADR-0024). O serviço aplica as
// migrações ANTES de subir qualquer worker, para que nenhum deles corra para aplicá-las
// ao mesmo tempo.
var caminhoDoBanco = configuracao.CaminhoDoBanco;
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminhoDoBanco))!);

var fabrica = new SqliteConnectionFactory(caminhoDoBanco);
try
{
    var migrador = new Migrator(fabrica);

    // Atualização com migração nova: uma cópia da base ANTES, em copias\antes-da-migracao (fora da
    // retenção das 14), para haver como voltar à versão anterior (achado E10-7 do docs/41). Se a cópia
    // falhar, o motivo fica no registro e a migração segue: o serviço não pode ficar parado por isso.
    var pendentes = migrador.PendentesNumaBaseExistente();
    if (pendentes.Count > 0)
    {
        try
        {
            var copia = new CopiaDeSeguranca(fabrica, Path.Combine(configuracao.PastaDeDados, "copias", "antes-da-migracao"))
                .Criar(DateTimeOffset.Now);
            registroDaPartida.Escrever($"PARTIDA cópia antes de {pendentes.Count} migração(ões) nova(s): {copia}");
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            FalhaNaPartida($"Não foi possível copiar a base antes das migrações ({erro.GetType().Name}: {erro.Message}); seguindo sem cópia.");
        }
    }

    migrador.Aplicar();
}
catch (Exception erro) when (erro is not OutOfMemoryException)
{
    // Banco ilegível, disco cheio ou migração que falha: o serviço não sobe sem base, e o motivo
    // tem de ficar onde o técnico procura (RB-01 e RB-09).
    FalhaNaPartida($"Não foi possível abrir ou atualizar a base local {caminhoDoBanco}: {erro.GetType().Name}: {erro.Message}");
    return 1;
}

// Banco fora da pasta da instalação ("banco" no workers.json): restringe só o que o serviço cria ao
// lado dele (arquivos do banco, copias, registros), nunca a pasta do banco, que pode ser a raiz de um
// disco (achado E8-3). Depois da migração, para o arquivo do banco já existir.
if (OperatingSystem.IsWindows())
{
    Directory.CreateDirectory(Path.Combine(configuracao.PastaDeDados, "copias"));
    Directory.CreateDirectory(Path.Combine(configuracao.PastaDeDados, "registros"));
    falhasDaRestricao.AddRange(SegurancaLocal.RestringirPastaDeDados(
        SegurancaLocal.AlvosDaRestricao(InstalacaoLocal.PastaDeDados, caminhoDoBanco).Skip(1), donoDoServico));
}

// A camada inteligente nasce DESLIGADA: sem linha em edge_setting, nada liga (ChavesDaInteligencia).
// Não se chama InicializacaoDasChavesDaInteligencia aqui. Ligá-la por padrão é a Etapa I.11, só
// depois de NOVO-LOAD-IA-01, NOVO-CHAOS-IA-01 e NOVO-SOAK-IA-24H; até lá, a chave não é gravada.

// A chave da impressão de código (Etapa B.1) vai para o worker pela entrada padrão: é com ela
// que ele grava os bilhetes coletados sem o código em claro (Etapa A.9, migração 015). Só no
// Windows, onde existe o cofre (DPAPI). Sem ela, o worker sobe e opera, e a coleta de
// bilhetes é recusada por ele — nada sai da memória da catraca.
string? chaveDaImpressao = null;
if (OperatingSystem.IsWindows())
{
    try
    {
        chaveDaImpressao = ChaveDaImpressao.ParaOWorker(new CofreDpapi(InstalacaoLocal.PastaDosSegredos));
    }
    catch (Exception erro) when (erro is InvalidOperationException or IOException or UnauthorizedAccessException
        or System.Security.Cryptography.CryptographicException)
    {
        Console.Error.WriteLine($"Chave da impressão de código indisponível ({erro.GetType().Name}): a coleta de bilhetes fica recusada.");
    }
}

// Esta partida do serviço (migração 016). Cada worker grava a situação das catracas com ela, e
// o serviço só acredita na situação desta partida: um worker órfão de uma partida anterior —
// o serviço morreu sem encerrá-lo — não aparece mais como "Atendendo" (docs/29, defeito de 01/10).
var sessaoDoServico = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
var pidDoServico = Environment.ProcessId;

// O worker morre com o serviço: Job Object no Windows (o kernel mata os workers quando o
// serviço some, de qualquer jeito) e, em todo sistema, a vigia do pai no worker (--pai).
var contencao = ContencaoDosWorkers.Criar();

// Batimento dos workers (achado E2-01): a situação que cada um grava na base a cada 2 s, só desta
// partida do serviço. O mais recente entre as catracas do grupo.
var operacao = new Operacao(fabrica);

DateTimeOffset? UltimaNoticiaDoGrupo(IReadOnlyList<int> inners) =>
    operacao.ListarSituacao()
        .Where(s => string.Equals(s.Sessao, sessaoDoServico, StringComparison.Ordinal) && inners.Contains(s.Inner))
        .Select(s => (DateTimeOffset?)s.AtualizadoEm)
        .DefaultIfEmpty()
        .Max();

var workers = configuracao.Grupos
    .Select(g => new ProcessoDeWorker(
        g.Nome,
        g.Porta,
        g.Inners,
        ConfiguracaoDoSupervisor.ResolverExecutavel(g.Executavel),
        argumentosExtras: [
            "--banco", caminhoDoBanco, "--worker", g.Nome,
            "--sessao", sessaoDoServico,
            "--pai", pidDoServico.ToString(CultureInfo.InvariantCulture),
            .. configuracao.Simulacao ? ["--simulador"] : Array.Empty<string>(),
            .. chaveDaImpressao is null ? Array.Empty<string>() : ["--chave-da-impressao-na-entrada"]],
        entradaPadrao: chaveDaImpressao is null ? null : () => chaveDaImpressao,
        contencao: contencao,
        ultimaNoticia: UltimaNoticiaDoGrupo))
    .ToList();

// Modo simulação: ingressos e cartões de teste carregados a cada partida (idempotente).
LeiturasSimuladas? leiturasSimuladas = null;
if (configuracao.Simulacao)
{
    leiturasSimuladas = new LeiturasSimuladas(fabrica);
    var exemplo = Path.Combine(AppContext.BaseDirectory, "simulacao.exemplo.json");

    if (File.Exists(exemplo))
    {
        var carga = ArquivoDeBancada.Carregar(File.ReadAllText(exemplo), new RepositorioDeIngressos(fabrica), DateTimeOffset.UtcNow);
        Console.WriteLine($"MODO SIMULAÇÃO: {carga.Ingressos} ingresso(s) e {carga.Cartoes} cartão(ões) de teste carregados.");
    }
}

var supervisor = new WorkerSupervisor(workers);
var nuvem = new EstadoDaNuvem();
var registro = new Edge.Worker.Operacao.RegistroEmArquivo(
    Path.Combine(configuracao.PastaDeDados, "registros"), "servico");

void Registrar(string linha)
{
    registro.Escrever(linha);
    Console.WriteLine(linha);
}

foreach (var falha in falhasDaRestricao)
{
    Registrar($"permissão da pasta de dados não aplicada: {falha}");
}

Registrar($"partida {sessaoDoServico} · contenção dos workers: {contencao.Descricao}");

// Antes de subir os workers desta partida: encerrar os que uma partida anterior deixou órfãos.
// Só os desta instalação (mesmo executável, caminho completo) cujo pai não existe mais.
if (OperatingSystem.IsWindows())
{
    foreach (var linha in FaxinaDeOrfaos.Executar(
        configuracao.Grupos.Select(g => ConfiguracaoDoSupervisor.ResolverExecutavel(g.Executavel)),
        pidDoServico,
        new ProcessosDoWindows()))
    {
        Registrar("faxina: " + linha);
    }
}

SincronizacaoComANuvem? sincronizacao = null;
if (configuracao.Nuvem is { } configuracaoDaNuvem)
{
    // Segredo ilegível vale como ausente: a nuvem para e as catracas sobem (achado E2-04 do docs/41).
    var cofre = new CofreQueNaoDerruba(
        OperatingSystem.IsWindows() ? new CofreDpapi(InstalacaoLocal.PastaDosSegredos) : new CofreEmMemoria(),
        Registrar);

    // Montar antes de subir os workers: é aqui que o espelho das tentativas é ligado na
    // configuração que eles leem ao subir. Uma falha aqui deixa a borda sem nuvem, não sem catracas.
    try
    {
        sincronizacao = SincronizacaoComANuvem.Montar(configuracaoDaNuvem, fabrica, cofre, nuvem, Registrar);
    }
    catch (Exception erro) when (erro is not OutOfMemoryException)
    {
        FalhaNaPartida($"nuvem: não foi possível preparar a sincronização ({erro.GetType().Name}: {erro.Message}); a borda opera sem nuvem até o serviço reiniciar.");
    }

    if (sincronizacao is not null && cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo) is null)
    {
        Registrar("nuvem: nenhum segredo legível; a sincronização fica parada até ele ser gravado (achados E8-1 e E2-04 do docs/41).");
    }
}

var construtor = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,

    // Como serviço, a pasta atual é System32; o conteúdo do programa está ao lado do exe.
    ContentRootPath = AppContext.BaseDirectory,
});

// Avisos e erros do ILogger (cópia, retenção, supervisão, falha de um laço) também no registro
// do serviço em arquivo, que é o que o suporte abre (E2-05, E10-6).
construtor.Logging.AddProvider(new ProvedorDeRegistroEmArquivo(registro.Escrever));

// Rede final: os laços em segundo plano já não deixam exceção escapar (LacoResiliente). Se uma
// escapar mesmo assim, ela fica registrada e o resto do serviço segue atendendo as catracas, em vez
// de o host parar e encerrar todos os workers (padrão do .NET; achado E2-02).
construtor.Services.Configure<HostOptions>(opcoes =>
    opcoes.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// Responde ao gerenciador de serviços do Windows. Fora de um serviço, não faz nada.
construtor.Host.UseWindowsService(opcoes => opcoes.ServiceName = "ConexaoTopdataEdge");
construtor.WebHost.ConfigureKestrel(opcoes => TransporteLocal.Escutar(opcoes, endereco));

if (OperatingSystem.IsWindows())
{
    SegurancaLocal.AplicarNoCanal(construtor.WebHost);
}

construtor.Services.AddSingleton(supervisor);
construtor.Services.AddSingleton(fabrica);
construtor.Services.AddSingleton(operacao);
construtor.Services.AddSingleton(nuvem);
var configuracoesDaBorda = new ConfiguracoesDaBorda(fabrica);
construtor.Services.AddSingleton(configuracoesDaBorda);

// Camada inteligente (Etapa I.0 do docs/36): o Analisador lê acesso.db só para leitura e grava só
// em telemetria.db, ao lado. Desligado por padrão (chave inteligencia.ligada, lida na partida);
// uma exceção ou um ciclo travado dele não derruba o serviço nem toca no worker.
var analisador = new AnalisadorDaOperacao(new CicloSobreABase(
    new LeituraSomenteDaOperacao(caminhoDoBanco),
    new FabricaDaTelemetria(FabricaDaTelemetria.CaminhoAoLadoDe(caminhoDoBanco)),
    sessaoDoServico));
construtor.Services.AddSingleton(_ => new EdgeControlService(
    supervisor,
    // A versão que o CI grava ao publicar (0.1.N, a do Setup); no Diagnóstico.
    versao: typeof(EdgeControlService).Assembly.GetName().Version?.ToString(3),
    operacao: operacao,
    nuvem: nuvem,
    consultas: new ConsultasDaOperacao(fabrica),
    configuracoes: configuracoesDaBorda,
    pastaDeDados: configuracao.PastaDeDados,
    semConfiguracao: semConfiguracao,
    nomesDasCatracas: configuracao.NomesDasCatracas,
    simulacao: leiturasSimuladas,
    comandos: new FilaDeComandosSqlite(fabrica),
    chavesDosComandos: new ChavesDosComandos(fabrica),
    configuracoesDasCatracas: new ConfiguracoesDasCatracas(fabrica),
    configuracaoPorCatraca: new ConfiguracaoPorCatraca(fabrica),
    sessao: sessaoDoServico,
    mapasDeGiro: new MapasDeGiro(fabrica),
    analisador: analisador,
    filaDeSaida: new FilaDeSaidaSqlite(fabrica),
    estornos: new EstornosDeUso(fabrica)));
construtor.Services.AddGrpc(o => o.Interceptors.Add<InterceptadorDeToken>(token));
construtor.Services.AddHostedService<LacoDeSupervisao>();
construtor.Services.AddHostedService<ImpedirSuspensao>();
construtor.Services.AddHostedService<AcompanhamentoDaOperacao>();

// Cópia de segurança de acesso.db (A05): verificada, com retenção. Fica em PastaDeDados\copias.
construtor.Services.AddSingleton(new CopiaDeSeguranca(fabrica, Path.Combine(configuracao.PastaDeDados, "copias")));
construtor.Services.AddHostedService<AgendadorDeCopias>();

// Retenção dos registros do serviço: 30 dias e teto de 200 MB (ZeladorDeRegistros).
construtor.Services.AddHostedService(sp => new ZeladorDeRegistrosServico(
    Path.Combine(configuracao.PastaDeDados, "registros"),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ZeladorDeRegistrosServico>>()));
construtor.Services.AddHostedService(_ => analisador);

if (sincronizacao is not null)
{
    construtor.Services.AddHostedService(_ => sincronizacao);
}

var aplicacao = construtor.Build();
aplicacao.MapGrpcService<EdgeControlService>();

// Parar o serviço (inclusive na atualização pelo instalador) encerra os workers junto.
aplicacao.Lifetime.ApplicationStopping.Register(supervisor.Encerrar);

Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"Serviço local em {endereco} · {workers.Count} grupo(s) · {workers.Sum(w => w.Inners.Count)} equipamento(s) · base {caminhoDoBanco}"));

await aplicacao.RunAsync().ConfigureAwait(false);
return 0;
