using Access.Application.Ingressos;
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
            pastaDeDados: "dados",
            comandos: new FilaDeComandosSqlite(_banco.Fabrica));

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

    /// <summary>
    /// A origem da leitura vai da catraca ao painel: gravada com a tentativa (migração 010)
    /// e entregue pelo AcompanharEventos de verdade. Docs/35, Etapa 0.3.
    /// </summary>
    [Fact]
    public async Task A_origem_da_leitura_chega_ao_acompanhar_eventos()
    {
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var fluxo = Cliente().AcompanharEventos(new AcompanharEventosRequest(), cancellationToken: cancelamento.Token);
        SpinWait.SpinUntil(() => _servico.Eventos.Assinantes > 0, TimeSpan.FromSeconds(10));

        var acompanhamento = new AcompanhamentoDaOperacao(new Operacao(_banco.Fabrica), _servico.Eventos, TimeSpan.FromSeconds(1));
        acompanhamento.ComecarDoFim();
        var decisor = new DecisorDeIngresso(_repositorio);
        var sequencia = 0L;

        DeviceEvent Leitura(int origem, string codigo) =>
            DeviceEvent.Create(
                new DeviceEventKey("inner-1", "boot", ++sequencia),
                EventOrigin.FromRaw(origem),
                DateTimeOffset.UtcNow,
                "corr",
                rawCardData: codigo);

        // Como era antes da migração: quem grava não informa a origem.
        _repositorio.TentarUsar("9999000101", "p1", "inner-1", DateTimeOffset.UtcNow);

        // Pela fenda da urna (leitor 2, origem 3).
        Assert.True(decisor.Decidir(Leitura(3, Qr)).ShouldRelease);

        // Origem que não consta da tabela oficial: chega com o número, marcada desconhecida.
        Assert.False(decisor.Decidir(Leitura(11, "9999000102")).ShouldRelease);

        Assert.Equal(3, acompanhamento.UmaLeitura());

        var recebidos = new List<EventoDeAcesso>();
        while (recebidos.Count < 3 && await fluxo.ResponseStream.MoveNext(cancelamento.Token))
        {
            recebidos.Add(fluxo.ResponseStream.Current);
        }

        var antiga = recebidos[0];
        Assert.Equal((0, string.Empty, false), (antiga.OrigemBruta, antiga.OrigemConhecida, antiga.OrigemDesconhecida));

        var urna = recebidos[1];
        Assert.Equal(ResultadoDoAcesso.Permitido, urna.Resultado);
        Assert.Equal((3, "Leitor2", false), (urna.OrigemBruta, urna.OrigemConhecida, urna.OrigemDesconhecida));

        var desconhecida = recebidos[2];
        Assert.Equal((11, string.Empty, true), (desconhecida.OrigemBruta, desconhecida.OrigemConhecida, desconhecida.OrigemDesconhecida));
        Assert.DoesNotContain("9999000102", desconhecida.CredencialMascarada, StringComparison.Ordinal);
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

    /// <summary>
    /// "Até hoje" sem hora inclui o dia de hoje inteiro. Antes, virava hoje 00:00 e o acesso
    /// de agora ficava de fora ("Nenhum acesso com esses filtros").
    /// </summary>
    [Fact]
    public async Task Acessos_ate_hoje_sem_hora_inclui_o_dia_inteiro_no_fuso_do_evento()
    {
        var agora = DateTimeOffset.UtcNow;
        _repositorio.TentarUsar(Qr, "p1", "inner-1", agora);
        var hoje = FusoDoEvento.NoEvento(agora).Date;

        var acessos = new AcessosViewModel(Cliente(), () => agora) { Desde = hoje, Ate = hoje };
        await acessos.Buscar.ExecutarAsync();
        Assert.Single(acessos.Linhas);

        // Com hora final antes do acesso, ele sai; com a hora do acesso (inclusive), volta.
        var horaDoAcesso = FusoDoEvento.NoEvento(agora);
        if (horaDoAcesso.Hour > 0 || horaDoAcesso.Minute > 0)
        {
            acessos.HoraAte = horaDoAcesso.AddMinutes(-1).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            await acessos.Buscar.ExecutarAsync();
            Assert.Empty(acessos.Linhas);
        }

        acessos.HoraAte = horaDoAcesso.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        await acessos.Buscar.ExecutarAsync();
        Assert.Single(acessos.Linhas);

        acessos.HoraAte = "25:00";
        await acessos.Buscar.ExecutarAsync();
        Assert.Equal("Hora no formato hh:mm, de 00:00 a 23:59 — ou deixe vazio.", acessos.Mensagem);
    }

    [Fact]
    public async Task Prestacao_de_contas_de_hoje_ate_hoje_conta_o_dia_inteiro()
    {
        var agora = DateTimeOffset.UtcNow;
        _repositorio.TentarUsar(Qr, "p1", "inner-1", agora);
        var hoje = FusoDoEvento.NoEvento(agora).Date;

        var contas = new ContasViewModel(Cliente(), () => agora) { Ate = hoje };
        Assert.Equal(hoje, contas.Desde);
        await contas.Gerar.ExecutarAsync();
        Assert.Equal(1, contas.Contas!.Liberados);

        contas.Ate = hoje.AddDays(-1);
        await contas.Gerar.ExecutarAsync();
        Assert.Equal("O fim do período é antes do começo.", contas.Mensagem);
    }

    [Fact]
    public async Task Barra_operacional_resume_servico_catracas_e_nuvem()
    {
        var painel = new PainelAoVivoViewModel(Cliente());
        await painel.AtualizarAsync();

        Assert.Equal(("Operacional", Sinal.Bom), (painel.ServicoResumo, painel.ServicoSinal));
        Assert.Equal(("1/2 online", Sinal.Atencao), (painel.CatracasResumo, painel.CatracasSinal));
        Assert.Equal(("Sem sincronização", Sinal.Neutro), (painel.NuvemResumo, painel.NuvemSinal));

        // Sem serviço: o bloco do serviço diz na hora, sem esperar outra tela.
        var semServico = new PainelAoVivoViewModel(Cliente(TransporteLocal.EnderecoPadrao($"inexistente-{Guid.NewGuid():N}")));
        await semServico.AtualizarAsync();
        Assert.Equal(("Sem resposta", Sinal.Problema), (semServico.ServicoResumo, semServico.ServicoSinal));
    }

    [Fact]
    public async Task Ver_acessos_do_cartao_abre_os_acessos_daquela_catraca()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);
        _repositorio.TentarUsar("5555555555", "p1", "inner-2", DateTimeOffset.UtcNow);

        var janela = new JanelaViewModel(Cliente());
        await janela.VerAcessos.ExecutarAsync(1);

        var acessos = Assert.IsType<AcessosViewModel>(janela.TelaAtual);
        Assert.Equal("1", acessos.Catraca);
        await acessos.AtualizarAsync();
        Assert.Equal(1, Assert.Single(acessos.Linhas).Inner);
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
        Assert.Contains(new ParDeTexto("Situação", "Já utilizado"), consulta.Detalhes);
        Assert.Contains(new ParDeTexto("Categoria", "meia"), consulta.Detalhes);
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
        Assert.Equal(11, janela.Telas.Count);
        Assert.Same(janela.Painel, janela.TelaAtual);

        janela.TelaAtual = janela.Telas.OfType<SincronizacaoViewModel>().Single();
        await janela.AtualizarAsync();

        Assert.Equal(1, janela.Painel.Catracas.Count(c => c.Sinal == Sinal.Bom));
        var sincronizacao = (SincronizacaoViewModel)janela.TelaAtual;
        Assert.Contains(new ParDeTexto("Nuvem", "Não configurada nesta instalação"), sincronizacao.Situacao);
        Assert.Equal(2, Assert.Single(sincronizacao.Provedores).Codigos);
    }

    /// <summary>
    /// Gêmeo digital contra o serviço de verdade: lê catracas e configuração, e no modo ao
    /// vivo o desenho segue só os eventos da catraca escolhida. Demonstração e ao vivo não
    /// se misturam: cenário não roda ao vivo.
    /// </summary>
    [Fact]
    public async Task Gemeo_digital_le_o_servico_e_ao_vivo_segue_so_a_catraca_escolhida()
    {
        var instante = TimeSpan.Zero;
        var tela = new GemeoDigitalViewModel(Cliente(), relogioDaCena: () => instante);

        await tela.AtualizarAsync();

        Assert.Equal(string.Empty, tela.Mensagem);
        Assert.Equal(1, tela.Catraca);
        Assert.Contains(tela.ResumoDaConfiguracao, p => p.Rotulo == "Mensagem padrão");
        Assert.StartsWith("DEMONSTRAÇÃO", tela.SeloDoModo, StringComparison.Ordinal);

        tela.ModoAoVivo = true;
        await tela.AtualizarAsync();
        Assert.StartsWith("AO VIVO · CATRACA 01", tela.SeloDoModo, StringComparison.Ordinal);
        Assert.Equal(Desktop.ViewModels.GemeoDigital.EstadoDaCena.Livre, tela.EstadoAtual);

        // Evento de outra catraca: nada muda.
        tela.AplicarEventoAoVivo(new EventoDeAcesso { Inner = 2, Resultado = ResultadoDoAcesso.Negado });
        Assert.Equal(Desktop.ViewModels.GemeoDigital.EstadoDaCena.Livre, tela.EstadoAtual);

        // Desta catraca: a leitura aparece, e a negação vem depois dela.
        tela.AplicarEventoAoVivo(new EventoDeAcesso { Inner = 1, Resultado = ResultadoDoAcesso.Negado, MensagemAoOperador = "Negado · já utilizado" });
        instante = TimeSpan.FromSeconds(2);
        tela.Quadro();
        Assert.Equal(Desktop.ViewModels.GemeoDigital.EstadoDaCena.Negada, tela.EstadoAtual);
        Assert.Contains("já utilizado", tela.UltimoEventoAoVivo, StringComparison.Ordinal);

        // Cenário não roda ao vivo.
        await tela.RodarRoteiro.ExecutarAsync(Desktop.ViewModels.GemeoDigital.Roteiros.QrValido);
        Assert.Null(tela.RoteiroEmAndamento);
    }

    /// <summary>
    /// Gerenciar catraca: a liberação manual só fica disponível com nome e motivo, o pedido
    /// vira linha no histórico, e o motivo não fica preenchido para a próxima.
    /// </summary>
    [Fact]
    public async Task Gerenciar_catraca_so_libera_com_nome_e_motivo_e_mostra_o_pedido_no_historico()
    {
        var tela = new GerenciarCatracaViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();

        Assert.Equal(1, tela.Catraca);
        Assert.Equal(("Atendendo", Sinal.Bom), (tela.Selecionada!.Situacao, tela.Selecionada.Sinal));
        Assert.False(tela.LiberarManualmente.CanExecute(null));

        tela.Operador = "Ana";
        tela.Motivo = "ok";
        Assert.False(tela.LiberarManualmente.CanExecute(null));

        tela.Motivo = "Criança de colo sem ingresso";
        Assert.True(tela.LiberarManualmente.CanExecute(null));
        await tela.LiberarManualmente.ExecutarAsync();

        var linha = Assert.Single(tela.Historico);
        Assert.Equal(("Liberação manual", "Criança de colo sem ingresso", "Ana"), (linha.Comando, linha.Detalhe, linha.Operador));
        Assert.Equal(("Aguardando a catraca", Sinal.Neutro), (linha.Situacao, linha.Sinal));
        Assert.Equal(string.Empty, tela.Motivo);
        Assert.StartsWith("Liberação manual: pedido à catraca 1.", tela.Mensagem, StringComparison.Ordinal);

        // O que não existe não se finge: aparece como aguardando confirmação.
        Assert.Contains(tela.AguardandoConfirmacao, p => p.Rotulo == "Recolher cartão na urna");
    }

    [Fact]
    public async Task Gerenciar_no_cartao_da_catraca_abre_a_tela_naquela_catraca()
    {
        var janela = new JanelaViewModel(Cliente());

        await janela.Gerenciar.ExecutarAsync(2);

        var tela = Assert.IsType<GerenciarCatracaViewModel>(janela.TelaAtual);
        Assert.Equal(2, tela.Catraca);
    }

    [Fact]
    public async Task Aplicar_agora_pede_a_todas_as_catracas_e_exige_o_nome()
    {
        var tela = new ConfiguracoesViewModel(Cliente());

        await tela.AplicarAgora.ExecutarAsync();
        Assert.Equal("Não foi pedido. Corrija os itens abaixo.", tela.Mensagem);
        Assert.Contains("Informe o nome de quem está pedindo (2 a 80 caracteres).", tela.Problemas);

        tela.Operador = "Ana";
        await tela.AplicarAgora.ExecutarAsync();
        Assert.StartsWith("Pedido a 2 catraca(s).", tela.Mensagem, StringComparison.Ordinal);
    }

    /// <summary>
    /// O ícone perto do relógio avisa só quando algo muda: a catraca que para, o serviço que
    /// some e o serviço que volta. Catraca parada há horas não gera aviso a cada 2 s.
    /// </summary>
    [Fact]
    public async Task Bandeja_avisa_so_quando_uma_catraca_para_ou_o_servico_some_e_volta()
    {
        var resumo = new ResumoDaBandeja();
        var painel = new PainelAoVivoViewModel(Cliente());
        await painel.AtualizarAsync();

        Assert.Null(resumo.Observar(painel));
        Assert.StartsWith("Rayzer XAcess · catracas 1/2", ResumoDaBandeja.Dica(painel), StringComparison.Ordinal);
        Assert.True(ResumoDaBandeja.Dica(painel).Length <= ResumoDaBandeja.LimiteDaDica);

        // A catraca 1 cai.
        new Operacao(_banco.Fabrica).GravarSituacao(
        [
            new SituacaoDoEquipamento("inner-1", 1, "setor-a", "Reconectar", false, "4.2.0", 3, null, null, DateTimeOffset.UtcNow),
        ]);
        await painel.AtualizarAsync();

        var aviso = resumo.Observar(painel);
        Assert.NotNull(aviso);
        Assert.Equal("Catraca parou de atender", aviso.Titulo);
        Assert.True(aviso.Problema);

        // Continua caída: sem aviso repetido.
        await painel.AtualizarAsync();
        Assert.Null(resumo.Observar(painel));

        // O serviço some, e depois volta.
        var semServico = new PainelAoVivoViewModel(Cliente(TransporteLocal.EnderecoPadrao($"inexistente-{Guid.NewGuid():N}")));
        await semServico.AtualizarAsync();
        Assert.Equal("Serviço local sem resposta", resumo.Observar(semServico)!.Titulo);
        Assert.StartsWith("Rayzer XAcess · serviço local: Sem resposta", ResumoDaBandeja.Dica(semServico), StringComparison.Ordinal);
        Assert.Null(resumo.Observar(semServico));

        var volta = resumo.Observar(painel);
        Assert.Equal(("Serviço local de volta", false), (volta!.Titulo, volta.Problema));
    }

    [Fact]
    public void Resultado_de_parar_ou_iniciar_a_operacao_diz_o_que_aconteceu()
    {
        Assert.StartsWith("Operação encerrada", ResumoDaBandeja.ResultadoDoControle(parar: true, 0), StringComparison.Ordinal);
        Assert.StartsWith("Operação iniciada", ResumoDaBandeja.ResultadoDoControle(parar: false, 0), StringComparison.Ordinal);
        Assert.StartsWith("Nada mudou", ResumoDaBandeja.ResultadoDoControle(parar: true, null), StringComparison.Ordinal);
        Assert.Contains("não está instalado", ResumoDaBandeja.ResultadoDoControle(parar: false, 2), StringComparison.Ordinal);
        Assert.StartsWith("Não foi possível encerrar", ResumoDaBandeja.ResultadoDoControle(parar: true, 1), StringComparison.Ordinal);
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
