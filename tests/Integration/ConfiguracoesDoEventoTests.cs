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
/// As seções das configurações do evento (docs/57): o evento e o período na decisão da catraca, os
/// tipos de entrada, as regras dos cartões da bilheteria e o dia de operação da Prestação de contas.
/// Pelo serviço de verdade, pelo IPC de verdade, com a base local de verdade. Números fictícios.
/// </summary>
public sealed class ConfiguracoesDoEventoTests : IAsyncLifetime, IDisposable
{
    private const string Cartao = "900000000101";

    // 03:00 de 20/10/2026 em Brasília.
    private static readonly DateTimeOffset Agora = new(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly EstadoDaNuvem _nuvem = new();
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;
    private RepositorioDeIngressos _repositorio = null!;
    private ConfiguracoesDaBorda _configuracoes = null!;

    private sealed class WorkerFalso : IWorkerHost
    {
        public string Nome => "setor-a";

        public int Porta => 3570;

        public IReadOnlyList<int> Inners { get; } = [1];

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _configuracoes = new ConfiguracoesDaBorda(_banco.Fabrica);
        CargaInicialDeCartoes.GarantirProvedorLocal(_banco.Fabrica, Agora);
        _repositorio.Ingerir(
            [new IngressoRecebido(CargaInicialDeCartoes.ProvedorLocal, Cartao, Cartao, Cartao, UsosMaximos: int.MaxValue, Categoria: "INTEIRA")],
            Agora.AddDays(-1));
        _repositorio.GarantirTiposDeEntrada(["INTEIRA", "MEIA"], "Teste", Agora);

        var supervisor = new WorkerSupervisor([new WorkerFalso()]);
        supervisor.Iniciar();

        var servico = new EdgeControlService(
            supervisor,
            relogio: () => Agora,
            nuvem: _nuvem,
            configuracoes: _configuracoes,
            pastaDeDados: "dados",
            cartoes: _repositorio);

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"evento-{Guid.NewGuid():N}");

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

        _banco.Dispose();
    }

    public void Dispose() => _banco.Dispose();

    private EdgeControl.EdgeControlClient Cliente() => new(TransporteLocal.CriarCanal(_endereco, _token));

    private ConfiguracoesViewModel Tela() => new(Cliente(), () => Agora);

    private (ResultadoDoUso Resultado, Guid Tentativa) Ler(string codigo, DateTimeOffset quando) =>
        _repositorio.TentarUsar(codigo, "portao-1", "inner-1", quando, leitor: KnownEventOrigin.Leitor2);

    private void GravarPeriodo(bool recusar) =>
        Assert.Empty(_configuracoes.GravarEvento(
            new EventoConfigurado("Festa Fictícia", Agora.AddHours(2), Agora.AddHours(10), TimeSpan.FromHours(6), recusar),
            Agora, "Teste"));

    // ---------- Evento na decisão da catraca ----------

    [Fact]
    public void Com_a_recusa_ligada_o_ingresso_antes_do_inicio_e_negado_sem_consumir()
    {
        GravarPeriodo(recusar: true);

        var (resultado, _) = Ler(Cartao, Agora);

        Assert.Equal(MotivoDoUso.ForaDoPeriodoDoEvento, resultado.Motivo);
        Assert.False(resultado.Liberou);
        Assert.Equal("INTEIRA", resultado.Categoria);

        // Dentro do período, o mesmo cartão passa: nada foi consumido antes.
        Assert.True(Ler(Cartao, Agora.AddHours(3)).Resultado.Liberou);

        // Depois do fim, negado de novo.
        Assert.Equal(MotivoDoUso.ForaDoPeriodoDoEvento, Ler(Cartao, Agora.AddHours(11)).Resultado.Motivo);
    }

    [Fact]
    public void Com_a_recusa_desligada_o_periodo_nao_muda_a_decisao()
    {
        GravarPeriodo(recusar: false);

        Assert.True(Ler(Cartao, Agora).Resultado.Liberou);
    }

