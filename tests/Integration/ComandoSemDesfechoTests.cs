using Access.Application.Devices;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Achado E3-09 do docs/41: um comando "recebido" cujo worker morreu antes de concluir ficava
/// "recebido" para sempre. Numa liberação manual, a auditoria não dizia se o braço liberou.
/// </summary>
public sealed class ComandoSemDesfechoTests : IDisposable
{
    private static readonly DateTimeOffset Pedido = new(2026, 10, 9, 20, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly FilaDeComandosSqlite _fila;

    public ComandoSemDesfechoTests()
    {
        _banco.Migrar();
        _fila = new FilaDeComandosSqlite(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private ComandoDeCatraca Liberacao()
    {
        var (comando, problemas) = ComandoDeCatraca.Criar(1, TipoDeComando.LiberacaoManual, "Ana", Pedido, motivo: "cadeirante");
        Assert.Empty(problemas);
        _fila.Pedir(comando!);
        return comando!;
    }

    [Fact]
    public void Recebido_sem_desfecho_vira_falhou_com_desfecho_desconhecido_depois_do_prazo()
    {
        var comando = Liberacao();
        Assert.True(_fila.Receber(comando.Id, Pedido.AddSeconds(1)));

        // Antes do prazo, o worker ainda pode concluir: nada muda.
        Assert.Equal(0, _fila.ExpirarVencidos(Pedido.AddSeconds(1) + FilaDeComandosSqlite.PrazoDoDesfecho - TimeSpan.FromSeconds(1)));
        Assert.Equal(SituacaoDoComando.Recebido, Assert.Single(_fila.Listar(1)).Situacao);

        Assert.Equal(1, _fila.ExpirarVencidos(Pedido.AddSeconds(1) + FilaDeComandosSqlite.PrazoDoDesfecho));

        var registro = Assert.Single(_fila.Listar(1));
        Assert.Equal(SituacaoDoComando.Falhou, registro.Situacao);
        Assert.Equal(FilaDeComandosSqlite.DesfechoDesconhecido, registro.Resultado);
        Assert.NotNull(registro.ConcluidoEm);
    }

    [Fact]
    public void Desfecho_real_gravado_antes_do_prazo_nao_e_trocado()
    {
        var comando = Liberacao();
        _fila.Receber(comando.Id, Pedido.AddSeconds(1));
        _fila.Concluir(comando.Id, SituacaoDoComando.Concluido, "braço liberado", Pedido.AddSeconds(2));

        Assert.Equal(0, _fila.ExpirarVencidos(Pedido.AddHours(1)));
        Assert.Equal("braço liberado", Assert.Single(_fila.Listar(1)).Resultado);
    }

    [Fact]
    public void Pedido_que_ninguem_pegou_continua_expirando_como_antes()
    {
        var comando = Liberacao();

        Assert.Equal(1, _fila.ExpirarVencidos(comando.ExpiraEm));
        Assert.Equal(SituacaoDoComando.Expirado, Assert.Single(_fila.Listar(1)).Situacao);
    }
}
