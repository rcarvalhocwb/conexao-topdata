using Access.Domain.Credentials;
using Access.Domain.Devices;

namespace Access.Application.Devices;

/// <summary>
/// A função nativa exata que libera o giro de quem <b>entra</b> nesta catraca.
/// </summary>
/// <remarks>
/// <para>
/// Cada valor é uma função da DLL, e só uma: <c>LiberarCatracaEntrada</c> (EI-041),
/// <c>LiberarCatracaEntradaInvertida</c> (EI-043), <c>LiberarCatracaSaida</c> (EI-042) e
/// <c>LiberarCatracaSaidaInvertida</c> (EI-044).
/// </para>
/// <para>
/// Existe para corrigir o defeito F1 do docs/34 §2: o perfil guardava só "sentido
/// invertido", o laço trocava Entrada por Saída e o adapter, com o mesmo perfil, trocava de
/// novo para a variante invertida — a catraca instalada à esquerda receberia
/// <c>LiberarCatracaSaidaInvertida</c>. Agora a escolha é feita uma vez, no
/// comissionamento, e ninguém combina sinalizadores depois (anexo 01 §1.11).
/// </para>
/// <para>
/// Qual valor serve a uma catraca instalada à esquerda é <c>A_CONFIRMAR_COM_TOPDATA</c>:
/// ensaios HIL-DIR-05 e HIL-DIR-06 do docs/21.
/// </para>
/// </remarks>
public enum FuncaoDeLiberacao
{
    /// <summary><c>LiberarCatracaEntrada</c> (EI-041). O padrão de hoje.</summary>
    Entrada,

    /// <summary><c>LiberarCatracaEntradaInvertida</c> (EI-043).</summary>
    EntradaInvertida,

    /// <summary><c>LiberarCatracaSaida</c> (EI-042).</summary>
    Saida,

    /// <summary><c>LiberarCatracaSaidaInvertida</c> (EI-044).</summary>
    SaidaInvertida,
}

/// <summary>Perfil físico do portão, resultado do comissionamento.</summary>
/// <param name="FuncaoDeLiberacaoDaEntrada">
/// A função que libera quem entra. O padrão, <see cref="FuncaoDeLiberacao.Entrada"/>, é o
/// comportamento de sempre.
/// </param>
public sealed record GatePhysicalProfile(FuncaoDeLiberacao FuncaoDeLiberacaoDaEntrada = FuncaoDeLiberacao.Entrada)
{
    /// <summary>O perfil de uma catraca comissionada sem inversão.</summary>
    public static GatePhysicalProfile Padrao { get; } = new();

    /// <summary>
    /// Para cada origem que libera, qual função chamar e como contar o giro (D9, docs/34 §9).
    /// Vazio = como sempre: toda origem chama <see cref="FuncaoDeLiberacaoDaEntrada"/> e conta
    /// como entrada.
    /// </summary>
    public MapaDeGiro MapaDeGiro { get; init; } = MapaDeGiro.Vazio;

    /// <summary>A função de uma origem, com o rótulo e o texto: a regra do mapa ou o padrão de hoje.</summary>
    /// <param name="origem">De onde veio o pedido de liberar.</param>
    public GiroResolvido Resolver(OrigemDoGiro origem)
    {
        var regra = MapaDeGiro.Regra(origem);
        var contaComo = regra?.ContaComo ?? SentidoContado.Entrada;
        return new GiroResolvido(
            origem,
            regra?.Funcao ?? FuncaoDeLiberacaoDaEntrada,
            contaComo,
            regra?.Texto ?? MapaDeGiro.TextoPadrao(contaComo),
            DoMapa: regra is not null);
    }

    /// <summary>O pedido ao adapter que libera o giro desta origem.</summary>
    /// <remarks>Com o mapa vazio, é <see cref="LiberacaoDaEntrada"/> para toda origem.</remarks>
    public GateDirection LiberacaoPara(OrigemDoGiro origem) => Resolver(origem).Direcao;

    /// <summary>Tradução um para um da função para o pedido ao adapter.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Valor fora do enum; <see cref="DeviceConfiguration.Validar"/> recusa antes.</exception>
    public static GateDirection Direcao(FuncaoDeLiberacao funcao) => funcao switch
    {
        FuncaoDeLiberacao.Entrada => GateDirection.Entrada,
        FuncaoDeLiberacao.EntradaInvertida => GateDirection.EntradaInvertida,
        FuncaoDeLiberacao.Saida => GateDirection.Saida,
        FuncaoDeLiberacao.SaidaInvertida => GateDirection.SaidaInvertida,
        _ => throw new ArgumentOutOfRangeException(nameof(funcao), funcao, "Função de liberação desconhecida."),
    };

