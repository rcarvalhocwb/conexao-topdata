using System.Globalization;
using System.Text;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>Uma linha do R1: um tipo de entrada no dia.</summary>
public sealed record LinhaDoBoletim(string Tipo, long Validados, string Participacao, long SemPassagem);

/// <summary>Uma linha de contagem simples (motivo de negativa, catraca, hora).</summary>
public sealed record LinhaDeContagem(string Rotulo, string Detalhe, long Total);

/// <summary>Uma linha do R3.</summary>
public sealed record LinhaDaCatracaNoRelatorio(string Catraca, long Validados, long SemPassagem, long Negados);

/// <summary>Um dia de operação para escolher na lista do R1.</summary>
public sealed record DiaDeOperacaoNaLista(string Rotulo, BoletimDoDiaDeOperacao Boletim);

/// <summary>
/// Os relatórios R1 (boletim do dia), R2 (fluxo por hora e tipo), R3 (por catraca) e R7 (consolidado)
/// da Prestação de contas (docs/25 §3), com o cabeçalho e o código de conferência do §4.
/// </summary>
public sealed partial class ContasViewModel
{
    private static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;
    private RelatoriosDoEvento? _relatorios;
    private DiaDeOperacaoNaLista? _diaEscolhido;

    /// <summary>Os relatórios do último "Gerar".</summary>
    public RelatoriosDoEvento? Relatorios
    {
        get => _relatorios;
        private set
        {
            if (Definir(ref _relatorios, value))
            {
                var dias = DiasDeOperacao;
                DiaEscolhido = dias.Count > 0 ? dias[^1] : null;
                Avisar(nameof(DiasDeOperacao));
                Avisar(nameof(Cabecalho));
                Avisar(nameof(Conferencia));
                Avisar(nameof(FluxoPorHora));
                Avisar(nameof(MovimentoPorCatraca));
                Avisar(nameof(Consolidado));
                Avisar(nameof(ResumoDoConsolidado));
                Avisar(nameof(TemRelatorios));
            }
        }
    }

    public bool TemRelatorios => Relatorios is not null;

    /// <summary>Cabeçalho do docs/25 §4: evento, período, fuso, gerado em, por quem, versão e autoria.</summary>
    public string Cabecalho => Relatorios is not { } r
        ? string.Empty
        : string.Create(Cultura,
            $"{(string.IsNullOrWhiteSpace(r.NomeDoEvento) ? "Evento sem nome (defina em Configurações › Evento)" : r.NomeDoEvento)} · " +
            $"{PeriodoDosRelatorios(r)} · dia de operação a partir das {r.HoraDeCorte} · horário de Brasília · gerado em " +
            $"{FusoDoEvento.NoEvento(r.GeradoEm.ToDateTimeOffset()):dd/MM/yyyy HH:mm} por {r.GeradoPor} · versão {r.Versao} · Rayzer Serviços e Tecnologia");

    /// <summary>O código de conferência: o mesmo período e os mesmos dados dão o mesmo código.</summary>
    public string Conferencia => Relatorios?.Conferencia ?? string.Empty;

    /// <summary>Os dias de operação do período, para escolher o boletim (R1).</summary>
    public IReadOnlyList<DiaDeOperacaoNaLista> DiasDeOperacao => Relatorios is not { } r
        ? []
        : [.. r.Dias.Select(d => new DiaDeOperacaoNaLista(
            string.Create(Cultura, $"Dia de operação de {FusoDoEvento.NoEvento(d.Inicio.ToDateTimeOffset()):dd/MM/yyyy} ({d.Validados.Sum(v => v.Quantidade)} validados)"),
            d))];

    /// <summary>O dia mostrado no R1.</summary>
    public DiaDeOperacaoNaLista? DiaEscolhido
    {
        get => _diaEscolhido;
        set
        {
            if (Definir(ref _diaEscolhido, value))
            {
                Avisar(nameof(Boletim));
                Avisar(nameof(ResumoDoBoletim));
                Avisar(nameof(NegadosDoDia));
            }
        }
    }

    /// <summary>R1 do dia escolhido: validados por tipo, com a participação, e os liberados sem passagem.</summary>
    public IReadOnlyList<LinhaDoBoletim> Boletim => DiaEscolhido?.Boletim is { } dia ? LinhasDoBoletim(dia) : [];

    /// <summary>R1 do dia escolhido em uma frase.</summary>
    public string ResumoDoBoletim => DiaEscolhido?.Boletim is { } dia ? ResumoDe(dia) : string.Empty;

    /// <summary>R1 do dia escolhido: os negados, por motivo.</summary>
    public IReadOnlyList<LinhaDeContagem> NegadosDoDia => DiaEscolhido?.Boletim is { } dia ? NegadosDe(dia) : [];

