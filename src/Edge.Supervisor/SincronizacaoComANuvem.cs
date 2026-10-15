using System.Globalization;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Microsoft.Extensions.Hosting;
using Sync.Connectors.Rest.Painel;
using Sync.Core;
using Sync.Ingestao;

namespace Edge.Supervisor;

/// <summary>Como esta borda fala com o painel na nuvem. Fica em <c>workers.json</c>.</summary>
/// <param name="Base">
/// Endereço das funções, terminado em <c>/</c>. Não é segredo, mas é do cliente: fica só
/// na máquina, nunca no repositório.
/// </param>
/// <param name="Dispositivo">Identificador desta borda no painel (<c>device_id</c>).</param>
/// <param name="Provedor">Provedor local que recebe os cartões da nuvem.</param>
/// <param name="Perfil">
/// Perfil do leitor aplicado aos números de cartão: <c>raw</c>, <c>mifare-catraca4</c> ou
/// <c>qr-catraca4</c>. O padrão é <c>raw</c> até a bancada dizer o formato (docs/22, 8.5).
/// </param>
/// <param name="IntervaloDeReusoSegundos">Cartão não volta antes disto (4 a 5 minutos).</param>
/// <param name="SomenteNaUrna">Cartão só vale na fenda da urna.</param>
/// <param name="Conector">Nome do conector das tentativas na outbox.</param>
/// <param name="EsperaPeloGiroSegundos">Quanto a liberação espera o giro antes de subir.</param>
/// <param name="IntervaloSegundos">De quanto em quanto tempo sincroniza.</param>
/// <param name="CabecalhoDoSegredo">Cabeçalho que leva o segredo; ver <see cref="CabecalhoDeSegredo"/>.</param>
public sealed record ConfiguracaoDaNuvem(
    string Base,
    string Dispositivo,
    string Provedor = "bilheteria-local",
    string Perfil = "raw",
    int IntervaloDeReusoSegundos = 240,
    bool SomenteNaUrna = true,
    string Conector = "painel-tentativas",
    int EsperaPeloGiroSegundos = 10,
    int IntervaloSegundos = 30,
    string CabecalhoDoSegredo = "Authorization")
{
    /// <summary>
    /// Perfis que podem ser escolhidos pelo nome: os mesmos do domínio, para que a
    /// sincronização, a consulta e a leitura conheçam exatamente a mesma lista.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, CredentialNormalization> Perfis = PerfisDeLeitura.Todos;

    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        if (!Uri.TryCreate(Base, UriKind.Absolute, out var endereco) || endereco.Scheme != Uri.UriSchemeHttps)
        {
            problemas.Add("nuvem.base precisa ser um endereço https completo.");
        }
        else if (!Base.EndsWith('/'))
        {
            // Sem a barra final, "functions/v1" + "middleware-sync-cards" vira
            // "functions/middleware-sync-cards" — erro silencioso de HttpClient.
            problemas.Add("nuvem.base precisa terminar com '/'.");
        }

        if (string.IsNullOrWhiteSpace(Dispositivo))
        {
            problemas.Add("nuvem.dispositivo é obrigatório.");
        }

        if (!Perfis.ContainsKey(Perfil))
        {
            problemas.Add($"nuvem.perfil '{Perfil}' não existe; use {string.Join(", ", Perfis.Keys)}.");
        }

        if (IntervaloDeReusoSegundos < 0)
        {
            problemas.Add("nuvem.intervaloDeReusoSegundos não pode ser negativo.");
        }

        if (IntervaloSegundos is < 5 or > 3600)
        {
            problemas.Add("nuvem.intervaloSegundos vai de 5 a 3600.");
        }

        return problemas;
    }
}

/// <summary>
/// A lista de cartões de uma rodada parece ter sido cortada pelo servidor (achado E5-1 do docs/41).
/// </summary>
/// <remarks>
/// A fonte só avisa por callback; a rodada lê este marcador para dar a rodada como falha, em vez de
/// "nuvem ok" com uns mil cartões faltando.
/// </remarks>
public sealed class SuspeitaDeCorte
{
    /// <summary>Quantos cartões vieram na lista suspeita, ou nulo se a última lista veio inteira.</summary>
    public int? Recebidos { get; set; }
}

