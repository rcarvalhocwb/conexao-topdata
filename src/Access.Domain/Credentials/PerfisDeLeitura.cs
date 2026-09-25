namespace Access.Domain.Credentials;

/// <summary>
/// Perfis de normalização que refletem o que a catraca <b>de fato</b> entrega.
/// </summary>
/// <remarks>
/// <para>
/// Cada número aqui vem da documentação da Topdata para a linha de catracas 4 (TopFit 4),
/// lida em trechos de busca do portal de suporte — o domínio estava bloqueado para leitura
/// direta. Todos precisam ser confirmados na bancada, com o equipamento real.
/// Ver docs/20-leitores-qr-e-cartao-mifare.md
/// </para>
/// <para>
/// A razão de existirem: um código que a catraca não consegue ler precisa ser recusado
/// <b>quando entra no sistema</b>, dias antes do evento, e não descoberto na porta, com a
/// pessoa e a fila na frente.
/// </para>
/// </remarks>
public static class PerfisDeLeitura
{
    /// <summary>
    /// QR Code lido pelo leitor da tampa da Catraca 4: de 4 a 16 caracteres.
    /// </summary>
    /// <remarks>
    /// A Topdata documenta a leitura de QR "de 4 até 16 dígitos", com número de dígitos
    /// variável habilitado. Não passa para maiúsculas e não completa zeros: o que o
    /// provedor gerou é o que a catraca lê. Se letras passam depende do tipo de leitor
    /// configurado — <c>A_CONFIRMAR</c> na bancada (ensaio <c>HIL-CARD-03</c>).
    /// </remarks>
    public static CredentialNormalization QrCatraca4 { get; } = new(
        "qr-catraca4",
        allowedLengths: Faixa(4, 16));

    /// <summary>
    /// Cartão Mifare lido pela Catraca 4: sempre 10 dígitos.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Topdata documenta que, na linha 4, o leitor Mifare se configura sozinho em
    /// "ABA Track, 10 dígitos". Completa zeros à esquerda até 10 — nunca remove — porque
    /// um leitor de mesa que entregue <c>78234567</c> precisa casar com a catraca que
    /// entrega <c>0078234567</c>.
    /// </para>
    /// <para>
    /// Dez dígitos decimais é exatamente o que cabe num identificador de 4 bytes
    /// (o maior é 4.294.967.295). Um cartão de <b>7 bytes</b> não cabe — daí a
    /// recomendação de comprar Mifare com identificador de 4 bytes. Como a catraca trata
    /// um cartão de 7 bytes é <c>A_CONFIRMAR_COM_TOPDATA</c>.
    /// </para>
    /// </remarks>
    public static CredentialNormalization MifareCatraca4 { get; } = new(
        "mifare-catraca4",
        padLeftTo: 10,
        allowedLengths: new HashSet<int> { 10 });

    private static HashSet<int> Faixa(int de, int ate) => [.. Enumerable.Range(de, ate - de + 1)];
}
