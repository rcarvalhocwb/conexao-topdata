using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Todo motivo de negativa chega ao operador em português, e não com o nome do enum.
/// </summary>
/// <remarks>
/// A tela de acessos e o resumo das negativas mostram <see cref="AcompanhamentoDaOperacao.MensagemPara"/>.
/// Um motivo novo sem tradução apareceria como "Negado · TipoInativo" (docs/35 B.2).
/// </remarks>
public sealed class MotivosEmPortuguesTests
{
    [Fact]
    public void Todo_motivo_de_negativa_tem_mensagem_propria()
    {
        foreach (var motivo in Enum.GetValues<MotivoDoUso>().Where(m => m != MotivoDoUso.Consumido))
        {
            var mensagem = AcompanhamentoDaOperacao.MensagemPara(Tentativa(motivo.ToString()));

            Assert.StartsWith("Negado · ", mensagem, StringComparison.Ordinal);
            Assert.DoesNotContain(motivo.ToString(), mensagem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tipo_inativo_aparece_como_tipo_de_entrada_desativado()
    {
        Assert.Equal(
            "Negado · tipo de entrada desativado",
            AcompanhamentoDaOperacao.MensagemPara(Tentativa(nameof(MotivoDoUso.TipoInativo), "MEIA")));
    }

    private static TentativaParaOPainel Tentativa(string motivo, string? categoria = null) =>
        new(1, Guid.NewGuid(), "catraca-01", "portao-1", DateTimeOffset.UnixEpoch, Liberou: false,
            motivo, categoria, "zet", "cred:****01(10)", Girou: false);
}
