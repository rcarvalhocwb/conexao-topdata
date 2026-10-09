using Contracts;

namespace Unit.Tests;

/// <summary>
/// Trava o limite de espera da conexão local.
/// </summary>
/// <remarks>
/// <para>
/// Escrito depois de um defeito de verdade: <c>NamedPipeClientStream.ConnectAsync</c> sem
/// limite espera para sempre o servidor aparecer. Com o serviço fora do ar, o painel
/// travava em silêncio no Windows em vez de mostrar "Sem resposta do serviço local".
/// </para>
/// <para>
/// O socket de domínio Unix falha na hora quando o arquivo não existe, então a CI Linux
/// passava verde e dava confiança falsa. A espera limitada foi extraída para cá
/// justamente para poder ser exercitada em qualquer plataforma, sem depender de rodar no
/// Windows para descobrir.
/// </para>
/// </remarks>
public sealed class TransporteLocalTests
{
    private static readonly TimeSpan Limite = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Conexao_que_nunca_completa_falha_no_limite_em_vez_de_travar()
    {
        var erro = await Assert.ThrowsAsync<IOException>(() =>
            TransporteLocal.ConectarAsync(
                async (_, cancelamento) =>
                {
                    await Task.Delay(Timeout.Infinite, cancelamento).ConfigureAwait(true);
                    throw new InvalidOperationException("nunca chega aqui");
                },
                "servico-inexistente",
                Limite,
                CancellationToken.None));

        // A mensagem precisa dizer o que fazer, não só que falhou.
        Assert.Contains("Edge.Supervisor", erro.Message, StringComparison.Ordinal);
        Assert.Contains("servico-inexistente", erro.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fechar a janela durante uma atualização não é falha de serviço.
    /// </summary>
    /// <remarks>
    /// Se o cancelamento de fora virasse <see cref="IOException"/>, o painel anunciaria
    /// "serviço fora do ar" toda vez que alguém fechasse o aplicativo.
    /// </remarks>
    [Fact]
    public async Task Cancelamento_de_fora_continua_sendo_cancelamento()
    {
        using var deFora = new CancellationTokenSource();
        await deFora.CancelAsync().ConfigureAwait(true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TransporteLocal.ConectarAsync(
                async (_, cancelamento) =>
                {
                    await Task.Delay(Timeout.Infinite, cancelamento).ConfigureAwait(true);
                    throw new InvalidOperationException("nunca chega aqui");
                },
                "qualquer",
                TimeSpan.FromMinutes(5),
                deFora.Token));
    }

    [Fact]
    public async Task Conexao_que_completa_devolve_o_fluxo()
    {
        await using var fluxo = await TransporteLocal
            .ConectarAsync(
                (_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])),
                "qualquer",
                Limite,
                CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(3, fluxo.Length);
    }

    /// <summary>
    /// O limite precisa ser curto o bastante para o operador perceber, e longo o bastante
    /// para não cortar uma conexão local lenta.
    /// </summary>
    [Fact]
    public void O_limite_padrao_esta_na_faixa_util()
    {
        Assert.InRange(
            TransporteLocal.TempoLimiteDeConexao,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(15));
    }
}
