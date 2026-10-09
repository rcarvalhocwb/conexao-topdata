using System.Globalization;
using Access.Domain.Tempo;
using Access.Domain.Ticketing;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// A decisão da catraca para o cadastro local de pessoas e para a catraca fechada (docs/43 §6, migração 021).
/// </summary>
/// <remarks>
/// <para>
/// Roda dentro da transação de <see cref="RepositorioDeIngressos.TentarUsar"/>: ler a situação e gravar a
/// tentativa não têm "entre", então bloquear uma pessoa vale já na leitura seguinte.
/// </para>
/// <para>
/// Não lê dado pessoal: nome, documento e contato ficam cifrados e a decisão não precisa deles.
/// </para>
/// </remarks>
internal static class DecisaoDePessoas
{
    /// <summary>Prefixo do identificador do equipamento (<c>inner-{número}</c>).</summary>
    public const string PrefixoDoEquipamento = "inner-";

    /// <summary>Por quanto tempo uma liberação sem giro ainda conta no limite do dia.</summary>
    public static readonly TimeSpan EsperaPeloGiro = TimeSpan.FromMinutes(2);

    /// <summary>A credencial de pessoa encontrada, com o que a decisão consulta.</summary>
    /// <param name="PessoaId">A pessoa.</param>
    /// <param name="Perfil">O nome do perfil (vai como categoria na tentativa).</param>
    /// <param name="SituacaoDaPessoa">ativo, bloqueado ou inativo.</param>
    /// <param name="SituacaoDaCredencial">ativa, bloqueada, perdida ou devolvida.</param>
    /// <param name="PessoaDe">Início da validade da pessoa.</param>
    /// <param name="PessoaAte">Fim da validade da pessoa.</param>
    /// <param name="CredencialDe">Início da validade da credencial.</param>
    /// <param name="CredencialAte">Fim da validade da credencial.</param>
    /// <param name="TabelaDeHorario">A tabela da pessoa ou, sem ela, a do perfil.</param>
    /// <param name="LimiteDiario">O limite da pessoa ou, sem ele, o do perfil.</param>
    /// <param name="PerfilId">O perfil, para os portões permitidos.</param>
    public sealed record CredencialDePessoa(
        string PessoaId,
        string Perfil,
        string SituacaoDaPessoa,
        string SituacaoDaCredencial,
        DateTimeOffset? PessoaDe,
        DateTimeOffset? PessoaAte,
        DateTimeOffset? CredencialDe,
        DateTimeOffset? CredencialAte,
        int? TabelaDeHorario,
        int? LimiteDiario,
        string PerfilId);

