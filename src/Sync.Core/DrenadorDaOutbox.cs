using System.Globalization;

namespace Sync.Core;

/// <summary>O que aconteceu numa rodada de drenagem.</summary>
/// <param name="Enviados">Itens confirmados pelo destino (aceitos ou já conhecidos).</param>
/// <param name="Adiados">Itens que falharam de forma temporária e serão repetidos.</param>
/// <param name="CartasMortas">Itens retirados da fila para conferência humana.</param>
/// <param name="ConectoresSemImplementacao">
/// Nomes que constam da fila mas para os quais nenhum conector está registrado.
/// </param>
public sealed record ResumoDaRodada(
    int Enviados,
    int Adiados,
    int CartasMortas,
    IReadOnlyList<string> ConectoresSemImplementacao)
{
    /// <summary>Rodada em que nada havia para fazer.</summary>
    public static ResumoDaRodada Vazia { get; } = new(0, 0, 0, []);

    /// <summary>Houve trabalho — vale tentar de novo já, sem esperar o intervalo.</summary>
    public bool TeveTrabalho => Enviados + Adiados + CartasMortas > 0;
}

/// <summary>Evento observável da drenagem, para log e métrica.</summary>
/// <param name="Conector">Destino envolvido.</param>
/// <param name="Acao">O que aconteceu.</param>
/// <param name="Quantidade">Quantos itens.</param>
/// <param name="Detalhe">Mensagem de erro resumida, quando houver.</param>
public sealed record OcorrenciaDeDrenagem(string Conector, string Acao, int Quantidade, string? Detalhe = null);

/// <summary>
/// Drena a outbox por prioridade, em segundo plano, fora do caminho crítico.
/// </summary>
/// <remarks>
/// <para>
/// Este é o componente que responde à pergunta "e quando a internet voltar?". Ele não
/// sabe nada sobre catraca: para ele, a borda operou sozinha por oito horas e deixou
/// duzentas mil linhas numa fila. O trabalho é entregá-las na ordem certa, sem duplicar,
/// sem perder e sem travar tudo por causa de uma linha ruim.
/// </para>
/// <para>
/// <b>Três invariantes, nesta ordem de importância:</b>
/// </para>
/// <list type="number">
/// <item>Nada sai da fila sem confirmação do destino ou registro em cartas mortas.</item>
/// <item>Um conector doente não atrasa os outros — cada destino é drenado isoladamente.</item>
/// <item>Prioridade é respeitada dentro de cada conector, sempre.</item>
/// </list>
/// <para>
/// Ver docs/15-integracao-e-sincronizacao.md e docs/ADR/ADR-0003-sqlite-wal-outbox.md.
/// </para>
/// </remarks>
public sealed class DrenadorDaOutbox
{
    private readonly IFilaDeSaida _fila;
    private readonly Dictionary<string, IConectorDeSincronizacao> _conectores;
    private readonly Func<int, TimeSpan> _esperaPorTentativa;
    private readonly TimeProvider _relogio;
    private readonly int _maximoDeTentativas;
    private readonly Action<OcorrenciaDeDrenagem>? _observador;

    /// <summary>
    /// </summary>
    /// <param name="fila">A outbox.</param>
    /// <param name="conectores">Destinos registrados. Nomes duplicados são recusados aqui.</param>
    /// <param name="esperaPorTentativa">
    /// Espera antes da próxima tentativa, a partir da tentativa 1. Injetado, e não
    /// embutido, porque o jitter que serve para reconexão de catraca serve igual aqui:
    /// sem ele, todas as bordas de uma rede voltam a martelar o mesmo servidor no mesmo
    /// instante em que a internet volta.
    /// </param>
    /// <param name="relogio">Relógio. Injetável para que o teste não durma.</param>
    /// <param name="maximoDeTentativas">
    /// Depois disto o item vai para cartas mortas. O padrão 12, com espera exponencial
    /// limitada a dois minutos, cobre cerca de quatro horas de destino fora do ar — bem
    /// mais que qualquer manutenção anunciada, e menos que um contrato encerrado.
    /// </param>
    /// <param name="observador">Recebe cada ocorrência, para log e métrica.</param>
    public DrenadorDaOutbox(
        IFilaDeSaida fila,
        IEnumerable<IConectorDeSincronizacao> conectores,
        Func<int, TimeSpan> esperaPorTentativa,
        TimeProvider? relogio = null,
        int maximoDeTentativas = 12,
        Action<OcorrenciaDeDrenagem>? observador = null)
    {
        ArgumentNullException.ThrowIfNull(fila);
        ArgumentNullException.ThrowIfNull(conectores);
        ArgumentNullException.ThrowIfNull(esperaPorTentativa);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximoDeTentativas, 1);

