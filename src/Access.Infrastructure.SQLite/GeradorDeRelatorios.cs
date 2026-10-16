using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Access.Infrastructure.SQLite;

/// <summary>Uma contagem por chave (tipo de entrada ou motivo).</summary>
public sealed record Contagem(string Chave, long Quantidade);

/// <summary>R1 — o boletim de um dia de operação (docs/25 §3).</summary>
/// <param name="Inicio">Começo do dia de operação (a hora de corte), no horário de Brasília.</param>
/// <param name="Validados">Acessos validados (liberou e girou, entrada) por tipo, na ordem dos tipos.</param>
/// <param name="SemPassagem">Liberados em que a pessoa não passou, por tipo.</param>
/// <param name="Negados">Negados por motivo (sem o registro de cartões em lote, que não é passagem).</param>
/// <param name="Distintos">Cartões e credenciais distintos validados: a diferença para o total são reentradas.</param>
/// <param name="Primeiro">Primeiro acesso validado do dia.</param>
/// <param name="Ultimo">Último acesso validado do dia.</param>
/// <param name="HoraDePico">Começo da hora com mais acessos validados.</param>
/// <param name="NoPico">Acessos validados na hora de pico.</param>
public sealed record BoletimDoDia(
    DateTimeOffset Inicio,
    IReadOnlyList<Contagem> Validados,
    IReadOnlyList<Contagem> SemPassagem,
    IReadOnlyList<Contagem> Negados,
    long Distintos,
    DateTimeOffset? Primeiro,
    DateTimeOffset? Ultimo,
    DateTimeOffset? HoraDePico,
    long NoPico)
{
    public long TotalValidados => Validados.Sum(c => c.Quantidade);

    public long TotalSemPassagem => SemPassagem.Sum(c => c.Quantidade);

    public long TotalNegados => Negados.Sum(c => c.Quantidade);
}

/// <summary>R2 — validados de uma hora, de um tipo.</summary>
public sealed record ValidadosNaHora(DateTimeOffset Hora, string Tipo, long Quantidade);

/// <summary>R3 — o que aconteceu numa catraca no período.</summary>
public sealed record MovimentoDaCatraca(string Dispositivo, long Validados, long SemPassagem, long Negados);

/// <summary>Os relatórios R1, R2, R3 e R7 de um período, com o código de conferência.</summary>
/// <param name="Desde">Início do período.</param>
/// <param name="Ate">Fim do período.</param>
/// <param name="Corte">Hora de corte do dia de operação.</param>
/// <param name="Tipos">Os tipos na ordem cadastrada (os sem cadastro vêm depois, em ordem alfabética).</param>
/// <param name="NomesDosTipos">O nome de cada tipo para a tela e o PDF.</param>
/// <param name="Dias">R1, um por dia de operação (o R7 é a soma deles).</param>
/// <param name="PorHora">R2.</param>
/// <param name="PorCatraca">R3.</param>
/// <param name="Conferencia">SHA-256 (16 primeiros dígitos) dos números: o mesmo período e os mesmos dados dão o mesmo código.</param>
public sealed record RelatorioDoPeriodo(
    DateTimeOffset Desde,
    DateTimeOffset Ate,
    TimeSpan Corte,
    IReadOnlyList<string> Tipos,
    IReadOnlyDictionary<string, string> NomesDosTipos,
    IReadOnlyList<BoletimDoDia> Dias,
    IReadOnlyList<ValidadosNaHora> PorHora,
    IReadOnlyList<MovimentoDaCatraca> PorCatraca,
    string Conferencia);

/// <summary>
/// Os relatórios da prestação de contas (docs/25 §3): R1 boletim do dia, R2 fluxo por hora e tipo,
/// R3 por catraca e R7 consolidado. Agregados: nenhum número de cartão nem nome de titular sai daqui.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validado</b> é a catraca liberar <b>e</b> a pessoa girar, contado como entrada (ADR-0007). Liberado
/// sem giro é "sem passagem". O <b>tipo</b> é o que a tentativa gravou na hora do uso (migração 004).
/// </para>
/// <para>
/// O <b>dia de operação</b> vai da hora de corte até a mesma hora do dia seguinte, no horário de
/// Brasília (E9): uma noite que passa da meia-noite é um dia só.
/// </para>
/// </remarks>
public sealed class GeradorDeRelatorios
{
    /// <summary>Tipo das tentativas sem categoria gravada.</summary>
    public const string SemTipo = "(sem tipo)";

