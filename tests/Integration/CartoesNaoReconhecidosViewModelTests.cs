using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests;

/// <summary>
/// A tela de cartões não reconhecidos (migração 026) falando com o serviço de verdade, pelo mesmo canal
/// que o painel usa. Cobre abrir, ler, fechar e cadastrar a partir da lista, sem mostrar o código.
/// </summary>
public sealed class CartoesNaoReconhecidosViewModelTests : IAsyncLifetime, IDisposable
{
    private const string Balcao = "balcao-local";
    private const string Codigo = "5C6D7E8F";
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private RepositorioDeIngressos _repo = null!;
    private WebApplication? _servidor;
    private string _endereco = null!;
    private string _token = null!;

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _repo = new RepositorioDeIngressos(_banco.Fabrica);
        _repo.RegistrarProvedor(new ProvedorDeIngresso(Balcao, "Bilheteria local", "raw", "", Reutilizavel: true), Agora);

        var servico = new EdgeControlService(new WorkerSupervisor([]), relogio: () => Agora, semConfiguracao: true, cartoes: _repo);
        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"cartoes-{Guid.NewGuid():N}");

        var construtor = WebApplication.CreateBuilder();
        construtor.WebHost.ConfigureKestrel(o => TransporteLocal.Escutar(o, _endereco));
        construtor.Services.AddSingleton(servico);
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

    private CartoesNaoReconhecidosViewModel Tela() =>
        new(new EdgeControl.EdgeControlClient(TransporteLocal.CriarCanal(_endereco, _token)), () => Agora);

    [Fact]
    public async Task Abrir_sessao_depois_ler_na_urna_mostra_o_lote_sem_listar_o_cartao_como_recusado()
    {
        var tela = Tela();
        tela.Provedor = Balcao;
        tela.Tipo = "INTEIRA";
        tela.Lote = "Evento 14/11";
        tela.Usos = "1";

        await tela.AbrirSessaoAsync();
        Assert.True(tela.SessaoAberta);
        Assert.StartsWith("Sessão aberta", tela.Mensagem, StringComparison.Ordinal);

        _repo.TentarUsar(Codigo, "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor2);
        await tela.AtualizarAsync();

        Assert.Empty(tela.Linhas);
        Assert.Contains("1 cadastrado(s)", tela.SessaoResumo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leitura_recusada_aparece_mascarada_e_cadastra_pela_selecao()
    {
        _repo.TentarUsar(Codigo, "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor1);
        var tela = Tela();
        tela.Provedor = Balcao;
        tela.Tipo = "MEIA";
        tela.Lote = "Meia 2";
        tela.Usos = "1";

        await tela.AtualizarAsync();
        var linha = Assert.Single(tela.Linhas);
        Assert.DoesNotContain(Codigo, linha.Codigo, StringComparison.Ordinal);
        Assert.Equal("1", linha.Vezes);

        tela.Selecionada = linha;
        await tela.CadastrarSelecionadoAsync();

        Assert.StartsWith("Cartão cadastrado no lote", tela.Mensagem, StringComparison.Ordinal);
        Assert.Empty(tela.Linhas);
    }

    [Fact]
    public async Task Cadastrar_sem_selecionar_avisa_e_nao_chama_o_servico()
    {
        var tela = Tela();

        await tela.CadastrarSelecionadoAsync();

        Assert.Equal("Escolha uma leitura da lista para cadastrar.", tela.Mensagem);
    }

    [Fact]
    public async Task Fechar_sem_sessao_avisa_e_fechar_com_sessao_devolve_a_contagem()
    {
        var tela = Tela();
        tela.Provedor = Balcao;
        tela.Tipo = "INTEIRA";
        tela.Lote = "Evento 14/11";
        tela.Usos = "1";

        await tela.FecharSessaoAsync();
        Assert.Equal("Não havia sessão de lote aberta.", tela.Mensagem);

        await tela.AbrirSessaoAsync();
        _repo.TentarUsar(Codigo, "portao-1", "catraca-01", Agora, leitor: KnownEventOrigin.Leitor2);
        await tela.FecharSessaoAsync();

        Assert.False(tela.SessaoAberta);
        Assert.Contains("1 cartão(ões) entraram no lote", tela.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tipo_invalido_nao_abre_sessao_e_diz_o_que_corrigir()
    {
        var tela = Tela();
        tela.Provedor = Balcao;
        tela.Tipo = "inteira";
        tela.Lote = "Evento";
        tela.Usos = "1";

        await tela.AbrirSessaoAsync();

        Assert.False(tela.SessaoAberta);
        Assert.StartsWith("Confira o tipo", tela.Mensagem, StringComparison.Ordinal);
    }
}
