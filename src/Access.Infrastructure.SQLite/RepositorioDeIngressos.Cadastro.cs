using System.Globalization;
using System.Text.RegularExpressions;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// Cadastro de cartões na própria operação: a sessão de lote (migração 026) e o cadastro de um cartão
/// recusado como desconhecido.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum método daqui devolve o código do cartão. A lista de recusados mostra a máscara, e o cadastro
/// recebe o identificador da TENTATIVA: o código é lido do banco pelo serviço, nunca cruza a rede até o
/// painel. Ver docs/35 B.5 ("a resposta só traz códigos mascarados").
/// </para>
/// <para>
/// Todo cartão criado aqui vai com <c>source='manual'</c>, <c>owner_of_fields='local'</c> (ADR-0025: quem
/// mudou por último) e com o operador em <c>created_by</c>. Cadastro sem conexão só: a regra de conexão
/// fica na camada que chama.
/// </para>
/// </remarks>
public sealed partial class RepositorioDeIngressos
{
    private static readonly Regex CategoriaValida = new("^[A-Z0-9_]{2,20}$", RegexOptions.CultureInvariant);

    /// <summary>Resultado de abrir uma sessão de cadastro por leitura.</summary>
    public enum ResultadoDaAberturaDeSessao
    {
        Aberta,
        JaAberta,
        ProvedorDesconhecido,
        ProvedorNaoReutilizavel,
        DadosInvalidos,
    }

    /// <summary>Resultado de cadastrar um cartão recusado.</summary>
    public enum ResultadoDoCadastroDeCartao
    {
        Cadastrado,
        JaCadastrado,
        TentativaNaoEncontrada,
        ProvedorDesconhecido,
        ProvedorNaoReutilizavel,
        DadosInvalidos,
    }

    /// <summary>A sessão de cadastro por leitura, como a base a guarda. Sem código de cartão.</summary>
    public sealed record SessaoDeLote(
        Guid Id,
        string ProvedorId,
        string Categoria,
        string Lote,
        int Usos,
        string Operador,
        DateTimeOffset AbertaEm,
        int Cadastrados);

    /// <summary>Um cartão recusado como desconhecido, pela máscara. <paramref name="TentativaId"/> é a última tentativa dele.</summary>
    public sealed record CartaoRecusado(
        Guid TentativaId,
        string CodigoMascarado,
        int Vezes,
        DateTimeOffset UltimaVez);

    /// <summary>Abre a sessão de cadastro por leitura. Só uma por vez.</summary>
    public ResultadoDaAberturaDeSessao AbrirSessaoDeCadastro(
        string provedorId,
        string categoria,
        string lote,
        string operador,
        int usos,
        DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedorId);
        if (!DadosDaSessaoValidos(categoria, lote, operador, usos))
        {
            return ResultadoDaAberturaDeSessao.DadosInvalidos;
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var perfil = PerfilDoProvedor(conexao, provedorId, transacao);
        if (perfil is null)
        {
            return ResultadoDaAberturaDeSessao.ProvedorDesconhecido;
        }

        if (!perfil.Value.Reutilizavel)
        {
            return ResultadoDaAberturaDeSessao.ProvedorNaoReutilizavel;
        }

        if (SessaoAberta(conexao, transacao) is not null)
        {
            return ResultadoDaAberturaDeSessao.JaAberta;
        }

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO enrollment_session
                (id, provider_id, category, batch_label, max_uses, operator, opened_at, registered, aberta)
            VALUES ($id, $provedor, $categoria, $lote, $usos, $operador, $em, 0, 1);
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
        comando.Parameters.AddWithValue("$provedor", provedorId);
        comando.Parameters.AddWithValue("$categoria", categoria);
        comando.Parameters.AddWithValue("$lote", lote.Trim());
        comando.Parameters.AddWithValue("$usos", usos);
        comando.Parameters.AddWithValue("$operador", operador.Trim());
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.ExecuteNonQuery();

        transacao.Commit();
        return ResultadoDaAberturaDeSessao.Aberta;
    }

    /// <summary>A sessão aberta agora, ou nulo.</summary>
    public SessaoDeLote? SessaoDeCadastroAberta()
    {
        using var conexao = _fabrica.Abrir();
        return SessaoAberta(conexao, transacao: null);
    }

    /// <summary>Fecha a sessão aberta e devolve o que ela era. Nulo se não havia nenhuma.</summary>
    public SessaoDeLote? FecharSessaoDeCadastro(DateTimeOffset agora)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var sessao = SessaoAberta(conexao, transacao);
        if (sessao is null)
        {
            transacao.Commit();
            return null;
        }

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            UPDATE enrollment_session SET aberta = 0, closed_at = $em WHERE id = $id AND aberta = 1;
            """;
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$id", sessao.Id.ToString());
        comando.ExecuteNonQuery();

        transacao.Commit();
        return sessao;
    }

    /// <summary>
    /// Cartões recusados como desconhecidos que ainda não existem na base, mais recentes primeiro.
    /// Pela máscara: o código nunca sai daqui.
    /// </summary>
    public IReadOnlyList<CartaoRecusado> CartoesNaoReconhecidos(int limite = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT MAX(t.id), t.qr_normalized, COUNT(*), MAX(t.at)
            FROM ticket_use_attempt t
            WHERE t.ticket_id IS NULL
              AND t.person_id IS NULL
              AND t.reason = 'Desconhecido'
              AND NOT EXISTS (SELECT 1 FROM ticket k WHERE k.qr_normalized = t.qr_normalized)
            GROUP BY t.qr_normalized
            ORDER BY MAX(t.at) DESC
            LIMIT $limite;
            """;
        comando.Parameters.AddWithValue("$limite", limite);

