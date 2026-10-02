namespace Edge.Supervisor.Instalacao;

/// <summary>
/// A regra de entrada do Firewall do Windows para as catracas.
/// </summary>
/// <remarks>
/// <para>
/// A catraca é o cliente TCP; o programa das catracas (<c>Edge.Worker.X86.exe</c>) é quem
/// escuta (docs/03). Sem a regra, o Windows descarta a conexão e a catraca fica em
/// "Conectar".
/// </para>
/// <para>
/// O instalador cria a regra (<c>installer/wix/ConexaoTopdata.wxs</c>, mesmo nome). O
/// assistente só confere e, se alguém a apagou, recria igual: por programa, TCP, só da
/// sub-rede local — nunca aberta para a internet.
/// </para>
/// </remarks>
public static class FirewallDasCatracas
{
    /// <summary>Nome da regra, igual ao do instalador.</summary>
    public const string NomeDaRegra = "Rayzer XAcess — catracas (entrada TCP)";

    /// <summary>Argumentos do <c>netsh</c> que conferem se a regra existe (código 0 = existe).</summary>
    public static string ArgumentosParaConferir => $"advfirewall firewall show rule name=\"{NomeDaRegra}\"";

    /// <summary>Argumentos do <c>netsh</c> que criam a regra para o programa informado.</summary>
    public static string ArgumentosParaCriar(string programa)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programa);

        if (programa.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("Caminho do programa inválido.", nameof(programa));
        }

        return $"advfirewall firewall add rule name=\"{NomeDaRegra}\" dir=in action=allow protocol=TCP " +
               $"program=\"{programa}\" remoteip=localsubnet profile=any enable=yes " +
               "description=\"Deixa as catracas da rede local conectarem ao programa das catracas do Rayzer XAcess.\"";
    }
}
