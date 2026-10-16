using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Desktop.ViewModels;
using Rayzer.Design;

namespace Desktop.App.Telas;

/// <summary>
/// Os relatórios R1, R2, R3 e R7 em papel (docs/25 §4): cabeçalho, código de conferência, página
/// numerada "1 de N" e campo para a assinatura do responsável no R1 e no R7. Em PDF, pela impressora
/// "Microsoft Print to PDF" do Windows: nenhuma biblioteca a mais.
/// </summary>
internal static class DocumentoDosRelatorios
{
    private const double Margem = 48;

    // Papel: sempre o tema claro do Rayzer, nunca cor solta (e nunca o tema escuro da tela).
    private static readonly ResourceDictionary Papel = TemaRayzer.DoPapel();

    internal static Brush Pincel(string chave) => (Brush)Papel[chave];

    /// <summary>O documento com todos os relatórios gerados na tela.</summary>
    public static FlowDocument Montar(ContasViewModel contas, double largura, double altura)
    {
        ArgumentNullException.ThrowIfNull(contas);

        var documento = new FlowDocument
        {
            PageWidth = largura,
            PageHeight = altura,
            ColumnWidth = largura,
            PagePadding = new Thickness(Margem, Margem, Margem, Margem + 24),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10.5,
            Foreground = Pincel("Rayzer.Text.Primary"),
            Background = Pincel("Rayzer.Surface"),
        };

        documento.Blocks.Add(Titulo("Prestação de contas — Rayzer XAcess", 18));
        documento.Blocks.Add(Texto(contas.Cabecalho, 9.5));
        documento.Blocks.Add(Texto($"Código de conferência: {contas.Conferencia}", 10, negrito: true));

        foreach (var dia in contas.DiasDeOperacao)
        {
            documento.Blocks.Add(Titulo($"R1 · Boletim do {dia.Rotulo.Replace(" (", " — ", StringComparison.Ordinal).TrimEnd(')')}", 13));
            documento.Blocks.Add(Tabela(
                ["Tipo", "Validados", "Participação", "Sem passagem"],
                contas.LinhasDoBoletim(dia.Boletim).Select(l => new[] { l.Tipo, Numero(l.Validados), l.Participacao, Numero(l.SemPassagem) })));
            documento.Blocks.Add(Texto(ContasViewModel.ResumoDe(dia.Boletim), 10));
            documento.Blocks.Add(Texto(ContasViewModel.NotaDaMeiaEntrada, 8.5));

            var negados = ContasViewModel.NegadosDe(dia.Boletim);
            if (negados.Count > 0)
            {
                documento.Blocks.Add(Tabela(["Negados por motivo", "Quantidade"], negados.Select(n => new[] { n.Rotulo, Numero(n.Total) })));
            }

            documento.Blocks.Add(Assinatura());
        }

        documento.Blocks.Add(Titulo("R2 · Fluxo por hora e tipo", 13));
        documento.Blocks.Add(Tabela(["Hora", "Por tipo", "Total"], contas.FluxoPorHora.Select(l => new[] { l.Rotulo, l.Detalhe, Numero(l.Total) })));

        documento.Blocks.Add(Titulo("R3 · Por catraca", 13));
        documento.Blocks.Add(Tabela(
            ["Catraca", "Validados", "Sem passagem", "Negados"],
            contas.MovimentoPorCatraca.Select(c => new[] { c.Catraca, Numero(c.Validados), Numero(c.SemPassagem), Numero(c.Negados) })));

        documento.Blocks.Add(Titulo("R7 · Consolidado do evento", 13));
        documento.Blocks.Add(Tabela(["Dia de operação", "Por tipo", "Total"], contas.Consolidado.Select(l => new[] { l.Rotulo, l.Detalhe, Numero(l.Total) })));
        documento.Blocks.Add(Texto(contas.ResumoDoConsolidado, 10));
        documento.Blocks.Add(Assinatura());

        documento.Blocks.Add(Texto(
            "Validado = a catraca liberou e a pessoa passou (giro confirmado). Sem passagem = a catraca liberou e a pessoa não passou. " +
            "Agregados: nenhum número de cartão nem nome de titular. Sistema desenvolvido sob demanda por Rayzer Serviços e Tecnologia.",
            8.5));
        return documento;
    }

    private static Paragraph Titulo(string texto, double tamanho) =>
        new(new Run(texto)) { FontSize = tamanho, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };

    private static Paragraph Texto(string texto, double tamanho, bool negrito = false) =>
        new(new Run(texto)) { FontSize = tamanho, FontWeight = negrito ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 4) };

