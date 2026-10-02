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
            comandos: new FilaDeComandosSqlite(_banco.Fabrica),
            configuracoesDasCatracas: new ConfiguracoesDasCatracas(_banco.Fabrica),
            configuracaoPorCatraca: new ConfiguracaoPorCatraca(_banco.Fabrica),
            mapasDeGiro: new MapasDeGiro(_banco.Fabrica));

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

        // "Aplicar agora" leva a mudança sem reiniciar o serviço (docs/32, docs/35 0.8).
        Assert.Contains("Aplicar agora nas catracas", configuracoes.Mensagem, StringComparison.Ordinal);
        Assert.Contains("sem reiniciar o serviço", configuracoes.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("espera pelo giro", configuracoes.Mensagem, StringComparison.Ordinal);

        // Só a espera pelo giro fica para o próximo início do serviço.
        configuracoes.EsperaPeloGiro = 15;
        await configuracoes.Salvar.ExecutarAsync();
        Assert.EndsWith("A espera pelo giro só muda quando o serviço reiniciar.", configuracoes.Mensagem, StringComparison.Ordinal);

        await configuracoes.Salvar.ExecutarAsync();
        Assert.Equal("Nada mudou.", configuracoes.Mensagem);

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
        Assert.Contains("Entradas (giros pelo mapa de giro);0", csv, StringComparison.Ordinal);
        Assert.Contains("Catraca;Liberados;Com giro;Negados;Entradas;Saídas", csv, StringComparison.Ordinal);
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

    // ===== Parametrização da catraca (Etapa A.6 do docs/35) =====

    private static CampoDaParametrizacao CampoDe(ParametrizacaoViewModel tela, Contracts.Edge.V1.CampoDaCatraca campo) =>
        tela.Campos.Single(c => c.Campo == campo);

    /// <summary>Salvar e aplicar exigem o nome digitado; aplicar exige salvar antes e confirmar.</summary>
    [Fact]
    public async Task Parametrizacao_so_salva_e_aplica_com_nome_digitado_e_confirmacao()
    {
        var tela = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();

        Assert.Equal(1, tela.Catraca);
        Assert.Equal(8, tela.Campos.Count);
        Assert.Equal(string.Empty, tela.Mensagem);

        var tempo = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1);
        tempo.Herda = false;
        tempo.Valor = "7";
        Assert.False(tela.Salvar.CanExecute(null));
        tela.Operador = "A";
        Assert.False(tela.Salvar.CanExecute(null));
        tela.Operador = "Ana";
        Assert.True(tela.Salvar.CanExecute(null));

        // O que vai para a catraca é o salvo: com alteração pendente, não aplica.
        Assert.False(tela.PedirAplicacao.CanExecute(null));
        Assert.StartsWith("Salve as alterações", tela.MotivoParaNaoAplicar, StringComparison.Ordinal);

        await tela.Salvar.ExecutarAsync();
        Assert.Empty(tela.Problemas);
        Assert.Empty(tela.Mudancas);
        Assert.Equal("7", CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1).ValorSalvo);
        Assert.StartsWith("Salvo para a catraca 1.", tela.Mensagem, StringComparison.Ordinal);

        tela.Operador = " ";
        Assert.False(tela.PedirAplicacao.CanExecute(null));
        Assert.Equal("Informe o seu nome.", tela.MotivoParaNaoAplicar);

        tela.Operador = "Ana";
        Assert.True(tela.PedirAplicacao.CanExecute(null));
        Assert.False(tela.ConfirmarAplicacao.CanExecute(null));
        await tela.PedirAplicacao.ExecutarAsync();
        Assert.True(tela.ConfirmandoAplicacao);
        Assert.Contains("fica alguns segundos sem atender", tela.TextoDaConfirmacao, StringComparison.Ordinal);

        await tela.ConfirmarAplicacao.ExecutarAsync();
        Assert.False(tela.ConfirmandoAplicacao);
        var pedido = Assert.Single(tela.Aplicacoes);
        Assert.Equal(("Aplicar configuração", "Ana", 1), (pedido.Comando, pedido.Operador, pedido.Inner));
        Assert.Equal(("Aplicando: aguardando a catraca", Sinal.Atencao), (tela.SituacaoNaCatraca, tela.SinalDaSituacao));
        Assert.False(tela.PedirAplicacao.CanExecute(null));
    }

    /// <summary>
    /// "Aplicada" só depois de o pedido terminar Concluido E de a versão que a catraca aceitou
    /// bater com a do salvo; nunca antes, nem com as versões iguais e o pedido ainda na fila.
    /// O worker é feito à mão aqui: a fila de comandos e a situação publicada na base.
    /// </summary>
    [Fact]
    public async Task Parametrizacao_diz_aplicada_so_depois_de_concluido_com_a_versao_igual()
    {
        var fila = new FilaDeComandosSqlite(_banco.Fabrica);
        var operacao = new Operacao(_banco.Fabrica);
        void Publicar(string? versao) => operacao.GravarSituacao(
        [
            new SituacaoDoEquipamento(
                "inner-1", 1, "setor-a", "Polling", true, "4.2.0", 0, null, null, DateTimeOffset.UtcNow,
                ConfiguracaoAplicadaEm: versao is null ? null : DateTimeOffset.UtcNow, ConfiguracaoVersao: versao),
        ]);

        var tela = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero) { Operador = "Ana" };
        await tela.AtualizarAsync();
        Assert.Equal(Sinal.Atencao, tela.SinalDaSituacao);
        Assert.StartsWith("Salva; a catraca ainda não confirmou", tela.SituacaoNaCatraca, StringComparison.Ordinal);

        // Subida do worker: a catraca aceitou o salvo de agora.
        var versaoAntiga = tela.VersaoSalva;
        Publicar(versaoAntiga);
        await tela.AcompanharAsync();
        Assert.Equal(Sinal.Bom, tela.SinalDaSituacao);
        Assert.StartsWith("Aplicada", tela.SituacaoNaCatraca, StringComparison.Ordinal);

        // Salvar muda a versão do salvo: a catraca ainda está com a antiga.
        var mensagem = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao);
        mensagem.Herda = false;
        mensagem.Valor = "Entrada sintetica 1";
        await tela.Salvar.ExecutarAsync();
        var versaoNova = tela.VersaoSalva;
        Assert.NotEqual(versaoAntiga, versaoNova);
        Assert.Equal("Salva, não aplicada", tela.SituacaoNaCatraca);

        await tela.PedirAplicacao.ExecutarAsync();
        await tela.ConfirmarAplicacao.ExecutarAsync();
        var id = Guid.Parse(fila.Listar(1).Single().Comando.Id.ToString());

        // O worker pega o pedido e a catraca já aceitou a versão nova; o pedido ainda não terminou.
        Assert.True(fila.Receber(id, DateTimeOffset.UtcNow));
        Publicar(versaoNova);
        await tela.AcompanharAsync();
        Assert.Equal(("Aplicando: aguardando a catraca", Sinal.Atencao), (tela.SituacaoNaCatraca, tela.SinalDaSituacao));

        // Concluído, mas a versão publicada ainda é a antiga (o worker publica a cada 2 s).
        Publicar(versaoAntiga);
        fila.Concluir(id, Access.Application.Devices.SituacaoDoComando.Concluido, "configuração enviada; catraca atendendo", DateTimeOffset.UtcNow);
        await tela.AcompanharAsync();
        Assert.Equal(("Salva, não aplicada", Sinal.Atencao), (tela.SituacaoNaCatraca, tela.SinalDaSituacao));
        Assert.Equal("Feito", tela.Aplicacoes[0].Situacao);

        // Concluído e a catraca com a versão do salvo: aplicada.
        Publicar(versaoNova);
        await tela.AcompanharAsync();
        Assert.Equal(Sinal.Bom, tela.SinalDaSituacao);
        Assert.StartsWith("Aplicada", tela.SituacaoNaCatraca, StringComparison.Ordinal);
    }

    [Fact]
    public void Parametrizacao_situacao_falhou_ou_expirou_nunca_e_aplicada()
    {
        static ComandoRegistrado Pedido(Contracts.Edge.V1.SituacaoDoComando s) =>
            new() { Tipo = Contracts.Edge.V1.TipoDeComando.AplicarConfiguracao, Situacao = s };

        var v1 = new string('a', 64);
        var v2 = new string('b', 64);

        Assert.Equal(Sinal.Problema, ParametrizacaoViewModel.Situacao(v1, v2, Pedido(Contracts.Edge.V1.SituacaoDoComando.Falhou)).Sinal);
        Assert.Equal(Sinal.Atencao, ParametrizacaoViewModel.Situacao(v1, v2, Pedido(Contracts.Edge.V1.SituacaoDoComando.Expirado)).Sinal);
        Assert.Equal(Sinal.Atencao, ParametrizacaoViewModel.Situacao(v1, v1, Pedido(Contracts.Edge.V1.SituacaoDoComando.Falhou)).Sinal);
        Assert.Equal(Sinal.Atencao, ParametrizacaoViewModel.Situacao(v1, v1, Pedido(Contracts.Edge.V1.SituacaoDoComando.Pendente)).Sinal);
        Assert.Equal(Sinal.Problema, ParametrizacaoViewModel.Situacao(string.Empty, v1, null).Sinal);
        Assert.Equal(Sinal.Bom, ParametrizacaoViewModel.Situacao(v1, v1, Pedido(Contracts.Edge.V1.SituacaoDoComando.Concluido)).Sinal);
    }

    /// <summary>"O que muda (atual → novo)" lista exatamente os campos mudados, e só eles.</summary>
    [Fact]
    public async Task Parametrizacao_o_que_muda_lista_exatamente_os_campos_alterados()
    {
        var tela = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();
        Assert.Empty(tela.Mudancas);

        var tempo = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1);
        var mensagem = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao);
        var urna = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.OperacaoDoLeitor2);
        tempo.Herda = false;
        tempo.Valor = "07";
        mensagem.Herda = false;
        mensagem.Valor = "Entrada sintetica 1";
        urna.Herda = false;
        urna.Herda = true;

        Assert.Equal(
            [
                new LinhaDeMudanca(tempo.Rotulo, "padrão do evento (5 s)", "7 s"),
                new LinhaDeMudanca(mensagem.Rotulo, "padrão do evento (“Aproxime o ingresso”)", "“Entrada sintetica 1”"),
            ],
            tela.Mudancas);
        Assert.Equal("2 alterações não salvas.", tela.ResumoDasMudancas);

        // Validação no campo, antes de salvar.
        tempo.Valor = "51";
        Assert.Equal("O tempo vai de 1 a 50 segundos.", tempo.Erro);
        mensagem.Valor = new string('x', 35);
        Assert.Equal("A mensagem tem 35 letras; o visor mostra 32.", mensagem.Erro);
        tela.Operador = "Ana";
        Assert.False(tela.Salvar.CanExecute(null));

        // Voltar ao que está salvo tira a linha.
        tempo.Herda = true;
        mensagem.Herda = true;
        Assert.Empty(tela.Mudancas);
        Assert.Equal(string.Empty, tempo.Erro);
    }

    /// <summary>
    /// O que aguarda confirmação aparece desabilitado, com o selo e o motivo: o campo atrás de
    /// chave técnica desligada, as opções que leem na saída e as variantes da função de liberação.
    /// </summary>
    [Fact]
    public async Task Parametrizacao_campo_aguardando_confirmacao_fica_desabilitado_com_selo_e_motivo()
    {
        var tela = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero) { ModoTecnico = true };
        await tela.AtualizarAsync();

        foreach (var campo in new[] { Contracts.Edge.V1.CampoDaCatraca.WiegandDoisLeitores, Contracts.Edge.V1.CampoDaCatraca.FormasDeEntradaOnLine })
        {
            var c = Assert.Single(tela.CamposDaInstalacao, x => x.Campo == campo);
            Assert.False(c.Disponivel);
            Assert.False(c.Editavel);
            Assert.NotEmpty(c.Selos);
            c.Herda = false;
            Assert.True(c.Herda);
        }

        Assert.Contains(CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.WiegandDoisLeitores).Selos, s => s.Contains("HIL-CARD-05", StringComparison.Ordinal));
        Assert.Contains(CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.FormasDeEntradaOnLine).Selos, s => s.Contains("INT-SM-032", StringComparison.Ordinal));

        // Decisão D9 (docs/34 §9): as quatro funções podem ser escolhidas; o sentido nesta
        // instalação é conferido no mapa de giro (aba Giro), e o aviso diz isso.
        var funcao = Assert.Single(tela.CamposDaLiberacao, x => x.Campo == Contracts.Edge.V1.CampoDaCatraca.FuncaoDeLiberacaoDaEntrada);
        Assert.True(funcao.Disponivel);
        Assert.Equal(4, funcao.Opcoes.Count(o => o.Disponivel));
        Assert.Contains(funcao.Selos, s => s.Contains("NOVO-HIL-DIR-11", StringComparison.Ordinal));

        funcao.Herda = false;
        funcao.Escolhida = funcao.Opcoes.Single(o => o.Valor == "EntradaInvertida");
        Assert.Equal(string.Empty, funcao.Erro);
        tela.Operador = "Ana";
        Assert.True(tela.Salvar.CanExecute(null));

        // 5 × 8 continua a confirmar: o selo aparece no tipo de leitor, que segue editável.
        var tipo = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TipoDeLeitor);
        Assert.True(tipo.Disponivel);
        Assert.Contains(tipo.Selos, s => s.Contains("NOVO-HIL-QR-02", StringComparison.Ordinal));
        Assert.Equal(9, tipo.Opcoes.Count);
    }

    /// <summary>
    /// No modo guiado, nenhum campo técnico e nenhum termo do SDK (GLOSSARIO; docs/34 §7, P13).
    /// O tipo de leitor vira lista com nomes do operador.
    /// </summary>
    [Fact]
    public async Task Parametrizacao_modo_guiado_nao_mostra_campo_tecnico_nem_termo_do_sdk()
    {
        var tela = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();
        Assert.False(tela.ModoTecnico);

        var visiveis = tela.CamposDaLeitura.Concat(tela.CamposDaLiberacao).Concat(tela.CamposDoDisplay).Concat(tela.CamposDaInstalacao).ToList();
        Assert.Equal(
            [
                Contracts.Edge.V1.CampoDaCatraca.TipoDeLeitor, Contracts.Edge.V1.CampoDaCatraca.OperacaoDoLeitor2,
                Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1, Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao,
            ],
            visiveis.Select(c => c.Campo));
        Assert.Empty(tela.CamposDaInstalacao);

        var tipo = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TipoDeLeitor);
        Assert.Equal(["Leitor de código de barras", "Leitor de QR Code"], tipo.Opcoes.Select(o => o.Nome));
        Assert.Equal("Leitor de QR Code", tipo.Escolhida!.Nome);
        Assert.NotEmpty(tipo.Selos);

        string[] termosDoSdk =
        [
            "Configurar", "Liberar", "Enviar", "EI-", "FUN:", "HIL-", "INT-", "NOVO-", "T25", "Wiegand", "Abatrack",
            "SmartCard", "por letras", "serial", "Inner", "relé", "Habilita",
        ];
        var textos = visiveis.SelectMany(c => (IEnumerable<string>)[c.Rotulo, c.TextoHerdar, c.TextoDaOrigem, .. c.Selos, .. c.Opcoes.Select(o => o.Nome)]);
        foreach (var texto in textos)
        {
            Assert.DoesNotContain(termosDoSdk, termo => texto.Contains(termo, StringComparison.OrdinalIgnoreCase));
        }

        // No técnico, os campos de instalação aparecem; ao voltar ao guiado, a aba some.
        tela.ModoTecnico = true;
        tela.AbaSelecionada = (int)AbaDaParametrizacao.Instalacao;
        Assert.Equal(2, tela.CamposDaInstalacao.Count);
        Assert.Contains("ConfigurarTipoLeitor", tipo.Rotulo, StringComparison.Ordinal);
        tela.ModoTecnico = false;
        Assert.Equal((int)AbaDaParametrizacao.Leitura, tela.AbaSelecionada);
    }

    [Fact]
    public async Task Parametrizacao_abre_pela_gerenciar_catraca_naquela_catraca_e_fica_fora_do_menu()
    {
        var janela = new JanelaViewModel(Cliente());
        await janela.Gerenciar.ExecutarAsync(2);
        ((GerenciarCatracaViewModel)janela.TelaAtual).Operador = "Ana";

        await janela.Parametrizar.ExecutarAsync(2);

        var tela = Assert.IsType<ParametrizacaoViewModel>(janela.TelaAtual);
        await tela.AtualizarAsync();
        Assert.Equal((2, "Ana"), (tela.Catraca, tela.Operador));
        Assert.Equal(8, tela.Campos.Count);
        Assert.DoesNotContain(janela.Telas, t => t is ParametrizacaoViewModel);

        // A atualização periódica não desfaz o que o operador está mudando.
        var tempo = CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1);
        tempo.Herda = false;
        tempo.Valor = "9";
        await janela.AtualizarAsync();
        Assert.Equal("9", CampoDe(tela, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1).Valor);
        Assert.Single(tela.Mudancas);
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

    // ------------------------------------------------------------------ mapa de giro (D9)

    /// <summary>
    /// Clicar nos braços abre "Giro desta catraca" com a seta do sentido; clicar na urna leva à
    /// linha do leitor 2. Mudar a função muda a seta; salvar exige o nome; o selo "sentido ainda
    /// não conferido" aparece até alguém girar e registrar.
    /// </summary>
    [Fact]
    public async Task Gemeo_clicar_nos_bracos_abre_o_giro_e_a_urna_leva_ao_leitor_2()
    {
        var tela = new GemeoDigitalViewModel(Cliente(), relogioDaCena: () => TimeSpan.Zero);
        await tela.AtualizarAsync();
        Assert.False(tela.PainelDoGiroAberto);
        Assert.Null(tela.SetaDoGiro);

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        await tela.CarregarGiroAsync();
        Assert.True(tela.PainelDoGiroAberto);
        Assert.Equal(4, tela.Giro.Linhas.Count);
        Assert.All(tela.Giro.Linhas, l => Assert.True(l.Herda));
        Assert.Equal(Desktop.ViewModels.GemeoDigital.SentidoDoGiro.Entrada, tela.SetaDoGiro);
        Assert.Equal(string.Empty, tela.Giro.SeloDaCatraca);

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
        var urna = Assert.IsType<LinhaDoMapaDeGiro>(tela.Giro.LinhaEmFoco);
        Assert.Equal(Contracts.Edge.V1.OrigemDoGiro.Leitor2, urna.Origem);
        Assert.True(urna.EmFoco);

        // "Urna gira para a esquerda (EI-042) e conta como entrada."
        urna.Herda = false;
        urna.FuncaoEscolhida = urna.OpcoesDeFuncao.Single(o => o.Valor == (int)FuncaoDoGiro.Saida);
        Assert.Equal(Desktop.ViewModels.GemeoDigital.SentidoDoGiro.Saida, tela.SetaDoGiro);
        Assert.Equal("Conta como entrada", urna.ResumoDoRotulo);
        Assert.Equal("Entrada liberada", urna.TextoEfetivo);
        Assert.Single(tela.Giro.Mudancas);
        Assert.False(tela.Giro.Salvar.CanExecute(null));

        tela.Giro.Operador = "Ana Sintética";
        await tela.Giro.Salvar.ExecutarAsync();
        Assert.Empty(tela.Giro.Problemas);
        Assert.Empty(tela.Giro.Mudancas);
        Assert.Equal("Sentido ainda não conferido nesta instalação", tela.Giro.SeloDaCatraca);
        var salva = tela.Giro.Linhas.Single(l => l.Origem == Contracts.Edge.V1.OrigemDoGiro.Leitor2);
        Assert.False(salva.Herda);
        Assert.Equal(SituacaoDaConferencia.NaoConferida, salva.Conferencia);
        Assert.Equal("Ana Sintética", tela.Giro.AlteradoPor);
        Assert.StartsWith("Salva", tela.Giro.SituacaoNaCatraca, StringComparison.Ordinal);

        // Aplicar em dois passos, com o comando da catraca.
        await tela.Giro.PedirAplicacao.ExecutarAsync();
        Assert.True(tela.Giro.ConfirmandoAplicacao);
        await tela.Giro.ConfirmarAplicacao.ExecutarAsync();
        var comandos = await Cliente().ListarComandosAsync(new ListarComandosRequest { Inner = 1, Limite = 10 });
        Assert.Contains(comandos.Comandos, c => c.Tipo is TipoDeComando.AplicarConfiguracao && c.Operador == "Ana Sintética");

        // Conferência: girou para o lado da seta.
        await tela.Giro.ConferirComoEsperado.ExecutarAsync(salva);
        Assert.Empty(tela.Giro.Problemas);
        Assert.Equal(string.Empty, tela.Giro.SeloDaCatraca);
        Assert.Equal(SituacaoDaConferencia.ComoEsperado, tela.Giro.Linhas.Single(l => l.Origem == Contracts.Edge.V1.OrigemDoGiro.Leitor2).Conferencia);

        await tela.FecharPainelDoGiro.ExecutarAsync();
        Assert.Null(tela.SetaDoGiro);
    }

    /// <summary>A pré-visualização só anima o desenho: nada é gravado, nada vai à catraca.</summary>
    [Fact]
    public async Task Gemeo_pre_visualizacao_do_giro_anima_so_o_desenho()
    {
        var tela = new GemeoDigitalViewModel(Cliente(), relogioDaCena: () => TimeSpan.Zero);
        await tela.AtualizarAsync();
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
        await tela.CarregarGiroAsync();
        Assert.True(tela.Giro.PreVisualizacaoDisponivel);

        var urna = tela.Giro.LinhaEmFoco!;
        await tela.Giro.PreVisualizar.ExecutarAsync(urna);

        Assert.NotNull(tela.RoteiroEmAndamento);
        Assert.StartsWith("Pré-visualização do giro", tela.RoteiroEmAndamento!.Nome, StringComparison.Ordinal);
        Assert.Contains(tela.RoteiroEmAndamento.Passos, p => p.Narracao.Contains("nada foi enviado", StringComparison.Ordinal));
        Assert.Empty((await Cliente().ListarComandosAsync(new ListarComandosRequest { Inner = 1, Limite = 10 })).Comandos);
        Assert.Equal(0, new MapasDeGiro(_banco.Fabrica).RevisoesNoHistorico(1));

        tela.ModoAoVivo = true;
        await tela.Giro.PreVisualizar.ExecutarAsync(urna);
        Assert.Contains("demonstração", tela.Mensagem, StringComparison.Ordinal);
    }

    /// <summary>A aba Giro da Parametrização é o mesmo mapa, da mesma catraca, sem gêmeo para animar.</summary>
    [Fact]
    public async Task Parametrizacao_aba_giro_carrega_o_mapa_da_catraca()
    {
        var tela = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();
        tela.Operador = "Ana";

        Assert.Equal(tela.Catraca, tela.Giro.Catraca);
        Assert.Equal(4, tela.Giro.Linhas.Count);
        Assert.Equal("Ana", tela.Giro.Operador);
        Assert.False(tela.Giro.PreVisualizacaoDisponivel);
        Assert.Equal((int)AbaDaParametrizacao.Giro, 4);

        var teclado = tela.Giro.Linhas.Single(l => l.Origem == Contracts.Edge.V1.OrigemDoGiro.Teclado);
        Assert.Contains("teclado", teclado.Aviso, StringComparison.Ordinal);

        teclado.Herda = false;
        teclado.Texto = new string('x', 33);
        Assert.NotEmpty(teclado.Erro);
        Assert.False(tela.Giro.Salvar.CanExecute(null));
    }
}
