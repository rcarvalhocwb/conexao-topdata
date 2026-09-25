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
    /// <summary>Perfis que podem ser escolhidos pelo nome.</summary>
    public static readonly IReadOnlyDictionary<string, CredentialNormalization> Perfis =
        new Dictionary<string, CredentialNormalization>(StringComparer.Ordinal)
        {
            [CredentialNormalization.Raw.Name] = CredentialNormalization.Raw,
            [PerfisDeLeitura.MifareCatraca4.Name] = PerfisDeLeitura.MifareCatraca4,
            [PerfisDeLeitura.QrCatraca4.Name] = PerfisDeLeitura.QrCatraca4,
        };

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

    public SincronizacaoComANuvem(
        LacoDeIngestao cartoes,
        DrenadorDaOutbox tentativas,
        EstadoDaNuvem estado,
        TimeSpan intervalo,
        Action<string> registrar,
        TimeProvider? relogio = null)
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
        _estado.Configurada = true;
    }

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
        TimeProvider? relogio = null)
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

        var fonte = new FonteDeCartoesDoPainel(
            http,
            nuvem.Provedor,
            nuvem.Dispositivo,
            perfil,
            aoRecusarCartao: (posicao, motivo) => registrar($"nuvem: cartão {posicao} recusado: {motivo}"),
            aoSuspeitarDeCorte: n => registrar(
                $"nuvem: a lista de cartões veio com {n}; pode ter sido cortada pelo limite do servidor. " +
                "O cursor não avançou. Ver docs/22, seção 8.2."));

        var ingestao = new LacoDeIngestao(fonte, repositorio, new CursoresSqlite(fabrica), relogio);

        var drenador = new DrenadorDaOutbox(
            new FilaDeSaidaSqlite(fabrica),
            [new ConectorDeTentativasDoPainel(http, nuvem.Conector)],
            tentativa => TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(tentativa, 6)))),
            relogio);

        return new SincronizacaoComANuvem(
            ingestao, drenador, estado, TimeSpan.FromSeconds(nuvem.IntervaloSegundos), registrar, relogio);
    }

    /// <summary>Uma rodada: cartões, depois tentativas. Devolve se as duas deram certo.</summary>
    public async Task<bool> UmaRodadaAsync(CancellationToken cancelamento)
    {
        var falhas = new List<string>();

        var cartoes = await _cartoes.PuxarAsync(cancelamento).ConfigureAwait(false);
        if (cartoes.Interrompido)
        {
            falhas.Add($"cartões: {cartoes.Erro}");
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
            falhas.Add(string.Create(CultureInfo.InvariantCulture, $"tentativas: {envio.Adiados} adiada(s)"));
        }

        if (envio.CartasMortas > 0)
        {
            _registrar(string.Create(
                CultureInfo.InvariantCulture,
                $"nuvem: {envio.CartasMortas} tentativa(s) recusada(s) pelo painel foram para cartas mortas."));
        }

        if (falhas.Count == 0)
        {
            _estado.RegistrarSucesso(_relogio.GetUtcNow());
            return true;
        }

        var motivo = string.Join("; ", falhas);
        _estado.RegistrarFalha(motivo);
        _registrar($"nuvem: falhou ({motivo}).");
        return false;
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
                _estado.RegistrarFalha(erro.GetType().Name);
                _registrar($"nuvem: erro inesperado ({erro.GetType().Name}).");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
