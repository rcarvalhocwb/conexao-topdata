namespace Access.Domain.Devices;

/// <summary>
/// Origens de evento documentadas pela Topdata para <c>ReceberDadosOnLine</c>.
/// </summary>
/// <remarks>
/// Fonte: Manual de Integração SDK Inner Acesso, seção 4.3.2 (tabela oficial).
/// Os valores 11, 14, 15, 16, 17 e 19 <b>não constam</b> da tabela oficial e por isso
/// não aparecem aqui — eles chegam com <see cref="EventOrigin.IsKnown"/> falso.
/// A tabela versionada está em <c>docs/compatibility-matrix/origens-evento.csv</c> e é
/// conferida contra este enum pelo teste de contrato.
/// </remarks>
public enum KnownEventOrigin
{
    /// <summary>ORIGEM_TECLADO — dados digitados no teclado.</summary>
    Teclado = 1,

    /// <summary>ORIGEM_LEITOR1 — leitura pelo leitor 1.</summary>
    Leitor1 = 2,

    /// <summary>ORIGEM_LEITOR2 — leitura pelo leitor 2 (fenda da urna).</summary>
    Leitor2 = 3,

    /// <summary>
    /// ORIGEM_SENSOR_CATRACA — sensor de giro legado.
    /// Obsoleto: nunca usar para confirmar passagem física.
    /// </summary>
    SensorCatracaLegado = 4,

    /// <summary>ORIGEM_FIM_TEMPO_ACIONAMENTO — o tempo de um acionamento expirou.</summary>
    FimTempoAcionamento = 5,

    /// <summary>
    /// ORIGEM_GIRO_CATRACA_TOPDATA — giro concluído, pelo sensor óptico.
    /// Única origem que comprova passagem física.
    /// </summary>
    GiroConfirmado = 6,

    /// <summary>ORIGEM_CARTAO_RECOLHIDO_URNA — cartão recolhido pela urna.</summary>
    CartaoRecolhidoUrna = 7,

    /// <summary>ORIGEM_EVENTO_SENSORI — evento no sensor externo 1.</summary>
    SensorExterno1 = 8,

    /// <summary>ORIGEM_EVENTO_SENSOR2 — evento no sensor externo 2.</summary>
    SensorExterno2 = 9,

    /// <summary>ORIGEM_EVENTO_SENSOR3 — evento no sensor externo 3.</summary>
    SensorExterno3 = 10,

    /// <summary>ORIGEM_SENSOR_BIOMETRICO — leitura no sensor biométrico.</summary>
    SensorBiometrico = 12,

    /// <summary>ORIGEM_RESPOSTA_REQUISICAO_BIO — resposta interna do módulo biométrico.</summary>
    RespostaRequisicaoBiometrica = 13,

    /// <summary>ORIGEM_TEMPLATE — envio ou recebimento de template biométrico.</summary>
    TemplateBiometrico = 18,

    /// <summary>ORIGEM_URNA_CHEIA — a urna coletora está cheia.</summary>
    UrnaCheia = 20,

    /// <summary>ORIGEM_QRCODE — leitura de QR Code.</summary>
    QrCode = 21,
}

/// <summary>
/// Origem de um evento recebido do equipamento, conhecida ou não.
/// </summary>
/// <remarks>
/// Um valor desconhecido <b>não é inválido</b>: é desconhecido. Ele é preservado
/// íntegro, contado em métrica própria e exibido ao operador — nunca descartado.
/// Ver docs/ADR/ADR-0018-eventos-desconhecidos.md
/// </remarks>
public readonly record struct EventOrigin
{
    private EventOrigin(int raw, KnownEventOrigin? known)
    {
        Raw = raw;
        Known = known;
    }

    /// <summary>Valor bruto recebido do equipamento, sempre preservado.</summary>
    public int Raw { get; }

    /// <summary>A origem documentada, ou <c>null</c> se este valor não é conhecido.</summary>
    public KnownEventOrigin? Known { get; }

    /// <summary>Verdadeiro quando a origem consta da tabela oficial.</summary>
    public bool IsKnown => Known is not null;

    /// <summary>
    /// Interpreta um valor bruto vindo do equipamento.
    /// Nunca lança: um valor fora da tabela vira uma origem desconhecida.
    /// </summary>
    public static EventOrigin FromRaw(int raw) =>
        Enum.IsDefined(typeof(KnownEventOrigin), raw)
            ? new EventOrigin(raw, (KnownEventOrigin)raw)
            : new EventOrigin(raw, known: null);

    /// <summary>Constrói a partir de uma origem documentada.</summary>
    public static EventOrigin From(KnownEventOrigin known) => new((int)known, known);

    /// <summary>
    /// Verdadeiro apenas para a origem 6, a única que comprova passagem física.
    /// </summary>
    /// <remarks>
    /// A origem 4 (sensor legado) <b>não</b> conta, por decisão explícita:
    /// o manual a declara obsoleta. Ver docs/ADR/ADR-0007.
    /// </remarks>
    public bool ConfirmaPassagemFisica => Known is KnownEventOrigin.GiroConfirmado;

    public override string ToString() =>
        Known is { } k ? $"{k} ({Raw})" : $"Desconhecida ({Raw})";
}
