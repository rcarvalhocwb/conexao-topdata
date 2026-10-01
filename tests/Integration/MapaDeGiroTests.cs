using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Worker.Operacao;
using Microsoft.Data.Sqlite;
using Simulator;
using OrigemDoGiro = Access.Application.Devices.OrigemDoGiro;
using OrigemDoGiroNoContrato = Contracts.Edge.V1.OrigemDoGiro;

namespace Integration.Tests;

/// <summary>
/// Mapa de giro (decisão D9 do dono do produto, docs/34 §9) contra a base de verdade: a 017,
/// o laço com o simulador lendo o mapa pela mesma leitura do worker x86, a contagem por rótulo
/// nos totais e na prestação de contas, e os RPCs.
/// </summary>
/// <remarks>Códigos sintéticos (<c>1000000001</c>); nenhum equipamento real.</remarks>
public sealed class MapaDeGiroTests : IDisposable
{
    private const string Qr = "1000000001";
    private const string Qr2 = "1000000002";

    private static readonly DateTimeOffset Agora = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();

    public MapaDeGiroTests() => _banco.Migrar();

    public void Dispose() => _banco.Dispose();

    private static readonly MapaDeGiro UrnaPelaSaidaContandoEntrada = MapaDeGiro.Vazio
        .Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Entrada));

    // ---------------------------------------------------------------------------- 017 e repositório

    [Fact]
    public void Gravar_e_ler_o_mapa_com_quem_e_quando()
    {
        var mapas = new MapasDeGiro(_banco.Fabrica);
        var mapa = UrnaPelaSaidaContandoEntrada
            .Com(OrigemDoGiro.LiberacaoManual, new RegraDeGiro(null, SentidoContado.Saida, "Volte sempre"));

        Assert.Empty(mapas.Gravar(1, mapa, Agora, "Ana Sintética"));

        var lido = mapas.Ler(1);
        Assert.Equal(mapa, lido.Mapa);
        Assert.Equal("Ana Sintética", lido.AlteradoPor);
        Assert.Equal(Agora, lido.AlteradoEm);
        Assert.Empty(lido.Problemas);

        // Sem linha: vazio, sem problema.
        Assert.True(mapas.Ler(2).Mapa.EstaVazio);
        Assert.Null(mapas.Ler(2).AlteradoPor);
    }

    [Fact]
    public void Voltar_ao_padrao_grava_nulo_e_o_historico_guarda_cada_revisao()
    {
        var mapas = new MapasDeGiro(_banco.Fabrica);
        Assert.Empty(mapas.Gravar(1, UrnaPelaSaidaContandoEntrada, Agora, "Ana"));
        Assert.Empty(mapas.Gravar(1, UrnaPelaSaidaContandoEntrada, Agora.AddMinutes(1), "Ana")); // igual: nada muda
        Assert.Empty(mapas.Gravar(1, MapaDeGiro.Vazio, Agora.AddMinutes(2), "Bruno"));

        Assert.True(mapas.Ler(1).Mapa.EstaVazio);

        // 4 origens na primeira gravação + a urna voltando ao padrão.
        Assert.Equal(5, mapas.RevisoesNoHistorico(1));
    }

    [Theory]
    [InlineData("UPDATE turn_map_rule_history SET counted_as = 'saida';")]
    [InlineData("DELETE FROM turn_map_rule_history;")]
    [InlineData("DELETE FROM turn_map_rule;")]
    [InlineData("UPDATE turn_check SET result = 'ao_contrario';")]
    [InlineData("DELETE FROM turn_check;")]
    [InlineData("UPDATE turn_map_rule SET counted_as = 'saida' WHERE origin = 'leitor2';")]
    public void Historico_e_conferencia_sao_so_insert(string sql)
    {
        var mapas = new MapasDeGiro(_banco.Fabrica);
        Assert.Empty(mapas.Gravar(1, UrnaPelaSaidaContandoEntrada, Agora, "Ana"));
        Assert.Empty(mapas.RegistrarConferencia(1, FuncaoDeLiberacao.Saida, comoEsperado: true, Agora, "Ana"));

        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        Assert.Throws<SqliteException>(() => comando.ExecuteNonQuery());
    }

    [Fact]
    public void A_base_recusa_regra_sem_rotulo_e_funcao_fora_da_lista()
    {
        using var conexao = _banco.Fabrica.Abrir();

        foreach (var sql in new[]
        {
            "INSERT INTO turn_map_rule VALUES (1, 'leitor2', 'Saida', NULL, NULL, 1, 'Ana', '2026-10-01T12:00:00Z');",
            "INSERT INTO turn_map_rule VALUES (1, 'leitor2', 'DoisSentidos', 'entrada', NULL, 1, 'Ana', '2026-10-01T12:00:00Z');",
            "INSERT INTO turn_map_rule VALUES (1, 'portao', NULL, 'entrada', NULL, 1, 'Ana', '2026-10-01T12:00:00Z');",
        })
        {
            using var comando = conexao.CreateCommand();
            comando.CommandText = sql;
            Assert.Throws<SqliteException>(() => comando.ExecuteNonQuery());
        }
    }

    [Fact]
    public void Gravar_com_problema_nao_grava_nada()
    {
        var mapas = new MapasDeGiro(_banco.Fabrica);

        Assert.NotEmpty(mapas.Gravar(1, UrnaPelaSaidaContandoEntrada, Agora, " "));
        Assert.NotEmpty(mapas.Gravar(1, MapaDeGiro.Vazio.Com(
            OrigemDoGiro.Leitor1, new RegraDeGiro(null, SentidoContado.Entrada, new string('x', 33))), Agora, "Ana"));
        Assert.Equal(0, mapas.RevisoesNoHistorico(1));
    }

    [Fact]
    public void A_ultima_conferencia_de_cada_funcao_e_a_que_vale()
    {
        var mapas = new MapasDeGiro(_banco.Fabrica);
        Assert.Empty(mapas.RegistrarConferencia(1, FuncaoDeLiberacao.Saida, comoEsperado: false, Agora, "Ana"));
        Assert.Empty(mapas.RegistrarConferencia(1, FuncaoDeLiberacao.Saida, comoEsperado: true, Agora.AddMinutes(3), "Bruno"));
        Assert.NotEmpty(mapas.RegistrarConferencia(1, FuncaoDeLiberacao.Saida, comoEsperado: true, Agora, "x"));

        var conferida = Assert.Single(mapas.Conferencias(1)).Value;
        Assert.True(conferida.ComoEsperado);
        Assert.Equal("Bruno", conferida.Por);
        Assert.Empty(mapas.Conferencias(2));
    }

    // ------------------------------------------------------------------- laço + simulador + base

    private sealed class Operando : IDisposable
    {
        public Operando(BancoTemporario banco)
        {
            Repositorio = new RepositorioDeIngressos(banco.Fabrica);
            Repositorio.RegistrarProvedor(new ProvedorDeIngresso("zet", "Zet", "qr-catraca4", ""), DateTimeOffset.UtcNow);
            Repositorio.Ingerir(
                [new IngressoRecebido("zet", "T1", Qr, Qr, Categoria: "inteira"), new IngressoRecebido("zet", "T2", Qr2, Qr2, Categoria: "meia")],
                DateTimeOffset.UtcNow.AddMinutes(-5));

            Operacao = new Operacao(banco.Fabrica);

            // A mesma leitura que o worker x86 faz na subida: fábrica → evento → camada → mapa.
            var porCatraca = new ConfiguracaoPorCatraca(banco.Fabrica);
            Sessao = new SessaoDeOperacao(
                Simulador,
                [1],
                inner => porCatraca.NaSubida(inner, new ConfiguracaoDaOperacao()).Configuracao,
                new DecisorDeIngresso(Repositorio),
                _ => { },
                _ => { });
            Sessao.Iniciar(3571);
            Voltas(12);
        }

        public InnerSimulator Simulador { get; } = new();

        public RepositorioDeIngressos Repositorio { get; }

        public Operacao Operacao { get; }

        public SessaoDeOperacao Sessao { get; }

        public void Voltas(int quantas = 10)
        {
            for (var i = 0; i < quantas; i++)
            {
                Sessao.UmaVolta();
            }
        }

        public void PassarEGirar(KnownEventOrigin origem, string codigo, byte complementoDoGiro)
        {
            Simulador.Dispositivo(1).Roteirizar(
                new ScriptedEvent(EventOrigin.From(origem), codigo),
                new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado), Complemento: complementoDoGiro));
            Voltas();
        }

        public void Dispose() => Simulador.Dispose();
    }

    private (string? ContaComo, string? Funcao, long? Complemento) Linha(string codigo)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            "SELECT counted_as, release_function, turn_complement FROM ticket_use_attempt WHERE qr_normalized = $qr AND outcome = 'consumido';";
        comando.Parameters.AddWithValue("$qr", codigo);
        using var leitor = comando.ExecuteReader();
        Assert.True(leitor.Read());
        return (
            leitor.IsDBNull(0) ? null : leitor.GetString(0),
            leitor.IsDBNull(1) ? null : leitor.GetString(1),
            leitor.IsDBNull(2) ? null : leitor.GetInt64(2));
    }

    /// <summary>
    /// O caso do dono do produto: cartão na urna, giro pela função de saída (EI-042), contado
    /// como entrada. O bruto — a função chamada e o complemento da origem 6 — fica ao lado.
    /// </summary>
    [Fact]
    public void Urna_com_o_mapa_libera_pela_saida_e_o_giro_e_gravado_como_entrada_com_o_bruto()
    {
        Assert.Empty(new MapasDeGiro(_banco.Fabrica).Gravar(1, UrnaPelaSaidaContandoEntrada, Agora, "Ana"));
        using var o = new Operando(_banco);

        o.PassarEGirar(KnownEventOrigin.Leitor2, Qr, complementoDoGiro: 1);

        Assert.Equal(1, o.Simulador.Dispositivo(1).LiberacoesPedidas.GetValueOrDefault(GateDirection.Saida));
        Assert.Equal(0, o.Simulador.Dispositivo(1).LiberacoesPedidas.GetValueOrDefault(GateDirection.Entrada));
        Assert.Equal(("entrada", "Saida", 1L), Linha(Qr));

        var resumo = o.Operacao.Resumir(DateTimeOffset.UtcNow);
        Assert.Equal((1L, 1L, 0L), (resumo.Giros, resumo.Entradas, resumo.Saidas));

        var tentativa = Assert.Single(o.Operacao.TentativasDepoisDe(0));
        Assert.Equal("entrada", tentativa.ContaComo);
        Assert.Equal("Entrada liberada · inteira", AcompanhamentoDaOperacao.MensagemPara(tentativa));
        Assert.Equal("entrada", AcompanhamentoDaOperacao.Converter(tentativa).ContaComo);
    }

    /// <summary>Leitura contada como saída entra nas saídas, nos totais e na prestação de contas.</summary>
    [Fact]
    public void Origem_que_conta_como_saida_vai_para_as_saidas_nos_totais_e_na_prestacao_de_contas()
    {
        var mapa = MapaDeGiro.Vazio.Com(OrigemDoGiro.Leitor2, new RegraDeGiro(FuncaoDeLiberacao.Saida, SentidoContado.Saida));
        Assert.Empty(new MapasDeGiro(_banco.Fabrica).Gravar(1, mapa, Agora, "Ana"));
        using var o = new Operando(_banco);

        o.PassarEGirar(KnownEventOrigin.QrCode, Qr, complementoDoGiro: 0);   // frente: padrão, entrada
        o.PassarEGirar(KnownEventOrigin.Leitor2, Qr2, complementoDoGiro: 1); // urna: saída

        Assert.Equal((null, "Entrada", 0L), Linha(Qr));
        Assert.Equal(("saida", "Saida", 1L), Linha(Qr2));

        var resumo = o.Operacao.Resumir(DateTimeOffset.UtcNow);
        Assert.Equal((2L, 1L, 1L), (resumo.Giros, resumo.Entradas, resumo.Saidas));

        var contas = new ConsultasDaOperacao(_banco.Fabrica).Contas(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal((1L, 1L), (contas.Entradas, contas.Saidas));
        var catraca = Assert.Single(contas.PorCatraca);
        Assert.Equal((2L, 1L, 1L), (catraca.Giros, catraca.Entradas, catraca.Saidas));
        Assert.Equal("Saída liberada · meia", AcompanhamentoDaOperacao.MensagemPara(o.Operacao.TentativasDepoisDe(0)[1]));
    }

    /// <summary>Mapa vazio: nada muda — EI-041, sem rótulo gravado, "Liberado" no painel.</summary>
    [Fact]
    public void Sem_mapa_tudo_como_hoje()
    {
        using var o = new Operando(_banco);

        o.PassarEGirar(KnownEventOrigin.Leitor2, Qr, complementoDoGiro: 0);

        Assert.Equal(1, o.Simulador.Dispositivo(1).LiberacoesPedidas.GetValueOrDefault(GateDirection.Entrada));
        Assert.Equal((null, "Entrada", 0L), Linha(Qr));
        var tentativa = Assert.Single(o.Operacao.TentativasDepoisDe(0));
        Assert.Equal("Liberado · inteira", AcompanhamentoDaOperacao.MensagemPara(tentativa));
        var resumo = o.Operacao.Resumir(DateTimeOffset.UtcNow);
        Assert.Equal((1L, 1L, 0L), (resumo.Giros, resumo.Entradas, resumo.Saidas));
    }

    /// <summary>Falha de base nega: o mapa não abre caminho para liberar sem gravar a tentativa.</summary>
    [Fact]
    public void Base_que_recusa_a_tentativa_nega_mesmo_com_o_mapa()
    {
        Assert.Empty(new MapasDeGiro(_banco.Fabrica).Gravar(1, UrnaPelaSaidaContandoEntrada, Agora, "Ana"));
        var decisor = new DecisorDeIngresso(new ValidadorQueFalha());
        var giro = GatePhysicalProfile.Padrao.Resolver(OrigemDoGiro.Leitor2);

        var leitura = DeviceEvent.Create(
            new DeviceEventKey("inner-1", "boot-sintetico", 1), EventOrigin.From(KnownEventOrigin.Leitor2), Agora, "teste", rawCardData: Qr);

        Assert.False(decisor.Decidir(leitura, giro).ShouldRelease);
    }

    private sealed class ValidadorQueFalha : IValidadorDeIngressos
    {
        public (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
            string qrNormalizado, string gateId, string deviceId, DateTimeOffset agora, KnownEventOrigin? leitor, int? origemBruta) =>
            throw new InvalidOperationException("base ocupada (sintético)");

        public void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em)
        {
        }
    }

    // ---------------------------------------------------------------------------------- RPCs

    private sealed class WorkerFalso(params int[] inners) : IWorkerHost
    {
        public string Nome => "setor-a";

        public int Porta => 3570;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    private EdgeControlService Servico()
    {
        var supervisor = new WorkerSupervisor([new WorkerFalso(1, 2)]);
        supervisor.Iniciar();
        return new EdgeControlService(
            supervisor,
            relogio: () => Agora,
            operacao: new Operacao(_banco.Fabrica),
            configuracoes: new ConfiguracoesDaBorda(_banco.Fabrica),
            configuracoesDasCatracas: new ConfiguracoesDasCatracas(_banco.Fabrica),
            configuracaoPorCatraca: new ConfiguracaoPorCatraca(_banco.Fabrica),
            mapasDeGiro: new MapasDeGiro(_banco.Fabrica));
    }

    private static MapaDeGiroDaCatraca Obter(EdgeControlService servico) =>
        servico.ObterMapaDeGiro(new ObterMapaDeGiroRequest { Inner = 1 }, null!).Result;

    private static GravarMapaDeGiroResponse Gravar(EdgeControlService servico, GravarMapaDeGiroRequest pedido) =>
        servico.GravarMapaDeGiro(pedido, null!).Result;

    private static RegistrarConferenciaDoGiroResponse Conferir(EdgeControlService servico, RegistrarConferenciaDoGiroRequest pedido) =>
        servico.RegistrarConferenciaDoGiro(pedido, null!).Result;

    [Fact]
    public void Obter_sem_mapa_devolve_as_quatro_origens_no_padrao_nao_conferidas()
    {
        var mapa = Obter(Servico());

        Assert.Empty(mapa.Problemas);
        Assert.Equal(
            [OrigemDoGiroNoContrato.Leitor1, OrigemDoGiroNoContrato.Leitor2, OrigemDoGiroNoContrato.Teclado, OrigemDoGiroNoContrato.LiberacaoManual],
            mapa.Regras.Select(r => r.Origem));
        Assert.All(mapa.Regras, r =>
        {
            Assert.False(r.Definida);
            Assert.Equal(FuncaoDoGiro.Entrada, r.Funcao);
            Assert.Equal(ContagemDoGiro.Entrada, r.ContaComo);
            Assert.Equal("Entrada liberada", r.Texto);
            Assert.Equal(SituacaoDaConferencia.NaoConferida, r.Conferencia);
        });
        Assert.Contains("teclado", mapa.Regras[2].Aviso, StringComparison.Ordinal);
        Assert.Equal(FuncaoDoGiro.Entrada, mapa.FuncaoDoPerfil);
        Assert.Equal(64, mapa.VersaoSalva.Length);
    }

    [Fact]
    public void Gravar_pela_tela_muda_a_versao_do_salvo_e_registra_quem()
    {
        var servico = Servico();
        var antes = Obter(servico).VersaoSalva;

        var semNome = Gravar(servico, Pedido(""));
        Assert.False(semNome.Gravado);
        Assert.NotEmpty(semNome.Problemas);

        var gravado = Gravar(servico, Pedido("Ana Sintética"));
        Assert.True(gravado.Gravado, string.Join(" | ", gravado.Problemas));
        Assert.NotEqual(antes, gravado.VersaoSalva);

        var mapa = Obter(servico);
        var urna = mapa.Regras.Single(r => r.Origem == OrigemDoGiroNoContrato.Leitor2);
        Assert.True(urna.Definida);
        Assert.Equal((FuncaoDoGiro.Saida, ContagemDoGiro.Entrada, "Bem-vindo", true), (urna.Funcao, urna.ContaComo, urna.Texto, urna.TextoPersonalizado));
        Assert.Equal("Ana Sintética", mapa.AlteradoPor);
        Assert.False(mapa.Regras.Single(r => r.Origem == OrigemDoGiroNoContrato.Leitor1).Definida);
        Assert.Equal(gravado.VersaoSalva, mapa.VersaoSalva);

        static GravarMapaDeGiroRequest Pedido(string quem) => new()
        {
            Inner = 1,
            Operador = quem,
            Regras =
            {
                new RegraDeGiroPedida { Origem = OrigemDoGiroNoContrato.Leitor1, Herdar = true },
                new RegraDeGiroPedida
                {
                    Origem = OrigemDoGiroNoContrato.Leitor2, Funcao = FuncaoDoGiro.Saida, ContaComo = ContagemDoGiro.Entrada, Texto = "Bem-vindo",
                },
            },
        };
    }

    [Fact]
    public void Gravar_recusa_origem_repetida_e_regra_sem_rotulo()
    {
        var servico = Servico();

        var repetida = Gravar(servico, new GravarMapaDeGiroRequest
        {
            Inner = 1,
            Operador = "Ana",
            Regras =
            {
                new RegraDeGiroPedida { Origem = OrigemDoGiroNoContrato.Leitor2, ContaComo = ContagemDoGiro.Entrada },
                new RegraDeGiroPedida { Origem = OrigemDoGiroNoContrato.Leitor2, ContaComo = ContagemDoGiro.Saida },
            },
        });
        var semRotulo = Gravar(servico, new GravarMapaDeGiroRequest
        {
            Inner = 1,
            Operador = "Ana",
            Regras = { new RegraDeGiroPedida { Origem = OrigemDoGiroNoContrato.Leitor2, Funcao = FuncaoDoGiro.Saida } },
        });
        var catracaQueNaoExiste = Gravar(servico, new GravarMapaDeGiroRequest { Inner = 9, Operador = "Ana" });

        Assert.False(repetida.Gravado);
        Assert.False(semRotulo.Gravado);
        Assert.False(catracaQueNaoExiste.Gravado);
        Assert.Equal(0, new MapasDeGiro(_banco.Fabrica).RevisoesNoHistorico(1));
    }

    [Fact]
    public void Conferencia_registrada_aparece_na_regra_com_quem_e_quando()
    {
        var servico = Servico();
        Assert.True(Gravar(servico, new GravarMapaDeGiroRequest
        {
            Inner = 1,
            Operador = "Ana",
            Regras = { new RegraDeGiroPedida { Origem = OrigemDoGiroNoContrato.Leitor2, Funcao = FuncaoDoGiro.Saida, ContaComo = ContagemDoGiro.Entrada } },
        }).Gravado);

        var semFuncao = Conferir(servico, new RegistrarConferenciaDoGiroRequest { Inner = 1, Operador = "Ana" });
        Assert.False(semFuncao.Registrada);

        var r = Conferir(servico, 
            new RegistrarConferenciaDoGiroRequest { Inner = 1, Funcao = FuncaoDoGiro.Saida, ComoEsperado = true, Operador = "Bruno Sintético" });
        Assert.True(r.Registrada, string.Join(" | ", r.Problemas));

        var mapa = Obter(servico);
        var urna = mapa.Regras.Single(x => x.Origem == OrigemDoGiroNoContrato.Leitor2);
        Assert.Equal(SituacaoDaConferencia.ComoEsperado, urna.Conferencia);
        Assert.Equal("Bruno Sintético", urna.ConferidaPor);
        Assert.Equal(Agora, urna.ConferidaEm.ToDateTimeOffset());

        // A frente usa EI-041, que ninguém conferiu.
        Assert.Equal(SituacaoDaConferencia.NaoConferida, mapa.Regras.Single(x => x.Origem == OrigemDoGiroNoContrato.Leitor1).Conferencia);
    }
}