    [Fact]
    public void Codigo_desconhecido_fora_do_periodo_continua_desconhecido()
    {
        GravarPeriodo(recusar: true);

        Assert.Equal(MotivoDoUso.Desconhecido, Ler("900000000999", Agora).Resultado.Motivo);
    }

    [Fact]
    public void Evento_invalido_nao_grava_e_diz_por_que()
    {
        var fimAntes = _configuracoes.GravarEvento(new EventoConfigurado("X", Agora, Agora.AddHours(-1)), Agora, "Teste");
        var recusaSemDatas = _configuracoes.GravarEvento(new EventoConfigurado("X", RecusarForaDoPeriodo: true), Agora, "Teste");

        Assert.Contains(fimAntes, p => p.Contains("depois do início", StringComparison.Ordinal));
        Assert.Contains(recusaSemDatas, p => p.Contains("informe o início ou o fim", StringComparison.Ordinal));
        Assert.Equal(string.Empty, _configuracoes.LerEvento().Evento.Nome);
    }

    // ---------- A tela, pelo serviço ----------

    [Fact]
    public async Task Tela_grava_e_le_o_evento_no_horario_de_brasilia()
    {
        var tela = Tela();
        await tela.AtualizarAsync();
        Assert.Contains("ainda não foi definido", tela.EventoAlterado, StringComparison.Ordinal);

        tela.NomeDoEvento = "Festa Fictícia";
        tela.InicioDoEvento = new DateTime(2026, 10, 20);
        tela.HoraDeInicio = "18:00";
        tela.FimDoEvento = new DateTime(2026, 10, 21);
        tela.HoraDeFim = "04:00";
        tela.HoraDeCorte = "06:00";
        tela.RecusarForaDoPeriodo = true;
        await tela.SalvarEvento.ExecutarAsync();

        Assert.Empty(tela.ProblemasDoEvento);
        var (evento, por, _) = _configuracoes.LerEvento();
        Assert.Equal("Festa Fictícia", evento.Nome);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.Zero), evento.Inicio);
        Assert.Equal(TimeSpan.FromHours(6), evento.Corte);
        Assert.True(evento.RecusarForaDoPeriodo);
        Assert.Equal("painel", por);

        var outra = Tela();
        await outra.AtualizarAsync();
        Assert.Equal(("18:00", "04:00", "06:00"), (outra.HoraDeInicio, outra.HoraDeFim, outra.HoraDeCorte));
        Assert.Contains("antes do início", outra.SituacaoDoPeriodo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tela_cria_renomeia_e_desativa_um_tipo_de_entrada()
    {
        var tela = Tela();
        await tela.AtualizarAsync();
        Assert.Equal(["INTEIRA", "MEIA"], tela.TiposDeEntrada.Select(t => t.Codigo));
        Assert.Equal(1, tela.TiposDeEntrada[0].EmUso);

        await tela.NovoTipo.ExecutarAsync();
        Assert.True(tela.TipoNovo);
        tela.CodigoDoTipo = "cortesia";
        tela.NomeDoTipo = "Cortesia";
        await tela.SalvarTipo.ExecutarAsync();
        Assert.Empty(tela.ProblemasDoTipo);
        Assert.Contains(tela.TiposDeEntrada, t => t.Codigo == "CORTESIA");
        Assert.False(tela.TipoNovo);

        tela.TipoSelecionado = tela.TiposDeEntrada.First(t => t.Codigo == "INTEIRA");
        tela.TipoAtivo = false;
        await tela.SalvarTipo.ExecutarAsync();

        Assert.False(tela.TiposDeEntrada.First(t => t.Codigo == "INTEIRA").Ativo);
        Assert.Equal(MotivoDoUso.TipoInativo, Ler(Cartao, Agora).Resultado.Motivo);
    }

    [Fact]
    public async Task Tipo_com_codigo_invalido_nao_grava()
    {
        var tela = Tela();
        await tela.AtualizarAsync();
        await tela.NovoTipo.ExecutarAsync();
        tela.CodigoDoTipo = "X";
        tela.NomeDoTipo = "Curto demais";
        await tela.SalvarTipo.ExecutarAsync();

        Assert.Contains(tela.ProblemasDoTipo, p => p.Contains("2 a 20", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tela_ajusta_as_regras_dos_cartoes_da_bilheteria_sem_nuvem()
    {
        var tela = Tela();
        await tela.AtualizarAsync();
        Assert.True(tela.PodeMudarBilheteria);
        Assert.Equal("4", tela.IntervaloDeReuso);
        Assert.Contains("1 cartão(ões) na base", tela.ResumoDaBilheteria, StringComparison.Ordinal);

        tela.IntervaloDeReuso = "2,5";
        tela.SomenteNaUrna = false;
        await tela.SalvarBilheteria.ExecutarAsync();

        Assert.Empty(tela.ProblemasDaBilheteria);
        var provedor = _repositorio.Provedores().Single(p => p.Id == CargaInicialDeCartoes.ProvedorLocal);
        Assert.Equal((TimeSpan.FromSeconds(150), false), (provedor.IntervaloDeReuso, provedor.SomenteNaUrna));
    }

    [Fact]
    public async Task Com_a_nuvem_ligada_as_regras_da_bilheteria_nao_mudam_pelo_painel()
    {
        _nuvem.Configurada = true;
        var tela = Tela();
        await tela.AtualizarAsync();

        Assert.False(tela.PodeMudarBilheteria);
        Assert.Contains("vêm dela", tela.ResumoDaBilheteria, StringComparison.Ordinal);

        var r = await Cliente().GravarBilheteriaLocalAsync(new GravarBilheteriaLocalRequest { IntervaloDeReusoSegundos = 60 });
        Assert.False(r.Gravada);
        Assert.Equal(TimeSpan.FromSeconds(240), _repositorio.Provedores().Single(p => p.Id == CargaInicialDeCartoes.ProvedorLocal).IntervaloDeReuso);
    }

    [Fact]
    public async Task Avancado_mostra_as_chaves_tecnicas_desligadas_com_o_ensaio()
    {
        var tela = Tela();
        await tela.AtualizarAsync();

        Assert.Equal(11, tela.ChavesTecnicas.Count);
        Assert.All(tela.ChavesTecnicas, c => Assert.False(c.Ligada));
        Assert.All(tela.ChavesTecnicas, c => Assert.False(string.IsNullOrWhiteSpace(c.LiberadaPor)));
        Assert.NotEmpty(ConfiguracoesViewModel.SecoesBloqueadas);
    }

    [Fact]
    public async Task Tipo_de_leitor_aparece_com_o_nome_e_grava_o_numero()
    {
        var tela = Tela();
        await tela.AtualizarAsync();
        Assert.Equal("8 · leitor de QR Code", tela.TipoDeLeitorEscolhido?.Nome);

        tela.TipoDeLeitorEscolhido = ConfiguracoesViewModel.TiposDeLeitor.Single(t => t.Valor == 5);
        Assert.Equal(5, tela.TipoDeLeitor);
    }

    [Fact]
    public async Task Prestacao_de_contas_abre_no_dia_de_operacao_pela_hora_de_corte()
    {
        GravarPeriodo(recusar: false);
        var contas = new ContasViewModel(Cliente(), () => Agora);

        await contas.AtualizarAsync();

        // 03:00 de 20/10 com corte às 06:00: o dia de operação começou em 19/10 às 06:00.
        Assert.Equal(new DateTime(2026, 10, 19), contas.Desde);
        Assert.Equal("06:00", contas.HoraDesde);
        Assert.Equal(new DateTime(2026, 10, 20), contas.Ate);
        Assert.Equal("05:59", contas.HoraAte);
    }
}
