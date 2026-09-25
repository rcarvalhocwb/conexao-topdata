using System.Globalization;
using Access.Domain.Ticketing;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>Um QR que dois provedores diferentes reivindicam.</summary>
/// <param name="QrNormalizado">O valor em conflito.</param>
/// <param name="ProvedorExistente">Quem já tinha o QR.</param>
/// <param name="ReferenciaExistente">Referência do ingresso já gravado.</param>
/// <param name="ProvedorNovo">Quem tentou gravar por cima.</param>
/// <param name="ReferenciaNova">Referência do ingresso recusado.</param>
public sealed record ColisaoDeQr(
    string QrNormalizado,
    string ProvedorExistente,
    string ReferenciaExistente,
    string ProvedorNovo,
    string ReferenciaNova);

/// <summary>O que uma ingestão fez.</summary>
/// <param name="Inseridos">Ingressos novos.</param>
/// <param name="Atualizados">Ingressos que já existiam e foram reescritos.</param>
/// <param name="Colisoes">Recusados por conflito de QR com outro provedor.</param>
/// <param name="ProvedorDesconhecido">Recusados porque o provedor não está cadastrado.</param>
public sealed record ResultadoDaIngestao(
    int Inseridos,
    int Atualizados,
    IReadOnlyList<ColisaoDeQr> Colisoes,
    int ProvedorDesconhecido)
{
    /// <summary>Nada entrou nem foi alterado.</summary>
    public bool Vazia => Inseridos == 0 && Atualizados == 0;
}

