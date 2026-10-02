using System.Globalization;
using Access.Application.Devices;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>O mapa de giro de uma catraca como está gravado, com quem mudou e quando.</summary>
/// <param name="Inner">Número da catraca.</param>
/// <param name="Mapa">As regras; origem sem regra segue o padrão.</param>
/// <param name="AlteradoPor">Quem gravou por último (nome digitado); nulo se nunca gravado.</param>
/// <param name="AlteradoEm">Quando, em UTC; nulo se nunca gravado.</param>
/// <param name="Problemas">Valores ilegíveis; cada origem com problema volta ao padrão.</param>
public sealed record MapaDeGiroGravado(
    int Inner,
    MapaDeGiro Mapa,
    string? AlteradoPor,
    DateTimeOffset? AlteradoEm,
    IReadOnlyList<string> Problemas);

/// <summary>Uma conferência de sentido feita no comissionamento.</summary>
/// <param name="Inner">Número da catraca.</param>
/// <param name="Funcao">A função liberada na conferência.</param>
/// <param name="ComoEsperado">O braço girou para o lado da seta do gêmeo.</param>
/// <param name="Por">Quem conferiu (nome digitado).</param>
/// <param name="Em">Quando, em UTC.</param>
public sealed record ConferenciaDoGiro(int Inner, FuncaoDeLiberacao Funcao, bool ComoEsperado, string Por, DateTimeOffset Em);

/// <summary>
/// Mapa de giro por catraca (tabelas <c>turn_map_rule</c> e <c>turn_check</c>, migração 017).
/// </summary>
/// <remarks>
/// <para>
/// Decisão D9 do dono do produto (docs/34 §9): o que conta como entrada ou saída é do sistema.
/// Mesmo contrato com a tela que <see cref="ConfiguracoesDasCatracas"/>: <b>nada de exceção</b>
/// para valor inválido — gravar devolve os problemas e não grava nada; ler devolve o valor
/// ilegível como problema e aquela origem volta ao padrão, em vez de derrubar o worker.
/// </para>
/// <para>
/// Gravar troca o mapa inteiro da catraca numa transação: cada origem é uma linha, e a origem
/// sem regra é gravada nula (a 017 não deixa apagar). O histórico é escrito pelo gatilho.
/// </para>
/// <para>
/// A conferência de comissionamento (NOVO-HIL-DIR-11, docs/21) é registro de ensaio, só-INSERT:
/// a última de cada (catraca, função) vale.
/// </para>
/// </remarks>
public sealed class MapasDeGiro
{
    private readonly SqliteConnectionFactory _fabrica;

    /// <param name="fabrica">Conexões com a base local.</param>
    public MapasDeGiro(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Como a origem fica na base.</summary>
    public static string Coluna(OrigemDoGiro origem) => origem switch
    {
        OrigemDoGiro.Leitor1 => "leitor1",
        OrigemDoGiro.Leitor2 => "leitor2",
        OrigemDoGiro.Teclado => "teclado",
        OrigemDoGiro.LiberacaoManual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(origem), origem, "Origem do giro desconhecida."),
    };

