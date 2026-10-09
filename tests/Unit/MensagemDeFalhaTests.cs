using Desktop.ViewModels;
using Grpc.Core;

namespace Unit.Tests;

/// <summary>
/// O que o operador lê quando o painel não consegue falar com o serviço. A causa só é afirmada
/// quando é certa: sem permissão, a mensagem diz o grupo; caso contrário, diz que o serviço não
/// respondeu. Nunca o contrário.
/// </summary>
public sealed class MensagemDeFalhaTests : IDisposable
{
    public void Dispose() => MensagemDeFalha.TokenSemPermissao = false;

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public void Servico_fora_do_ar_pede_para_aguardar_o_reinicio(StatusCode codigo)
    {
        MensagemDeFalha.TokenSemPermissao = false;

        Assert.Equal(MensagemDeFalha.SemResposta, MensagemDeFalha.Para(codigo));
    }

    [Fact]
    public void Sem_permissao_a_mensagem_diz_o_grupo_e_nao_manda_reiniciar()
    {
        MensagemDeFalha.TokenSemPermissao = true;

        var mensagem = MensagemDeFalha.Para(StatusCode.Unavailable);

        Assert.Contains("ConexaoTopdata Operadores", mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("reiniciá-lo", mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Resposta_recusada_pelo_servico_mostra_o_codigo_sem_inventar_causa()
    {
        MensagemDeFalha.TokenSemPermissao = false;

        Assert.Equal("O serviço recusou o pedido (Unauthenticated).", MensagemDeFalha.Para(StatusCode.Unauthenticated));
    }

    [Fact]
    public void Painel_sem_permissao_mostra_a_mensagem_do_grupo_mesmo_com_dados_antigos()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
        var estado = EstadoDoPainel.Carregando();

        var falha = estado.ComFalhaDeComunicacao("Unauthenticated", agora, semPermissao: true);

        Assert.Equal(MensagemDeFalha.SemPermissao, falha.Mensagem);
        Assert.True(falha.Desatualizado);
    }

    [Fact]
    public void Painel_com_servico_fora_do_ar_nao_fala_em_permissao()
    {
        var agora = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

        var falha = EstadoDoPainel.Carregando().ComFalhaDeComunicacao("Unavailable", agora, semPermissao: false);

        Assert.DoesNotContain("grupo", falha.Mensagem, StringComparison.Ordinal);
    }
}
