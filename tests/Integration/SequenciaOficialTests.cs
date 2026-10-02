using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Infrastructure.SQLite;
using Edge.Worker.Operacao;
using Simulator;

namespace Integration.Tests;

/// <summary>
/// Etapa A.7 do docs/35: a chave técnica <c>catraca.sequencia_oficial</c> em <c>edge_setting</c>
/// — nasce desligada, vai e volta do banco, ilegível = desligada — e chega ao laço do worker como
/// o x86 a liga (<c>Edge.Worker.X86/Program.cs</c>, que não carrega em teste).
/// </summary>
/// <remarks>
/// A sequência exata de chamadas à DLL é provada em <c>HardwareInLoop.Tests.SequenciaOficialTests</c>
/// (laço + adapter + costura falsa). Aqui a prova é o que a catraca simulada recebeu.
/// </remarks>
public sealed class SequenciaOficialTests
{
    private const int Porta = 3570;
    private static readonly int[] Catracas = [1, 2];
    private static readonly DateTimeOffset Inicio = new(2026, 10, 1, 21, 0, 0, TimeSpan.Zero);

    private static void Escrever(BancoTemporario banco, string chave, string valor)
    {
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "UPDATE edge_setting SET value = $valor WHERE key = $chave;";
        comando.Parameters.AddWithValue("$chave", chave);
        comando.Parameters.AddWithValue("$valor", valor);
        Assert.Equal(1, comando.ExecuteNonQuery());
    }

    [Fact]
    public void Chave_nasce_desligada_vai_e_volta_do_banco_e_ilegivel_desliga()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);

        Assert.Equal("catraca.sequencia_oficial", ConfiguracoesDaBorda.ChaveSequenciaOficial);
        Assert.False(new ConfiguracaoDaOperacao().SequenciaOficial);
        Assert.False(configuracoes.Ler().Configuracao.SequenciaOficial);

        // Gravar o padrão escreve "0": desligada.
        configuracoes.Gravar(new ConfiguracaoDaOperacao(), Inicio, "técnico");
        Assert.False(configuracoes.Ler().Configuracao.SequenciaOficial);

        configuracoes.Gravar(new ConfiguracaoDaOperacao(SequenciaOficial: true), Inicio, "técnico");
        var (lida, ilegiveis) = configuracoes.Ler();
        Assert.Empty(ilegiveis);
        Assert.True(lida.SequenciaOficial);
        Assert.Equal(new ConfiguracaoDaOperacao(SequenciaOficial: true), lida);

        Escrever(banco, ConfiguracoesDaBorda.ChaveSequenciaOficial, "sim");
        (lida, ilegiveis) = configuracoes.Ler();
        Assert.False(lida.SequenciaOficial);
        Assert.Contains(ConfiguracoesDaBorda.ChaveSequenciaOficial, ilegiveis);
    }

    /// <summary>O operador grava por cima da atual (<c>atual with { … }</c>): a chave ligada continua ligada.</summary>
    [Fact]
    public void Gravar_o_que_o_operador_muda_preserva_a_chave()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);
        configuracoes.Gravar(new ConfiguracaoDaOperacao(SequenciaOficial: true), Inicio, "técnico");

        var atual = configuracoes.Ler().Configuracao;
        configuracoes.Gravar(atual with { MensagemPadrao = "Pista sintetica 9" }, Inicio, "operador");

        Assert.True(configuracoes.Ler().Configuracao.SequenciaOficial);
    }

    /// <summary>
    /// A chave é do laço, não da catraca: não muda a <see cref="DeviceConfiguration"/> montada (nem a
    /// cobertura da ADR-0020) — só a ordem e a forma de envio.
    /// </summary>
    [Fact]
    public void Chave_nao_entra_na_configuracao_da_catraca()
    {
        var camadaDesligada = new ConfiguracaoDaOperacao().ParaACatraca();
        var camadaLigada = new ConfiguracaoDaOperacao(SequenciaOficial: true).ParaACatraca();
        Assert.Equal(camadaDesligada, camadaLigada);

        var ligada = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, camadaLigada, SobreposicoesDaCatraca.Nenhuma);
        Assert.Equal(0, ligada.MudancaAutomatica);
        Assert.True(ligada.Online);
    }

    /// <summary>
    /// Do banco ao laço, como o worker x86 sobe: com a chave, cada catraca recebe as três etapas na
    /// ordem (cfg off-line, mudança, cfg on-line) e duas configurações inteiras; sem a chave, os três
    /// envios completos de sempre e nenhuma etapa. As duas chegam a Polling.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Worker_sobe_com_a_sequencia_da_chave_ate_polling(bool ligada)
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var borda = new ConfiguracoesDaBorda(banco.Fabrica);
        borda.Gravar(new ConfiguracaoDaOperacao(SequenciaOficial: ligada), Inicio, "técnico");

        var agora = Inicio;
        using var simulador = new InnerSimulator(() => agora);
        var (evento, _) = borda.Ler();
        var porCatraca = new ConfiguracaoPorCatraca(banco.Fabrica);
        var configuracoes = Catracas.ToDictionary(inner => inner, inner => porCatraca.NaSubida(inner, evento).Configuracao);

        var sessao = new SessaoDeOperacao(
            simulador,
            Catracas,
            inner => configuracoes[inner],
            new DecisorDeIngresso(new RepositorioDeIngressos(banco.Fabrica)),
            _ => { },
            _ => { },
            relogio: () => agora,
            sequenciaOficial: evento.SequenciaOficial);
        sessao.Iniciar(Porta);

        for (var i = 0; i < 40 && !sessao.Dispositivos.All(d => d.Maquina.Current is DeviceState.Polling); i++)
        {
            agora += TimeSpan.FromSeconds(1);
            sessao.UmaVolta();
        }

        Assert.All(sessao.Dispositivos, d => Assert.Equal(DeviceState.Polling, d.Maquina.Current));

        foreach (var inner in Catracas)
        {
            var catraca = simulador.Dispositivo(inner);
            if (ligada)
            {
                Assert.Equal(
                    [EtapaDaSequenciaOficial.ConfiguracaoOffLine, EtapaDaSequenciaOficial.MudancaAutomatica, EtapaDaSequenciaOficial.ConfiguracaoOnLine],
                    catraca.EtapasDaSequenciaOficial);
                Assert.Equal(2, catraca.ConfiguracoesRecebidas.Count);
            }
            else
            {
                Assert.Empty(catraca.EtapasDaSequenciaOficial);
                Assert.Equal(3, catraca.ConfiguracoesRecebidas.Count);
            }

            Assert.All(catraca.ConfiguracoesRecebidas, c => Assert.Equal(configuracoes[inner], c));
            Assert.Equal(1, catraca.ReabilitacoesDoLeitor);
        }
    }
}