    /// <summary>O mapa gravado da catraca. Sem linha, <see cref="MapaDeGiro.Vazio"/>.</summary>
    public MapaDeGiroGravado Ler(int inner)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT origin, release_function, counted_as, display_text, updated_by, updated_at
            FROM turn_map_rule WHERE inner_number = $inner;
            """;
        comando.Parameters.AddWithValue("$inner", inner);

        var mapa = MapaDeGiro.Vazio;
        var problemas = new List<string>();
        string? por = null;
        DateTimeOffset? em = null;

        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            var quando = DateTimeOffset.Parse(leitor.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (em is null || quando > em)
            {
                em = quando;
                por = leitor.GetString(4);
            }

            var origem = MapaDeGiro.Origens.FirstOrDefault(o => Coluna(o) == leitor.GetString(0), (OrigemDoGiro)(-1));
            if (!Enum.IsDefined(origem))
            {
                problemas.Add($"Mapa de giro da catraca {inner}: origem \"{leitor.GetString(0)}\" desconhecida; ignorada.");
                continue;
            }

            if (leitor.IsDBNull(2))
            {
                continue; // linha nula: a origem voltou ao padrão
            }

            var contaComo = leitor.GetString(2) switch
            {
                "entrada" => SentidoContado.Entrada,
                "saida" => SentidoContado.Saida,
                _ => (SentidoContado?)null,
            };

            FuncaoDeLiberacao? funcao = null;
            var funcaoLegivel = true;
            if (!leitor.IsDBNull(1))
            {
                funcaoLegivel = Enum.TryParse<FuncaoDeLiberacao>(leitor.GetString(1), ignoreCase: false, out var lida)
                    && Enum.IsDefined(lida);
                funcao = funcaoLegivel ? lida : null;
            }

            if (contaComo is null || !funcaoLegivel)
            {
                problemas.Add($"Mapa de giro da catraca {inner}, {MapaDeGiro.Nome(origem)}: valor ilegível; segue o padrão.");
                continue;
            }

            mapa = mapa.Com(origem, new RegraDeGiro(funcao, contaComo.Value, leitor.IsDBNull(3) ? null : leitor.GetString(3)));
        }

        return new MapaDeGiroGravado(inner, mapa, por, em, problemas);
    }

    /// <summary>Grava o mapa inteiro da catraca (origem sem regra = padrão), com quem mudou.</summary>
    /// <returns>Os problemas; vazia quando gravou. Com problema, nada é gravado.</returns>
    public IReadOnlyList<string> Gravar(int inner, MapaDeGiro mapa, DateTimeOffset agora, string quem)
    {
        ArgumentNullException.ThrowIfNull(mapa);

        var problemas = new List<string>();
        if (inner is < ConfiguracoesDasCatracas.InnerMinimo or > ConfiguracoesDasCatracas.InnerMaximo)
        {
            problemas.Add($"A catraca vai de {ConfiguracoesDasCatracas.InnerMinimo} a {ConfiguracoesDasCatracas.InnerMaximo}.");
        }

        if (string.IsNullOrWhiteSpace(quem) || quem.Trim().Length < 2)
        {
            problemas.Add("Informe o seu nome: ele fica registrado com a alteração.");
        }

        problemas.AddRange(mapa.Validar());
        if (problemas.Count > 0)
        {
            return problemas;
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        foreach (var origem in MapaDeGiro.Origens)
        {
            var regra = mapa.Regra(origem);

            using var comando = conexao.CreateCommand();
            comando.Transaction = transacao;
            comando.CommandText =
                """
                INSERT INTO turn_map_rule
                    (inner_number, origin, release_function, counted_as, display_text, revision, updated_by, updated_at)
                VALUES ($inner, $origem, $funcao, $conta, $texto, 1, $quem, $em)
                ON CONFLICT (inner_number, origin) DO UPDATE SET
                    release_function = excluded.release_function,
                    counted_as       = excluded.counted_as,
                    display_text     = excluded.display_text,
                    revision         = turn_map_rule.revision + 1,
                    updated_by       = excluded.updated_by,
                    updated_at       = excluded.updated_at
                WHERE turn_map_rule.release_function IS NOT excluded.release_function
                   OR turn_map_rule.counted_as IS NOT excluded.counted_as
                   OR turn_map_rule.display_text IS NOT excluded.display_text;
                """;
            comando.Parameters.AddWithValue("$inner", inner);
            comando.Parameters.AddWithValue("$origem", Coluna(origem));
            comando.Parameters.AddWithValue("$funcao", (object?)regra?.Funcao?.ToString() ?? DBNull.Value);
            comando.Parameters.AddWithValue("$conta", regra is null ? DBNull.Value : (object)RepositorioDeIngressos.RotuloDoGiro(regra.ContaComo));
            comando.Parameters.AddWithValue("$texto", (object?)regra?.Texto ?? DBNull.Value);
            comando.Parameters.AddWithValue("$quem", quem.Trim());
            comando.Parameters.AddWithValue("$em", Iso(agora));
            comando.ExecuteNonQuery();
        }

        transacao.Commit();
        return [];
    }

    /// <summary>Quantas gravações cada origem da catraca teve (o histórico da 017).</summary>
    public int RevisoesNoHistorico(int inner)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM turn_map_rule_history WHERE inner_number = $inner;";
        comando.Parameters.AddWithValue("$inner", inner);
        return Convert.ToInt32(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Registra a conferência de sentido de uma função nesta catraca.</summary>
    /// <returns>Os problemas; vazia quando registrou.</returns>
    public IReadOnlyList<string> RegistrarConferencia(int inner, FuncaoDeLiberacao funcao, bool comoEsperado, DateTimeOffset agora, string quem)
    {
        var problemas = new List<string>();
        if (inner is < ConfiguracoesDasCatracas.InnerMinimo or > ConfiguracoesDasCatracas.InnerMaximo)
        {
            problemas.Add($"A catraca vai de {ConfiguracoesDasCatracas.InnerMinimo} a {ConfiguracoesDasCatracas.InnerMaximo}.");
        }

        if (!Enum.IsDefined(funcao))
        {
            problemas.Add("Função de liberação desconhecida.");
        }

        if (string.IsNullOrWhiteSpace(quem) || quem.Trim().Length < 2)
        {
            problemas.Add("Informe o seu nome: ele fica registrado com a conferência.");
        }

        if (problemas.Count > 0)
        {
            return problemas;
        }

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO turn_check (id, inner_number, release_function, result, checked_by, checked_at)
            VALUES ($id, $inner, $funcao, $resultado, $quem, $em);
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
        comando.Parameters.AddWithValue("$inner", inner);
        comando.Parameters.AddWithValue("$funcao", funcao.ToString());
        comando.Parameters.AddWithValue("$resultado", comoEsperado ? "como_esperado" : "ao_contrario");
        comando.Parameters.AddWithValue("$quem", quem.Trim());
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.ExecuteNonQuery();
        return [];
    }

    /// <summary>A última conferência de cada função desta catraca.</summary>
    public IReadOnlyDictionary<FuncaoDeLiberacao, ConferenciaDoGiro> Conferencias(int inner)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT release_function, result, checked_by, checked_at
            FROM turn_check WHERE inner_number = $inner
            ORDER BY checked_at, id;
            """;
        comando.Parameters.AddWithValue("$inner", inner);

        var ultimas = new Dictionary<FuncaoDeLiberacao, ConferenciaDoGiro>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            if (!Enum.TryParse<FuncaoDeLiberacao>(leitor.GetString(0), ignoreCase: false, out var funcao))
            {
                continue;
            }

            ultimas[funcao] = new ConferenciaDoGiro(
                inner,
                funcao,
                string.Equals(leitor.GetString(1), "como_esperado", StringComparison.Ordinal),
                leitor.GetString(2),
                DateTimeOffset.Parse(leitor.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        return ultimas;
    }

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
