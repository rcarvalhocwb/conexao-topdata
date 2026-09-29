namespace Desktop.ViewModels.GemeoDigital;

/// <summary>O momento em que a catraca desenhada está.</summary>
/// <remarks>
/// É um resumo <b>visual</b> da máquina de estados real (DeviceStateMachine, no worker), e
/// não uma cópia dela: aqui só importa o que se vê. "Configurando", "Reconectando" e afins
/// aparecem todos como <see cref="SemComunicacao"/> ou <see cref="Livre"/>.
/// </remarks>
public enum EstadoDaCena
{
    /// <summary>Sem notícia da catraca: display apagado, nada responde.</summary>
    SemComunicacao,

    /// <summary>Travada, esperando alguém. Display com a mensagem padrão.</summary>
    Livre,

    /// <summary>Uma credencial está sendo apresentada a um leitor.</summary>
    LendoCredencial,

    /// <summary>Leitura feita; a borda está decidindo (na operação, milissegundos).</summary>
    Decidindo,

    /// <summary>Liberada: o braço solta e a catraca espera o giro.</summary>
    Liberada,

    /// <summary>Um terço de volta em andamento.</summary>
    Girando,

    /// <summary>Recusada: mensagem de negação no display, braço travado.</summary>
    Negada,
}

/// <summary>Onde a credencial foi apresentada.</summary>
public enum LeitorDaCena
{
    Nenhum,
    Qr,
    CartaoNaFrente,
    CartaoNaUrna,
}

/// <summary>Sentido do giro.</summary>
public enum SentidoDoGiro
{
    /// <summary>Quem chega pela frente do painel e atravessa.</summary>
    Entrada,

    /// <summary>O contrário. Liberar a saída ainda depende de confirmação (docs/32).</summary>
    Saida,
}

/// <summary>O que pode acontecer com a catraca desenhada.</summary>
public enum TipoDeSinal
{
    Conectou,
    PerdeuComunicacao,
    CredencialApresentada,
    AcessoLiberado,
    AcessoNegado,
    GiroConfirmado,

    /// <summary>Liberada, mas ninguém girou dentro do tempo (origem 5).</summary>
    TempoDeGiroEsgotado,
    UrnaCheia,
    UrnaEsvaziada,
}

/// <summary>Um acontecimento aplicado à cena, vindo de um cenário ou da catraca real.</summary>
public readonly record struct SinalDaCena(
    TipoDeSinal Tipo,
    LeitorDaCena Leitor = LeitorDaCena.Nenhum,
    SentidoDoGiro Sentido = SentidoDoGiro.Entrada)
{
    public static SinalDaCena Conectou() => new(TipoDeSinal.Conectou);

    public static SinalDaCena PerdeuComunicacao() => new(TipoDeSinal.PerdeuComunicacao);

    public static SinalDaCena Credencial(LeitorDaCena leitor) => new(TipoDeSinal.CredencialApresentada, leitor);

    public static SinalDaCena Liberado() => new(TipoDeSinal.AcessoLiberado);

    public static SinalDaCena Negado() => new(TipoDeSinal.AcessoNegado);

    public static SinalDaCena Giro(SentidoDoGiro sentido = SentidoDoGiro.Entrada) => new(TipoDeSinal.GiroConfirmado, Sentido: sentido);

    public static SinalDaCena TempoEsgotado() => new(TipoDeSinal.TempoDeGiroEsgotado);

    public static SinalDaCena UrnaCheia() => new(TipoDeSinal.UrnaCheia);

    public static SinalDaCena UrnaEsvaziada() => new(TipoDeSinal.UrnaEsvaziada);
}

/// <summary>Tudo o que a tela precisa para desenhar um quadro. Só números e textos.</summary>
public readonly record struct QuadroDaCena(
    EstadoDaCena Estado,
    LeitorDaCena Leitor,
    double AnguloDoRotor,
    double Aproximacao,
    double Travessia,
    bool AderecoVisivel,
    bool PessoaVisivel,
    bool LuzDeLiberado,
    bool LuzDeBloqueado,
    bool LuzDeFundoDoDisplay,
    string Linha1,
    string Linha2,
    bool UrnaCheia);

