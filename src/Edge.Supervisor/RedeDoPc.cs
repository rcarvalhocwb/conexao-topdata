using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Edge.Supervisor;

/// <summary>Uma conexão TCP aberta neste PC, como o Windows a lista.</summary>
/// <param name="PortaLocal">Porta deste PC em que a conexão chegou.</param>
/// <param name="Remoto">Endereço do outro lado: a catraca, quando é uma catraca.</param>
/// <param name="Estado">Estado da conexão.</param>
public sealed record ConexaoTcp(int PortaLocal, IPAddress Remoto, TcpState Estado);

/// <summary>
/// Os endereços deste PC e as conexões que chegam nele, para o operador conectar uma catraca sem
/// ferramenta externa.
/// </summary>
/// <remarks>
/// <para>
/// A catraca é o cliente TCP: ela abre a conexão para o IP e a porta configurados nela (docs/61).
/// Para conectar, o operador precisa de duas coisas deste PC: o IP e a porta, para digitar na
/// catraca. E, depois, a confirmação de que a conexão chegou, com o IP de onde ela veio.
/// </para>
/// <para>
/// Não há varredura da rede: só o que o Windows já mostra sobre este PC.
/// </para>
/// </remarks>
public static class RedeDoPc
{
    /// <summary>IPv4 deste PC, sem loopback nem endereço automático (169.254), privados primeiro.</summary>
    public static IReadOnlyList<string> FiltrarIpv4(IEnumerable<IPAddress> enderecos)
    {
        ArgumentNullException.ThrowIfNull(enderecos);

        return [.. enderecos
            .Where(e => e.AddressFamily == AddressFamily.InterNetwork)
            .Where(e => !IPAddress.IsLoopback(e))
            .Where(e => !e.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(e => e.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(e => EhPrivado(e) ? 0 : 1)
            .ThenBy(e => e, StringComparer.Ordinal)];
    }

    /// <summary>IPs de origem das conexões estabelecidas nas portas informadas, sem repetir.</summary>
    public static IReadOnlyList<string> IpsConectados(IEnumerable<int> portas, IEnumerable<ConexaoTcp> conexoes)
    {
        ArgumentNullException.ThrowIfNull(portas);
        ArgumentNullException.ThrowIfNull(conexoes);

        var conjuntoDePortas = portas.ToHashSet();
        return [.. conexoes
            .Where(c => c.Estado == TcpState.Established)
            .Where(c => conjuntoDePortas.Contains(c.PortaLocal))
            .Where(c => c.Remoto.AddressFamily == AddressFamily.InterNetwork)
            .Select(c => c.Remoto.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Se a porta pode ser aberta agora. Falso quando outro programa já a usa.</summary>
    public static bool PortaLivre(int porta)
    {
        try
        {
            var escuta = new TcpListener(IPAddress.Any, porta);
            escuta.Start();
            escuta.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>IPv4 deste PC agora, nas interfaces ativas.</summary>
    public static IReadOnlyList<string> EnderecosAgora()
    {
        try
        {
            return FiltrarIpv4(NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address));
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>IPs que têm conexão estabelecida com as portas informadas, agora.</summary>
    public static IReadOnlyList<string> IpsConectadosAgora(IEnumerable<int> portas)
    {
        ArgumentNullException.ThrowIfNull(portas);

        try
        {
            var conexoes = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Select(c => new ConexaoTcp(c.LocalEndPoint.Port, c.RemoteEndPoint.Address, c.State));
            return IpsConectados(portas, conexoes);
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    private static bool EhPrivado(string ip) =>
        ip.StartsWith("10.", StringComparison.Ordinal)
        || ip.StartsWith("192.168.", StringComparison.Ordinal)
        || (ip.StartsWith("172.", StringComparison.Ordinal) && SegundoOctetoEntre16e31(ip));

    private static bool SegundoOctetoEntre16e31(string ip)
    {
        var partes = ip.Split('.');
        return partes.Length == 4 && int.TryParse(partes[1], out var segundo) && segundo is >= 16 and <= 31;
    }
}