    /// <summary>
    /// O pedido ao adapter que libera quem entra: tradução um para um, sem combinar nada.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Valor fora do enum; <see cref="DeviceConfiguration.Validar"/> recusa antes.</exception>
    public GateDirection LiberacaoDaEntrada => FuncaoDeLiberacaoDaEntrada switch
    {
        FuncaoDeLiberacao.Entrada => GateDirection.Entrada,
        FuncaoDeLiberacao.EntradaInvertida => GateDirection.EntradaInvertida,
        FuncaoDeLiberacao.Saida => GateDirection.Saida,
        FuncaoDeLiberacao.SaidaInvertida => GateDirection.SaidaInvertida,
        _ => throw new ArgumentOutOfRangeException(
            nameof(FuncaoDeLiberacaoDaEntrada), FuncaoDeLiberacaoDaEntrada, "Função de liberação desconhecida."),
    };

    /// <summary>
    /// O pedido ao adapter que libera quem <b>sai</b>, para o comando "Liberar saída" do
    /// operador (Etapa A.8 do docs/35): a outra função do mesmo par da matriz.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A matriz agrupa as funções em dois pares: instalação direta, <c>LiberarCatracaEntrada</c>
    /// (EI-041) e <c>LiberarCatracaSaida</c> (EI-042); instalação invertida,
    /// <c>LiberarCatracaEntradaInvertida</c> (EI-043) e <c>LiberarCatracaSaidaInvertida</c>
    /// (EI-044). Quem sai é liberado pela outra função do par da entrada comissionada; nada é
    /// combinado com sinalizador (defeito F1, docs/34 §2).
    /// </para>
    /// <para>
    /// Que a outra função do par gire mesmo no sentido de quem sai, em cada instalação, é
    /// <c>A_CONFIRMAR_COM_TOPDATA</c>: ensaios HIL-DIR-04 (direta) e HIL-DIR-05/06 (invertida).
    /// Por isso o comando fica atrás da chave técnica <c>comando.liberar_saida</c>, desligada.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Valor fora do enum; <see cref="DeviceConfiguration.Validar"/> recusa antes.</exception>
    public GateDirection LiberacaoDaSaida => FuncaoDeLiberacaoDaEntrada switch
    {
        FuncaoDeLiberacao.Entrada => GateDirection.Saida,
        FuncaoDeLiberacao.EntradaInvertida => GateDirection.SaidaInvertida,
        FuncaoDeLiberacao.Saida => GateDirection.Entrada,
        FuncaoDeLiberacao.SaidaInvertida => GateDirection.EntradaInvertida,
        _ => throw new ArgumentOutOfRangeException(
            nameof(FuncaoDeLiberacaoDaEntrada), FuncaoDeLiberacaoDaEntrada, "Função de liberação desconhecida."),
    };
}

/// <summary>
/// Configuração <b>completa</b> de um equipamento. Não existe configuração parcial.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnviarConfiguracoes</c> envia os valores padrão da DLL para tudo que não tiver
/// sido montado explicitamente, sobrescrevendo em silêncio o que havia no equipamento —
/// inclusive o que foi ajustado pelo WebServer. Por isso todo campo aqui é obrigatório:
/// esquecer um não deixa "como estava", volta ao padrão da DLL.
/// Ver docs/ADR/ADR-0020-configuracao-sempre-completa.md
/// </para>
/// </remarks>
public sealed record DeviceConfiguration
{
    /// <summary>0 = Topdata, 1 = Livre.</summary>
    public required byte PadraoCartao { get; init; }

    /// <summary>Quantidade fixa de dígitos, quando o padrão exigir.</summary>
    public byte? QuantidadeFixaDeDigitos { get; init; }

    /// <summary>Comprimentos aceitos, quando o equipamento usa dígitos variáveis.</summary>
    /// <remarks>
    /// Só chega à catraca com <see cref="EnviarDigitosVariaveis"/> ligado. Desligado, a
    /// catraca segue com o padrão da DLL para dígitos variáveis (docs/34 §2, F2).
    /// </remarks>
    public IReadOnlyList<byte> QuantidadesVariaveisDeDigitos { get; init; } = [];

