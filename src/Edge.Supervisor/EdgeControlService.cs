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
    private readonly WorkerSupervisor _supervisor;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly string _versao;

    public EdgeControlService(WorkerSupervisor supervisor, string? versao = null, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervisor = supervisor;
        _versao = versao ?? "0.1.0-fase1";
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Eventos a entregar ao aplicativo. Preenchido pela operação.</summary>
    public System.Threading.Channels.Channel<EventoDeAcesso> Eventos { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<EventoDeAcesso>();

    public override Task<ObterEstadoResponse> ObterEstado(ObterEstadoRequest request, ServerCallContext context)
    {
        var situacoes = _supervisor.Situacoes;

        return Task.FromResult(new ObterEstadoResponse
        {
            Versao = _versao,
            WorkersAtivos = situacoes.Count(s => s.Value is SituacaoDoWorker.Saudavel),
            EquipamentosConectados = _supervisor.Workers
                .Where(w => situacoes[w.Nome] is SituacaoDoWorker.Saudavel)
                .Sum(w => w.Inners.Count),
            OutboxPendente = 0,
            OutboxIdadeMaisAntigoSegundos = 0,

            // Sem internet é o regime NORMAL de um evento, não uma anomalia.
            // Ver docs/ADR/ADR-0017.
            Nivel = NivelDeDegradacao.T1SemInternet,
            InternetDisponivel = false,
        });
    }

    public override Task<ListarEquipamentosResponse> ListarEquipamentos(
        ListarEquipamentosRequest request,
        ServerCallContext context)
    {
        var resposta = new ListarEquipamentosResponse();
        var situacoes = _supervisor.Situacoes;
        var agora = _relogio();

        foreach (var worker in _supervisor.Workers)
        {
            var saudavel = situacoes[worker.Nome] is SituacaoDoWorker.Saudavel;

            foreach (var inner in worker.Inners)
            {
                resposta.Equipamentos.Add(new Equipamento
                {
                    Inner = inner,
                    NomeDoGate = $"{worker.Nome}/{inner}",
                    Worker = worker.Nome,
                    Porta = worker.Porta,
                    Estado = situacoes[worker.Nome].ToString(),
                    Saudavel = saudavel,
                    UltimoEvento = Timestamp.FromDateTimeOffset(agora),
                    Firmware = string.Empty,
                    Homologado = false,
                });
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

        await foreach (var evento in Eventos.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        {
            if (filtro.Count > 0 && !filtro.Contains(evento.Inner))
            {
                continue;
            }

            await responseStream.WriteAsync(evento, context.CancellationToken).ConfigureAwait(false);
        }
    }
}
