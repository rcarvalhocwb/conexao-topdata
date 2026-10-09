using System.Collections;
using System.Reflection;
using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Worker;
using Edge.Worker.Operacao;
using Simulator;
using SituacaoIpc = Contracts.Edge.V1.SituacaoDoComando;
using TipoDeComandoIpc = Contracts.Edge.V1.TipoDeComando;

namespace Integration.Tests;

/// <summary>
/// Etapa A.4 do docs/35: o worker aplica por catraca. Duas catracas simuladas num worker, a
/// base temporária com a camada de cada uma (<c>device_config</c>, migração 012), os
/// comandos pelo serviço e pela fila na base (ADR-0024), e a prova pelo que a catraca
/// simulada recebeu de cada Inner.
/// </summary>
/// <remarks>
/// <para>
/// A ligação é a mesma do worker x86 (<c>Edge.Worker.X86/Program.cs</c>, que não carrega em
/// teste): <see cref="ConfiguracaoPorCatraca.NaSubida"/> para cada catraca e
/// <see cref="ConfiguracaoPorCatraca.ParaAplicar"/> no "Aplicar agora".
/// </para>
/// <para>
/// A camada recusada pelo <c>Validar()</c> vem de um padrão de fábrica com o relé 2
/// configurado (valor sintético) e da catraca gravada "sem leitor 2" — válida contra o padrão
/// de quando foi gravada (regra 3 do docs/34 §4.2). Dados sintéticos; nenhum código lido.
/// </para>
/// </remarks>
public sealed class ComandosDaCatracaPorInnerTests : IDisposable
{
    private const int Porta = 3570;
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly InnerSimulator _sim;
    private readonly FilaDeComandosSqlite _fila;
    private readonly EdgeControlService _servico;
    private readonly ConfiguracoesDasCatracas _camadas;
    private readonly List<string> _registro = [];
    private SessaoDeOperacao? _sessao;
    private DateTimeOffset _agora = Inicio;

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

    public ComandosDaCatracaPorInnerTests()
    {
        _banco.Migrar();
        _fila = new FilaDeComandosSqlite(_banco.Fabrica);
        _camadas = new ConfiguracoesDasCatracas(_banco.Fabrica);

        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", Porta, 1, 2)]);
        supervisor.Iniciar();
        _servico = new EdgeControlService(supervisor, relogio: () => _agora, comandos: _fila);

        _sim = new InnerSimulator(() => _agora);
    }

    public void Dispose()
    {
        _sim.Dispose();
        _banco.Dispose();
    }

    private static DeviceConfiguration ComReleDois() =>
        PadroesDeFabrica.TopFit4 with { FuncaoDoAcionamento2 = 3, TempoDoAcionamento2 = 5 };

    private SessaoDeOperacao Sessao => _sessao ?? throw new InvalidOperationException("Suba o worker antes.");

