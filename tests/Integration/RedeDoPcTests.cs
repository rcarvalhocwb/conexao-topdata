using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Docs/62: o operador conecta uma catraca sem ferramenta externa. O PC mostra o próprio IP e a porta,
/// e de onde já chegou conexão nelas. Só o que o Windows lista; nenhuma varredura da rede.
/// </summary>
public sealed class RedeDoPcTests
{
    [Fact]
    public void Os_enderecos_sao_so_ipv4_uteis_sem_loopback_nem_automatico_e_privados_primeiro()
    {
        var enderecos = new[]
        {
            IPAddress.Parse("127.0.0.1"),
            IPAddress.Parse("169.254.10.1"),
            IPAddress.Parse("::1"),
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse("192.168.0.5"),
            IPAddress.Parse("192.168.0.5"),
            IPAddress.Parse("10.0.0.2"),
        };

        var lista = RedeDoPc.FiltrarIpv4(enderecos);

        Assert.Equal(["10.0.0.2", "192.168.0.5", "8.8.8.8"], lista);
    }

    [Fact]
    public void Sem_endereco_util_a_lista_fica_vazia()
    {
        Assert.Empty(RedeDoPc.FiltrarIpv4([IPAddress.Loopback, IPAddress.Parse("169.254.1.1")]));
    }

    [Fact]
    public void Conta_so_conexao_estabelecida_na_porta_da_catraca_e_cada_ip_uma_vez()
    {
        var conexoes = new[]
        {
            new ConexaoTcp(3570, IPAddress.Parse("10.0.0.9"), TcpState.Established),
            new ConexaoTcp(3570, IPAddress.Parse("10.0.0.9"), TcpState.Established),
            new ConexaoTcp(3570, IPAddress.Parse("10.0.0.7"), TcpState.TimeWait),
            new ConexaoTcp(9999, IPAddress.Parse("10.0.0.8"), TcpState.Established),
            new ConexaoTcp(3570, IPAddress.IPv6Loopback, TcpState.Established),
        };

        Assert.Equal(["10.0.0.9"], RedeDoPc.IpsConectados([3570], conexoes));
    }

    [Fact]
    public void Porta_ocupada_por_outro_programa_nao_esta_livre()
    {
        using var ocupada = new TcpListener(IPAddress.Any, 0);
        ocupada.Start();
        var porta = ((IPEndPoint)ocupada.LocalEndpoint).Port;

        Assert.False(RedeDoPc.PortaLivre(porta));
    }

    [Fact]
    public void Porta_sem_uso_esta_livre()
    {
        var sonda = new TcpListener(IPAddress.Loopback, 0);
        sonda.Start();
        var porta = ((IPEndPoint)sonda.LocalEndpoint).Port;
        sonda.Stop();

        Assert.True(RedeDoPc.PortaLivre(porta));
    }

    /// <summary>Uma conexão real na porta: o que o Windows lista é o que o operador vê.</summary>
    [Fact]
    public async Task A_conexao_que_chega_na_porta_aparece_com_o_ip_de_origem()
    {
        var escuta = new TcpListener(IPAddress.Loopback, 0);
        escuta.Start();
        var porta = ((IPEndPoint)escuta.LocalEndpoint).Port;

        using var cliente = new TcpClient();
        await cliente.ConnectAsync(IPAddress.Loopback, porta);
        using var aceito = await escuta.AcceptTcpClientAsync();

        var ips = RedeDoPc.IpsConectadosAgora([porta]);

        Assert.Contains("127.0.0.1", ips);
        escuta.Stop();
    }
}
