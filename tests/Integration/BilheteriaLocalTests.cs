using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using static Access.Infrastructure.SQLite.RepositorioDeIngressos;

namespace Integration.Tests;

/// <summary>
/// A bilheteria local: cartão físico RFID/NFC vendido, usado, devolvido e revendido.
/// </summary>
/// <remarks>
/// O cartão é um recipiente; o que se vende é o uso. E o intervalo mínimo de reuso não é
/// regra de conveniência — é o que separa o ciclo físico honesto (passar, devolver,
/// revender) do cartão passado por cima da grade.
/// Ver docs/19-bilheteria-local-e-divisao-das-catracas.md
/// </remarks>
public sealed class BilheteriaLocalTests
{
    private const string Bilheteria = "bilheteria-local";
    private const string Online = "zet";
    private static readonly DateTimeOffset Abertura = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(4);

    private static RepositorioDeIngressos Preparar(BancoTemporario banco, string conectorDaBilheteria = "")
    {
        banco.Migrar();
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);

        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Bilheteria, "Bilheteria local", "raw", conectorDaBilheteria,
                Reutilizavel: true, IntervaloDeReuso: Intervalo),
            Abertura);

        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso(Online, "Zet", "raw", "zet-rest"),
            Abertura);

        return repositorio;
    }

    private static ResultadoDoUso Passar(RepositorioDeIngressos r, string cartao, DateTimeOffset quando) =>
        r.TentarUsar(cartao, "portao-1", "catraca-01", quando).Resultado;

    [Fact]
    public void Primeira_venda_cria_o_cartao_e_ele_gira()
    {
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        Assert.Equal(ResultadoDaVenda.Vendido, r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura));

        var uso = Passar(r, "04A1B2C3", Abertura.AddMinutes(1));

        Assert.True(uso.Liberou);
        Assert.Equal("inteira", uso.Categoria);
    }

    [Fact]
    public void Cartao_passado_por_cima_da_grade_e_revendido_na_hora_nao_gira()
    {
        // O golpe: a pessoa entra, joga o cartão para fora, o comparsa entrega no balcão,
        // e ele é revendido na hora. A revenda é aceita — o balcão não tem como saber —
        // mas a catraca recusa, porque o relógio de reuso não foi zerado.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura);
        Assert.True(Passar(r, "04A1B2C3", Abertura.AddMinutes(1)).Liberou);

        Assert.Equal(ResultadoDaVenda.Vendido, r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura.AddMinutes(2)));

        var segunda = Passar(r, "04A1B2C3", Abertura.AddMinutes(2).AddSeconds(30));

        Assert.False(segunda.Liberou);
        Assert.Equal(MotivoDoUso.EmIntervaloDeReuso, segunda.Motivo);
    }

    [Fact]
    public void Depois_do_ciclo_fisico_honesto_o_cartao_revendido_gira()
    {
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura);
        Passar(r, "04A1B2C3", Abertura.AddMinutes(1));

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura.AddMinutes(4));

        // Exatamente no limite: 1 min + 4 min de intervalo = 5 min.
        var naHora = Passar(r, "04A1B2C3", Abertura.AddMinutes(5));

        Assert.True(naHora.Liberou);
        Assert.Equal("meia", naHora.Categoria);
    }

    [Fact]
    public void Nao_revende_cartao_com_venda_paga_e_nao_usada()
    {
        // Sobrescrever uma venda paga e não usada apaga dinheiro — e apaga a entrada de
        // quem pagou.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura);

        Assert.Equal(
            ResultadoDaVenda.VendaAnteriorNaoUsada,
            r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura.AddMinutes(10)));

        // E a venda original continua valendo, na categoria original.
        Assert.Equal("inteira", Passar(r, "04A1B2C3", Abertura.AddMinutes(11)).Categoria);
    }

    [Fact]
    public void Provedor_online_nao_vende_no_balcao()
    {
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        Assert.Equal(ResultadoDaVenda.ProvedorNaoReutilizavel, r.VenderNoBalcao(Online, "04A1B2C3", "inteira", Abertura));
        Assert.Equal(ResultadoDaVenda.ProvedorDesconhecido, r.VenderNoBalcao("nao-existe", "04A1B2C3", "inteira", Abertura));
    }

    [Fact]
    public void Codigo_de_cartao_que_ja_e_ingresso_online_e_recusado_no_balcao()
    {
        // O QR é único no evento inteiro — vale também entre cartão físico e ingresso
        // online. A catraca não tem como saber de quem é.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);
        r.Ingerir([new IngressoRecebido(Online, "ZET-1", "04A1B2C3", "04A1B2C3")], Abertura);

        Assert.Equal(
            ResultadoDaVenda.CodigoDeOutroProvedor,
            r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura));
    }

    [Fact]
    public void Cartao_bloqueado_nao_e_revendido()
    {
        using var banco = new BancoTemporario();
        var r = Preparar(banco);
        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura);
        Passar(r, "04A1B2C3", Abertura.AddMinutes(1));

        using (var conexao = banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "UPDATE ticket SET status = 'bloqueado' WHERE qr_normalized = '04A1B2C3';";
            comando.ExecuteNonQuery();
        }

        Assert.Equal(
            ResultadoDaVenda.CartaoBloqueado,
            r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura.AddMinutes(30)));
    }

    [Fact]
    public void A_categoria_de_cada_uso_e_a_da_venda_nao_a_do_cartao_hoje()
    {
        // Se o relatório lesse a categoria atual do cartão, a meia-entrada vendida às 18h
        // viraria a inteira vendida às 18h10, e a prestação de contas por tipo mentiria.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura);
        Passar(r, "04A1B2C3", Abertura.AddMinutes(1));

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura.AddMinutes(8));
        Passar(r, "04A1B2C3", Abertura.AddMinutes(10));

        var resumo = r.ResumoPorCategoria(Bilheteria, Abertura.AddHours(6));

        Assert.Equal(
            [new LinhaPorCategoria("inteira", 1, 1), new LinhaPorCategoria("meia", 1, 1)],
            resumo);
    }

    [Fact]
    public void Categoria_nova_nao_exige_versao_nova_do_sistema()
    {
        // "Solidária" hoje; amanhã "cortesia", "idoso", "estudante". Texto aberto.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04000001", "solidaria", Abertura);
        r.VenderNoBalcao(Bilheteria, "04000002", "cortesia-patrocinador", Abertura);
        Passar(r, "04000001", Abertura.AddMinutes(1));

        var resumo = r.ResumoPorCategoria(Bilheteria, Abertura.AddHours(6));

        Assert.Contains(new LinhaPorCategoria("cortesia-patrocinador", 1, 0), resumo);
        Assert.Contains(new LinhaPorCategoria("solidaria", 1, 1), resumo);
        Assert.Equal(1, resumo.Single(l => l.Categoria == "cortesia-patrocinador").Saldo);
    }

    [Fact]
    public void A_conta_da_bilheteria_conta_vendas_e_nao_cartoes()
    {
        // Um cartão, três vendas. Contar linhas de cartão diria "1 recebido", e isso não
        // é prestação de contas de nada.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        for (var i = 0; i < 3; i++)
        {
            var t = Abertura.AddMinutes(i * 10);
            r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", t);
            Passar(r, "04A1B2C3", t.AddMinutes(1));
        }

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura.AddMinutes(40));

        var conta = r.Conciliar(Bilheteria, Abertura.AddHours(6));

        Assert.Equal(4, conta.IngressosRecebidos);
        Assert.Equal(3, conta.UsosConsumidos);
        Assert.Equal(1, conta.NuncaUsados);
        Assert.Equal(0, conta.AvisosPendentesDeConfirmacao);
    }

    [Fact]
    public void Bilheteria_local_sem_conector_nao_gera_aviso_de_saida()
    {
        // O sistema dela é este aqui. Não há ninguém do outro lado para dar baixa, e um
        // aviso sem destino ficaria na fila para sempre.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura);
        Passar(r, "04A1B2C3", Abertura.AddMinutes(1));

        Assert.Empty(new FilaDeSaidaSqlite(banco.Fabrica).BacklogPorConector());
    }

    [Fact]
    public void Cartao_revendido_com_conector_gera_um_aviso_por_venda_e_nao_um_so()
    {
        // O bug que quase entrou: a chave era uso:{cartão}:{número do uso}. A revenda
        // zera o contador, então o primeiro uso da segunda venda repetia a chave do
        // primeiro uso da primeira — e o INSERT OR IGNORE descartava o aviso em silêncio.
        using var banco = new BancoTemporario();
        var r = Preparar(banco, conectorDaBilheteria: "sistema-central");

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "inteira", Abertura);
        Passar(r, "04A1B2C3", Abertura.AddMinutes(1));

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura.AddMinutes(8));
        Passar(r, "04A1B2C3", Abertura.AddMinutes(10));

        Assert.Equal(2L, new FilaDeSaidaSqlite(banco.Fabrica).BacklogPorConector()["sistema-central"]);
    }

    [Fact]
    public void Ingresso_online_nao_e_afetado_pelo_intervalo_da_bilheteria()
    {
        // O intervalo é política do provedor, não do sistema. Um passe online de dois
        // usos pode girar duas vezes seguidas — é reentrada, e o provedor permitiu.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);
        r.Ingerir([new IngressoRecebido(Online, "ZET-1", "ZET-QR-1", "ZET-QR-1", UsosMaximos: 2)], Abertura);

        Assert.True(Passar(r, "ZET-QR-1", Abertura.AddMinutes(1)).Liberou);
        Assert.True(Passar(r, "ZET-QR-1", Abertura.AddMinutes(1).AddSeconds(20)).Liberou);
    }

    [Fact]
    public void A_mesma_catraca_valida_cartao_da_bilheteria_e_ingresso_online()
    {
        // O software não separa por catraca: separa por dado. Se o hardware ler os dois,
        // a mesma catraca serve os dois — e a conta sai separada do mesmo jeito.
        using var banco = new BancoTemporario();
        var r = Preparar(banco);

        r.VenderNoBalcao(Bilheteria, "04A1B2C3", "meia", Abertura);
        r.Ingerir([new IngressoRecebido(Online, "ZET-1", "ZET-QR-1", "ZET-QR-1", Categoria: "inteira")], Abertura);

        Assert.Equal(Bilheteria, Passar(r, "04A1B2C3", Abertura.AddMinutes(1)).ProvedorId);
        Assert.Equal(Online, Passar(r, "ZET-QR-1", Abertura.AddMinutes(1)).ProvedorId);

        Assert.Equal(1, r.Conciliar(Bilheteria, Abertura.AddHours(6)).UsosConsumidos);
        Assert.Equal(1, r.Conciliar(Online, Abertura.AddHours(6)).UsosConsumidos);
    }
}
