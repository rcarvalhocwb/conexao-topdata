using Access.Application.Devices;
using Desktop.ViewModels;
using Edge.Worker;

namespace Unit.Tests;

/// <summary>
/// Docs/62: as frases que o operador lê quando a catraca não conecta. Cada uma diz o caso e o que fazer,
/// com o IP e a porta reais, para ele não precisar de outra ferramenta.
/// </summary>
public sealed class ConexaoDaCatracaTests
{
    [Fact]
    public void Porta_ja_aberta_diz_qual_porta_e_o_que_fazer()
    {
        var frase = DiagnosticoDaPorta.Mensagem(AdapterStatus.PortaJaAberta, 3570);

        Assert.NotNull(frase);
        Assert.Contains("porta 3570 já está aberta", frase, StringComparison.Ordinal);
        Assert.Contains("reinicie o serviço", frase, StringComparison.Ordinal);
    }

    [Fact]
    public void Erro_de_comunicacao_na_abertura_diz_qual_porta()
    {
        var frase = DiagnosticoDaPorta.Mensagem(AdapterStatus.ErroDeComunicacao, 3571);

        Assert.NotNull(frase);
        Assert.Contains("porta 3571", frase, StringComparison.Ordinal);
    }

    [Fact]
    public void Casos_sem_frase_propria_ficam_para_o_texto_geral()
    {
        Assert.Null(DiagnosticoDaPorta.Mensagem(AdapterStatus.Ok, 3570));
        Assert.Null(DiagnosticoDaPorta.Mensagem(AdapterStatus.FalhaDeDependencia, 3570));
    }

    [Fact]
    public void Catraca_que_ja_atende_nao_mostra_instrucao()
    {
        Assert.Equal(string.Empty, Textos.ComoConectar(3, ["192.168.0.5"], 3570, emOperacao: true));
    }

    [Fact]
    public void Sem_endereco_de_rede_a_instrucao_manda_conferir_o_cabo()
    {
        var texto = Textos.ComoConectar(3, [], 3570, emOperacao: false);

        Assert.Contains("endereço de rede ativo", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Com_um_endereco_a_instrucao_traz_ip_porta_e_numero_do_equipamento()
    {
        var texto = Textos.ComoConectar(3, ["192.168.0.5"], 3570, emOperacao: false);

        Assert.Contains("Na catraca 03", texto, StringComparison.Ordinal);
        Assert.Contains("\"IP do servidor\": 192.168.0.5", texto, StringComparison.Ordinal);
        Assert.Contains("\"porta do servidor\": 3570", texto, StringComparison.Ordinal);
        Assert.Contains("\"número do equipamento\": 3", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("mesma rede", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Com_dois_enderecos_a_instrucao_pede_a_mesma_rede_da_catraca()
    {
        var texto = Textos.ComoConectar(3, ["10.0.0.5", "192.168.0.5"], 3570, emOperacao: false);

        Assert.Contains("mesma rede da catraca", texto, StringComparison.Ordinal);
        Assert.Contains("10.0.0.5, 192.168.0.5", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Resumo_da_rede_sem_conexao_diz_que_nenhuma_chegou()
    {
        var resumo = Textos.ResumoDaRede(["192.168.0.5"], [], [3570]);

        Assert.Contains("IP 192.168.0.5, porta 3570", resumo, StringComparison.Ordinal);
        Assert.Contains("Nenhuma conexão de catraca chegou ainda", resumo, StringComparison.Ordinal);
    }

    [Fact]
    public void Resumo_da_rede_mostra_de_onde_chegou_a_conexao()
    {
        var resumo = Textos.ResumoDaRede(["192.168.0.5"], ["192.168.0.50"], [3570]);

        Assert.Contains("Conexão chegando de: 192.168.0.50", resumo, StringComparison.Ordinal);
    }

    [Fact]
    public void Resumo_da_rede_sem_endereco_diz_isso()
    {
        Assert.Contains("sem endereço de rede ativo", Textos.ResumoDaRede([], [], []), StringComparison.Ordinal);
    }

    [Fact]
    public void Conexao_sem_catraca_atendendo_gera_aviso_com_o_ip()
    {
        var aviso = Textos.AvisoDeConexaoSemCatraca(["192.168.0.50"], algumaEmOperacao: false);

        Assert.NotNull(aviso);
        Assert.Contains("192.168.0.50", aviso, StringComparison.Ordinal);
        Assert.Contains("número do equipamento", aviso, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_conexao_ou_com_catraca_atendendo_nao_ha_aviso()
    {
        Assert.Null(Textos.AvisoDeConexaoSemCatraca([], algumaEmOperacao: false));
        Assert.Null(Textos.AvisoDeConexaoSemCatraca(["192.168.0.50"], algumaEmOperacao: true));
    }
}