/// <summary>
/// A catraca desenhada, como máquina de estados <b>pura</b>: não tem relógio, timer nem
/// thread. Quem desenha passa o instante a cada quadro; quem testa passa o instante que
/// quiser.
/// </summary>
/// <remarks>
/// <para>
/// Regra que a tela depende: <b>nunca dois giros ao mesmo tempo</b>. Um giro que chega
/// durante outro entra na fila e começa quando o primeiro termina — duas passagens
/// aconteceram, e as duas aparecem.
/// </para>
/// <para>
/// Uma decisão que chega antes de a leitura terminar de ser mostrada (o caso normal na
/// operação: a borda decide em milissegundos) espera a animação da leitura, para o
/// operador ver a ordem certa: leu, decidiu, liberou.
/// </para>
/// </remarks>
public sealed class CenaDaCatraca
{
    /// <summary>Um terço de volta: o passo do tripé.</summary>
    public const double PassoDoRotor = 120;

    private readonly Queue<SentidoDoGiro> _girosNaFila = new();
    private TimeSpan _desde;
    private TimeSpan _agora;
    private SentidoDoGiro _sentidoDoGiro;
    private TipoDeSinal? _decisaoGuardada;
    private string _mensagemPadrao;
    private string? _mensagemTemporaria;
    private TimeSpan _mensagemTemporariaAte;

    public CenaDaCatraca(
        string mensagemPadrao,
        TimeSpan? duracaoDoGiro = null,
        TimeSpan? duracaoDaLeitura = null,
        TimeSpan? limiteDaLiberacao = null,
        bool comunicando = true)
    {
        _mensagemPadrao = mensagemPadrao ?? string.Empty;
        DuracaoDoGiro = duracaoDoGiro ?? TimeSpan.FromMilliseconds(900);
        DuracaoDaLeitura = duracaoDaLeitura ?? TimeSpan.FromMilliseconds(1100);
        LimiteDaLiberacao = limiteDaLiberacao ?? TimeSpan.FromSeconds(8);
        Estado = comunicando ? EstadoDaCena.Livre : EstadoDaCena.SemComunicacao;
    }

    public TimeSpan DuracaoDoGiro { get; }

    public TimeSpan DuracaoDaLeitura { get; }

    /// <summary>
    /// Quanto a liberação espera o giro antes de voltar a travar. Na catraca real é o tempo de
    /// acionamento; aqui vale para quando ninguém avisa o fim (o fluxo ao vivo avisa a
    /// decisão, não o giro).
    /// </summary>
    public TimeSpan LimiteDaLiberacao { get; set; }

    public EstadoDaCena Estado { get; private set; }

    public LeitorDaCena Leitor { get; private set; }

    /// <summary>Ângulo do rotor com os giros já concluídos, em graus.</summary>
    public double AnguloBase { get; private set; }

    public bool UrnaCheia { get; private set; }

    /// <summary>Giros concluídos desde o começo da cena.</summary>
    public int Giros { get; private set; }

    public int Liberacoes { get; private set; }

    public int Negacoes { get; private set; }

    /// <summary>Liberações que terminaram sem giro (a pessoa desistiu).</summary>
    public int LiberacoesSemGiro { get; private set; }

    /// <summary>Giros esperando o atual terminar.</summary>
    public int GirosNaFila => _girosNaFila.Count;

    /// <summary>Muda a mensagem que aparece com a catraca livre.</summary>
    public void DefinirMensagemPadrao(string mensagem) => _mensagemPadrao = mensagem ?? string.Empty;

    /// <summary>Mostra um texto no display por um tempo, como a mensagem temporária da catraca.</summary>
    public void MostrarMensagem(string texto, TimeSpan agora, TimeSpan duracao)
    {
        Avancar(agora);
        _mensagemTemporaria = texto ?? string.Empty;
        _mensagemTemporariaAte = agora + duracao;
    }

    /// <summary>Volta ao começo: livre (ou sem comunicação), rotor parado, sem fila.</summary>
    public void Reiniciar(TimeSpan agora, bool comunicando = true)
    {
        _girosNaFila.Clear();
        _decisaoGuardada = null;
        _mensagemTemporaria = null;
        UrnaCheia = false;
        Leitor = LeitorDaCena.Nenhum;
        _agora = agora;
        Mudar(comunicando ? EstadoDaCena.Livre : EstadoDaCena.SemComunicacao, agora);
    }

