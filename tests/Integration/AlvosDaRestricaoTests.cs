using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Achado E8-3 do docs/41: a restrição de permissão era aplicada, recursivamente, à pasta do banco.
/// Com <c>"banco": "D:\\acesso.db"</c>, o serviço reescrevia a permissão do disco D inteiro. Agora ela
/// alcança a pasta da instalação e, fora dela, só o que o serviço cria ao lado do banco.
/// </summary>
public sealed class AlvosDaRestricaoTests
{
    private static readonly string Instalacao = Path.Combine(Path.GetTempPath(), "ConexaoTopdata");

    [Fact]
    public void Banco_na_pasta_da_instalacao_restringe_so_a_pasta_da_instalacao()
    {
        var alvos = SegurancaLocal.AlvosDaRestricao(Instalacao, Path.Combine(Instalacao, "acesso.db"));

        var unico = Assert.Single(alvos);
        Assert.Equal(Path.GetFullPath(Instalacao), unico.Caminho);
        Assert.True(unico.Pasta);
    }

    [Fact]
    public void Banco_em_subpasta_da_instalacao_tambem_fica_coberto_pela_pasta_da_instalacao()
    {
        var alvos = SegurancaLocal.AlvosDaRestricao(Instalacao, Path.Combine(Instalacao, "base", "acesso.db"));

        Assert.Single(alvos);
    }

    [Fact]
    public void Banco_fora_da_instalacao_nunca_restringe_a_pasta_do_banco()
    {
        var raizDeOutroDisco = Path.Combine(Path.GetTempPath(), "outro-disco");
        var banco = Path.Combine(raizDeOutroDisco, "acesso.db");

        var alvos = SegurancaLocal.AlvosDaRestricao(Instalacao, banco);

        Assert.DoesNotContain(alvos, a => a.Caminho == Path.GetFullPath(raizDeOutroDisco));
        Assert.Equal(Path.GetFullPath(Instalacao), alvos[0].Caminho);

        // Só o que o serviço cria: os arquivos do banco e da telemetria, e as pastas dele.
        var pastas = alvos.Skip(1).Where(a => a.Pasta).Select(a => Path.GetFileName(a.Caminho)).Order();
        Assert.Equal(["copias", "registros"], pastas);
        Assert.Contains(alvos, a => !a.Pasta && a.Caminho == Path.GetFullPath(banco));
        Assert.Contains(alvos, a => !a.Pasta && a.Caminho == Path.GetFullPath(banco) + "-wal");
        Assert.All(alvos.Skip(1), a => Assert.True(SegurancaLocal.EstaDentro(a.Caminho, raizDeOutroDisco)));
    }

    [Fact]
    public void Pasta_com_nome_que_comeca_igual_nao_conta_como_dentro()
    {
        // "ConexaoTopdata-velha" não é subpasta de "ConexaoTopdata".
        Assert.False(SegurancaLocal.EstaDentro(Instalacao + "-velha", Instalacao));
        Assert.True(SegurancaLocal.EstaDentro(Instalacao, Instalacao));
        Assert.True(SegurancaLocal.EstaDentro(Path.Combine(Instalacao, "copias"), Instalacao));
    }
}
