using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// Achado E1-05 do docs/41, com a decisão do dono do produto: quando a liberação falha depois da
/// autorização, o ingresso continua consumido, a tentativa guarda a causa, e o operador estorna pelo
/// painel, com nome e motivo.
/// </summary>
public sealed class EstornoDeUsoTests : IDisposable
{
    private const string Qr = "ZET-ESTORNO-1";
    private static readonly DateTimeOffset Leitura = new(2026, 11, 14, 21, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repositorio;
    private readonly EstornosDeUso _estornos;

    public EstornoDeUsoTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "raw", "rest-zet"), Leitura.AddDays(-1));
        Assert.Equal(1, _repositorio.Ingerir([new IngressoRecebido("zet", "Z1", Qr, Qr, Categoria: "inteira")], Leitura.AddDays(-1)).Inseridos);
        _estornos = new EstornosDeUso(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    /// <summary>Lê o QR pelo decisor, como o worker, e a liberação falha: o laço anota a causa e desiste.</summary>
    private void LeituraComLiberacaoQueFalha()
    {
        var decisor = new DecisorDeIngresso(_repositorio, relogio: new RelogioFixo(Leitura));
        var evento = DeviceEvent.Create(
            new DeviceEventKey("catraca-01", "boot", 1), EventOrigin.From(KnownEventOrigin.QrCode), Leitura, "teste", rawCardData: Qr);

        Assert.Equal(DecisionOutcome.Allowed, decisor.Decidir(evento).Outcome);

        decisor.RegistrarCausaSemGiro("catraca-01", "a catraca não aceitou a liberação (erro de comunicação)");
        decisor.DescartarPendente("catraca-01");
        Assert.Equal(0, decisor.ConfirmacoesAGravar);
    }

    [Fact]
    public void Liberacao_que_falha_fica_consumida_com_a_causa_e_o_estorno_devolve_o_ingresso()
    {
        LeituraComLiberacaoQueFalha();

        // Consumido: lido de novo, nega como já usado (a decisão não muda sozinha).
        Assert.Equal(MotivoDoUso.UsosEsgotados, _repositorio.TentarUsar(Qr, "portao-1", "catraca-01", Leitura.AddMinutes(1)).Resultado.Motivo);

        // Na lista do painel, depois da espera pelo giro, com a causa.
        var depois = Leitura + EstornosDeUso.EsperaAntesDoEstorno;
        var uso = Assert.Single(_estornos.Listar(depois, somenteComFalha: true));
        Assert.Equal("catraca-01", uso.Catraca);
        Assert.Equal("a catraca não aceitou a liberação (erro de comunicação)", uso.FalhaDaLiberacao);
        Assert.DoesNotContain(Qr, uso.Codigo, StringComparison.Ordinal);
        Assert.Equal(1, new Operacao(_banco.Fabrica).Resumir(depois).Liberados);

        var (estornado, problemas) = _estornos.Estornar(uso.Tentativa, "Ana", "pessoa não passou, conferido no portão", depois);

        Assert.True(estornado, string.Join(" ", problemas));
        Assert.Empty(_estornos.Listar(depois));
        Assert.Equal(0, new Operacao(_banco.Fabrica).Resumir(depois).Liberados);

        // O ingresso volta a valer: a pessoa passa.
        Assert.True(_repositorio.TentarUsar(Qr, "portao-1", "catraca-01", depois.AddMinutes(1)).Resultado.Liberou);

        // O estorno fica na auditoria, com quem e por quê, e não se apaga.
        using var conexao = _banco.Fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText = "SELECT refunded_by, reason FROM ticket_use_refund;";
        using (var leitor = sql.ExecuteReader())
        {
            Assert.True(leitor.Read());
            Assert.Equal("Ana", leitor.GetString(0));
            Assert.Equal("pessoa não passou, conferido no portão", leitor.GetString(1));
        }

        sql.CommandText = "DELETE FROM ticket_use_refund;";
        Assert.Throws<SqliteException>(() => sql.ExecuteNonQuery());
    }

    [Fact]
    public void Estorno_recusa_sem_nome_ou_motivo_cedo_demais_repetido_ou_com_giro()
    {
        LeituraComLiberacaoQueFalha();
        var tentativa = Assert.Single(_estornos.Listar(Leitura + EstornosDeUso.EsperaAntesDoEstorno)).Tentativa;

        Assert.False(_estornos.Estornar(tentativa, " ", "motivo", Leitura.AddHours(1)).Estornado);
        Assert.False(_estornos.Estornar(tentativa, "Ana", "", Leitura.AddHours(1)).Estornado);

        var cedo = _estornos.Estornar(tentativa, "Ana", "não passou", Leitura.AddSeconds(30));
        Assert.False(cedo.Estornado);
        Assert.Contains("o giro ainda pode chegar", Assert.Single(cedo.Problemas), StringComparison.Ordinal);

        Assert.True(_estornos.Estornar(tentativa, "Ana", "não passou", Leitura.AddHours(1)).Estornado);
        Assert.Contains("já foi estornado", Assert.Single(_estornos.Estornar(tentativa, "Ana", "de novo", Leitura.AddHours(1)).Problemas), StringComparison.Ordinal);
    }

    [Fact]
    public void Uso_com_giro_confirmado_nao_se_estorna()
    {
        var (resultado, tentativa) = _repositorio.TentarUsar(Qr, "portao-1", "catraca-01", Leitura);
        Assert.True(resultado.Liberou);
        _repositorio.ConfirmarPassagemFisica(tentativa, Leitura.AddSeconds(2));

        Assert.Empty(_estornos.Listar(Leitura.AddHours(1)));
        var (estornado, problemas) = _estornos.Estornar(tentativa, "Ana", "engano", Leitura.AddHours(1));
        Assert.False(estornado);
        Assert.Contains("a pessoa passou", Assert.Single(problemas), StringComparison.Ordinal);
    }

    [Fact]
    public void Liberou_e_nao_girou_tambem_aparece_sem_causa()
    {
        Assert.True(_repositorio.TentarUsar(Qr, "portao-1", "catraca-01", Leitura).Resultado.Liberou);

        var uso = Assert.Single(_estornos.Listar(Leitura.AddHours(1)));
        Assert.Null(uso.FalhaDaLiberacao);
        Assert.Empty(_estornos.Listar(Leitura.AddHours(1), somenteComFalha: true));
    }
}
