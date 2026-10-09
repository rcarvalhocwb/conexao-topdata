using System.Text;

namespace Integration.Tests;

/// <summary>
/// Os modelos de planilha instalados em "Modelos" (docs/26) seguem o formato documentado e
/// só trazem números fictícios.
/// </summary>
public sealed class ModelosDePlanilhaTests
{
    private static readonly string[] CabecalhoDeCartoes =
        ["codigo", "tipo", "titular", "situacao", "validade_inicio", "validade_fim", "usos_maximos", "observacao"];

    private static readonly string[] CabecalhoDeTipos = ["tipo", "nome_exibido", "ordem", "ativo"];

    private static readonly string[] Situacoes = ["ATIVO", "BLOQUEADO"];

    private static readonly string[] SimOuNao = ["SIM", "NAO"];

    [Fact]
    public void O_modelo_de_cartoes_em_csv_tem_o_cabecalho_documentado_e_so_exemplos_ficticios()
    {
        var linhas = Ler("cartoes-modelo.csv");
        Assert.Equal(CabecalhoDeCartoes, linhas[0]);

        var tipos = Ler("tipos-modelo.csv").Skip(1).Select(l => l[0]).ToHashSet(StringComparer.Ordinal);

        foreach (var linha in linhas.Skip(1))
        {
            Assert.Equal(CabecalhoDeCartoes.Length, linha.Length);
            // Fictício: 9999 no início, depois de eventuais zeros à esquerda.
            Assert.StartsWith("9999", linha[0].TrimStart('0'), StringComparison.Ordinal);
            Assert.Contains(linha[1], tipos);
            Assert.Contains(linha[3], Situacoes);
        }

        // O exemplo com zeros à esquerda precisa existir: é o caso que o modelo ensina.
        Assert.Contains(linhas.Skip(1), l => l[0].StartsWith('0'));
    }

    [Fact]
    public void O_modelo_de_tipos_em_csv_tem_o_cabecalho_documentado()
    {
        var linhas = Ler("tipos-modelo.csv");
        Assert.Equal(CabecalhoDeTipos, linhas[0]);
        Assert.All(linhas.Skip(1), l => Assert.Contains(l[3], SimOuNao));
    }

    [Fact]
    public void O_modelo_em_excel_existe_e_nao_esta_vazio()
    {
        var arquivo = new FileInfo(Path.Combine(Pasta(), "modelo-cadastro-de-cartoes.xlsx"));
        Assert.True(arquivo.Exists);
        Assert.True(arquivo.Length > 1024);
    }

    private static List<string[]> Ler(string nome) =>
        [.. File.ReadAllLines(Path.Combine(Pasta(), nome), Encoding.UTF8)
            .Where(l => l.Length > 0)
            .Select(l => l.TrimStart('﻿').Split(';'))];

    private static string Pasta()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "ConexaoTopdata.slnx")))
        {
            raiz = raiz.Parent;
        }

        Assert.NotNull(raiz);
        return Path.Combine(raiz.FullName, "installer", "modelos");
    }
}
