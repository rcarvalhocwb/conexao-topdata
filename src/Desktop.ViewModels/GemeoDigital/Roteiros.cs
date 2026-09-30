namespace Desktop.ViewModels.GemeoDigital;

/// <summary>Um passo de um cenário: quando, o que acontece e a frase que explica.</summary>
/// <param name="Em">Instante desde o começo do cenário.</param>
/// <param name="Sinal">O acontecimento; nulo quando o passo só explica.</param>
/// <param name="Narracao">Frase em português de operador.</param>
/// <param name="Destaque">A peça que a frase comenta, para a tela destacar.</param>
public sealed record PassoDoRoteiro(TimeSpan Em, SinalDaCena? Sinal, string Narracao, PecaDaCatraca? Destaque = null);

/// <summary>Um cenário de demonstração, do começo ao fim.</summary>
/// <param name="Nome">Título curto.</param>
/// <param name="Descricao">Uma frase: o que o cenário ensina.</param>
/// <param name="Passos">Os passos, em ordem de tempo.</param>
/// <param name="CodigoDeTeste">
/// Código do modo simulação (installer/simulacao.exemplo.json) que produz o mesmo resultado
/// na catraca simulada de verdade; nulo quando não há como reproduzir por leitura.
/// </param>
/// <param name="NaUrna">Na catraca simulada, passar o código na fenda da urna.</param>
/// <param name="Gira">Na catraca simulada, a pessoa gira se for liberada.</param>
public sealed record RoteiroDeDemonstracao(
    string Nome,
    string Descricao,
    IReadOnlyList<PassoDoRoteiro> Passos,
    string? CodigoDeTeste = null,
    bool NaUrna = false,
    bool Gira = true)
{
    /// <summary>Quanto dura, até o último passo.</summary>
    public TimeSpan Duracao => Passos.Count == 0 ? TimeSpan.Zero : Passos[^1].Em;

    /// <summary>Pode ser repetido na catraca simulada pelo serviço.</summary>
    public bool TemCodigoDeTeste => CodigoDeTeste is not null;
}

/// <summary>Os cenários que o gêmeo sabe mostrar.</summary>
/// <remarks>
/// Cada frase diz o que o sistema faz <b>hoje</b>. Onde a função depende de confirmação da
/// Topdata (recolher o cartão, urna cheia), o cenário diz isso em vez de mostrar como se
/// existisse.
/// </remarks>
public static class Roteiros
{
    private static TimeSpan S(double segundos) => TimeSpan.FromSeconds(segundos);

    public static RoteiroDeDemonstracao QrValido { get; } = new(
        "QR válido: a pessoa passa",
        "O caminho normal, do celular ao giro.",
        [
            new(S(0), SinalDaCena.Credencial(LeitorDaCena.Qr),
                "A pessoa aproxima do leitor da tampa o celular com o QR do ingresso.", PecaDaCatraca.LeitorQr),
            new(S(1.4), SinalDaCena.Liberado(),
                "A borda confere o código na base local e libera: o braço solta. O verde no desenho só marca o momento.",
                PecaDaCatraca.Rotor),
            new(S(2.8), SinalDaCena.Giro(),
                "A pessoa empurra o braço. O sensor de giro avisa a catraca: só agora a passagem conta.", PecaDaCatraca.Rotor),
            new(S(4.2), null,
                "A catraca trava de novo e volta a mostrar a mensagem padrão.", PecaDaCatraca.Display),
        ],
        CodigoDeTeste: "1000000001");

    public static RoteiroDeDemonstracao CodigoDesconhecido { get; } = new(
        "Código desconhecido: negado",
        "O que a pessoa vê quando o código não vale.",
        [
            new(S(0), SinalDaCena.Credencial(LeitorDaCena.Qr),
                "A pessoa aproxima um QR que não está na base.", PecaDaCatraca.LeitorQr),
            new(S(1.4), SinalDaCena.Negado(),
                "A borda recusa: \"Acesso nao autorizado\" no display por 3 segundos, e o braço não solta. O X no desenho só marca o momento.",
                PecaDaCatraca.Display),
            new(S(4.8), null,
                "Passados os 3 segundos, o display volta à mensagem padrão e o leitor aceita a próxima leitura.",
                PecaDaCatraca.Display),
        ],
        CodigoDeTeste: "9999999999");

    public static RoteiroDeDemonstracao LiberadoSemGiro { get; } = new(
        "Liberada, mas a pessoa desiste",
        "Liberar não é passar: sem giro, não conta como entrada.",
        [
            new(S(0), SinalDaCena.Credencial(LeitorDaCena.Qr),
                "A pessoa aproxima o QR de um ingresso válido.", PecaDaCatraca.LeitorQr),
            new(S(1.4), SinalDaCena.Liberado(),
                "Liberada: braço solto.", PecaDaCatraca.Rotor),
            new(S(3.2), null,
                "A pessoa não passa. A catraca continua esperando o giro.", PecaDaCatraca.Rotor),
            new(S(5.6), SinalDaCena.TempoEsgotado(),
                "Acaba o tempo de acionamento. Fica registrado \"liberado sem giro\", fora da contagem de entradas.",
                PecaDaCatraca.Rotor),
        ],
        CodigoDeTeste: "1000000002",
        Gira: false);

