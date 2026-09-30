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

    /// <summary>Função do relé 1: 0 a 5.</summary>
    public required byte FuncaoDoAcionamento1 { get; init; }

    /// <summary>Tempo do relé 1, de 0 a 50 segundos.</summary>
    public required byte TempoDoAcionamento1 { get; init; }

    /// <summary>Função do relé 2 (urna).</summary>
    public required byte FuncaoDoAcionamento2 { get; init; }

    /// <summary>Tempo do relé 2, de 0 a 50 segundos.</summary>
    public required byte TempoDoAcionamento2 { get; init; }

    /// <summary>Verdadeiro para modo on-line; falso para off-line.</summary>
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
    public required GatePhysicalProfile PerfilFisico { get; init; }

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

        return problemas;
    }
}
