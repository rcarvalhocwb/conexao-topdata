using Access.Infrastructure.SQLite;
using Rayzer.Suporte;

namespace Unit.Tests.Suporte;

/// <summary>
/// Recuperação pelo suporte Rayzer (docs/58): o pedido só vale para a instalação, o login e o prazo dele,
/// e só a chave privada do suporte faz uma resposta que confere. Não existe senha mestra.
/// </summary>
public sealed class RecuperacaoPeloSuporteTests
{
    private const string Senha = "senha-da-chave-de-teste";
    private const string Instalacao = "instalacao-de-teste";
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly (string Privada, string Publica) Chaves = FerramentaDoSuporte.GerarChaves(Senha);

    private static (byte[] Pedido, byte[] Resposta) PedidoAssinado(string login = "ana.admin")
    {
        var pedido = RecuperacaoPeloSuporte.Pedido(Instalacao, login, Agora);
        var texto = RecuperacaoPeloSuporte.ParaTexto(pedido);
        var assinado = FerramentaDoSuporte.Assinar(Chaves.Privada, Senha, texto)!.Value;
        return (pedido, RecuperacaoPeloSuporte.DoTexto(assinado.Resposta, RecuperacaoPeloSuporte.BytesDaResposta)!);
    }

    [Fact]
    public void Resposta_do_suporte_confere_para_o_mesmo_pedido_login_e_instalacao()
    {
        var (pedido, resposta) = PedidoAssinado();

        Assert.True(RecuperacaoPeloSuporte.Confere(pedido, resposta, Instalacao, "ana.admin", Agora, [Chaves.Publica]));
        Assert.True(RecuperacaoPeloSuporte.Confere(pedido, resposta, Instalacao, "ANA.ADMIN", Agora, [Chaves.Publica]));
    }

    [Fact]
    public void Resposta_nao_serve_para_outro_login_outra_maquina_ou_depois_do_prazo()
    {
        var (pedido, resposta) = PedidoAssinado();

        Assert.False(RecuperacaoPeloSuporte.Confere(pedido, resposta, Instalacao, "outro.admin", Agora, [Chaves.Publica]));
        Assert.False(RecuperacaoPeloSuporte.Confere(pedido, resposta, "outra-instalacao", "ana.admin", Agora, [Chaves.Publica]));
        Assert.False(RecuperacaoPeloSuporte.Confere(
            pedido, resposta, Instalacao, "ana.admin", Agora + RecuperacaoPeloSuporte.Validade + TimeSpan.FromSeconds(1), [Chaves.Publica]));
    }

    [Fact]
    public void Outra_chave_ou_resposta_alterada_nao_confere()
    {
        var (pedido, resposta) = PedidoAssinado();
        var outra = FerramentaDoSuporte.GerarChaves("outra-senha-qualquer");
        resposta[10] ^= 0x01;

        Assert.False(RecuperacaoPeloSuporte.Confere(pedido, PedidoAssinado().Resposta, Instalacao, "ana.admin", Agora, [outra.PublicaBase64]));
        Assert.False(RecuperacaoPeloSuporte.Confere(pedido, resposta, Instalacao, "ana.admin", Agora, [Chaves.Publica]));
    }

    [Fact]
    public void Sem_chave_publica_nada_confere()
    {
        var (pedido, resposta) = PedidoAssinado();

        Assert.False(RecuperacaoPeloSuporte.Confere(pedido, resposta, Instalacao, "ana.admin", Agora, []));
    }

    [Fact]
    public void Texto_tolera_minuscula_espaco_e_as_confusoes_de_leitura()
    {
        var pedido = RecuperacaoPeloSuporte.Pedido(Instalacao, "ana.admin", Agora);
        var texto = RecuperacaoPeloSuporte.ParaTexto(pedido);
        var baguncado = texto.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal).Replace('0', 'o').Replace('1', 'l');

        Assert.Equal(pedido, RecuperacaoPeloSuporte.DoTexto(baguncado, RecuperacaoPeloSuporte.BytesDoPedido));
        Assert.Null(RecuperacaoPeloSuporte.DoTexto("U!@#", RecuperacaoPeloSuporte.BytesDoPedido));
    }

    [Fact]
    public void Chave_privada_sem_a_senha_certa_nao_assina()
    {
        var texto = RecuperacaoPeloSuporte.ParaTexto(RecuperacaoPeloSuporte.Pedido(Instalacao, "ana.admin", Agora));

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => FerramentaDoSuporte.Assinar(Chaves.Privada, "senha-errada", texto));
        Assert.Contains("ENCRYPTED PRIVATE KEY", Chaves.Privada, StringComparison.Ordinal);
    }

    [Fact]
    public void Versao_publicada_nao_tem_chave_privada_nem_senha_mestra()
    {
        // Só chaves públicas, e elas só conferem: nenhuma assina.
        foreach (var publica in ChavesDoSuporte.Publicas)
        {
            using var ecdsa = System.Security.Cryptography.ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publica), out _);
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => ecdsa.ExportParameters(includePrivateParameters: true));
        }
    }
}