    /// <summary>
    /// Envia <see cref="QuantidadesVariaveisDeDigitos"/> à catraca, uma chamada de
    /// <c>InserirQuantidadeDigitoVariavel</c> (EI-012) por tamanho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Desligado por padrão, e desligado nada é enviado: a sequência nativa é a de sempre e a
    /// catraca fica com o padrão da DLL para dígitos variáveis, valor que ninguém conhece
    /// (defeito F2 do docs/34 §2; ADR-0020). Liga pela chave técnica
    /// <c>catraca.enviar_digitos_variaveis</c>, sem tela.
    /// </para>
    /// <para>
    /// Fica desligado até o ensaio HIL-CARD-02: o manual diz "uma chamada por tamanho aceito"
    /// e "0 desabilita" (FUN:13), mas não diz se os tamanhos acumulam entre uma montagem e
    /// outra depois de uma falha (T30) — <c>A_CONFIRMAR_COM_TOPDATA</c>.
    /// </para>
    /// </remarks>
    public bool EnviarDigitosVariaveis { get; init; }

    /// <summary>Tecnologia do leitor: 0 a 8 (8 = QR Code por letras).</summary>
    public required byte TipoDeLeitor { get; init; }

    /// <summary>Operação do leitor 1: 0 a 4.</summary>
    public required byte OperacaoDoLeitor1 { get; init; }

    /// <summary>Operação do leitor 2. É o leitor da fenda da urna.</summary>
    public required byte OperacaoDoLeitor2 { get; init; }

    /// <summary>Função do relé 1: 0 a 9 (enum do SDK; o manual parava em 5).</summary>
    public required byte FuncaoDoAcionamento1 { get; init; }

    /// <summary>Tempo do relé 1, de 0 a 50 segundos.</summary>
    public required byte TempoDoAcionamento1 { get; init; }

    /// <summary>Função do relé 2 (urna).</summary>
    public required byte FuncaoDoAcionamento2 { get; init; }

    /// <summary>Tempo do relé 2, de 0 a 50 segundos.</summary>
    public required byte TempoDoAcionamento2 { get; init; }

    /// <summary>Verdadeiro para modo on-line; falso para off-line.</summary>
    /// <remarks>
    /// É o <c>RegimeAlvo</c> do docs/34 §4.1 (anexo 01 §3.1: "<c>Online</c> → <c>RegimeAlvo</c>"),
    /// reaproveitado em vez de duplicado (Etapa A.2). Sem a sequência oficial, cada passo manda
    /// a configuração completa e este campo escolhe <c>ConfigurarInnerOnLine</c> (EI-018) ou
    /// <c>ConfigurarInnerOffLine</c> (EI-019). Com ela (Etapa A.7, chave
    /// <c>catraca.sequencia_oficial</c>: cfg off-line → mudança → cfg on-line) a cfg off-line
    /// usa off-line sempre, e este campo diz em qual regime a catraca <b>termina</b>, lido como
    /// <see cref="Devices.RegimeAlvo"/> por <see cref="RegimeDaConfiguracao.RegimeAlvo"/>. Não foi
    /// renomeado nem duplicado: um campo novo mudaria a cobertura da ADR-0020 sem valor novo.
    /// </remarks>
    public required bool Online { get; init; }

    public required bool TecladoHabilitado { get; init; }

    /// <summary>Eco do teclado no display: 0, 1 ou 2.</summary>
    public required byte EcoDoTeclado { get; init; }

    /// <summary>Mudança automática on-line/off-line: 0, 1 ou 2.</summary>
    public required byte MudancaAutomatica { get; init; }

    /// <summary>Tempo da mudança automática, de 1 a 50.</summary>
    public required byte TempoDaMudancaAutomatica { get; init; }

    /// <summary>Mensagem exibida no display quando ocioso. Até 32 caracteres.</summary>
    public required string MensagemPadrao { get; init; }

    /// <summary>Perfil físico do portão, do comissionamento.</summary>
    /// <remarks>
    /// Guarda a <c>FuncaoDeLiberacaoDaEntrada</c> do docs/34 §4.1 (Etapa 0.1, F1): o campo do
    /// modelo completo já existe aqui e não é duplicado.
    /// </remarks>
    public required GatePhysicalProfile PerfilFisico { get; init; }

