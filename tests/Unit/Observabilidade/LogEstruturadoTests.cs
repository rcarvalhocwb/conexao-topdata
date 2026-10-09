using System.Text.Json;
using Shared.Observability;

namespace Unit.Tests.Observabilidade;

public sealed class LogEstruturadoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

    private static (LogEstruturado Log, DestinoEmMemoria Destino) Montar()
    {
        var destino = new DestinoEmMemoria();
        return (new LogEstruturado(destino, "worker-teste", () => Agora), destino);
    }

    private static JsonElement Unico(DestinoEmMemoria destino) =>
        JsonDocument.Parse(Assert.Single(destino.Linhas)).RootElement;

    [Fact]
    public void Escreve_json_valido_com_os_campos_obrigatorios()
    {
        var (log, destino) = Montar();

        log.Informacao("equipamento conectado", "corr-1");

        var registro = Unico(destino);
        Assert.Equal("Informacao", registro.GetProperty("nivel").GetString());
        Assert.Equal("worker-teste", registro.GetProperty("componente").GetString());
        Assert.Equal("corr-1", registro.GetProperty("correlationId").GetString());
        Assert.Equal("equipamento conectado", registro.GetProperty("mensagem").GetString());
        Assert.False(string.IsNullOrWhiteSpace(registro.GetProperty("t").GetString()));
    }

    /// <summary>
    /// Não existe caminho que escreva sem passar pelo redator. É desenho, não
    /// disciplina.
    /// </summary>
    [Fact]
    public void Mensagem_com_cartao_sai_redigida()
    {
        var (log, destino) = Montar();

        log.Informacao("cartao 0001234567 lido", "corr-1");

        Assert.DoesNotContain("0001234567", Assert.Single(destino.Linhas), StringComparison.Ordinal);
    }

    [Fact]
    public void Campo_sensivel_sai_redigido()
    {
        var (log, destino) = Montar();

        log.Informacao("leitura", "corr-1", new Dictionary<string, object?>
        {
            ["cartao"] = "0001234567",
            ["inner"] = 8,
        });

        var registro = Unico(destino);
        Assert.Equal(RedatorDeDadoSensivel.Marca, registro.GetProperty("cartao").GetString());
        Assert.Equal("8", registro.GetProperty("inner").GetString());
    }

    /// <summary>O carimbo de tempo precisa continuar legível depois da redação.</summary>
    [Fact]
    public void Carimbo_de_tempo_sobrevive()
    {
        var (log, destino) = Montar();

        log.Informacao("qualquer", "corr-1");

        var t = Unico(destino).GetProperty("t").GetString();
        Assert.NotNull(t);
        Assert.True(DateTimeOffset.TryParse(t, out var lido), $"carimbo ilegível: {t}");
        Assert.Equal(Agora, lido);
    }

    [Fact]
    public void Exige_correlation_id()
    {
        var (log, _) = Montar();

        Assert.Throws<ArgumentException>(() => log.Informacao("sem correlação", "  "));
    }

    [Fact]
    public void Todos_os_niveis_escrevem()
    {
        var (log, destino) = Montar();

        log.Depuracao("d", "c");
        log.Informacao("i", "c");
        log.Aviso("a", "c");
        log.Erro("e", "c");
        log.Critico("x", "c");

        Assert.Equal(5, destino.Linhas.Count);
    }
}
