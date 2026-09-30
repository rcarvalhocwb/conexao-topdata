namespace Access.Application.Devices;

/// <summary>
/// Padrões de fábrica: a configuração completa de um modelo, antes do evento e da catraca.
/// </summary>
/// <remarks>
/// <para>
/// Era <c>ConfiguracaoDeBancada.TopFit4</c>, no worker. Veio para cá na Etapa A.1 do docs/35
/// para ser a primeira camada do <see cref="MontadorDaConfiguracao"/>, com os mesmos valores.
/// Cada valor tem fonte, e o que não tem está desligado em vez de adivinhado:
/// </para>
/// <list type="bullet">
/// <item>Cartão <b>padrão livre</b> e <b>dígitos variáveis de 4 a 16</b> — passo a passo da
/// Topdata para cadastrar a Catraca 4 com QR. Os tamanhos só chegam à catraca com a chave
/// <c>catraca.enviar_digitos_variaveis</c> (F2, docs/34 §2).</item>
/// <item>Sem quantidade fixa de dígitos: <c>DefinirQuantidadeDigitosCartao</c> não é chamada
/// e a catraca fica com o padrão da DLL para ela (ADR-0020; modelo completo na Etapa A.2).</item>
/// <item>Leitor 1 = <b>1, "somente entrada"</b>; leitor 2 (fenda da urna) = <b>1</b> — FUN:15/16.</item>
/// <item>Acionamento 1 = <b>2, "registro entrada"</b>, 5 s — matriz de funções, SDK 6.0.2.0.</item>
/// <item>Tipo de leitor <b>8</b> por padrão (QR por letras). A Topdata fala em "serial barcode",
/// que no SDK pode ser o <b>5</b> — o ensaio decide (NOVO-HIL-QR-02, T25). <c>A_CONFIRMAR</c>.</item>
/// <item><b>Relé 2 (urna) desligado.</b> A função que faz a urna recolher o cartão não está
/// documentada. Na bancada, o leitor da urna lê e o sistema decide; o recolhimento é a Etapa
/// A.11, bloqueada por bancada.</item>
/// <item>Mudança automática on-line/off-line <b>desligada</b>: a catraca só funciona com o
/// sistema rodando, para que tudo que acontece passe por ele (sequência oficial: Etapa A.7).</item>
/// <item>Liberação por <c>LiberarCatracaEntrada</c>, como sempre foi. A catraca instalada à
/// esquerda escolhe a variante no comissionamento, depois de HIL-DIR-05/06 (F1, docs/34 §2).</item>
/// <item>Campos da Etapa A.2 (docs/34 §4.1) no valor inicial do modelo: data e hora no evento
/// ligada, tipo de lista 0, Wiegand (0, 0), formas de entrada de sempre — cada um só chega à
/// catraca com a sua chave técnica, todas desligadas. Registro de acesso negado, cartão master,
/// WebServer e mensagens de apresentação e off-line ficam vazios. O tipo de leitor continua 8
/// até NOVO-HIL-QR-02 (T25).</item>
/// </list>
/// </remarks>
public static class PadroesDeFabrica
{
    /// <summary>A TopFit 4 (Catraca 4, linha Inner) num evento com ingresso em QR.</summary>
    /// <remarks>
    /// Uma instância nova a cada leitura: quem compõe com <c>with</c> não divide lista com
    /// ninguém.
    /// </remarks>
    public static DeviceConfiguration TopFit4 => new()
    {
        PadraoCartao = 1,
        QuantidadesVariaveisDeDigitos = [.. Enumerable.Range(4, 13).Select(n => (byte)n)],
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = MontadorDaConfiguracao.LeitorSomenteEntrada,
        OperacaoDoLeitor2 = MontadorDaConfiguracao.LeitorSomenteEntrada,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 0,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Aproxime o ingresso",
        PerfilFisico = new GatePhysicalProfile(FuncaoDeLiberacao.Entrada),
    };
}

