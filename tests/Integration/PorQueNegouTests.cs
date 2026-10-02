using Access.Application.Ingressos;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Access.Inteligencia;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;
using Edge.Supervisor;
using Edge.Worker.Bancada;
using Edge.Worker.Operacao;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// <c>NOVO-SIM-NEG-01</c> (Etapa I.2 do docs/36; critério em docs/36-anexos/03 §5.2): o simulador
/// gera uma negação de cada tipo que a catraca produz, pelo caminho de verdade (catraca simulada →
/// worker → decisão → base), e a tela — a ViewModel de Acessos, pelo IPC de verdade — mostra no
/// "Por quê?" a explicação certa de cada uma, com a catraca e a hora certas do uso anterior e
/// nenhum código. A camada inteligente fica <b>desligada</b> o tempo todo: "Por que negou" não
/// depende dela.
/// </summary>
public sealed class PorQueNegouTests : IAsyncLifetime, IDisposable
{
    private const string Online = "simulacao-online";
    private const string Bilheteria = "simulacao-bilheteria";

    private static readonly string[] CodigosDeTeste =
    [
        "1000000001", "0000000101", "0000000102", "9999999999", "3000000001", "3000000002", "3000000003",
        "3000000004", "3000000005",
    ];

    private readonly BancoTemporario _banco = new();
    private readonly InnerSimulator _simulador = new();
    private readonly EstadoDaNuvem _nuvem = new();
    private RepositorioDeIngressos _repositorio = null!;
    private LeiturasSimuladas _leituras = null!;
    private SessaoDeOperacao _sessao = null!;
    private ConducaoDeLeituras _conducao = null!;
    private EdgeControlService _servico = null!;
    private WebApplication? _servidor;
    private string _endereco = string.Empty;
    private string _token = string.Empty;

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

