using System.Globalization;
using Access.Application.Devices;
using Access.Domain.Access;
using Access.Domain.Devices;
using Edge.Worker.Resiliencia;

namespace Edge.Worker;

/// <summary>Um equipamento sob responsabilidade deste worker.</summary>
public sealed class DeviceSlot
{
    public DeviceSlot(int inner, DeviceConfiguration configuracao, Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        Inner = inner;
        Configuracao = configuracao;
        Maquina = new DeviceStateMachine($"inner-{inner}", DeviceState.Discovering);
        Disjuntor = new CircuitBreaker(relogio: relogio);
    }

    public int Inner { get; }

    /// <summary>
    /// Configuração enviada a cada conexão. Só muda por <see cref="TipoDeComando.AplicarConfiguracao"/>,
    /// e só passa a valer na reconexão que o comando provoca (ADR-0020: sempre completa).
    /// </summary>
    public DeviceConfiguration Configuracao { get; internal set; }

    /// <summary>Configuração relida, esperando o comando que a aplica.</summary>
    internal DeviceConfiguration? ConfiguracaoNova { get; set; }

    /// <summary>
    /// Quando a catraca aceitou pela última vez a configuração (retorno 0 de
    /// <c>EnviarConfiguracoes</c>, EI-030). Nulo até o primeiro envio aceito deste worker.
    /// </summary>
    /// <remarks>
    /// Etapa A.5 do docs/35: "salva × aplicada". Não muda quando <see cref="Configuracao"/> é
    /// trocada pelo "Aplicar agora" — essa troca acontece antes da reconexão —, só quando o
    /// envio dá certo. Muda a cada envio aceito, inclusive na reconexão com a mesma
    /// configuração: é a última vez que a catraca a recebeu.
    /// </remarks>
    public DateTimeOffset? ConfiguracaoAplicadaEm { get; internal set; }

    /// <summary>
    /// A versão (<see cref="VersaoDaConfiguracao"/>) da configuração que a catraca aceitou por
    /// último. Nula até o primeiro envio aceito deste worker.
    /// </summary>
    public string? ConfiguracaoVersao { get; internal set; }

    /// <summary>
    /// O texto do giro já foi para o display nesta liberação (chave técnica
    /// <c>catraca.exibir_texto_do_giro</c>): o próximo passo é a liberação.
    /// </summary>
    internal bool TextoDoGiroExibido { get; set; }

    /// <summary>Comandos do operador esperando a catraca ficar livre (Polling).</summary>
    internal Queue<ComandoDeCatraca> Comandos { get; } = new();

    /// <summary>O comando sendo executado agora, quando leva mais de um passo.</summary>
    internal ComandoEmCurso? EmCurso { get; set; }

    /// <summary>Quantos comandos esperam a vez.</summary>
    public int ComandosNaFila => Comandos.Count + (EmCurso is null ? 0 : 1);

    /// <summary>
    /// O bilhete que já saiu da catraca e ainda não está durável na base (R-68; Etapa A.9).
    /// Enquanto houver um, o laço não chama <c>ColetarBilhete</c> de novo: o próximo passo desta
    /// catraca é tentar gravá-lo.
    /// </summary>
    public BilheteColetado? BilheteAGravar { get; internal set; }

    /// <summary>A coleta em andamento: identificação e contagens.</summary>
    internal ColetaDeBilhetes? Coleta { get; set; }

    /// <summary>Tentativas de gravar um bilhete coletado que a base recusou (base ocupada, por exemplo).</summary>
    public long FalhasAoGravarBilhete { get; internal set; }

    /// <summary>Entrega um comando para ser executado quando a catraca estiver livre.</summary>
    /// <param name="comando">O pedido.</param>
    /// <param name="configuracaoNova">
    /// Obrigatória em <see cref="TipoDeComando.AplicarConfiguracao"/>: a configuração
    /// completa, já validada, que a reconexão vai enviar.
    /// </param>
    public void Enfileirar(ComandoDeCatraca comando, DeviceConfiguration? configuracaoNova = null)
    {
        ArgumentNullException.ThrowIfNull(comando);

        if (comando.Inner != Inner)
        {
            throw new ArgumentException($"Comando da catraca {comando.Inner} entregue à catraca {Inner}.", nameof(comando));
        }

        if (comando.Tipo is TipoDeComando.AplicarConfiguracao)
        {
            if (configuracaoNova is null)
            {
                throw new ArgumentNullException(nameof(configuracaoNova), "Aplicar configuração exige a configuração nova.");
            }

            var problemas = configuracaoNova.Validar();
            if (problemas.Count > 0)
            {
                throw new ArgumentException("Configuração nova inválida: " + string.Join(" ", problemas), nameof(configuracaoNova));
            }

            ConfiguracaoNova = configuracaoNova;
        }

        Comandos.Enqueue(comando);
    }

    public DeviceStateMachine Maquina { get; }

    public CircuitBreaker Disjuntor { get; }

    /// <summary>Tentativas seguidas de reconexão, para o cálculo do backoff.</summary>
    public int TentativasDeReconexao { get; internal set; }

    /// <summary>Quando este equipamento pode ser tentado de novo.</summary>
    public DateTimeOffset? EsperarAte { get; internal set; }

    /// <summary>
    /// Quantas vezes a espera por evento voltou com erro (retorno nativo ≠ 0 que não é
    /// falha de dependência).
    /// </summary>
    /// <remarks>
    /// Antes esses retornos viravam "sem eventos" e a queda só aparecia quando o watchdog
    /// pegava (defeito F6, docs/34 §2; ADR-0018: contar, nunca calar).
    /// </remarks>
    public long ErrosDeRecepcao { get; internal set; }

    /// <summary>
    /// Último sinal de que a catraca responde: conexão, evento recebido ou sonda de vida respondida.
    /// </summary>
    /// <remarks>
    /// Em espera de leitura, a DLL pode devolver "sem eventos" para sempre com o cabo puxado
    /// (achado E1-01 do docs/41). A sonda de vida (<see cref="DevicePump"/>) usa este instante para
    /// saber quando testar a conexão.
    /// </remarks>
    public DateTimeOffset? UltimaProvaDeVida { get; internal set; }

    /// <summary>Quando saiu o último PingOnLine (keep-alive, manual de desenvolvimento, passo 11). Nulo até o primeiro.</summary>
    public DateTimeOffset? UltimoPingOnLine { get; internal set; }

    /// <summary>PingOnLine sem resposta seguidos. Ao chegar em <see cref="DevicePump.LimiteDeFalhasDePingOnLine"/>, reconecta.</summary>
    public int FalhasDePingOnLine { get; internal set; }

    /// <summary>Identidade lida do equipamento, quando já conhecida.</summary>
    public FirmwareInfo? Firmware { get; internal set; }

    /// <summary>
    /// Último evento recebido deste equipamento.
    /// </summary>
    /// <remarks>
    /// Guarda apenas o último, de propósito. Uma fila aqui cresceria sem limite num
    /// worker que roda por dias, e não teria serventia: a decisão é sempre sobre a
    /// leitura mais recente. Quem precisa de todos os eventos é a persistência, que os
    /// recebe pelo sink do <c>DevicePump</c> no instante em que chegam.
    /// </remarks>
    public DeviceEvent? UltimoEvento { get; internal set; }

    /// <summary>Última decisão tomada para este equipamento.</summary>
    public Decision? UltimaDecisao { get; internal set; }

    /// <summary>
    /// A leitura que está sendo atendida. Na urna, o último evento passa a ser a origem 7 (cartão
    /// recolhido), e é esta leitura que diz de qual leitor veio, para o mapa de giro.
    /// </summary>
    internal DeviceEvent? LeituraEmCurso { get; set; }

    /// <summary>A guia da urna foi aberta e ainda não foi fechada: fecha antes de liberar ou negar.</summary>
    internal bool UrnaPorFechar { get; set; }

    /// <summary>
    /// Este ciclo passou pela urna: fecha a guia de novo antes de rearmar o leitor, como o middleware de
    /// produção fazia depois do giro (docs/61).
    /// </summary>
    internal bool CicloDeUrna { get; set; }

    /// <summary>"Deposite o cartao" já foi ao display nesta urna: o próximo passo abre a urna.</summary>
    internal bool PedidoDeDepositoExibido { get; set; }

    /// <summary>O texto da próxima negação no display; nulo = "Acesso nao autorizado".</summary>
    internal string? MensagemDeNegacao { get; set; }

    /// <summary>Urnas abertas que terminaram sem o cartão cair (origem 5, prazo ou erro).</summary>
    public long UrnasSemRecolhimento { get; internal set; }

    /// <summary>A catraca avisou urna cheia (origem 20). Fica até a próxima urna que recolher.</summary>
    public bool UrnaCheia { get; internal set; }

    /// <summary>Quando acertar o relógio da catraca. Marcado a cada conexão.</summary>
    public DateTimeOffset? AcertarRelogioEm { get; internal set; }

    /// <summary>Quando conferir o relógio da catraca de novo.</summary>
    public DateTimeOffset? ConferirRelogioEm { get; internal set; }

    /// <summary>Último acerto do relógio que a catraca aceitou.</summary>
    public DateTimeOffset? RelogioAcertadoEm { get; internal set; }

    /// <summary>Última conferência do relógio.</summary>
    public DateTimeOffset? RelogioConferidoEm { get; internal set; }

