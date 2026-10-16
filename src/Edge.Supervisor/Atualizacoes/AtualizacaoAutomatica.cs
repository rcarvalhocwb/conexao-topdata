using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Access.Infrastructure.SQLite;
using Microsoft.Extensions.Hosting;

namespace Edge.Supervisor.Atualizacoes;

/// <summary>Como a atualização automática roda nesta instalação.</summary>
/// <param name="Repositorio">De onde vêm as versões estáveis (<c>dono/repositorio</c> no GitHub).</param>
/// <param name="Canal">O canal deste instalador: <c>producao</c>, <c>teste</c> ou vazio (desconhecido).</param>
/// <param name="VersaoInstalada">A versão do serviço que está rodando.</param>
/// <param name="PastaDeTrabalho">Onde os MSIs baixados e o script de instalação ficam.</param>
/// <param name="Chaves">As chaves públicas que assinam a ficha; vazia desliga.</param>
/// <param name="PrimeiraVerificacao">Quanto esperar depois de subir para a primeira verificação.</param>
/// <param name="Intervalo">De quanto em quanto tempo verifica.</param>
public sealed record OpcoesDaAtualizacao(
    string Repositorio,
    string Canal,
    Version VersaoInstalada,
    string PastaDeTrabalho,
    IReadOnlyList<string> Chaves,
    TimeSpan PrimeiraVerificacao,
    TimeSpan Intervalo)
{
    /// <summary>O canal que se atualiza sozinho.</summary>
    public const string CanalDeProducao = "producao";

    /// <summary>O repositório público das versões.</summary>
    public const string RepositorioPadrao = "rcarvalhocwb/conexao-topdata";

    /// <summary>Ligada só com chave pública e no canal de produção (docs/60 §2.2).</summary>
    public bool Ligada => Chaves.Count > 0 && Canal == CanalDeProducao;
}

/// <summary>O que a tela Diagnóstico mostra da atualização.</summary>
public sealed record SituacaoLocalDaAtualizacao(
    bool Ligada,
    string Canal,
    string VersaoInstalada,
    string? VersaoDisponivel,
    string Situacao,
    DateTimeOffset? UltimaVerificacao,
    string? UltimoResultado);

/// <summary>
/// Atualização automática (docs/60 §2): verifica a última versão estável no GitHub, confere a ficha assinada,
/// baixa e confere o MSI, e instala em silêncio quando o sistema é finalizado ou de madrugada, com as catracas
/// paradas. Nunca com a licença bloqueada.
/// </summary>
/// <remarks>
/// Nada aqui está no caminho da catraca: uma falha vira texto no Diagnóstico e a próxima verificação tenta
/// de novo. A mesma versão é tentada no máximo <see cref="TentativasPorVersao"/> vezes.
/// </remarks>
public sealed class AtualizacaoAutomatica : BackgroundService
{
    /// <summary>Quantas vezes a mesma versão é tentada.</summary>
    public const int TentativasPorVersao = 3;

    /// <summary>Quanto o painel é avisado antes de uma instalação de madrugada.</summary>
    public static readonly TimeSpan AvisoAoPainel = TimeSpan.FromMinutes(2);

    // Sem resposta do serviço novo em tanto tempo depois de mandar instalar, a instalação não terminou.
    private static readonly TimeSpan TempoDaInstalacao = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private readonly OpcoesDaAtualizacao _opcoes;
    private readonly HttpClient _http;
    private readonly ConfiguracoesDaBorda _configuracoes;
    private readonly ControleDeLicenca? _licenca;
    private readonly Func<DateTimeOffset?> _ultimaLeitura;
    private readonly Action<string> _registrar;
    private readonly IInstaladorDoWindows _instalador;
    private readonly Action _antesDeInstalar;
    private readonly TimeProvider _relogio;
    private readonly SemaphoreSlim _acordar = new(0, 1);
    private readonly object _trava = new();

    private FichaDaVersao? _ultimaFicha;
    private string? _msiPronto;
    private DateTimeOffset? _ultimaVerificacao;
    private string _situacao;
    private DateTimeOffset? _painelFinalizadoEm;
    private DateTimeOffset? _instalarApos;
    private bool _instalando;
    private DateTimeOffset _instalandoDesde;
    private DateTimeOffset _naoAntesDe = DateTimeOffset.MinValue;

    // Depois de uma falha, a próxima tentativa fica para a próxima janela, não para o minuto seguinte.
    private static readonly TimeSpan EsperaDepoisDeFalha = TimeSpan.FromHours(6);