    // ─────────────────────────────────────────────────────────────────────────────────────
    // Etapa A.2 do docs/35: o resto do modelo do docs/34 §4.1.
    //
    // Nenhum campo novo é "required", e o valor inicial de cada um não muda nada na catraca.
    // O valor inicial é o padrão proposto no anexo 01 §3.1 quando há fonte para ele, ou nulo
    // quando não há. O que depende de bancada só chega à DLL com a sua chave técnica
    // (edge_setting, sem tela, desligada): desligada = não enviado = o comportamento de antes,
    // com a catraca no padrão da DLL para aquele parâmetro (ADR-0020, registrado no docs/34).
    // As exceções são nulas por construção: RegistrarAcessoNegado (a chave é o próprio valor)
    // e CartaoMaster (sem origem até a custódia DPAPI existir). Sem função com linha na matriz
    // FUN, o campo fica no modelo e não é enviado (WebServer e mensagens).
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fixo ou variável. Nulo = não declarado: vale o comportamento de antes da Etapa A.2
    /// (quantidade fixa se preenchida; tamanhos variáveis só com a chave).
    /// </summary>
    /// <remarks>
    /// Declarado, liga as regras 1 e 2 do docs/34 §4.2. O padrão de fábrica ainda não declara
    /// (seria <see cref="Devices.ModoDeDigitos.Variavel"/>): declarar sem a chave
    /// <c>catraca.enviar_digitos_variaveis</c> ligada não mudaria nada na catraca, e a
    /// configuração da Etapa A.1 fica idêntica. Não é enviado: escolhe EI-011 ou EI-012.
    /// </remarks>
    public ModoDeDigitos? ModoDeDigitos { get; init; }

    /// <summary>
    /// A catraca manda data e hora junto do evento on-line: <c>ReceberDataHoraDadosOnLine</c>
    /// (EI-027, FUN:28, 0 ou 1). Padrão proposto: ligado (anexo 01 §3.1).
    /// </summary>
    /// <remarks>
    /// Só é enviado com <see cref="EnviarDataHoraNoEventoOnLine"/>. Hoje vai o padrão da DLL e o
    /// adapter monta a data com o que vier — se vier zerada, <c>AguardarEvento</c> já trata data
    /// inválida.
    /// </remarks>
    public bool DataHoraNoEventoOnLine { get; init; } = true;

    /// <summary>
    /// Chave técnica <c>catraca.enviar_data_hora_no_evento</c>. Desligada até o ensaio
    /// INT-CFG-07 (<c>A_CONFIRMAR_COM_TOPDATA</c>: o padrão da DLL não é conhecido, T13).
    /// </summary>
    public bool EnviarDataHoraNoEventoOnLine { get; init; }

    /// <summary>
    /// <c>RegistrarAcessoNegado(TipoRegistro)</c> — EI-021, FUN:22, faixa 0 a 3. Nulo = não
    /// enviado (a catraca fica com o padrão da DLL).
    /// </summary>
    /// <remarks>
    /// A matriz dá a faixa, não o significado de cada valor: nenhum valor é escolhido aqui. A
    /// chave técnica <c>catraca.registrar_acesso_negado</c> leva o próprio valor (vazia =
    /// desligada), para o ensaio INT-OFF-08 descobrir o que cada um faz. Cada negação
    /// registrada ocupa a memória circular de 30.000 marcações (docs/34 §3.3).
    /// </remarks>
    public byte? RegistrarAcessoNegado { get; init; }

    /// <summary>
    /// <c>DefinirTipoListaAcesso</c> — EI-033, FUN:34: 0 não usar, 1 lista branca, 2 lista
    /// negra. Padrão: 0.
    /// </summary>
    /// <remarks>
    /// Na Etapa A.2 só o 0 é válido: não existe lista gravada na catraca (Etapa D, decisões D3
    /// e D5). Enviar o 0 explícito tira do padrão da DLL a decisão "usar lista ou não".
    /// </remarks>
    public byte TipoDeLista { get; init; }

    /// <summary>
    /// Chave técnica <c>catraca.enviar_tipo_de_lista</c>. Desligada até o ensaio INT-OFF-02.
    /// </summary>
    public bool EnviarTipoDeLista { get; init; }

    /// <summary><c>ConfigurarWiegandDoisLeitores</c> — EI-024, FUN:25. Padrão proposto: (0, 0).</summary>
    public WiegandDoisLeitores WiegandDoisLeitores { get; init; }

    /// <summary>
    /// Chave técnica <c>catraca.enviar_wiegand_dois_leitores</c>. Desligada até o ensaio HIL-CARD-05.
    /// </summary>
    public bool EnviarWiegandDoisLeitores { get; init; }

    /// <summary>
    /// Os parâmetros de <c>EnviarFormasEntradasOnLine</c> (EI-032), o rearme do leitor.
    /// Padrão: os valores de sempre (<see cref="FormasDeEntradaOnLine.DeHoje"/>).
    /// </summary>
    public FormasDeEntradaOnLine FormasDeEntradaOnLine { get; init; } = FormasDeEntradaOnLine.DeHoje;

    /// <summary>
    /// Chave técnica <c>catraca.enviar_formas_de_entrada</c>. Desligada, o rearme vai com os
    /// valores de sempre; ligada, com <see cref="FormasDeEntradaOnLine"/>. Desligada até
    /// INT-SM-032 (T26: significado de cada <c>FormaEntrada</c>).
    /// </summary>
    public bool EnviarFormasDeEntradaOnLine { get; init; }

