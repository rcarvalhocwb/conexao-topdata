using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Relatórios R1, R2, R3 e R7 (docs/25 §3): validado é liberou e girou; o dia de operação vai do corte
/// ao corte (uma noite que passa da meia-noite é um dia só); o código de conferência só muda se os
/// números mudarem. Números fictícios.
/// </summary>
public sealed class RelatoriosDoEventoTests : IDisposable
{
    private static readonly TimeSpan Corte = TimeSpan.FromHours(6);
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repo;

    public RelatoriosDoEventoTests()
    {
        _banco.Migrar();
        _repo = new RepositorioDeIngressos(_banco.Fabrica);
        var inicio = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _repo.RegistrarProvedor(new ProvedorDeIngresso("bilheteria-local", "Bilheteria", "raw", "", Reutilizavel: true,
            IntervaloDeReuso: TimeSpan.FromMinutes(4), SomenteNaUrna: true), inicio);
        _repo.GarantirTiposDeEntrada(["INTEIRA", "MEIA"], "Teste", inicio);
        _repo.Ingerir(
        [
            Cartao("900000000201", "INTEIRA"), Cartao("900000000202", "INTEIRA"), Cartao("900000000203", "INTEIRA"),
            Cartao("900000000204", "MEIA"), Cartao("900000000205", "MEIA"),
        ], inicio);
    }

    public void Dispose() => _banco.Dispose();

    private static IngressoRecebido Cartao(string numero, string tipo) =>
        new("bilheteria-local", numero, numero, numero, UsosMaximos: int.MaxValue, Categoria: tipo);

    private static DateTimeOffset NoEvento(int dia, int hora, int minuto = 0) => new(2026, 10, dia, hora, minuto, 0, Brasilia);

    private void Passar(string numero, DateTimeOffset quando, bool girou = true)
    {
        var (resultado, tentativa) = _repo.TentarUsar(numero, "portao-1", "inner-1", quando, leitor: KnownEventOrigin.Leitor2);
        Assert.True(resultado.Liberou, resultado.Motivo.ToString());
        if (girou)
        {
            _repo.ConfirmarPassagemFisica(tentativa, quando.AddSeconds(2));
        }
    }

    private void Movimentar()
    {
        // Noite de 20/10: das 23h às 2h da madrugada de 21/10 é o mesmo dia de operação (corte às 6h).
        Passar("900000000201", NoEvento(20, 23));
        Passar("900000000202", NoEvento(21, 1, 30));
        Passar("900000000204", NoEvento(21, 2));
        Passar("900000000205", NoEvento(21, 2, 10), girou: false);
        _repo.TentarUsar("900000000999", "portao-1", "inner-1", NoEvento(21, 2, 20), leitor: KnownEventOrigin.Leitor2);

        // Noite de 21/10: outro dia de operação; o 201 volta (reentrada no evento, outro dia).
        Passar("900000000203", NoEvento(21, 20));
        Passar("900000000201", NoEvento(21, 20, 30));
    }

    private RelatorioDoPeriodo Gerar() =>
        new GeradorDeRelatorios(_banco.Fabrica).Gerar(NoEvento(20, 6), NoEvento(22, 6), Corte);

    [Fact]
    public void R1_separa_os_dias_pelo_corte_e_conta_validado_so_com_giro()
    {
        Movimentar();

        var r = Gerar();

        Assert.Equal(2, r.Dias.Count);
        var noite1 = r.Dias[0];
        Assert.Equal(NoEvento(20, 6), noite1.Inicio);
        Assert.Equal([new Contagem("INTEIRA", 2), new Contagem("MEIA", 1)], noite1.Validados);
        Assert.Equal([new Contagem("MEIA", 1)], noite1.SemPassagem);
        Assert.Equal([new Contagem(nameof(MotivoDoUso.Desconhecido), 1)], noite1.Negados);
        Assert.Equal(3, noite1.Distintos);
        Assert.Equal(NoEvento(20, 23), noite1.Primeiro);
        Assert.Equal(NoEvento(21, 2), noite1.Ultimo);

        var noite2 = r.Dias[1];
        Assert.Equal([new Contagem("INTEIRA", 2)], noite2.Validados);
        Assert.Equal(NoEvento(21, 20), noite2.HoraDePico);
        Assert.Equal(2, noite2.NoPico);
    }

    [Fact]
    public void R2_e_R3_contam_por_hora_tipo_e_catraca()
    {
        Movimentar();

        var r = Gerar();

        Assert.Contains(r.PorHora, h => h.Hora == NoEvento(21, 2) && h.Tipo == "MEIA" && h.Quantidade == 1);
        Assert.Contains(r.PorHora, h => h.Hora == NoEvento(21, 20) && h.Tipo == "INTEIRA" && h.Quantidade == 2);
        var catraca = Assert.Single(r.PorCatraca);
        Assert.Equal(("inner-1", 5L, 1L, 1L), (catraca.Dispositivo, catraca.Validados, catraca.SemPassagem, catraca.Negados));
        Assert.Equal(["INTEIRA", "MEIA"], r.Tipos);
        Assert.Equal("Meia-entrada", r.NomesDosTipos["MEIA"]);
    }

    [Fact]
    public void Codigo_de_conferencia_so_muda_quando_os_numeros_mudam()
    {
        Movimentar();

        var primeiro = Gerar().Conferencia;
        Assert.Equal(primeiro, Gerar().Conferencia);
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", primeiro);

        Passar("900000000202", NoEvento(21, 21));
        Assert.NotEqual(primeiro, Gerar().Conferencia);
    }

    [Fact]
    public void Registro_de_cartoes_em_lote_nao_conta_como_negado()
    {
        _repo.AbrirSessaoDeCadastro("bilheteria-local", "INTEIRA", "Lote 1", "Operadora Teste", 1, NoEvento(21, 1));
        _repo.TentarUsar("900000000777", "portao-1", "inner-1", NoEvento(21, 1, 5), leitor: KnownEventOrigin.Leitor2);

        var r = Gerar();

        Assert.All(r.Dias, d => Assert.Empty(d.Negados));
    }
}
