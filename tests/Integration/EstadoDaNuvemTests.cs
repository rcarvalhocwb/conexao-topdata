using Edge.Supervisor;
using Sync.Core;

namespace Integration.Tests;

/// <summary>
/// P0-02: internet, nuvem e fila saem da última tentativa recente, nunca de um sucesso antigo.
/// Os cinco casos do enunciado: sem tentativa, sucesso recente, sucesso antigo, recusa de credencial e
/// falta de rede. Mais segredo ausente, fila com itens e recuperação depois de falha.
/// </summary>
public sealed class EstadoDaNuvemTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static EstadoDaNuvem Configurada() => new() { Configurada = true, Intervalo = TimeSpan.FromSeconds(30) };

    [Fact]
    public void Sem_nenhuma_tentativa_o_estado_e_conectando_e_a_internet_e_desconhecida()
    {
        var estado = Configurada();

        Assert.Equal(NuvemMedida.Conectando, estado.Nuvem(Agora));
        Assert.Equal(InternetMedida.Desconhecida, estado.Internet(Agora));
        Assert.Equal(SincronizacaoMedida.Desconhecida, estado.Sincronizacao(0));
    }

    [Fact]
    public void Sucesso_recente_prova_internet_nuvem_e_fila_sincronizada()
    {
        var estado = Configurada();
        estado.RegistrarSucesso(Agora.AddSeconds(-20));

        Assert.Equal(NuvemMedida.Conectada, estado.Nuvem(Agora));
        Assert.Equal(InternetMedida.Disponivel, estado.Internet(Agora));
        Assert.Equal(SincronizacaoMedida.Sincronizada, estado.Sincronizacao(0));
    }

    [Fact]
    public void Sucesso_antigo_nao_diz_que_a_internet_esta_de_pe_nem_fora()
    {
        var estado = Configurada();
        estado.RegistrarSucesso(Agora.AddMinutes(-10));

        // Antes, um sucesso de dez minutos atrás virava "sem internet". Agora não se sabe.
        Assert.Equal(InternetMedida.Desconhecida, estado.Internet(Agora));
        Assert.Equal(NuvemMedida.Desconhecida, estado.Nuvem(Agora));
    }

    [Fact]
    public void Credencial_recusada_mostra_que_a_internet_funciona_e_a_nuvem_recusou_a_chave()
    {
        var estado = Configurada();
        estado.RegistrarFalha("tentativas: 1 adiada(s)", TipoDeFalha.Autenticacao, Agora.AddSeconds(-5));

        // O destino respondeu: a rede está de pé. Quem recusou foi a credencial.
        Assert.Equal(InternetMedida.Disponivel, estado.Internet(Agora));
        Assert.Equal(NuvemMedida.FalhaDeAutenticacao, estado.Nuvem(Agora));
        Assert.Equal(SincronizacaoMedida.Falha, estado.Sincronizacao(3));
    }

    [Fact]
    public void Falta_de_rede_mostra_internet_indisponivel_e_nuvem_indisponivel()
    {
        var estado = Configurada();
        estado.RegistrarFalha("cartões: Connection refused", TipoDeFalha.Rede, Agora.AddSeconds(-5));

        Assert.Equal(InternetMedida.Indisponivel, estado.Internet(Agora));
        Assert.Equal(NuvemMedida.Indisponivel, estado.Nuvem(Agora));
    }

    [Fact]
    public void Erro_de_servidor_nao_e_falta_de_internet()
    {
        var estado = Configurada();
        estado.RegistrarFalha("cartões: HTTP 503", TipoDeFalha.Servidor, Agora.AddSeconds(-5));

        Assert.Equal(InternetMedida.Disponivel, estado.Internet(Agora));
        Assert.Equal(NuvemMedida.Indisponivel, estado.Nuvem(Agora));
    }

    [Fact]
    public void Sem_segredo_nada_e_tentado_e_o_estado_diz_segredo_ausente()
    {
        var estado = Configurada();
        estado.SegredoAusente = true;
        estado.RegistrarFalha("sem o segredo", TipoDeFalha.Outra, Agora);

        Assert.Equal(NuvemMedida.SegredoAusente, estado.Nuvem(Agora));
        Assert.Equal(InternetMedida.Desconhecida, estado.Internet(Agora));
    }

    [Fact]
    public void Nao_configurada_nao_aparece_como_falha()
    {
        var estado = new EstadoDaNuvem();

        Assert.Equal(NuvemMedida.NaoConfigurada, estado.Nuvem(Agora));
        Assert.Equal(SincronizacaoMedida.Desconhecida, estado.Sincronizacao(5));
    }

    [Fact]
    public void Fila_com_itens_e_sucesso_recente_fica_pendente_e_em_andamento_vence()
    {
        var estado = Configurada();
        estado.RegistrarSucesso(Agora.AddSeconds(-10));

        Assert.Equal(SincronizacaoMedida.Pendente, estado.Sincronizacao(4));

        estado.EmAndamento = true;
        Assert.Equal(SincronizacaoMedida.Processando, estado.Sincronizacao(4));
    }

    [Fact]
    public void Falha_na_fila_continua_falha_mesmo_com_itens_e_so_volta_com_sucesso()
    {
        var estado = Configurada();
        estado.RegistrarSucesso(Agora.AddMinutes(-1));
        estado.RegistrarFalha("tentativas: 2 adiada(s)", TipoDeFalha.Rede, Agora);

        Assert.Equal(SincronizacaoMedida.Falha, estado.Sincronizacao(2));

        estado.RegistrarSucesso(Agora.AddSeconds(1));
        Assert.Null(estado.UltimaFalha);
        Assert.Equal(SincronizacaoMedida.Sincronizada, estado.Sincronizacao(0));
        Assert.Equal(NuvemMedida.Conectada, estado.Nuvem(Agora.AddSeconds(1)));
    }

    [Fact]
    public void A_janela_acompanha_o_intervalo_de_sincronizacao_lento()
    {
        var estado = new EstadoDaNuvem { Configurada = true, Intervalo = TimeSpan.FromMinutes(10) };
        estado.RegistrarSucesso(Agora.AddMinutes(-15));

        // Com intervalo de 10 min, a tentativa de 15 min atrás ainda vale (janela = 2 × intervalo).
        Assert.Equal(TimeSpan.FromMinutes(20), estado.JanelaDeValidade);
        Assert.Equal(NuvemMedida.Conectada, estado.Nuvem(Agora));
    }
}
