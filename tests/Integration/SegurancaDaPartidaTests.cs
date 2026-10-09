using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Achados E8-2 e E2-04 do docs/41: o serviço (SYSTEM) só inicia o programa das catracas que está na
/// pasta do programa, toma posse da pasta de dados, e um cofre ilegível não o impede de subir.
/// </summary>
public sealed class SegurancaDaPartidaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "conexao-topdata-testes", Guid.NewGuid().ToString("N"));

    public SegurancaDaPartidaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            // Sobra de arquivo temporário não reprova o teste.
        }
    }

    private static string Executavel(string pasta)
    {
        var caminho = Path.Combine(pasta, "Worker", "Edge.Worker.X86.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, "x");
        return caminho;
    }

    [Fact]
    public void Executavel_fora_da_pasta_do_programa_e_recusado_mesmo_existindo()
    {
        var programa = Path.Combine(_pasta, "Programa");
        var deUmUsuario = Executavel(Path.Combine(_pasta, "Usuario"));
        var configuracao = new ConfiguracaoDoSupervisor(null, [new GrupoConfigurado("Entrada", 3570, [1], deUmUsuario)]);

        var problemas = configuracao.Validar(programa);

        var problema = Assert.Single(problemas);
        Assert.Contains("fora da pasta do programa", problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Executavel_dentro_da_pasta_do_programa_passa()
    {
        var programa = Path.Combine(_pasta, "Programa");
        var configuracao = new ConfiguracaoDoSupervisor(null, [new GrupoConfigurado("Entrada", 3570, [1], Executavel(programa))]);

        Assert.Empty(configuracao.Validar(programa));
    }

    [Fact]
    public void Pasta_com_nome_parecido_nao_conta_como_a_do_programa()
    {
        var programa = Path.Combine(_pasta, "XAcess");
        var vizinha = Executavel(Path.Combine(_pasta, "XAcess-falso"));
        var configuracao = new ConfiguracaoDoSupervisor(null, [new GrupoConfigurado("Entrada", 3570, [1], vizinha)]);

        Assert.Contains(configuracao.Validar(programa), p => p.Contains("fora da pasta do programa", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pasta_do_programa_e_a_que_contem_a_pasta_do_servico()
    {
        // Instalado: Program Files\Rayzer\XAcess\Servico\Edge.Supervisor.exe e ...\XAcess\Worker\Edge.Worker.X86.exe.
        var servico = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        Assert.Equal(Path.GetDirectoryName(servico), ConfiguracaoDoSupervisor.PastaDoPrograma());
        Assert.True(SegurancaLocal.EstaDentro(
            ConfiguracaoDoSupervisor.ResolverExecutavel(Path.Combine("..", "Worker", "Edge.Worker.X86.exe")),
            ConfiguracaoDoSupervisor.PastaDoPrograma()));
    }

    [Fact]
    public void Cofre_ilegivel_vale_como_ausente_e_registra_o_motivo_uma_vez()
    {
        var registros = new List<string>();
        var cofre = new CofreQueNaoDerruba(new CofreQueLanca(), registros.Add);

        Assert.Null(cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo));
        Assert.Null(cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo));

        var linha = Assert.Single(registros);
        Assert.Contains("não pode ser lido", linha, StringComparison.Ordinal);
        Assert.Contains("Grave o segredo de novo", linha, StringComparison.Ordinal);
    }

    [Fact]
    public void Cofre_legivel_passa_o_segredo_adiante()
    {
        var interno = new CofreEmMemoria();
        interno.Gravar(CabecalhoDeSegredo.NomeDoSegredo, "segredo-de-teste-com-mais-de-32-caracteres");
        var registros = new List<string>();

        var cofre = new CofreQueNaoDerruba(interno, registros.Add);

        Assert.Equal("segredo-de-teste-com-mais-de-32-caracteres", cofre.Ler(CabecalhoDeSegredo.NomeDoSegredo));
        Assert.Empty(registros);
    }

    [FactSoNoWindows]
    public void A_restricao_grava_o_dono_pedido_e_a_partida_reconhece_dono_desconhecido()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var arquivo = Path.Combine(_pasta, "workers.json");
        File.WriteAllText(arquivo, "{}");
        using var eu = WindowsIdentity.GetCurrent();
        var usuario = eu.User!;

        // O usuário do teste não é SYSTEM nem o grupo Administradores: no papel de quem preparou a pasta.
        var falhas = SegurancaLocal.RestringirPastaDeDados([new SegurancaLocal.AlvoDaRestricao(_pasta, Pasta: true)], usuario);

        Assert.Empty(falhas);
        Assert.Equal(usuario, new FileInfo(arquivo).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(usuario, new DirectoryInfo(_pasta).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)));

        var suspeitos = SegurancaLocal.ComDonoDesconhecido([_pasta, arquivo, Path.Combine(_pasta, "nao-existe")]);
        Assert.Equal(2, suspeitos.Count);
        Assert.Contains(usuario.Value, suspeitos[0], StringComparison.Ordinal);

        // Fora do serviço (processo comum), a partida não troca dono.
        Assert.Null(SegurancaLocal.DonoQuandoRodaComoSistema());
    }

    private sealed class CofreQueLanca : ICofreDeSegredos
    {
        public string? Ler(string nome) => throw new CryptographicException("A chave não é válida para uso no estado especificado.");

        public void Gravar(string nome, string valor) => throw new NotSupportedException();
    }
}
