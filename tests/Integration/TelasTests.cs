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
            mapasDeGiro: new MapasDeGiro(_banco.Fabrica),
            filaDeSaida: new FilaDeSaidaSqlite(_banco.Fabrica));

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
                if (painel.UltimosAcessos.Count > 0)
                {
                    chegou.TrySetResult();
                }
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

    /// <summary>
    /// O que passou antes de o painel abrir aparece na lista, e não só nos contadores: com
    /// 1 liberado no contador, a lista não pode dizer "Aguardando o primeiro acesso".
    /// </summary>
    [Fact]
    public async Task Painel_ao_vivo_mostra_os_acessos_de_antes_de_abrir()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);
        _repositorio.TentarUsar("5555555555", "p1", "inner-1", DateTimeOffset.UtcNow);

        var painel = new PainelAoVivoViewModel(Cliente());
        await painel.AtualizarAsync();
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var carregou = new TaskCompletionSource();
        var acompanhando = painel.AcompanharAsync(
            acao =>
            {
                acao();
                if (painel.UltimosAcessos.Count > 0)
                {
                    carregou.TrySetResult();
                }
            },
            cancelamento.Token);

        await carregou.Task.WaitAsync(cancelamento.Token);
        cancelamento.Cancel();
        await acompanhando;

        Assert.Equal((1, 1), (painel.Liberados, painel.Negados));
        Assert.Equal(2, painel.UltimosAcessos.Count);
        Assert.False(painel.UltimosAcessos[0].Liberado);
        Assert.True(painel.UltimosAcessos[1].Liberado);
        Assert.All(painel.UltimosAcessos, l => Assert.DoesNotContain(Qr, l.Codigo, StringComparison.Ordinal));
    }

    /// <summary>
    /// Sem o fluxo ao vivo (ainda conectando, ou na captura das telas), a atualização da tela já
    /// enche a lista vazia pelo que está gravado.
    /// </summary>
    [Fact]
    public async Task Painel_ao_vivo_enche_a_lista_vazia_na_atualizacao()
    {
        _repositorio.TentarUsar(Qr, "p1", "inner-1", DateTimeOffset.UtcNow);

        var painel = new PainelAoVivoViewModel(Cliente());
        await painel.AtualizarAsync();

        var linha = Assert.Single(painel.UltimosAcessos);
        Assert.True(linha.Liberado);
        Assert.DoesNotContain(Qr, linha.Codigo, StringComparison.Ordinal);

        // Uma segunda atualização não repete a linha.
        await painel.AtualizarAsync();
        Assert.Single(painel.UltimosAcessos);
    }

    [Fact]
    public void A_lista_ao_vivo_completa_sem_apagar_o_que_chegou_ao_vivo()
    {
        var painel = new PainelAoVivoViewModel(Cliente());
        var antiga = new LinhaDeAcesso("10:00:00", 1, "Liberado", true, false, "", "", Sinal.Bom, EventoId: "e-1");
        painel.Acrescentar(antiga with { EventoId = "e-3" });
        painel.Completar([antiga with { EventoId = "e-2" }, antiga, antiga with { EventoId = "e-3" }]);

        Assert.Equal(["e-3", "e-2", "e-1"], painel.UltimosAcessos.Select(l => l.EventoId));
    }

    [Fact]
    public void A_lista_ao_vivo_nao_repete_o_acesso_que_ja_mostrou()
    {
        var painel = new PainelAoVivoViewModel(Cliente());
        var linha = new LinhaDeAcesso("10:00:00", 1, "Liberado", true, false, "", "", Sinal.Bom, EventoId: "e-1");
        painel.Repor([linha]);
        painel.Acrescentar(linha);
        painel.Acrescentar(linha with { EventoId = "e-2" });

        Assert.Equal(["e-2", "e-1"], painel.UltimosAcessos.Select(l => l.EventoId));
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

    /// <summary>
    /// Defeito (relatório "não funcional", 01/10): com o campo "De" vazio a tela não mandava o
    /// início, e o serviço contava só as últimas 24 h — sem dizer. Vazio quer dizer "desde o
    /// começo", como na tela de Acessos.
    /// </summary>
    [Fact]
    public async Task Prestacao_de_contas_sem_data_inicial_conta_desde_o_comeco_e_nao_so_24_horas()
    {
        var agora = DateTimeOffset.UtcNow;
        _repositorio.TentarUsar("9999000001", "p1", "inner-1", agora.AddDays(-3));
        _repositorio.TentarUsar("9999000002", "p1", "inner-1", agora.AddMinutes(-5));

        var contas = new ContasViewModel(Cliente(), () => agora) { Desde = null };
        await contas.Gerar.ExecutarAsync();

        Assert.Equal(2, contas.Contas!.Negados);
        Assert.StartsWith("desde o começo até ", contas.Periodo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Defeito: o CSV não dizia que período os números cobriam — e quem mudava o filtro depois de
    /// gerar exportava números de outro período sem saber. O período vem do serviço, como ele
    /// aplicou, no horário de Brasília.
    /// </summary>
    [Fact]
    public async Task Prestacao_de_contas_diz_na_tela_e_no_csv_o_periodo_que_os_numeros_cobrem()
    {
        // 01/10/2026 15:30 em Brasília.
        var agora = new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero);
        var contas = new ContasViewModel(Cliente(), () => agora) { HoraDesde = "08:00" };
        Assert.False(contas.PodeExportar);

        await contas.Gerar.ExecutarAsync();

        Assert.True(contas.PodeExportar);
        Assert.Equal("de 01/10/2026 08:00 a 01/10/2026 15:30 (horário de Brasília)", contas.Periodo);

        // Mudar o filtro sem gerar de novo não muda o período dos números.
        contas.HoraDesde = "10:00";
        Assert.Contains("Período;de 01/10/2026 08:00 a 01/10/2026 15:30 (horário de Brasília)", contas.ParaCsv(), StringComparison.Ordinal);
    }

    /// <summary>Defeito: o nome sugerido do arquivo usava a hora do Windows, não a do evento.</summary>
    [Fact]
    public void Nome_do_arquivo_da_prestacao_usa_a_hora_de_brasilia()
    {
        // 02:30 UTC de 02/10 = 23:30 de 01/10 em Brasília.
        var contas = new ContasViewModel(Cliente(), () => new DateTimeOffset(2026, 10, 2, 2, 30, 0, TimeSpan.Zero));
        Assert.Equal("prestacao-de-contas-2026-10-01-2330.csv", contas.NomeDoArquivoSugerido());
    }

    /// <summary>
    /// A tela não pode parecer ter o que não tem: R1–R8 (docs/25) e o corte fechado aparecem
    /// como ainda não disponíveis, cada um com o motivo.
    /// </summary>
    [Fact]
    public void Relatorios_da_fase_6_aparecem_como_ainda_nao_disponiveis_com_motivo()
    {
        var contas = new ContasViewModel(Cliente());

        foreach (var r in new[] { "R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8" })
        {
            var item = Assert.Single(contas.AindaNaoDisponivel, i => i.Rotulo.StartsWith(r + " ", StringComparison.Ordinal));
            Assert.Contains("Fase 6", item.Valor, StringComparison.Ordinal);
        }

        Assert.Contains(contas.AindaNaoDisponivel, i => i.Rotulo.Contains("código de conferência", StringComparison.Ordinal));
        Assert.Contains("(PDF)", contas.AindaNaoDisponivel[0].Rotulo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Defeito: exportar para um arquivo que não pode ser gravado (aberto no Excel, pasta que
    /// sumiu, sem permissão) estourava como "erro inesperado" em vez de dizer o que houve.
    /// </summary>
    [Fact]
    public async Task Prestacao_de_contas_que_nao_pode_ser_gravada_vira_mensagem_e_nao_erro()
    {
        var contas = new ContasViewModel(Cliente());
        await contas.Gerar.ExecutarAsync();

        var caminho = Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}", "contas.csv");
        await contas.ExportarAsync(caminho);

        Assert.StartsWith("Não foi possível gravar o arquivo", contas.Mensagem, StringComparison.Ordinal);
        Assert.False(File.Exists(caminho));
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
    /// A confirmação da liberação manual não vale para outro motivo nem para outra catraca:
    /// mudar qualquer um dos dois desfaz a confirmação pendente, e nada é enviado.
    /// </summary>
    [Fact]
    public async Task Liberacao_manual_confirmada_nao_sobrevive_a_mudanca_de_motivo_ou_catraca()
    {
        var tela = new GerenciarCatracaViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();
        tela.Operador = "Ana";
        tela.Motivo = "Criança de colo sem ingresso";
        await tela.PrepararLiberacao.ExecutarAsync();
        Assert.True(tela.ConfirmandoLiberacao);

        tela.Motivo = "Outro motivo";
        Assert.False(tela.ConfirmandoLiberacao);
        Assert.False(tela.LiberarManualmente.CanExecute(null));

        tela.Motivo = "Criança de colo sem ingresso";
        await tela.PrepararLiberacao.ExecutarAsync();
        tela.Catraca = 2;
        Assert.False(tela.ConfirmandoLiberacao);
        Assert.Empty(tela.Historico);
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
        // Dois passos: sem a confirmação, nada sai.
        Assert.False(tela.LiberarManualmente.CanExecute(null));
        Assert.True(tela.PrepararLiberacao.CanExecute(null));
        await tela.PrepararLiberacao.ExecutarAsync();
        Assert.True(tela.ConfirmandoLiberacao);
        Assert.Contains("Confirmar: liberar um giro na Catraca 01", tela.TextoDaConfirmacaoDaLiberacao, StringComparison.Ordinal);
        Assert.Empty(tela.Historico);

        await tela.LiberarManualmente.ExecutarAsync();
        Assert.False(tela.ConfirmandoLiberacao);

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

    /// <summary>
    /// U07: numa instalação real (sem modo simulação), o Simulador não aparece no menu do operador.
    /// A tela continua registrada: só a entrada do menu some.
    /// </summary>
    [Fact]
    public async Task Em_modo_real_o_simulador_nao_aparece_no_menu()
    {
        var janela = new JanelaViewModel(Cliente());
        await janela.Painel.AtualizarAsync();

        Assert.False(janela.Painel.Estado.Simulacao);
        Assert.DoesNotContain(janela.TelasDoMenu, t => t is SimuladorViewModel);
        Assert.Contains(janela.Telas, t => t is SimuladorViewModel);
    }

    [Fact]
    public async Task Aplicar_agora_pede_a_todas_as_catracas_e_exige_o_nome()
    {
        var tela = new ConfiguracoesViewModel(Cliente());

        // Sem confirmação, o comando nem executa: nada sai para as catracas.
        Assert.False(tela.AplicarAgora.CanExecute(null));
        await tela.PrepararAplicacao.ExecutarAsync();
        Assert.True(tela.ConfirmandoAplicacao);

        await tela.AplicarAgora.ExecutarAsync();
        Assert.Equal("Não foi pedido. Corrija os itens abaixo.", tela.Mensagem);
        Assert.Contains("Informe o nome de quem está pedindo (2 a 80 caracteres).", tela.Problemas);
        Assert.True(tela.ConfirmandoAplicacao, "um pedido recusado mantém a confirmação");

        tela.Operador = "Ana";
        await tela.AplicarAgora.ExecutarAsync();
        Assert.StartsWith("Pedido a 2 catraca(s).", tela.Mensagem, StringComparison.Ordinal);
        Assert.False(tela.ConfirmandoAplicacao);
    }

    /// <summary>
    /// Achado E6-1 do docs/41: o botão do WPF só reconsulta CanExecute quando o comando dispara
    /// CanExecuteChanged. Chamar ExecutarAsync direto (como o teste acima) não percebe o defeito.
    /// </summary>
    [Fact]
    public async Task Aplicar_agora_avisa_os_botoes_quando_a_confirmacao_abre_e_fecha()
    {
        var tela = new ConfiguracoesViewModel(Cliente());
        var avisos = new Dictionary<string, int> { ["preparar"] = 0, ["aplicar"] = 0, ["cancelar"] = 0 };
        tela.PrepararAplicacao.CanExecuteChanged += (_, _) => avisos["preparar"]++;
        tela.AplicarAgora.CanExecuteChanged += (_, _) => avisos["aplicar"]++;
        tela.CancelarAplicacao.CanExecuteChanged += (_, _) => avisos["cancelar"]++;

        await tela.PrepararAplicacao.ExecutarAsync();

        Assert.True(avisos["aplicar"] > 0, "Confirmar nasceria desabilitado");
        Assert.True(avisos["cancelar"] > 0, "Cancelar nasceria desabilitado");
        Assert.True(tela.AplicarAgora.CanExecute(null));
        Assert.True(tela.CancelarAplicacao.CanExecute(null));

        var antes = avisos["preparar"];
        await tela.CancelarAplicacao.ExecutarAsync();

        Assert.True(avisos["preparar"] > antes, "Aplicar agora ficaria desabilitado depois de cancelar");
        Assert.True(tela.PrepararAplicacao.CanExecute(null));
    }

    /// <summary>
    /// Achado E5-2 do docs/41: o que ia para cartas mortas não tinha caminho de volta. Pela tela de
    /// Sincronização, com o nome de quem pede, volta para a fila e fica anotado na base.
    /// </summary>
    [Fact]
    public async Task Reenviar_os_recusados_pela_tela_devolve_a_fila_e_anota_quem_pediu()
    {
        var fila = new FilaDeSaidaSqlite(_banco.Fabrica);
        using (var conexao = _banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                INSERT INTO outbox (id, aggregate_type, aggregate_id, payload_json, priority, connector, idempotency_key, created_at)
                VALUES ('t1', 'tentativa', 't1', '{}', 5, 'painel-tentativas', 'tentativa:t1', '2026-10-08T20:00:00.0000000+00:00');
                """;
            comando.ExecuteNonQuery();
        }

        await fila.MoverParaCartasMortasAsync("t1", "HTTP 401 Unauthorized", DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Empty(fila.BacklogPorConector());

        var tela = new SincronizacaoViewModel(Cliente());
        await tela.AtualizarAsync();
        Assert.Equal(1, tela.CartasMortas);
        Assert.True(tela.ReenviarCartasMortas.CanExecute(null));

        // Sem nome, não reenvia.
        await tela.ReenviarCartasMortas.ExecutarAsync();
        Assert.Contains("Informe o nome", tela.Mensagem, StringComparison.Ordinal);
        Assert.Equal(0, fila.BacklogPorConector().GetValueOrDefault("painel-tentativas"));

        tela.Operador = "Ana";
        await tela.ReenviarCartasMortas.ExecutarAsync();

        Assert.StartsWith("1 tentativa(s) de volta à fila", tela.Mensagem, StringComparison.Ordinal);
        Assert.Equal(1L, fila.BacklogPorConector()["painel-tentativas"]);
        Assert.Equal(0, fila.ContarCartasMortas());
        Assert.Equal(0, tela.CartasMortas);
        Assert.False(tela.ReenviarCartasMortas.CanExecute(null));

        using var leitura = _banco.Fabrica.Abrir();
        using var consulta = leitura.CreateCommand();
        consulta.CommandText = "SELECT reprocessed_by FROM dead_letter;";
        Assert.Equal("Ana", consulta.ExecuteScalar());
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

    /// <summary>
    /// Abrir a tela dispara um carregamento e quem abriu pede outro: os dois são o mesmo, senão o
    /// que terminasse por último apagava o que o operador digitou no meio (CI, 02/10/2026). Trocar
    /// de catraca no meio termina com a catraca nova carregada.
    /// </summary>
    [Fact]
    public async Task Parametrizacao_carrega_uma_vez_por_vez_e_termina_na_catraca_escolhida()
    {
        var tela = new ParametrizacaoViewModel(Cliente()) { Operador = "Ana" };
        var primeiro = tela.AtualizarAsync();
        var segundo = tela.AtualizarAsync();
        Assert.Same(primeiro, segundo);
        await primeiro;
        Assert.Equal(1, tela.Catraca);

        var troca = tela.AtualizarAsync();
        tela.Catraca = 2;
        await troca;
        await tela.AtualizarAsync();
        Assert.Equal(2, tela.Catraca);
        Assert.Equal(8, tela.Campos.Count);
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

    // ------------------------------------------------------------------ gêmeo, central da configuração (docs/33 §9)

    private static readonly string[] TermosDoSdkNoGuiado =
    [
        "Configurar", "Liberar", "Enviar", "EI-", "FUN:", "HIL-", "INT-", "NOVO-", "T25", "Wiegand", "Abatrack",
        "SmartCard", "por letras", "serial", "Inner", "relé", "Habilita",
    ];

    private async Task<GemeoDigitalViewModel> GemeoCarregadoAsync()
    {
        var tela = new GemeoDigitalViewModel(Cliente(), relogioDaCena: () => TimeSpan.Zero, esperaPeloResultado: TimeSpan.Zero);
        await tela.AtualizarAsync();
        Assert.Equal(1, tela.Central.Catraca);
        Assert.Equal(8, tela.Central.Parametrizacao.Campos.Count);
        return tela;
    }

    /// <summary>
    /// O selo da configuração no gêmeo mostra só a situação em poucas palavras (a coluna é
    /// estreita); a frase inteira continua na dica do selo.
    /// </summary>
    [Fact]
    public async Task Gemeo_selo_da_configuracao_usa_a_situacao_curta()
    {
        var tela = await GemeoCarregadoAsync();
        var inteira = tela.Central.SituacaoNaCatraca;
        var curta = tela.Central.SituacaoCurta;
        Assert.StartsWith("Salva; a catraca ainda não confirmou", inteira, StringComparison.Ordinal);
        Assert.Equal("Salva", curta);
        Assert.DoesNotContain(':', curta);
        Assert.StartsWith(curta, inteira, StringComparison.Ordinal);
    }

    private static Contracts.Edge.V1.CampoDaCatraca[] CamposDoPainel(GemeoDigitalViewModel tela) =>
        [.. tela.Central.CamposDaPeca.Select(c => c.Campo)];

    /// <summary>
    /// Clicar em cada peça abre o painel dela com os campos certos: no guiado, só os do operador;
    /// no técnico, os de instalação também. Peça sem parâmetro diz que não há nada a configurar.
    /// </summary>
    [Fact]
    public async Task Gemeo_cada_peca_abre_o_painel_certo_com_os_campos_certos()
    {
        var tela = await GemeoCarregadoAsync();
        Assert.False(tela.Central.PainelAberto);
        Assert.True(tela.Central.SemPainel);

        // Leitor da frente (QR): tipo de leitor; o leitor 1 só no técnico.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.LeitorQr);
        Assert.True(tela.Central.PainelAberto);
        Assert.Equal("Leitor da frente (QR)", tela.Central.TituloDoPainel);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.TipoDeLeitor], CamposDoPainel(tela));
        Assert.Contains(tela.Central.CamposDaPeca[0].Selos, s => s.Length > 0);
        Assert.False(tela.Central.MostraGiro);
        Assert.False(tela.PainelDoGiroAberto);
        Assert.Equal("No modo técnico aparece mais 1 ajuste desta peça.", tela.Central.AvisoDoModo);

        // Urna: o leitor 2 e só a linha do leitor 2 do giro.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.OperacaoDoLeitor2], CamposDoPainel(tela));
        Assert.True(tela.PainelDoGiroAberto);
        var linhaDaUrna = Assert.Single(tela.Giro.LinhasVisiveis);
        Assert.Equal(Contracts.Edge.V1.OrigemDoGiro.Leitor2, linhaDaUrna.Origem);
        Assert.False(tela.Giro.ControlesProprios);

        // Braços: tempo de liberação e o mapa de giro inteiro.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1], CamposDoPainel(tela));
        Assert.Equal(4, tela.Giro.LinhasVisiveis.Count);
        Assert.NotNull(tela.SetaDoGiro);

        // Display: mensagem padrão e, como pedido imediato, a mensagem temporária.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao], CamposDoPainel(tela));
        Assert.True(tela.Central.MostraMensagemTemporaria);
        Assert.False(tela.Central.MostraAcertarRelogio);
        Assert.Null(tela.SetaDoGiro);

        // Placa (na coluna): no guiado, só o equipamento e os pedidos de manutenção.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Coluna);
        Assert.Empty(CamposDoPainel(tela));
        Assert.True(tela.Central.MostraEquipamento);
        Assert.True(tela.Central.MostraAcertarRelogio);
        Assert.True(tela.Central.MostraRefazerConexao);
        Assert.False(tela.Central.MostraRele2);
        Assert.Equal("4.2.0", tela.Central.Firmware);
        Assert.Equal("No modo técnico aparecem mais 3 ajustes desta peça.", tela.Central.AvisoDoModo);

        // Peças sem parâmetro: a ficha, e "nada a configurar".
        foreach (var peca in new[]
        {
            Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Base, Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Tampa,
            Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Teclado, Desktop.ViewModels.GemeoDigital.PecaDaCatraca.SinalLiberado,
        })
        {
            tela.Escolher(peca);
            Assert.True(tela.Central.NadaAConfigurar, peca.ToString());
            Assert.Empty(CamposDoPainel(tela));
            Assert.Equal(peca, tela.Central.Ficha!.Peca);
            Assert.False(string.IsNullOrWhiteSpace(tela.Central.Ficha.OQueFaz));
        }

        // Técnico: os campos técnicos de cada peça aparecem, na ordem do painel.
        tela.Central.ModoTecnico = true;
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.LeitorQr);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.TipoDeLeitor, Contracts.Edge.V1.CampoDaCatraca.OperacaoDoLeitor1], CamposDoPainel(tela));
        Assert.Equal(string.Empty, tela.Central.AvisoDoModo);
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1, Contracts.Edge.V1.CampoDaCatraca.FuncaoDeLiberacaoDaEntrada], CamposDoPainel(tela));
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Coluna);
        Assert.Equal([Contracts.Edge.V1.CampoDaCatraca.WiegandDoisLeitores, Contracts.Edge.V1.CampoDaCatraca.FormasDeEntradaOnLine], CamposDoPainel(tela));
        Assert.True(tela.Central.MostraRele2);

        // Pela lista de peças (teclado), o mesmo painel.
        tela.PecaSelecionada = Desktop.ViewModels.GemeoDigital.CatalogoDaFit4.De(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        Assert.Equal(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display, tela.Central.Peca);

        await tela.Central.FecharPainel.ExecutarAsync();
        Assert.True(tela.Central.SemPainel);
    }

    /// <summary>
    /// Alterações em duas peças (e no giro) se acumulam num só "o que muda"; as peças ganham a
    /// marcação de alteração não salva; um só Salvar grava tudo, e só com o nome digitado. Depois
    /// de salvar, a marcação vira "diferente do padrão do evento". Desfazer volta tudo.
    /// </summary>
    [Fact]
    public async Task Gemeo_alteracoes_de_varias_pecas_vao_juntas_num_so_o_que_muda_e_salvar_exige_nome()
    {
        var tela = await GemeoCarregadoAsync();
        var central = tela.Central;
        Assert.Empty(central.Mudancas);
        Assert.Empty(central.Marcas);

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        var mensagem = Assert.Single(central.CamposDaPeca);
        mensagem.Herda = false;
        mensagem.Valor = "Portao sintetico 2";
        Assert.Equal("Portao sintetico 2", tela.TextoDaPrevia);

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
        var urna = Assert.Single(central.CamposDaPeca);
        urna.Herda = false;
        urna.Escolhida = urna.Opcoes.Single(o => o.Nome == "Desligado");
        var giroDaUrna = Assert.Single(tela.Giro.LinhasVisiveis);
        giroDaUrna.Herda = false;
        giroDaUrna.ContaComoEscolhida = giroDaUrna.OpcoesDeContagem.Single(o => o.Nome == "Saída");

        // Navegar para outra peça não perde nada: o "o que muda" é da catraca inteira.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        Assert.Equal(3, central.Mudancas.Count);
        Assert.Contains(central.Mudancas, m => m.Campo == mensagem.Rotulo && m.Novo == "“Portao sintetico 2”");
        Assert.Contains(central.Mudancas, m => m.Campo == urna.Rotulo && m.Novo == "Desligado");
        Assert.Contains(central.Mudancas, m => m.Campo == "Giro · Leitor 2 (urna)");
        Assert.Equal("3 alterações não salvas.", central.ResumoDasMudancas);
        Assert.Equal("3 alterações não salvas", central.AlteracoesPendentes);

        var naoSalvas = central.Marcas.Where(m => m.Tipo is Desktop.ViewModels.GemeoDigital.TipoDeMarca.AlteracaoNaoSalva).Select(m => m.Peca).ToHashSet();
        Assert.Equal(
            new HashSet<Desktop.ViewModels.GemeoDigital.PecaDaCatraca>
            {
                Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display, Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna,
                Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor,
            },
            naoSalvas);
        Assert.DoesNotContain(central.Marcas, m => m.Tipo is Desktop.ViewModels.GemeoDigital.TipoDeMarca.DiferenteDoEvento);
        var versaoDasMarcas = central.VersaoDasMarcas;

        // Aplicar só depois de salvar; salvar só com o nome.
        Assert.False(central.PedirAplicacao.CanExecute(null));
        Assert.StartsWith("Salve as alterações", central.MotivoParaNaoAplicar, StringComparison.Ordinal);
        Assert.False(central.Salvar.CanExecute(null));
        central.Operador = "A";
        Assert.False(central.Salvar.CanExecute(null));
        central.Operador = "Ana Sintética";
        Assert.True(central.Salvar.CanExecute(null));
        Assert.Equal("Ana Sintética", tela.Giro.Operador);
        Assert.Equal("Ana Sintética", central.Comandos.Operador);

        await central.Salvar.ExecutarAsync();
        Assert.Empty(central.Problemas);
        Assert.Empty(central.Mudancas);
        Assert.StartsWith("Salvo para a catraca 1.", central.Mensagem, StringComparison.Ordinal);
        Assert.Equal(string.Empty, tela.Giro.Mensagem);

        // Gravado de verdade, nos dois RPCs, com o nome.
        var salvo = await Cliente().ObterConfiguracaoDaCatracaAsync(new ObterConfiguracaoDaCatracaRequest { Inner = 1 });
        Assert.Equal("Portao sintetico 2", salvo.Campos.Single(c => c.Campo == Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao).ValorDaCatraca);
        Assert.Equal("0", salvo.Campos.Single(c => c.Campo == Contracts.Edge.V1.CampoDaCatraca.OperacaoDoLeitor2).ValorDaCatraca);
        Assert.Equal("Ana Sintética", salvo.AlteradaPor);
        var mapa = await Cliente().ObterMapaDeGiroAsync(new ObterMapaDeGiroRequest { Inner = 1 });
        Assert.Equal(ContagemDoGiro.Saida, mapa.Regras.Single(r => r.Origem == Contracts.Edge.V1.OrigemDoGiro.Leitor2).ContaComo);
        Assert.Equal(salvo.VersaoSalva, central.Parametrizacao.VersaoSalva);
        Assert.StartsWith("Salva", central.SituacaoNaCatraca, StringComparison.Ordinal);

        // Salvo: as peças ficam marcadas como diferentes do padrão do evento.
        Assert.NotEqual(versaoDasMarcas, central.VersaoDasMarcas);
        Assert.DoesNotContain(central.Marcas, m => m.Tipo is Desktop.ViewModels.GemeoDigital.TipoDeMarca.AlteracaoNaoSalva);
        var diferentes = central.Marcas.Where(m => m.Tipo is Desktop.ViewModels.GemeoDigital.TipoDeMarca.DiferenteDoEvento).Select(m => m.Peca).ToHashSet();
        Assert.Contains(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display, diferentes);
        Assert.Contains(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna, diferentes);
        Assert.DoesNotContain(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.LeitorQr, diferentes);

        // Desfazer volta todas as peças ao salvo.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        var tempo = Assert.Single(central.CamposDaPeca);
        tempo.Herda = false;
        tempo.Valor = "9";
        tela.Giro.Linhas.Single(l => l.Origem == Contracts.Edge.V1.OrigemDoGiro.Teclado).Herda = false;
        Assert.Equal(2, central.Mudancas.Count);
        await central.Desfazer.ExecutarAsync();
        Assert.Empty(central.Mudancas);
        Assert.Null(central.Parametrizacao.Campos.Single(c => c.Campo == Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1).ValorSalvo);
    }

    /// <summary>
    /// Aplicar pede confirmação e usa o mesmo comando por catraca da Parametrização ("Aplicar
    /// configuração", só desta catraca). "Aplicada" só depois de Concluido com a versão igual.
    /// </summary>
    [Fact]
    public async Task Gemeo_aplicar_exige_confirmacao_usa_o_comando_da_catraca_e_so_diz_aplicada_depois_de_concluido()
    {
        var fila = new FilaDeComandosSqlite(_banco.Fabrica);
        var operacao = new Operacao(_banco.Fabrica);
        void Publicar(string? versao) => operacao.GravarSituacao(
        [
            new SituacaoDoEquipamento(
                "inner-1", 1, "setor-a", "Polling", true, "4.2.0", 0, null, null, DateTimeOffset.UtcNow,
                ConfiguracaoAplicadaEm: versao is null ? null : DateTimeOffset.UtcNow, ConfiguracaoVersao: versao),
        ]);

        var tela = await GemeoCarregadoAsync();
        var central = tela.Central;
        central.Operador = "Ana Sintética";

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        var tempo = Assert.Single(central.CamposDaPeca);
        tempo.Herda = false;
        tempo.Valor = "8";
        await central.Salvar.ExecutarAsync();
        var versaoNova = central.Parametrizacao.VersaoSalva;
        Assert.NotEmpty(versaoNova);

        // Dois passos: pedir mostra a confirmação; nada vai antes de confirmar.
        Assert.True(central.PedirAplicacao.CanExecute(null));
        Assert.False(central.ConfirmarAplicacao.CanExecute(null));
        await central.PedirAplicacao.ExecutarAsync();
        Assert.True(central.ConfirmandoAplicacao);
        Assert.Contains("fica alguns segundos sem atender", central.TextoDaConfirmacao, StringComparison.Ordinal);
        Assert.Empty(fila.Listar(1));

        await central.CancelarAplicacao.ExecutarAsync();
        Assert.False(central.ConfirmandoAplicacao);
        Assert.Empty(fila.Listar(1));

        await central.PedirAplicacao.ExecutarAsync();
        await central.ConfirmarAplicacao.ExecutarAsync();
        var pedido = Assert.Single(fila.Listar(1)).Comando;
        Assert.Equal((1, "Ana Sintética"), (pedido.Inner, pedido.Operador));
        Assert.Equal(Access.Application.Devices.TipoDeComando.AplicarConfiguracao, pedido.Tipo);
        Assert.Equal(("Aplicando: aguardando a catraca", Sinal.Atencao), (central.SituacaoNaCatraca, central.SinalDaSituacao));
        Assert.False(central.PedirAplicacao.CanExecute(null));

        // O worker pega o pedido e a catraca aceita a versão nova; o pedido ainda não terminou.
        var id = pedido.Id;
        Assert.True(fila.Receber(id, DateTimeOffset.UtcNow));
        Publicar(versaoNova);
        await tela.AtualizarAsync();
        Assert.StartsWith("Aplicando", central.SituacaoNaCatraca, StringComparison.Ordinal);
        Assert.False(central.AplicadaConhecida);

        // Concluído, mas a versão publicada é outra: não aplicada.
        Publicar(new string('a', 64));
        fila.Concluir(id, Access.Application.Devices.SituacaoDoComando.Concluido, "configuração enviada; catraca atendendo", DateTimeOffset.UtcNow);
        await tela.AtualizarAsync();
        Assert.Equal(("Salva, não aplicada", Sinal.Atencao), (central.SituacaoNaCatraca, central.SinalDaSituacao));

        // Concluído e a versão igual: aplicada.
        Publicar(versaoNova);
        await tela.AtualizarAsync();
        Assert.Equal(Sinal.Bom, central.SinalDaSituacao);
        Assert.StartsWith("Aplicada", central.SituacaoNaCatraca, StringComparison.Ordinal);
        Assert.True(central.AplicadaConhecida);
    }

    /// <summary>
    /// No painel da placa (modo técnico), o que aguarda confirmação aparece desabilitado, com o
    /// selo e o motivo: os campos atrás de chave técnica desligada e o relé 2, que nem é campo.
    /// </summary>
    [Fact]
    public async Task Gemeo_campo_a_confirmar_fica_desabilitado_com_selo_no_painel_da_peca()
    {
        var tela = await GemeoCarregadoAsync();
        tela.Central.ModoTecnico = true;
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Coluna);

        foreach (var campo in tela.Central.CamposDaPeca)
        {
            Assert.False(campo.Disponivel);
            Assert.False(campo.Editavel);
            Assert.NotEmpty(campo.Selos);
            campo.Herda = false;
            Assert.True(campo.Herda);
        }

        Assert.Contains(tela.Central.CamposDaPeca[0].Selos, s => s.Contains("HIL-CARD-05", StringComparison.Ordinal));
        Assert.Contains(tela.Central.CamposDaPeca[1].Selos, s => s.Contains("INT-SM-032", StringComparison.Ordinal));
        Assert.Empty(tela.Central.Mudancas);

        Assert.True(tela.Central.MostraRele2);
        Assert.Contains("NOVO-HIL-REL-04/06", CentralDaCatracaViewModel.SeloDoRele2, StringComparison.Ordinal);
        Assert.Contains("padrão de fábrica", CentralDaCatracaViewModel.ValorDoRele2, StringComparison.Ordinal);

        // O tipo de leitor segue editável, com o aviso do 5 × 8.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.LeitorQr);
        var tipo = tela.Central.CamposDaPeca[0];
        Assert.True(tipo.Disponivel);
        Assert.Contains(tipo.Selos, s => s.Contains("NOVO-HIL-QR-02", StringComparison.Ordinal));
    }

    /// <summary>
    /// No modo guiado, nenhum painel de peça mostra campo técnico ou termo do SDK (GLOSSARIO;
    /// docs/34 §7, P13). O aviso diz só que há mais ajustes no modo técnico.
    /// </summary>
    [Fact]
    public async Task Gemeo_modo_guiado_nao_mostra_campo_tecnico_nem_termo_do_sdk_em_nenhuma_peca()
    {
        var tela = await GemeoCarregadoAsync();
        Assert.False(tela.Central.ModoTecnico);

        foreach (var ficha in tela.Pecas)
        {
            tela.Escolher(ficha.Peca);
            Assert.All(tela.Central.CamposDaPeca, c => Assert.False(c.SoTecnico, $"{ficha.Peca}: {c.Campo}"));
            Assert.False(tela.Central.MostraRele2);

            var textos = tela.Central.CamposDaPeca
                .SelectMany(c => (IEnumerable<string>)[c.Rotulo, c.TextoHerdar, c.TextoDaOrigem, .. c.Selos, .. c.Opcoes.Select(o => o.Nome)])
                .Append(tela.Central.TituloDoPainel)
                .Append(tela.Central.AvisoDoModo);
            foreach (var texto in textos)
            {
                Assert.DoesNotContain(TermosDoSdkNoGuiado, termo => texto.Contains(termo, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>
    /// A configuração é real; o selo DEMONSTRAÇÃO fala dos cenários. Ao vivo, o gêmeo mostra a
    /// configuração que a catraca confirmou (versão aplicada igual à salva, Etapa A.5) — e diz que
    /// não sabe quando ela está com outra versão.
    /// </summary>
    [Fact]
    public async Task Gemeo_ao_vivo_mostra_a_configuracao_aplicada_e_nao_presume()
    {
        var operacao = new Operacao(_banco.Fabrica);
        void Publicar(string? versao) => operacao.GravarSituacao(
        [
            new SituacaoDoEquipamento(
                "inner-1", 1, "setor-a", "Polling", true, "4.2.0", 0, null, null, DateTimeOffset.UtcNow,
                ConfiguracaoAplicadaEm: versao is null ? null : DateTimeOffset.UtcNow, ConfiguracaoVersao: versao),
        ]);

        var tela = await GemeoCarregadoAsync();
        Assert.StartsWith("DEMONSTRAÇÃO · cenários", tela.SeloDoModo, StringComparison.Ordinal);
        Assert.Equal("Configuração real desta catraca — vale depois de Aplicar.", CentralDaCatracaViewModel.AvisoDaConfiguracao);

        var central = tela.Central;
        central.Operador = "Ana Sintética";
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        var mensagem = Assert.Single(central.CamposDaPeca);
        mensagem.Herda = false;
        mensagem.Valor = "Bem vindo portao 9";
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
        var urna = Assert.Single(central.CamposDaPeca);
        urna.Herda = false;
        urna.Escolhida = urna.Opcoes.Single(o => o.Nome == "Desligado");
        await central.Salvar.ExecutarAsync();
        var versao = central.Parametrizacao.VersaoSalva;

        // Salvo, mas a catraca não confirmou: o desenho não presume.
        tela.ModoAoVivo = true;
        await tela.AtualizarAsync();
        Assert.Equal("Na catraca agora", tela.TituloDaConfiguracao);
        Assert.False(central.AplicadaConhecida);
        Assert.Empty(tela.ResumoDaConfiguracao);
        Assert.Contains("não dá para saber o que ela está usando", tela.TextoDaConfiguracao, StringComparison.Ordinal);
        Assert.Equal("Aproxime o ingresso", tela.MensagemPadrao);
        Assert.True(tela.UrnaLigada);

        // A catraca confirmou a versão do salvo: o desenho e o quadro mostram o que ela usa.
        Publicar(versao);
        await tela.AtualizarAsync();
        Assert.True(central.AplicadaConhecida);
        Assert.Equal("Bem vindo portao 9", tela.MensagemPadrao);
        Assert.False(tela.UrnaLigada);
        Assert.Contains(tela.ResumoDaConfiguracao, p => p.Valor == "“Bem vindo portao 9”");
        Assert.Contains(tela.ResumoDaConfiguracao, p => p.Rotulo == urna.Rotulo && p.Valor == "Desligado");
        Assert.Contains(tela.ResumoDaConfiguracao, p => p.Rotulo.StartsWith("Giro · ", StringComparison.Ordinal));
        Assert.StartsWith("A catraca confirmou", tela.TextoDaConfiguracao, StringComparison.Ordinal);

        // Outra versão na catraca: o gêmeo diz que não sabe, e não mostra o salvo como se fosse dela.
        Publicar(new string('b', 64));
        await tela.AtualizarAsync();
        Assert.False(central.AplicadaConhecida);
        Assert.Empty(tela.ResumoDaConfiguracao);
        Assert.Contains("outra versão", tela.TextoDaConfiguracao, StringComparison.Ordinal);
        Assert.Equal("Aproxime o ingresso", tela.MensagemPadrao);

        // Na demonstração, o desenho volta ao padrão do evento.
        tela.ModoAoVivo = false;
        Assert.Contains(tela.ResumoDaConfiguracao, p => p.Rotulo == "Mensagem padrão");
    }

    /// <summary>
    /// O gêmeo é a porta principal: "Abrir no gêmeo" (Gerenciar e Parametrização) leva à mesma
    /// catraca, com o nome já digitado; "Ver em lista" leva de volta à Parametrização. Nada some.
    /// </summary>
    [Fact]
    public async Task Abrir_no_gemeo_e_ver_em_lista_levam_a_mesma_catraca_com_o_nome()
    {
        var janela = new JanelaViewModel(Cliente());
        await janela.Gerenciar.ExecutarAsync(2);
        ((GerenciarCatracaViewModel)janela.TelaAtual).Operador = "Ana Sintética";

        await janela.AbrirNoGemeo.ExecutarAsync(2);
        var gemeo = Assert.IsType<GemeoDigitalViewModel>(janela.TelaAtual);
        await gemeo.AtualizarAsync();
        Assert.Equal((2, 2, "Ana Sintética"), (gemeo.Catraca, gemeo.Central.Catraca, gemeo.Central.Operador));

        await janela.Parametrizar.ExecutarAsync(gemeo.Central.Catraca);
        var lista = Assert.IsType<ParametrizacaoViewModel>(janela.TelaAtual);
        await lista.AtualizarAsync();
        Assert.Equal((2, "Ana Sintética"), (lista.Catraca, lista.Operador));

        // A Parametrização e a Gerenciar continuam existindo.
        Assert.Contains(janela.Telas, t => t is GerenciarCatracaViewModel);
        Assert.NotNull(janela.Parametrizacao);
    }

    /// <summary>
    /// Um erro em qualquer peça bloqueia o Salvar da catraca inteira: nada é gravado pela metade
    /// por causa de um campo inválido que o operador nem está vendo.
    /// </summary>
    [Fact]
    public async Task Gemeo_erro_numa_peca_bloqueia_o_salvar_da_catraca_inteira()
    {
        var tela = await GemeoCarregadoAsync();
        var central = tela.Central;
        central.Operador = "Ana Sintética";

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        var mensagem = Assert.Single(central.CamposDaPeca);
        mensagem.Herda = false;
        mensagem.Valor = "Portao sintetico 3";
        Assert.True(central.Salvar.CanExecute(null));

        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
        var tempo = Assert.Single(central.CamposDaPeca);
        tempo.Herda = false;
        tempo.Valor = "51";
        Assert.Equal("O tempo vai de 1 a 50 segundos.", tempo.Erro);
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        Assert.False(central.Salvar.CanExecute(null));

        tempo.Valor = "6";
        Assert.True(central.Salvar.CanExecute(null));
        var teclado = tela.Giro.Linhas.Single(l => l.Origem == Contracts.Edge.V1.OrigemDoGiro.Teclado);
        teclado.Herda = false;
        teclado.Texto = new string('x', 33);
        Assert.False(central.Salvar.CanExecute(null));

        await central.Salvar.ExecutarAsync();
        Assert.Equal(0, new MapasDeGiro(_banco.Fabrica).RevisoesNoHistorico(1));
        var salvo = await Cliente().ObterConfiguracaoDaCatracaAsync(new ObterConfiguracaoDaCatracaRequest { Inner = 1 });
        Assert.False(salvo.Campos.Single(c => c.Campo == Contracts.Edge.V1.CampoDaCatraca.MensagemPadrao).HasValorDaCatraca);
    }

    /// <summary>
    /// Salvo em outro lugar (a Parametrização): sem nada pendente, o gêmeo recarrega e mostra o
    /// novo; com algo pendente, não apaga o rascunho, mas avisa antes que alguém grave por cima.
    /// </summary>
    [Fact]
    public async Task Gemeo_recarrega_o_que_foi_salvo_na_parametrizacao_e_avisa_se_ha_rascunho()
    {
        var tela = await GemeoCarregadoAsync();
        var lista = new ParametrizacaoViewModel(Cliente(), esperaPeloResultado: TimeSpan.Zero) { Operador = "Bia Sintética" };
        await lista.AtualizarAsync();

        var tempo = CampoDe(lista, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1);
        tempo.Herda = false;
        tempo.Valor = "12";
        await lista.Salvar.ExecutarAsync();
        Assert.Empty(lista.Problemas);

        await tela.AtualizarAsync();
        Assert.Equal("12", tela.Central.Parametrizacao.Campos.Single(c => c.Campo == Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1).ValorSalvo);
        Assert.Contains(tela.Central.Marcas, m => m.Peca is Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor
                                                  && m.Tipo is Desktop.ViewModels.GemeoDigital.TipoDeMarca.DiferenteDoEvento);

        // Rascunho no gêmeo, e outra gravação na Parametrização: o rascunho fica, com o aviso.
        tela.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
        var mensagem = Assert.Single(tela.Central.CamposDaPeca);
        mensagem.Herda = false;
        mensagem.Valor = "Rascunho sintetico";

        tempo = CampoDe(lista, Contracts.Edge.V1.CampoDaCatraca.TempoDoAcionamento1);
        tempo.Valor = "13";
        await lista.Salvar.ExecutarAsync();

        await tela.AtualizarAsync();
        Assert.Single(tela.Central.Mudancas);
        Assert.Equal("Rascunho sintetico", mensagem.Valor);
        Assert.Contains("salva em outro lugar", tela.Central.Mensagem, StringComparison.Ordinal);
    }
}
