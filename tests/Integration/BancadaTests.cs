using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Edge.Worker.Bancada;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// O ensaio de bancada inteiro, com o simulador no lugar da catraca.
/// </summary>
/// <remarks>
/// Mesmo laço, mesma máquina de estados, mesma base e mesmo carregador de arquivo que vão
/// rodar na bancada real. O que muda é só o adapter. É a prova de que, quando a DLL
/// carregar (HIL-STACK-01), o resto do caminho até a catraca girar já está de pé.
/// Ver docs/21-roteiro-da-bancada.md
/// </remarks>
public sealed class BancadaTests
{
    private const string Arquivo = """
        {
          "provedores": [
            { "id": "zet", "nome": "Zet" },
            { "id": "bilheteria-local", "nome": "Bilheteria", "reutilizavel": true,
              "intervaloDeReusoSegundos": 240, "somenteNaUrna": true }
          ],
          "ingressos": [
            { "provedor": "zet", "referencia": "TESTE-1", "qr": "1000000001", "categoria": "inteira" },
            { "provedor": "zet", "referencia": "TESTE-LONGO", "qr": "9f3c2a10-7b4e-4c1a-9d2e-5f6a7b8c9d0e" }
          ],
          "cartoes": [
            { "provedor": "bilheteria-local", "codigo": "12345678", "categoria": "meia" }
          ]
        }
        """;

    private sealed record Montagem(
        BancoTemporario Banco,
        RepositorioDeIngressos Repositorio,
        InnerSimulator Simulador,
        SessaoDeBancada Sessao,
        List<string> Tela) : IDisposable
    {
        public void Dispose()
        {
            Simulador.Dispose();
            Banco.Dispose();
        }
    }

    private static Montagem Montar()
    {
        var banco = new BancoTemporario();
        banco.Migrar();
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);
        ArquivoDeBancada.Carregar(Arquivo, repositorio, DateTimeOffset.UtcNow.AddMinutes(-30));

        var simulador = new InnerSimulator();
        var tela = new List<string>();
        var sessao = new SessaoDeBancada(
            simulador,
            [1],
            ConfiguracaoDeBancada.TopFit4(),
            new DecisorDeIngresso(repositorio),
            tela.Add);

        sessao.Iniciar(3570);
        for (var i = 0; i < 12; i++)
        {
            sessao.UmaVolta();
        }

