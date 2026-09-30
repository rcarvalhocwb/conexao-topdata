using System.Security.Cryptography;
using Access.Domain.Credentials;

namespace Edge.Supervisor;

/// <summary>
/// A chave do HMAC da trilha do cadastro (<see cref="ImpressaoDeCodigo"/>), guardada no
/// cofre do serviço.
/// </summary>
/// <remarks>
/// <para>
/// Segue o precedente do segredo da nuvem (<see cref="CofreDpapi"/>): no Windows, DPAPI no
/// escopo da máquina, num arquivo que só SYSTEM e Administradores leem (ADR-0014). A chave
/// nunca está no repositório (é gerada na máquina, com gerador criptográfico, no primeiro
/// uso), nunca está na base (a base guarda só o identificador derivado dela) e nunca vai
/// para log.
/// </para>
/// <para>
/// Perder o cofre é perder a ligação entre um código e o seu histórico — o histórico
/// continua lá, mascarado, e a cadeia de hash continua verificável. É o mesmo efeito do
/// expurgo por destruição de chave (docs/34-anexos/03 §5.4); por isso uma chave ilegível
/// <b>não</b> é trocada em silêncio por outra: o serviço para e avisa.
/// </para>
/// <para>
/// Uma chave por instalação, por enquanto. Chave por evento, rotação e expurgo dependem dos
/// prazos do jurídico (D7) e são da Etapa B.9; a coluna <c>code_key_id</c> já permite a troca
/// sem reescrever a trilha.
/// </para>
/// </remarks>
public static class ChaveDaImpressao
{
    /// <summary>Nome do segredo no cofre.</summary>
    public const string NomeNoCofre = "impressao-de-codigo";

    /// <summary>A impressão com a chave do cofre; cria e guarda a chave no primeiro uso.</summary>
    /// <exception cref="InvalidOperationException">O cofre tem uma chave ilegível ou curta demais.</exception>
    public static ImpressaoDeCodigo Obter(ICofreDeSegredos cofre)
    {
        ArgumentNullException.ThrowIfNull(cofre);

        if (cofre.Ler(NomeNoCofre) is { Length: > 0 } guardada)
        {
            return new ImpressaoDeCodigo(Decodificar(guardada));
        }

        var nova = RandomNumberGenerator.GetBytes(ImpressaoDeCodigo.TamanhoMinimoDaChave);
        cofre.Gravar(NomeNoCofre, Convert.ToBase64String(nova));
        return new ImpressaoDeCodigo(nova);
    }

    private static byte[] Decodificar(string guardada)
    {
        var chave = new byte[guardada.Length];
        if (!Convert.TryFromBase64String(guardada, chave, out var tamanho)
            || tamanho < ImpressaoDeCodigo.TamanhoMinimoDaChave)
        {
            throw new InvalidOperationException(
                $"A chave '{NomeNoCofre}' do cofre está ilegível. Ela não é recriada sozinha: uma chave nova " +
                "desligaria o histórico do cadastro de todos os códigos. Restaure o cofre desta máquina.");
        }

        return chave[..tamanho];
    }
}
