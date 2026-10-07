using Desktop.ViewModels;

namespace Unit.Tests;

/// <summary>
/// A regra do aviso "Novidades desta versão": aparecer uma vez quando se atualiza por cima de uma
/// instalação, ficar quieto numa instalação nova, e nunca repetir a mesma edição.
/// </summary>
/// <remarks>
/// Escrito junto do recurso (observação do teste de 10/2026): o operador atualizava e não tinha
/// nota do que mudou. A decisão é pura (sem WPF, sem disco) — a gravação entra por um
/// <see cref="Action{T}"/> que o teste observa.
/// </remarks>
public sealed class NovidadesTests
{
    private readonly List<int> _gravacoes = [];

    private NovidadesViewModel Criar(int? edicaoVista, bool haInstalacaoAnterior, string versao = "0.1.120") =>
        new(versao, edicaoVista, haInstalacaoAnterior, _gravacoes.Add);

    [Fact]
    public void Instalacao_nova_nao_mostra_e_deixa_o_registro_em_dia()
    {
        var vm = Criar(edicaoVista: null, haInstalacaoAnterior: false);

        Assert.False(vm.DeveMostrar);
        Assert.False(vm.Aberto);
        // Grava a edição atual sem incomodar, para o próximo aviso com novidade aparecer.
        Assert.Equal([Novidades.Edicao], _gravacoes);
    }

    [Fact]
    public void Atualizacao_por_cima_sem_registro_mostra()
    {
        var vm = Criar(edicaoVista: null, haInstalacaoAnterior: true);

        Assert.True(vm.DeveMostrar);
        Assert.True(vm.Aberto);
        // Só grava quando o operador fecha — não antes de ver.
        Assert.Empty(_gravacoes);
    }

    [Fact]
    public void Edicao_mais_nova_que_a_vista_mostra()
    {
        var vm = Criar(edicaoVista: Novidades.Edicao - 1, haInstalacaoAnterior: false);

        Assert.True(vm.DeveMostrar);
        Assert.Empty(_gravacoes);
    }

    [Fact]
    public void Mesma_edicao_ja_vista_nao_mostra_nem_regrava()
    {
        var vm = Criar(edicaoVista: Novidades.Edicao, haInstalacaoAnterior: true);

        Assert.False(vm.DeveMostrar);
        Assert.Empty(_gravacoes);
    }

    [Fact]
    public void Edicao_vista_mais_nova_nao_mostra()
    {
        // Downgrade (ou arquivo adulterado): não insiste.
        var vm = Criar(edicaoVista: Novidades.Edicao + 1, haInstalacaoAnterior: true);

        Assert.False(vm.DeveMostrar);
    }

    [Fact]
    public async Task Fechar_marca_a_edicao_como_vista()
    {
        var vm = Criar(edicaoVista: null, haInstalacaoAnterior: true);
        Assert.True(vm.Aberto);

        await vm.Fechar.ExecutarAsync();

        Assert.False(vm.Aberto);
        Assert.Equal([Novidades.Edicao], _gravacoes);
    }

    [Fact]
    public void Confirmar_visto_grava_uma_vez_so()
    {
        var vm = Criar(edicaoVista: null, haInstalacaoAnterior: true);

        vm.ConfirmarVisto();
        vm.ConfirmarVisto();
        vm.ConfirmarVisto();

        Assert.Equal([Novidades.Edicao], _gravacoes);
    }

    [Fact]
    public void Versao_em_branco_vira_travessao()
    {
        var vm = Criar(edicaoVista: Novidades.Edicao, haInstalacaoAnterior: false, versao: "   ");

        Assert.Equal("—", vm.Versao);
    }

    [Fact]
    public void O_texto_do_aviso_existe_e_e_para_operador()
    {
        var vm = Criar(edicaoVista: null, haInstalacaoAnterior: true, versao: "0.1.120");

        Assert.Equal(Novidades.Titulo, vm.Titulo);
        Assert.Equal(Novidades.Abertura, vm.Abertura);
        Assert.NotEmpty(vm.Itens);
        Assert.Equal("0.1.120", vm.Versao);

        // Nada de código interno ou sigla de etapa no texto do operador.
        foreach (var item in vm.Itens)
        {
            Assert.DoesNotContain("origem ", item, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("docs/", item, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Etapa ", item, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    public void Decidir_cobre_os_casos(int? edicaoVista, bool haInstalacaoAnterior, bool esperado)
    {
        // edicaoAtual = 1 fixo aqui, para a tabela não depender da constante.
        Assert.Equal(esperado, NovidadesViewModel.Decidir(edicaoVista, edicaoAtual: 1, haInstalacaoAnterior));
    }
}
