using System.Globalization;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// O evento em si (docs/57 §4): nome, período e hora de corte do dia de operação.
/// </summary>
/// <param name="Nome">Como o evento aparece no painel e nos relatórios. Até 80 letras; vazio é permitido.</param>
/// <param name="Inicio">Quando o evento começa. Nulo = sem início definido.</param>
/// <param name="Fim">Quando o evento termina. Nulo = sem fim definido.</param>
/// <param name="Corte">Hora, no horário de Brasília, em que um dia de operação vira o seguinte (E9).</param>
/// <param name="RecusarForaDoPeriodo">
/// Nega ingresso e cartão da bilheteria lidos antes do início ou depois do fim. Pessoas
/// credenciadas não passam por esta regra (têm os horários delas).
/// </param>
public sealed record EventoConfigurado(
    string Nome = "",
    DateTimeOffset? Inicio = null,
    DateTimeOffset? Fim = null,
    TimeSpan Corte = default,
    bool RecusarForaDoPeriodo = false)
{
    /// <summary>O maior nome aceito.</summary>
    public const int MaiorNome = 80;

    /// <summary>O que impede gravar, em português de portaria.</summary>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        if (Nome.Trim().Length > MaiorNome)
        {
            problemas.Add($"O nome do evento vai até {MaiorNome} letras.");
        }

        if (Inicio is { } inicio && Fim is { } fim && fim <= inicio)
        {
            problemas.Add("O fim do evento precisa ser depois do início.");
        }

        if (Corte < TimeSpan.Zero || Corte >= TimeSpan.FromDays(1) || Corte.Seconds != 0 || Corte.Milliseconds != 0)
        {
            problemas.Add("A hora de corte vai de 00:00 a 23:59.");
        }

        if (RecusarForaDoPeriodo && Inicio is null && Fim is null)
        {
            problemas.Add("Para recusar fora do período, informe o início ou o fim do evento.");
        }

        return problemas;
    }

    /// <summary>Verdadeiro quando a recusa está ligada e o instante cai fora do período.</summary>
    public bool ForaDoPeriodo(DateTimeOffset agora) =>
        RecusarForaDoPeriodo && ((Inicio is { } inicio && agora < inicio) || (Fim is { } fim && agora > fim));
}

public sealed partial class ConfiguracoesDaBorda
{
    public const string ChaveEventoNome = "evento.nome";
    public const string ChaveEventoInicio = "evento.inicio";
    public const string ChaveEventoFim = "evento.fim";
    public const string ChaveEventoCorte = "evento.corte";
    public const string ChaveEventoRecusarForaDoPeriodo = "evento.recusar_fora_do_periodo";

