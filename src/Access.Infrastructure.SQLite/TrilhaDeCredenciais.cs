using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Access.Domain.Credentials;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>O que aconteceu com o cadastro de um cartão (coluna <c>credential_event.action</c>).</summary>
public enum AcaoSobreCredencial
{
    Criado,
    Editado,
    TipoAlterado,
    Bloqueado,
    Desbloqueado,
    Cancelado,
    Importado,
    ImportacaoDesfeita,
    CustodiaAlterada,
    TitularRevelado,
    Expurgado,
}

/// <summary>
/// Um evento da trilha do cadastro, <b>sem o código</b>: o código entra à parte em
/// <see cref="TrilhaDeCredenciais.Registrar(SqliteConnection, SqliteTransaction, string, EventoDeCredencial)"/>
/// e só sai de lá como máscara e impressão.
/// </summary>
/// <param name="IngressoId">O cartão (<c>ticket.id</c>), quando já existe.</param>
/// <param name="Acao">O que aconteceu.</param>
/// <param name="Autor">Nome digitado, 2 a 80.</param>
/// <param name="Estacao">Conta Windows e máquina, postas pelo serviço (não pela tela).</param>
/// <param name="Em">Quando.</param>
/// <param name="AntesJson">Campos não pessoais antes da mudança (tipo, situação, validade, usos).</param>
/// <param name="DepoisJson">Os mesmos campos depois.</param>
/// <param name="Motivo">5 a 200; obrigatório em bloqueio, desbloqueio e cancelamento.</param>
/// <param name="LoteId">Lote de importação de origem.</param>
public sealed record EventoDeCredencial(
    Guid? IngressoId,
    AcaoSobreCredencial Acao,
    string Autor,
    string Estacao,
    DateTimeOffset Em,
    string? AntesJson = null,
    string? DepoisJson = null,
    string? Motivo = null,
    Guid? LoteId = null);

/// <summary>Um evento lido da trilha. Só a máscara: o número não está lá para ser lido.</summary>
public sealed record RegistroDeCredencial(
    Guid Id,
    Guid? IngressoId,
    string CodigoMascarado,
    AcaoSobreCredencial Acao,
    string Autor,
    DateTimeOffset Em,
    string? Motivo);

/// <summary>
/// Grava e lê a trilha do cadastro de cartões (<c>credential_event</c>, migração 011).
/// </summary>
/// <remarks>
/// <para>
/// Só INSERT: gatilhos recusam UPDATE e DELETE. Cada evento guarda a máscara do código
/// (<see cref="CredentialValue.Mascarar"/>) e a impressão (<see cref="ImpressaoDeCodigo"/>,
/// HMAC com chave fora da base), nunca o número (docs/34 §5.1; docs/35 B.1).
/// </para>
/// <para>
/// Os eventos são encadeados por SHA-256 (padrão do <c>audit_log</c>): cada um carrega o
/// hash do anterior, e um índice único em <c>prev_hash</c> impede a cadeia de bifurcar.
/// <see cref="VerificarCadeia"/> refaz a conta.
/// </para>
/// <para>
/// Quem chama são o cadastro e a importação (Etapas B.4 e B.6, sem conexão, ADR-0025);
/// o evento vai na <b>mesma transação</b> da mudança do cartão.
/// </para>
/// </remarks>
public sealed class TrilhaDeCredenciais
{
    /// <summary>O "anterior" do primeiro evento.</summary>
    public const string Origem = "0000000000000000000000000000000000000000000000000000000000000000";

    // Abaixo disto o código aparece por acaso em datas e contagens (mesmo piso do redator
    // de log e do gatilho da migração 011).
    private const int PisoDaVarredura = 6;

    private readonly SqliteConnectionFactory _fabrica;
    private readonly ImpressaoDeCodigo _impressao;

