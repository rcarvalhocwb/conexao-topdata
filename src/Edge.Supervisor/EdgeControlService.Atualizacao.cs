using Contracts.Edge.V1;
using Edge.Supervisor.Atualizacoes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>Atualização automática (docs/60 §2): ver a situação e o aviso de "painel finalizado".</summary>
public sealed partial class EdgeControlService
{
    /// <summary>A atualização automática; nula em testes antigos e sem base.</summary>
    public AtualizacaoAutomatica? Atualizacao { get; init; }

    public override Task<SituacaoDaAtualizacao> ObterAtualizacao(ObterAtualizacaoRequest request, ServerCallContext context)
    {
        if (Atualizacao is null)
        {
            return Task.FromResult(new SituacaoDaAtualizacao
            {
                VersaoInstalada = _versao,
                Situacao = "Atualização automática não disponível neste serviço.",
            });
        }

        var s = Atualizacao.Situacao();
        var resposta = new SituacaoDaAtualizacao
        {
            Ligada = s.Ligada,
            Canal = s.Canal switch
            {
                OpcoesDaAtualizacao.CanalDeProducao => "Produção",
                "teste" => "Teste",
                _ => "Desconhecido",
            },
            VersaoInstalada = s.VersaoInstalada,
            VersaoDisponivel = s.VersaoDisponivel ?? string.Empty,
            Situacao = s.Situacao,
            UltimoResultado = s.UltimoResultado ?? string.Empty,
        };

        if (s.UltimaVerificacao is { } quando)
        {
            resposta.UltimaVerificacao = Timestamp.FromDateTimeOffset(quando);
        }

        return Task.FromResult(resposta);
    }

    public override Task<AvisarPainelFinalizadoResponse> AvisarPainelFinalizado(AvisarPainelFinalizadoRequest request, ServerCallContext context) =>
        Task.FromResult(new AvisarPainelFinalizadoResponse
        {
            VaiInstalar = Atualizacao?.PainelFinalizado(_relogio()) ?? false,
        });
}