    /// <summary>
    /// Relógio da catraca menos o da borda, na última conferência, em segundos inteiros
    /// (a catraca não guarda fração). Positivo: a catraca está adiantada.
    /// </summary>
    public TimeSpan? DivergenciaDoRelogio { get; internal set; }

    /// <summary>A catraca devolveu uma data impossível (relógio zerado, por exemplo).</summary>
    public bool RelogioInvalido { get; internal set; }

    /// <summary>Verdadeiro quando a última conferência passou do limite de divergência.</summary>
    public bool RelogioDivergente =>
        RelogioInvalido || DivergenciaDoRelogio is { } divergencia && divergencia.Duration() > DevicePump.LimiteDeDivergenciaDoRelogio;
}

/// <summary>Uma coleta de bilhetes: quem a pediu (o comando, quando houver) e o que já foi feito.</summary>
internal sealed class ColetaDeBilhetes(Guid id)
{
    public Guid Id { get; } = id;

    /// <summary>Devolvidos pela catraca (cada um já saiu da memória dela).</summary>
    public int Coletados { get; set; }

    /// <summary>Gravados agora na base.</summary>
    public int Gravados { get; set; }

    /// <summary>Que a base já tinha (mesmo bilhete de novo, ou 128 com o original gravado).</summary>
    public int Repetidos { get; set; }

    public string Resumo() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Coletados} bilhete(s) coletado(s): {Gravados} gravado(s), {Repetidos} já estava(m) na base");
}

/// <summary>Um comando que leva mais de um passo: liberação manual, reconexão, coleta de bilhetes.</summary>
internal sealed class ComandoEmCurso(ComandoDeCatraca comando, DateTimeOffset iniciadoEm)
{
    public ComandoDeCatraca Comando { get; } = comando;

    public DateTimeOffset IniciadoEm { get; } = iniciadoEm;

    /// <summary>A catraca aceitou a liberação.</summary>
    public bool Liberou { get; set; }

    /// <summary>Veio o giro (origem 6) depois da liberação.</summary>
    public bool Girou { get; set; }

    /// <summary>
    /// O laço desistiu de esperar o giro pelo prazo (C1, docs/36), sem origem 5 nem 6. Guarda
    /// o prazo aplicado, para o desfecho dizer qual foi.
    /// </summary>
    public TimeSpan? GiroNaoConfirmadoNoPrazo { get; set; }

    /// <summary>A reconexão pedida já saiu de Polling (para não concluir antes de começar).</summary>
    public bool SaiuDeOperacao { get; set; }
}

/// <summary>
/// Executa um passo da máquina de estados de um equipamento.
/// </summary>
/// <remarks>
/// Um passo faz <b>no máximo uma</b> chamada bloqueante ao adapter. É o que permite ao
/// laço alternar entre equipamentos numa única thread, como o manual recomenda
/// (seção 2.1.1).
/// </remarks>
public sealed class DevicePump
{
    /// <summary>Intervalo do PingOnLine no polling (manual de desenvolvimento, passo 11).</summary>
    public static readonly TimeSpan IntervaloPadraoDoPingOnLine = TimeSpan.FromSeconds(3);

    /// <summary>PingOnLine sem resposta seguidos antes de reconectar (manual de desenvolvimento, passo 12).</summary>
    public const int LimiteDeFalhasDePingOnLine = 6;

    /// <summary>Acima disto o relógio da catraca é dado como divergente (docs/24 §1).</summary>
    public static readonly TimeSpan LimiteDeDivergenciaDoRelogio = TimeSpan.FromSeconds(30);

    /// <summary>De quanto em quanto tempo o relógio é conferido.</summary>
    public static readonly TimeSpan IntervaloDeConferenciaDoRelogio = TimeSpan.FromHours(1);

    /// <summary>
    /// A primeira conferência vem logo depois do acerto: é ela que mostra se a catraca
    /// guardou mesmo a hora enviada.
    /// </summary>
    public static readonly TimeSpan PrimeiraConferenciaDoRelogio = TimeSpan.FromMinutes(1);

    /// <summary>Depois de uma falha no acerto ou na leitura, espera isto para tentar de novo.</summary>
    public static readonly TimeSpan EsperaAposFalhaNoRelogio = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Quanto uma reconexão pedida pelo operador pode levar para a catraca voltar a
    /// atender, antes de o comando ser dado como falho.
    /// </summary>
    public static readonly TimeSpan LimiteDaReconexaoPedida = TimeSpan.FromMinutes(2);

    /// <summary>Quanto esperar pelo giro, ou pelo fim do tempo, de uma liberação manual.</summary>
    public static readonly TimeSpan LimiteDaLiberacaoManual = TimeSpan.FromSeconds(60);

    private readonly ITopdataInnerAdapter _adapter;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly IReadOnlySet<byte> _linhasHomologadas;

    /// <summary>
    /// Linhas de firmware aceitas sem a chave técnica <c>catraca.linhas_homologadas</c> (ADR-0010).
    /// 14 e 16 vêm da matriz do manual; a <b>19</b> foi lida numa Catraca Fit real (firmware 6.42.1,
    /// sem biometria) na bancada de 15/10/2026 — ensaio HIL-CAP-01, achado E1-06 do docs/41.
    /// </summary>
    public static readonly IReadOnlySet<byte> LinhasHomologadasPadrao = new HashSet<byte> { 14, 16, 19 };
    private readonly Func<DeviceEvent, Decision>? _decidir;
    private readonly Action<DeviceEvent>? _aoReceberEvento;
    private readonly bool _acertarRelogioAoDivergir;
    private readonly Action<ComandoDeCatraca, SituacaoDoComando, string>? _aoConcluirComando;
    private readonly Action<string>? _antesDaLiberacaoManual;
    private readonly bool _sequenciaOficial;
    private readonly IGravadorDeBilhetes? _gravadorDeBilhetes;
    private readonly bool _exibirTextoDoGiro;
    private readonly Action<string>? _aoDesistirDoGiro;
    private readonly Action<string, string>? _aoFalharALiberacao;
    private readonly Action<string, string>? _aoDesistirDaUrna;
    private readonly TimeSpan _intervaloDoPingOnLine;