    public TrilhaDeCredenciais(SqliteConnectionFactory fabrica, ImpressaoDeCodigo impressao)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(impressao);
        _fabrica = fabrica;
        _impressao = impressao;
    }

    /// <summary>Grava um evento numa transação própria.</summary>
    public Guid Registrar(string codigoNormalizado, EventoDeCredencial evento)
    {
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();
        var id = Registrar(conexao, transacao, codigoNormalizado, evento);
        transacao.Commit();
        return id;
    }

    /// <summary>Grava um evento dentro da transação de quem muda o cartão.</summary>
    /// <param name="conexao">Conexão da mudança.</param>
    /// <param name="transacao">Transação da mudança: o evento vale junto com ela, ou não vale.</param>
    /// <param name="codigoNormalizado">O código, já normalizado. Vira máscara e impressão aqui.</param>
    /// <param name="evento">O resto do evento.</param>
    /// <returns>O identificador do evento.</returns>
    public Guid Registrar(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string codigoNormalizado,
        EventoDeCredencial evento)
    {
        ArgumentNullException.ThrowIfNull(conexao);
        ArgumentNullException.ThrowIfNull(transacao);
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoNormalizado);
        ArgumentNullException.ThrowIfNull(evento);

        RecusarCodigoEmClaro(codigoNormalizado, evento);

        var id = Guid.CreateVersion7(evento.Em);
        var linha = new[]
        {
            id.ToString(),
            evento.IngressoId?.ToString(),
            CredentialValue.Mascarar(codigoNormalizado),
            _impressao.De(codigoNormalizado),
            _impressao.IdDaChave,
            NomeDa(evento.Acao),
            evento.AntesJson,
            evento.DepoisJson,
            evento.Motivo,
            evento.Autor,
            evento.Estacao,
            evento.LoteId?.ToString(),
            evento.Em.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };

        var anterior = UltimoHash(conexao, transacao);
        var hash = Encadear(anterior, linha);

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO credential_event
                (id, ticket_id, code_masked, code_hmac, code_key_id, action, before_json, after_json,
                 reason, actor, workstation, batch_id, at, prev_hash, hash)
            VALUES
                ($id, $ticket, $mascara, $impressao, $chave, $acao, $antes, $depois,
                 $motivo, $autor, $estacao, $lote, $em, $anterior, $hash);
            """;
        string[] nomes = ["$id", "$ticket", "$mascara", "$impressao", "$chave", "$acao", "$antes", "$depois",
            "$motivo", "$autor", "$estacao", "$lote", "$em"];
        for (var i = 0; i < nomes.Length; i++)
        {
            comando.Parameters.AddWithValue(nomes[i], (object?)linha[i] ?? DBNull.Value);
        }

        comando.Parameters.AddWithValue("$anterior", anterior);
        comando.Parameters.AddWithValue("$hash", hash);
        comando.ExecuteNonQuery();
        return id;
    }

    /// <summary>
    /// O histórico de um código, achado pela impressão. A resposta traz só a máscara.
    /// </summary>
    /// <remarks>
    /// Acha só os eventos calculados com a chave atual: um evento de chave destruída
    /// (expurgo) deixou de ser ligável a qualquer código, que é o propósito do expurgo.
    /// </remarks>
    public IReadOnlyList<RegistroDeCredencial> Historico(string codigoNormalizado)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoNormalizado);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, ticket_id, code_masked, action, actor, at, reason
            FROM credential_event
            WHERE code_hmac = $impressao AND code_key_id = $chave
            ORDER BY at, rowid;
            """;
        comando.Parameters.AddWithValue("$impressao", _impressao.De(codigoNormalizado));
        comando.Parameters.AddWithValue("$chave", _impressao.IdDaChave);

        var historico = new List<RegistroDeCredencial>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            historico.Add(new RegistroDeCredencial(
                Guid.Parse(leitor.GetString(0)),
                leitor.IsDBNull(1) ? null : Guid.Parse(leitor.GetString(1)),
                leitor.GetString(2),
                AcaoDe(leitor.GetString(3)),
                leitor.GetString(4),
                DateTimeOffset.Parse(leitor.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                leitor.IsDBNull(6) ? null : leitor.GetString(6)));
        }

        return historico;
    }

    /// <summary>
    /// Refaz o encadeamento do primeiro ao último evento.
    /// </summary>
    /// <returns>Nulo quando a cadeia fecha; senão, o identificador do primeiro evento que não bate.</returns>
    public static string? VerificarCadeia(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        using var conexao = fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, ticket_id, code_masked, code_hmac, code_key_id, action, before_json, after_json,
                   reason, actor, workstation, batch_id, at, prev_hash, hash
            FROM credential_event
            ORDER BY rowid;
            """;

        var esperado = Origem;
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            var linha = new string?[13];
            for (var i = 0; i < linha.Length; i++)
            {
                linha[i] = leitor.IsDBNull(i) ? null : leitor.GetString(i);
            }

            var anterior = leitor.GetString(13);
            var hash = leitor.GetString(14);

            if (!string.Equals(anterior, esperado, StringComparison.Ordinal)
                || !string.Equals(hash, Encadear(anterior, linha), StringComparison.Ordinal))
            {
                return linha[0];
            }

            esperado = hash;
        }

        return null;
    }

    private static string UltimoHash(SqliteConnection conexao, SqliteTransaction transacao)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;

        // rowid cresce e nunca é reaproveitado aqui: a tabela não aceita DELETE.
        comando.CommandText = "SELECT hash FROM credential_event ORDER BY rowid DESC LIMIT 1;";
        return comando.ExecuteScalar() as string ?? Origem;
    }

    // Cada campo entra com o tamanho na frente: "ab"+"c" e "a"+"bc" não podem dar o mesmo hash.
    private static string Encadear(string anterior, IReadOnlyList<string?> campos)
    {
        var texto = new StringBuilder(anterior);
        foreach (var campo in campos)
        {
            texto.Append('\n');
            texto.Append(campo is null ? "-" : string.Create(CultureInfo.InvariantCulture, $"{campo.Length}:{campo}"));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())));
    }

    // O gatilho da migração faz o mesmo quando o cartão já existe; aqui vale também para o
    // evento sem ticket. A mensagem nunca repete o código.
    private static void RecusarCodigoEmClaro(string codigo, EventoDeCredencial evento)
    {
        if (codigo.Length < PisoDaVarredura)
        {
            return;
        }

        foreach (var texto in new[] { evento.AntesJson, evento.DepoisJson, evento.Motivo, evento.Autor, evento.Estacao })
        {
            if (texto is not null && texto.Contains(codigo, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"O evento da trilha contém o código do cartão ({CredentialValue.Mascarar(codigo)}) em claro.",
                    nameof(evento));
            }
        }
    }

    private static string NomeDa(AcaoSobreCredencial acao) => acao switch
    {
        AcaoSobreCredencial.Criado => "criado",
        AcaoSobreCredencial.Editado => "editado",
        AcaoSobreCredencial.TipoAlterado => "tipo_alterado",
        AcaoSobreCredencial.Bloqueado => "bloqueado",
        AcaoSobreCredencial.Desbloqueado => "desbloqueado",
        AcaoSobreCredencial.Cancelado => "cancelado",
        AcaoSobreCredencial.Importado => "importado",
        AcaoSobreCredencial.ImportacaoDesfeita => "importacao_desfeita",
        AcaoSobreCredencial.CustodiaAlterada => "custodia_alterada",
        AcaoSobreCredencial.TitularRevelado => "titular_revelado",
        AcaoSobreCredencial.Expurgado => "expurgado",
        _ => throw new ArgumentOutOfRangeException(nameof(acao), acao, "Ação sem nome na trilha."),
    };

    private static AcaoSobreCredencial AcaoDe(string nome) =>
        Enum.GetValues<AcaoSobreCredencial>().First(a => string.Equals(NomeDa(a), nome, StringComparison.Ordinal));
}