    /// <summary>
    /// O cartão master (<c>DefinirNumeroCartaoMaster</c>, EI-023). Nulo = não enviado.
    /// </summary>
    /// <remarks>
    /// Vai à DLL sempre que existir; na Etapa A.2 ele nunca existe, porque a custódia (número
    /// aleatório, cifrado com DPAPI) é PROPOSTA FUTURA — ver <see cref="CodigoDoCartaoMaster"/>.
    /// </remarks>
    public CodigoDoCartaoMaster? CartaoMaster { get; init; }

    /// <summary>
    /// WebServer da catraca desabilitado. Nulo = não definido. <b>Não é enviado.</b>
    /// </summary>
    /// <remarks>
    /// A função existe no SDK (<c>DesabilitarWebServer(byte)</c>, inventário linha 83), mas não
    /// tem linha na matriz FUN, e o sentido de 0/1, se persiste e como reabilitar são
    /// <c>INFERIDO</c> (T32, NOVO-SEC-WEB-01). A senha do WebServer fica fora do modelo:
    /// nenhuma função conhecida a grava (PROPOSTA FUTURA).
    /// </remarks>
    public bool? WebServerDesabilitado { get; init; }

    /// <summary>
    /// Mensagem de apresentação de quem entra. Nula = não definida. <b>Não é enviada.</b>
    /// </summary>
    /// <remarks>
    /// <c>DefinirMensagemApresentacaoEntrada</c> só tem assinatura do SDK (inventário linha 62),
    /// sem linha na matriz FUN; o limite e o <c>ExibirData</c> são <c>INFERIDO</c>. Importa por
    /// LGPD: a primeira linha pode mostrar o número do cartão por padrão (anexo 01 §1.9;
    /// ensaio NOVO-INT-MSG-03).
    /// </remarks>
    public MensagemDoDisplay? MensagemDeApresentacaoDaEntrada { get; init; }

    /// <summary>Mensagem de apresentação de quem sai (inventário linha 63). <b>Não é enviada.</b></summary>
    public MensagemDoDisplay? MensagemDeApresentacaoDaSaida { get; init; }

    /// <summary>
    /// Mensagem padrão em off-line (inventário linha 68). <b>Não é enviada.</b>
    /// </summary>
    /// <remarks>
    /// As mensagens off-line têm enviador próprio com Inner (<c>EnviarMensagensOffLine</c>,
    /// inventário linha 113), que o docs/34 §4.3 põe depois do envio da configuração off-line —
    /// um passo da sequência oficial (Etapa A.7) que hoje não existe. Ensaio NOVO-INT-MSG-04.
    /// </remarks>
    public MensagemDoDisplay? MensagemPadraoOffLine { get; init; }

    /// <summary>Mensagem de entrada em off-line (inventário linha 64). <b>Não é enviada.</b></summary>
    public MensagemDoDisplay? MensagemDeEntradaOffLine { get; init; }

    /// <summary>Mensagem de saída em off-line (inventário linha 69). <b>Não é enviada.</b></summary>
    public MensagemDoDisplay? MensagemDeSaidaOffLine { get; init; }

    /// <summary>
    /// Valida os limites documentados no manual, antes de qualquer chamada nativa.
    /// </summary>
    /// <returns>Lista vazia quando a configuração é válida.</returns>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        if (PadraoCartao > 1)
        {
            problemas.Add($"PadraoCartao deve ser 0 (Topdata) ou 1 (Livre); recebido {PadraoCartao}.");
        }

        // 0 a 8, conforme o enum TipoLeitor do SDK oficial. O manual parava em 7 e descrevia
        // o 7 como "TTL Serial ASCII" — errado: 7 é Wiegand FC COM separador, e 8 é QR Code
        // por letras. Eu rejeitava o 8, que é exatamente o leitor de um evento com ingresso
        // em QR.
        if (TipoDeLeitor > 8)
        {
            problemas.Add($"TipoDeLeitor deve estar entre 0 e 8; recebido {TipoDeLeitor}.");
        }

        if (OperacaoDoLeitor1 > 4)
        {
            problemas.Add($"OperacaoDoLeitor1 deve estar entre 0 e 4; recebido {OperacaoDoLeitor1}.");
        }

        if (OperacaoDoLeitor2 > 4)
        {
            problemas.Add($"OperacaoDoLeitor2 deve estar entre 0 e 4; recebido {OperacaoDoLeitor2}.");
        }