    /// <summary>Sobe o worker como o x86 faz, e leva as duas catracas até Polling.</summary>
    private void Subir(Func<DeviceConfiguration>? padraoDeFabrica = null)
    {
        var porCatraca = new ConfiguracaoPorCatraca(_banco.Fabrica, padraoDeFabrica);
        var (evento, _) = new ConfiguracoesDaBorda(_banco.Fabrica).Ler();
        var configuracoes = new Dictionary<int, DeviceConfiguration>();

        foreach (var inner in new[] { 1, 2 })
        {
            var (configuracao, avisos) = porCatraca.NaSubida(inner, evento);
            _registro.AddRange(avisos);
            configuracoes[inner] = configuracao;
        }

        var repositorio = new RepositorioDeIngressos(_banco.Fabrica);
        _sessao = new SessaoDeOperacao(
            _sim,
            [1, 2],
            inner => configuracoes[inner],
            new DecisorDeIngresso(repositorio),
            _registro.Add,
            _ => { },
            relogio: () => _agora,
            comandos: _fila,
            recarregarConfiguracao: porCatraca.ParaAplicar);

        _sessao.Iniciar(Porta);
        for (var i = 0; i < 40 && !Sessao.Dispositivos.All(d => d.Maquina.Current is DeviceState.Polling); i++)
        {
            Voltas(1);
        }

        Assert.All(Sessao.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));
    }

    /// <summary>Cada volta anda 1 s: o worker olha a fila de comandos a cada 500 ms.</summary>
    private void Voltas(int quantas)
    {
        for (var i = 0; i < quantas; i++)
        {
            _agora += TimeSpan.FromSeconds(1);
            Sessao.UmaVolta();
        }
    }

    private EnviarComandoResponse Aplicar(int inner)
    {
        var resposta = _servico.EnviarComando(
            new EnviarComandoRequest { Inner = inner, Tipo = TipoDeComandoIpc.AplicarConfiguracao, Operador = "Ana (portaria)" },
            null!).Result;
        Assert.True(resposta.Aceito, string.Join(" ", resposta.Problemas));
        return resposta;
    }

    private List<ComandoRegistrado> Comandos(int inner) =>
        [.. _servico.ListarComandos(new ListarComandosRequest { Inner = inner }, null!).Result.Comandos];

    private ComandoRegistrado Desfecho(int inner) => Assert.Single(Comandos(inner));

    private List<DeviceConfiguration> Recebidas(int inner) => _sim.Dispositivo(inner).ConfiguracoesRecebidas;

    private DeviceSlot Slot(int inner) => Sessao.Dispositivos.Single(d => d.Inner == inner);

    private static void CampoACampo(DeviceConfiguration esperado, DeviceConfiguration obtido)
    {
        foreach (var propriedade in typeof(DeviceConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var a = propriedade.GetValue(esperado);
            var b = propriedade.GetValue(obtido);

            if (a is IEnumerable listaA and not string && b is IEnumerable listaB and not string)
            {
                Assert.True(listaA.Cast<object>().SequenceEqual(listaB.Cast<object>()), propriedade.Name);
            }
            else
            {
                Assert.True(Equals(a, b), $"{propriedade.Name}: esperado {a}, obtido {b}");
            }
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    public void Sem_login_a_autoria_de_cada_comando_e_painel(int inner, int quantidade)
    {
        Assert.Equal(quantidade, Aplicar(inner).Ids.Count);
        var registros = Comandos(inner);
        Assert.Equal(quantidade, registros.Count);
        Assert.All(registros, r => Assert.Equal("painel", r.Operador));
    }

    private void Gravar(int inner, SobreposicoesDaCatraca camada) =>
        Assert.Empty(_camadas.Gravar(inner, camada, _agora, "Bia (técnica)"));

    [Fact]
    public void Gravar_a_camada_da_2_e_aplicar_na_2_muda_so_a_2()
    {
        Subir();
        var enviadasA1 = Recebidas(1).Count;
        var configuracaoDa1 = Slot(1).Configuracao;

        Gravar(2, new SobreposicoesDaCatraca { MensagemPadrao = "Portao sintetico 2", TempoDoAcionamento1 = 7 });
        Assert.Single(Aplicar(2).Ids);
        Voltas(16);

        var da2 = Recebidas(2)[^1];
        Assert.Equal("Portao sintetico 2", da2.MensagemPadrao);
        Assert.Equal((byte)7, da2.TempoDoAcionamento1);
        Assert.Equal(da2, Slot(2).Configuracao);
        var desfecho = Desfecho(2);
        Assert.Equal((SituacaoIpc.Concluido, "configuração enviada; catraca atendendo"), (desfecho.Situacao, desfecho.Resultado));

        // A catraca 1 não foi tocada: nenhum envio, a mesma configuração, atendendo.
        Assert.Equal(enviadasA1, Recebidas(1).Count);
        Assert.Same(configuracaoDa1, Slot(1).Configuracao);
        Assert.Equal("Aproxime o ingresso", Recebidas(1)[^1].MensagemPadrao);
        Assert.Empty(Comandos(1));
        Assert.All(Sessao.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));
    }

    /// <summary>
    /// O contrato de sempre (proto intocado): catraca 0 = todas, um comando por catraca; cada
    /// uma relê a sua camada.
    /// </summary>
    [Fact]
    public void Aplicar_em_todas_envia_a_cada_catraca_a_sua_configuracao()
    {
        Subir();
        Gravar(2, new SobreposicoesDaCatraca { MensagemPadrao = "Portao sintetico 2" });

        Assert.Equal(2, Aplicar(0).Ids.Count);
        Voltas(20);

        Assert.Equal("Aproxime o ingresso", Recebidas(1)[^1].MensagemPadrao);
        Assert.Equal("Portao sintetico 2", Recebidas(2)[^1].MensagemPadrao);
        Assert.Equal(SituacaoIpc.Concluido, Desfecho(1).Situacao);
        Assert.Equal(SituacaoIpc.Concluido, Desfecho(2).Situacao);
    }

    [Fact]
    public void Configuracao_recusada_na_2_falha_o_comando_e_a_1_segue_em_polling_sem_reenvio()
    {
        Subir(ComReleDois);
        var enviadasA1 = Recebidas(1).Count;
        var enviadasA2 = Recebidas(2).Count;
        var configuracaoDa2 = Slot(2).Configuracao;

        // Válida contra o padrão TopFit 4 (o da gravação); recusada contra o do worker.
        Gravar(2, new SobreposicoesDaCatraca { OperacaoDoLeitor2 = MontadorDaConfiguracao.LeitorDesabilitado });
        Aplicar(2);
        Voltas(16);

        var desfecho = Desfecho(2);
        Assert.Equal(SituacaoIpc.Falhou, desfecho.Situacao);
        Assert.StartsWith("configuração não aplicada: Catraca 2: O relé 2 está configurado (urna)", desfecho.Resultado, StringComparison.Ordinal);

        // A 2 segue com a configuração que tinha, sem reconectar; a 1 nem percebe.
        Assert.Equal(enviadasA2, Recebidas(2).Count);
        Assert.Same(configuracaoDa2, Slot(2).Configuracao);
        Assert.Equal(enviadasA1, Recebidas(1).Count);
        Assert.All(Sessao.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));
        Assert.Contains("inner-2: configuração não aplicada (1 problema(s)); segue com a que tinha", _registro);
    }

    /// <summary>No aplicar, qualquer problema de leitura da camada faz o comando falhar.</summary>
    [Fact]
    public void Camada_ilegivel_na_2_falha_o_aplicar_com_o_problema()
    {
        Subir();
        var enviadasA2 = Recebidas(2).Count;
        Gravar(2, new SobreposicoesDaCatraca { MensagemPadrao = "Portao sintetico 2" });

        using (var conexao = _banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                PRAGMA ignore_check_constraints = ON;
                UPDATE device_config SET reader_type = 42, revision = revision + 1 WHERE inner_number = 2;
                """;
            comando.ExecuteNonQuery();
        }

        Aplicar(2);
        Voltas(16);

        var desfecho = Desfecho(2);
        Assert.Equal(SituacaoIpc.Falhou, desfecho.Situacao);
        Assert.Contains("Catraca 2: reader_type ilegível (42); herda do evento.", desfecho.Resultado, StringComparison.Ordinal);
        Assert.Equal(enviadasA2, Recebidas(2).Count);
        Assert.Equal(DeviceState.Polling, Sessao.Dispositivos.Single(d => d.Inner == 2).Maquina.Current);
    }

    [Fact]
    public void Subida_com_a_camada_da_2_recusada_usa_o_padrao_dela_e_a_1_a_sua()
    {
        Gravar(1, new SobreposicoesDaCatraca { MensagemPadrao = "Portao sintetico 1" });
        Gravar(2, new SobreposicoesDaCatraca
        {
            OperacaoDoLeitor2 = MontadorDaConfiguracao.LeitorDesabilitado,
            MensagemPadrao = "Portao sintetico 2",
        });

        Subir(ComReleDois);

        Assert.Equal("Portao sintetico 1", Recebidas(1)[^1].MensagemPadrao);

        // A 2 sobe com o padrão dela (fábrica + evento), inteira, sem nada da camada.
        var da2 = Recebidas(2)[^1];
        Assert.Equal("Aproxime o ingresso", da2.MensagemPadrao);
        Assert.Equal(MontadorDaConfiguracao.LeitorSomenteEntrada, da2.OperacaoDoLeitor2);
        Assert.Empty(da2.Validar());

        var aviso = Assert.Single(_registro, l => l.StartsWith("Catraca 2:", StringComparison.Ordinal));
        Assert.StartsWith("Catraca 2: configuração própria recusada, subindo com o padrão dela", aviso, StringComparison.Ordinal);
        Assert.DoesNotContain(_registro, l => l.StartsWith("Catraca 1:", StringComparison.Ordinal));
    }

    [Fact]
    public void Subida_com_campo_ilegivel_avisa_e_o_resto_da_camada_vale()
    {
        Gravar(2, new SobreposicoesDaCatraca { MensagemPadrao = "Portao sintetico 2", TipoDeLeitor = 5 });
        using (var conexao = _banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                PRAGMA ignore_check_constraints = ON;
                UPDATE device_config SET reader_type = 42, revision = revision + 1 WHERE inner_number = 2;
                """;
            comando.ExecuteNonQuery();
        }

        Subir();

        var da2 = Recebidas(2)[^1];
        Assert.Equal("Portao sintetico 2", da2.MensagemPadrao);
        Assert.Equal((byte)8, da2.TipoDeLeitor);
        Assert.Contains("Catraca 2: reader_type ilegível (42); herda do evento.", _registro);
    }

    /// <summary>
    /// Tabela vazia: a subida e o "Aplicar agora" dão, campo a campo, o que o worker dava antes
    /// da A.4 — fábrica → evento → camada vazia.
    /// </summary>
    [Fact]
    public void Tabela_vazia_e_o_mesmo_de_antes_na_subida_e_no_aplicar()
    {
        new ConfiguracoesDaBorda(_banco.Fabrica).Gravar(
            new ConfiguracaoDaOperacao(TipoDeLeitor: 5, LeitorDaUrna: false, TempoDeAcionamento: 6, MensagemPadrao: "Evento sintetico"),
            _agora);
        Subir();
        var antes = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4,
            new ConfiguracoesDaBorda(_banco.Fabrica).Ler().Configuracao.ParaACatraca(),
            SobreposicoesDaCatraca.Nenhuma);

        CampoACampo(antes, Recebidas(1)[^1]);
        CampoACampo(antes, Recebidas(2)[^1]);
        Assert.DoesNotContain(_registro, l => l.StartsWith("Catraca ", StringComparison.Ordinal));

        var (aplicar, problemas) = new ConfiguracaoPorCatraca(_banco.Fabrica).ParaAplicar(2);
        Assert.Empty(problemas);
        CampoACampo(antes, aplicar!);
    }
}