        return new Montagem(banco, repositorio, simulador, sessao, tela);
    }

    private static void Voltas(SessaoDeBancada sessao, int quantas = 10)
    {
        for (var i = 0; i < quantas; i++)
        {
            sessao.UmaVolta();
        }
    }

    [Fact]
    public void A_configuracao_da_TopFit_4_para_bancada_e_valida()
    {
        Assert.Empty(ConfiguracaoDeBancada.TopFit4().Validar());
        Assert.Empty(ConfiguracaoDeBancada.TopFit4(tipoDeLeitor: 5, leitorDaUrna: false).Validar());
    }

    [Fact]
    public void QR_da_base_na_catraca_libera_gira_e_a_passagem_fica_gravada()
    {
        using var m = Montar();

        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1000000001"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(m.Sessao);

        Assert.NotEmpty(m.Simulador.Dispositivo(1).LiberacoesPedidas);
        Assert.Contains(m.Tela, l => l.Contains("LIBERADO", StringComparison.Ordinal));
        Assert.Contains(m.Tela, l => l.Contains("código=[1000000001] (10 caracteres)", StringComparison.Ordinal));

        var conta = m.Repositorio.Conciliar("zet", DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(1, conta.UsosConsumidos);
        Assert.Equal(1, conta.UsosComPassagemFisica);
    }

    [Fact]
    public void QR_que_a_base_nao_conhece_e_negado_e_a_catraca_nao_libera()
    {
        using var m = Montar();

        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "5555555555"));
        Voltas(m.Sessao);

        Assert.Empty(m.Simulador.Dispositivo(1).LiberacoesPedidas);
        Assert.Contains(m.Tela, l => l.Contains("NEGADO", StringComparison.Ordinal)
            && l.Contains("CREDENCIAL_DESCONHECIDA", StringComparison.Ordinal));
    }

    [Fact]
    public void Cartao_da_bilheteria_libera_na_urna_e_e_negado_na_frente()
    {
        using var m = Montar();

        // Na frente: recusado, e a venda fica intacta.
        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "0012345678"));
        Voltas(m.Sessao);

        Assert.Empty(m.Simulador.Dispositivo(1).LiberacoesPedidas);
        Assert.Contains(m.Tela, l => l.Contains("FORA_DA_URNA", StringComparison.Ordinal));

        // Na fenda da urna: liberado.
        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor2), "0012345678"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(m.Sessao);

        Assert.NotEmpty(m.Simulador.Dispositivo(1).LiberacoesPedidas);
        Assert.Equal(1, m.Repositorio.Conciliar("bilheteria-local", DateTimeOffset.UtcNow.AddHours(1)).UsosComPassagemFisica);
    }

    [Fact]
    public void Autorizado_sem_giro_aparece_como_uso_sem_passagem_fisica()
    {
        using var m = Montar();

        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor1), "1000000001"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.FimTempoAcionamento)));
        Voltas(m.Sessao);

        var conta = m.Repositorio.Conciliar("zet", DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(1, conta.UsosSemPassagemFisica);
        Assert.Contains("autorizados sem giro: 1", m.Sessao.Resumo(), StringComparison.Ordinal);
    }

    [Fact]
    public void O_arquivo_de_bancada_aplica_as_regras_do_sistema()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);

        var carga = ArquivoDeBancada.Carregar(Arquivo, repositorio, DateTimeOffset.UtcNow);

        Assert.Equal(2, carga.Provedores);
        Assert.Equal(1, carga.Ingressos);
        Assert.Equal(1, carga.Cartoes);

        // O QR de 36 caracteres não entra: a catraca não o leria.
        Assert.Contains(carga.Problemas, p => p.Contains("TESTE-LONGO", StringComparison.Ordinal)
            && p.Contains("36 caracteres", StringComparison.Ordinal));

        // A bancada nunca avisa o Zet: nenhum provedor sai com conector.
        Assert.All(repositorio.Provedores(), p => Assert.Equal("", p.Conector));
    }

    [Fact]
    public void Cartao_de_oito_digitos_no_arquivo_entra_com_os_zeros_que_a_catraca_entrega()
    {
        using var m = Montar();

        m.Simulador.Dispositivo(1).Roteirizar(
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.Leitor2), "0012345678"),
            new ScriptedEvent(EventOrigin.From(KnownEventOrigin.GiroConfirmado)));
        Voltas(m.Sessao);

        Assert.Contains(m.Tela, l => l.Contains("LIBERADO", StringComparison.Ordinal)
            && l.Contains("bilheteria-local/meia", StringComparison.Ordinal));
    }

    [Fact]
    public void O_arquivo_de_exemplo_que_vai_no_instalador_carrega()
    {
        // É o arquivo que a pessoa na bancada vai abrir primeiro. Se ele não carregar, o
        // ensaio para no primeiro minuto por um erro nosso.
        var raiz = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(raiz, "ConexaoTopdata.slnx")))
        {
            raiz = Path.GetDirectoryName(raiz) ?? throw new DirectoryNotFoundException("raiz do repositório");
        }

        var json = File.ReadAllText(Path.Combine(raiz, "installer", "bancada.exemplo.json"));

        using var banco = new BancoTemporario();
        banco.Migrar();
        var carga = ArquivoDeBancada.Carregar(json, new RepositorioDeIngressos(banco.Fabrica), DateTimeOffset.UtcNow);

        Assert.Equal(2, carga.Provedores);
        Assert.Equal(5, carga.Ingressos);
        Assert.Empty(carga.Problemas);
    }
}
