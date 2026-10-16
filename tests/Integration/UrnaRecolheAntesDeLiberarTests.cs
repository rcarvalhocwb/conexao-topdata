using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Edge.Worker;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Bancada de 15/10/2026 (docs/61): o cartão do evento na fenda da urna liberava o giro na hora e a pessoa
/// passava levando o cartão. Agora segue o manual do SDK (seção 5.3, fluxo 8) e o exemplo oficial em Java:
/// aprovado na fenda → "Deposite o cartao" → urna aberta (relé 2) → origem 7 → só então o giro. Laço,
/// decisor e base de verdade; simulador no lugar da DLL. Dados sintéticos.
/// </summary>
public sealed class UrnaRecolheAntesDeLiberarTests : IDisposable
{
    private const string Cartao = "0000123456";
    private static readonly DateTimeOffset Inicio = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repositorio;
    private readonly InnerSimulator _sim;
    private readonly DecisorDeIngresso _decisor;
    private readonly DevicePump _bomba;
    private readonly DeviceSlot _catraca;
    private DateTimeOffset _agora = Inicio;

    public UrnaRecolheAntesDeLiberarTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(
            new ProvedorDeIngresso("evento", "Cartões do evento", "raw", "rest-evento", SomenteNaUrna: true), Inicio.AddDays(-1));
        Assert.Equal(1, _repositorio.Ingerir([new IngressoRecebido("evento", "C1", Cartao, Cartao, Categoria: "inteira")], Inicio.AddDays(-1)).Inseridos);

        _sim = new InnerSimulator(() => _agora);
        _sim.AbrirPorta(3570);
        _decisor = new DecisorDeIngresso(_repositorio, relogio: new Relogio(() => _agora));
        _bomba = new DevicePump(
            _sim,
            () => _agora,
            decidir: _decisor.Decidir,
            aoReceberEvento: _decisor.AoReceberEvento,
            aoDesistirDoGiro: _decisor.DescartarPendente,
            aoDesistirDaUrna: _decisor.DevolverNaUrna);
        _catraca = new DeviceSlot(1, PadroesDeFabrica.TopFit4, () => _agora);

        for (var i = 0; i < 30 && Dispositivo.AcertosDeRelogio == 0; i++)
        {
            _bomba.Passo(_catraca, TimeSpan.Zero);
        }