    /// <summary>O evento gravado, e quem mudou por último e quando. Valor ilegível volta ao padrão.</summary>
    public (EventoConfigurado Evento, string? AlteradoPor, DateTimeOffset? AlteradoEm) LerEvento()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT key, value, updated_at, updated_by FROM edge_setting
            WHERE key IN ($nome, $inicio, $fim, $corte, $recusar);
            """;
        comando.Parameters.AddWithValue("$nome", ChaveEventoNome);
        comando.Parameters.AddWithValue("$inicio", ChaveEventoInicio);
        comando.Parameters.AddWithValue("$fim", ChaveEventoFim);
        comando.Parameters.AddWithValue("$corte", ChaveEventoCorte);
        comando.Parameters.AddWithValue("$recusar", ChaveEventoRecusarForaDoPeriodo);

        var valores = new Dictionary<string, string>(StringComparer.Ordinal);
        string? por = null;
        DateTimeOffset? em = null;
        using (var leitor = comando.ExecuteReader())
        {
            while (leitor.Read())
            {
                valores[leitor.GetString(0)] = leitor.GetString(1);
                if (DateTimeOffset.TryParse(leitor.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.None, out var quando)
                    && (em is null || quando > em))
                {
                    em = quando;
                    por = leitor.IsDBNull(3) ? null : leitor.GetString(3);
                }
            }
        }

        return (EventoDe(valores), por, em);
    }

    /// <summary>Grava o evento inteiro, de uma vez, com quem mudou. Devolve o que impediu gravar.</summary>
    public IReadOnlyList<string> GravarEvento(EventoConfigurado evento, DateTimeOffset agora, string quem)
    {
        ArgumentNullException.ThrowIfNull(evento);

        var problemas = evento.Validar();
        if (problemas.Count > 0)
        {
            return problemas;
        }

        GravarPares(
            new Dictionary<string, string>
            {
                [ChaveEventoNome] = evento.Nome.Trim(),
                [ChaveEventoInicio] = Instante(evento.Inicio),
                [ChaveEventoFim] = Instante(evento.Fim),
                [ChaveEventoCorte] = evento.Corte.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                [ChaveEventoRecusarForaDoPeriodo] = evento.RecusarForaDoPeriodo ? "1" : "0",
            },
            agora,
            quem);
        return [];
    }

    /// <summary>
    /// O evento a partir das chaves de <c>edge_setting</c>. Usado também pela decisão na catraca,
    /// que lê as chaves dentro da própria transação.
    /// </summary>
    public static EventoConfigurado EventoDe(IReadOnlyDictionary<string, string> valores)
    {
        ArgumentNullException.ThrowIfNull(valores);

        static DateTimeOffset? Ler(IReadOnlyDictionary<string, string> v, string chave) =>
            v.TryGetValue(chave, out var texto) && texto.Length > 0
            && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instante)
                ? instante
                : null;

        var corte = valores.TryGetValue(ChaveEventoCorte, out var c)
                    && TimeSpan.TryParseExact(c, @"hh\:mm", CultureInfo.InvariantCulture, out var hora)
            ? hora
            : TimeSpan.Zero;

        var evento = new EventoConfigurado(
            Nome: valores.GetValueOrDefault(ChaveEventoNome, string.Empty),
            Inicio: Ler(valores, ChaveEventoInicio),
            Fim: Ler(valores, ChaveEventoFim),
            Corte: corte,
            RecusarForaDoPeriodo: valores.GetValueOrDefault(ChaveEventoRecusarForaDoPeriodo) == "1");

        // Gravado por fora e inconsistente (fim antes do início): a recusa fica desligada. Negar todo
        // mundo por um erro de digitação é pior que deixar o período sem efeito.
        return evento.Validar().Count == 0 ? evento : evento with { RecusarForaDoPeriodo = false };
    }

    /// <summary>Identificador desta instalação (criado na primeira vez que alguém pede).</summary>
    public const string ChaveIdDaInstalacao = "instalacao.id";

    /// <summary>
    /// O identificador aleatório desta instalação: amarra um pedido de recuperação a esta máquina
    /// (docs/58). Criado na primeira vez e nunca mudado.
    /// </summary>
    public string IdDaInstalacao(DateTimeOffset agora)
    {
        using (var conexao = _fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "INSERT OR IGNORE INTO edge_setting (key, value, updated_at, updated_by) VALUES ($k, $v, $em, 'servico');";
            comando.Parameters.AddWithValue("$k", ChaveIdDaInstalacao);
            comando.Parameters.AddWithValue("$v", Guid.NewGuid().ToString("N"));
            comando.Parameters.AddWithValue("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            comando.ExecuteNonQuery();
        }

        return Todos()[ChaveIdDaInstalacao];
    }

    /// <summary>A última mudança das regras da bilheteria local, com quem mudou (a trilha do ajuste pelo painel).</summary>
    public const string ChaveRegrasDaBilheteria = "bilheteria.regras";

    /// <summary>Registra o ajuste das regras da bilheteria local feito pelo painel.</summary>
    public void RegistrarAjusteDaBilheteria(string regras, DateTimeOffset agora, string quem) =>
        GravarPares(new Dictionary<string, string> { [ChaveRegrasDaBilheteria] = regras }, agora, quem);

    private static string Instante(DateTimeOffset? valor) =>
        valor?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
}
