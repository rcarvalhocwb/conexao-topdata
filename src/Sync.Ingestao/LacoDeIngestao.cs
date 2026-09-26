using System.Globalization;
using Access.Domain.Ticketing;

namespace Sync.Ingestao;

/// <summary>O que uma rodada de ingestão fez.</summary>
/// <param name="Paginas">Quantas páginas foram lidas.</param>
/// <param name="Inseridos">Ingressos novos.</param>
/// <param name="Atualizados">Ingressos reescritos.</param>
/// <param name="Colisoes">QR recusado por conflito com outro provedor.</param>
/// <param name="ProvedorDesconhecido">Recusados por provedor não cadastrado.</param>
/// <param name="Interrompido">A rodada parou antes de esgotar o que havia.</param>
/// <param name="Erro">Motivo da interrupção, quando houve.</param>
public sealed record ResumoDaIngestao(
    int Paginas,
    int Inseridos,
    int Atualizados,
    IReadOnlyList<ColisaoDeQr> Colisoes,
    int ProvedorDesconhecido,
    bool Interrompido = false,
    string? Erro = null)
{
    /// <summary>Rodada em que não havia nada.</summary>
    public static ResumoDaIngestao Vazia { get; } = new(0, 0, 0, [], 0);

    /// <summary>Entrou alguma coisa.</summary>
    public bool TeveTrabalho => Inseridos + Atualizados > 0;

    /// <summary>Há conflito que alguém precisa ver.</summary>
    public bool ExigeAtencao => Colisoes.Count > 0 || ProvedorDesconhecido > 0 || Interrompido;
}

/// <summary>Evento observável da ingestão, para log e métrica.</summary>
/// <param name="Provedor">De quem.</param>
/// <param name="Fluxo">Incremental ou completa.</param>
/// <param name="Acao">O que aconteceu.</param>
/// <param name="Quantidade">Quantos itens.</param>
/// <param name="Detalhe">Mensagem resumida, sem dado sensível.</param>
public sealed record OcorrenciaDeIngestao(
    string Provedor,
    string Fluxo,
    string Acao,
    int Quantidade,
    string? Detalhe = null);

/// <summary>
/// Puxa ingressos do provedor até a base local, por cursor, sem perder nenhum.
/// </summary>
/// <remarks>
/// <para>
/// A garantia que este laço dá é <b>ao menos uma vez</b>, e ela vem de uma regra só:
/// <b>o cursor só avança depois do lote ter sido aplicado.</b> Se a aplicação falhar, o
/// cursor fica onde estava e a página inteira é lida de novo na rodada seguinte. Reler é
/// barato porque o destino é idempotente; perder um ingresso é caro porque a pessoa é
/// barrada na porta com o ingresso pago na mão.
/// </para>
/// <para>
/// Os dois fluxos existem por motivos diferentes e nunca se atropelam:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Incremental</b> — de segundos, é o que dá o quase-tempo-real. Guarda o próprio cursor.
/// </item>
/// <item>
/// <b>Varredura completa</b> — periódica, lê do começo. Existe porque provedor reprocessa
/// venda, corrige lote e publica fora de ordem, e o incremental não vê o que foi alterado
/// para trás. <b>Nunca move o cursor do incremental</b>, sob pena de fazê-lo andar para
/// trás e reprocessar horas de fila.
/// </item>
/// </list>
/// <para>Ver docs/16-multiplos-provedores-de-ingresso.md.</para>
/// </remarks>
public sealed class LacoDeIngestao
{
    /// <summary>Nome do fluxo incremental, como fica gravado no cursor.</summary>
    public const string FluxoIncremental = "ingressos";

    /// <summary>Nome do fluxo de varredura completa.</summary>
    public const string FluxoCompleto = "ingressos-completa";

    private readonly IFonteDeIngressos _fonte;
    private readonly IDestinoDeIngressos _destino;
    private readonly IArmazenamentoDeCursor _cursores;
    private readonly TimeProvider _relogio;
    private readonly int _maximoDePaginasPorRodada;
    private readonly Action<OcorrenciaDeIngestao>? _observador;