    private static Paragraph Assinatura() =>
        new(new Run("Responsável: ________________________________________    Data: ____/____/________"))
        {
            FontSize = 10,
            Margin = new Thickness(0, 18, 0, 8),
        };

    private static Table Tabela(string[] cabecalho, IEnumerable<string[]> linhas)
    {
        var tabela = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 6), BorderBrush = Pincel("Rayzer.Border.Strong"), BorderThickness = new Thickness(0.5) };
        for (var i = 0; i < cabecalho.Length; i++)
        {
            tabela.Columns.Add(new TableColumn { Width = i == 0 || cabecalho[i] == "Por tipo" ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star) });
        }

        var grupo = new TableRowGroup();
        grupo.Rows.Add(Linha(cabecalho, negrito: true));
        var vazia = true;
        foreach (var linha in linhas)
        {
            grupo.Rows.Add(Linha(linha, negrito: false));
            vazia = false;
        }

        if (vazia)
        {
            var celulas = new string[cabecalho.Length];
            Array.Fill(celulas, string.Empty);
            celulas[0] = "Nada no período.";
            grupo.Rows.Add(Linha(celulas, negrito: false));
        }

        tabela.RowGroups.Add(grupo);
        return tabela;
    }

    private static TableRow Linha(string[] celulas, bool negrito)
    {
        var linha = new TableRow();
        if (negrito)
        {
            linha.Background = Pincel("Rayzer.Surface.Sunken");
        }

        foreach (var celula in celulas)
        {
            linha.Cells.Add(new TableCell(new Paragraph(new Run(celula)) { Margin = new Thickness(4, 2, 4, 2) })
            {
                BorderBrush = Pincel("Rayzer.Border"),
                BorderThickness = new Thickness(0, 0, 0, 0.5),
                FontWeight = negrito ? FontWeights.SemiBold : FontWeights.Normal,
            });
        }

        return linha;
    }

    private static string Numero(long valor) => valor.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Põe no pé de cada página "Página X de N" e o código de conferência.</summary>
internal sealed class PaginadorComRodape : DocumentPaginator
{
    private readonly DocumentPaginator _original;
    private readonly string _rodape;

    public PaginadorComRodape(DocumentPaginator original, string rodape)
    {
        ArgumentNullException.ThrowIfNull(original);
        _original = original;
        _rodape = rodape;
        _original.ComputePageCount();
    }

    public override bool IsPageCountValid => _original.IsPageCountValid;

    public override int PageCount => _original.PageCount;

    public override Size PageSize
    {
        get => _original.PageSize;
        set => _original.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => _original.Source;

    public override DocumentPage GetPage(int pageNumber)
    {
        var pagina = _original.GetPage(pageNumber);
        var visual = new ContainerVisual();
        visual.Children.Add(pagina.Visual);

        var texto = new FormattedText(
            string.Create(CultureInfo.InvariantCulture, $"Página {pageNumber + 1} de {PageCount} · {_rodape}"),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            9,
            DocumentoDosRelatorios.Pincel("Rayzer.Text.Primary"),
            1.0);

        var rodape = new DrawingVisual();
        using (var desenho = rodape.RenderOpen())
        {
            desenho.DrawText(texto, new Point(48, pagina.Size.Height - 40));
        }

        visual.Children.Add(rodape);
        return new DocumentPage(visual, pagina.Size, pagina.BleedBox, pagina.ContentBox);
    }
}