    /// <param name="opcoes">Repositório, canal, versão e chaves.</param>
    /// <param name="configuracoes">Onde ficam as tentativas e o resultado.</param>
    /// <param name="licenca">A licença: bloqueada, não instala. Nulo: não exigida.</param>
    /// <param name="ultimaLeitura">A última leitura de qualquer catraca.</param>
    /// <param name="registrar">Registro do serviço.</param>
    /// <param name="instalador">Quem roda o MSI.</param>
    /// <param name="antesDeInstalar">Cópia da base e guarda do SDK; se lançar, não instala.</param>
    /// <param name="transporte">HTTP (testes passam um falso).</param>
    /// <param name="relogio">Relógio.</param>
    public AtualizacaoAutomatica(
        OpcoesDaAtualizacao opcoes,
        ConfiguracoesDaBorda configuracoes,
        ControleDeLicenca? licenca,
        Func<DateTimeOffset?> ultimaLeitura,
        Action<string> registrar,
        IInstaladorDoWindows instalador,
        Action antesDeInstalar,
        HttpMessageHandler? transporte = null,
        TimeProvider? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(configuracoes);
        ArgumentNullException.ThrowIfNull(ultimaLeitura);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(instalador);
        ArgumentNullException.ThrowIfNull(antesDeInstalar);

        _opcoes = opcoes;
        _configuracoes = configuracoes;
        _licenca = licenca;
        _ultimaLeitura = ultimaLeitura;
        _registrar = registrar;
        _instalador = instalador;
        _antesDeInstalar = antesDeInstalar;
        _relogio = relogio ?? TimeProvider.System;

        // Sem limite no cliente: cada chamada tem o seu (o MSI tem quase 200 MB).
        _http = new HttpClient(transporte ?? new HttpClientHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RayzerXAcess", opcoes.VersaoInstalada.ToString()));

        _situacao = !opcoes.Ligada
            ? Desligada(opcoes)
            : "Aguardando a primeira verificação.";
    }

    /// <summary>Verdadeiro quando vai instalar em instantes: o painel deixa o relançador e fecha.</summary>
    public bool Iminente(DateTimeOffset agora)
    {
        lock (_trava)
        {
            // Com prazo: um msiexec que falhou sem parar o serviço não pode deixar o painel fechando para sempre.
            return (_instalando && agora - _instalandoDesde < TimeSpan.FromMinutes(10))
                || (_instalarApos is { } quando && agora >= quando - AvisoAoPainel && agora < quando + TimeSpan.FromMinutes(10));
        }
    }

    /// <summary>O que a tela mostra.</summary>
    public SituacaoLocalDaAtualizacao Situacao()
    {
        lock (_trava)
        {
            return new SituacaoLocalDaAtualizacao(
                _opcoes.Ligada,
                _opcoes.Canal,
                _opcoes.VersaoInstalada.ToString(),
                _ultimaFicha?.Versao.ToString(),
                _situacao,
                _ultimaVerificacao,
                _configuracoes.LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoResultado));
        }
    }

    /// <summary>
    /// O operador finalizou o painel. Devolve verdadeiro se há versão pronta e ela vai ser instalada agora.
    /// </summary>
    public bool PainelFinalizado(DateTimeOffset agora)
    {
        bool vai;
        lock (_trava)
        {
            _painelFinalizadoEm = agora;
            vai = _msiPronto is not null && !_instalando && agora >= _naoAntesDe
                && QuandoInstalar.Decidir(agora, _ultimaLeitura(), agora, _licenca?.Bloqueada ?? false) is MotivoDaInstalacao.SistemaFinalizado;
        }

        Acordar();
        return vai;
    }

    /// <summary>O painel voltou a perguntar: ele não está mais finalizado.</summary>
    public void PainelAtivo(DateTimeOffset agora)
    {
        lock (_trava)
        {
            // Uma última pergunta do painel logo antes de fechar não conta como "voltou".
            if (_painelFinalizadoEm is { } finalizado && agora - finalizado > TimeSpan.FromSeconds(30))
            {
                _painelFinalizadoEm = null;
            }
        }
    }

    /// <summary>
    /// Depois de subir: a instalação anterior terminou? Registra "atualizado de A para B" ou a falha.
    /// </summary>
    public void ConferirAtualizacaoAnterior()
    {
        var agora = _relogio.GetUtcNow();
        if (_configuracoes.LerValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso) is not { Length: > 0 } emCurso)
        {
            return;
        }

        var partes = emCurso.Split('|');
        if (partes.Length != 2 || FichaDaVersao.VersaoDe(partes[0]) is not { } pedida
            || !DateTimeOffset.TryParse(partes[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var desde))
        {
            _configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso, string.Empty, agora);
            return;
        }