    /// <param name="adapter">Acesso à EasyInner.</param>
    /// <param name="relogio">Relógio da borda.</param>
    /// <param name="linhasHomologadas">Linhas de firmware da matriz (ADR-0010).</param>
    /// <param name="decidir">Motor de decisão.</param>
    /// <param name="aoReceberEvento">Recebe cada evento no instante em que chega.</param>
    /// <param name="acertarRelogioAoDivergir">
    /// Acerta sozinho, com a catraca em operação, quando a conferência horária achar
    /// divergência. Desligado por padrão: o fluxo oficial só acerta na conexão, e acertar
    /// com a catraca atendendo é <c>A_CONFIRMAR_COM_TOPDATA</c> (docs/21, INT-CLK-02).
    /// Desligado, a divergência só é informada — e o operador acerta pelo painel.
    /// </param>
    /// <param name="aoConcluirComando">Recebe o desfecho de cada comando do operador.</param>
    /// <param name="antesDaLiberacaoManual">
    /// Chamado com o id do equipamento logo antes de uma liberação manual. O decisor usa
    /// para encerrar a tentativa pendente: o giro que vier é do operador, não do último
    /// ingresso lido.
    /// </param>
    /// <param name="sequenciaOficial">
    /// Segue a sequência oficial de conexão (docs/34 §4.3): os estados <c>EnviarCfgOffline</c>,
    /// <c>EnviarConfigMudOnlineOffline</c> e <c>EnviarCfgOnline</c> mandam, cada um, a sua etapa
    /// (<see cref="ITopdataInnerAdapter.EnviarEtapaDaSequenciaOficial"/>). Desligado por padrão: os
    /// três mandam a mesma configuração completa, como sempre (defeito F3, docs/34 §2). Chave
    /// técnica <c>catraca.sequencia_oficial</c>, <c>A_CONFIRMAR_COM_TOPDATA</c> até o ensaio
    /// INT-SM-021 (Etapa A.7 do docs/35). Não liga a contingência: a mudança automática segue com
    /// o valor da configuração, 0 no padrão (D8).
    /// </param>
    /// <param name="gravadorDeBilhetes">
    /// Quem torna durável cada bilhete coletado (Etapa A.9 do docs/35). A gravação é um passo
    /// próprio, sem chamada nativa, entre um <c>ColetarBilhete</c> e o próximo (R-68). Sem ele,
    /// este laço <b>não coleta</b>: o comando falha e o estado de coleta sai sem chamar a catraca —
    /// coletar sem gravar apagaria marcações da memória dela.
    /// </param>
    /// <param name="exibirTextoDoGiro">
    /// Mostra no display o texto do giro do mapa ("Entrada liberada", "Saida liberada" ou o
    /// personalizado; D9, docs/34 §9) antes de liberar, num passo próprio com uma chamada só
    /// (<see cref="ITopdataInnerAdapter.ExibirMensagemTemporaria"/>). Desligado por padrão: cada
    /// chamada a mais no caminho da passagem reduz a vazão (docs/34 §8), e se a mensagem no meio
    /// da liberação atrapalha o giro é <c>A_CONFIRMAR_COM_TOPDATA</c> (NOVO-HIL-DIR-12, docs/21).
    /// Chave técnica <c>catraca.exibir_texto_do_giro</c>.
    /// </param>
    /// <param name="aoDesistirDoGiro">
    /// Chamado com o id do equipamento quando o laço desiste de esperar o giro pelo prazo de
    /// <see cref="DeviceState.MonitoraGiroCatraca"/>, sem origem 5 nem 6 (Etapa I.1b do docs/36,
    /// C1). O decisor encerra a tentativa pendente sem giro, como faria com a origem 5: um giro
    /// que chegue depois não é da pessoa liberada, e confirmá-la com ele daria a passagem a outro.
    /// </param>
    /// <param name="intervaloDoPingOnLine">
    /// Intervalo do PingOnLine enquanto a catraca espera leitura (keep-alive). O manual de desenvolvimento
    /// manda a cada 3 s (passo 11); com a DLL em silêncio, o ping é a prova de que a conexão ainda vive
    /// (achado E1-01 do docs/41). Padrão: <see cref="IntervaloPadraoDoPingOnLine"/>.
    /// </param>
    /// <param name="aoFalharALiberacao">
    /// Chamado com o id do equipamento e a causa, logo antes de <paramref name="aoDesistirDoGiro"/>,
    /// quando a liberação de um ingresso não saiu ou a comunicação caiu esperando o giro (achado E1-05
    /// do docs/41). O decisor grava a causa na tentativa, que fica consumida e aparece no painel para o
    /// operador estornar.
    /// </param>
    /// <param name="aoDesistirDaUrna">
    /// Chamado com o id do equipamento e a causa quando a urna foi aberta e o cartão não caiu (origem 5,
    /// urna cheia, prazo ou erro): a catraca não liberou o braço, ninguém passou, e o cartão continua com
    /// a pessoa. O decisor devolve o uso (manual do SDK, fluxo 8: "NaoRegistrarAcesso").
    /// </param>
    public DevicePump(
        ITopdataInnerAdapter adapter,
        Func<DateTimeOffset>? relogio = null,
        IReadOnlySet<byte>? linhasHomologadas = null,
        Func<DeviceEvent, Decision>? decidir = null,
        Action<DeviceEvent>? aoReceberEvento = null,
        bool acertarRelogioAoDivergir = false,
        Action<ComandoDeCatraca, SituacaoDoComando, string>? aoConcluirComando = null,
        Action<string>? antesDaLiberacaoManual = null,
        bool sequenciaOficial = false,
        IGravadorDeBilhetes? gravadorDeBilhetes = null,
        bool exibirTextoDoGiro = false,
        Action<string>? aoDesistirDoGiro = null,
        TimeSpan? intervaloDoPingOnLine = null,
        Action<string, string>? aoFalharALiberacao = null,
        Action<string, string>? aoDesistirDaUrna = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _aoDesistirDaUrna = aoDesistirDaUrna;
        _aoDesistirDoGiro = aoDesistirDoGiro;
        _aoFalharALiberacao = aoFalharALiberacao;
        _intervaloDoPingOnLine = intervaloDoPingOnLine ?? IntervaloPadraoDoPingOnLine;
        _sequenciaOficial = sequenciaOficial;
        _exibirTextoDoGiro = exibirTextoDoGiro;
        _gravadorDeBilhetes = gravadorDeBilhetes;
        _acertarRelogioAoDivergir = acertarRelogioAoDivergir;
        _aoConcluirComando = aoConcluirComando;
        _antesDaLiberacaoManual = antesDaLiberacaoManual;
        _adapter = adapter;
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);

        // O motor de decisão é de fora: o laço não decide acesso, só transporta.
        _decidir = decidir;

        // Quem persiste recebe cada evento no instante em que chega, e não por uma fila
        // acumulada no slot.
        _aoReceberEvento = aoReceberEvento;