    /// <summary>
    /// </summary>
    /// <param name="fonte">O provedor.</param>
    /// <param name="destino">Onde gravar.</param>
    /// <param name="cursores">Onde guardar a marca de retomada.</param>
    /// <param name="relogio">Relógio. Injetável para que o teste não durma.</param>
    /// <param name="maximoDePaginasPorRodada">
    /// Teto por rodada. Existe para que uma carga inicial de cinquenta mil ingressos não
    /// monopolize o processo e atrase tudo o mais — inclusive a drenagem dos avisos de
    /// uso, que é o que o provedor está esperando.
    /// </param>
    /// <param name="observador">Recebe cada ocorrência.</param>
    public LacoDeIngestao(
        IFonteDeIngressos fonte,
        IDestinoDeIngressos destino,
        IArmazenamentoDeCursor cursores,
        TimeProvider? relogio = null,
        int maximoDePaginasPorRodada = 20,
        Action<OcorrenciaDeIngestao>? observador = null)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(cursores);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximoDePaginasPorRodada, 1);

        _fonte = fonte;
        _destino = destino;
        _cursores = cursores;
        _relogio = relogio ?? TimeProvider.System;
        _maximoDePaginasPorRodada = maximoDePaginasPorRodada;
        _observador = observador;
    }

    /// <summary>Puxa o que mudou desde a última vez.</summary>
    public Task<ResumoDaIngestao> PuxarAsync(CancellationToken cancelamento = default) =>
        RodarAsync(FluxoIncremental, _cursores.Ler(_fonte.Provedor, FluxoIncremental), cancelamento);

    /// <summary>
    /// Lê tudo do começo, para pegar o que a leitura incremental não viu.
    /// </summary>
    /// <remarks>
    /// Guarda o progresso no próprio fluxo, de modo que uma varredura interrompida
    /// retoma de onde parou em vez de recomeçar — numa base de trinta mil ingressos, a
    /// diferença entre retomar e recomeçar é a diferença entre terminar e nunca terminar.
    /// </remarks>
    public Task<ResumoDaIngestao> VarrerTudoAsync(CancellationToken cancelamento = default) =>
        RodarAsync(FluxoCompleto, _cursores.Ler(_fonte.Provedor, FluxoCompleto), cancelamento);

    /// <summary>Recomeça a varredura completa do zero na próxima chamada.</summary>
    public void ReiniciarVarredura() => _cursores.Apagar(_fonte.Provedor, FluxoCompleto);

    private async Task<ResumoDaIngestao> RodarAsync(
        string fluxo,
        string? cursor,
        CancellationToken cancelamento)
    {
        var paginas = 0;
        var inseridos = 0;
        var atualizados = 0;
        var provedorDesconhecido = 0;
        var colisoes = new List<ColisaoDeQr>();

        while (paginas < _maximoDePaginasPorRodada)
        {
            cancelamento.ThrowIfCancellationRequested();

            PaginaDeIngressos pagina;
            try
            {
                pagina = await _fonte.LerAsync(cursor, cancelamento).ConfigureAwait(false)
                    ?? PaginaDeIngressos.Vazia;
            }
            catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                // Provedor fora do ar. O cursor não andou, então nada se perdeu: a
                // próxima rodada relê exatamente daqui.
                Observar(new OcorrenciaDeIngestao(_fonte.Provedor, fluxo, "falha_na_fonte", 0, Resumir(erro)));
                return new ResumoDaIngestao(
                    paginas, inseridos, atualizados, colisoes, provedorDesconhecido,
                    Interrompido: true, Erro: Resumir(erro));
            }

            if (pagina.Itens.Count > 0)
            {
                ResultadoDaIngestao aplicado;
                try
                {
                    aplicado = _destino.Aplicar(pagina.Itens, _relogio.GetUtcNow());
                }
                catch (Exception erro) when (erro is not OutOfMemoryException)
                {
                    // Falha ao gravar. O cursor fica onde está — reler é barato, perder
                    // ingresso não é.
                    Observar(new OcorrenciaDeIngestao(_fonte.Provedor, fluxo, "falha_ao_gravar", pagina.Itens.Count, Resumir(erro)));
                    return new ResumoDaIngestao(
                        paginas, inseridos, atualizados, colisoes, provedorDesconhecido,
                        Interrompido: true, Erro: Resumir(erro));
                }

                inseridos += aplicado.Inseridos;
                atualizados += aplicado.Atualizados;
                provedorDesconhecido += aplicado.ProvedorDesconhecido;
                colisoes.AddRange(aplicado.Colisoes);

                if (aplicado.Colisoes.Count > 0)
                {
                    // Conflito não interrompe a ingestão: os outros ingressos da página
                    // entraram, e o conflito fica registrado para alguém resolver.
                    Observar(new OcorrenciaDeIngestao(
                        _fonte.Provedor, fluxo, "colisao_de_qr", aplicado.Colisoes.Count,
                        aplicado.Colisoes[0].QrNormalizado));
                }
            }

            paginas++;

            // Só aqui, e só depois de gravar.
            if (!string.IsNullOrEmpty(pagina.ProximoCursor))
            {
                _cursores.Gravar(_fonte.Provedor, fluxo, pagina.ProximoCursor, _relogio.GetUtcNow());
                cursor = pagina.ProximoCursor;
            }

            if (!pagina.TemMais)
            {
                break;
            }
        }

        if (inseridos + atualizados > 0)
        {
            Observar(new OcorrenciaDeIngestao(_fonte.Provedor, fluxo, "ingeridos", inseridos + atualizados));
        }

        return new ResumoDaIngestao(paginas, inseridos, atualizados, colisoes, provedorDesconhecido);
    }

    private void Observar(OcorrenciaDeIngestao ocorrencia)
    {
        try
        {
            _observador?.Invoke(ocorrencia);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Observador quebrado não pode parar a ingestão.
            _ = erro;
        }
    }

    private static string Resumir(Exception erro) =>
        string.Create(CultureInfo.InvariantCulture, $"{erro.GetType().Name}: {erro.Message}");
}