    /// <summary>
    /// Aplica um acontecimento. Devolve falso, sem mudar nada, quando ele não cabe no
    /// momento (por exemplo, uma leitura com a catraca liberada).
    /// </summary>
    public bool Aplicar(SinalDaCena sinal, TimeSpan agora)
    {
        Avancar(agora);

        switch (sinal.Tipo)
        {
            case TipoDeSinal.PerdeuComunicacao:
                // O giro em andamento termina onde a catraca estava: não se inventa meio giro.
                if (Estado is EstadoDaCena.Girando)
                {
                    ConcluirGiro();
                }

                _girosNaFila.Clear();
                _decisaoGuardada = null;
                Leitor = LeitorDaCena.Nenhum;
                return Mudar(EstadoDaCena.SemComunicacao, agora);

            case TipoDeSinal.Conectou:
                return Estado is EstadoDaCena.SemComunicacao && Mudar(EstadoDaCena.Livre, agora);

            case TipoDeSinal.CredencialApresentada:
                if (Estado is not (EstadoDaCena.Livre or EstadoDaCena.Negada))
                {
                    return false;
                }

                Leitor = sinal.Leitor;
                _decisaoGuardada = null;
                return Mudar(EstadoDaCena.LendoCredencial, agora);

            case TipoDeSinal.AcessoLiberado:
            case TipoDeSinal.AcessoNegado:
                if (Estado is EstadoDaCena.LendoCredencial)
                {
                    _decisaoGuardada = sinal.Tipo;
                    return true;
                }

                if (Estado is not (EstadoDaCena.Decidindo or EstadoDaCena.Livre))
                {
                    return false;
                }

                Decidir(sinal.Tipo, agora);
                return true;

            case TipoDeSinal.GiroConfirmado:
                if (Estado is EstadoDaCena.Girando)
                {
                    _girosNaFila.Enqueue(sinal.Sentido);
                    return true;
                }

                // Livre: um giro sem liberação vista aqui (liberação manual, ou a decisão
                // chegou antes de a tela abrir). A passagem aconteceu; o desenho mostra.
                if (Estado is not (EstadoDaCena.Liberada or EstadoDaCena.Livre))
                {
                    return false;
                }

                _sentidoDoGiro = sinal.Sentido;
                return Mudar(EstadoDaCena.Girando, agora);

            case TipoDeSinal.TempoDeGiroEsgotado:
                if (Estado is not EstadoDaCena.Liberada)
                {
                    return false;
                }

                LiberacoesSemGiro++;
                Leitor = LeitorDaCena.Nenhum;
                return Mudar(EstadoDaCena.Livre, agora);

            case TipoDeSinal.UrnaCheia:
                UrnaCheia = true;
                return true;

            case TipoDeSinal.UrnaEsvaziada:
                UrnaCheia = false;
                return true;

            default:
                return false;
        }
    }

    /// <summary>Faz o tempo passar: termina leituras, giros e negações que já acabaram.</summary>
    public void Avancar(TimeSpan agora)
    {
        if (agora < _agora)
        {
            // Relógio que volta não desfaz nada.
            return;
        }

        _agora = agora;

        // Laço: um único avanço grande pode atravessar várias etapas (leitura → decisão →
        // giro → próximo giro da fila).
        for (var i = 0; i < 16; i++)
        {
            var decorrido = agora - _desde;

            switch (Estado)
            {
                case EstadoDaCena.LendoCredencial when decorrido >= DuracaoDaLeitura:
                    var fim = _desde + DuracaoDaLeitura;
                    if (_decisaoGuardada is { } decisao)
                    {
                        _decisaoGuardada = null;
                        Decidir(decisao, fim);
                    }
                    else
                    {
                        Mudar(EstadoDaCena.Decidindo, fim);
                    }

                    continue;

                case EstadoDaCena.Girando when decorrido >= DuracaoDoGiro:
                    var terminou = _desde + DuracaoDoGiro;
                    ConcluirGiro();

                    if (_girosNaFila.TryDequeue(out var proximo))
                    {
                        _sentidoDoGiro = proximo;
                        Mudar(EstadoDaCena.Girando, terminou);
                    }
                    else
                    {
                        Leitor = LeitorDaCena.Nenhum;
                        Mudar(EstadoDaCena.Livre, terminou);
                    }

                    continue;

                case EstadoDaCena.Negada when decorrido >= Display2x16.ExibicaoDaNegacao:
                    Leitor = LeitorDaCena.Nenhum;
                    Mudar(EstadoDaCena.Livre, _desde + Display2x16.ExibicaoDaNegacao);
                    continue;

                case EstadoDaCena.Liberada when decorrido >= LimiteDaLiberacao:
                    LiberacoesSemGiro++;
                    Leitor = LeitorDaCena.Nenhum;
                    Mudar(EstadoDaCena.Livre, _desde + LimiteDaLiberacao);
                    continue;

                default:
                    break;
            }

            break;
        }

        if (_mensagemTemporaria is not null && agora >= _mensagemTemporariaAte)
        {
            _mensagemTemporaria = null;
        }
    }

