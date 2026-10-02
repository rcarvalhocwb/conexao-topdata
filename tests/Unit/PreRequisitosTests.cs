using Edge.Worker;

namespace Unit.Tests;

public sealed class PreRequisitosTests
{
    [Fact]
    public void Processo_de_64_bits_e_apontado_como_impeditivo()
    {
        var itens = VerificadorDePreRequisitos.Verificar(processoE64Bits: true, ehWindows: true);

        var arquitetura = itens.Single(i => i.Id == "ARQUITETURA_X86");

        Assert.False(arquitetura.Atendido);
        Assert.True(arquitetura.Impeditivo);
        Assert.Contains("PlatformTarget=x86", arquitetura.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Processo_x86_no_windows_nao_tem_impeditivo()
    {
        var itens = VerificadorDePreRequisitos.Verificar(processoE64Bits: false, ehWindows: true);

        Assert.Empty(VerificadorDePreRequisitos.Impeditivos(itens));
    }

    [Fact]
    public void Fora_do_windows_e_impeditivo()
    {
        var itens = VerificadorDePreRequisitos.Verificar(processoE64Bits: false, ehWindows: false);

        Assert.Contains(VerificadorDePreRequisitos.Impeditivos(itens), i => i.Id == "SISTEMA_WINDOWS");
    }

    /// <summary>
    /// As quatro causas de retorno 8 documentadas no manual precisam estar cobertas,
    /// mesmo as que só uma pessoa pode confirmar. O valor está em dizer ao operador
    /// o que conferir, em vez de mostrar "erro 8".
    /// </summary>
    [Fact]
    public void Cobre_as_causas_documentadas_de_retorno_8()
    {
        var ids = VerificadorDePreRequisitos.Verificar().Select(i => i.Id).ToList();

        Assert.Contains("ARQUITETURA_X86", ids);
        Assert.Contains("DOTNET_FRAMEWORK_35", ids);
        Assert.Contains("DLLS_REGISTRADAS", ids);
    }

    [Fact]
    public void Itens_que_so_uma_pessoa_confirma_nao_bloqueiam_o_worker()
    {
        var itens = VerificadorDePreRequisitos.Verificar(processoE64Bits: false, ehWindows: true);

        var manuais = itens.Where(i => i.Atendido is null).ToList();

        Assert.NotEmpty(manuais);
        Assert.All(manuais, i => Assert.False(i.Impeditivo));
    }

    [Fact]
    public void Toda_mensagem_e_acionavel_e_nao_um_codigo()
    {
        var itens = VerificadorDePreRequisitos.Verificar();

        Assert.All(itens, i => Assert.True(i.Mensagem.Length > 30, $"{i.Id} tem mensagem curta demais"));
    }
}
