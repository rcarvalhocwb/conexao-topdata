using System.Reflection;
using System.Text.RegularExpressions;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Integration.Tests;

/// <summary>
/// Confere, sem abrir o WPF, que todo <c>{Binding ...}</c> das telas aponta para uma
/// propriedade que existe.
/// </summary>
/// <remarks>
/// Nome de propriedade errado em XAML não é erro de compilação: a tela abre com o campo em
/// branco, e o único aviso é uma linha no depurador. Como as telas só rodam no Windows com
/// tela, este teste é a única rede antes de o operador abrir o painel.
/// </remarks>
public sealed partial class LigacoesDasTelasTests
{
    // Tipos cujas propriedades cada arquivo pode ligar: a ViewModel e o que aparece nas
    // listas dela.
    private static readonly Dictionary<string, System.Type[]> TiposPorArquivo = new()
    {
        ["Telas/PainelAoVivo.xaml"] = [typeof(PainelAoVivoViewModel), typeof(LinhaDeCatraca), typeof(LinhaDeAcesso)],
        ["Telas/Catracas.xaml"] = [typeof(CatracasViewModel), typeof(LinhaDeCatraca)],
        ["Telas/Acessos.xaml"] = [typeof(AcessosViewModel), typeof(LinhaDeAcesso)],
        ["Telas/Consulta.xaml"] = [typeof(ConsultaViewModel), typeof(ParDeTexto), typeof(LinhaDeAcesso)],
        ["Telas/Sincronizacao.xaml"] = [typeof(SincronizacaoViewModel), typeof(ParDeTexto), typeof(ProvedorCadastrado)],
        ["Telas/Contas.xaml"] =
        [
            typeof(ContasViewModel), typeof(PrestacaoDeContas), typeof(LinhaPorCategoria), typeof(LinhaPorCatraca),
            typeof(LinhaPorHora), typeof(LinhaDeNegativa), typeof(ParDeTexto),
        ],
        ["Telas/GerenciarCatraca.xaml"] = [typeof(GerenciarCatracaViewModel), typeof(LinhaDeCatraca), typeof(LinhaDeComando), typeof(ParDeTexto)],
        ["Telas/Gemeo.xaml"] =
        [
            typeof(GemeoDigitalViewModel), typeof(LinhaDeCatraca), typeof(Desktop.ViewModels.GemeoDigital.FichaDaPeca),
            typeof(Desktop.ViewModels.GemeoDigital.FuncaoDaPeca), typeof(Desktop.ViewModels.GemeoDigital.RoteiroDeDemonstracao),
            typeof(Desktop.ViewModels.GemeoDigital.LinhaDaNarracao), typeof(ParDeTexto), typeof(Desktop.ViewModels.GemeoDigital.MarcaDaPeca),
            typeof(LinhaDeMudanca),
        ],
        ["Telas/Configuracoes.xaml"] = [typeof(ConfiguracoesViewModel)],
        ["Telas/Parametrizacao.xaml"] =
        [
            typeof(ParametrizacaoViewModel), typeof(LinhaDeCatraca), typeof(CampoDaParametrizacao), typeof(OpcaoDoCampo),
            typeof(LinhaDeMudanca), typeof(LinhaDeComando),
        ],
        ["Telas/MapaDeGiro.xaml"] = [typeof(MapaDeGiroViewModel), typeof(LinhaDoMapaDeGiro), typeof(OpcaoDoGiro), typeof(LinhaDeMudanca)],
        ["Telas/CampoDaCatraca.xaml"] = [typeof(CampoDaParametrizacao), typeof(OpcaoDoCampo)],
        ["Telas/CentralDaCatraca.xaml"] =
        [
            typeof(CentralDaCatracaViewModel), typeof(LinhaDeMudanca), typeof(Desktop.ViewModels.GemeoDigital.FuncaoDaPeca),
        ],
        ["Telas/Diagnostico.xaml"] = [typeof(DiagnosticoViewModel), typeof(Diagnostico), typeof(DiagnosticoDeWorker)],
        ["Telas/Simulador.xaml"] = [typeof(SimuladorViewModel), typeof(LinhaDeAcesso), typeof(ParDeTexto)],
        ["JanelaPrincipal.xaml"] = [typeof(JanelaViewModel), typeof(PainelAoVivoViewModel), typeof(EstadoDoPainel), typeof(ITela)],
    };

    [GeneratedRegex(@"\{Binding(?:\s+Path=)?\s*([A-Za-z_][A-Za-z0-9_.]*)?")]
    private static partial Regex Ligacao();

    public static TheoryData<string> Arquivos() => [.. TiposPorArquivo.Keys];