        _fila = fila;
        _esperaPorTentativa = esperaPorTentativa;
        _relogio = relogio ?? TimeProvider.System;
        _maximoDeTentativas = maximoDeTentativas;
        _observador = observador;

        _conectores = new Dictionary<string, IConectorDeSincronizacao>(StringComparer.Ordinal);
        foreach (var conector in conectores)
        {
            ArgumentNullException.ThrowIfNull(conector);
            if (!_conectores.TryAdd(conector.Nome, conector))
            {
                throw new ArgumentException(
                    $"Dois conectores registrados com o mesmo nome: '{conector.Nome}'. " +
                    "O nome é a chave de roteamento da outbox.",
                    nameof(conectores));
            }

            if (conector.TamanhoMaximoDoLote < 1)
            {
                throw new ArgumentException(
                    $"O conector '{conector.Nome}' declara lote máximo {conector.TamanhoMaximoDoLote}.",
                    nameof(conectores));
            }
        }
    }

    /// <summary>
    /// Faz uma passagem: um lote por conector com pendência.
    /// </summary>
    /// <remarks>
    /// Um lote por rodada, e não "até esvaziar", de propósito: mantém a latência entre
    /// conectores previsível e devolve o controle ao chamador, que pode ter sido mandado
    /// parar. Quando há backlog, o chamador simplesmente chama de novo — o resumo diz se
    /// valeu a pena.
    /// </remarks>
    public async Task<ResumoDaRodada> DrenarUmaVezAsync(CancellationToken cancelamento = default)
    {
        var agora = _relogio.GetUtcNow();
        var pendentes = await _fila.ConectoresComPendenciaAsync(agora, cancelamento).ConfigureAwait(false);

        if (pendentes.Count == 0)
        {
            return ResumoDaRodada.Vazia;
        }

        var enviados = 0;
        var adiados = 0;
        var cartasMortas = 0;
        var semImplementacao = new List<string>();

        foreach (var nome in pendentes)
        {
            cancelamento.ThrowIfCancellationRequested();

            if (!_conectores.TryGetValue(nome, out var conector))
            {
                // Não é erro do item: é configuração faltando. O item fica onde está,
                // intacto, até alguém registrar o conector. Mandar para cartas mortas
                // aqui destruiria a fila inteira por causa de um arquivo de configuração.
                semImplementacao.Add(nome);
                Observar(new OcorrenciaDeDrenagem(nome, "conector_nao_registrado", 0));
                continue;
            }

            var (e, a, c) = await DrenarConectorAsync(conector, cancelamento).ConfigureAwait(false);
            enviados += e;
            adiados += a;
            cartasMortas += c;
        }

        return new ResumoDaRodada(enviados, adiados, cartasMortas, semImplementacao);
    }

    /// <summary>
    /// Laço contínuo: drena, e espera <paramref name="intervalo"/> só quando não há mais
    /// nada a fazer.
    /// </summary>
    /// <remarks>
    /// Enquanto houver backlog, não espera: é exatamente o momento em que a internet
    /// acabou de voltar e há horas de fila para subir.
    /// </remarks>
    public async Task ExecutarAsync(TimeSpan intervalo, CancellationToken cancelamento)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(intervalo, TimeSpan.Zero);

        while (!cancelamento.IsCancellationRequested)
        {
            ResumoDaRodada resumo;
            try
            {
                resumo = await DrenarUmaVezAsync(cancelamento).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
            {
                return;
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                // A fila em si falhou — banco travado, disco cheio. Não é motivo para
                // derrubar o processo: a borda continua decidindo acesso sem sincronizar.
                Observar(new OcorrenciaDeDrenagem("(fila)", "falha_na_rodada", 0, Resumir(erro)));
                resumo = ResumoDaRodada.Vazia;
            }

            if (resumo.TeveTrabalho)
            {
                continue;
            }

            try
            {
                await Task.Delay(intervalo, _relogio, cancelamento).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<(int Enviados, int Adiados, int CartasMortas)> DrenarConectorAsync(
        IConectorDeSincronizacao conector,
        CancellationToken cancelamento)
    {
        var agora = _relogio.GetUtcNow();

        var lote = await _fila
            .ProximosAsync(conector.Nome, conector.TamanhoMaximoDoLote, agora, cancelamento)
            .ConfigureAwait(false);

        if (lote.Count == 0)
        {
            return (0, 0, 0);
        }

        IReadOnlyList<RespostaDeItem> respostas;
        try
        {
            respostas = await conector.EnviarAsync(lote, cancelamento).ConfigureAwait(false)
                ?? [];
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            // Parada ordenada. O lote continua pendente, como se nada tivesse acontecido.
            throw;
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Conector que explode é tratado como destino fora do ar, nunca como item
            // ruim: a causa quase sempre é rede, e marcar carta morta aqui perderia o
            // lote inteiro por causa de um cabo.
            respostas = [.. lote.Select(i => new RespostaDeItem(i.Id, ResultadoDoEnvio.FalhaTemporaria, Resumir(erro)))];
            Observar(new OcorrenciaDeDrenagem(conector.Nome, "excecao_no_conector", lote.Count, Resumir(erro)));
        }

        var porId = new Dictionary<string, RespostaDeItem>(StringComparer.Ordinal);
        foreach (var resposta in respostas)
        {
            if (resposta is not null)
            {
                porId[resposta.Id] = resposta;
            }
        }

        var confirmados = new List<string>();
        var adiados = 0;
        var cartasMortas = 0;

        foreach (var item in lote)
        {
            // Item sem resposta é item não enviado. O silêncio nunca é interpretado como
            // sucesso: o custo de repetir é um registro duplicado que a idempotência
            // descarta; o custo de assumir entrega é uma passagem que some do relatório.
            var resposta = porId.TryGetValue(item.Id, out var r)
                ? r
                : new RespostaDeItem(item.Id, ResultadoDoEnvio.FalhaTemporaria, "sem resposta do conector para este item");

            switch (resposta.Resultado)
            {
                case ResultadoDoEnvio.Aceito:
                case ResultadoDoEnvio.Duplicado:
                    confirmados.Add(item.Id);
                    break;

                case ResultadoDoEnvio.FalhaPermanente:
                    await _fila
                        .MoverParaCartasMortasAsync(item.Id, resposta.Erro ?? "recusa permanente sem detalhe", _relogio.GetUtcNow(), cancelamento)
                        .ConfigureAwait(false);
                    cartasMortas++;
                    break;

                default:
                    var tentativas = item.Tentativas + 1;
                    if (tentativas >= _maximoDeTentativas)
                    {
                        await _fila
                            .MoverParaCartasMortasAsync(
                                item.Id,
                                $"esgotadas {tentativas} tentativas; último erro: {resposta.Erro ?? "desconhecido"}",
                                _relogio.GetUtcNow(),
                                cancelamento)
                            .ConfigureAwait(false);
                        cartasMortas++;
                    }
                    else
                    {
                        var proxima = _relogio.GetUtcNow() + _esperaPorTentativa(tentativas);
                        await _fila
                            .AdiarAsync(item.Id, tentativas, proxima, resposta.Erro ?? "falha temporária sem detalhe", cancelamento)
                            .ConfigureAwait(false);
                        adiados++;
                    }

                    break;
            }
        }

        if (confirmados.Count > 0)
        {
            await _fila.MarcarEnviadosAsync(confirmados, _relogio.GetUtcNow(), cancelamento).ConfigureAwait(false);
            Observar(new OcorrenciaDeDrenagem(conector.Nome, "enviados", confirmados.Count));
        }

        if (adiados > 0)
        {
            Observar(new OcorrenciaDeDrenagem(conector.Nome, "adiados", adiados));
        }

        if (cartasMortas > 0)
        {
            Observar(new OcorrenciaDeDrenagem(conector.Nome, "cartas_mortas", cartasMortas));
        }

        return (confirmados.Count, adiados, cartasMortas);
    }

    private void Observar(OcorrenciaDeDrenagem ocorrencia)
    {
        try
        {
            _observador?.Invoke(ocorrencia);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Observador quebrado não pode parar a drenagem.
            _ = erro;
        }
    }

    private static string Resumir(Exception erro) =>
        string.Create(CultureInfo.InvariantCulture, $"{erro.GetType().Name}: {erro.Message}");
}