        // Nada é configurado sem que o firmware conste da matriz (ADR-0010).
        _linhasHomologadas = linhasHomologadas ?? LinhasHomologadasPadrao;
    }

    /// <summary>Executa um passo. Devolve o que foi feito, para log e métrica.</summary>
    public string Passo(DeviceSlot dispositivo, TimeSpan limiteDeEspera)
    {
        ArgumentNullException.ThrowIfNull(dispositivo);

        ExpirarNaFila(dispositivo, _relogio());
        var feito = PassoDaMaquina(dispositivo, limiteDeEspera);
        return AcompanharComando(dispositivo, _relogio()) is { } desfecho ? $"{feito} · {desfecho}" : feito;
    }

    // Catraca fora do ar ou presa num estado: o comando não espera para sempre na fila.
    private void ExpirarNaFila(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.Comandos.Count == 0 || d.Comandos.All(c => agora < c.ExpiraEm))
        {
            return;
        }

        var validos = new List<ComandoDeCatraca>(d.Comandos.Count);
        while (d.Comandos.TryDequeue(out var comando))
        {
            if (agora < comando.ExpiraEm)
            {
                validos.Add(comando);
            }
            else
            {
                Concluir(comando, SituacaoDoComando.Expirado, $"a catraca não ficou livre a tempo (estado {d.Maquina.Current}); não executado");
            }
        }

        foreach (var comando in validos)
        {
            d.Comandos.Enqueue(comando);
        }
    }

    private string PassoDaMaquina(DeviceSlot dispositivo, TimeSpan limiteDeEspera)
    {
        var agora = _relogio();

        // Bilhete coletado e ainda não gravado: este passo é a gravação, e só ela (R-68). Vem
        // antes do backoff e do disjuntor porque não fala com a catraca — e antes de tudo o
        // mais porque a catraca já apagou o bilhete: aqui está a única cópia.
        if (dispositivo.BilheteAGravar is { } pendente)
        {
            return GravarBilhete(dispositivo, pendente);
        }

        if (dispositivo.EsperarAte is { } ate && agora < ate)
        {
            return PrazoComOPassoImpedido(dispositivo, agora, "aguardando backoff") ?? "aguardando backoff";
        }

        if (!dispositivo.Disjuntor.PermitePassar)
        {
            return PrazoComOPassoImpedido(dispositivo, agora, "disjuntor aberto") ?? "disjuntor aberto";
        }

        // Comandos do operador e relógio só em Polling: é o ponto ocioso e seguro, sem
        // ninguém no meio de uma passagem. Uma chamada por passo, como o resto.
        if (dispositivo.Maquina.Current is DeviceState.Polling)
        {
            if (dispositivo.EmCurso is null && dispositivo.Comandos.TryDequeue(out var comando))
            {
                return Executar(dispositivo, comando, agora);
            }

            if (CuidarDoRelogio(dispositivo, agora) is { } relogio)
            {
                return relogio;
            }
        }

        // A guia da urna se fecha num passo próprio (uma chamada), antes de liberar, negar ou rearmar.
        if (FecharUrnaSePreciso(dispositivo, agora) is { } fechou)
        {
            return fechou;
        }

        // A coleta é de vários passos, um bilhete por vez: o prazo do estado (10 min) corta
        // antes de pedir o próximo, nunca entre a coleta e a gravação (R-68, tratada acima).
        if (dispositivo.Maquina.Current is DeviceState.ColetarBilhetes
            && PrazoEstourado(dispositivo, agora) is { } prazoDaColeta)
        {
            return EsgotarPrazo(dispositivo, agora, prazoDaColeta);
        }

        return dispositivo.Maquina.Current switch
        {
            DeviceState.Discovering or DeviceState.Reconectar or DeviceState.Degradado => Conectar(dispositivo, agora),
            DeviceState.Conectar => Conectar(dispositivo, agora),
            DeviceState.LendoIdentidade => LerIdentidade(dispositivo, agora),
            DeviceState.VerificandoCompatibilidade => VerificarCompatibilidade(dispositivo, agora),
            DeviceState.EnviarCfgOffline =>
                EnviarConfiguracao(dispositivo, agora, "cfg offline", EtapaDaSequenciaOficial.ConfiguracaoOffLine),
            DeviceState.EnviarConfigMudOnlineOffline =>
                EnviarConfiguracao(dispositivo, agora, "cfg mudança automática", EtapaDaSequenciaOficial.MudancaAutomatica),
            DeviceState.EnviarCfgOnline =>
                EnviarConfiguracao(dispositivo, agora, "cfg online", EtapaDaSequenciaOficial.ConfiguracaoOnLine),
            DeviceState.SincronizandoDadosOffline => SincronizarDadosOffline(dispositivo, agora),
            DeviceState.ConfigurarEntradasOnline => ConfigurarEntradas(dispositivo, agora),
            DeviceState.EnviarMsgPadrao => EnviarMensagemPadrao(dispositivo, agora),
            DeviceState.Polling or DeviceState.MonitoraGiroCatraca or DeviceState.MonitoraUrna =>
                Aguardar(dispositivo, agora, limiteDeEspera),
            DeviceState.AcionarUrna => AbrirUrna(dispositivo, agora),
            DeviceState.ValidarAcesso => Decidir(dispositivo, agora),
            DeviceState.LiberarCatraca => Liberar(dispositivo, agora),
            DeviceState.EnviarMsgAcessoNegado => ExibirNegado(dispositivo, agora),
            DeviceState.ColetarBilhetes => ColetarBilhete(dispositivo, agora),
            _ => $"estado sem ação de laço: {dispositivo.Maquina.Current}",
        };
    }

    private string Conectar(DeviceSlot d, DateTimeOffset agora)
    {
        // De Discovering, Reconectar ou Degradado, o caminho para tentar de novo passa por
        // Conectar. Antes, só Discovering disparava este gatilho: a catraca que caía ficava
        // em Reconectar para sempre, mesmo com a conexão de volta (teste
        // Catraca_que_cai_volta_a_operar_quando_a_conexao_volta).
        if (d.Maquina.Current is DeviceState.Discovering or DeviceState.Reconectar or DeviceState.Degradado)
        {
            Disparar(d, DeviceTrigger.EquipamentoApareceu, agora);
        }

        var resultado = _adapter.TestarConexao(d.Inner);

        if (resultado.IsOk)
        {
            d.Disjuntor.RegistrarSucesso();
            d.TentativasDeReconexao = 0;
            d.EsperarAte = null;
            d.UltimaProvaDeVida = agora;
            d.UltimoPingOnLine = agora;
            d.FalhasDePingOnLine = 0;
            d.UrnaPorFechar = false;
            d.CicloDeUrna = false;

            // A cada conexão, o relógio é acertado assim que a catraca chegar a Polling —
            // o fluxo oficial acerta na passagem para on-line (manual 4.6.1).
            d.AcertarRelogioEm = agora;
            d.ConferirRelogioEm = null;
            Disparar(d, DeviceTrigger.ConexaoOk, agora);
            return "conectado";
        }

        Falhar(d, agora, DeviceTrigger.ConexaoFalhou);
        return $"falha ao conectar ({resultado})";
    }

    private string LerIdentidade(DeviceSlot d, DateTimeOffset agora)
    {
        var (resultado, firmware) = _adapter.LerFirmware(d.Inner);

        if (!resultado.IsOk || firmware is null)
        {
            Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
            return $"falha ao ler firmware ({resultado})";
        }

        d.Firmware = firmware;
        Disparar(d, DeviceTrigger.IdentidadeLida, agora);
        return $"firmware lido: {firmware}";
    }

    private string VerificarCompatibilidade(DeviceSlot d, DateTimeOffset agora)
    {
        // Sem chamada nativa: é conferência contra a matriz.
        if (d.Firmware is { } firmware && _linhasHomologadas.Contains(firmware.Linha))
        {
            Disparar(d, DeviceTrigger.CompatibilidadeOk, agora);
            return "compatível";
        }

        Disparar(d, DeviceTrigger.CompatibilidadeRecusada, agora);
        return $"firmware não homologado (linha {d.Firmware?.Linha.ToString(provider: null) ?? "?"}) — não será configurado";
    }

    private string EnviarConfiguracao(DeviceSlot d, DateTimeOffset agora, string etapa, EtapaDaSequenciaOficial daSequenciaOficial)
    {
        // Sempre a configuração COMPLETA: o que não for setado volta ao padrão da DLL.
        // Ver ADR-0020. Com a sequência oficial, cada estado manda a sua etapa — e as duas
        // configurações levam os mesmos campos comuns (docs/34 §4.3). Uma chamada ao adapter
        // por passo, nos dois casos: a máquina de estados não muda.
        var resultado = _sequenciaOficial
            ? _adapter.EnviarEtapaDaSequenciaOficial(d.Inner, d.Configuracao, daSequenciaOficial)
            : _adapter.EnviarConfiguracaoCompleta(d.Inner, d.Configuracao);

        if (resultado.IsOk)
        {
            // Só aqui a configuração passa a ser "aplicada" (Etapa A.5): EnviarConfiguracoes
            // devolveu 0. Falha em qualquer passo da montagem ou no envio não toca a versão.
            // Calcular é CPU pura, sem chamada nativa: o passo continua com uma só.
            d.ConfiguracaoVersao = VersaoDaConfiguracao.Calcular(d.Configuracao);
            d.ConfiguracaoAplicadaEm = agora;
            Disparar(d, DeviceTrigger.ConfiguracaoEnviada, agora);
            return etapa + " enviada";
        }

        Falhar(d, agora, DeviceTrigger.ConfiguracaoFalhou);
        return $"falha em {etapa} ({resultado})";
    }

    private static string SincronizarDadosOffline(DeviceSlot d, DateTimeOffset agora)
    {
        // Fase 1: ainda não há lista a enviar. A transição existe para que a ordem
        // — dados de contingência ANTES de operar — já esteja correta.
        Disparar(d, DeviceTrigger.DadosOfflineSincronizados, agora);
        return "dados offline sincronizados (vazio nesta fase)";
    }

    private string ConfigurarEntradas(DeviceSlot d, DateTimeOffset agora)
    {
        var resultado = _adapter.ConfigurarEntradasOnline(d.Inner, d.Configuracao);

        if (resultado.IsOk)
        {
            Disparar(d, DeviceTrigger.ConfiguracaoEnviada, agora);
            return "leitor reabilitado";
        }

        Falhar(d, agora, DeviceTrigger.ConfiguracaoFalhou);
        return $"falha ao reabilitar o leitor ({resultado})";
    }

    private string EnviarMensagemPadrao(DeviceSlot d, DateTimeOffset agora)
    {
        var resultado = _adapter.EnviarMensagemPadrao(d.Inner, d.Configuracao.MensagemPadrao);

        if (resultado.IsOk)
        {
            // A catraca acabou de responder: é a prova de vida com que ela entra em espera de leitura.
            d.UltimaProvaDeVida = agora;
            Disparar(d, DeviceTrigger.ConfiguracaoEnviada, agora);
            return "mensagem padrão enviada";
        }

        Falhar(d, agora, DeviceTrigger.ConfiguracaoFalhou);
        return $"falha na mensagem padrão ({resultado})";
    }

    private string Aguardar(DeviceSlot d, DateTimeOffset agora, TimeSpan limite)
    {
        // Só em espera de leitura: durante o giro, nada pode atrasar a origem 5 ou 6.
        if (d.Maquina.Current is DeviceState.Polling && ManterOnline(d, agora) is { } keepAlive)
        {
            return keepAlive;
        }

        var (resultado, evento) = _adapter.AguardarEvento(d.Inner, limite);

        switch (resultado.Status)
        {
            case AdapterStatus.Ok when evento is not null:
                d.Disjuntor.RegistrarSucesso();
                d.UltimoEvento = evento;
                d.UltimaProvaDeVida = agora;

                if (evento.Origin.ConfirmaPassagemFisica && d.EmCurso is { Liberou: true } manual)
                {
                    manual.Girou = true;
                }

                if (evento.Origin.Known is KnownEventOrigin.UrnaCheia)
                {
                    d.UrnaCheia = true;
                }

                // Urna aberta (fluxo 8 do manual): a resposta é a origem 7. Sem ela, a desistência vai
                // para o decisor ANTES do evento, porque a origem 5 encerra a tentativa lá sem devolver o
                // uso — e aqui ninguém passou: a catraca nem liberou o braço.
                if (d.Maquina.Current is DeviceState.MonitoraUrna)
                {
                    return EventoNaUrna(d, evento, agora);
                }

                // Todo evento vai inteiro para quem registra, leitura ou não (ADR-0018).
                _aoReceberEvento?.Invoke(evento);

                // Só leitura vai para a decisão. Sinal da catraca (cartão recolhido, sensor,
                // urna cheia, tecla, origem desconhecida) sem código virava negação, com
                // "Acesso nao autorizado" no display e o leitor rearmado (F4, docs/34 §2).
                var gatilho = evento.Origin switch
                {
                    { ConfirmaPassagemFisica: true } => DeviceTrigger.GiroConfirmado,
                    { Known: KnownEventOrigin.FimTempoAcionamento } => DeviceTrigger.TempoDeAcionamentoEsgotado,
                    { EhLeitura: true } => DeviceTrigger.EventoRecebido,
                    _ => DeviceTrigger.SinalDaCatraca,
                };

                Disparar(d, gatilho, agora);
                return gatilho is DeviceTrigger.SinalDaCatraca
                    ? $"sinal da catraca {evento.Origin} — registrado, sem decisão"
                    : $"evento {evento.Origin}";

            case AdapterStatus.SemEventos:
            {
                d.Disjuntor.RegistrarSucesso();
                string? erroDeRecepcao = null;
                if (resultado.NativeReturn != 0)
                {
                    // Retorno ≠ 0 que o adaptador, com a reconexão desligada até HIL-EVT-01,
                    // devolveu como "sem eventos": conta sempre e deixa o bruto no registro (F6).
                    // Se a DLL devolver ≠ 0 em toda volta sem evento, uma linha por volta
                    // afogaria o registro: registra a 1ª, a 10ª, a 100ª... e o total fica no contador.
                    d.ErrosDeRecepcao++;
                    if (EhMarco(d.ErrosDeRecepcao))
                    {
                        erroDeRecepcao = $"sem eventos com retorno {resultado} — erro de recepção nº {d.ErrosDeRecepcao}, " +
                            "sem reconectar até HIL-EVT-01";
                    }
                }

                // Prazo do giro (C1, docs/36): só depois de uma espera que voltou vazia. Um giro
                // ou uma origem 5 que já esteja na fila da DLL sai antes, por mais que a volta
                // tenha demorado; o prazo nunca corta o que a catraca já mandou.
                if (d.Maquina.Current is DeviceState.MonitoraGiroCatraca or DeviceState.MonitoraUrna
                    && PrazoEstourado(d, agora) is { } prazo)
                {
                    var desistiu = EsgotarPrazo(d, agora, prazo);
                    return erroDeRecepcao is null ? desistiu : $"{desistiu} · {erroDeRecepcao}";
                }

                Disparar(d, DeviceTrigger.SemEventos, agora);
                return erroDeRecepcao ?? "sem eventos";
            }

            case AdapterStatus.FalhaDeDependencia:
                // Retorno 8 não é problema de rede: é DLL, .NET Framework ou
                // arquitetura. Insistir não adianta.
                Disparar(d, DeviceTrigger.DependenciaFatal, agora);
                return $"falha de dependência ({resultado}) — worker inutilizável";

            default:
                // Retorno ≠ 0 de ReceberDadosOnLine não é silêncio (F6, docs/34 §2): conta,
                // registra com o bruto e segue pelo caminho de falha de sempre — disjuntor,
                // backoff e reconexão —, sem derrubar o laço das outras catracas. Da DLL real só
                // chega aqui com ReconectarEmErroDeRecepcao ligado no adaptador (HIL-EVT-01).
                d.ErrosDeRecepcao++;
                DesistirDoGiroEmCurso(d);
                Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
                return $"erro ao aguardar evento ({resultado}) — erro de recepção nº {d.ErrosDeRecepcao}";
        }
    }

    /// <summary>1, 10, 100, 1000...: quando um erro repetido volta ao registro.</summary>
    private static bool EhMarco(long n)
    {
        while (n >= 10 && n % 10 == 0)
        {
            n /= 10;
        }

        return n == 1;
    }

    private string ColetarBilhete(DeviceSlot d, DateTimeOffset agora)
    {
        if (_gravadorDeBilhetes is null)
        {
            // Sem quem grave, nenhum bilhete sai da catraca: coletar e descartar perdia a
            // marcação (defeito F5, docs/34 §2). Sai do estado sem chamar a DLL.
            d.Coleta = null;
            Disparar(d, DeviceTrigger.SemBilhetes, agora);
            return "coleta não feita: este laço não tem onde gravar bilhetes (R-68)";
        }

        var (resultado, bilhete) = _adapter.ColetarBilhete(d.Inner);

        switch (resultado.Status)
        {
            case AdapterStatus.Ok when bilhete is not null:
            {
                // O bilhete já saiu da memória do equipamento (FUN:40). O próximo passo desta
                // catraca é gravá-lo; só depois vem o próximo ColetarBilhete (R-68).
                var coleta = d.Coleta ??= new ColetaDeBilhetes(Guid.CreateVersion7(agora));
                coleta.Coletados++;
                d.BilheteAGravar = new BilheteColetado(d.Inner, bilhete, coleta.Id, coleta.Coletados, agora);
                d.Disjuntor.RegistrarSucesso();
                Disparar(d, DeviceTrigger.BilheteColetado, agora);
                return string.Create(CultureInfo.InvariantCulture, $"bilhete tipo {bilhete.Tipo} coletado (nº {coleta.Coletados}); gravando antes do próximo");
            }

            case AdapterStatus.SemBilhetes:
                if (d.EmCurso is not { Comando.Tipo: TipoDeComando.ColetarBilhetes })
                {
                    d.Coleta = null;
                }

                Disparar(d, DeviceTrigger.SemBilhetes, agora);
                return "sem bilhetes";

            default:
                Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
                return $"erro ao coletar bilhete ({resultado})";
        }
    }

    // Passo sem chamada nativa: a base, e só ela. Falhou, o bilhete fica com o worker e o
    // próximo passo desta catraca tenta de novo; a catraca não é chamada enquanto isso.
    private string GravarBilhete(DeviceSlot d, BilheteColetado pendente)
    {
        if (_gravadorDeBilhetes is null)
        {
            // Inalcançável: só se coleta com gravador. Se acontecer, não some com o bilhete.
            return "bilhete coletado sem onde gravar; a catraca não será chamada";
        }

        DesfechoDaGravacaoDoBilhete desfecho;
        try
        {
            desfecho = _gravadorDeBilhetes.Gravar(pendente);
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            d.FalhasAoGravarBilhete++;
            return EhMarco(d.FalhasAoGravarBilhete)
                ? $"bilhete coletado ainda não gravado ({erro.GetType().Name}, falha nº {d.FalhasAoGravarBilhete}); a coleta espera a base"
                : "bilhete coletado ainda não gravado; a coleta espera a base";
        }

        d.BilheteAGravar = null;
        var coleta = d.Coleta;

        if (desfecho is DesfechoDaGravacaoDoBilhete.Gravado)
        {
            if (coleta is not null)
            {
                coleta.Gravados++;
            }

            return string.Create(CultureInfo.InvariantCulture, $"bilhete nº {pendente.Ordem} gravado (tipo {pendente.Bilhete.Tipo})");
        }

        if (coleta is not null)
        {
            coleta.Repetidos++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"bilhete nº {pendente.Ordem} já estava na base (tipo {pendente.Bilhete.Tipo}); não gravado de novo");
    }

    private string Decidir(DeviceSlot d, DateTimeOffset agora)
    {
        // Sem motor de decisão configurado, o laço PARA aqui em vez de escolher sozinho.
        // Liberar por omissão seria uma política de segurança tomada por engano.
        if (_decidir is null)
        {
            return "aguardando motor de decisão (nenhum configurado)";
        }

        var evento = d.UltimoEvento;
        if (evento is null)
        {
            Disparar(d, DeviceTrigger.AcessoNegado, agora);
            return "sem evento para decidir";
        }

        var decisao = _decidir(evento);
        d.UltimaDecisao = decisao;
        d.LeituraEmCurso = evento;
        d.MensagemDeNegacao = decisao.ShouldRelease ? null : MensagensDaCatraca.ParaNegacao(decisao.Reason);

        // Uma liberação nova começa sem texto exibido, mesmo que a anterior tenha sido
        // interrompida entre o texto e a liberação.
        d.TextoDoGiroExibido = false;

        // Cartão na fenda de uma catraca que recolhe (manual do SDK, fluxo 8 e seção 5.3): primeiro a
        // urna, e o giro só depois da origem 7. Antes, o giro era liberado na hora e a pessoa passava
        // levando o cartão (bancada de 15/10/2026).
        if (decisao.ShouldRelease && evento.Origin.Known is KnownEventOrigin.Leitor2 && d.Configuracao.RecolheNaUrna)
        {
            d.PedidoDeDepositoExibido = false;
            Disparar(d, DeviceTrigger.AcessoPermitidoNaUrna, agora);
            return $"decisão {decisao.Outcome} ({decisao.Reason}) — abrindo a urna";
        }

        Disparar(d, decisao.ShouldRelease ? DeviceTrigger.AcessoPermitido : DeviceTrigger.AcessoNegado, agora);
        return $"decisão {decisao.Outcome} ({decisao.Reason})";
    }

    private string Liberar(DeviceSlot d, DateTimeOffset agora)
    {
        // A função exata vem do perfil físico do portão, definido no comissionamento —
        // nunca de constante em código nem de combinação de sinalizadores (docs/04, seção
        // 3; defeito F1 do docs/34 §2). Ingresso e liberação manual passam por aqui, e
        // também as liberações de saída e nos dois sentidos pedidas pelo operador (Etapa A.8).
        // O mapa de giro (D9) escolhe a função pela origem: leitor 1, urna, teclado ou manual.
        // Vazio, é a função do perfil para todas, como sempre.
        var perfil = d.Configuracao.PerfilFisico;
        var giro = d.EmCurso?.Comando.Tipo switch
        {
            TipoDeComando.LiberarSaida or TipoDeComando.LiberarDoisSentidos => null,
            TipoDeComando.LiberacaoManual => perfil.Resolver(OrigemDoGiro.LiberacaoManual),
            _ => (d.LeituraEmCurso ?? d.UltimoEvento) is { } leitura && MapaDeGiro.DaLeitura(leitura.Origin) is { } origem
                ? perfil.Resolver(origem)
                : null,
        };

        var direcao = d.EmCurso?.Comando.Tipo switch
        {
            TipoDeComando.LiberarSaida => perfil.LiberacaoDaSaida,
            TipoDeComando.LiberarDoisSentidos => GateDirection.DoisSentidos,
            _ => giro?.Direcao ?? perfil.LiberacaoDaEntrada,
        };

        // Com a chave técnica, o texto do giro vai ao display num passo próprio, antes de
        // liberar: uma chamada por passo, e o estado continua LiberarCatraca até a liberação.
        // Falha aqui não derruba nada: a liberação vem no passo seguinte de qualquer jeito.
        if (_exibirTextoDoGiro && giro is not null && !d.TextoDoGiroExibido)
        {
            d.TextoDoGiroExibido = true;
            var exibido = _adapter.ExibirMensagemTemporaria(
                d.Inner, giro.Texto, TimeSpan.FromSeconds(Math.Max((byte)1, d.Configuracao.TempoDoAcionamento1)));
            return exibido.IsOk
                ? $"texto do giro no display ({giro.ContaComo})"
                : $"texto do giro não exibido ({exibido}); a liberação segue";
        }

        d.TextoDoGiroExibido = false;
        var resultado = _adapter.LiberarGiro(d.Inner, direcao);

        if (resultado.IsOk)
        {
            if (d.EmCurso is { } manual && ComandoDeCatraca.EhLiberacao(manual.Comando.Tipo))
            {
                manual.Liberou = true;
            }

            Disparar(d, DeviceTrigger.ComandoDeLiberacaoOk, agora);
            return giro is { DoMapa: true }
                ? $"giro liberado ({direcao}; {MapaDeGiro.Nome(giro.Origem)} conta como {giro.ContaComo})"
                : $"giro liberado ({direcao})";
        }

        DesistirDoGiroEmCurso(d);
        Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
        return $"falha ao liberar giro ({resultado})";
    }

    /// <summary>
    /// Pede o depósito no display e abre a urna (relé 2), em dois passos de uma chamada cada — como o
    /// exemplo oficial em Java do SDK 6.0.2.0 (<c>PASSO_ESTADO_ENVIA_MSG_URNA</c>: mensagem
    /// "DEPOSITE O CARTAO" e <c>AcionarRele2</c>, depois <c>ESTADO_MONITORA_URNA</c>).
    /// </summary>
    private string AbrirUrna(DeviceSlot d, DateTimeOffset agora)
    {
        if (!d.PedidoDeDepositoExibido)
        {
            // Falha aqui não impede a urna: a mensagem só ajuda quem está na frente da catraca.
            d.PedidoDeDepositoExibido = true;
            var exibido = _adapter.ExibirMensagemTemporaria(
                d.Inner, MensagensDaCatraca.DepositeOCartao, TimeSpan.FromSeconds(Math.Max((byte)1, d.Configuracao.TempoDoAcionamento2)));
            return exibido.IsOk ? "pedindo o depósito do cartão no display" : $"pedido de depósito não exibido ({exibido}); a urna abre a seguir";
        }

        d.PedidoDeDepositoExibido = false;
        var resultado = _adapter.AcionarReleDaUrna(d.Inner);

        if (resultado.IsOk)
        {
            d.UrnaPorFechar = true;
            d.CicloDeUrna = true;
            Disparar(d, DeviceTrigger.UrnaAcionada, agora);
            return "urna aberta — esperando o cartão cair (origem 7) para liberar o giro";
        }

        DesistirDaUrna(d, $"a urna não abriu ({resultado})");
        Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
        return $"falha ao abrir a urna ({resultado}) — giro não liberado";
    }

    /// <summary>Um evento com a urna aberta: origem 7 libera o giro; 5 e 20 negam e devolvem o uso.</summary>
    private string EventoNaUrna(DeviceSlot d, DeviceEvent evento, DateTimeOffset agora)
    {
        switch (evento.Origin.Known)
        {
            case KnownEventOrigin.CartaoRecolhidoUrna:
                d.UrnaCheia = false;
                _aoReceberEvento?.Invoke(evento);
                Disparar(d, DeviceTrigger.CartaoRecolhido, agora);
                return "cartão recolhido pela urna (origem 7) — liberando o giro";

            case KnownEventOrigin.FimTempoAcionamento:
                DesistirDaUrna(d, "o cartão não caiu na urna no tempo do relé 2 (origem 5)");
                d.MensagemDeNegacao = MensagensDaCatraca.CartaoNaoRecolhido;
                _aoReceberEvento?.Invoke(evento);
                Disparar(d, DeviceTrigger.TempoDeAcionamentoEsgotado, agora);
                return "o cartão não caiu na urna (origem 5) — giro não liberado, uso devolvido";

            case KnownEventOrigin.UrnaCheia:
                DesistirDaUrna(d, "urna cheia (origem 20)");
                d.MensagemDeNegacao = MensagensDaCatraca.UrnaCheia;
                _aoReceberEvento?.Invoke(evento);
                Disparar(d, DeviceTrigger.UrnaCheia, agora);
                return "urna cheia (origem 20) — giro não liberado, uso devolvido; esvazie a urna";

            default:
                // Outra leitura ou sinal com a urna aberta: fica registrado e a espera continua. A leitura
                // nova não é decidida — a vez é de quem está com o cartão na fenda.
                _aoReceberEvento?.Invoke(evento);
                Disparar(d, DeviceTrigger.SinalDaCatraca, agora);
                return $"{evento.Origin} com a urna aberta — registrado, esperando o cartão cair";
        }
    }

    /// <summary>
    /// Fecha a guia da urna (<c>DesabilitarRele2</c>) quando ela ficou aberta: antes de liberar o giro (cartão
    /// caiu), antes da negação (não caiu, urna cheia, prazo) e de novo antes de rearmar o leitor depois do
    /// giro. É o que o middleware de produção de 2025 fazia: com o relé 2 acionado, a urna fica aberta e o
    /// próximo cartão não é lido. Falha ao fechar não prende a catraca: registra e segue.
    /// </summary>
    private string? FecharUrnaSePreciso(DeviceSlot d, DateTimeOffset agora)
    {
        var estado = d.Maquina.Current;
        var antesDeLiberarOuNegar = d.UrnaPorFechar && estado is DeviceState.LiberarCatraca or DeviceState.EnviarMsgAcessoNegado;
        var antesDeRearmar = d.CicloDeUrna && estado is DeviceState.ConfigurarEntradasOnline;
        if (!antesDeLiberarOuNegar && !antesDeRearmar)
        {
            return null;
        }

        d.UrnaPorFechar = false;
        if (antesDeRearmar)
        {
            d.CicloDeUrna = false;
        }

        var resultado = _adapter.FecharReleDaUrna(d.Inner);
        _ = agora;
        return resultado.IsOk
            ? (antesDeRearmar ? "urna fechada antes de rearmar o leitor" : "urna fechada")
            : $"falha ao fechar a urna ({resultado}) — segue";
    }

    /// <summary>A urna terminou sem o cartão cair: conta e avisa o decisor para devolver o uso.</summary>
    private void DesistirDaUrna(DeviceSlot d, string causa)
    {
        d.UrnasSemRecolhimento++;
        _aoDesistirDaUrna?.Invoke(d.Maquina.DeviceId, causa);
    }

    private string ExibirNegado(DeviceSlot d, DateTimeOffset agora)
    {
        var texto = d.MensagemDeNegacao ?? MensagensDaCatraca.AcessoNaoAutorizado;
        var resultado = _adapter.ExibirMensagemTemporaria(d.Inner, texto, TimeSpan.FromSeconds(3));

        if (resultado.IsOk)
        {
            d.MensagemDeNegacao = null;
            Disparar(d, DeviceTrigger.MensagemExibida, agora);
            return "mensagem de negação exibida";
        }

        Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
        return $"falha ao exibir negação ({resultado})";
    }

    private string Executar(DeviceSlot d, ComandoDeCatraca comando, DateTimeOffset agora)
    {
        // Nome e motivo digitados ficam só na auditoria (operator_command): texto livre pode
        // conter qualquer coisa, e o registro do worker não é lugar para isso.
        var rotulo = $"comando {comando.Tipo}";

        if (agora >= comando.ExpiraEm)
        {
            Concluir(comando, SituacaoDoComando.Expirado, "a catraca não ficou livre a tempo; não executado");
            return $"{rotulo}: expirado, não executado";
        }

        switch (comando.Tipo)
        {
            case TipoDeComando.AcertarRelogio:
            {
                var (ok, feito) = AcertarRelogio(d, agora);
                Concluir(comando, ok ? SituacaoDoComando.Concluido : SituacaoDoComando.Falhou, feito);
                return $"{rotulo}: {feito}";
            }

            case TipoDeComando.MensagemTemporaria:
            {
                var resultado = _adapter.ExibirMensagemTemporaria(
                    d.Inner, comando.Texto ?? string.Empty, TimeSpan.FromSeconds(comando.DuracaoSegundos));
                var feito = resultado.IsOk ? "mensagem exibida" : $"a catraca recusou a mensagem ({resultado})";
                Concluir(comando, resultado.IsOk ? SituacaoDoComando.Concluido : SituacaoDoComando.Falhou, feito);
                return $"{rotulo}: {feito}";
            }

            case TipoDeComando.LiberacaoManual:
            case TipoDeComando.LiberarSaida:
            case TipoDeComando.LiberarDoisSentidos:
                // O giro que vier é do operador: a tentativa pendente do último ingresso
                // termina aqui, sem giro, e não leva a passagem de outra pessoa. Saída e dois
                // sentidos seguem o mesmo caminho da liberação manual — liberar, monitorar o
                // giro, reabilitar o leitor —; só a função nativa muda, em Liberar.
                _antesDaLiberacaoManual?.Invoke(d.Maquina.DeviceId);
                d.TextoDoGiroExibido = false;
                d.EmCurso = new ComandoEmCurso(comando, agora);
                Disparar(d, DeviceTrigger.LiberacaoManualSolicitada, agora);
                return comando.Tipo switch
                {
                    TipoDeComando.LiberarSaida => $"{rotulo}: liberação de saída pedida",
                    TipoDeComando.LiberarDoisSentidos => $"{rotulo}: liberação nos dois sentidos pedida",
                    _ => $"{rotulo}: liberação manual pedida",
                };

            case TipoDeComando.BipCurto:
            case TipoDeComando.BipLongo:
            {
                // Uma chamada, como a mensagem temporária. Só por pedido do operador: o bip
                // nunca é acoplado à decisão automática (cada chamada reduz a vazão, docs/34 §8).
                var bip = comando.Tipo is TipoDeComando.BipCurto ? TipoDeBip.Curto : TipoDeBip.Longo;
                var resultado = _adapter.AcionarBip(d.Inner, bip);
                var feito = resultado.IsOk ? "bip acionado" : $"a catraca recusou o bip ({resultado})";
                Concluir(comando, resultado.IsOk ? SituacaoDoComando.Concluido : SituacaoDoComando.Falhou, feito);
                return $"{rotulo}: {feito}";
            }

            case TipoDeComando.ReiniciarConexao:
            case TipoDeComando.AplicarConfiguracao:
                if (comando.Tipo is TipoDeComando.AplicarConfiguracao)
                {
                    if (d.ConfiguracaoNova is not { } nova)
                    {
                        Concluir(comando, SituacaoDoComando.Falhou, "nenhuma configuração nova foi carregada");
                        return $"{rotulo}: sem configuração nova";
                    }

                    d.Configuracao = nova;
                    d.ConfiguracaoNova = null;
                }

                d.EmCurso = new ComandoEmCurso(comando, agora);
                Disparar(d, DeviceTrigger.ReconexaoSolicitada, agora);
                return $"{rotulo}: reconectando";

            case TipoDeComando.ColetarBilhetes:
                if (_gravadorDeBilhetes is null)
                {
                    Concluir(comando, SituacaoDoComando.Falhou, "este worker não tem onde gravar bilhetes; nada foi coletado");
                    return $"{rotulo}: sem onde gravar, não executado";
                }

                // A coleta tira a catraca de Polling até a memória esvaziar: durante ela, a
                // catraca não atende leitura neste laço. Por isso é só por pedido (docs/34 §8).
                d.EmCurso = new ComandoEmCurso(comando, agora);
                d.Coleta = new ColetaDeBilhetes(comando.Id);
                Disparar(d, DeviceTrigger.IniciarColetaDeBilhetes, agora);
                return $"{rotulo}: coleta iniciada";

            default:
                Concluir(comando, SituacaoDoComando.Falhou, "comando sem execução neste worker");
                return $"{rotulo}: desconhecido";
        }
    }

    /// <summary>Fecha o comando de vários passos quando a catraca termina o que ele pediu.</summary>
    private string? AcompanharComando(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.EmCurso is not { } emCurso)
        {
            return null;
        }

        var estado = d.Maquina.Current;

        if (emCurso.Comando.Tipo is TipoDeComando.ColetarBilhetes)
        {
            // Termina quando a catraca sai da coleta e o último bilhete já está na base.
            if (estado is DeviceState.ColetarBilhetes || d.BilheteAGravar is not null)
            {
                return null;
            }

            d.EmCurso = null;
            var resumo = d.Coleta?.Resumo() ?? "nenhum bilhete coletado";
            d.Coleta = null;

            if (estado is DeviceState.Polling)
            {
                Concluir(emCurso.Comando, SituacaoDoComando.Concluido, $"memória da catraca vazia; {resumo}");
                return resumo;
            }

            // Erro de comunicação ou tempo esgotado no meio: o que já foi coletado está gravado;
            // o que ficou na catraca sai no próximo pedido.
            var interrompida = $"coleta interrompida (estado {estado}); {resumo}; o restante fica na catraca para o próximo pedido";
            Concluir(emCurso.Comando, SituacaoDoComando.Falhou, interrompida);
            return interrompida;
        }

        if (ComandoDeCatraca.EhLiberacao(emCurso.Comando.Tipo))
        {
            var liberada = emCurso.Comando.Tipo switch
            {
                TipoDeComando.LiberarSaida => "liberada na saída",
                TipoDeComando.LiberarDoisSentidos => "liberada nos dois sentidos",
                _ => "liberada",
            };

            if (estado is DeviceState.LiberarCatraca or DeviceState.MonitoraGiroCatraca)
            {
                // A saída de MonitoraGiro é a origem 5 (fim do tempo de acionamento) ou, se ela
                // não vier, o prazo do estado (C1, docs/36: tempo do relé 1 + 3 s, no máximo
                // 53 s). Este limite é a última defesa, para o comando não ficar aberto para sempre.
                if (agora - emCurso.IniciadoEm <= LimiteDaLiberacaoManual)
                {
                    return null;
                }

                d.EmCurso = null;
                var semSinal = emCurso.Girou
                    ? $"{liberada}; girou"
                    : $"{liberada}; a catraca não informou giro nem fim do tempo em {LimiteDaLiberacaoManual.TotalSeconds:0} s";
                Concluir(emCurso.Comando, SituacaoDoComando.Concluido, semSinal);
                return semSinal;
            }

            d.EmCurso = null;
            var (situacao, resultado) = emCurso switch
            {
                { Liberou: false } => (SituacaoDoComando.Falhou, "a catraca não recebeu a liberação"),
                { Girou: true } => (SituacaoDoComando.Concluido, $"{liberada}; girou"),
                { GiroNaoConfirmadoNoPrazo: { } prazo } =>
                    (SituacaoDoComando.Concluido, $"{liberada}; {GiroNaoConfirmado(prazo)}"),
                _ => (SituacaoDoComando.Concluido, $"{liberada}; ninguém girou"),
            };
            Concluir(emCurso.Comando, situacao, resultado);
            return resultado;
        }

        // Reconexão pedida: conclui quando a catraca volta a atender.
        if (estado is not DeviceState.Polling)
        {
            emCurso.SaiuDeOperacao = true;
        }
        else if (emCurso.SaiuDeOperacao)
        {
            d.EmCurso = null;
            var resultado = emCurso.Comando.Tipo is TipoDeComando.AplicarConfiguracao
                ? "configuração enviada; catraca atendendo"
                : "reconectada; catraca atendendo";
            Concluir(emCurso.Comando, SituacaoDoComando.Concluido, resultado);
            return resultado;
        }

        if (agora - emCurso.IniciadoEm > LimiteDaReconexaoPedida)
        {
            d.EmCurso = null;
            var resultado = $"a catraca não voltou a atender em {LimiteDaReconexaoPedida.TotalMinutes:0} min (estado {estado})";
            Concluir(emCurso.Comando, SituacaoDoComando.Falhou, resultado);
            return resultado;
        }

        return null;
    }

    private void Concluir(ComandoDeCatraca comando, SituacaoDoComando situacao, string resultado) =>
        _aoConcluirComando?.Invoke(comando, situacao, resultado);

    private string? CuidarDoRelogio(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.AcertarRelogioEm is { } acertar && agora >= acertar)
        {
            return AcertarRelogio(d, agora).Feito;
        }

        if (d.ConferirRelogioEm is { } conferir && agora >= conferir)
        {
            return ConferirRelogio(d, agora);
        }

        return null;
    }

    // Falha no relógio nunca derruba a catraca: não conta no disjuntor nem dispara
    // reconexão. Se a comunicação caiu de fato, a próxima espera por evento percebe.
    private (bool Ok, string Feito) AcertarRelogio(DeviceSlot d, DateTimeOffset agora)
    {
        AdapterResult resultado;
        try
        {
            resultado = _adapter.AcertarRelogio(d.Inner, agora);
        }
        catch (ArgumentOutOfRangeException)
        {
            // O relógio do PC está fora de 2000–2099: acertar a catraca por ele seria pior.
            d.AcertarRelogioEm = null;
            d.ConferirRelogioEm = agora + PrimeiraConferenciaDoRelogio;
            return (false, "relógio do PC fora do intervalo que a catraca guarda — relógio da catraca não acertado");
        }

        if (!resultado.IsOk)
        {
            d.AcertarRelogioEm = agora + EsperaAposFalhaNoRelogio;
            return (false, $"falha ao acertar o relógio ({resultado}) — a operação segue");
        }

        d.RelogioAcertadoEm = agora;
        d.AcertarRelogioEm = null;
        d.ConferirRelogioEm = agora + PrimeiraConferenciaDoRelogio;
        return (true, "relógio acertado");
    }

    private string ConferirRelogio(DeviceSlot d, DateTimeOffset agora)
    {
        var (resultado, lido) = _adapter.LerRelogio(d.Inner);

        if (!resultado.IsOk || lido is not { } relogio)
        {
            d.ConferirRelogioEm = agora + EsperaAposFalhaNoRelogio;
            return $"falha ao ler o relógio ({resultado}) — a operação segue";
        }

        d.RelogioConferidoEm = agora;
        d.ConferirRelogioEm = agora + IntervaloDeConferenciaDoRelogio;
        d.RelogioInvalido = relogio == DateTimeOffset.MinValue;
        d.DivergenciaDoRelogio = d.RelogioInvalido
            ? null
            : TimeSpan.FromSeconds(Math.Round((relogio - agora).TotalSeconds));

        if (!d.RelogioDivergente)
        {
            return string.Create(CultureInfo.InvariantCulture, $"relógio conferido ({d.DivergenciaDoRelogio!.Value.TotalSeconds:+0;-0;0} s)");
        }

        var descricao = d.RelogioInvalido
            ? "data inválida"
            : string.Create(CultureInfo.InvariantCulture, $"{d.DivergenciaDoRelogio!.Value.TotalSeconds:+0;-0} s");

        if (_acertarRelogioAoDivergir)
        {
            d.AcertarRelogioEm = agora;
            return $"relógio divergente ({descricao}) — será acertado";
        }

        return $"relógio divergente ({descricao}) — acerte pelo painel";
    }

    /// <summary>
    /// O prazo do estado atual, quando já estourou; nulo quando não estourou, quando o estado não
    /// tem prazo ou quando o laço não o aplica.
    /// </summary>
    /// <remarks>
    /// Medido desde a entrada no estado (<see cref="DeviceStateMachine.EstadoAtualDesde"/>), pelo
    /// relógio injetado. O prazo de <see cref="DeviceState.ValidarAcesso"/> (150 ms) nunca é
    /// aplicado aqui: é o orçamento da decisão (medido em <see cref="Decision"/>), não um prazo
    /// de passo, e o destino da tabela para ele é <see cref="DeviceState.LiberarCatraca"/> —
    /// aplicá-lo liberaria o giro sem decisão sempre que a vez da catraca no laço demorasse mais
    /// de 150 ms, ou com o motor de decisão ausente. Falha de base nega, nunca libera (ADR-0013).
    /// </remarks>
    private static TimeSpan? PrazoEstourado(DeviceSlot d, DateTimeOffset agora)
    {
        var estado = d.Maquina.Current;
        if (estado is DeviceState.ValidarAcesso
            || d.Maquina.EstadoAtualDesde is not { } desde
            || DeviceStateMachine.PrazoEfetivo(estado, d.Configuracao) is not { } prazo)
        {
            return null;
        }

        return agora - desde >= prazo ? prazo : null;
    }

    /// <summary>
    /// O prazo dos estados de uma chamada só (conexão, identidade, configuração, rearme,
    /// mensagem, liberação), quando o passo não pôde fazer a chamada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nesses estados o próprio passo resolve o estado: a chamada dá certo e a máquina segue, ou
    /// falha e vai para a reconexão. O prazo só tem o que fazer quando o passo é impedido de
    /// chamar — espera de backoff ou disjuntor aberto — e o estado ficaria parado. Aí o caminho
    /// é o da tabela: reconectar, degradar ou quarentenar.
    /// </para>
    /// <para>
    /// A espera pela vez no laço <b>não</b> conta contra eles. Com até 20 catracas numa thread
    /// e chamadas que bloqueiam segundos (conexão a uma catraca morta, envio da configuração),
    /// uma volta passa de 5 s numa reconexão em massa: aplicar o prazo no começo do passo
    /// mandaria a catraca sadia, que acabou de conectar, para a quarentena (prazo de
    /// <see cref="DeviceState.LendoIdentidade"/>) ou prenderia as catracas num ciclo de
    /// configurar e reconectar sem nunca chegar a enviar.
    /// </para>
    /// </remarks>
    private string? PrazoComOPassoImpedido(DeviceSlot d, DateTimeOffset agora, string motivo) =>
        PrazoEstourado(d, agora) is { } prazo ? $"{EsgotarPrazo(d, agora, prazo)} ({motivo})" : null;

    /// <summary>Dispara o tempo esgotado do estado atual, com o efeito próprio de cada um.</summary>
    private string EsgotarPrazo(DeviceSlot d, DateTimeOffset agora, TimeSpan prazo)
    {
        var estado = d.Maquina.Current;

        if (estado is DeviceState.MonitoraUrna or DeviceState.AcionarUrna)
        {
            // A urna não respondeu: ninguém passou (o braço não foi liberado), o uso volta.
            DesistirDaUrna(d, estado is DeviceState.MonitoraUrna
                ? $"o cartão não caiu na urna no prazo de {Segundos(prazo)} s, sem origem 5 nem 7"
                : $"a urna não abriu no prazo de {Segundos(prazo)} s");
            d.MensagemDeNegacao = MensagensDaCatraca.CartaoNaoRecolhido;
        }

        Disparar(d, DeviceTrigger.TempoEsgotado, agora);

        if (estado is DeviceState.MonitoraUrna)
        {
            return $"prazo de {Segundos(prazo)} s esperando o cartão cair na urna — giro não liberado, uso devolvido";
        }

        if (estado is not DeviceState.MonitoraGiroCatraca)
        {
            if (estado is DeviceState.LiberarCatraca)
            {
                // A liberação nem saiu: nenhum giro pode ser da pessoa autorizada.
                _aoFalharALiberacao?.Invoke(d.Maquina.DeviceId, $"a liberação não saiu no prazo de {Segundos(prazo)} s");
                _aoDesistirDoGiro?.Invoke(d.Maquina.DeviceId);
            }

            return $"prazo de {Segundos(prazo)} s em {estado} esgotado — {d.Maquina.Current}";
        }

        // Liberou e não veio origem 5 nem 6: o mesmo desfecho da origem 5, sem inventar giro. A
        // tentativa pendente termina sem giro e o leitor é rearmado no próximo passo
        // (MonitoraGiroCatraca → ConfigurarEntradasOnline → EnviarMsgPadrao → Polling).
        _aoDesistirDoGiro?.Invoke(d.Maquina.DeviceId);
        if (d.EmCurso is { Liberou: true, Girou: false } manual)
        {
            manual.GiroNaoConfirmadoNoPrazo = prazo;
        }

        return $"{GiroNaoConfirmado(prazo)}, sem origem 5 nem 6 — rearmando o leitor";
    }

    /// <summary>
    /// Keep-alive do polling (manual de desenvolvimento, passos 11 e 12): a cada
    /// <see cref="_intervaloDoPingOnLine"/> manda PingOnLine à catraca. Sem resposta, tenta de novo no
    /// passo seguinte; depois de <see cref="LimiteDeFalhasDePingOnLine"/> falhas seguidas, reconecta.
    /// Quando não é hora, ou a catraca respondeu, devolve nulo e o passo segue para a leitura.
    /// </summary>
    /// <remarks>
    /// Antes este teste era o <c>Ping</c> (teste de conexão) a cada 10 s. O manual recomenda o PingOnLine
    /// justamente para a conexão TCP e para manter a catraca on-line (sem ele, a mudança automática
    /// manda a catraca para off-line). Uma chamada nativa por passo, como sempre.
    /// </remarks>
    private string? ManterOnline(DeviceSlot d, DateTimeOffset agora)
    {
        if (d.UltimoPingOnLine is { } ultimo && agora - ultimo < _intervaloDoPingOnLine)
        {
            return null;
        }

        d.UltimoPingOnLine = agora;
        var resultado = _adapter.Ping(d.Inner);
        if (resultado.IsOk)
        {
            d.FalhasDePingOnLine = 0;
            d.UltimaProvaDeVida = agora;
            return null;
        }

        if (resultado.Status is AdapterStatus.FalhaDeDependencia)
        {
            Disparar(d, DeviceTrigger.DependenciaFatal, agora);
            return $"falha de dependência no PingOnLine ({resultado}) — worker inutilizável";
        }

        d.FalhasDePingOnLine++;
        if (d.FalhasDePingOnLine < LimiteDeFalhasDePingOnLine)
        {
            return null;
        }

        var falhas = d.FalhasDePingOnLine;
        d.FalhasDePingOnLine = 0;
        Falhar(d, agora, DeviceTrigger.ErroDeComunicacao);
        return $"catraca não respondeu a {falhas} PingOnLine seguidos ({resultado}) — reconectando";
    }

    /// <summary>
    /// Uma saída por erro de comunicação no meio de uma liberação encerra a tentativa pendente,
    /// como o prazo já fazia (achado E1-05 do docs/41). Antes, a falha em LiberarCatraca ou na
    /// recepção durante o giro deixava a tentativa aberta: o giro que chegasse depois da reconexão
    /// confirmava a passagem errada, e a prestação de contas não via o uso sem passagem.
    /// </summary>
    private void DesistirDoGiroEmCurso(DeviceSlot d)
    {
        if (d.Maquina.Current is DeviceState.MonitoraUrna)
        {
            // O braço não foi liberado: ninguém passou. Se o cartão caiu e o aviso se perdeu, ele está na
            // urna e a contagem dela mostra a diferença (docs/04 §6).
            DesistirDaUrna(d, "a comunicação caiu esperando o cartão cair na urna");
            return;
        }

        if (d.Maquina.Current is DeviceState.LiberarCatraca or DeviceState.MonitoraGiroCatraca)
        {
            _aoFalharALiberacao?.Invoke(
                d.Maquina.DeviceId,
                d.Maquina.Current is DeviceState.LiberarCatraca
                    ? "a catraca não aceitou a liberação (erro de comunicação)"
                    : "a comunicação caiu esperando o giro: não se sabe se a pessoa passou");
            _aoDesistirDoGiro?.Invoke(d.Maquina.DeviceId);
        }
    }

    private static string GiroNaoConfirmado(TimeSpan prazo) => $"giro não confirmado: prazo de {Segundos(prazo)} s";

    private static string Segundos(TimeSpan prazo) =>
        prazo.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Disparar(DeviceSlot d, DeviceTrigger gatilho, DateTimeOffset agora) =>
        d.Maquina.TryFire(gatilho, agora, $"pump-{d.Inner}-{agora.UtcTicks}", out _);

    private static void Falhar(DeviceSlot d, DateTimeOffset agora, DeviceTrigger gatilho)
    {
        d.Disjuntor.RegistrarFalha();
        d.TentativasDeReconexao++;
        d.EsperarAte = agora + new BackoffComJitter().Para(d.TentativasDeReconexao);
        Disparar(d, gatilho, agora);
    }
}
