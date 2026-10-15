using Contracts.Edge.V1;
using Grpc.Core;
using Sync.Core;

namespace Edge.Supervisor;

/// <summary>O botão "Testar conexão com a nuvem" da tela Sincronização.</summary>
public sealed partial class EdgeControlService
{
    /// <summary>
    /// Roda uma rodada com a nuvem agora. Nulo quando a nuvem não está configurada neste computador; o
    /// serviço liga à <see cref="SincronizacaoComANuvem.RodarAsync"/>.
    /// </summary>
    public Func<CancellationToken, Task<ResultadoDaRodada>>? RodadaDaNuvem { get; init; }

    // Uma rodada que não termina não pode prender o painel: a requisição HTTP já tem 30 s por chamada.
    private static readonly TimeSpan LimiteDoTeste = TimeSpan.FromSeconds(90);

    public override async Task<ResultadoDoTesteDaNuvem> TestarNuvem(TestarNuvemRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (RodadaDaNuvem is null)
        {
            return new ResultadoDoTesteDaNuvem
            {
                Configurada = false,
                Orientacao = "A nuvem não está configurada neste computador. Configure pelo Assistente de configuração, passo Nuvem.",
            };
        }

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        limite.CancelAfter(LimiteDoTeste);

        ResultadoDaRodada r;
        try
        {
            r = await RodadaDaNuvem(limite.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return new ResultadoDoTesteDaNuvem
            {
                Configurada = true,
                Orientacao = "O teste passou de 90 segundos sem terminar. Confira a internet deste computador e tente de novo.",
            };
        }

        return new ResultadoDoTesteDaNuvem
        {
            Configurada = true,
            Respondeu = r.Respondeu,
            Completo = r.Sucesso,
            ListaIncompletaCom = r.ListaIncompletaCom ?? 0,
            Novos = r.Novos,
            Atualizados = r.Atualizados,
            Enviadas = r.Enviadas,
            Adiadas = r.Adiadas,
            Orientacao = r.Sucesso ? string.Empty : Orientacao(r),
            Detalhe = r.Motivo ?? string.Empty,
            DuracaoMs = (long)r.Duracao.TotalMilliseconds,
        };
    }

    /// <summary>O que o operador faz, pelo tipo da falha. Nunca "procure o TI" sem dizer o quê.</summary>
    internal static string Orientacao(ResultadoDaRodada r)
    {
        ArgumentNullException.ThrowIfNull(r);

        if (r.Motivo == SincronizacaoComANuvem.FaltaOSegredo)
        {
            return "Falta o segredo da nuvem neste computador. Grave-o pelo Assistente de configuração, passo Nuvem.";
        }

        if (r.ListaIncompletaCom is { } veio)
        {
            return $"A nuvem respondeu, mas mandou só {veio} cartões e parou no limite dela. Os que vieram já valem aqui. Avise quem cuida da nuvem.";
        }

        return r.TipoDaFalha switch
        {
            TipoDeFalha.Rede =>
                "Não chegou à nuvem: confira a internet deste computador (cabo, Wi-Fi, firewall). As catracas seguem funcionando com a lista local.",
            TipoDeFalha.Autenticacao =>
                "A nuvem respondeu, mas recusou o segredo deste computador. Confira o segredo e o identificador do equipamento no Assistente de configuração.",
            TipoDeFalha.Servidor =>
                "A nuvem respondeu com erro do lado dela. Tente de novo em alguns minutos; se continuar, avise quem cuida da nuvem.",
            _ =>
                "A nuvem respondeu algo inesperado. Tente de novo; se continuar, mande o detalhe abaixo ao suporte.",
        };
    }
}