/// <summary>O que uma rodada com a nuvem fez: o botão "Testar conexão com a nuvem" mostra isto.</summary>
/// <param name="Sucesso">Tudo certo: lista de cartões completa e nada adiado no envio.</param>
/// <param name="Respondeu">A nuvem respondeu, mesmo que com erro: a conexão existe.</param>
/// <param name="TipoDaFalha">O tipo mais grave de falha da rodada; <see cref="TipoDeFalha.Nenhuma"/> se não houve.</param>
/// <param name="ListaIncompletaCom">Quantos cartões vieram numa lista cortada; nulo se não houve corte.</param>
/// <param name="Novos">Cartões que entraram na base nesta rodada.</param>
/// <param name="Atualizados">Cartões que mudaram nesta rodada.</param>
/// <param name="Enviadas">Passagens que subiram.</param>
/// <param name="Adiadas">Passagens que ficaram na fila para a próxima vez.</param>
/// <param name="Motivo">O motivo da falha, sem segredo nem corpo de resposta; nulo se deu certo.</param>
/// <param name="Duracao">Quanto a rodada levou.</param>
public sealed record ResultadoDaRodada(
    bool Sucesso,
    bool Respondeu,
    TipoDeFalha TipoDaFalha,
    int? ListaIncompletaCom,
    int Novos,
    int Atualizados,
    int Enviadas,
    int Adiadas,
    string? Motivo,
    TimeSpan Duracao);

/// <summary>
/// Sincroniza com o painel na nuvem, em segundo plano: cartões descem, tentativas sobem.
/// </summary>
/// <remarks>
/// <para>
/// Nada aqui está no caminho da catraca (ADR-0023). Sem internet, cada rodada falha, o
/// painel mostra "sem internet", e as tentativas se acumulam na outbox até a próxima vez.
/// </para>
/// <para>
/// Uma falha não impede a outra metade: se a leitura de cartões falhar, o envio das
/// tentativas ainda é tentado, e vice-versa.
/// </para>
/// </remarks>
public sealed class SincronizacaoComANuvem : BackgroundService
{
    private readonly LacoDeIngestao _cartoes;
    private readonly DrenadorDaOutbox _tentativas;
    private readonly EstadoDaNuvem _estado;
    private readonly TimeSpan _intervalo;
    private readonly TimeProvider _relogio;
    private readonly Action<string> _registrar;
    private readonly Func<bool> _temSegredo;
    private readonly SuspeitaDeCorte? _corte;

    // Uma rodada por vez: o teste pedido pelo painel espera a rodada automática terminar, e
    // vice-versa. Duas ao mesmo tempo puxariam a mesma lista e mandariam a mesma passagem duas vezes.
    private readonly SemaphoreSlim _umaPorVez = new(1, 1);

    /// <param name="cartoes">O laço que traz os cartões da nuvem.</param>
    /// <param name="tentativas">O drenador que sobe as tentativas.</param>
    /// <param name="estado">O que o painel mostra da nuvem.</param>
    /// <param name="intervalo">Intervalo entre rodadas.</param>
    /// <param name="registrar">Registro do serviço.</param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="temSegredo">
    /// Se há segredo da nuvem gravado. Sem segredo a rodada não roda e o painel mostra o motivo
    /// (achado E8-1 do docs/41): antes, as requisições saíam sem credencial e só uma linha no
    /// registro avisava. Nulo: sempre há (testes).
    /// </param>
    /// <param name="corte">Marcador da lista de cartões cortada, preenchido pela fonte.</param>
    public SincronizacaoComANuvem(
        LacoDeIngestao cartoes,
        DrenadorDaOutbox tentativas,
        EstadoDaNuvem estado,
        TimeSpan intervalo,
        Action<string> registrar,
        TimeProvider? relogio = null,
        Func<bool>? temSegredo = null,
        SuspeitaDeCorte? corte = null)
    {
        ArgumentNullException.ThrowIfNull(cartoes);
        ArgumentNullException.ThrowIfNull(tentativas);
        ArgumentNullException.ThrowIfNull(estado);
        ArgumentNullException.ThrowIfNull(registrar);

        _cartoes = cartoes;
        _tentativas = tentativas;
        _estado = estado;
        _intervalo = intervalo;
        _registrar = registrar;
        _relogio = relogio ?? TimeProvider.System;
        _temSegredo = temSegredo ?? (() => true);
        _corte = corte;
        _estado.Configurada = true;
        _estado.Intervalo = intervalo;
    }

    /// <summary>O que o painel mostra quando falta o segredo da nuvem.</summary>
    public const string FaltaOSegredo =
        "sem o segredo da nuvem nesta máquina: nada é enviado nem recebido. Grave o segredo pelo Assistente de configuração";

    /// <summary>
    /// Monta a sincronização a partir da configuração: registra o provedor local dos
    /// cartões, liga o espelho no worker e prepara os dois sentidos.
    /// </summary>
    public static SincronizacaoComANuvem Montar(
        ConfiguracaoDaNuvem nuvem,
        SqliteConnectionFactory fabrica,
        ICofreDeSegredos cofre,
        EstadoDaNuvem estado,
        Action<string> registrar,
        HttpMessageHandler? transporte = null,
        TimeProvider? relogio = null,
        bool simulacao = false)
    {
        ArgumentNullException.ThrowIfNull(nuvem);
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(cofre);

        var agora = (relogio ?? TimeProvider.System).GetUtcNow();
        var perfil = ConfiguracaoDaNuvem.Perfis[nuvem.Perfil];

        var repositorio = new RepositorioDeIngressos(fabrica);
        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(
                nuvem.Provedor,
                "Bilheteria (painel na nuvem)",
                perfil.Name,
                Conector: string.Empty,
                Reutilizavel: true,
                IntervaloDeReuso: TimeSpan.FromSeconds(nuvem.IntervaloDeReusoSegundos),
                SomenteNaUrna: nuvem.SomenteNaUrna),
            agora);

