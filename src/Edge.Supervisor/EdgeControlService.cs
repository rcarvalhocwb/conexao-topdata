using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>
/// Implementa o contrato IPC a partir do estado do supervisor.
/// </summary>
/// <remarks>
/// <para>
/// A interface gráfica conversa só com isto. Ela nunca carrega a DLL, nunca abre o
/// banco e nunca fala com um equipamento — o que a mantém viva quando um worker morre.
/// Ver docs/ADR/ADR-0001 e ADR-0004.
/// </para>
/// <para>
/// Nenhum método devolve credencial em texto claro: o próprio contrato não tem campo
/// para isso.
/// </para>
/// </remarks>
public sealed class EdgeControlService : EdgeControl.EdgeControlBase
{
    /// <summary>
    /// Sem notícia do worker há mais que isto, a catraca deixa de contar como em operação.
    /// O worker publica a cada 2 s; a folga cobre uma base ocupada por alguns segundos.
    /// </summary>
    public static readonly TimeSpan NoticiaVelha = TimeSpan.FromSeconds(15);

    private readonly WorkerSupervisor _supervisor;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly string _versao;
    private readonly Access.Infrastructure.SQLite.Operacao? _operacao;
    private readonly EstadoDaNuvem? _nuvem;

    /// <param name="supervisor">Os workers.</param>
    /// <param name="versao">Versão exibida no painel.</param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="operacao">
    /// Base local da operação (ADR-0024). Sem ela, o serviço só sabe dos processos, não
    /// das catracas.
    /// </param>
    /// <param name="nuvem">Situação da sincronização com a nuvem.</param>
    public EdgeControlService(
        WorkerSupervisor supervisor,
        string? versao = null,
        Func<DateTimeOffset>? relogio = null,
        Access.Infrastructure.SQLite.Operacao? operacao = null,
        EstadoDaNuvem? nuvem = null)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervisor = supervisor;
        _versao = versao ?? "0.1.0-fase1";
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
        _operacao = operacao;
        _nuvem = nuvem;
    }

    /// <summary>Por onde os acessos chegam aos painéis conectados.</summary>
    public DifusorDeEventos Eventos { get; } = new();

    public override Task<ObterEstadoResponse> ObterEstado(ObterEstadoRequest request, ServerCallContext context)
    {
        var situacoes = _supervisor.Situacoes;
        var agora = _relogio();
        var catracas = CatracasPorInner(agora);
        var ultimaSincronizacao = _nuvem?.UltimoSucesso;
        var internet = ultimaSincronizacao is { } s && agora - s < EstadoDaNuvem.ConsideradaFora;

        var resposta = new ObterEstadoResponse
        {
            Versao = _versao,
            WorkersAtivos = situacoes.Count(x => x.Value is SituacaoDoWorker.Saudavel),
            EquipamentosCadastrados = _supervisor.Workers.Sum(w => w.Inners.Count),
            EquipamentosConectados = _operacao is null
                ? _supervisor.Workers.Where(w => situacoes[w.Nome] is SituacaoDoWorker.Saudavel).Sum(w => w.Inners.Count)
                : catracas.Values.Count(c => c.EmOperacao),

            // Sem internet é o regime NORMAL de um evento, não uma anomalia.
            // Ver docs/ADR/ADR-0017.
            Nivel = internet ? NivelDeDegradacao.T0Normal : NivelDeDegradacao.T1SemInternet,
            InternetDisponivel = internet,
        };

        if (ultimaSincronizacao is { } quando)
        {
            resposta.UltimaSincronizacao = Timestamp.FromDateTimeOffset(quando);
        }

        if (_operacao is not null)
        {
            var resumo = _operacao.Resumir(agora);
            resposta.Liberados = resumo.Liberados;
            resposta.Negados = resumo.Negados;
            resposta.Giros = resumo.Giros;
            resposta.LiberadosUltimos5Minutos = resumo.LiberadosNosUltimos5Minutos;
            resposta.CartasMortas = resumo.CartasMortas;
            resposta.OutboxPendente = resumo.PendentesDeEnvio;
            resposta.OutboxIdadeMaisAntigoSegundos = resumo.PendenteMaisAntigo is { } antigo
                ? (long)Math.Max(0, (agora - antigo).TotalSeconds)
                : 0;
        }

        return Task.FromResult(resposta);
    }

    public override Task<ListarEquipamentosResponse> ListarEquipamentos(
        ListarEquipamentosRequest request,
        ServerCallContext context)
    {
        var resposta = new ListarEquipamentosResponse();
        var situacoes = _supervisor.Situacoes;
        var agora = _relogio();
        var catracas = CatracasPorInner(agora);

        foreach (var worker in _supervisor.Workers)
        {
            var workerSaudavel = situacoes[worker.Nome] is SituacaoDoWorker.Saudavel;

            foreach (var inner in worker.Inners)
            {
                var equipamento = new Equipamento
                {
                    Inner = inner,
                    NomeDoGate = $"{worker.Nome}/{inner}",
                    Worker = worker.Nome,
                    Porta = worker.Porta,
                    Estado = situacoes[worker.Nome].ToString(),
                    Saudavel = workerSaudavel,
                    UltimoEvento = Timestamp.FromDateTimeOffset(agora),
                    Firmware = string.Empty,
                    Homologado = false,
                };

                if (catracas.TryGetValue(inner, out var c))
                {
                    equipamento.Estado = c.Velha ? $"sem notícia do worker ({c.Situacao.Estado})" : c.Situacao.Estado;
                    equipamento.EmOperacao = c.EmOperacao;
                    equipamento.Saudavel = workerSaudavel && c.EmOperacao;
                    equipamento.Firmware = c.Situacao.Firmware ?? string.Empty;
                    equipamento.TentativasDeReconexao = c.Situacao.TentativasDeReconexao;
                    equipamento.UltimaDecisao = c.Situacao.UltimaDecisao ?? string.Empty;
                    equipamento.NoticiaEm = Timestamp.FromDateTimeOffset(c.Situacao.AtualizadoEm);

                    if (c.Situacao.UltimoEventoEm is { } evento)
                    {
                        equipamento.UltimoEvento = Timestamp.FromDateTimeOffset(evento);
                    }
                }
                else if (_operacao is not null)
                {
                    equipamento.Estado = workerSaudavel ? "aguardando a catraca" : situacoes[worker.Nome].ToString();
                    equipamento.Saudavel = false;
                }

                resposta.Equipamentos.Add(equipamento);
            }
        }

        return Task.FromResult(resposta);
    }

    public override async Task AcompanharEventos(
        AcompanharEventosRequest request,
        IServerStreamWriter<EventoDeAcesso> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var filtro = request.Inners.ToHashSet();
        using var assinatura = Eventos.Assinar();

        await foreach (var evento in assinatura.Leitor.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        {
            if (filtro.Count > 0 && !filtro.Contains(evento.Inner))
            {
                continue;
            }

            await responseStream.WriteAsync(evento, context.CancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record Catraca(Access.Infrastructure.SQLite.SituacaoDoEquipamento Situacao, bool Velha)
    {
        public bool EmOperacao => Situacao.Online && !Velha;
    }

    private Dictionary<int, Catraca> CatracasPorInner(DateTimeOffset agora)
    {
        if (_operacao is null)
        {
            return [];
        }

        try
        {
            return _operacao.ListarSituacao()
                .GroupBy(s => s.Inner)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var s = g.OrderByDescending(x => x.AtualizadoEm).First();
                        return new Catraca(s, agora - s.AtualizadoEm > NoticiaVelha);
                    });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Base ocupada: o painel mostra o que sabe dos processos e tenta de novo.
            return [];
        }
    }
}