/// <summary>
/// O que a configuração do evento sobrepõe ao padrão de fábrica. Nulo = herda o padrão.
/// </summary>
/// <remarks>
/// <para>
/// Existe em <c>Access.Application</c> porque a configuração do evento guardada na base
/// (<c>ConfiguracaoDaOperacao</c>, em <c>Access.Infrastructure.SQLite</c>) não pode ser vista
/// daqui nem do <c>Edge.Worker</c>: quem lê a base traduz para este tipo
/// (<c>ConfiguracaoDaOperacao.ParaACatraca</c>). São só os campos que chegam à catraca; a
/// nuvem, a espera pelo giro e as chaves do relógio e da reconexão seguem por outro caminho.
/// </para>
/// <para>
/// Os campos são exatamente os que o evento já mudava antes da Etapa A.1: tipo de leitor,
/// leitor 2 pela urna, tempo do relé 1 e mensagem (docs/34-anexos/02-arquiteto.md, "vêm do
/// operador"), mais a chave de dígitos variáveis da Etapa 0.4. A Etapa A.2 acrescentou só
/// as chaves técnicas dos campos novos (docs/34 §4.1): nenhum valor novo vem do operador.
/// </para>
/// </remarks>
public sealed record SobreposicoesDoEvento
{
    /// <summary>Nenhuma sobreposição: vale o padrão de fábrica inteiro.</summary>
    public static SobreposicoesDoEvento Nenhuma { get; } = new();

    /// <summary>Tipo de leitor (0 a 8). <c>leitor.tipo</c>.</summary>
    public byte? TipoDeLeitor { get; init; }

    /// <summary>
    /// Leitor 2, da fenda da urna: ligado vira "somente entrada" (1), desligado vira 0.
    /// <c>leitor.urna</c>.
    /// </summary>
    public bool? LeitorDaUrna { get; init; }

    /// <summary>Tempo do relé 1, em segundos. <c>catraca.acionamento_segundos</c>.</summary>
    public byte? TempoDeAcionamento { get; init; }

    /// <summary>Mensagem do display em repouso. <c>catraca.mensagem</c>.</summary>
    public string? MensagemPadrao { get; init; }

    /// <summary>
    /// Chave técnica <c>catraca.enviar_digitos_variaveis</c>, desligada até HIL-CARD-02
    /// (F2, docs/34 §2).
    /// </summary>
    public bool? EnviarDigitosVariaveis { get; init; }

    // Etapa A.2: o docs/34 não põe nenhum campo novo nas mãos do operador (anexo 02: só tipo
    // de leitor, urna, tempo e mensagem "vêm do operador"). O evento só liga ou desliga, por
    // chave técnica sem tela, o envio dos valores do padrão de fábrica — todas desligadas.

    /// <summary>
    /// Chave técnica <c>catraca.enviar_data_hora_no_evento</c> (EI-027), desligada até INT-CFG-07.
    /// </summary>
    public bool? EnviarDataHoraNoEventoOnLine { get; init; }

    /// <summary>
    /// Chave técnica <c>catraca.registrar_acesso_negado</c> (EI-021): o próprio valor, 0 a 3.
    /// Nulo = não enviado. Vazia até INT-OFF-08.
    /// </summary>
    public byte? RegistrarAcessoNegado { get; init; }

    /// <summary>Chave técnica <c>catraca.enviar_tipo_de_lista</c> (EI-033), desligada até INT-OFF-02.</summary>
    public bool? EnviarTipoDeLista { get; init; }

    /// <summary>
    /// Chave técnica <c>catraca.enviar_wiegand_dois_leitores</c> (EI-024), desligada até HIL-CARD-05.
    /// </summary>
    public bool? EnviarWiegandDoisLeitores { get; init; }

    /// <summary>
    /// Chave técnica <c>catraca.enviar_formas_de_entrada</c> (EI-032), desligada até INT-SM-032 (T26).
    /// </summary>
    public bool? EnviarFormasDeEntradaOnLine { get; init; }
}

/// <summary>
/// O que uma catraca sobrepõe ao evento. <b>Vazio de propósito</b> até a Etapa A.3.
/// </summary>
/// <remarks>
/// <para>
/// A camada existe desde a Etapa A.1 para que o montador já tenha a forma final
/// (fábrica → evento → catraca, docs/34 §4.1: "herda do evento o que não for definido
/// nela") e o worker já passe por ela. Os campos chegam com a tabela por <c>inner_number</c>
/// da Etapa A.3 (colunas anuláveis = herda do evento); inventá-los antes seria prometer uma
/// parametrização por catraca que ainda não é gravada em lugar nenhum.
/// </para>
/// </remarks>
public sealed record SobreposicoesDaCatraca
{
    /// <summary>Nenhuma sobreposição: a catraca herda tudo do evento.</summary>
    public static SobreposicoesDaCatraca Nenhuma { get; } = new();
}