    /// <summary>R1 de um dia: validados por tipo, na ordem dos tipos, com a participação e os sem passagem.</summary>
    public IReadOnlyList<LinhaDoBoletim> LinhasDoBoletim(BoletimDoDiaDeOperacao dia)
    {
        ArgumentNullException.ThrowIfNull(dia);
        if (Relatorios is not { } r)
        {
            return [];
        }

        var total = dia.Validados.Sum(v => v.Quantidade);
        return [.. r.Tipos
            .Select(t => (Tipo: t, Validados: Quantidade(dia.Validados, t.Tipo), SemPassagem: Quantidade(dia.SemPassagem, t.Tipo)))
            .Where(l => l.Validados > 0 || l.SemPassagem > 0)
            .Select(l => new LinhaDoBoletim(l.Tipo.Nome, l.Validados, Percentual(l.Validados, total), l.SemPassagem))];
    }

    /// <summary>R1 de um dia em uma frase: total, distintos e reentradas, primeiro e último acesso, pico.</summary>
    public static string ResumoDe(BoletimDoDiaDeOperacao d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var total = d.Validados.Sum(v => v.Quantidade);
        return string.Create(Cultura,
            $"{total} acessos validados, de {d.Distintos} ingressos e credenciais distintos ({Math.Max(0, total - d.Distintos)} reentradas) · " +
            $"{d.SemPassagem.Sum(v => v.Quantidade)} liberados sem passagem · {d.Negados.Sum(v => v.Quantidade)} negados · " +
            $"primeiro acesso {Hora(d.Primeiro)} · último {Hora(d.Ultimo)} · pico {Hora(d.HoraDePico)} ({d.NoPico} na hora)");
    }

    /// <summary>R1 de um dia: os negados por motivo, com o texto de portaria.</summary>
    public static IReadOnlyList<LinhaDeContagem> NegadosDe(BoletimDoDiaDeOperacao d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return [.. d.Negados.Select(n => new LinhaDeContagem(string.IsNullOrEmpty(n.Descricao) ? n.Chave : n.Descricao, string.Empty, n.Quantidade))];
    }

    /// <summary>R2: cada hora com os validados por tipo e o total.</summary>
    public IReadOnlyList<LinhaDeContagem> FluxoPorHora => Relatorios is not { } r
        ? []
        : [.. r.PorHora
            .GroupBy(h => h.Hora.ToDateTimeOffset())
            .OrderBy(g => g.Key)
            .Select(g => new LinhaDeContagem(
                string.Create(Cultura, $"{FusoDoEvento.NoEvento(g.Key):dd/MM HH}:00"),
                string.Join(" · ", r.Tipos.Where(t => g.Any(h => h.Tipo == t.Tipo))
                    .Select(t => string.Create(Cultura, $"{t.Nome} {g.Where(h => h.Tipo == t.Tipo).Sum(h => h.Quantidade)}"))),
                g.Sum(h => h.Quantidade)))];

    /// <summary>R3: validados, sem passagem e negados por catraca.</summary>
    public IReadOnlyList<LinhaDaCatracaNoRelatorio> MovimentoPorCatraca => Relatorios is not { } r
        ? []
        : [.. r.PorCatraca.Select(c => new LinhaDaCatracaNoRelatorio(c.Nome, c.Validados, c.SemPassagem, c.Negados))];

    /// <summary>R7: cada dia de operação, com os tipos e o total.</summary>
    public IReadOnlyList<LinhaDeContagem> Consolidado => Relatorios is not { } r
        ? []
        : [.. r.Dias.Select(d => new LinhaDeContagem(
            string.Create(Cultura, $"{FusoDoEvento.NoEvento(d.Inicio.ToDateTimeOffset()):dd/MM/yyyy}"),
            PorTipo(r, d.Validados),
            d.Validados.Sum(v => v.Quantidade)))];

    /// <summary>R7 em uma frase: total por tipo, total geral, média por dia, maior dia e maior hora.</summary>
    public string ResumoDoConsolidado
    {
        get
        {
            if (Relatorios is not { } r || r.Dias.Count == 0)
            {
                return string.Empty;
            }

            var todos = r.Dias.SelectMany(d => d.Validados).GroupBy(v => v.Chave)
                .Select(g => new QuantidadePorChave { Chave = g.Key, Quantidade = g.Sum(v => v.Quantidade) }).ToList();
            var total = todos.Sum(v => v.Quantidade);
            var maiorDia = r.Dias.MaxBy(d => d.Validados.Sum(v => v.Quantidade))!;
            var maiorHora = r.PorHora.GroupBy(h => h.Hora.ToDateTimeOffset()).MaxBy(g => g.Sum(h => h.Quantidade));

            var resumo = string.Create(Cultura,
                $"Total: {total} validados ({PorTipo(r, todos)}) · média de {Math.Round((double)total / r.Dias.Count, 1)} por dia em {r.Dias.Count} dia(s) · " +
                $"maior dia {FusoDoEvento.NoEvento(maiorDia.Inicio.ToDateTimeOffset()):dd/MM} ({maiorDia.Validados.Sum(v => v.Quantidade)})");
            return maiorHora is null
                ? resumo
                : resumo + string.Create(Cultura, $" · maior hora {FusoDoEvento.NoEvento(maiorHora.Key):dd/MM HH}:00 ({maiorHora.Sum(h => h.Quantidade)})");
        }
    }