    [Theory]
    [MemberData(nameof(Arquivos))]
    public void Toda_ligacao_da_tela_aponta_para_propriedade_que_existe(string arquivo)
    {
        var xaml = File.ReadAllText(Path.Combine(PastaDoAplicativo(), arquivo));
        var tipos = TiposPorArquivo[arquivo];
        var faltando = new List<string>();

        foreach (Match m in Ligacao().Matches(xaml))
        {
            var caminho = m.Groups[1].Value;

            // Ligação a outro elemento da tela, não à ViewModel.
            var expressao = xaml[m.Index..Math.Min(xaml.Length, xaml.IndexOf('}', m.Index) + 1)];
            if (expressao.Contains("ElementName", StringComparison.Ordinal) || expressao.Contains("RelativeSource", StringComparison.Ordinal))
            {
                continue;
            }

            // {Binding} sozinho, ou só com StringFormat/Converter: liga ao próprio item.
            if (string.IsNullOrEmpty(caminho) || caminho is "StringFormat" or "Converter" or "Mode")
            {
                continue;
            }

            if (!Resolve(caminho, tipos))
            {
                faltando.Add(caminho);
            }
            else if (caminho.EndsWith(".Count", StringComparison.Ordinal) && !ContagemVisivelAoWpf(caminho[..^6], tipos))
            {
                // Lista somente leitura: Count é implementado de forma explícita e a ligação
                // do WPF não o enxerga. Ligue na lista (ZeroParaVisivel conta sozinho).
                faltando.Add($"{caminho} (Count invisível ao WPF)");
            }
        }

        Assert.True(faltando.Count == 0, $"{arquivo}: ligações sem propriedade: {string.Join(", ", faltando.Distinct())}");
    }

    [Fact]
    public void Toda_viewmodel_de_tela_tem_desenho_registrado()
    {
        var app = File.ReadAllText(Path.Combine(PastaDoAplicativo(), "App.xaml"));

        foreach (var tipo in typeof(ITela).Assembly.GetTypes().Where(t => typeof(ITela).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
        {
            Assert.Contains($"{{x:Type vm:{tipo.Name}}}", app, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Sem x:Class, o App.xaml vira um segundo aplicativo com Main próprio, que nunca abre
    /// a janela. Foi assim que o painel "não abria" no primeiro Setup.
    /// </summary>
    [Theory]
    [InlineData("src/Desktop.App/App.xaml", "Desktop.App.App")]
    [InlineData("src/Edge.Configurador/App.xaml", "Edge.Configurador.App")]
    public void O_App_xaml_esta_ligado_a_classe_do_aplicativo(string arquivo, string classe)
    {
        var xaml = File.ReadAllText(Path.Combine(PastaDoAplicativo(), "..", "..", arquivo));
        Assert.Contains($"x:Class=\"{classe}\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// WPF em globalização invariante cai na primeira ligação que converte número em texto
    /// ("Cannot find non-neutral culture related to 'en-us'"). Foi o painel caindo ao abrir
    /// e o assistente fechando no Avançar.
    /// </summary>
    [Theory]
    [InlineData("src/Desktop.App/Desktop.App.csproj")]
    [InlineData("src/Edge.Configurador/Edge.Configurador.csproj")]
    public void Aplicativo_de_tela_nao_usa_globalizacao_invariante(string projeto)
    {
        var xml = File.ReadAllText(Path.Combine(PastaDoAplicativo(), "..", "..", projeto));
        Assert.Contains("<InvariantGlobalization>false</InvariantGlobalization>", xml, StringComparison.Ordinal);
    }

    // Caminho pontilhado: cada trecho precisa existir no tipo do trecho anterior; o
    // primeiro, em algum dos tipos da tela.
    private static bool Resolve(string caminho, System.Type[] tipos)
    {
        var partes = caminho.Split('.');

        foreach (var inicial in tipos)
        {
            var tipo = inicial;
            var ok = true;

            foreach (var parte in partes)
            {
                var propriedade = Propriedade(tipo, parte);
                if (propriedade is null)
                {
                    ok = false;
                    break;
                }

                tipo = propriedade.PropertyType;
            }

            if (ok)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContagemVisivelAoWpf(string caminhoDaLista, System.Type[] tipos) =>
        tipos.Select(t => Propriedade(t, caminhoDaLista)?.PropertyType)
            .Any(t => t is not null && t.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance) is not null && !t.IsInterface);

    private static PropertyInfo? Propriedade(System.Type tipo, string nome) =>
        tipo.GetProperty(nome, BindingFlags.Public | BindingFlags.Instance)
        ?? tipo.GetInterfaces().Select(i => i.GetProperty(nome)).FirstOrDefault(p => p is not null);

    private static string PastaDoAplicativo()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        return Path.Combine(raiz.FullName, "src", "Desktop.App");
    }
}