        // O worker lê a configuração ao subir: o espelho precisa estar ligado antes.
        var configuracoes = new ConfiguracoesDaBorda(fabrica);
        var (atual, _) = configuracoes.Ler();
        var desejada = atual with
        {
            ConectorDoEspelho = nuvem.Conector,
            EsperaPeloGiroSegundos = Math.Max(nuvem.EsperaPeloGiroSegundos, atual.TempoDeAcionamento + 1),
        };

        if (desejada != atual)
        {
            configuracoes.Gravar(desejada, agora, "servico");
        }

        var http = new HttpClient(
            new CabecalhoDeSegredo(cofre, nuvem.CabecalhoDoSegredo) { InnerHandler = transporte ?? new HttpClientHandler() },
            disposeHandler: true)
        {
            BaseAddress = new Uri(nuvem.Base),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var corte = new SuspeitaDeCorte();
        var fonte = new FonteDeCartoesDoPainel(
            http,
            nuvem.Provedor,
            nuvem.Dispositivo,
            perfil,
            aoRecusarCartao: (posicao, motivo) => registrar($"nuvem: cartão {posicao} recusado: {motivo}"),
            aoSuspeitarDeCorte: n =>
            {
                corte.Recebidos = n;
                registrar(
                    $"nuvem: a lista de cartões veio com {n}; pode ter sido cortada pelo limite do servidor. " +
                    "O cursor não avançou. Ver docs/22, seção 8.2.");
            });

        var ingestao = new LacoDeIngestao(fonte, repositorio, new CursoresSqlite(fabrica), relogio);

        var drenador = new DrenadorDaOutbox(
            new FilaDeSaidaSqlite(fabrica),
            // Um device_id por PC, o mesmo do segredo (decisão S10); a catraca vai em extra.catraca
            // (achado E5-3 do docs/41: antes ia "inner-N", que não bate com o segredo do equipamento).
            [new ConectorDeTentativasDoPainel(http, nuvem.Conector, equipamentoNoPainel: _ => nuvem.Dispositivo, simulacao: simulacao)],
            tentativa => TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(tentativa, 6)))),
            relogio);

        return new SincronizacaoComANuvem(
            ingestao, drenador, estado, TimeSpan.FromSeconds(nuvem.IntervaloSegundos), registrar, relogio,
            temSegredo: () => cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo) is { Length: > 0 },
            corte: corte);
    }

    /// <summary>
    /// Uma rodada: cartões, depois tentativas. Devolve se as duas deram certo.
    /// Sem o segredo, nada é tentado e o estado diz isso (<see cref="EstadoDaNuvem.SegredoAusente"/>).
    /// </summary>
    public async Task<bool> UmaRodadaAsync(CancellationToken cancelamento) =>
        (await RodarAsync(cancelamento).ConfigureAwait(false)).Sucesso;

    /// <summary>
    /// Uma rodada agora, com o que ela fez. É o botão "Testar conexão com a nuvem": a mesma rodada do
    /// laço, não um ping à parte, para o teste provar o caminho que os cartões e as passagens fazem.
    /// Se a rodada automática estiver em andamento, espera ela terminar.
    /// </summary>
    public async Task<ResultadoDaRodada> RodarAsync(CancellationToken cancelamento)
    {
        await _umaPorVez.WaitAsync(cancelamento).ConfigureAwait(false);
        try
        {
            var inicio = _relogio.GetTimestamp();
            if (!_temSegredo())
            {
                _estado.SegredoAusente = true;
                _estado.RegistrarFalha(FaltaOSegredo, TipoDeFalha.Outra, _relogio.GetUtcNow());
                return new ResultadoDaRodada(
                    Sucesso: false, Respondeu: false, TipoDeFalha.Outra, ListaIncompletaCom: null,
                    Novos: 0, Atualizados: 0, Enviadas: 0, Adiadas: 0, FaltaOSegredo, TimeSpan.Zero);
            }

            _estado.SegredoAusente = false;
            _estado.EmAndamento = true;
            try
            {
                return await FazerRodadaAsync(inicio, cancelamento).ConfigureAwait(false);
            }
            finally
            {
                _estado.EmAndamento = false;
            }
        }
        finally
        {
            _umaPorVez.Release();
        }
    }

    private async Task<ResultadoDaRodada> FazerRodadaAsync(long inicio, CancellationToken cancelamento)
    {
        var falhas = new List<string>();
        var tipo = TipoDeFalha.Nenhuma;
        int? listaCortadaCom = null;

        void Falhar(string texto, TipoDeFalha tipoDaFalha)
        {
            falhas.Add(texto);
            tipo = TipoDeFalhaHttp.MaisGrave(tipo, tipoDaFalha);
        }

        if (_corte is not null)
        {
            _corte.Recebidos = null;
        }

        var cartoes = await _cartoes.PuxarAsync(cancelamento).ConfigureAwait(false);
        if (cartoes.Interrompido)
        {
            Falhar($"cartões: {cartoes.Erro}", cartoes.Causa is { } causa ? TipoDeFalhaHttp.DeExcecao(causa) : TipoDeFalha.Outra);
        }
        else if (_corte?.Recebidos is { } recebidos)
        {
            // O servidor respondeu, mas cortou a lista: o destino está de pé e a leitura é que falhou.
            listaCortadaCom = recebidos;
            Falhar(string.Create(
                CultureInfo.InvariantCulture,
                $"cartões: a lista veio com {recebidos} e pode ter sido cortada pelo servidor; os que faltam não chegaram a este computador"),
                TipoDeFalha.Servidor);
        }
        else if (cartoes.TeveTrabalho)
        {
            _registrar(string.Create(
                CultureInfo.InvariantCulture,
                $"nuvem: {cartoes.Inseridos} cartão(ões) novo(s), {cartoes.Atualizados} atualizado(s)."));
        }

        if (cartoes.Colisoes.Count > 0)
        {
            _registrar(string.Create(
                CultureInfo.InvariantCulture,
                $"nuvem: {cartoes.Colisoes.Count} cartão(ões) com número já usado por outro cadastro; ignorado(s)."));
        }

        var envio = await _tentativas.DrenarUmaVezAsync(cancelamento).ConfigureAwait(false);
        if (envio.Adiados > 0 && envio.Enviados == 0)
        {
            Falhar(envio.UltimoErro is { } erroDoEnvio
                ? string.Create(CultureInfo.InvariantCulture, $"tentativas: {envio.Adiados} adiada(s): {erroDoEnvio}")
                : string.Create(CultureInfo.InvariantCulture, $"tentativas: {envio.Adiados} adiada(s)"),
                envio.TipoDoUltimoErro);
        }

        if (envio.CartasMortas > 0)
        {
            _registrar(string.Create(
                CultureInfo.InvariantCulture,
                $"nuvem: {envio.CartasMortas} tentativa(s) recusada(s) pelo painel foram para cartas mortas."));
        }

        var agora = _relogio.GetUtcNow();

        // A nuvem respondeu se a lista de cartões chegou, se alguma passagem subiu, ou se a falha foi
        // uma resposta dela (credencial recusada, erro do servidor).
        var respondeu = !cartoes.Interrompido || envio.Enviados > 0
                        || tipo is TipoDeFalha.Autenticacao or TipoDeFalha.Servidor;

        ResultadoDaRodada Resultado(bool sucesso, string? motivo) => new(
            sucesso, respondeu, tipo, listaCortadaCom, cartoes.Inseridos, cartoes.Atualizados,
            envio.Enviados, envio.Adiados, motivo, _relogio.GetElapsedTime(inicio));

        if (falhas.Count == 0)
        {
            _estado.RegistrarSucesso(agora);
            return Resultado(sucesso: true, motivo: null);
        }

        var motivo = string.Join("; ", falhas);

        // Só o corte da lista falhou: a nuvem respondeu, então ela está conectada, com a lista
        // incompleta. Dizer "offline" mandaria o operador procurar defeito na internet.
        if (listaCortadaCom is { } cortada && falhas.Count == 1)
        {
            _estado.RegistrarListaIncompleta(cortada, motivo, agora);
            _registrar($"nuvem: conectada, mas com a lista de cartões incompleta ({motivo}).");
            return Resultado(sucesso: false, motivo);
        }

        _estado.RegistrarFalha(motivo, tipo, agora);
        _registrar($"nuvem: falhou ({motivo}).");
        return Resultado(sucesso: false, motivo);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(_intervalo);

        do
        {
            try
            {
                await UmaRodadaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                // Base ocupada ou algo inesperado: a próxima rodada tenta de novo. Nunca
                // derruba o serviço — ele é quem supervisiona as catracas.
                _estado.RegistrarFalha(erro.GetType().Name, TipoDeFalha.Outra, _relogio.GetUtcNow());
                _registrar($"nuvem: erro inesperado ({erro.GetType().Name}).");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public override void Dispose()
    {
        _umaPorVez.Dispose();
        base.Dispose();
    }
}