    public static RoteiroDeDemonstracao CartaoNaUrna { get; } = new(
        "Cartão da bilheteria na urna",
        "O cartão é lido na fenda da urna, pelo leitor 2.",
        [
            new(S(0), SinalDaCena.Credencial(LeitorDaCena.CartaoNaUrna),
                "A pessoa põe o cartão da bilheteria na fenda da urna. O leitor da urna lê.", PecaDaCatraca.Urna),
            new(S(1.4), SinalDaCena.Liberado(),
                "Liberado. A urna ainda não recolhe o cartão: isso depende de confirmação da Topdata.",
                PecaDaCatraca.Urna),
            new(S(2.8), SinalDaCena.Giro(),
                "A pessoa passa. O giro confirma a entrada.", PecaDaCatraca.Rotor),
            new(S(4.2), null,
                "Livre de novo. O mesmo cartão só volta a valer depois do intervalo de reuso (4 min).",
                PecaDaCatraca.Display),
        ],
        CodigoDeTeste: "0000000101",
        NaUrna: true);

    public static RoteiroDeDemonstracao CartaoNaFrente { get; } = new(
        "Cartão da bilheteria na frente",
        "Cartão \"só na urna\" é recusado no leitor da frente, de propósito.",
        [
            new(S(0), SinalDaCena.Credencial(LeitorDaCena.CartaoNaFrente),
                "A pessoa encosta o cartão da bilheteria no leitor da frente.", PecaDaCatraca.LeitorDeProximidade),
            new(S(1.4), SinalDaCena.Negado(),
                "Negado: para este provedor o cartão só vale na fenda da urna. No painel aparece \"use a fenda da urna\".",
                PecaDaCatraca.Display),
            new(S(4.8), null,
                "A pessoa pode tentar de novo, agora na urna.", PecaDaCatraca.Urna),
        ],
        CodigoDeTeste: "0000000101");

    public static RoteiroDeDemonstracao LiberacaoManual { get; } = new(
        "Liberação manual pelo painel",
        "Um giro sem ingresso, pedido pelo operador com motivo.",
        [
            new(S(0), SinalDaCena.Liberado(),
                "Em Gerenciar catraca, o operador pede um giro de entrada, com o nome dele e o motivo.",
                PecaDaCatraca.Rotor),
            new(S(1.6), SinalDaCena.Giro(),
                "A pessoa passa. Fica registrado como liberação manual, fora da contagem de ingressos.",
                PecaDaCatraca.Rotor),
            new(S(3.0), null,
                "A catraca trava de novo.", PecaDaCatraca.Rotor),
        ]);

    public static RoteiroDeDemonstracao QuedaDeComunicacao { get; } = new(
        "Queda de comunicação",
        "O que muda quando o PC perde contato com a catraca.",
        [
            new(S(0), SinalDaCena.PerdeuComunicacao(),
                "O programa da catraca perde contato com ela. No painel, o cartão da catraca fica \"Sem notícia\".",
                PecaDaCatraca.Display),
            new(S(2.6), null,
                "Nesse intervalo, a catraca pode operar pela lista local, se tiver. O que ela decidir sozinha chega depois.",
                PecaDaCatraca.Tampa),
            new(S(5.0), SinalDaCena.Conectou(),
                "A comunicação volta. A catraca recebe a configuração completa de novo e volta a atender.",
                PecaDaCatraca.Display),
            new(S(6.4), null,
                "Pronta para a próxima leitura.", PecaDaCatraca.Display),
        ]);

    public static RoteiroDeDemonstracao UrnaCheia { get; } = new(
        "Urna cheia (a confirmar)",
        "O aviso de urna cheia chega da catraca; o tratamento ainda não foi ensaiado.",
        [
            new(S(0), SinalDaCena.UrnaCheia(),
                "A catraca avisa urna cheia (origem 20). O sistema guarda o evento.", PecaDaCatraca.Urna),
            new(S(2.6), null,
                "Como a urna ainda não recolhe cartão, o efeito na operação precisa ser confirmado na bancada.",
                PecaDaCatraca.Urna),
            new(S(5.0), SinalDaCena.UrnaEsvaziada(),
                "Depois de esvaziada, o aviso sai.", PecaDaCatraca.Urna),
        ]);

    /// <summary>Todos, na ordem da lista da tela.</summary>
    public static IReadOnlyList<RoteiroDeDemonstracao> Todos { get; } =
    [
        QrValido,
        CodigoDesconhecido,
        LiberadoSemGiro,
        CartaoNaUrna,
        CartaoNaFrente,
        LiberacaoManual,
        QuedaDeComunicacao,
        UrnaCheia,
    ];
}

/// <summary>Um cenário em andamento. Pura: o instante vem de fora.</summary>
public sealed class ExecucaoDeRoteiro
{
    private int _proximo;

    public ExecucaoDeRoteiro(RoteiroDeDemonstracao roteiro, TimeSpan inicio)
    {
        ArgumentNullException.ThrowIfNull(roteiro);
        Roteiro = roteiro;
        Inicio = inicio;
    }

    public RoteiroDeDemonstracao Roteiro { get; }

    public TimeSpan Inicio { get; }

    /// <summary>Índice do último passo já acontecido; -1 antes do primeiro.</summary>
    public int PassoAtual => _proximo - 1;

    public bool Terminou => _proximo >= Roteiro.Passos.Count;

    /// <summary>Os passos que venceram desde a última chamada, em ordem.</summary>
    public IReadOnlyList<PassoDoRoteiro> Avancar(TimeSpan agora)
    {
        var vencidos = new List<PassoDoRoteiro>();

        while (_proximo < Roteiro.Passos.Count && agora - Inicio >= Roteiro.Passos[_proximo].Em)
        {
            vencidos.Add(Roteiro.Passos[_proximo]);
            _proximo++;
        }

        return vencidos;
    }
}