    private static readonly TimeZoneInfo Brasilia = Localizar();
    private readonly SqliteConnectionFactory _fabrica;

    public GeradorDeRelatorios(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Monta os relatórios do período.</summary>
    public RelatorioDoPeriodo Gerar(DateTimeOffset desde, DateTimeOffset ate, TimeSpan corte)
    {
        var linhas = Ler(desde, ate);
        // O tipo de uma negativa de código desconhecido não existe: só entra tipo que liberou alguém.
        var (tiposNaOrdem, nomes) = TiposNaOrdem(linhas.Where(l => l.Validado || l.SemPassagem).Select(l => l.Tipo));
        var tipos = tiposNaOrdem.ToList();

        var dias = linhas
            .GroupBy(l => DiaDeOperacao(l.Em, corte))
            .OrderBy(g => g.Key)
            .Select(g => Boletim(g.Key, g.ToList(), tipos))
            .ToList();

        var porHora = linhas
            .Where(l => l.Validado)
            .GroupBy(l => (Hora: InicioDaHora(l.Em), l.Tipo))
            .OrderBy(g => g.Key.Hora).ThenBy(g => tipos.IndexOf(g.Key.Tipo))
            .Select(g => new ValidadosNaHora(g.Key.Hora, g.Key.Tipo, g.LongCount()))
            .ToList();

        var porCatraca = linhas
            .GroupBy(l => l.Dispositivo)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new MovimentoDaCatraca(
                g.Key, g.LongCount(l => l.Validado), g.LongCount(l => l.SemPassagem), g.LongCount(l => l.Negado)))
            .ToList();

        return new RelatorioDoPeriodo(desde, ate, corte, tipos, nomes, dias, porHora, porCatraca,
            Conferencia(desde, ate, corte, dias, porHora, porCatraca));
    }

    /// <summary>O começo do dia de operação de um instante, no horário de Brasília.</summary>
    public static DateTimeOffset DiaDeOperacao(DateTimeOffset instante, TimeSpan corte)
    {
        var local = TimeZoneInfo.ConvertTime(instante, Brasilia);
        var dia = local.TimeOfDay < corte ? local.Date.AddDays(-1) : local.Date;
        var inicio = dia + corte;
        return new DateTimeOffset(inicio, Brasilia.GetUtcOffset(inicio));
    }

    private static DateTimeOffset InicioDaHora(DateTimeOffset instante)
    {
        var local = TimeZoneInfo.ConvertTime(instante, Brasilia);
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
    }

    private static BoletimDoDia Boletim(DateTimeOffset inicio, List<Linha> linhas, List<string> tipos)
    {
        IReadOnlyList<Contagem> PorTipo(Func<Linha, bool> filtro) =>
            [.. linhas.Where(filtro).GroupBy(l => l.Tipo)
                .OrderBy(g => tipos.IndexOf(g.Key))
                .Select(g => new Contagem(g.Key, g.LongCount()))];

        var validados = linhas.Where(l => l.Validado).ToList();
        var pico = validados.GroupBy(l => InicioDaHora(l.Em)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();

        return new BoletimDoDia(
            inicio,
            PorTipo(l => l.Validado),
            PorTipo(l => l.SemPassagem),
            [.. linhas.Where(l => l.Negado).GroupBy(l => l.Motivo).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Contagem(g.Key, g.LongCount()))],
            validados.Select(l => l.Quem).Where(q => q is not null).Distinct(StringComparer.Ordinal).LongCount(),
            validados.Count > 0 ? validados.Min(l => l.Em) : null,
            validados.Count > 0 ? validados.Max(l => l.Em) : null,
            pico?.Key,
            pico?.LongCount() ?? 0);
    }

    private List<Linha> Ler(DateTimeOffset desde, DateTimeOffset ate)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();

