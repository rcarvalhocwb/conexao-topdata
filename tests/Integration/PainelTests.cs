using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Integration.Tests;

/// <summary>
/// O painel contra o serviço de verdade, pelo IPC de verdade.
/// </summary>
/// <remarks>
/// A tela é WPF e só existe no Windows, mas o comportamento — o que o operador lê e o
/// que acontece quando o serviço cai — mora nas ViewModels e é verificado aqui, em
/// qualquer plataforma.
/// </remarks>
public sealed class PainelTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 24, 19, 0, 0, TimeSpan.Zero);

    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

    private sealed class WorkerDeTeste(string nome, int porta, params int[] inners) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel { get; set; } = true;

        public string Diagnostico => "saudável";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    public async Task InitializeAsync()
    {
        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"painel-{Guid.NewGuid():N}");

        var supervisor = new WorkerSupervisor([new WorkerDeTeste("setor-A", 3570, 1, 2, 3)]);
        supervisor.Iniciar();

        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(supervisor);
        construtor.Services.AddSingleton<EdgeControlService>();
        construtor.Services.AddGrpc(o => o.Interceptors.Add<InterceptadorDeToken>(_token));

        _servidor = construtor.Build();
        _servidor.MapGrpcService<EdgeControlService>();
        await _servidor.StartAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        if (_servidor is not null)
        {
            await _servidor.StopAsync().ConfigureAwait(true);
            await _servidor.DisposeAsync().ConfigureAwait(true);
        }

        if (!TransporteLocal.UsaNamedPipe && File.Exists(_endereco))
        {
            File.Delete(_endereco);
        }
    }

    private PainelViewModel Painel(string? token = null, string? endereco = null)
    {
        var canal = TransporteLocal.CriarCanal(endereco ?? _endereco, token ?? _token);
        return new PainelViewModel(new EdgeControl.EdgeControlClient(canal), () => Agora);
    }

    [Fact]
    public async Task Mostra_o_estado_em_portugues_e_lista_os_equipamentos()
    {
        var painel = Painel();

        await painel.AtualizarAsync().ConfigureAwait(true);

        Assert.Equal(SaudeDoPainel.Normal, painel.Estado.Saude);
        Assert.Equal("Operando sem internet — nenhuma ação necessária", painel.Estado.Mensagem);
        Assert.Equal(3, painel.Equipamentos.Count);
        Assert.Equal(0, painel.FalhasSeguidas);
    }

    /// <summary>
    /// Sem internet é o regime normal de um evento. Anunciá-lo como falha treinaria o
    /// operador a ignorar alerta — e aí o alerta que importa também passa batido.
    /// </summary>
    [Fact]
    public async Task Operar_sem_internet_nao_e_apresentado_como_problema()
    {
        var painel = Painel();

        await painel.AtualizarAsync().ConfigureAwait(true);

        Assert.NotEqual(SaudeDoPainel.Acao, painel.Estado.Saude);
        Assert.DoesNotContain("erro", painel.Estado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("falha", painel.Estado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Uma exceção não tratada fecharia o aplicativo justamente quando o operador mais
    /// precisa dele. Falha vira estado visível.
    /// </summary>
    [Fact]
    public async Task Servico_indisponivel_vira_estado_visivel_e_nao_excecao()
    {
        var painel = Painel(endereco: TransporteLocal.EnderecoPadrao($"inexistente-{Guid.NewGuid():N}"));

        await painel.AtualizarAsync().ConfigureAwait(true);

        Assert.Equal(SaudeDoPainel.Acao, painel.Estado.Saude);
        Assert.Contains("serviço local", painel.Estado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, painel.FalhasSeguidas);
    }

    [Fact]
    public async Task Token_invalido_tambem_vira_estado_visivel()
    {
        var painel = Painel(token: InterceptadorDeToken.GerarToken());

        await painel.AtualizarAsync().ConfigureAwait(true);

        Assert.Equal(SaudeDoPainel.Acao, painel.Estado.Saude);
        Assert.Contains("Unauthenticated", painel.Estado.Detalhe ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Quando o serviço cai, o painel continua mostrando o que sabia — e diz de quando
    /// é. Tela em branco esconderia do operador que algo caiu.
    /// </summary>
    [Fact]
    public async Task Apos_a_queda_mantem_os_dados_anteriores_e_informa_a_idade()
    {
        var painel = Painel();
        await painel.AtualizarAsync().ConfigureAwait(true);

        var equipamentosAntes = painel.Equipamentos.Count;

        await _servidor!.StopAsync().ConfigureAwait(true);
        await painel.AtualizarAsync().ConfigureAwait(true);

        Assert.True(painel.Estado.Desatualizado);
        Assert.Equal(equipamentosAntes, painel.Equipamentos.Count);
        Assert.Contains("mostrando dados de", painel.Estado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Estado_inicial_nao_finge_que_esta_tudo_bem()
    {
        var inicial = EstadoDoPainel.Carregando();

        Assert.Equal(SaudeDoPainel.Carregando, inicial.Saude);
        Assert.Null(inicial.AtualizadoEm);
    }

    [Fact]
    public void Nenhuma_catraca_conectada_exige_acao()
    {
        var resposta = new ObterEstadoResponse
        {
            Versao = "teste",
            Nivel = NivelDeDegradacao.T1SemInternet,
            EquipamentosConectados = 0,
        };

        var estado = EstadoDoPainel.De(resposta, Agora);

        Assert.Equal(SaudeDoPainel.Acao, estado.Saude);
        Assert.Contains("Nenhuma catraca conectada", estado.Mensagem, StringComparison.Ordinal);
    }

    /// <summary>O detalhe técnico existe, mas separado da frase que o operador lê.</summary>
    [Fact]
    public void Mensagem_ao_operador_nao_carrega_jargao()
    {
        var resposta = new ObterEstadoResponse
        {
            Versao = "1.0",
            Nivel = NivelDeDegradacao.T2ListaLocal,
            EquipamentosConectados = 5,
            OutboxPendente = 42,
        };

        var estado = EstadoDoPainel.De(resposta, Agora);

        Assert.DoesNotContain("T2", estado.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("outbox", estado.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outbox", estado.Detalhe ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
