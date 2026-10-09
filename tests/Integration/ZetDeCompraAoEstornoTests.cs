using System.Text;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Sync.Connectors.Rest;

namespace Integration.Tests;

/// <summary>
/// O que a Zet faz de verdade, contra o banco de verdade: compra, estorno e a mesma compra
/// reenviada depois (a Zet reenviou 1.254 pedidos na edição 2025). Ver docs/30.
/// </summary>
public sealed class ZetDeCompraAoEstornoTests
{
    private static readonly DateTimeOffset Agora = new(2025, 11, 20, 19, 0, 0, TimeSpan.FromHours(-3));
    private static readonly TradutorDaZet Tradutor = new("zet", 538);

    private static IReadOnlyList<IngressoRecebido> Webhook(string acao, long id, string voucher, string data = "2025-11-20") =>
        Tradutor.Traduzir(Encoding.UTF8.GetBytes(
            $$$"""
            {"action":"{{{acao}}}","data":{"order":{"id":1,"cpf":null},"event":{"id":538},
              "eventTicketCodes":[{"id":{{{id}}},"voucher":"{{{voucher}}}","used":"NAO",
                "eventsValues":{"sector":"Ingresso","session":"19h15","description":"Inteira",
                  "eventsDates":{"startDate":"{{{data}}}T00:00:00.000Z","endDate":"{{{data}}}T00:00:00.000Z"} } } ] } }
            """), "application/json");

    private static RepositorioDeIngressos Repositorio(BancoTemporario banco)
    {
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Compre no Zet", "raw", "zet-webhook"), Agora.AddDays(-30));
        return repositorio;
    }

    [Fact]
    public void Compra_libera_estorno_barra_e_o_reenvio_da_compra_nao_ressuscita()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var repositorio = Repositorio(banco);

        repositorio.Ingerir(Webhook("CP", 10, "1100001000010"), Agora.AddDays(-1));
        repositorio.Ingerir(Webhook("CP", 11, "1100001000011"), Agora.AddDays(-1));
        repositorio.Ingerir(Webhook("ES", 11, "1100001000011"), Agora.AddHours(-2));
        repositorio.Ingerir(Webhook("CP", 11, "1100001000011"), Agora.AddHours(-1)); // reenvio

        Assert.True(repositorio.TentarUsar("1100001000010", "portao-1", "catraca-01", Agora).Resultado.Liberou);
        var estornado = repositorio.TentarUsar("1100001000011", "portao-1", "catraca-01", Agora).Resultado;
        Assert.False(estornado.Liberou);
        Assert.Equal(MotivoDoUso.Cancelado, estornado.Motivo);
    }

    [Fact]
    public void O_ingresso_so_vale_no_seu_dia_de_operacao()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var repositorio = Repositorio(banco);
        repositorio.Ingerir(Webhook("CP", 20, "1100001000020", data: "2025-11-21"), Agora.AddDays(-1));

        // Dia 20 às 19h: o ingresso é do dia 21.
        Assert.False(repositorio.TentarUsar("1100001000020", "portao-1", "catraca-01", Agora).Resultado.Liberou);
        // Dia 21 às 19h15, e ainda à 01h do dia 22 (antes das 06:00, o dia de operação é o 21).
        Assert.True(repositorio.TentarUsar("1100001000020", "portao-1", "catraca-01", Agora.AddDays(1).AddMinutes(15)).Resultado.Liberou);
    }

    [Fact]
    public void Estorno_que_chega_antes_da_compra_tambem_barra()
    {
        // 183 estornos da edição 2025 não tinham compra recebida por webhook.
        using var banco = new BancoTemporario();
        banco.Migrar();
        var repositorio = Repositorio(banco);

        repositorio.Ingerir(Webhook("ES", 30, "1100001000030"), Agora.AddHours(-3));
        repositorio.Ingerir(Webhook("CP", 30, "1100001000030"), Agora.AddHours(-2));

        Assert.False(repositorio.TentarUsar("1100001000030", "portao-1", "catraca-01", Agora).Resultado.Liberou);
    }
}
