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
/// A lógica de cada tela contra o serviço de verdade, pelo IPC de verdade, com a base
/// local de verdade. Só a janela WPF fica de fora — ela não roda sem Windows com tela.
/// </summary>
public sealed class TelasTests : IAsyncLifetime, IDisposable
{
    private const string Qr = "1000000001";

    private readonly BancoTemporario _banco = new();
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;
    private RepositorioDeIngressos _repositorio = null!;
    private EdgeControlService _servico = null!;

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

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), DateTimeOffset.UtcNow);
        _repositorio.Ingerir(
            [
                new IngressoRecebido("zet", "T1", Qr, Qr, Categoria: "meia"),
                new IngressoRecebido("zet", "T2", "1000000002", "1000000002", Categoria: "=HYPERLINK(\"x\")"),
            ],
            DateTimeOffset.UtcNow.AddHours(-1));

        var operacao = new Operacao(_banco.Fabrica);
        operacao.GravarSituacao(
        [
            new SituacaoDoEquipamento("inner-1", 1, "setor-a", "Polling", true, "4.2.0", 0, null, null, DateTimeOffset.UtcNow),
        ]);

        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1, 2)]);
        supervisor.Iniciar();

        _servico = new EdgeControlService(
            supervisor,
            operacao: operacao,
            nuvem: new EstadoDaNuvem(),
            consultas: new ConsultasDaOperacao(_banco.Fabrica),
            configuracoes: new ConfiguracoesDaBorda(_banco.Fabrica),
            pastaDeDados: "dados");

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"telas-{Guid.NewGuid():N}");

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

        _banco.Dispose();
    }

    public void Dispose() => _banco.Dispose();

    private EdgeControl.EdgeControlClient Cliente(string? endereco = null) =>
        new(TransporteLocal.CriarCanal(endereco ?? _endereco, _token));

    [Fact]
    public async Task Painel_ao_vivo_conta_e_traduz_a_situacao_das_catracas()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);
        _repositorio.TentarUsar("5555555555", "p1", "inner-1", DateTimeOffset.UtcNow);

        var painel = new PainelAoVivoViewModel(Cliente());
        await painel.AtualizarAsync();

        Assert.Equal(string.Empty, painel.Mensagem);
        Assert.Equal(1, painel.Liberados);
        Assert.Equal(1, painel.Negados);
        Assert.Equal("1 de 2 catraca(s) atendendo", painel.Resumo);
        Assert.Equal(("Atendendo", Sinal.Bom), (painel.Catracas[0].Situacao, painel.Catracas[0].Sinal));
        Assert.Equal("Aguardando a catraca conectar", painel.Catracas[1].Situacao);
        Assert.StartsWith("Nuvem: sem sincronização", painel.Internet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acesso_gravado_aparece_ao_vivo_no_painel()
    {
        var painel = new PainelAoVivoViewModel(Cliente());
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var chegou = new TaskCompletionSource();

        var acompanhando = painel.AcompanharAsync(
            acao =>
            {
                acao();
                chegou.TrySetResult();
            },
            cancelamento.Token);

        // Aguarda o painel estar ouvindo antes de publicar.
        SpinWait.SpinUntil(() => _servico.Eventos.Assinantes > 0, TimeSpan.FromSeconds(10));

        var acompanhamento = new AcompanhamentoDaOperacao(new Operacao(_banco.Fabrica), _servico.Eventos, TimeSpan.FromSeconds(1));
        acompanhamento.ComecarDoFim();
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);
        acompanhamento.UmaLeitura();

        await chegou.Task.WaitAsync(cancelamento.Token);
        cancelamento.Cancel();
        await acompanhando;

        var linha = Assert.Single(painel.UltimosAcessos);
        Assert.True(linha.Liberado);
        Assert.Equal("Liberado · meia", linha.Mensagem);
        Assert.DoesNotContain(Qr, linha.Codigo, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lista_ao_vivo_nao_cresce_sem_limite()
    {
        var painel = new PainelAoVivoViewModel(Cliente());

        for (var i = 0; i < PainelAoVivoViewModel.AcessosNaTela + 20; i++)
        {
            painel.Acrescentar(new LinhaDeAcesso(i.ToString(System.Globalization.CultureInfo.InvariantCulture), 1, "x", true, false, "", "", Sinal.Bom));
        }

        Assert.Equal(PainelAoVivoViewModel.AcessosNaTela, painel.UltimosAcessos.Count);
        Assert.Equal("119", painel.UltimosAcessos[0].Hora);
    }

    [Fact]
    public async Task Acessos_filtra_e_recusa_catraca_que_nao_e_numero()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);
        _repositorio.TentarUsar("5555555555", "p1", "inner-2", DateTimeOffset.UtcNow);

        var acessos = new AcessosViewModel(Cliente()) { Resultado = 2 };
        await acessos.Buscar.ExecutarAsync();
        Assert.Equal("Negado · código não cadastrado", Assert.Single(acessos.Linhas).Mensagem);

        acessos.Resultado = 0;
        acessos.Catraca = "1";
        await acessos.Buscar.ExecutarAsync();
        Assert.Equal(1, Assert.Single(acessos.Linhas).Inner);

        acessos.Catraca = "catraca um";
        await acessos.Buscar.ExecutarAsync();
        Assert.Equal("O número da catraca precisa ser um número inteiro.", acessos.Mensagem);
    }

    [Fact]
    public async Task Consulta_apaga_o_codigo_digitado_e_mostra_so_a_mascara()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);

        var consulta = new ConsultaViewModel(Cliente()) { Codigo = Qr };
        Assert.True(consulta.Consultar.CanExecute(null));

        await consulta.Consultar.ExecutarAsync();

        Assert.Equal(string.Empty, consulta.Codigo);
        Assert.True(consulta.Encontrado);
        Assert.Contains(("Situação", "Já utilizado"), consulta.Detalhes);
        Assert.Contains(("Categoria", "meia"), consulta.Detalhes);
        Assert.DoesNotContain(consulta.Detalhes, d => d.Valor.Contains(Qr, StringComparison.Ordinal));
        Assert.Single(consulta.Historico);

        consulta.Codigo = "0000000000";
        await consulta.Consultar.ExecutarAsync();
        Assert.False(consulta.Encontrado);
        Assert.Equal("Código não cadastrado. A catraca negaria este código.", consulta.Mensagem);

        Assert.False(consulta.Consultar.CanExecute(null));
    }

    [Fact]
    public async Task Configuracoes_carrega_valida_e_grava()
    {
        var configuracoes = new ConfiguracoesViewModel(Cliente()) { Operador = "ana" };
        await configuracoes.AtualizarAsync();
        Assert.Equal(8, configuracoes.TipoDeLeitor);

        configuracoes.TempoDeAcionamento = 99;
        await configuracoes.Salvar.ExecutarAsync();
        Assert.Equal("Não foi gravado. Corrija os itens abaixo.", configuracoes.Mensagem);
        Assert.NotEmpty(configuracoes.Problemas);

        configuracoes.TempoDeAcionamento = 6;
        configuracoes.MensagemPadrao = "Bem-vindo";
        await configuracoes.Salvar.ExecutarAsync();
        Assert.StartsWith("Gravado.", configuracoes.Mensagem, StringComparison.Ordinal);

        var releitura = new ConfiguracoesViewModel(Cliente());
        await releitura.AtualizarAsync();
        Assert.Equal(("Bem-vindo", 6), (releitura.MensagemPadrao, releitura.TempoDeAcionamento));
    }

    [Fact]
    public async Task Prestacao_de_contas_gera_csv_para_excel_sem_formula_injetada()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);
        _repositorio.TentarUsar("1000000002", "p1", "inner-1", DateTimeOffset.UtcNow);

        var contas = new ContasViewModel(Cliente());
        await contas.Gerar.ExecutarAsync();

        Assert.NotNull(contas.Contas);
        Assert.Equal(2, contas.Contas.Liberados);

        var csv = contas.ParaCsv();
        Assert.Contains("Liberados;2", csv, StringComparison.Ordinal);
        Assert.Contains("meia;1;0", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\n=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);

        var arquivo = Path.Combine(Path.GetTempPath(), $"contas-{Guid.NewGuid():N}.csv");
        try
        {
            await contas.ExportarAsync(arquivo);
            var bytes = await File.ReadAllBytesAsync(arquivo);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        }
        finally
        {
            File.Delete(arquivo);
        }
    }

    [Fact]
    public async Task Janela_troca_de_tela_e_o_cabecalho_continua_atualizando()
    {
        var janela = new JanelaViewModel(Cliente());
        Assert.Equal(8, janela.Telas.Count);
        Assert.Same(janela.Painel, janela.TelaAtual);

        janela.TelaAtual = janela.Telas.OfType<SincronizacaoViewModel>().Single();
        await janela.AtualizarAsync();

        Assert.Equal(1, janela.Painel.Catracas.Count(c => c.Sinal == Sinal.Bom));
        var sincronizacao = (SincronizacaoViewModel)janela.TelaAtual;
        Assert.Contains(("Nuvem", "Não configurada nesta instalação"), sincronizacao.Situacao);
        Assert.Equal(2, Assert.Single(sincronizacao.Provedores).Codigos);
    }

    [Fact]
    public async Task Servico_fora_do_ar_vira_mensagem_e_nunca_excecao()
    {
        var semServico = Cliente(TransporteLocal.EnderecoPadrao($"inexistente-{Guid.NewGuid():N}"));
        var janela = new JanelaViewModel(semServico);

        foreach (var tela in janela.Telas)
        {
            await tela.AtualizarAsync();
        }

        await janela.AtualizarAsync();

        Assert.StartsWith("Sem resposta do serviço local", janela.Painel.Mensagem, StringComparison.Ordinal);
        Assert.Equal(SaudeDoPainel.Acao, janela.Painel.Estado.Saude);
    }
}
