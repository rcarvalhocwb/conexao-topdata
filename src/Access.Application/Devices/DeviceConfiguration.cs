namespace Access.Application.Devices;

/// <summary>Perfil físico do portão, resultado do comissionamento.</summary>
/// <param name="SentidoInvertido">
/// Verdadeiro quando a instalação exige as variantes invertidas de liberação.
/// </param>
public sealed record GatePhysicalProfile(bool SentidoInvertido);

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
    public IReadOnlyList<byte> QuantidadesVariaveisDeDigitos { get; init; } = [];

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
