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

    /// <summary>
    /// Os perfis que podem ser escolhidos pelo nome gravado no provedor
    /// (<c>ticket_provider.normalization_profile</c>) ou na configuração da nuvem.
    /// </summary>
    /// <remarks>Declarado depois dos perfis de propósito: a ordem dos estáticos importa.</remarks>
    public static IReadOnlyDictionary<string, CredentialNormalization> Todos { get; } =
        new Dictionary<string, CredentialNormalization>(StringComparer.Ordinal)
        {
            [CredentialNormalization.Raw.Name] = CredentialNormalization.Raw,
            [MifareCatraca4.Name] = MifareCatraca4,
            [QrCatraca4.Name] = QrCatraca4,
        };

    /// <summary>
    /// Perfil aplicado ao que a catraca lê, antes de procurar o código na base.
    /// </summary>
    /// <remarks>
    /// <para>
    /// É <c>raw</c> (só tira espaços nas pontas), que é exatamente o que a decisão sempre
    /// fez. A decisão não sabe de que provedor é o código antes de achá-lo, então não dá
    /// para aplicar "o perfil do provedor" na leitura; o que dá é aplicar o perfil
    /// <b>do leitor</b>, e ele precisa ser o mesmo que o provedor usou no cadastro.
    /// </para>
    /// <para>
    /// O perfil por leitor e por catraca virá da parametrização por catraca (docs/35,
    /// Etapa A) e depende do que a bancada medir: quantos dígitos cada leitor entrega
    /// (docs/21, passo 3, linhas 8 a 12). Até lá, trocar este perfil sozinho quebraria o
    /// casamento dos QR de site, cadastrados com outro perfil — por isso ele é fixo.
    /// </para>
    /// <para>
    /// Para o cartão de provedor <c>mifare-catraca4</c> lido com menos de 10 dígitos, a base tenta, só
    /// depois da leitura exata e só nos ingressos desse perfil, o código completado com zeros como o
    /// cadastro fez (achado E4-2 do docs/41; <c>RepositorioDeIngressos.TentarUsar</c>).
    /// </para>
    /// </remarks>
    public static CredentialNormalization DaLeitura => CredentialNormalization.Raw;

    /// <summary>
    /// A normalização única de um código, qualquer que seja a porta de entrada: leitura da
    /// catraca, cadastro, ingestão, sincronização ou consulta do operador.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe para que leitura e cadastro comparem o <b>mesmo texto</b>. Se cada entrada
    /// normalizasse do seu jeito, um perfil que completa zeros casaria no cadastro e não
    /// na leitura — cartão válido recusado na porta (docs/34 §2, defeito F8).
    /// </para>
    /// <para>
    /// Nunca remove zeros à esquerda e nunca converte para número (ADR-0008).
    /// </para>
    /// </remarks>
    /// <param name="codigo">O código como chegou. Nulo ou só espaços não é código.</param>
    /// <param name="perfil">O perfil do provedor (cadastro) ou do leitor (leitura).</param>
    /// <returns>O código normalizado, ou nulo quando não há código.</returns>
    public static string? Normalizar(string? codigo, CredentialNormalization perfil)
    {
        ArgumentNullException.ThrowIfNull(perfil);
        return string.IsNullOrWhiteSpace(codigo) ? null : perfil.Apply(codigo);
    }

    private static HashSet<int> Faixa(int de, int ate) => [.. Enumerable.Range(de, ate - de + 1)];
}