        // 0 a 9, conforme o enum FuncaoAcionamento do SDK oficial. O manual parava em 5
        // (revista). Os valores 6 a 9 sinalizam estados da catraca: saída liberada, entrada
        // liberada, liberada nos dois sentidos, e liberada nos dois sentidos com marcação.
        if (FuncaoDoAcionamento1 > 9)
        {
            problemas.Add($"FuncaoDoAcionamento1 deve estar entre 0 e 9; recebido {FuncaoDoAcionamento1}.");
        }

        if (FuncaoDoAcionamento2 > 9)
        {
            problemas.Add($"FuncaoDoAcionamento2 deve estar entre 0 e 9; recebido {FuncaoDoAcionamento2}.");
        }

        if (TempoDoAcionamento1 > 50)
        {
            problemas.Add($"TempoDoAcionamento1 vai de 0 a 50 segundos; recebido {TempoDoAcionamento1}.");
        }

        if (TempoDoAcionamento2 > 50)
        {
            problemas.Add($"TempoDoAcionamento2 vai de 0 a 50 segundos; recebido {TempoDoAcionamento2}.");
        }

        if (EcoDoTeclado > 2)
        {
            problemas.Add($"EcoDoTeclado deve ser 0, 1 ou 2; recebido {EcoDoTeclado}.");
        }

        if (MudancaAutomatica > 2)
        {
            problemas.Add($"MudancaAutomatica deve ser 0, 1 ou 2; recebido {MudancaAutomatica}.");
        }

        if (TempoDaMudancaAutomatica is < 1 or > 50)
        {
            problemas.Add($"TempoDaMudancaAutomatica vai de 1 a 50; recebido {TempoDaMudancaAutomatica}.");
        }

        if (MensagemPadrao.Length > 32)
        {
            problemas.Add($"MensagemPadrao tem no máximo 32 caracteres; recebida com {MensagemPadrao.Length}.");
        }

        if (QuantidadeFixaDeDigitos is { } fixa && (fixa < 1 || fixa > 16))
        {
            problemas.Add($"QuantidadeFixaDeDigitos vai de 1 a 16; recebido {fixa}.");
        }

        foreach (var variavel in QuantidadesVariaveisDeDigitos.Where(v => v is < 1 or > 16))
        {
            problemas.Add($"Quantidade variável de dígitos vai de 1 a 16; recebido {variavel}.");
        }

        // Ligar o envio sem tamanho nenhum não mandaria nada e daria a impressão de que os
        // dígitos variáveis foram configurados (docs/34 §4.2, regra 1).
        if (EnviarDigitosVariaveis && QuantidadesVariaveisDeDigitos.Count == 0)
        {
            problemas.Add("O envio de dígitos variáveis está ligado, mas nenhum tamanho foi informado.");
        }

        // Um valor fora do enum chegaria ao adapter como "sentido desconhecido" no meio de
        // uma passagem; aqui ele é recusado antes de qualquer chamada nativa (F1, docs/34 §2).
        if (!Enum.IsDefined(PerfilFisico.FuncaoDeLiberacaoDaEntrada))
        {
            problemas.Add(
                $"A função de liberação da entrada deve ser Entrada, EntradaInvertida, Saida ou SaidaInvertida; " +
                $"recebido {(int)PerfilFisico.FuncaoDeLiberacaoDaEntrada}.");
        }

        // Mapa de giro (D9): a regra de cada origem decide a função chamada no meio da
        // passagem; valor fora do enum é recusado aqui, antes de qualquer chamada nativa.
        problemas.AddRange(PerfilFisico.MapaDeGiro.Validar());

        // Sem leitor 2 não há como receber o cartão na fenda da urna.
        if (FuncaoDoAcionamento2 != 0 && OperacaoDoLeitor2 == 0)
        {
            problemas.Add(
                "O relé 2 está configurado (urna), mas o leitor 2 está desabilitado: " +
                "a fenda não receberia leitura. Ver manual, seção 7.2.5.");
        }

        // Modo 2 da mudança automática depende de PingOnline periódico.
        if (MudancaAutomatica == 2 && !Online)
        {
            problemas.Add("MudancaAutomatica=2 pressupõe operação on-line com PingOnline periódico.");
        }