/// <summary>Prestação de contas de um provedor.</summary>
/// <param name="ProvedorId">De quem.</param>
/// <param name="IngressosRecebidos">Quantos chegaram na base local.</param>
/// <param name="NuncaUsados">Recebidos e nunca apresentados — o <i>no-show</i>.</param>
/// <param name="Usados">Ingressos com pelo menos um uso consumido.</param>
/// <param name="UsosConsumidos">Total de consumos (um passe de 2 dias usado 2 vezes conta 2).</param>
/// <param name="UsosComPassagemFisica">Consumos com prova de giro (origem 6).</param>
/// <param name="UsosSemPassagemFisica">Consumos sem prova de giro. <b>Precisam de explicação.</b></param>
/// <param name="CanceladosDepoisDeUsados">Cancelamento que chegou depois da entrada. Divergência financeira.</param>
/// <param name="AvisosPendentesDeConfirmacao">Usos que o provedor ainda não confirmou ter recebido.</param>
/// <param name="TentativasNegadas">Negativas por motivo.</param>
public sealed record ConciliacaoDoProvedor(
    string ProvedorId,
    long IngressosRecebidos,
    long NuncaUsados,
    long Usados,
    long UsosConsumidos,
    long UsosComPassagemFisica,
    long UsosSemPassagemFisica,
    long CanceladosDepoisDeUsados,
    long AvisosPendentesDeConfirmacao,
    IReadOnlyDictionary<MotivoDoUso, long> TentativasNegadas)
{
    /// <summary>
    /// Verdadeiro quando não há nada a explicar: todo consumo tem giro, nenhum
    /// cancelamento chegou atrasado e o provedor confirmou todos os avisos.
    /// </summary>
    public bool Fecha =>
        UsosSemPassagemFisica == 0
        && CanceladosDepoisDeUsados == 0
        && AvisosPendentesDeConfirmacao == 0;

    /// <summary>
    /// Compara pelo <b>conteúdo</b>, inclusive o dicionário de negativas.
    /// </summary>
    /// <remarks>
    /// A igualdade que o <c>record</c> gera sozinho compararia o dicionário por
    /// referência, e dois relatórios com exatamente os mesmos números sairiam como
    /// diferentes. Num tipo cujo propósito é ser conferido, arquivado e comparado com a
    /// versão de ontem, isso não é detalhe: é o relatório não sabendo reconhecer a si
    /// mesmo.
    /// </remarks>
    public bool Equals(ConciliacaoDoProvedor? outro) =>
        outro is not null
        && string.Equals(ProvedorId, outro.ProvedorId, StringComparison.Ordinal)
        && IngressosRecebidos == outro.IngressosRecebidos
        && NuncaUsados == outro.NuncaUsados
        && Usados == outro.Usados
        && UsosConsumidos == outro.UsosConsumidos
        && UsosComPassagemFisica == outro.UsosComPassagemFisica
        && UsosSemPassagemFisica == outro.UsosSemPassagemFisica
        && CanceladosDepoisDeUsados == outro.CanceladosDepoisDeUsados
        && AvisosPendentesDeConfirmacao == outro.AvisosPendentesDeConfirmacao
        && TentativasNegadas.Count == outro.TentativasNegadas.Count
        && TentativasNegadas.All(par =>
            outro.TentativasNegadas.TryGetValue(par.Key, out var valor) && valor == par.Value);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProvedorId, StringComparer.Ordinal);
        hash.Add(IngressosRecebidos);
        hash.Add(NuncaUsados);
        hash.Add(Usados);
        hash.Add(UsosConsumidos);
        hash.Add(UsosComPassagemFisica);
        hash.Add(UsosSemPassagemFisica);
        hash.Add(CanceladosDepoisDeUsados);
        hash.Add(AvisosPendentesDeConfirmacao);

        // Ordenado, porque a ordem de enumeração de um dicionário não é contratual.
        foreach (var par in TentativasNegadas.OrderBy(p => p.Key))
        {
            hash.Add(par.Key);
            hash.Add(par.Value);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Ingressos de múltiplos provedores, na base local.
/// </summary>
/// <remarks>
/// <para>
/// Três decisões sustentam este repositório, e todas as três existem por causa de um
/// evento com bilheteria de mais de um fornecedor:
/// </para>
/// <list type="number">
/// <item>
/// <b>O QR é único no evento inteiro, não por provedor.</b> A catraca lê uma string e
/// nada mais — ela não sabe de quem é o ingresso. Colisão entre provedores é recusada na
/// ingestão, com hora e culpado.
/// </item>
/// <item>
/// <b>O consumo é uma única instrução SQL.</b> Duas catracas lendo o mesmo QR no mesmo
/// instante têm exatamente um vencedor, sem trava distribuída e sem consultar ninguém.
/// </item>
/// <item>
/// <b>Toda tentativa vira linha</b>, inclusive a negada e inclusive o QR desconhecido.
/// Sem isso não existe prestação de contas — só se sabe quem entrou, nunca quem tentou.
/// </item>
/// </list>
/// <para>Ver docs/16-multiplos-provedores-de-ingresso.md.</para>
/// </remarks>
public sealed class RepositorioDeIngressos
{
    private const string StatusValido = "valido";
    private const string StatusConsumido = "consumido";
    private const string StatusCancelado = "cancelado";

    private readonly SqliteConnectionFactory _fabrica;

    public RepositorioDeIngressos(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Cadastra ou atualiza um provedor.</summary>
    public void RegistrarProvedor(ProvedorDeIngresso provedor, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(provedor);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO ticket_provider (id, name, normalization_profile, connector, enabled, created_at)
            VALUES ($id, $nome, $perfil, $conector, $habilitado, $em)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name,
                normalization_profile = excluded.normalization_profile,
                connector = excluded.connector,
                enabled = excluded.enabled;
            """;
        comando.Parameters.AddWithValue("$id", provedor.Id);
        comando.Parameters.AddWithValue("$nome", provedor.Nome);
        comando.Parameters.AddWithValue("$perfil", provedor.PerfilDeNormalizacao);
        comando.Parameters.AddWithValue("$conector", provedor.Conector);
        comando.Parameters.AddWithValue("$habilitado", provedor.Habilitado ? 1 : 0);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.ExecuteNonQuery();
    }

    /// <summary>Provedores cadastrados.</summary>
    public IReadOnlyList<ProvedorDeIngresso> Provedores()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            "SELECT id, name, normalization_profile, connector, enabled FROM ticket_provider ORDER BY id;";

        var lista = new List<ProvedorDeIngresso>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new ProvedorDeIngresso(
                leitor.GetString(0),
                leitor.GetString(1),
                leitor.GetString(2),
                leitor.GetString(3),
                leitor.GetInt32(4) == 1));
        }

        return lista;
    }

    /// <summary>
    /// Ingere um lote de ingressos. Repetir o mesmo lote é seguro.
    /// </summary>
    /// <remarks>
    /// <b>Um ingresso ruim não derruba o lote.</b> Cinco mil ingressos bons não podem
    /// deixar de entrar porque um veio com QR conflitante — a colisão é registrada e o
    /// resto entra. Ingestão que é tudo-ou-nada vira ingestão que não acontece.
    /// </remarks>
    public ResultadoDaIngestao Ingerir(IReadOnlyCollection<IngressoRecebido> lote, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(lote);

        if (lote.Count == 0)
        {
            return new ResultadoDaIngestao(0, 0, [], 0);
        }

        var inseridos = 0;
        var atualizados = 0;
        var provedorDesconhecido = 0;
        var colisoes = new List<ColisaoDeQr>();

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var conhecidos = ProvedoresConhecidos(conexao, transacao);

        foreach (var item in lote)
        {
            if (!conhecidos.Contains(item.ProvedorId))
            {
                provedorDesconhecido++;
                continue;
            }

            var dono = DonoDoQr(conexao, transacao, item.QrNormalizado);

            if (dono is { } d && (d.Provedor != item.ProvedorId || d.Referencia != item.ReferenciaExterna))
            {
                colisoes.Add(new ColisaoDeQr(
                    item.QrNormalizado, d.Provedor, d.Referencia, item.ProvedorId, item.ReferenciaExterna));
                continue;
            }

            if (Gravar(conexao, transacao, item, agora))
            {
                inseridos++;
            }
            else
            {
                atualizados++;
            }
        }

        transacao.Commit();
        return new ResultadoDaIngestao(inseridos, atualizados, colisoes, provedorDesconhecido);
    }

    /// <summary>
    /// Tenta consumir um uso do ingresso correspondente ao QR lido.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O consumo é <b>uma instrução</b>: o <c>UPDATE</c> com as condições na cláusula
    /// <c>WHERE</c>. Duas catracas lendo o mesmo QR ao mesmo tempo produzem exatamente um
    /// vencedor — quem afetar uma linha venceu, quem afetar zero perdeu. Não há leitura
    /// antes da escrita, e por isso não há janela entre conferir e consumir.
    /// </para>
    /// <para>
    /// Só depois de perder é que se pergunta por quê, e essa pergunta é barata porque só
    /// acontece no caminho da negativa.
    /// </para>
    /// </remarks>
    /// <param name="qrNormalizado">Valor já normalizado pelo perfil do leitor.</param>
    /// <param name="gateId">Portão.</param>
    /// <param name="deviceId">Equipamento.</param>
    /// <param name="agora">Instante da leitura.</param>
    /// <param name="decisionId">Decisão de acesso correspondente, quando houver.</param>
    /// <returns>O resultado, já com o motivo exato da negativa.</returns>
    public (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
        string qrNormalizado,
        string gateId,
        string deviceId,
        DateTimeOffset agora,
        Guid? decisionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qrNormalizado);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        // A escrita vem primeiro, de propósito: é ela que pega a trava. Ler antes e
        // escrever depois, numa transação adiada, é a receita do SQLITE_BUSY que não
        // se recupera.
        var consumiu = ConsumirUmUso(conexao, transacao, qrNormalizado, gateId, agora) == 1;

        var estado = Estado(conexao, transacao, qrNormalizado);
        var tentativaId = Guid.CreateVersion7(agora);

        ResultadoDoUso resultado;

        if (consumiu && estado is { } e)
        {
            resultado = new ResultadoDoUso(
                MotivoDoUso.Consumido, e.Id, e.Provedor, e.Setor, e.UsosMaximos - e.UsosFeitos);

            RegistrarTentativa(conexao, transacao, tentativaId, e.Id, e.Provedor, qrNormalizado, gateId, deviceId,
                "consumido", MotivoDoUso.Consumido, decisionId, agora);

            // O aviso ao provedor sai na MESMA transação do consumo. Se o processo morrer
            // no microssegundo seguinte, ou o ingresso foi consumido e o aviso está na
            // fila, ou nada aconteceu. Não existe "consumiu e esqueceu de avisar".
            EnfileirarAvisoDeUso(conexao, transacao, e, qrNormalizado, gateId, agora);
        }
        else
        {
            var motivo = Diagnosticar(estado, agora);
            resultado = new ResultadoDoUso(motivo, estado?.Id, estado?.Provedor, estado?.Setor);

            RegistrarTentativa(conexao, transacao, tentativaId, estado?.Id, estado?.Provedor, qrNormalizado,
                gateId, deviceId, "negado", motivo, decisionId, agora);
        }

        transacao.Commit();
        return (resultado, tentativaId);
    }

    /// <summary>
    /// Anexa a prova de giro a uma tentativa consumida.
    /// </summary>
    /// <remarks>
    /// Chamado só com origem 6. Enquanto não for chamado, o consumo conta como
    /// <i>uso sem passagem física</i> na prestação de contas — que é exatamente o que
    /// ele é. Ver docs/ADR/ADR-0007-autorizacao-versus-passagem.md
    /// </remarks>
    public void ConfirmarPassagemFisica(Guid tentativaId, DateTimeOffset em)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            UPDATE ticket_use_attempt
            SET passage_confirmed_at = $em
            WHERE id = $id AND outcome = 'consumido' AND passage_confirmed_at IS NULL;
            """;
        comando.Parameters.AddWithValue("$em", Iso(em));
        comando.Parameters.AddWithValue("$id", tentativaId.ToString());
        comando.ExecuteNonQuery();
    }

    /// <summary>
    /// Marca que o provedor confirmou ter recebido o aviso de uso.
    /// </summary>
    /// <remarks>
    /// É o que fecha o ciclo financeiro: enquanto isto não acontecer, o uso está
    /// contabilizado aqui e possivelmente não lá.
    /// </remarks>
    public void ConfirmarAvisoDeUso(Guid ingressoId, DateTimeOffset em)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "UPDATE ticket SET reported_at = $em WHERE id = $id AND reported_at IS NULL;";
        comando.Parameters.AddWithValue("$em", Iso(em));
        comando.Parameters.AddWithValue("$id", ingressoId.ToString());
        comando.ExecuteNonQuery();
    }

    /// <summary>
    /// Fecha a conta de um provedor até um instante de corte.
    /// </summary>
    /// <remarks>
    /// O corte existe para que o relatório seja <b>reproduzível</b>: rodar de novo amanhã
    /// com o mesmo corte tem de dar o mesmo número, mesmo que evento atrasado tenha
    /// chegado no meio. Sem corte, a prestação de contas muda sozinha depois de assinada.
    /// </remarks>
    public ConciliacaoDoProvedor Conciliar(string provedorId, DateTimeOffset corte)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedorId);

        using var conexao = _fabrica.Abrir();
        var ate = Iso(corte);

        var recebidos = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate;", provedorId, ate);

        var nuncaUsados = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate AND used_count = 0;",
            provedorId, ate);

        var usados = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate AND used_count > 0;",
            provedorId, ate);

        var canceladosDepois = Escalar(conexao,
            $"SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate AND used_count > 0 AND status = '{StatusCancelado}';",
            provedorId, ate);

        var avisosPendentes = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate AND used_count > 0 AND reported_at IS NULL;",
            provedorId, ate);

        var usosConsumidos = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido';",
            provedorId, ate);

        var comGiro = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido' AND passage_confirmed_at IS NOT NULL;",
            provedorId, ate);

        var negadas = new Dictionary<MotivoDoUso, long>();
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                SELECT reason, COUNT(*)
                FROM ticket_use_attempt
                WHERE provider_id = $p AND at <= $ate AND outcome = 'negado'
                GROUP BY reason;
                """;
            comando.Parameters.AddWithValue("$p", provedorId);
            comando.Parameters.AddWithValue("$ate", ate);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                if (Enum.TryParse<MotivoDoUso>(leitor.GetString(0), out var motivo))
                {
                    negadas[motivo] = leitor.GetInt64(1);
                }
            }
        }

        return new ConciliacaoDoProvedor(
            provedorId,
            recebidos,
            nuncaUsados,
            usados,
            usosConsumidos,
            comGiro,
            usosConsumidos - comGiro,
            canceladosDepois,
            avisosPendentes,
            negadas);
    }

    /// <summary>
    /// Tentativas com QR que a base local não conhece, até o corte.
    /// </summary>
    /// <remarks>
    /// <b>É o número mais importante do relatório.</b> Ou é falsificação, ou é ingresso
    /// vendido que nunca chegou até aqui — e nesse caso o cliente foi barrado por falha
    /// nossa. Os dois casos exigem ação, e nenhum deles aparece se as tentativas negadas
    /// não forem gravadas.
    /// </remarks>
    public (long Tentativas, long CodigosDistintos) QrDesconhecidos(DateTimeOffset corte)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT COUNT(*), COUNT(DISTINCT qr_normalized)
            FROM ticket_use_attempt
            WHERE ticket_id IS NULL AND at <= $ate;
            """;
        comando.Parameters.AddWithValue("$ate", Iso(corte));

        using var leitor = comando.ExecuteReader();
        return leitor.Read() ? (leitor.GetInt64(0), leitor.GetInt64(1)) : (0, 0);
    }

    private static HashSet<string> ProvedoresConhecidos(SqliteConnection conexao, SqliteTransaction transacao)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT id FROM ticket_provider WHERE enabled = 1;";

        var ids = new HashSet<string>(StringComparer.Ordinal);
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            ids.Add(leitor.GetString(0));
        }

        return ids;
    }

    private static (string Provedor, string Referencia)? DonoDoQr(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string qr)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT provider_id, external_ref FROM ticket WHERE qr_normalized = $qr;";
        comando.Parameters.AddWithValue("$qr", qr);

        using var leitor = comando.ExecuteReader();
        return leitor.Read() ? (leitor.GetString(0), leitor.GetString(1)) : null;
    }

    private static bool Gravar(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        IngressoRecebido item,
        DateTimeOffset agora)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;

        // Reenvio do mesmo ingresso é atualização. O que NUNCA se reescreve é o consumo:
        // used_count, first_used_at e last_used_at são fato local, e o provedor não é
        // dono deles. Um cancelamento que chega depois da entrada não desfaz a entrada —
        // vira uma linha da prestação de contas.
        comando.CommandText =
            $"""
            INSERT INTO ticket
                (id, provider_id, external_ref, qr_raw, qr_normalized, sector,
                 valid_from, valid_to, max_uses, used_count, status, ingested_at)
            VALUES
                ($id, $provedor, $ref, $bruto, $qr, $setor,
                 $de, $ate, $usos, 0, $status, $em)
            ON CONFLICT (provider_id, external_ref) DO UPDATE SET
                qr_raw        = excluded.qr_raw,
                qr_normalized = excluded.qr_normalized,
                sector        = excluded.sector,
                valid_from    = excluded.valid_from,
                valid_to      = excluded.valid_to,
                max_uses      = MAX(excluded.max_uses, ticket.used_count),
                status        = CASE
                                    WHEN excluded.status = '{StatusCancelado}' THEN '{StatusCancelado}'
                                    WHEN ticket.used_count >= MAX(excluded.max_uses, ticket.used_count) THEN '{StatusConsumido}'
                                    ELSE ticket.status
                                END;
            """;

        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
        comando.Parameters.AddWithValue("$provedor", item.ProvedorId);
        comando.Parameters.AddWithValue("$ref", item.ReferenciaExterna);
        comando.Parameters.AddWithValue("$bruto", item.QrBruto);
        comando.Parameters.AddWithValue("$qr", item.QrNormalizado);
        comando.Parameters.AddWithValue("$setor", (object?)item.Setor ?? DBNull.Value);
        comando.Parameters.AddWithValue("$de", (object?)IsoOuNulo(item.ValidoDe) ?? DBNull.Value);
        comando.Parameters.AddWithValue("$ate", (object?)IsoOuNulo(item.ValidoAte) ?? DBNull.Value);
        comando.Parameters.AddWithValue("$usos", item.UsosMaximos);
        comando.Parameters.AddWithValue("$status", item.Cancelado ? StatusCancelado : StatusValido);
        comando.Parameters.AddWithValue("$em", Iso(agora));

        // changes() devolve 1 tanto no INSERT quanto no UPDATE do upsert; o que separa os
        // dois é se a linha já existia, e isso já foi consultado por DonoDoQr.
        using var conferencia = conexao.CreateCommand();
        conferencia.Transaction = transacao;
        conferencia.CommandText = "SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND external_ref = $r;";
        conferencia.Parameters.AddWithValue("$p", item.ProvedorId);
        conferencia.Parameters.AddWithValue("$r", item.ReferenciaExterna);
        var jaExistia = Convert.ToInt64(conferencia.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;

        comando.ExecuteNonQuery();
        return !jaExistia;
    }

    private static int ConsumirUmUso(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string qr,
        string gateId,
        DateTimeOffset agora)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            $"""
            UPDATE ticket
            SET used_count    = used_count + 1,
                status        = CASE WHEN used_count + 1 >= max_uses THEN '{StatusConsumido}' ELSE status END,
                first_used_at = COALESCE(first_used_at, $em),
                last_used_at  = $em,
                last_gate_id  = $gate
            WHERE qr_normalized = $qr
              AND status = '{StatusValido}'
              AND used_count < max_uses
              AND (valid_from IS NULL OR valid_from <= $em)
              AND (valid_to   IS NULL OR valid_to   >= $em)
              AND EXISTS (SELECT 1 FROM ticket_provider p WHERE p.id = ticket.provider_id AND p.enabled = 1);
            """;
        comando.Parameters.AddWithValue("$qr", qr);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$gate", gateId);
        return comando.ExecuteNonQuery();
    }

    private sealed record EstadoDoIngresso(
        Guid Id,
        string Provedor,
        string? Setor,
        string Status,
        int UsosFeitos,
        int UsosMaximos,
        DateTimeOffset? De,
        DateTimeOffset? Ate,
        string ReferenciaExterna,
        bool ProvedorHabilitado);

    private static EstadoDoIngresso? Estado(SqliteConnection conexao, SqliteTransaction transacao, string qr)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT t.id, t.provider_id, t.sector, t.status, t.used_count, t.max_uses,
                   t.valid_from, t.valid_to, t.external_ref, p.enabled
            FROM ticket t
            JOIN ticket_provider p ON p.id = t.provider_id
            WHERE t.qr_normalized = $qr;
            """;
        comando.Parameters.AddWithValue("$qr", qr);

        using var leitor = comando.ExecuteReader();
        if (!leitor.Read())
        {
            return null;
        }

        return new EstadoDoIngresso(
            Guid.Parse(leitor.GetString(0)),
            leitor.GetString(1),
            leitor.IsDBNull(2) ? null : leitor.GetString(2),
            leitor.GetString(3),
            leitor.GetInt32(4),
            leitor.GetInt32(5),
            leitor.IsDBNull(6) ? null : Parse(leitor.GetString(6)),
            leitor.IsDBNull(7) ? null : Parse(leitor.GetString(7)),
            leitor.GetString(8),
            leitor.GetInt32(9) == 1);
    }

    private static MotivoDoUso Diagnosticar(EstadoDoIngresso? estado, DateTimeOffset agora)
    {
        if (estado is null)
        {
            return MotivoDoUso.Desconhecido;
        }

        if (!estado.ProvedorHabilitado)
        {
            return MotivoDoUso.ProvedorDesabilitado;
        }

        // A ordem importa: cancelado vem antes de esgotado porque explica melhor o que
        // aconteceu com quem está na frente da catraca.
        if (string.Equals(estado.Status, StatusCancelado, StringComparison.Ordinal))
        {
            return MotivoDoUso.Cancelado;
        }

        if (!string.Equals(estado.Status, StatusValido, StringComparison.Ordinal)
            && !string.Equals(estado.Status, StatusConsumido, StringComparison.Ordinal))
        {
            return MotivoDoUso.Bloqueado;
        }

        if (estado.De is { } de && agora < de)
        {
            return MotivoDoUso.ForaDaJanela;
        }

        if (estado.Ate is { } ate && agora > ate)
        {
            return MotivoDoUso.ForaDaJanela;
        }

        return MotivoDoUso.UsosEsgotados;
    }

    private static void RegistrarTentativa(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        Guid tentativaId,
        Guid? ingressoId,
        string? provedorId,
        string qr,
        string gateId,
        string deviceId,
        string desfecho,
        MotivoDoUso motivo,
        Guid? decisionId,
        DateTimeOffset agora)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO ticket_use_attempt
                (id, ticket_id, provider_id, qr_normalized, gate_id, device_id,
                 outcome, reason, decision_id, at)
            VALUES
                ($id, $ingresso, $provedor, $qr, $gate, $dispositivo,
                 $desfecho, $motivo, $decisao, $em);
            """;
        comando.Parameters.AddWithValue("$id", tentativaId.ToString());
        comando.Parameters.AddWithValue("$ingresso", (object?)ingressoId?.ToString() ?? DBNull.Value);
        comando.Parameters.AddWithValue("$provedor", (object?)provedorId ?? DBNull.Value);
        comando.Parameters.AddWithValue("$qr", qr);
        comando.Parameters.AddWithValue("$gate", gateId);
        comando.Parameters.AddWithValue("$dispositivo", deviceId);
        comando.Parameters.AddWithValue("$desfecho", desfecho);
        comando.Parameters.AddWithValue("$motivo", motivo.ToString());
        comando.Parameters.AddWithValue("$decisao", (object?)decisionId?.ToString() ?? DBNull.Value);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.ExecuteNonQuery();
    }

    private static void EnfileirarAvisoDeUso(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        EstadoDoIngresso estado,
        string qr,
        string gateId,
        DateTimeOffset agora)
    {
        // O uso que está sendo avisado é o de número (usos feitos + 1) — o UPDATE já
        // aconteceu, mas este estado foi lido depois dele, então used_count já inclui o
        // consumo atual.
        var numeroDoUso = estado.UsosFeitos;

        var conteudo = System.Text.Json.JsonSerializer.Serialize(new
        {
            ingresso = estado.ReferenciaExterna,
            provedor = estado.Provedor,
            uso = numeroDoUso,
            de = estado.UsosMaximos,
            portao = gateId,
            em = Iso(agora),
        });

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT OR IGNORE INTO outbox
                (id, aggregate_type, aggregate_id, payload_json, priority,
                 connector, idempotency_key, created_at)
            SELECT $id, 'ticket_use', $ingresso, $conteudo, $prioridade,
                   p.connector, $idempotencia, $em
            FROM ticket_provider p
            WHERE p.id = $provedor;
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
        comando.Parameters.AddWithValue("$ingresso", estado.Id.ToString());
        comando.Parameters.AddWithValue("$conteudo", conteudo);

        // Prioridade 5 — movimento de credencial. Não é emergência: o portão já girou.
        // Mas está acima do histórico, porque é dinheiro.
        comando.Parameters.AddWithValue("$prioridade", 5);
        comando.Parameters.AddWithValue("$provedor", estado.Provedor);

        // A chave inclui o número do uso: reenviar não duplica, e o segundo uso legítimo
        // de um passe de dois dias é um aviso diferente, não o mesmo repetido.
        comando.Parameters.AddWithValue("$idempotencia", $"uso:{estado.Id}:{numeroDoUso}");
        comando.Parameters.AddWithValue("$em", Iso(agora));
        _ = qr;
        comando.ExecuteNonQuery();
    }

    private static long Escalar(SqliteConnection conexao, string sql, string provedorId, string ate)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.Parameters.AddWithValue("$p", provedorId);
        comando.Parameters.AddWithValue("$ate", ate);
        return Convert.ToInt64(comando.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static string Iso(DateTimeOffset valor) =>
        valor.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? IsoOuNulo(DateTimeOffset? valor) => valor is { } v ? Iso(v) : null;

    private static DateTimeOffset Parse(string valor) =>
        DateTimeOffset.Parse(valor, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
