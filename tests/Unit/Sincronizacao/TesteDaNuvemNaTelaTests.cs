using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Unit.Tests.Sincronizacao;

/// <summary>
/// O resultado do botão "Testar conexão com a nuvem" em português de operador: o que houve, os números
/// e o que fazer. Verde só quando deu tudo certo; vermelho quando a nuvem nem respondeu.
/// </summary>
public sealed class TesteDaNuvemNaTelaTests
{
    [Fact]
    public void Tudo_certo_e_verde_com_os_numeros()
    {
        var (sinal, resultado, orientacao) = SincronizacaoViewModel.DescreverTeste(new ResultadoDoTesteDaNuvem
        {
            Configurada = true, Respondeu = true, Completo = true, Novos = 3, Enviadas = 2, DuracaoMs = 1200,
        });

        Assert.Equal(Sinal.Bom, sinal);
        Assert.StartsWith("Conexão com a nuvem OK", resultado, StringComparison.Ordinal);
        Assert.Contains("3 cartão(ões) novo(s)", resultado, StringComparison.Ordinal);
        Assert.Contains("2 passagem(ns) enviada(s)", resultado, StringComparison.Ordinal);
        Assert.Empty(orientacao);
    }

    [Fact]
    public void Respondeu_com_problema_e_atencao_e_leva_o_detalhe()
    {
        var (sinal, resultado, orientacao) = SincronizacaoViewModel.DescreverTeste(new ResultadoDoTesteDaNuvem
        {
            Configurada = true, Respondeu = true, Orientacao = "Confira o segredo.", Detalhe = "HTTP 401",
        });

        Assert.Equal(Sinal.Atencao, sinal);
        Assert.StartsWith("A nuvem respondeu, mas com problema", resultado, StringComparison.Ordinal);
        Assert.Equal("Confira o segredo. Detalhe: HTTP 401", orientacao);
    }

    [Fact]
    public void Sem_resposta_e_problema_e_lembra_que_as_catracas_seguem()
    {
        var (sinal, resultado, _) = SincronizacaoViewModel.DescreverTeste(new ResultadoDoTesteDaNuvem
        {
            Configurada = true, Orientacao = "Confira a internet.",
        });

        Assert.Equal(Sinal.Problema, sinal);
        Assert.Contains("As catracas seguem com a lista local", resultado, StringComparison.Ordinal);
    }
}