/// <summary>
/// Monta a <see cref="DeviceConfiguration"/> de uma catraca a partir de três camadas:
/// padrão de fábrica, evento e catraca, nesta ordem.
/// </summary>
/// <remarks>
/// <para>
/// É a <b>única</b> fonte da verdade do que vai para a catraca (Etapa A.1 do docs/35). Antes
/// havia duas: <c>ConfiguracaoDeBancada.TopFit4</c> no worker e <c>ConfiguracaoDasCatracas</c>
/// no hospedeiro x86, cada uma com uma parte da regra.
/// </para>
/// <para>
/// Função pura — sem E/S, sem relógio, sem estado — para que o resultado seja o mesmo no
/// worker, no "Aplicar agora" e no teste. A saída é sempre uma configuração completa: a
/// camada de baixo é completa e as de cima só trocam valores (ADR-0020: não existe
/// configuração parcial, porque o que falta vai com o padrão da DLL).
/// </para>
/// <para>
/// Não valida: quem chama decide o que fazer com <see cref="DeviceConfiguration.Validar"/>
/// (o adapter recusa antes de qualquer chamada nativa; o worker cai no padrão e avisa).
/// </para>
/// </remarks>
public static class MontadorDaConfiguracao
{
    /// <summary><c>ConfigurarLeitor1/2</c>: 0 = desabilitado (FUN:15/16).</summary>
    public const byte LeitorDesabilitado = 0;

    /// <summary><c>ConfigurarLeitor1/2</c>: 1 = somente entrada (FUN:15/16).</summary>
    public const byte LeitorSomenteEntrada = 1;

    /// <summary>Aplica as sobreposições do evento e da catraca ao padrão de fábrica.</summary>
    /// <param name="padraoDeFabrica">Configuração completa do modelo (ver <see cref="PadroesDeFabrica"/>).</param>
    /// <param name="evento">O que o evento muda; nulo em cada campo = herda o padrão.</param>
    /// <param name="catraca">O que a catraca muda. Vazio até a Etapa A.3.</param>
    public static DeviceConfiguration Montar(
        DeviceConfiguration padraoDeFabrica,
        SobreposicoesDoEvento evento,
        SobreposicoesDaCatraca catraca)
    {
        ArgumentNullException.ThrowIfNull(padraoDeFabrica);
        ArgumentNullException.ThrowIfNull(evento);
        ArgumentNullException.ThrowIfNull(catraca);

        var doEvento = padraoDeFabrica with
        {
            TipoDeLeitor = evento.TipoDeLeitor ?? padraoDeFabrica.TipoDeLeitor,
            OperacaoDoLeitor2 = evento.LeitorDaUrna switch
            {
                true => LeitorSomenteEntrada,
                false => LeitorDesabilitado,
                null => padraoDeFabrica.OperacaoDoLeitor2,
            },
            TempoDoAcionamento1 = evento.TempoDeAcionamento ?? padraoDeFabrica.TempoDoAcionamento1,
            MensagemPadrao = evento.MensagemPadrao ?? padraoDeFabrica.MensagemPadrao,
            EnviarDigitosVariaveis = evento.EnviarDigitosVariaveis ?? padraoDeFabrica.EnviarDigitosVariaveis,
            EnviarDataHoraNoEventoOnLine = evento.EnviarDataHoraNoEventoOnLine ?? padraoDeFabrica.EnviarDataHoraNoEventoOnLine,
            RegistrarAcessoNegado = evento.RegistrarAcessoNegado ?? padraoDeFabrica.RegistrarAcessoNegado,
            EnviarTipoDeLista = evento.EnviarTipoDeLista ?? padraoDeFabrica.EnviarTipoDeLista,
            EnviarWiegandDoisLeitores = evento.EnviarWiegandDoisLeitores ?? padraoDeFabrica.EnviarWiegandDoisLeitores,
            EnviarFormasDeEntradaOnLine = evento.EnviarFormasDeEntradaOnLine ?? padraoDeFabrica.EnviarFormasDeEntradaOnLine,
        };

        // A camada da catraca ainda não tem campos (Etapa A.3): herda tudo do evento.
        return doEvento;
    }
}
