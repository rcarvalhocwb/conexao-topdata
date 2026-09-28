using Access.Application.Ingressos;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Supervisor.Instalacao;
using Edge.Worker.Bancada;
using Edge.Worker.Operacao;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// O modo simulação ponta a ponta, menos a janela: o painel pede a leitura pelo serviço, o
/// worker a entrega à catraca simulada, e daí em diante é o caminho de verdade.
/// </summary>
public sealed class ModoSimulacaoTests : IDisposable
{
    private readonly BancoTemporario _banco = new();
    private readonly InnerSimulator _simulador = new();
    private readonly RepositorioDeIngressos _repositorio;
    private readonly LeiturasSimuladas _leituras;
    private readonly SessaoDeOperacao _sessao;
    private readonly ConducaoDeLeituras _conducao;
    private readonly EdgeControlService _servico;

    private sealed class Catracas : IWorkerHost
    {
        public string Nome => "catracas";

        public int Porta => 3570;

        public IReadOnlyList<int> Inners { get; } = [1, 2];

        public bool EstaVivo => true;

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar()
        {
        }

        public void Matar()
        {
        }

        public void Dispose()
        {
        }
    }

    public ModoSimulacaoTests()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _leituras = new LeiturasSimuladas(_banco.Fabrica);

        // Os mesmos ingressos que o serviço carrega em modo simulação.
        var carga = ArquivoDeBancada.Carregar(
            File.ReadAllText(Path.Combine(Raiz(), "installer", "simulacao.exemplo.json")),
            _repositorio,
            DateTimeOffset.UtcNow.AddMinutes(-10));
        Assert.Empty(carga.Problemas);

        _sessao = new SessaoDeOperacao(
            _simulador, [1, 2], ConfiguracaoDeBancada.TopFit4(), new DecisorDeIngresso(_repositorio),
            _ => { }, _ => { });
        _sessao.Iniciar(3570);

        _conducao = new ConducaoDeLeituras(
            _simulador,
            catracas => [.. _leituras.Retirar(catracas, DateTimeOffset.UtcNow)
                .Select(l => new LeituraParaSimular(l.Inner, l.Codigo, l.NaUrna, l.Girar))],
            [1, 2],
            intervalo: TimeSpan.Zero);

        _servico = new EdgeControlService(
            new WorkerSupervisor([new Catracas()]),
            consultas: new ConsultasDaOperacao(_banco.Fabrica),
            simulacao: _leituras);

