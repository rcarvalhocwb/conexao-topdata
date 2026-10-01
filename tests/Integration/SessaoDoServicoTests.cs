using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>
/// Defeito relatado pelo dono do produto (01/10, docs/29): "desliguei o simulador e o sistema
/// ainda reconhecia como atendendo, sendo que não tinha catracas na rede". Workers simulados de
/// uma partida anterior do serviço ficaram órfãos e continuaram gravando situação fresca na
/// base; o serviço novo, em modo real, acreditou neles. Daqui em diante o serviço só acredita
/// na situação da partida dele (migração 016).
/// </summary>
public sealed class SessaoDoServicoTests : IAsyncLifetime, IDisposable
{
    private const string PartidaAtual = "0199a1b2c3d47000800000000000000a";
    private const string PartidaAnterior = "0199a1b2c3d47000800000000000000b";

    private readonly BancoTemporario _banco = new();
    private readonly Operacao _operacao;
    private readonly EdgeControlService _servico;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    private sealed class WorkerFalso(string nome, int porta, params int[] inners) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    public SessaoDoServicoTests()
    {
        _banco.Migrar();
        _operacao = new Operacao(_banco.Fabrica);

        // Três catracas, como na captura do dono: o serviço desta partida está em modo REAL.
        var supervisor = new WorkerSupervisor([new WorkerFalso("catracas", 3570, 1, 2, 3)]);
        supervisor.Iniciar();
        _servico = new EdgeControlService(supervisor, operacao: _operacao, sessao: PartidaAtual);
    }

    public async Task InitializeAsync()
    {
        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"sessao-{Guid.NewGuid():N}");

        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(_servico);
        construtor.Services.AddGrpc(o => o.Interceptors.Add<InterceptadorDeToken>(_token));

