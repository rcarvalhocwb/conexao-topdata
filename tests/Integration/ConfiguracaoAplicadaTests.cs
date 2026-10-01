using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using Edge.Worker;
using Edge.Worker.Operacao;
using Microsoft.Data.Sqlite;
using Simulator;
using SituacaoIpc = Contracts.Edge.V1.SituacaoDoComando;
using TipoDeComandoIpc = Contracts.Edge.V1.TipoDeComando;

namespace Integration.Tests;

/// <summary>
/// Etapa A.5 do docs/35: configuração salva × aplicada. O worker publica, com a situação de cada
/// catraca, a versão (<see cref="VersaoDaConfiguracao"/>) e o momento da configuração que a
/// catraca <b>aceitou</b>; o serviço lê pela base (migração 013, ADR-0024) e entrega no
/// <c>Equipamento</c> do contrato.
/// </summary>
/// <remarks>
/// <para>
/// A armadilha que estes testes cobrem (da A.4): no "Aplicar agora" o worker troca a
/// configuração da catraca <b>antes</b> de reconectar. Se a versão saísse dessa troca, o painel
/// diria "aplicada" de uma configuração que a catraca recusou. Ela sai do envio com retorno 0.
/// </para>
/// <para>
/// Mesma ligação do worker x86 (<c>Edge.Worker.X86/Program.cs</c>, que não carrega em teste):
/// <see cref="ConfiguracaoPorCatraca"/> na subida e no aplicar, e a publicação em
/// <see cref="Operacao.GravarSituacao"/>. Duas catracas simuladas; dados sintéticos.
/// </para>
/// </remarks>
public sealed class ConfiguracaoAplicadaTests : IDisposable
{
    private const int Porta = 3571;
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly InnerSimulator _sim;
    private readonly FilaDeComandosSqlite _fila;
    private readonly Operacao _operacao;
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

    public ConfiguracaoAplicadaTests()
    {
        _banco.Migrar();
        _fila = new FilaDeComandosSqlite(_banco.Fabrica);
        _camadas = new ConfiguracoesDasCatracas(_banco.Fabrica);
        _operacao = new Operacao(_banco.Fabrica);

        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", Porta, 1, 2)]);
        supervisor.Iniciar();
        _servico = new EdgeControlService(supervisor, relogio: () => _agora, operacao: _operacao, comandos: _fila);

        _sim = new InnerSimulator(() => _agora);
    }

    public void Dispose()
    {
        _sim.Dispose();
        _banco.Dispose();
    }

    private SessaoDeOperacao Sessao => _sessao ?? throw new InvalidOperationException("Suba o worker antes.");

    private void Subir()
    {
        var porCatraca = new ConfiguracaoPorCatraca(_banco.Fabrica);
        var (evento, _) = new ConfiguracoesDaBorda(_banco.Fabrica).Ler();
        var configuracoes = new Dictionary<int, DeviceConfiguration>();

        foreach (var inner in new[] { 1, 2 })
        {
            var (configuracao, avisos) = porCatraca.NaSubida(inner, evento);
            _registro.AddRange(avisos);
            configuracoes[inner] = configuracao;
        }

        _sessao = new SessaoDeOperacao(
            _sim,
            [1, 2],
            inner => configuracoes[inner],
            new DecisorDeIngresso(new RepositorioDeIngressos(_banco.Fabrica)),
            _registro.Add,
            Publicar,
            intervaloDePublicacao: TimeSpan.Zero,
            relogio: () => _agora,
            comandos: _fila,
            recarregarConfiguracao: porCatraca.ParaAplicar);

        _sessao.Iniciar(Porta);
        AteAtender(40);
    }

    // A mesma tradução do worker x86, com o relógio do teste.
    private void Publicar(IReadOnlyList<SituacaoDaCatraca> situacoes) =>
        _operacao.GravarSituacao(
            [.. situacoes.Select(c => new SituacaoDoEquipamento(
                c.DeviceId, c.Inner, "setor-a", c.Estado.ToString(), c.EmOperacao, c.Firmware,
                c.TentativasDeReconexao, c.UltimoEventoEm, c.UltimaDecisao, _agora,
                c.RelogioAcertadoEm, c.RelogioConferidoEm,
                c.DivergenciaDoRelogio is { } d ? (int)d.TotalSeconds : null, c.RelogioDivergente,
                c.ConfiguracaoAplicadaEm, c.ConfiguracaoVersao))]);