        var lista = new List<CartaoRecusado>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new CartaoRecusado(
                Guid.Parse(leitor.GetString(0)),
                CredentialValue.Mascarar(leitor.GetString(1)),
                (int)leitor.GetInt64(2),
                DateTimeOffset.Parse(leitor.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return lista;
    }

    /// <summary>
    /// Cadastra o cartão de uma tentativa recusada como desconhecida. O código vem do banco, não do painel.
    /// </summary>
    public (ResultadoDoCadastroDeCartao Resultado, Guid? IngressoId) CadastrarNaoReconhecido(
        Guid tentativaId,
        string provedorId,
        string categoria,
        string lote,
        string operador,
        int usos,
        DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedorId);
        if (!DadosDoCadastroValidos(categoria, lote, operador, usos))
        {
            return (ResultadoDoCadastroDeCartao.DadosInvalidos, null);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var perfil = PerfilDoProvedor(conexao, provedorId, transacao);
        if (perfil is null)
        {
            return (ResultadoDoCadastroDeCartao.ProvedorDesconhecido, null);
        }

        if (!perfil.Value.Reutilizavel)
        {
            return (ResultadoDoCadastroDeCartao.ProvedorNaoReutilizavel, null);
        }

        using var busca = conexao.CreateCommand();
        busca.Transaction = transacao;
        busca.CommandText =
            """
            SELECT qr_normalized FROM ticket_use_attempt
            WHERE id = $id AND ticket_id IS NULL AND person_id IS NULL AND reason = 'Desconhecido';
            """;
        busca.Parameters.AddWithValue("$id", tentativaId.ToString());
        var codigo = busca.ExecuteScalar() as string;
        if (codigo is null)
        {
            return (ResultadoDoCadastroDeCartao.TentativaNaoEncontrada, null);
        }

        if (Estado(conexao, transacao, codigo) is not null)
        {
            return (ResultadoDoCadastroDeCartao.JaCadastrado, null);
        }

        var id = InserirCartao(conexao, transacao, provedorId, codigo, categoria, lote, usos, operador, agora);
        transacao.Commit();
        return (ResultadoDoCadastroDeCartao.Cadastrado, id);
    }

    private static bool DadosDaSessaoValidos(string categoria, string lote, string operador, int usos) =>
        DadosDoCadastroValidos(categoria, lote, operador, usos);

    private static bool DadosDoCadastroValidos(string categoria, string lote, string operador, int usos) =>
        categoria is not null && CategoriaValida.IsMatch(categoria)
        && lote is not null && lote.Trim().Length is >= 1 and <= 40
        && operador is not null && operador.Trim().Length is >= 2 and <= 80
        && usos is >= 1 and <= 50;

    private static SessaoDeLote? SessaoAberta(SqliteConnection conexao, SqliteTransaction? transacao)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT id, provider_id, category, batch_label, max_uses, operator, opened_at, registered
            FROM enrollment_session WHERE aberta = 1;
            """;

        using var leitor = comando.ExecuteReader();
        if (!leitor.Read())
        {
            return null;
        }

        return new SessaoDeLote(
            Guid.Parse(leitor.GetString(0)),
            leitor.GetString(1),
            leitor.GetString(2),
            leitor.GetString(3),
            (int)leitor.GetInt64(4),
            leitor.GetString(5),
            DateTimeOffset.Parse(leitor.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            (int)leitor.GetInt64(7));
    }

    /// <summary>O cartão, se existe na base, com o lote de importação que o criou (nulo se não tem).</summary>
    private static (Guid Id, string Provedor, string? Lote)? CartaoComLote(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string codigo)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT id, provider_id, batch_label FROM ticket WHERE qr_normalized = $codigo;";
        comando.Parameters.AddWithValue("$codigo", codigo);

        using var leitor = comando.ExecuteReader();
        if (!leitor.Read())
        {
            return null;
        }

        return (Guid.Parse(leitor.GetString(0)), leitor.GetString(1), leitor.IsDBNull(2) ? null : leitor.GetString(2));
    }

    private static void ContarCadastroNaSessao(SqliteConnection conexao, SqliteTransaction transacao, Guid sessaoId)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "UPDATE enrollment_session SET registered = registered + 1 WHERE id = $id;";
        comando.Parameters.AddWithValue("$id", sessaoId.ToString());
        comando.ExecuteNonQuery();
    }

    /// <summary>Cria o cartão no provedor local. Devolve o identificador do ingresso criado.</summary>
    private static Guid InserirCartao(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string provedorId,
        string codigo,
        string categoria,
        string lote,
        int usos,
        string operador,
        DateTimeOffset agora)
    {
        var id = Guid.CreateVersion7(agora);
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO ticket
                (id, provider_id, external_ref, qr_raw, qr_normalized, max_uses, used_count, status, ingested_at,
                 category, kind, source, owner_of_fields, created_by, batch_label)
            VALUES
                ($id, $provedor, $codigo, $codigo, $codigo, $usos, 0, 'valido', $em,
                 $categoria, 'cartao_bilheteria', 'manual', 'local', $operador, $lote);
            """;
        comando.Parameters.AddWithValue("$id", id.ToString());
        comando.Parameters.AddWithValue("$provedor", provedorId);
        comando.Parameters.AddWithValue("$codigo", codigo);
        comando.Parameters.AddWithValue("$usos", usos);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$categoria", categoria);
        comando.Parameters.AddWithValue("$operador", operador.Trim());
        comando.Parameters.AddWithValue("$lote", lote.Trim());
        comando.ExecuteNonQuery();
        return id;
    }
}
