using Desktop.ViewModels.GemeoDigital;

namespace Unit.Tests.Gemeo;

/// <summary>A catraca desenhada como máquina de estados: tempo explícito, sem relógio.</summary>
public sealed class CenaDaCatracaTests
{
    private static readonly TimeSpan Giro = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan Leitura = TimeSpan.FromMilliseconds(1100);

    private static TimeSpan S(double segundos) => TimeSpan.FromSeconds(segundos);

    private static CenaDaCatraca Cena() => new("Aproxime o ingresso", Giro, Leitura, S(8));

    [Fact]
    public void Decisao_que_chega_durante_a_leitura_espera_a_leitura_terminar()
    {
        var cena = Cena();

        Assert.True(cena.Aplicar(SinalDaCena.Credencial(LeitorDaCena.Qr), S(0)));
        Assert.True(cena.Aplicar(SinalDaCena.Liberado(), S(0.1)));

        Assert.Equal(EstadoDaCena.LendoCredencial, cena.Quadro(S(0.5)).Estado);
        Assert.Equal(EstadoDaCena.Liberada, cena.Quadro(S(1.2)).Estado);
        Assert.Equal(1, cena.Liberacoes);
    }

    [Fact]
    public void Giro_de_entrada_e_um_terco_de_volta_no_sentido_negativo()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(0));
        cena.Aplicar(SinalDaCena.Giro(SentidoDoGiro.Entrada), S(1));

        var meio = cena.Quadro(S(1.45));
        Assert.Equal(EstadoDaCena.Girando, meio.Estado);
        Assert.InRange(meio.AnguloDoRotor, -119, -1);
        Assert.True(meio.LuzDeLiberado);

        var fim = cena.Quadro(S(2));
        Assert.Equal(EstadoDaCena.Livre, fim.Estado);
        Assert.Equal(-CenaDaCatraca.PassoDoRotor, fim.AnguloDoRotor, 6);
        Assert.Equal(1, cena.Giros);
        Assert.False(fim.LuzDeLiberado);
    }

    [Fact]
    public void Nunca_dois_giros_ao_mesmo_tempo_o_segundo_espera_na_fila()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(0));
        cena.Aplicar(SinalDaCena.Giro(), S(1));
        Assert.True(cena.Aplicar(SinalDaCena.Giro(), S(1.2)));

        Assert.Equal(EstadoDaCena.Girando, cena.Quadro(S(1.3)).Estado);
        Assert.Equal(1, cena.GirosNaFila);

        // Terminou o primeiro: o segundo começa na hora, sem pular para o fim.
        var segundo = cena.Quadro(S(2.0));
        Assert.Equal(EstadoDaCena.Girando, segundo.Estado);
        Assert.Equal(0, cena.GirosNaFila);
        Assert.InRange(segundo.AnguloDoRotor, -240, -120);

        var fim = cena.Quadro(S(3));
        Assert.Equal(EstadoDaCena.Livre, fim.Estado);
        Assert.Equal(2, cena.Giros);
        Assert.Equal(-240, fim.AnguloDoRotor, 6);
    }

    [Fact]
    public void Negada_mostra_a_mensagem_da_catraca_por_tres_segundos()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Credencial(LeitorDaCena.Qr), S(0));
        cena.Aplicar(SinalDaCena.Negado(), S(0.2));

        var negada = cena.Quadro(S(1.5));
        Assert.Equal(EstadoDaCena.Negada, negada.Estado);
        Assert.True(negada.LuzDeBloqueado);
        Assert.Equal(("Acesso nao autor", "izado           "), (negada.Linha1, negada.Linha2));

        var livre = cena.Quadro(S(1.1 + 3.01));
        Assert.Equal(EstadoDaCena.Livre, livre.Estado);
        Assert.Equal("Aproxime o ingre", livre.Linha1);
        Assert.Equal(0, cena.Giros);
    }

    [Fact]
    public void Liberada_sem_giro_volta_a_travar_e_conta_a_parte()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(0));
        Assert.True(cena.Aplicar(SinalDaCena.TempoEsgotado(), S(3)));

        Assert.Equal(EstadoDaCena.Livre, cena.Estado);
        Assert.Equal(1, cena.LiberacoesSemGiro);
        Assert.Equal(0, cena.Giros);
    }

    [Fact]
    public void Liberada_sem_ninguem_avisar_volta_a_travar_no_limite()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(0));

        Assert.Equal(EstadoDaCena.Liberada, cena.Quadro(S(7.9)).Estado);
        Assert.Equal(EstadoDaCena.Livre, cena.Quadro(S(8.1)).Estado);
        Assert.Equal(1, cena.LiberacoesSemGiro);
    }

    [Fact]
    public void Leitura_com_a_catraca_liberada_e_recusada_sem_mudar_nada()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(0));

        Assert.False(cena.Aplicar(SinalDaCena.Credencial(LeitorDaCena.Qr), S(0.5)));
        Assert.Equal(EstadoDaCena.Liberada, cena.Estado);
        Assert.Equal(LeitorDaCena.Nenhum, cena.Leitor);
    }

    [Fact]
    public void Sem_comunicacao_apaga_o_display_e_nao_aceita_leitura()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.PerdeuComunicacao(), S(0));

        var quadro = cena.Quadro(S(0.1));
        Assert.Equal(EstadoDaCena.SemComunicacao, quadro.Estado);
        Assert.False(quadro.LuzDeFundoDoDisplay);
        Assert.Equal(new string(' ', 16), quadro.Linha1);
        Assert.False(cena.Aplicar(SinalDaCena.Credencial(LeitorDaCena.Qr), S(0.2)));

        Assert.True(cena.Aplicar(SinalDaCena.Conectou(), S(1)));
        Assert.Equal(EstadoDaCena.Livre, cena.Estado);
    }

    [Fact]
    public void Queda_no_meio_do_giro_termina_o_giro_onde_estava_sem_meio_giro()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(0));
        cena.Aplicar(SinalDaCena.Giro(), S(1));
        cena.Aplicar(SinalDaCena.PerdeuComunicacao(), S(1.3));

        Assert.Equal(-120, cena.Quadro(S(1.4)).AnguloDoRotor, 6);
        Assert.Equal(1, cena.Giros);
    }

    [Fact]
    public void Relogio_que_volta_nao_desfaz_nada()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Liberado(), S(5));
        cena.Aplicar(SinalDaCena.Giro(), S(6));
        cena.Quadro(S(7));

        cena.Quadro(S(2));
        Assert.Equal(EstadoDaCena.Livre, cena.Estado);
        Assert.Equal(1, cena.Giros);
    }

    [Fact]
    public void Adereco_so_aparece_com_leitor_conhecido()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.Credencial(LeitorDaCena.Nenhum), S(0));
        Assert.False(cena.Quadro(S(0.5)).AderecoVisivel);

        cena.Reiniciar(S(1));
        cena.Aplicar(SinalDaCena.Credencial(LeitorDaCena.CartaoNaUrna), S(1));
        var quadro = cena.Quadro(S(1.5));
        Assert.True(quadro.AderecoVisivel);
        Assert.InRange(quadro.Aproximacao, 0.01, 0.99);
    }

    [Fact]
    public void Mensagem_temporaria_some_no_tempo_certo()
    {
        var cena = Cena();
        cena.MostrarMensagem("Portao B", S(0), S(2));

        Assert.Equal("Portao B        ", cena.Quadro(S(1)).Linha1);
        Assert.Equal("Aproxime o ingre", cena.Quadro(S(2.1)).Linha1);
    }

    [Fact]
    public void Urna_cheia_fica_marcada_ate_esvaziar()
    {
        var cena = Cena();
        cena.Aplicar(SinalDaCena.UrnaCheia(), S(0));
        Assert.True(cena.Quadro(S(1)).UrnaCheia);

        cena.Aplicar(SinalDaCena.UrnaEsvaziada(), S(2));
        Assert.False(cena.Quadro(S(3)).UrnaCheia);
    }
}