    private void Voltas(int quantas)
    {
        for (var i = 0; i < quantas; i++)
        {
            _agora += TimeSpan.FromSeconds(1);
            Sessao.UmaVolta();
        }
    }

    private void AteAtender(int limite)
    {
        for (var i = 0; i < limite && !Sessao.Dispositivos.All(d => d.Maquina.Current is DeviceState.Polling); i++)
        {
            Voltas(1);
        }

        Assert.All(Sessao.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));
    }

    private void Aplicar(int inner)
    {
        var resposta = _servico.EnviarComando(
            new EnviarComandoRequest { Inner = inner, Tipo = TipoDeComandoIpc.AplicarConfiguracao, Operador = "Ana (portaria)" },
            null!).Result;
        Assert.True(resposta.Aceito, string.Join(" ", resposta.Problemas));
    }

    private void Gravar(int inner, SobreposicoesDaCatraca camada) =>
        Assert.Empty(_camadas.Gravar(inner, camada, _agora, "Bia (técnica)"));

    /// <summary>O que o painel recebe: o <c>Equipamento</c> do contrato, lido da base pelo serviço.</summary>
    private Equipamento NoPainel(int inner) =>
        _servico.ListarEquipamentos(new ListarEquipamentosRequest(), null!).Result.Equipamentos.Single(e => e.Inner == inner);

    private (string Versao, DateTimeOffset? Em) Aplicada(int inner)
    {
        var e = NoPainel(inner);
        return (e.ConfiguracaoVersao, e.ConfiguracaoAplicadaEm?.ToDateTimeOffset());
    }

    private List<ComandoRegistrado> Comandos(int inner) =>
        [.. _servico.ListarComandos(new ListarComandosRequest { Inner = inner }, null!).Result.Comandos];

    private DeviceSlot Slot(int inner) => Sessao.Dispositivos.Single(d => d.Inner == inner);

    private List<DeviceConfiguration> Recebidas(int inner) => _sim.Dispositivo(inner).ConfiguracoesRecebidas;

    [Fact]
    public void Na_subida_cada_catraca_publica_a_versao_do_que_aceitou()
    {
        Subir();

        foreach (var inner in new[] { 1, 2 })
        {
            var (versao, em) = Aplicada(inner);
            Assert.Equal(VersaoDaConfiguracao.Calcular(Recebidas(inner)[^1]), versao);
            Assert.NotNull(em);

            // O que o serviço entregou é o que está na base, e o que o worker tem.
            var linha = _operacao.ListarSituacao().Single(s => s.Inner == inner);
            Assert.Equal((versao, em), (linha.ConfiguracaoVersao, linha.ConfiguracaoAplicadaEm));
            Assert.Equal(versao, Slot(inner).ConfiguracaoVersao);
        }

        // Mesma configuração (tabela vazia: fábrica + evento nas duas) = mesma versão.
        Assert.Equal(Aplicada(1).Versao, Aplicada(2).Versao);
    }

    /// <summary>
    /// O teste que prova a A.5: com a catraca recusando o envio (retorno ≠ 0), a versão e o
    /// momento publicados não mudam — embora o worker já tenha trocado a configuração; quando o
    /// envio volta a dar 0, mudam.
    /// </summary>
    [Fact]
    public void Envio_recusado_nao_muda_a_versao_nem_o_momento_e_o_aceito_muda()
    {
        Subir();
        var antesDa1 = Aplicada(1);
        var antesDa2 = Aplicada(2);
        var configuracaoDeAntes = Slot(2).Configuracao;

        Gravar(2, new SobreposicoesDaCatraca { MensagemPadrao = "Portao sintetico 2", TempoDoAcionamento1 = 7 });
        _sim.Dispositivo(2).RetornoDaConfiguracao = 1;
        Aplicar(2);
        Voltas(30);

        // A armadilha da A.4: o worker já trocou a configuração da 2 e está reconectando...
        Assert.NotSame(configuracaoDeAntes, Slot(2).Configuracao);
        Assert.Equal("Portao sintetico 2", Slot(2).Configuracao.MensagemPadrao);
        Assert.NotEqual(DeviceState.Polling, Slot(2).Maquina.Current);
        Assert.NotEqual(antesDa2.Versao, VersaoDaConfiguracao.Calcular(Slot(2).Configuracao));

        // ...mas a catraca recusou cada envio: nada mudou no que o painel recebe.
        Assert.DoesNotContain(Recebidas(2), c => c.MensagemPadrao == "Portao sintetico 2");
        Assert.Equal(antesDa2, Aplicada(2));
        Assert.Equal(antesDa1, Aplicada(1));

        // A catraca volta a aceitar: a versão passa a ser a do que ela recebeu.
        _sim.Dispositivo(2).RetornoDaConfiguracao = null;
        AteAtender(300);

        var depois = Aplicada(2);
        Assert.Equal("Portao sintetico 2", Recebidas(2)[^1].MensagemPadrao);
        Assert.Equal(VersaoDaConfiguracao.Calcular(Recebidas(2)[^1]), depois.Versao);
        Assert.NotEqual(antesDa2.Versao, depois.Versao);
        Assert.True(depois.Em > antesDa2.Em);

        // A 1 nunca foi tocada.
        Assert.Equal(antesDa1, Aplicada(1));
    }

    [Fact]
    public void Aplicar_na_2_muda_so_a_versao_da_2()
    {
        Subir();
        var antesDa1 = Aplicada(1);
        var antesDa2 = Aplicada(2);

        Gravar(2, new SobreposicoesDaCatraca { TipoDeLeitor = 5 });
        Aplicar(2);
        Voltas(16);

        Assert.Equal(SituacaoIpc.Concluido, Assert.Single(Comandos(2)).Situacao);

        var depoisDa2 = Aplicada(2);
        Assert.NotEqual(antesDa2.Versao, depoisDa2.Versao);
        Assert.Equal(VersaoDaConfiguracao.Calcular(Recebidas(2)[^1]), depoisDa2.Versao);
        Assert.True(depoisDa2.Em > antesDa2.Em);

        Assert.Equal(antesDa1, Aplicada(1));
    }

    /// <summary>
    /// Reaplicar o que a catraca já tem: mesma versão (nada mudou nela), momento novo (ela recebeu
    /// de novo).
    /// </summary>
    [Fact]
    public void Reaplicar_a_mesma_configuracao_mantem_a_versao_e_atualiza_o_momento()
    {
        Subir();
        var antes = Aplicada(2);

        Aplicar(2);
        Voltas(16);

        var depois = Aplicada(2);
        Assert.Equal(antes.Versao, depois.Versao);
        Assert.True(depois.Em > antes.Em);
    }

    /// <summary>A base recusa uma versão que não seja SHA-256 em hexadecimal minúsculo (CHECK da 013).</summary>
    [Fact]
    public void A_base_recusa_versao_fora_do_formato()
    {
        var invalida = new SituacaoDoEquipamento(
            "inner-1", 1, "setor-a", "Polling", true, null, 0, null, null, Inicio,
            ConfiguracaoAplicadaEm: Inicio, ConfiguracaoVersao: new string('A', 64));

        Assert.Throws<SqliteException>(() => _operacao.GravarSituacao([invalida]));
        Assert.Empty(_operacao.ListarSituacao());

        var valida = invalida with { ConfiguracaoVersao = VersaoDaConfiguracao.Calcular(PadroesDeFabrica.TopFit4) };
        _operacao.GravarSituacao([valida]);
        var lida = Assert.Single(_operacao.ListarSituacao());
        Assert.Equal((valida.ConfiguracaoVersao, Inicio), (lida.ConfiguracaoVersao, lida.ConfiguracaoAplicadaEm));
    }
}
