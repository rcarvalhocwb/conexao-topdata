using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Uma catraca tem um só nome na tela: o configurado no assistente; sem nome, "Catraca NN".
/// Antes, o serviço usava "setor-a/1" e a explicação usava "Catraca NN", e o operador via os dois.
/// </summary>
public sealed class NomeDaCatracaTests
{
    [Fact]
    public void Catraca_com_nome_configurado_mostra_o_nome()
    {
        var nomes = new Dictionary<int, string> { [2] = "Entrada 2" };

        Assert.Equal("Entrada 2", EdgeControlService.NomeDaCatracaPara(nomes, 2));
    }

    [Fact]
    public void Catraca_sem_nome_mostra_o_numero_com_dois_digitos()
    {
        Assert.Equal("Catraca 01", EdgeControlService.NomeDaCatracaPara(new Dictionary<int, string>(), 1));
        Assert.Equal("Catraca 12", EdgeControlService.NomeDaCatracaPara(new Dictionary<int, string>(), 12));
    }

    [Fact]
    public void Nome_em_branco_conta_como_sem_nome()
    {
        var nomes = new Dictionary<int, string> { [3] = "   " };

        Assert.Equal("Catraca 03", EdgeControlService.NomeDaCatracaPara(nomes, 3));
    }
}