    /// <summary>O quadro de agora, para desenhar.</summary>
    public QuadroDaCena Quadro(TimeSpan agora)
    {
        Avancar(agora);
        var decorrido = (agora - _desde).TotalMilliseconds;

        var angulo = AnguloBase;
        var travessia = 0.0;
        if (Estado is EstadoDaCena.Girando)
        {
            var t = Suavizar(Math.Clamp(decorrido / DuracaoDoGiro.TotalMilliseconds, 0, 1));
            angulo += Sinal(_sentidoDoGiro) * PassoDoRotor * t;
            travessia = t;
        }

        var aproximacao = Estado switch
        {
            EstadoDaCena.LendoCredencial => Suavizar(Math.Clamp(decorrido / (DuracaoDaLeitura.TotalMilliseconds * 0.7), 0, 1)),
            EstadoDaCena.Decidindo or EstadoDaCena.Liberada or EstadoDaCena.Negada => 1,
            _ => 0,
        };

        var aderecoVisivel = Leitor is not LeitorDaCena.Nenhum &&
            Estado is EstadoDaCena.LendoCredencial or EstadoDaCena.Decidindo or EstadoDaCena.Liberada or EstadoDaCena.Negada;
        var pessoaVisivel = Leitor is not LeitorDaCena.Nenhum &&
            Estado is EstadoDaCena.LendoCredencial or EstadoDaCena.Decidindo or EstadoDaCena.Liberada or EstadoDaCena.Negada or EstadoDaCena.Girando;

        var texto = Estado switch
        {
            EstadoDaCena.SemComunicacao => string.Empty,
            EstadoDaCena.Negada => Display2x16.MensagemDeNegacao,
            _ => _mensagemTemporaria ?? _mensagemPadrao,
        };
        var (linha1, linha2) = Display2x16.Formatar(texto);

        return new QuadroDaCena(
            Estado,
            Leitor,
            angulo,
            aproximacao,
            travessia,
            aderecoVisivel,
            pessoaVisivel,
            LuzDeLiberado: Estado is EstadoDaCena.Liberada or EstadoDaCena.Girando,
            LuzDeBloqueado: Estado is EstadoDaCena.Negada,
            LuzDeFundoDoDisplay: Estado is not EstadoDaCena.SemComunicacao,
            linha1,
            linha2,
            UrnaCheia);
    }

    /// <summary>+1 para a saída, -1 para a entrada: ver <see cref="ModeloDaFit4.EixoDoRotor"/>.</summary>
    public static int Sinal(SentidoDoGiro sentido) => sentido is SentidoDoGiro.Entrada ? -1 : 1;

    // Começa e termina devagar, como um braço empurrado.
    private static double Suavizar(double t) => t * t * (3 - (2 * t));

    private void Decidir(TipoDeSinal decisao, TimeSpan agora)
    {
        if (decisao is TipoDeSinal.AcessoLiberado)
        {
            Liberacoes++;
            Mudar(EstadoDaCena.Liberada, agora);
        }
        else
        {
            Negacoes++;
            Mudar(EstadoDaCena.Negada, agora);
        }
    }

    private void ConcluirGiro()
    {
        AnguloBase = (AnguloBase + (Sinal(_sentidoDoGiro) * PassoDoRotor)) % 360;
        Giros++;
    }

    private bool Mudar(EstadoDaCena estado, TimeSpan desde)
    {
        Estado = estado;
        _desde = desde;
        return true;
    }
}
