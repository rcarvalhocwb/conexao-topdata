using Access.Application.Devices;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Chaves técnicas da Etapa A.2 (docs/35) em <c>edge_setting</c>: nascem desligadas, vão e
/// voltam do banco, valor ilegível cai no padrão (desligado), e o montador as leva à
/// <see cref="DeviceConfiguration"/> sem tocar em mais nada.
/// </summary>
/// <remarks>
/// Mesmo padrão de <c>OperacaoTests.Chave_de_digitos_variaveis_nasce_desligada_e_vai_e_volta_do_banco</c>.
/// </remarks>
public sealed class ChavesDaConfiguracaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>As chaves booleanas: nome, como ligar e como ler.</summary>
    public static TheoryData<string> ChavesLogicas() =>
    [
        ConfiguracoesDaBorda.ChaveEnviarDataHoraNoEvento,
        ConfiguracoesDaBorda.ChaveEnviarTipoDeLista,
        ConfiguracoesDaBorda.ChaveEnviarWiegandDoisLeitores,
        ConfiguracoesDaBorda.ChaveEnviarFormasDeEntrada,
    ];

    private static ConfiguracaoDaOperacao Ligada(string chave) => chave switch
    {
        ConfiguracoesDaBorda.ChaveEnviarDataHoraNoEvento => new ConfiguracaoDaOperacao(EnviarDataHoraNoEvento: true),
        ConfiguracoesDaBorda.ChaveEnviarTipoDeLista => new ConfiguracaoDaOperacao(EnviarTipoDeLista: true),
        ConfiguracoesDaBorda.ChaveEnviarWiegandDoisLeitores => new ConfiguracaoDaOperacao(EnviarWiegandDoisLeitores: true),
        ConfiguracoesDaBorda.ChaveEnviarFormasDeEntrada => new ConfiguracaoDaOperacao(EnviarFormasDeEntrada: true),
        _ => throw new ArgumentOutOfRangeException(nameof(chave), chave, "chave desconhecida"),
    };

    private static bool Valor(ConfiguracaoDaOperacao c, string chave) => chave switch
    {
        ConfiguracoesDaBorda.ChaveEnviarDataHoraNoEvento => c.EnviarDataHoraNoEvento,
        ConfiguracoesDaBorda.ChaveEnviarTipoDeLista => c.EnviarTipoDeLista,
        ConfiguracoesDaBorda.ChaveEnviarWiegandDoisLeitores => c.EnviarWiegandDoisLeitores,
        ConfiguracoesDaBorda.ChaveEnviarFormasDeEntrada => c.EnviarFormasDeEntrada,
        _ => throw new ArgumentOutOfRangeException(nameof(chave), chave, "chave desconhecida"),
    };

    private static void Escrever(BancoTemporario banco, string chave, string valor)
    {
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "UPDATE edge_setting SET value = $valor WHERE key = $chave;";
        comando.Parameters.AddWithValue("$chave", chave);
        comando.Parameters.AddWithValue("$valor", valor);
        Assert.Equal(1, comando.ExecuteNonQuery());
    }

    [Theory]
    [MemberData(nameof(ChavesLogicas))]
    public void Chave_nasce_desligada_vai_e_volta_do_banco_e_ilegivel_desliga(string chave)
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);

        Assert.False(Valor(configuracoes.Ler().Configuracao, chave));

        configuracoes.Gravar(Ligada(chave), Agora, "técnico");
        var (lida, ilegiveis) = configuracoes.Ler();
        Assert.Empty(ilegiveis);
        Assert.True(Valor(lida, chave));
        Assert.Equal(Ligada(chave), lida);

        Escrever(banco, chave, "sim");
        (lida, ilegiveis) = configuracoes.Ler();
        Assert.False(Valor(lida, chave));
        Assert.Contains(chave, ilegiveis);
    }

    /// <summary>
    /// <c>catraca.registrar_acesso_negado</c> leva o próprio valor: vazia é desligada, 0 a 3 vai,
    /// e o que não é número cai no padrão (desligada).
    /// </summary>
    [Fact]
    public void Registro_de_acesso_negado_nasce_vazio_vai_e_volta_e_ilegivel_desliga()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);

        Assert.Null(configuracoes.Ler().Configuracao.RegistrarAcessoNegado);

        // Gravar com o padrão deixa a chave vazia: desligada.
        configuracoes.Gravar(new ConfiguracaoDaOperacao(), Agora, "técnico");
        var (lida, ilegiveis) = configuracoes.Ler();
        Assert.Null(lida.RegistrarAcessoNegado);
        Assert.Empty(ilegiveis);

        for (byte valor = 0; valor <= 3; valor++)
        {
            configuracoes.Gravar(new ConfiguracaoDaOperacao(RegistrarAcessoNegado: valor), Agora, "técnico");
            Assert.Equal(valor, configuracoes.Ler().Configuracao.RegistrarAcessoNegado);
        }

        Escrever(banco, ConfiguracoesDaBorda.ChaveRegistrarAcessoNegado, "registrar");
        (lida, ilegiveis) = configuracoes.Ler();
        Assert.Null(lida.RegistrarAcessoNegado);
        Assert.Contains(ConfiguracoesDaBorda.ChaveRegistrarAcessoNegado, ilegiveis);
    }

    [Fact]
    public void Registro_de_acesso_negado_fora_de_0_a_3_nao_e_gravado()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);

        var fora = new ConfiguracaoDaOperacao(RegistrarAcessoNegado: 4);
        Assert.NotEmpty(fora.Validar());
        Assert.Throws<ArgumentException>(() => configuracoes.Gravar(fora, Agora));
    }

    /// <summary>
    /// O operador não muda nenhuma chave: a tela de Configurações grava por cima da atual
    /// (<c>atual with { … }</c>), e as chaves ligadas continuam ligadas.
    /// </summary>
    [Fact]
    public void Gravar_o_que_o_operador_muda_preserva_as_chaves()
    {
        using var banco = new BancoTemporario();
        banco.Migrar();
        var configuracoes = new ConfiguracoesDaBorda(banco.Fabrica);

        var tecnica = new ConfiguracaoDaOperacao(
            EnviarDataHoraNoEvento: true,
            RegistrarAcessoNegado: 1,
            EnviarTipoDeLista: true,
            EnviarWiegandDoisLeitores: true,
            EnviarFormasDeEntrada: true);
        configuracoes.Gravar(tecnica, Agora, "técnico");

        var atual = configuracoes.Ler().Configuracao;
        configuracoes.Gravar(atual with { MensagemPadrao = "Pista sintetica 9" }, Agora, "operador");

        Assert.Equal(tecnica with { MensagemPadrao = "Pista sintetica 9" }, configuracoes.Ler().Configuracao);
    }

    /// <summary>
    /// Do banco à <see cref="DeviceConfiguration"/>: cada chave liga só o seu envio, e os
    /// valores enviados são os do padrão de fábrica (nenhum valor novo vem do operador).
    /// </summary>
    [Fact]
    public void Montador_leva_as_chaves_do_evento_e_os_valores_da_fabrica()
    {
        var desligada = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4, new ConfiguracaoDaOperacao().ParaACatraca(), SobreposicoesDaCatraca.Nenhuma);
        var ligada = MontadorDaConfiguracao.Montar(
            PadroesDeFabrica.TopFit4,
            new ConfiguracaoDaOperacao(
                EnviarDataHoraNoEvento: true,
                RegistrarAcessoNegado: 2,
                EnviarTipoDeLista: true,
                EnviarWiegandDoisLeitores: true,
                EnviarFormasDeEntrada: true).ParaACatraca(),
            SobreposicoesDaCatraca.Nenhuma);

        Assert.False(desligada.EnviarDataHoraNoEventoOnLine);
        Assert.Null(desligada.RegistrarAcessoNegado);
        Assert.False(desligada.EnviarTipoDeLista);
        Assert.False(desligada.EnviarWiegandDoisLeitores);
        Assert.False(desligada.EnviarFormasDeEntradaOnLine);

        Assert.True(ligada.EnviarDataHoraNoEventoOnLine);
        Assert.Equal((byte)2, ligada.RegistrarAcessoNegado);
        Assert.True(ligada.EnviarTipoDeLista);
        Assert.True(ligada.EnviarWiegandDoisLeitores);
        Assert.True(ligada.EnviarFormasDeEntradaOnLine);

        // Tirando as chaves, as duas são iguais: nenhum valor de catraca mudou.
        Assert.Equal(
            desligada,
            ligada with
            {
                EnviarDataHoraNoEventoOnLine = false,
                RegistrarAcessoNegado = null,
                EnviarTipoDeLista = false,
                EnviarWiegandDoisLeitores = false,
                EnviarFormasDeEntradaOnLine = false,
                QuantidadesVariaveisDeDigitos = desligada.QuantidadesVariaveisDeDigitos,
            });
        Assert.Empty(ligada.Validar());
    }

    /// <summary>
    /// Toda configuração do evento que passa em <c>Validar</c>, com qualquer combinação das
    /// chaves, monta uma <see cref="DeviceConfiguration"/> que o adapter aceita.
    /// </summary>
    [Fact]
    public void Qualquer_combinacao_de_chaves_valida_monta_configuracao_valida()
    {
        byte?[] registros = [null, 0, 1, 2, 3];

        for (var mascara = 0; mascara < 16; mascara++)
        {
            foreach (var registro in registros)
            {
                var evento = new ConfiguracaoDaOperacao(
                    EnviarDataHoraNoEvento: (mascara & 1) != 0,
                    RegistrarAcessoNegado: registro,
                    EnviarTipoDeLista: (mascara & 2) != 0,
                    EnviarWiegandDoisLeitores: (mascara & 4) != 0,
                    EnviarFormasDeEntrada: (mascara & 8) != 0,
                    EnviarDigitosVariaveis: mascara % 3 == 0);

                Assert.Empty(evento.Validar());
                Assert.Empty(MontadorDaConfiguracao.Montar(
                    PadroesDeFabrica.TopFit4, evento.ParaACatraca(), SobreposicoesDaCatraca.Nenhuma).Validar());
            }
        }
    }
}