        Assert.Equal(DeviceState.Polling, _catraca.Maquina.Current);
        Assert.True(_catraca.Configuracao.RecolheNaUrna);
    }

    public void Dispose()
    {
        _sim.Dispose();
        _banco.Dispose();
    }

    private sealed class Relogio(Func<DateTimeOffset> agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora();
    }

    private SimulatedDevice Dispositivo => _sim.Dispositivo(1);

    private int Liberacoes => Dispositivo.LiberacoesPedidas.Values.Sum();

    private void Ler(KnownEventOrigin origem) =>
        Dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.From(origem), Cartao));

    private List<string> Passos(int quantos)
    {
        var feitos = new List<string>();
        for (var i = 0; i < quantos; i++)
        {
            feitos.Add(_bomba.Passo(_catraca, TimeSpan.Zero));
        }

        return feitos;
    }

    private List<string> Ate(DeviceState alvo, int limite = 20)
    {
        var feitos = new List<string>();
        for (var i = 0; i < limite && _catraca.Maquina.Current != alvo; i++)
        {
            feitos.Add(_bomba.Passo(_catraca, TimeSpan.Zero));
        }

        Assert.Equal(alvo, _catraca.Maquina.Current);
        return feitos;
    }

    private (string Desfecho, bool Girou) Tentativa()
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var sql = conexao.CreateCommand();
        sql.CommandText = "SELECT outcome, passage_confirmed_at IS NOT NULL FROM ticket_use_attempt ORDER BY at, rowid LIMIT 1;";
        using var leitor = sql.ExecuteReader();
        Assert.True(leitor.Read());
        return (leitor.GetString(0), leitor.GetInt32(1) == 1);
    }

    private long Contar(string sql)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return (long)comando.ExecuteScalar()!;
    }

    [Fact]
    public void Cartao_na_fenda_abre_a_urna_e_so_libera_o_giro_depois_que_o_cartao_cai()
    {
        Ler(KnownEventOrigin.Leitor2);

        Ate(DeviceState.AcionarUrna);
        Assert.Equal(0, Liberacoes);

        var abrindo = Ate(DeviceState.MonitoraUrna);
        Assert.Contains(Dispositivo.MensagensTemporarias, m => m.Texto == MensagensDaCatraca.DepositeOCartao);
        Assert.Equal(1, Dispositivo.AcionamentosDaUrna);
        Assert.Equal(0, Liberacoes);
        Assert.Contains(abrindo, p => p.Contains("urna aberta", StringComparison.Ordinal));

        // O simulador deixa o cartão cair (origem 7): só agora o braço é liberado.
        Ate(DeviceState.MonitoraGiroCatraca);
        Assert.Equal(1, Liberacoes);

        Dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Ate(DeviceState.Polling);

        Assert.Equal(("consumido", true), Tentativa());
        Assert.Equal(0, _catraca.UrnasSemRecolhimento);
    }

    [Fact]
    public void Cartao_que_nao_cai_nao_libera_o_giro_e_o_uso_volta()
    {
        Dispositivo.CartaoCaiNaUrna = false;
        Ler(KnownEventOrigin.Leitor2);

        Ate(DeviceState.MonitoraUrna);
        Ate(DeviceState.EnviarMsgAcessoNegado);
        Ate(DeviceState.Polling);

        Assert.Equal(0, Liberacoes);
        Assert.Contains(Dispositivo.MensagensTemporarias, m => m.Texto == MensagensDaCatraca.CartaoNaoRecolhido);
        Assert.Equal(1, _catraca.UrnasSemRecolhimento);
        Assert.Equal(1, _decisor.UsosDevolvidosNaUrna);

        // A tentativa fica estornada, pelo sistema, e o aviso de uso não sai para o provedor.
        Assert.Equal(("estornado", false), Tentativa());
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM ticket_use_refund WHERE refunded_by = 'sistema';"));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM outbox WHERE aggregate_type = 'ticket_use' AND sent_at IS NULL;"));

        // O cartão continua com a pessoa: na segunda tentativa, cai e ela passa.
        Dispositivo.CartaoCaiNaUrna = true;
        _agora = _agora.AddSeconds(10);
        Ler(KnownEventOrigin.Leitor2);
        Ate(DeviceState.MonitoraGiroCatraca);
        Assert.Equal(1, Liberacoes);
    }

    [Fact]
    public void Sem_resposta_da_urna_o_prazo_nega_e_devolve_o_uso()
    {
        Dispositivo.UrnaMuda = true;
        Ler(KnownEventOrigin.Leitor2);
        Ate(DeviceState.MonitoraUrna);
        Passos(3);
        Assert.Equal(DeviceState.MonitoraUrna, _catraca.Maquina.Current);

        _agora += DeviceStateMachine.PrazoEfetivo(DeviceState.MonitoraUrna, _catraca.Configuracao)!.Value;
        Ate(DeviceState.Polling);

        Assert.Equal(0, Liberacoes);
        Assert.Equal(("estornado", false), Tentativa());
    }

    [Fact]
    public void Urna_cheia_nega_avisa_e_devolve_o_uso()
    {
        Dispositivo.UrnaCheia = true;
        Ler(KnownEventOrigin.Leitor2);

        Ate(DeviceState.MonitoraUrna);
        Ate(DeviceState.Polling);

        Assert.Equal(0, Liberacoes);
        Assert.True(_catraca.UrnaCheia);
        Assert.Contains(Dispositivo.MensagensTemporarias, m => m.Texto == MensagensDaCatraca.UrnaCheia);
        Assert.Equal(("estornado", false), Tentativa());
    }

    [Fact]
    public void Cartao_do_evento_no_leitor_da_frente_e_negado_com_use_a_urna()
    {
        Ler(KnownEventOrigin.Leitor1);
        Ate(DeviceState.EnviarMsgAcessoNegado);
        Ate(DeviceState.Polling);

        Assert.Equal(0, Liberacoes);
        Assert.Equal(0, Dispositivo.AcionamentosDaUrna);
        Assert.Contains(Dispositivo.MensagensTemporarias, m => m.Texto == "Use a urna");
    }

    [Fact]
    public void Com_o_rele_da_urna_desligado_volta_o_comportamento_antigo()
    {
        // Chave técnica catraca.funcao_do_rele_da_urna = 0: o montador tira o relé 2 e a fenda volta a liberar na hora.
        var semRecolher = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4, new SobreposicoesDoEvento { FuncaoDoReleDaUrna = 0 }, SobreposicoesDaCatraca.Nenhuma);
        Assert.False(semRecolher.RecolheNaUrna);
        var catraca = new DeviceSlot(2, semRecolher, () => _agora);
        var dispositivo = _sim.Dispositivo(2);
        for (var i = 0; i < 30 && dispositivo.AcertosDeRelogio == 0; i++)
        {
            _bomba.Passo(catraca, TimeSpan.Zero);
        }

        dispositivo.Roteirizar(new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor2), Cartao));
        for (var i = 0; i < 20 && catraca.Maquina.Current != DeviceState.MonitoraGiroCatraca; i++)
        {
            _bomba.Passo(catraca, TimeSpan.Zero);
        }

        Assert.Equal(DeviceState.MonitoraGiroCatraca, catraca.Maquina.Current);
        Assert.Equal(0, dispositivo.AcionamentosDaUrna);
        Assert.Equal(1, dispositivo.LiberacoesPedidas.Values.Sum());
    }
}