    // As seções dos relatórios no fim do CSV da prestação de contas (docs/25 §4: CSV para o contador).
    private void AcrescentarRelatorios(StringBuilder csv)
    {
        if (Relatorios is not { } r)
        {
            return;
        }

        csv.AppendLine();
        csv.AppendLine(string.Create(Cultura, $"Relatórios;{Celula(Cabecalho)}"));
        csv.AppendLine(string.Create(Cultura, $"Código de conferência;{r.Conferencia}"));

        foreach (var dia in r.Dias)
        {
            var total = dia.Validados.Sum(v => v.Quantidade);
            csv.AppendLine();
            csv.AppendLine(string.Create(Cultura, $"R1 · Boletim do dia de operação de {FusoDoEvento.NoEvento(dia.Inicio.ToDateTimeOffset()):dd/MM/yyyy}"));
            csv.AppendLine("Tipo;Validados;Participação;Liberados sem passagem");
            foreach (var t in r.Tipos)
            {
                var validados = Quantidade(dia.Validados, t.Tipo);
                var semPassagem = Quantidade(dia.SemPassagem, t.Tipo);
                if (validados > 0 || semPassagem > 0)
                {
                    csv.AppendLine(string.Create(Cultura, $"{Celula(t.Nome)};{validados};{Percentual(validados, total)};{semPassagem}"));
                }
            }

            csv.AppendLine(string.Create(Cultura, $"Total;{total};100%;{dia.SemPassagem.Sum(v => v.Quantidade)}"));
            csv.AppendLine(string.Create(Cultura, $"Ingressos e credenciais distintos;{dia.Distintos}"));
            csv.AppendLine(string.Create(Cultura, $"Primeiro acesso;{Hora(dia.Primeiro)}"));
            csv.AppendLine(string.Create(Cultura, $"Último acesso;{Hora(dia.Ultimo)}"));
            csv.AppendLine(string.Create(Cultura, $"Hora de pico;{Hora(dia.HoraDePico)};{dia.NoPico}"));
            csv.AppendLine("Negados por motivo;Quantidade");
            foreach (var n in dia.Negados)
            {
                csv.AppendLine(string.Create(Cultura, $"{Celula(string.IsNullOrEmpty(n.Descricao) ? n.Chave : n.Descricao)};{n.Quantidade}"));
            }
        }

        csv.AppendLine();
        csv.AppendLine("R2 · Fluxo por hora e tipo");
        csv.AppendLine("Hora;Tipo;Validados");
        foreach (var h in r.PorHora)
        {
            var nome = r.Tipos.FirstOrDefault(t => t.Tipo == h.Tipo)?.Nome ?? h.Tipo;
            csv.AppendLine(string.Create(Cultura, $"{FusoDoEvento.NoEvento(h.Hora.ToDateTimeOffset()):dd/MM/yyyy HH}:00;{Celula(nome)};{h.Quantidade}"));
        }

        csv.AppendLine();
        csv.AppendLine("R3 · Por catraca");
        csv.AppendLine("Catraca;Validados;Liberados sem passagem;Negados");
        foreach (var c in r.PorCatraca)
        {
            csv.AppendLine(string.Create(Cultura, $"{Celula(c.Nome)};{c.Validados};{c.SemPassagem};{c.Negados}"));
        }

        csv.AppendLine();
        csv.AppendLine("R7 · Consolidado do evento");
        csv.AppendLine("Dia de operação;Por tipo;Total");
        foreach (var l in Consolidado)
        {
            csv.AppendLine(string.Create(Cultura, $"{l.Rotulo};{Celula(l.Detalhe)};{l.Total}"));
        }

        csv.AppendLine(string.Create(Cultura, $"Resumo;{Celula(ResumoDoConsolidado)}"));
    }

    private static long Quantidade(IEnumerable<QuantidadePorChave> lista, string chave) =>
        lista.Where(v => v.Chave == chave).Sum(v => v.Quantidade);

    private static string PorTipo(RelatoriosDoEvento r, IEnumerable<QuantidadePorChave> validados)
    {
        var lista = validados.ToList();
        return string.Join(" · ", r.Tipos
            .Select(t => (t.Nome, Quantidade: Quantidade(lista, t.Tipo)))
            .Where(t => t.Quantidade > 0)
            .Select(t => string.Create(Cultura, $"{t.Nome} {t.Quantidade}")));
    }

    private static string Percentual(long parte, long total) =>
        total == 0 ? "0%" : string.Create(Cultura, $"{Math.Round(100.0 * parte / total, 1)}%");

    private static string Hora(Google.Protobuf.WellKnownTypes.Timestamp? instante) =>
        instante is null ? "—" : FusoDoEvento.NoEvento(instante.ToDateTimeOffset()).ToString("dd/MM HH:mm", Cultura);

    private static string PeriodoDosRelatorios(RelatoriosDoEvento r) =>
        string.Create(Cultura,
            $"de {FusoDoEvento.NoEvento(r.PeriodoDesde.ToDateTimeOffset()):dd/MM/yyyy HH:mm} a {FusoDoEvento.NoEvento(r.PeriodoAte.ToDateTimeOffset()):dd/MM/yyyy HH:mm}");
}