        _servidor = construtor.Build();
        _servidor.MapGrpcService<EdgeControlService>();
        await _servidor.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_servidor is not null)
        {
            await _servidor.StopAsync();
            await _servidor.DisposeAsync();
        }

        if (!TransporteLocal.UsaNamedPipe && File.Exists(_endereco))
        {
            File.Delete(_endereco);
        }
    }

    public void Dispose() => _banco.Dispose();

    /// <summary>O que um worker grava: "Polling", em operação, firmware do simulador, agora.</summary>
    private void Gravar(int inner, string? partida, bool? simulada, DateTimeOffset? em = null)
    {
        var agora = em ?? DateTimeOffset.UtcNow;
        _operacao.GravarSituacao(
        [
            new SituacaoDoEquipamento(
                $"inner-{inner}", inner, "catracas", "Polling", true, "4.2.0", 0, agora, "liberado", agora,
                RelogioConferidoEm: agora.AddMinutes(-40), Sessao: partida, Simulacao: simulada),
        ]);
    }

    private async Task<Equipamento> Catraca(int inner) =>
        (await _servico.ListarEquipamentos(new ListarEquipamentosRequest(), null!)).Equipamentos.Single(e => e.Inner == inner);

    /// <summary>A captura do dono: situação fresca de um worker simulado órfão, serviço em modo real.</summary>
    [Fact]
    public async Task Situacao_fresca_de_outra_partida_nunca_aparece_como_atendendo()
    {
        Gravar(1, PartidaAnterior, simulada: true);
        Gravar(2, PartidaAnterior, simulada: true);
        Gravar(3, PartidaAnterior, simulada: true);

        foreach (var inner in new[] { 1, 2, 3 })
        {
            var catraca = await Catraca(inner);

            Assert.False(catraca.EmOperacao);
            Assert.False(catraca.Saudavel);
            Assert.False(catraca.Simulacao);

            // Nada da partida anterior chega ao painel: nem o firmware do simulador, nem o relógio.
            Assert.Equal(string.Empty, catraca.Firmware);
            Assert.Null(catraca.RelogioConferidoEm);

            // Sem catraca na rede: "Aguardando a catraca conectar", nunca "Atendendo".
            Assert.Equal(("Aguardando a catraca conectar", Sinal.Neutro), Textos.SituacaoDaCatraca(catraca));
        }

        var estado = await _servico.ObterEstado(new ObterEstadoRequest(), null!);
        Assert.Equal(0, estado.EquipamentosConectados);
        Assert.Equal(3, estado.EquipamentosCadastrados);
    }

    /// <summary>
    /// Linha gravada antes da migração 016, ou por quem não foi subido pelo serviço (bancada,
    /// ferramenta): sem partida, o serviço não acredita nela. É também o worker órfão da versão
    /// anterior, que não sabe gravar a partida.
    /// </summary>
    [Fact]
    public async Task Situacao_sem_partida_nao_conta_para_o_servico_que_tem_partida()
    {
        Gravar(1, partida: null, simulada: null);

        var catraca = await Catraca(1);
        Assert.False(catraca.EmOperacao);
        Assert.Equal("aguardando a catraca", catraca.Estado);
    }

    [Fact]
    public async Task Situacao_desta_partida_conta_e_diz_se_a_catraca_e_simulada()
    {
        Gravar(1, PartidaAtual, simulada: true);
        Gravar(2, PartidaAtual, simulada: false);

        var simulada = await Catraca(1);
        Assert.True(simulada.EmOperacao);
        Assert.True(simulada.Simulacao);
        Assert.Equal("4.2.0", simulada.Firmware);
        Assert.Equal(("Atendendo", Sinal.Bom), Textos.SituacaoDaCatraca(simulada));

        var real = await Catraca(2);
        Assert.True(real.EmOperacao);
        Assert.False(real.Simulacao);

        Assert.Equal(2, (await _servico.ObterEstado(new ObterEstadoRequest(), null!)).EquipamentosConectados);
    }

    /// <summary>O selo "Simulação" chega ao cartão da catraca, pelo IPC de verdade.</summary>
    [Fact]
    public async Task O_cartao_da_catraca_simulada_leva_o_selo_e_o_da_outra_partida_nao_atende()
    {
        Gravar(1, PartidaAtual, simulada: true);
        Gravar(2, PartidaAnterior, simulada: true);

        var tela = new CatracasViewModel(new EdgeControl.EdgeControlClient(TransporteLocal.CriarCanal(_endereco, _token)));
        await tela.AtualizarAsync();

        var um = tela.Catracas.Single(c => c.Inner == 1);
        Assert.True(um.Simulacao);
        Assert.Equal("Atendendo", um.Situacao);

        var dois = tela.Catracas.Single(c => c.Inner == 2);
        Assert.False(dois.Simulacao);
        Assert.Equal("Aguardando a catraca conectar", dois.Situacao);
        Assert.Equal("—", dois.Firmware);
    }

    /// <summary>Velha continua velha: a partida certa não salva notícia de 15 s atrás.</summary>
    [Fact]
    public async Task Situacao_desta_partida_mas_velha_continua_sem_noticia()
    {
        Gravar(1, PartidaAtual, simulada: false, DateTimeOffset.UtcNow - EdgeControlService.NoticiaVelha - TimeSpan.FromSeconds(5));

        var catraca = await Catraca(1);
        Assert.False(catraca.EmOperacao);
        Assert.StartsWith("sem notícia do worker", catraca.Estado, StringComparison.Ordinal);
    }

    /// <summary>Catraca que saiu da configuração: a linha dela fica na base e não conta como conectada.</summary>
    [Fact]
    public async Task Catraca_fora_da_configuracao_nao_conta_como_conectada()
    {
        Gravar(9, PartidaAtual, simulada: false);

        var estado = await _servico.ObterEstado(new ObterEstadoRequest(), null!);
        Assert.Equal(0, estado.EquipamentosConectados);
    }

    /// <summary>Migração 016: partida e simulação vão e voltam da base; valor fora da regra é recusado.</summary>
    [Fact]
    public void A_base_guarda_a_partida_e_a_simulacao_e_recusa_valor_invalido()
    {
        Gravar(1, PartidaAtual, simulada: true);
        Gravar(2, partida: null, simulada: null);

        var lidas = _operacao.ListarSituacao();
        Assert.Equal((PartidaAtual, (bool?)true), (lidas.Single(s => s.Inner == 1).Sessao, lidas.Single(s => s.Inner == 1).Simulacao));
        Assert.Equal((null, (bool?)null), (lidas.Single(s => s.Inner == 2).Sessao, lidas.Single(s => s.Inner == 2).Simulacao));

        // Quem regrava sem partida (outro worker) apaga a partida: a linha nunca fica com a
        // partida de um e a situação de outro.
        Gravar(1, partida: null, simulada: false);
        Assert.Null(_operacao.ListarSituacao().Single(s => s.Inner == 1).Sessao);

        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "UPDATE device_status SET simulated = 2 WHERE device_id = 'inner-1';";
        Assert.Throws<SqliteException>(() => comando.ExecuteNonQuery());

        comando.CommandText = $"UPDATE device_status SET session_id = '{new string('a', 65)}' WHERE device_id = 'inner-1';";
        Assert.Throws<SqliteException>(() => comando.ExecuteNonQuery());
    }
}