        if (_opcoes.VersaoInstalada >= pedida)
        {
            Concluir($"Atualizado para a versão {pedida} em {Hora(agora)}.", agora);
        }
        else if (agora - desde > TempoDaInstalacao)
        {
            var codigo = LerCodigoDoMsiexec(pedida);
            Concluir(
                $"A instalação da versão {pedida} não terminou{(codigo is null ? string.Empty : $" (msiexec {codigo})")}; segue a versão {_opcoes.VersaoInstalada}. Tenta de novo na próxima janela.",
                agora);
        }
    }

    /// <summary>Uma verificação: ficha, assinatura, e o download do MSI se a versão é nova.</summary>
    public async Task VerificarAsync(CancellationToken cancelamento)
    {
        if (!_opcoes.Ligada)
        {
            return;
        }

        var agora = _relogio.GetUtcNow();
        try
        {
            var situacao = await BuscarAsync(cancelamento).ConfigureAwait(false);
            lock (_trava)
            {
                _situacao = situacao;
                _ultimaVerificacao = agora;
            }
        }
        catch (Exception erro) when (erro is HttpRequestException or IOException or JsonException or TaskCanceledException
                                         or UnauthorizedAccessException or InvalidDataException
                                     && !cancelamento.IsCancellationRequested)
        {
            // Sem internet é o normal de um evento: tenta na próxima.
            lock (_trava)
            {
                _situacao = $"Não deu para verificar agora ({Resumo(erro)}). Tenta de novo em algumas horas.";
                _ultimaVerificacao = agora;
            }
        }
    }

    /// <summary>
    /// Instala agora se é hora (<see cref="QuandoInstalar"/>). Devolve verdadeiro se mandou instalar.
    /// </summary>
    public bool TentarInstalar()
    {
        var agora = _relogio.GetUtcNow();
        string msi;
        FichaDaVersao ficha;

        lock (_trava)
        {
            if (_instalando && agora - _instalandoDesde > TempoDaInstalacao)
            {
                // O serviço não foi parado pelo instalador: a instalação falhou e esta versão continua.
                _instalando = false;
                _instalarApos = null;
                _painelFinalizadoEm = null;
                _naoAntesDe = agora + EsperaDepoisDeFalha;
                var versao = _ultimaFicha?.Versao;
                var codigo = versao is null ? null : LerCodigoDoMsiexec(versao);
                Concluir(
                    $"A instalação da versão {versao} não terminou{(codigo is null ? string.Empty : $" (msiexec {codigo})")}; segue a versão {_opcoes.VersaoInstalada}. Tenta de novo na próxima janela.",
                    agora);
                _situacao = "A última instalação não terminou; tenta de novo na próxima janela.";
            }

            if (_msiPronto is null || _ultimaFicha is null || _instalando || agora < _naoAntesDe)
            {
                return false;
            }

            var motivo = QuandoInstalar.Decidir(agora, _ultimaLeitura(), _painelFinalizadoEm, _licenca?.Bloqueada ?? false);
            if (motivo is null)
            {
                _instalarApos = null;
                return false;
            }

            if (motivo is MotivoDaInstalacao.Madrugada)
            {
                // O painel pode estar aberto: avisa e dá tempo de ele sair sozinho.
                if (_instalarApos is null)
                {
                    _instalarApos = agora + AvisoAoPainel;
                    return false;
                }

                if (agora < _instalarApos)
                {
                    return false;
                }
            }

            _instalando = true;
            _instalandoDesde = agora;
            msi = _msiPronto;
            ficha = _ultimaFicha;
        }

        try
        {
            _antesDeInstalar();
            var tentativas = Tentativas(ficha.Versao) + 1;
            _configuracoes.GravarValor(ChaveDeTentativas(ficha.Versao), tentativas.ToString(CultureInfo.InvariantCulture), agora);
            _configuracoes.GravarValor(
                ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso,
                $"{ficha.Versao}|{agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}",
                agora);
            _registrar($"atualização: instalando a versão {ficha.Versao} (tentativa {tentativas} de {TentativasPorVersao}). O serviço vai parar e voltar sozinho.");
            _instalador.Instalar(msi, PastaDaVersao(ficha.Versao), ficha.Versao);
            lock (_trava)
            {
                _situacao = $"Instalando a versão {ficha.Versao}…";
            }

            return true;
        }
        catch (Exception erro) when (erro is IOException or InvalidOperationException or UnauthorizedAccessException
                                         or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            _configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso, string.Empty, agora);
            Concluir($"Não deu para instalar a versão {ficha.Versao}: {Resumo(erro)}. Tenta de novo na próxima janela.", agora);
            lock (_trava)
            {
                _instalando = false;
                _instalarApos = null;
                _painelFinalizadoEm = null;
                _naoAntesDe = agora + EsperaDepoisDeFalha;
            }

            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            ConferirAtualizacaoAnterior();
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            _registrar($"atualização: não deu para conferir a anterior ({erro.GetType().Name}).");
        }

        if (!_opcoes.Ligada)
        {
            return;
        }

        var proxima = _relogio.GetUtcNow() + _opcoes.PrimeiraVerificacao;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_relogio.GetUtcNow() >= proxima)
                {
                    await VerificarAsync(stoppingToken).ConfigureAwait(false);
                    proxima = _relogio.GetUtcNow() + _opcoes.Intervalo;
                }

                TentarInstalar();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                // Nunca derruba o serviço: ele é quem supervisiona as catracas.
                _registrar($"atualização: erro inesperado ({erro.GetType().Name}).");
            }

            try
            {
                await _acordar.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override void Dispose()
    {
        _http.Dispose();
        _acordar.Dispose();
        base.Dispose();
    }

    private async Task<string> BuscarAsync(CancellationToken cancelamento)
    {
        using var curto = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
        curto.CancelAfter(TimeSpan.FromSeconds(30));

        using var pedido = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_opcoes.Repositorio}/releases/latest");
        pedido.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resposta = await _http.SendAsync(pedido, curto.Token).ConfigureAwait(false);

        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            return "Nenhuma versão estável publicada ainda.";
        }

        resposta.EnsureSuccessStatusCode();
        var arquivos = LerArquivos(await resposta.Content.ReadAsByteArrayAsync(curto.Token).ConfigureAwait(false));

        if (!arquivos.TryGetValue(FichaDaVersao.Arquivo, out var urlDaFicha)
            || !arquivos.TryGetValue(FichaDaVersao.ArquivoDaAssinatura, out var urlDaAssinatura))
        {
            return "A última versão estável não tem ficha assinada; ignorada.";
        }

        var bytesDaFicha = await _http.GetByteArrayAsync(urlDaFicha, curto.Token).ConfigureAwait(false);
        var assinatura = await _http.GetStringAsync(urlDaAssinatura, curto.Token).ConfigureAwait(false);

        if (FichaDaVersao.Ler(bytesDaFicha, assinatura, _opcoes.Chaves, out var problema) is not { } ficha)
        {
            _registrar($"atualização: {problema}.");
            return $"A última versão estável foi recusada: {problema}.";
        }

        if (ficha.Canal != _opcoes.Canal)
        {
            return $"A última versão estável é do canal {ficha.Canal}; esta instalação é do canal {_opcoes.Canal}.";
        }

        lock (_trava)
        {
            _ultimaFicha = ficha;
        }

        if (ficha.Versao <= _opcoes.VersaoInstalada)
        {
            lock (_trava)
            {
                _msiPronto = null;
            }

            return $"Em dia: a versão {_opcoes.VersaoInstalada} é a mais nova.";
        }

        if (Tentativas(ficha.Versao) >= TentativasPorVersao)
        {
            return $"A versão {ficha.Versao} falhou {TentativasPorVersao} vezes; instale pelo Setup ou chame o suporte.";
        }

        if (!arquivos.TryGetValue(ficha.Msi, out var urlDoMsi))
        {
            return $"A versão {ficha.Versao} não tem o instalador {ficha.Msi} na release.";
        }

        var msi = await BaixarAsync(ficha, urlDoMsi, cancelamento).ConfigureAwait(false);
        LimparVersoesAntigas(ficha.Versao);

        lock (_trava)
        {
            _msiPronto = msi;
        }

        _registrar($"atualização: versão {ficha.Versao} baixada e conferida.");
        return $"Versão {ficha.Versao} baixada. Instala sozinha quando o sistema for finalizado ou de madrugada, com as catracas paradas.";
    }

    private async Task<string> BaixarAsync(FichaDaVersao ficha, string url, CancellationToken cancelamento)
    {
        var pasta = PastaDaVersao(ficha.Versao);
        Directory.CreateDirectory(pasta);
        var destino = Path.Combine(pasta, ficha.Msi);

        if (File.Exists(destino) && await ConfereAsync(destino, ficha, cancelamento).ConfigureAwait(false))
        {
            return destino;
        }

        var parcial = destino + ".baixando";
        using (var longo = CancellationTokenSource.CreateLinkedTokenSource(cancelamento))
        {
            longo.CancelAfter(TimeSpan.FromMinutes(30));
            using var resposta = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, longo.Token).ConfigureAwait(false);
            resposta.EnsureSuccessStatusCode();

            var fluxo = await resposta.Content.ReadAsStreamAsync(longo.Token).ConfigureAwait(false);
            await using (fluxo.ConfigureAwait(false))
            {
                var arquivo = File.Create(parcial);
                await using (arquivo.ConfigureAwait(false))
                {
                    await fluxo.CopyToAsync(arquivo, longo.Token).ConfigureAwait(false);
                }
            }
        }

        if (!await ConfereAsync(parcial, ficha, cancelamento).ConfigureAwait(false))
        {
            File.Delete(parcial);
            throw new InvalidDataException($"o instalador baixado da versão {ficha.Versao} não confere com a ficha");
        }

        File.Move(parcial, destino, overwrite: true);
        return destino;
    }

    private static async Task<bool> ConfereAsync(string arquivo, FichaDaVersao ficha, CancellationToken cancelamento)
    {
        if (new FileInfo(arquivo).Length != ficha.Tamanho)
        {
            return false;
        }

        var leitura = File.OpenRead(arquivo);
        await using (leitura.ConfigureAwait(false))
        {
            var hash = await SHA256.HashDataAsync(leitura, cancelamento).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash) == ficha.Sha256;
        }
    }

    private static Dictionary<string, string> LerArquivos(byte[] release)
    {
        using var documento = JsonDocument.Parse(release);
        var arquivos = new Dictionary<string, string>(StringComparer.Ordinal);
        if (documento.RootElement.TryGetProperty("assets", out var lista) && lista.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in lista.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var nome) && item.TryGetProperty("browser_download_url", out var url)
                    && nome.GetString() is { } n && url.GetString() is { } u
                    && u.StartsWith("https://", StringComparison.Ordinal))
                {
                    arquivos[n] = u;
                }
            }
        }

        return arquivos;
    }

    private void LimparVersoesAntigas(Version atual)
    {
        if (!Directory.Exists(_opcoes.PastaDeTrabalho))
        {
            return;
        }

        foreach (var pasta in Directory.GetDirectories(_opcoes.PastaDeTrabalho))
        {
            if (FichaDaVersao.VersaoDe(Path.GetFileName(pasta)) is { } versao && versao != atual)
            {
                try
                {
                    Directory.Delete(pasta, recursive: true);
                }
                catch (IOException)
                {
                    // Arquivo preso: fica para a próxima.
                }
            }
        }
    }

    private string? LerCodigoDoMsiexec(Version versao)
    {
        var arquivo = Path.Combine(PastaDaVersao(versao), "resultado.txt");
        try
        {
            return File.Exists(arquivo) ? File.ReadAllText(arquivo).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Concluir(string resultado, DateTimeOffset agora)
    {
        _configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveAtualizacaoEmCurso, string.Empty, agora);
        _configuracoes.GravarValor(ConfiguracoesDaBorda.ChaveAtualizacaoResultado, resultado, agora);
        _registrar($"atualização: {resultado}");
    }

    private int Tentativas(Version versao) =>
        int.TryParse(_configuracoes.LerValor(ChaveDeTentativas(versao)), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string ChaveDeTentativas(Version versao) => ConfiguracoesDaBorda.PrefixoTentativasDeAtualizacao + versao;

    private string PastaDaVersao(Version versao) => Path.Combine(_opcoes.PastaDeTrabalho, versao.ToString());

    private void Acordar()
    {
        try
        {
            if (_acordar.CurrentCount == 0)
            {
                _acordar.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Já acordado.
        }
    }

    private static string Desligada(OpcoesDaAtualizacao opcoes) =>
        opcoes.Chaves.Count == 0
            ? "Desligada: esta versão ainda não tem a chave de atualização (docs/60 §2.5)."
            : opcoes.Canal == "teste"
                ? "Desligada no canal de teste: instale a versão de teste nova pelo Setup."
                : "Desligada: o canal desta instalação é desconhecido. Reinstale pelo Setup de produção.";

    private static string Hora(DateTimeOffset instante) =>
        instante.ToOffset(Brasilia).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Resumo(Exception erro) => erro switch
    {
        HttpRequestException { StatusCode: { } status } => $"o GitHub respondeu {(int)status}",
        HttpRequestException => "sem internet",
        TaskCanceledException => "demorou demais",
        _ => erro.Message,
    };
}
