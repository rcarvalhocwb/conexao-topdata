using System.Security.Cryptography;
using Access.Infrastructure.SQLite;

namespace Edge.Supervisor;

/// <summary>
/// A chave da cifra dos dados pessoais do cadastro local (<see cref="CifraDeDadosPessoais"/>, ADR-0026),
/// guardada no cofre do serviço.
/// </summary>
/// <remarks>
/// <para>
/// O mesmo cofre de <see cref="ChaveDaImpressao"/>: DPAPI no escopo da máquina, arquivo que só SYSTEM e
/// Administradores leem (ADR-0014). A chave é gerada na máquina no primeiro uso, nunca vai para a base, o
/// log ou o worker (a decisão da catraca não lê dado pessoal).
/// </para>
/// <para>
/// Uma chave ilegível <b>não</b> é trocada em silêncio: os nomes e documentos já gravados ficariam
/// ilegíveis para sempre. O serviço avisa e o cadastro de pessoas fica fora até restaurar o cofre; a
/// catraca continua decidindo, porque a decisão não precisa da chave.
/// </para>
/// </remarks>
public static class ChaveDosDadosPessoais
{
    /// <summary>Nome do segredo no cofre.</summary>
    public const string NomeNoCofre = "dados-pessoais";

    /// <summary>A cifra com a chave do cofre; cria e guarda a chave no primeiro uso.</summary>
    /// <exception cref="InvalidOperationException">O cofre tem uma chave ilegível ou de tamanho errado.</exception>
    public static CifraDeDadosPessoais Obter(ICofreDeSegredos cofre)
    {
        ArgumentNullException.ThrowIfNull(cofre);

        if (cofre.Ler(NomeNoCofre) is { Length: > 0 } guardada)
        {
            var chave = new byte[guardada.Length];
            if (!Convert.TryFromBase64String(guardada, chave, out var tamanho) || tamanho != CifraDeDadosPessoais.TamanhoDaChave)
            {
                throw new InvalidOperationException(
                    $"A chave '{NomeNoCofre}' do cofre está ilegível. Ela não é recriada sozinha: uma chave nova " +
                    "deixaria ilegíveis os dados das pessoas já cadastradas. Restaure o cofre desta máquina.");
            }

            return new CifraDeDadosPessoais(chave.AsSpan(0, tamanho));
        }

        var nova = RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave);
        cofre.Gravar(NomeNoCofre, Convert.ToBase64String(nova));
        return new CifraDeDadosPessoais(nova);
    }
}
