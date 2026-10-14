using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>O erro interno vai ao registro com tipo e mensagem, sem nenhum dado da requisição.</summary>
public sealed class InterceptadorDeErrosTests
{
    [Fact]
    public void A_linha_traz_metodo_tipo_e_mensagem_mas_nunca_a_senha_da_requisicao()
    {
        var erro = new InvalidOperationException("base indisponível");

        var linha = InterceptadorDeErros.Descrever("/edge.v1.EdgeControl/Entrar", erro);

        Assert.Contains("/edge.v1.EdgeControl/Entrar", linha, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", linha, StringComparison.Ordinal);
        Assert.Contains("base indisponível", linha, StringComparison.Ordinal);
        Assert.DoesNotContain("xacess", linha, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Uma_exceção_sem_pilha_ainda_gera_linha()
    {
        var linha = InterceptadorDeErros.Descrever("/x/Y", new InvalidOperationException());

        Assert.Contains("sem pilha", linha, StringComparison.Ordinal);
    }
}