    /// <summary>O número do Inner a partir do identificador do equipamento; nulo se não for <c>inner-N</c>.</summary>
    public static int? NumeroDoInner(string deviceId) =>
        deviceId.StartsWith(PrefixoDoEquipamento, StringComparison.Ordinal)
        && int.TryParse(deviceId.AsSpan(PrefixoDoEquipamento.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var numero)
            ? numero
            : null;

    /// <summary>Verdadeiro se o operador fechou esta catraca.</summary>
    public static bool CatracaFechada(SqliteConnection conexao, SqliteTransaction transacao, int? inner)
    {
        if (inner is not { } numero)
        {
            return false;
        }

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT 1 FROM gate_closure WHERE inner_number = $inner;";
        comando.Parameters.AddWithValue("$inner", numero);
        return comando.ExecuteScalar() is not null;
    }

    /// <summary>A credencial de pessoa com este código; nula se não houver.</summary>
    public static CredencialDePessoa? Credencial(SqliteConnection conexao, SqliteTransaction transacao, string codigo)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT p.id, pr.name, p.status, c.status, p.valid_from, p.valid_to, c.valid_from, c.valid_to,
                   COALESCE(p.schedule_id, pr.schedule_id), COALESCE(p.daily_limit, pr.daily_limit), pr.id
            FROM person_credential c
            JOIN person p ON p.id = c.person_id
            JOIN person_profile pr ON pr.id = p.profile_id
            WHERE c.value_normalized = $codigo;
            """;
        comando.Parameters.AddWithValue("$codigo", codigo);

        using var leitor = comando.ExecuteReader();
        if (!leitor.Read())
        {
            return null;
        }

        return new CredencialDePessoa(
            leitor.GetString(0),
            leitor.GetString(1),
            leitor.GetString(2),
            leitor.GetString(3),
            Instante(leitor, 4),
            Instante(leitor, 5),
            Instante(leitor, 6),
            Instante(leitor, 7),
            leitor.IsDBNull(8) ? null : leitor.GetInt32(8),
            leitor.IsDBNull(9) ? null : leitor.GetInt32(9),
            leitor.GetString(10));
    }

    /// <summary>
    /// Decide sobre a credencial de pessoa: <see cref="MotivoDoUso.Consumido"/> libera; qualquer outro nega.
    /// </summary>
    /// <remarks>
    /// A ordem explica a negativa a quem está na frente da catraca: primeiro a situação da pessoa, depois a
    /// da credencial, a validade, a catraca, o horário e, por último, o limite do dia.
    /// </remarks>
    /// <param name="conexao">A conexão da decisão.</param>
    /// <param name="transacao">A transação da decisão.</param>
    /// <param name="credencial">A credencial encontrada.</param>
    /// <param name="inner">O número da catraca; nulo quando o equipamento não é um Inner (bancada).</param>
    /// <param name="agora">O instante da leitura.</param>
    public static MotivoDoUso Decidir(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        CredencialDePessoa credencial,
        int? inner,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(credencial);

        if (credencial.SituacaoDaPessoa == "inativo")
        {
            return MotivoDoUso.PessoaInativa;
        }

        if (credencial.SituacaoDaPessoa != "ativo")
        {
            return MotivoDoUso.PessoaBloqueada;
        }

        if (credencial.SituacaoDaCredencial != "ativa")
        {
            return MotivoDoUso.CredencialInativa;
        }

        if (ForaDe(credencial.PessoaDe, credencial.PessoaAte, agora) || ForaDe(credencial.CredencialDe, credencial.CredencialAte, agora))
        {
            return MotivoDoUso.ForaDaValidade;
        }

        if (!CatracaPermitida(conexao, transacao, credencial, inner))
        {
            return MotivoDoUso.PortaoNaoPermitido;
        }

        if (credencial.TabelaDeHorario is { } tabela && !DentroDoHorario(conexao, transacao, tabela, agora))
        {
            return MotivoDoUso.ForaDoHorario;
        }

        if (credencial.LimiteDiario is { } limite && UsosDoDia(conexao, transacao, credencial.PessoaId, agora) >= limite)
        {
            return MotivoDoUso.LimiteDiario;
        }

        return MotivoDoUso.Consumido;
    }

    /// <summary>O início do dia de Brasília que contém o instante.</summary>
    public static DateTimeOffset InicioDoDia(DateTimeOffset agora) =>
        HoraDeBrasilia.DoEvento(HoraDeBrasilia.NoEvento(agora).Date);

    private static bool ForaDe(DateTimeOffset? de, DateTimeOffset? ate, DateTimeOffset agora) =>
        (de is { } inicio && agora < inicio) || (ate is { } fim && agora > fim);

    // Portões da pessoa, se ela tiver; senão os do perfil; sem nenhum dos dois, todas as catracas.
    private static bool CatracaPermitida(SqliteConnection conexao, SqliteTransaction transacao, CredencialDePessoa credencial, int? inner)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM person_gate WHERE person_id = $pessoa)
                    THEN EXISTS (SELECT 1 FROM person_gate WHERE person_id = $pessoa AND inner_number = $inner)
                WHEN EXISTS (SELECT 1 FROM person_profile_gate WHERE profile_id = $perfil)
                    THEN EXISTS (SELECT 1 FROM person_profile_gate WHERE profile_id = $perfil AND inner_number = $inner)
                ELSE 1
            END;
            """;
        comando.Parameters.AddWithValue("$pessoa", credencial.PessoaId);
        comando.Parameters.AddWithValue("$perfil", credencial.PerfilId);

        // Equipamento que não é Inner (bancada) só passa quando não há restrição de catraca.
        comando.Parameters.AddWithValue("$inner", (object?)inner ?? DBNull.Value);
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    // Dia da semana (0 = domingo) ou 7 em feriado, minuto do dia em Brasília; a faixa vale de início a fim exclusivo.
    private static bool DentroDoHorario(SqliteConnection conexao, SqliteTransaction transacao, int tabela, DateTimeOffset agora)
    {
        var local = HoraDeBrasilia.NoEvento(agora);
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM time_schedule_slot
                WHERE schedule_id = $tabela
                  AND weekday = CASE WHEN EXISTS (SELECT 1 FROM holiday WHERE day = $dia) THEN 7 ELSE $semana END
                  AND start_min <= $minuto AND $minuto < end_min);
            """;
        comando.Parameters.AddWithValue("$tabela", tabela);
        comando.Parameters.AddWithValue("$dia", local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$semana", (int)local.DayOfWeek);
        comando.Parameters.AddWithValue("$minuto", (local.Hour * 60) + local.Minute);
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    // Conta as passagens com giro e as liberações ainda à espera do giro. Liberação que não girou não
    // gasta a entrada do dia depois da espera: a pessoa não passou.
    private static long UsosDoDia(SqliteConnection conexao, SqliteTransaction transacao, string pessoa, DateTimeOffset agora)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT COUNT(*) FROM ticket_use_attempt
            WHERE person_id = $pessoa AND outcome = 'consumido' AND at >= $inicio
              AND (passage_confirmed_at IS NOT NULL OR at >= $semGiroDesde);
            """;
        comando.Parameters.AddWithValue("$pessoa", pessoa);
        comando.Parameters.AddWithValue("$inicio", InicioDoDia(agora).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.Parameters.AddWithValue("$semGiroDesde", (agora - EsperaPeloGiro).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? Instante(SqliteDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna)
            ? null
            : DateTimeOffset.Parse(leitor.GetString(coluna), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