    public async Task InitializeAsync()
    {
        _banco.Migrar();
        _repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _leituras = new LeiturasSimuladas(_banco.Fabrica);
        var agora = DateTimeOffset.UtcNow;

        // Os ingressos do modo simulação (docs/23) e mais um de cada situação que nega.
        var carga = ArquivoDeBancada.Carregar(
            File.ReadAllText(Path.Combine(Raiz(), "installer", "simulacao.exemplo.json")), _repositorio, agora.AddMinutes(-10));
        Assert.Empty(carga.Problemas);

        _repositorio.RegistrarProvedor(new ProvedorDeIngresso("desligado", "Canal desligado", "raw", ""), agora);
        _repositorio.Ingerir(
            [
                new IngressoRecebido(Online, "NEG-CANC", "3000000001", "3000000001", Cancelado: true),
                new IngressoRecebido(Online, "NEG-BLOQ", "3000000002", "3000000002"),
                new IngressoRecebido(Online, "NEG-JAN", "3000000003", "3000000003", ValidoAte: agora.AddHours(-1)),
                new IngressoRecebido(Online, "NEG-TIPO", "3000000004", "3000000004", Categoria: "CORTESIA"),
            ],
            agora.AddMinutes(-10));
        _repositorio.Ingerir([new IngressoRecebido("desligado", "NEG-PROV", "3000000005", "3000000005")], agora.AddMinutes(-10));

        Executar("UPDATE ticket SET status = 'bloqueado' WHERE external_ref = 'NEG-BLOQ';");
        Executar("UPDATE ticket_provider SET enabled = 0 WHERE id = 'desligado';"); // desabilitado depois da carga
        Executar("INSERT INTO ticket_type (code, display_name, sort_order, active, created_at) VALUES ('CORTESIA', 'Cortesia', 1, 0, '2026-10-02T00:00:00Z');");

        // A camada inteligente: desligada (sem a chave). "Por que negou" não depende dela.
        Assert.Null(new LeituraSomenteDaOperacao(_banco.Caminho).ValorDaChave(ChavesDaInteligencia.Ligada));

        _sessao = new SessaoDeOperacao(
            _simulador, [1, 2], ConfiguracaoDeBancada.TopFit4(), new DecisorDeIngresso(_repositorio), _ => { }, _ => { });
        _sessao.Iniciar(3570);

        _conducao = new ConducaoDeLeituras(
            _simulador,
            catracas => [.. _leituras.Retirar(catracas, DateTimeOffset.UtcNow)
                .Select(l => new LeituraParaSimular(l.Inner, l.Codigo, l.NaUrna, l.Girar))],
            [1, 2],
            intervalo: TimeSpan.Zero);

        _nuvem.Configurada = true;
        _nuvem.RegistrarSucesso(agora.AddMinutes(-3));

        _servico = new EdgeControlService(
            new WorkerSupervisor([new Catracas()]),
            consultas: new ConsultasDaOperacao(_banco.Fabrica),
            nuvem: _nuvem,
            simulacao: _leituras);

        Voltas(15);

        _token = InterceptadorDeToken.GerarToken();
        _endereco = TransporteLocal.EnderecoPadrao($"porque-{Guid.NewGuid():N}");
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

    public void Dispose()
    {
        _simulador.Dispose();
        _banco.Dispose();
    }

    [Fact]
    public async Task NOVO_SIM_NEG_01_cada_negacao_do_simulador_tem_a_explicacao_certa_na_tela()
    {
        // Uma negação de cada tipo que a catraca produz hoje (VendaAnteriorNaoUsada só existe na
        // venda do balcão, nunca na catraca; a função pura cobre o texto dela).
        await Passar(1, "1000000001");                     // libera e gira (uso anterior)
        var esperado = new List<(string Codigo, int Inner, string Motivo)>
        {
            ("1000000001", 2, nameof(MotivoDoUso.UsosEsgotados)),
            ("9999999999", 2, nameof(MotivoDoUso.Desconhecido)),
            ("0000000102", 1, nameof(MotivoDoUso.ForaDaUrna)),
            ("3000000001", 1, nameof(MotivoDoUso.Cancelado)),
            ("3000000002", 1, nameof(MotivoDoUso.Bloqueado)),
            ("3000000003", 1, nameof(MotivoDoUso.ForaDaJanela)),
            ("3000000004", 1, nameof(MotivoDoUso.TipoInativo)),
            ("3000000005", 1, nameof(MotivoDoUso.ProvedorDesabilitado)),
        };

        foreach (var (codigo, inner, _) in esperado)
        {
            await Passar(inner, codigo);
        }

        // O cartão da bilheteria: entra na urna, é revendido na hora e volta antes de 4 min.
        await Passar(2, "0000000101", naUrna: true);
        Assert.Equal(RepositorioDeIngressos.ResultadoDaVenda.Vendido, _repositorio.VenderNoBalcao(Bilheteria, "0000000101", "inteira", DateTimeOffset.UtcNow));
        await Passar(2, "0000000101", naUrna: true);
        esperado.Add(("0000000101", 2, nameof(MotivoDoUso.EmIntervaloDeReuso)));

        // A tela de Acessos, só os negados, pelo IPC de verdade.
        var acessos = new AcessosViewModel(Cliente()) { Resultado = 2 };
        await acessos.AtualizarAsync();
        Assert.Equal(esperado.Count, acessos.Linhas.Count);
        Assert.All(acessos.Linhas, l => Assert.True(l.PodeExplicar));

        // Do mais recente ao mais antigo, como a lista mostra.
        esperado.Reverse();
        var nomesDosMotivos = Enum.GetNames<MotivoDoUso>();

        for (var i = 0; i < esperado.Count; i++)
        {
            var (codigo, inner, motivo) = esperado[i];
            var linha = acessos.Linhas[i];
            await acessos.PorQue.Abrir.ExecutarAsync(linha);
            var painel = acessos.PorQue;

            Assert.True(painel.Aberto);
            Assert.Equal(Sinal.Problema, painel.Sinal);
            Assert.StartsWith($"× Negado às {linha.Hora} · Catraca {inner:D2}", painel.Cabecalho, StringComparison.Ordinal);

            // O texto é o da função pura para aquele motivo: "o que dizer" sempre; "o que fazer"
            // quando não depende do uso anterior (o do já usado é conferido abaixo, com o contexto).
            var daFuncao = PorQueNegou.ExplicarNegativa(motivo, new ContextoDaNegativa(DateTimeOffset.UtcNow, inner));
            Assert.Equal(daFuncao.OQueDizer, painel.OQueDizer);
            if (motivo != nameof(MotivoDoUso.UsosEsgotados))
            {
                Assert.Equal(daFuncao.OQueFazer, painel.OQueFazer);
            }
            Assert.False(string.IsNullOrWhiteSpace(painel.OQueAconteceu));

            // Nenhum código, nem o desta linha nem outro, nem o nome do motivo em código.
            var tudo = $"{painel.Cabecalho} {painel.OQueAconteceu} {painel.OQueDizer} {painel.OQueFazer}";
            Assert.All(CodigosDeTeste, c => Assert.DoesNotContain(c, tudo, StringComparison.Ordinal));
            Assert.All(nomesDosMotivos, n => Assert.DoesNotContain(n, tudo, StringComparison.Ordinal));
            _ = codigo;
        }

        // O contexto: o ingresso já usado diz a catraca e a hora do uso anterior, com giro.
        var usado = acessos.Linhas[esperado.FindIndex(e => e.Motivo == nameof(MotivoDoUso.UsosEsgotados))];
        var anterior = (await _servico.ListarAcessos(new ListarAcessosRequest { Inner = 1, Resultado = FiltroDeResultado.Liberados }, null!))
            .Acessos.Single(a => a.Inner == 1);
        await acessos.PorQue.Abrir.ExecutarAsync(usado);
        Assert.Equal(
            $"Este ingresso já entrou às {Textos.Hora(anterior.RecebidoEm.ToDateTimeOffset())} pela catraca 01, com giro confirmado pelo sensor.",
            acessos.PorQue.OQueAconteceu);

        // O cartão no intervalo diz quando volta a valer (4 min depois do uso anterior).
        var reuso = acessos.Linhas[esperado.FindIndex(e => e.Motivo == nameof(MotivoDoUso.EmIntervaloDeReuso))];
        await acessos.PorQue.Abrir.ExecutarAsync(reuso);
        Assert.Contains("pela catraca 02 e só volta a valer às", acessos.PorQue.OQueAconteceu, StringComparison.Ordinal);

        // O desconhecido diz a idade da base (nuvem configurada, sincronizada há 3 min).
        var desconhecido = acessos.Linhas[esperado.FindIndex(e => e.Motivo == nameof(MotivoDoUso.Desconhecido))];
        await acessos.PorQue.Abrir.ExecutarAsync(desconhecido);
        Assert.Equal("Este código não está na base deste PC. A base recebeu a última atualização da nuvem há 3 min.", acessos.PorQue.OQueAconteceu);

        // Fora da urna diz onde foi lido, sem termo do SDK.
        var foraDaUrna = acessos.Linhas[esperado.FindIndex(e => e.Motivo == nameof(MotivoDoUso.ForaDaUrna))];
        await acessos.PorQue.Abrir.ExecutarAsync(foraDaUrna);
        Assert.EndsWith("· leitor da frente", acessos.PorQue.Cabecalho, StringComparison.Ordinal);

        await acessos.PorQue.Fechar.ExecutarAsync();
        Assert.False(acessos.PorQue.Aberto);
    }

    [Fact]
    public async Task Painel_ao_vivo_tambem_explica_e_liberado_nao_tem_por_que()
    {
        await Passar(1, "1000000001");
        await Passar(1, "1000000001");

        var resposta = await _servico.ListarAcessos(new ListarAcessosRequest { Inner = 1 }, null!);
        var painel = new PainelAoVivoViewModel(Cliente());
        foreach (var evento in resposta.Acessos.Reverse())
        {
            painel.Acrescentar(LinhaDeAcesso.De(evento));
        }

        var negada = painel.UltimosAcessos[0];
        var liberada = painel.UltimosAcessos[1];
        Assert.True(negada.PodeExplicar);
        Assert.False(liberada.PodeExplicar);

        await painel.PorQue.Abrir.ExecutarAsync(negada);
        Assert.Equal("Este ingresso já foi usado. Procure o atendimento, por favor.", painel.PorQue.OQueDizer);
    }

    [Fact]
    public async Task Tentativa_que_nao_existe_e_servico_fora_viram_texto_e_nao_erro()
    {
        var acessos = new AcessosViewModel(Cliente());
        await acessos.PorQue.ExplicarAsync(new LinhaDeAcesso("", 1, "", false, false, "", "", Sinal.Problema, Guid.NewGuid().ToString()));
        Assert.Equal("Esta tentativa não está na base deste PC.", acessos.PorQue.OQueAconteceu);

        var fora = new AcessosViewModel(Cliente(TransporteLocal.EnderecoPadrao($"fora-{Guid.NewGuid():N}")));
        await fora.PorQue.ExplicarAsync(new LinhaDeAcesso("", 1, "", false, false, "", "", Sinal.Problema, Guid.NewGuid().ToString()));
        Assert.True(fora.PorQue.Aberto);
        Assert.Equal("Sem resposta do serviço local", fora.PorQue.Cabecalho);
    }

    [Fact]
    public async Task Principais_motivos_de_negacao_trazem_o_que_fazer_e_a_parte_dos_negados()
    {
        await Passar(1, "9999999999");
        await Passar(1, "9999999999");
        await Passar(1, "9999999999");
        await Passar(1, "0000000102");

        var contas = await _servico.ObterPrestacaoDeContas(
            new ObterPrestacaoDeContasRequest
            {
                Desde = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1)),
                Ate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow.AddMinutes(1)),
            },
            null!);

        Assert.Equal(4, contas.Negados);
        var primeiro = contas.Negativas[0];
        Assert.Equal(nameof(MotivoDoUso.Desconhecido), primeiro.Motivo);
        Assert.Equal(75, primeiro.PercentualDosNegados);
        Assert.Equal(PorQueNegou.ExplicarNegativa(primeiro.Motivo, new ContextoDaNegativa(DateTimeOffset.UtcNow, 0)).OQueFazer, primeiro.OQueFazer);
        Assert.Equal(25, contas.Negativas[1].PercentualDosNegados);
    }

    // ------------------------------------------------------------------ apoio

    private EdgeControl.EdgeControlClient Cliente(string? endereco = null) => new(TransporteLocal.CriarCanal(endereco ?? _endereco, _token));

    private async Task Passar(int inner, string codigo, bool naUrna = false)
    {
        var r = await _servico.SimularLeitura(new SimularLeituraRequest { Inner = inner, Codigo = codigo, NaUrna = naUrna, Girar = true }, null!);
        Assert.True(r.Aceita, r.Mensagem);
        Voltas(30);
    }

    private void Voltas(int quantas)
    {
        for (var i = 0; i < quantas; i++)
        {
            _sessao.UmaVolta();
            _conducao.UmaVolta();
        }
    }

    private void Executar(string sql)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        Assert.Equal(1, comando.ExecuteNonQuery());
    }

    private static string Raiz()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, "ConexaoTopdata.slnx")))
        {
            pasta = pasta.Parent;
        }

        return pasta?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