        ValidarCamposDaEtapaA2(problemas);
        return problemas;
    }

    /// <summary>
    /// O que é permitido, mas depende de uma resposta da Topdata ou da bancada: não impede o
    /// envio, e quem sobe o worker registra.
    /// </summary>
    /// <remarks>
    /// Duas regras do docs/34 §4.2 não podem ser erro sem recusar a configuração de hoje:
    /// a 9 (o tipo de leitor padrão é 8 com recepção numérica, e só muda depois de T25) e a 11
    /// (o evento aceita tempo do relé 1 até 50 s). Ficam aqui até a bancada decidir.
    /// </remarks>
    /// <returns>Lista vazia quando não há nada a observar.</returns>
    public IReadOnlyList<string> Alertas()
    {
        var alertas = new List<string>();

        // Regra 9 (docs/34 §4.2): o 8 é "QR Code por letras" (FUN:14), mas o adapter recebe pela
        // variante numérica de ReceberDadosOnLine; se a DLL só entregar letras pela variante
        // _QRCodeComLetras, o QR alfanumérico chega vazio. Receber letras exige buffer maior
        // que os 64 bytes de hoje e terminador validado (docs/34 §8). A_CONFIRMAR: T25,
        // NOVO-HIL-QR-02. O QR numérico de 4 a 16 dígitos segue valendo (docs/20 §5).
        if (TipoDeLeitor == 8)
        {
            alertas.Add(
                "TipoDeLeitor 8 (QR por letras) com recepção numérica: QR com letras pode chegar vazio. " +
                "A_CONFIRMAR_COM_TOPDATA (T25, NOVO-HIL-QR-02).");
        }

        // Regra 11 (docs/34 §4.2): a liberação dura o tempo do relé 1, e o laço desiste de
        // esperar o giro em MonitoraGiroCatraca (8 s, DeviceStateMachine). Tempo igual ou maior
        // que essa espera faz o laço voltar a ler com a catraca ainda liberada. A margem é
        // medida na bancada (NOVO-LOAD-LOOP-01), não escolhida aqui.
        if (DeviceStateMachine.TimeoutFor(DeviceState.MonitoraGiroCatraca) is { } espera
            && TimeSpan.FromSeconds(TempoDoAcionamento1) >= espera)
        {
            alertas.Add(
                $"TempoDoAcionamento1 de {TempoDoAcionamento1} s não é menor que a espera pelo giro " +
                $"({espera.TotalSeconds:0} s): o laço pode voltar a ler com a catraca liberada.");
        }

        return alertas;
    }

    /// <summary>Faixas e regras entre campos dos campos da Etapa A.2 (docs/34 §4.1 e §4.2).</summary>
    private void ValidarCamposDaEtapaA2(List<string> problemas)
    {
        ValidarDigitos(problemas);

        // FUN:22: TipoRegistro de 0 a 3. O significado de cada valor é A_CONFIRMAR (INT-OFF-08).
        if (RegistrarAcessoNegado is > 3)
        {
            problemas.Add($"RegistrarAcessoNegado vai de 0 a 3; recebido {RegistrarAcessoNegado}.");
        }

        ValidarLista(problemas);

        // Regra 10 (docs/34 §4.2), na parte que tem fonte: a faixa de FormaEntrada (FUN:33). A
        // coerência com teclado e leitores ativos precisa da tabela de FormaEntrada (T26).
        if (!FormasDeEntradaOnLine.FormaEntradaDocumentada(FormasDeEntradaOnLine.FormaEntrada))
        {
            problemas.Add(
                "FormaEntrada deve estar em 0–7, 10–14 ou 100–105 (FUN:33); " +
                $"recebido {FormasDeEntradaOnLine.FormaEntrada}.");
        }

        if (CartaoMaster is { } master)
        {
            // FUN:24: "até 14 dígitos", "válido para padrão Livre". A mensagem nunca leva o número.
            if (!master.Valido)
            {
                problemas.Add($"O cartão master tem só dígitos, de 1 a {CodigoDoCartaoMaster.MaximoDeDigitos}.");
            }

            if (PadraoCartao != 1)
            {
                problemas.Add("O cartão master só vale com o padrão de cartão Livre (1) (FUN:24).");
            }
        }

        ValidarMensagem(problemas, nameof(MensagemDeApresentacaoDaEntrada), MensagemDeApresentacaoDaEntrada);
        ValidarMensagem(problemas, nameof(MensagemDeApresentacaoDaSaida), MensagemDeApresentacaoDaSaida);
        ValidarMensagem(problemas, nameof(MensagemPadraoOffLine), MensagemPadraoOffLine);
        ValidarMensagem(problemas, nameof(MensagemDeEntradaOffLine), MensagemDeEntradaOffLine);
        ValidarMensagem(problemas, nameof(MensagemDeSaidaOffLine), MensagemDeSaidaOffLine);
    }

    /// <summary>Regras 1 e 2 do docs/34 §4.2, só quando o modo é declarado.</summary>
    private void ValidarDigitos(List<string> problemas)
    {
        switch (ModoDeDigitos)
        {
            case null:
                // Não declarado: as regras de antes da Etapa A.2, acima, continuam valendo.
                return;

            case Devices.ModoDeDigitos.Fixo:
                // Regra 1: Fixo ⇒ 4–16. O manual também fala em 1–16 (T6); fica o mais estreito.
                if (QuantidadeFixaDeDigitos is not { } fixa || fixa < 4 || fixa > 16)
                {
                    problemas.Add(
                        "Com dígitos fixos, QuantidadeFixaDeDigitos é obrigatória e vai de 4 a 16 " +
                        $"(docs/34 §4.2, regra 1); recebido {QuantidadeFixaDeDigitos?.ToString(provider: null) ?? "nada"}.");
                }

                if (EnviarDigitosVariaveis)
                {
                    problemas.Add("Com dígitos fixos, o envio de dígitos variáveis não pode estar ligado.");
                }

                return;

            case Devices.ModoDeDigitos.Variavel:
                // Regra 1: Variável ⇒ conjunto não vazio.
                if (QuantidadesVariaveisDeDigitos.Count == 0)
                {
                    problemas.Add("Com dígitos variáveis, informe ao menos um tamanho (docs/34 §4.2, regra 1).");
                }

                if (QuantidadeFixaDeDigitos is not null)
                {
                    // Senão DefinirQuantidadeDigitosCartao iria junto e a catraca receberia os dois.
                    problemas.Add("Com dígitos variáveis, QuantidadeFixaDeDigitos fica vazia.");
                }

                // Regra 2: padrão Livre com leitor de QR ⇒ os tamanhos cobrem todo QR que o
                // produto aceita (perfil qr-catraca4, 4 a 16; docs/20 §5). O leitor de QR é o 8
                // (QR por letras, FUN:14) ou o 5 (barras serial, pela página da Topdata; T25).
                if (PadraoCartao == 1
                    && TipoDeLeitor is 5 or 8
                    && PerfisDeLeitura.QrCatraca4.AllowedLengths is { } aceitos)
                {
                    var faltando = aceitos.Where(n => !QuantidadesVariaveisDeDigitos.Contains((byte)n)).Order().ToList();
                    if (faltando.Count > 0)
                    {
                        problemas.Add(
                            "Com padrão Livre e leitor de QR, os tamanhos variáveis precisam cobrir todo QR aceito " +
                            $"(4 a 16, docs/34 §4.2, regra 2); faltam {string.Join(", ", faltando)}.");
                    }
                }

                return;

            default:
                problemas.Add($"ModoDeDigitos deve ser Fixo ou Variavel; recebido {(int)ModoDeDigitos}.");
                return;
        }
    }

    /// <summary>FUN:34 e regras 7 e 8 do docs/34 §4.2, na forma que valem enquanto a lista não existe.</summary>
    private void ValidarLista(List<string> problemas)
    {
        if (TipoDeLista > 2)
        {
            problemas.Add($"TipoDeLista deve ser 0, 1 ou 2 (FUN:34); recebido {TipoDeLista}.");
            return;
        }

        // Regra 7: tipo de lista ≠ 0 ⇒ a lista cabe (em posições). Não há lista gravada na
        // catraca (Etapa D, decisão D3), então não há o que caber: só o 0 é possível.
        if (TipoDeLista != 0)
        {
            problemas.Add(
                "TipoDeLista diferente de 0 exige a lista gravada na catraca, que ainda não existe " +
                "(docs/34 §4.2, regra 7; Etapa D, decisão D3).");
        }

        // Regra 8: lista negra ⇒ decisão D5 (fail-safe × fail-secure) registrada. Não há.
        if (TipoDeLista == 2)
        {
            problemas.Add("Lista negra exige a decisão D5 registrada (docs/34 §4.2, regra 8; §9).");
        }
    }

    /// <summary>Regra 6 do docs/34 §4.2: exibir data ⇒ mensagem de até 16 caracteres.</summary>
    private static void ValidarMensagem(List<string> problemas, string campo, MensagemDoDisplay? mensagem)
    {
        if (mensagem is null)
        {
            return;
        }

        if (mensagem.Texto is null)
        {
            problemas.Add($"{campo}: o texto é obrigatório.");
            return;
        }

        var limite = mensagem.ExibirData ? MensagemDoDisplay.LimiteComData : MensagemDoDisplay.LimiteSemData;
        if (mensagem.Texto.Length > limite)
        {
            problemas.Add(
                $"{campo} tem no máximo {limite} caracteres{(mensagem.ExibirData ? " com a data (docs/34 §4.2, regra 6)" : string.Empty)}; " +
                $"recebida com {mensagem.Texto.Length}.");
        }
    }
}