        Voltas(15);
    }

    public void Dispose()
    {
        _simulador.Dispose();
        _banco.Dispose();
    }

    private void Voltas(int quantas = 30)
    {
        for (var i = 0; i < quantas; i++)
        {
            _sessao.UmaVolta();
            _conducao.UmaVolta();
        }
    }

    private async Task<SimularLeituraResponse> Passar(int inner, string codigo, bool naUrna = false, bool girar = true)
    {
        var resposta = await _servico.SimularLeitura(
            new SimularLeituraRequest { Inner = inner, Codigo = codigo, NaUrna = naUrna, Girar = girar }, null!);
        Voltas();
        return resposta;
    }

    private async Task<EventoDeAcesso> Ultimo(int inner) =>
        (await _servico.ListarAcessos(new ListarAcessosRequest { Inner = inner, Limite = 1 }, null!)).Acessos.Single();

    [Fact]
    public async Task QR_passado_no_simulador_libera_gira_e_fica_registrado()
    {
        Assert.True((await Passar(1, "1000000001")).Aceita);

        var acesso = await Ultimo(1);
        Assert.Equal(ResultadoDoAcesso.Permitido, acesso.Resultado);
        Assert.True(acesso.PassagemConfirmada);
        Assert.Equal("inteira", acesso.Categoria);
        Assert.NotEmpty(_simulador.Dispositivo(1).LiberacoesPedidas);

        // Segunda vez: já utilizado, e a catraca simulada não libera de novo.
        await Passar(1, "1000000001");
        Assert.Equal("Negado · já utilizado", (await Ultimo(1)).MensagemAoOperador);
    }

    [Fact]
    public async Task Cartao_da_bilheteria_so_passa_na_urna()
    {
        await Passar(2, "0000000102");
        Assert.Equal("Negado · use a fenda da urna", (await Ultimo(2)).MensagemAoOperador);

        await Passar(2, "0000000102", naUrna: true);
        var acesso = await Ultimo(2);
        Assert.Equal(ResultadoDoAcesso.Permitido, acesso.Resultado);
        Assert.Equal("meia", acesso.Categoria);
    }

    [Fact]
    public async Task Liberado_sem_girar_fica_sem_prova_de_passagem()
    {
        await Passar(1, "1000000002", girar: false);

        var acesso = await Ultimo(1);
        Assert.Equal(ResultadoDoAcesso.Permitido, acesso.Resultado);
        Assert.False(acesso.PassagemConfirmada);
    }

    [Fact]
    public async Task Codigo_desconhecido_e_negado()
    {
        await Passar(1, "9999999999");
        Assert.Equal("Negado · código não cadastrado", (await Ultimo(1)).MensagemAoOperador);
    }

    [Fact]
    public async Task Fora_do_modo_simulacao_o_servico_recusa_passar_codigo()
    {
        var servicoReal = new EdgeControlService(new WorkerSupervisor([new Catracas()]));

        var resposta = await servicoReal.SimularLeitura(new SimularLeituraRequest { Inner = 1, Codigo = "1000000001" }, null!);

        Assert.False(resposta.Aceita);
        Assert.Contains("não está ligado", resposta.Mensagem, StringComparison.Ordinal);
        Assert.False((await servicoReal.ObterEstado(new ObterEstadoRequest(), null!)).Simulacao);
    }

    [Fact]
    public async Task Catraca_que_nao_existe_e_recusada_e_o_painel_sabe_que_e_simulacao()
    {
        var resposta = await _servico.SimularLeitura(new SimularLeituraRequest { Inner = 9, Codigo = "1000000001" }, null!);
        Assert.False(resposta.Aceita);

        var estado = await _servico.ObterEstado(new ObterEstadoRequest(), null!);
        Assert.True(estado.Simulacao);
        Assert.StartsWith("MODO SIMULAÇÃO", Desktop.ViewModels.EstadoDoPainel.De(estado, DateTimeOffset.UtcNow).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Leitura_pedida_e_retirada_uma_vez_so_e_so_pelas_catracas_do_worker()
    {
        _leituras.Pedir(1, "1000000001", naUrna: false, girar: true, DateTimeOffset.UtcNow);
        _leituras.Pedir(7, "1000000002", naUrna: false, girar: true, DateTimeOffset.UtcNow);

        Assert.Single(_leituras.Retirar([1, 2], DateTimeOffset.UtcNow));
        Assert.Empty(_leituras.Retirar([1, 2], DateTimeOffset.UtcNow));
        Assert.Single(_leituras.Retirar([7], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Assistente_grava_o_modo_simulacao_e_dispensa_o_SDK()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));
        var worker = Path.Combine(pasta, "Worker", "Edge.Worker.X86.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(worker)!);
        File.WriteAllText(worker, "x");
        var arquivo = Path.Combine(pasta, "workers.json");

        try
        {
            AssistenteDeConfiguracao.Gravar(
                new DadosDaInstalacao { Catracas = [new(1, "Entrada 1")], Simulacao = true },
                arquivo, worker, new CofreEmMemoria());

            Assert.True(ConfiguracaoDoSupervisor.Ler(arquivo).Simulacao);
            Assert.True(AssistenteDeConfiguracao.Carregar(arquivo).Simulacao);

            var ambiente = AssistenteDeConfiguracao.VerificarAmbiente(_ => false, true, pasta, simulacao: true);
            Assert.Equal(true, ambiente[0].Ok);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    private static string Raiz()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        return raiz.FullName;
    }
}