        // O registro de cartões em lote (CadastradoNoLote) não é passagem nem negativa de verdade.
        comando.CommandText =
            """
            SELECT at, outcome, reason, COALESCE(category, ''), device_id,
                   passage_confirmed_at IS NOT NULL AND COALESCE(counted_as, 'entrada') = 'entrada',
                   passage_confirmed_at IS NULL,
                   COALESCE(ticket_id, person_id)
            FROM ticket_use_attempt
            WHERE at >= $desde AND at <= $ate AND reason <> 'CadastradoNoLote';
            """;
        // O mesmo formato com que "at" é gravado (ISO "O", UTC): a comparação é de texto.
        comando.Parameters.AddWithValue("$desde", desde.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$ate", ate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var linhas = new List<Linha>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            var consumido = leitor.GetString(1) == "consumido";
            var tipo = leitor.GetString(3);
            linhas.Add(new Linha(
                DateTimeOffset.Parse(leitor.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                leitor.GetString(2),
                tipo.Length == 0 ? SemTipo : tipo,
                leitor.GetString(4),
                Validado: consumido && leitor.GetInt64(5) == 1,
                SemPassagem: consumido && leitor.GetInt64(6) == 1,
                Negado: !consumido,
                Quem: leitor.IsDBNull(7) ? null : leitor.GetString(7)));
        }

        return linhas;
    }

    private (IReadOnlyList<string> Tipos, IReadOnlyDictionary<string, string> Nomes) TiposNaOrdem(IEnumerable<string> usados)
    {
        var cadastrados = new List<(string Codigo, string Nome)>();
        using (var conexao = _fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "SELECT code, display_name FROM ticket_type ORDER BY sort_order, code;";
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                cadastrados.Add((leitor.GetString(0), leitor.GetString(1)));
            }
        }

        var nomes = cadastrados.ToDictionary(c => c.Codigo, c => c.Nome, StringComparer.Ordinal);
        var tipos = cadastrados.Select(c => c.Codigo).ToList();
        foreach (var tipo in usados.Distinct(StringComparer.Ordinal).Where(t => !nomes.ContainsKey(t)).Order(StringComparer.Ordinal))
        {
            tipos.Add(tipo);
            nomes[tipo] = tipo;
        }

        return (tipos, nomes);
    }

    private static string Conferencia(
        DateTimeOffset desde,
        DateTimeOffset ate,
        TimeSpan corte,
        IReadOnlyList<BoletimDoDia> dias,
        IReadOnlyList<ValidadosNaHora> porHora,
        IReadOnlyList<MovimentoDaCatraca> porCatraca)
    {
        var texto = new StringBuilder();
        texto.Append(CultureInfo.InvariantCulture, $"{Iso(desde)}|{Iso(ate)}|{corte:hh\\:mm}\n");
        foreach (var d in dias)
        {
            texto.Append(CultureInfo.InvariantCulture, $"D|{Iso(d.Inicio)}|{d.Distintos}\n");
            foreach (var c in d.Validados.Concat(d.SemPassagem).Concat(d.Negados))
            {
                texto.Append(CultureInfo.InvariantCulture, $"{c.Chave}={c.Quantidade}\n");
            }
        }

        foreach (var h in porHora)
        {
            texto.Append(CultureInfo.InvariantCulture, $"H|{Iso(h.Hora)}|{h.Tipo}={h.Quantidade}\n");
        }

        foreach (var c in porCatraca)
        {
            texto.Append(CultureInfo.InvariantCulture, $"C|{c.Dispositivo}|{c.Validados}|{c.SemPassagem}|{c.Negados}\n");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())))[..16];
        return string.Join('-', hash.Chunk(4).Select(g => new string(g)));
    }

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static TimeZoneInfo Localizar()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception erro) when (erro is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("America/Sao_Paulo", TimeSpan.FromHours(-3), "Horário de Brasília", "Horário de Brasília");
    }

    private sealed record Linha(
        DateTimeOffset Em, string Motivo, string Tipo, string Dispositivo, bool Validado, bool SemPassagem, bool Negado, string? Quem);
}
