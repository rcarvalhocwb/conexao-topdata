using System.Globalization;
using Access.Application.Ingressos;
using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Microsoft.Data.Sqlite;
using Sync.Core;
using Sync.Ingestao;

namespace Access.Infrastructure.SQLite;

/// <summary>Uma linha da prestação de contas por categoria.</summary>
/// <param name="Categoria">Inteira, meia, solidária, ou o que houver.</param>
/// <param name="Vendidos">Entradas vendidas nesta categoria.</param>
/// <param name="Usados">Entradas efetivamente consumidas na catraca.</param>
public sealed record LinhaPorCategoria(string Categoria, long Vendidos, long Usados)
{
    /// <summary>Rótulo para venda ou uso sem categoria — uma divergência a explicar.</summary>
    public const string SemCategoria = "(sem categoria)";

    /// <summary>Vendido e não usado: pagou e não entrou, ou o cartão ainda está na mão.</summary>
    public long Saldo => Vendidos - Usados;
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

/// <summary>Liga o espelho das tentativas para um sistema de fora.</summary>
/// <param name="Conector">Nome do conector na outbox, igual ao do conector registrado.</param>
/// <param name="EsperaPeloGiro">
/// Quanto a liberação espera o giro antes de sair. Precisa ser maior que o tempo de
/// acionamento configurado na catraca (5 s na bancada), senão o evento sai antes da
/// prova de passagem.
/// </param>
public sealed record EspelhoDeTentativas(string Conector, TimeSpan EsperaPeloGiro);

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
public sealed class RepositorioDeIngressos : IDestinoDeIngressos, IValidadorDeIngressos
{
    private const string StatusValido = "valido";
    private const string StatusConsumido = "consumido";
    private const string StatusCancelado = "cancelado";

    private readonly SqliteConnectionFactory _fabrica;
    private readonly EspelhoDeTentativas? _espelho;

    /// <param name="fabrica">Conexões com a base local.</param>
    /// <param name="espelho">
    /// Quando informado, toda tentativa também entra na outbox para o painel na nuvem.
    /// Nulo desliga o espelho, e a borda funciona igual sem ele.
    /// </param>
    public RepositorioDeIngressos(SqliteConnectionFactory fabrica, EspelhoDeTentativas? espelho = null)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        if (espelho is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(espelho.Conector);
            ArgumentOutOfRangeException.ThrowIfLessThan(espelho.EsperaPeloGiro, TimeSpan.Zero);
        }

        _fabrica = fabrica;
        _espelho = espelho;
    }

    /// <inheritdoc />
    (ResultadoDoUso Resultado, Guid TentativaId) IValidadorDeIngressos.TentarUsar(
        string qrNormalizado,
        string gateId,
        string deviceId,
        DateTimeOffset agora,
        KnownEventOrigin? leitor) =>
        TentarUsar(qrNormalizado, gateId, deviceId, agora, decisionId: null, leitor);

    /// <inheritdoc />
    /// <remarks>É o mesmo que <see cref="Ingerir"/>: a ingestão não pede nada além disso.</remarks>
    public ResultadoDaIngestao Aplicar(IReadOnlyCollection<IngressoRecebido> lote, DateTimeOffset agora) =>
        Ingerir(lote, agora);

    /// <summary>Cadastra ou atualiza um provedor.</summary>
    public void RegistrarProvedor(ProvedorDeIngresso provedor, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(provedor);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO ticket_provider
                (id, name, normalization_profile, connector, enabled, created_at, reusable, reuse_interval_seconds, urn_only)
            VALUES ($id, $nome, $perfil, $conector, $habilitado, $em, $reutilizavel, $intervalo, $urna)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name,
                normalization_profile = excluded.normalization_profile,
                connector = excluded.connector,
                enabled = excluded.enabled,
                reusable = excluded.reusable,
                reuse_interval_seconds = excluded.reuse_interval_seconds,
                urn_only = excluded.urn_only;
            """;
        comando.Parameters.AddWithValue("$id", provedor.Id);
        comando.Parameters.AddWithValue("$nome", provedor.Nome);
        comando.Parameters.AddWithValue("$perfil", provedor.PerfilDeNormalizacao);
        comando.Parameters.AddWithValue("$conector", provedor.Conector);
        comando.Parameters.AddWithValue("$habilitado", provedor.Habilitado ? 1 : 0);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$reutilizavel", provedor.Reutilizavel ? 1 : 0);
        comando.Parameters.AddWithValue("$intervalo", (long)Math.Max(0, provedor.IntervaloDeReuso.TotalSeconds));
        comando.Parameters.AddWithValue("$urna", provedor.SomenteNaUrna ? 1 : 0);
        comando.ExecuteNonQuery();
    }

    /// <summary>Provedores cadastrados.</summary>
    public IReadOnlyList<ProvedorDeIngresso> Provedores()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            SELECT id, name, normalization_profile, connector, enabled, reusable, reuse_interval_seconds, urn_only
            FROM ticket_provider ORDER BY id;
            """;

        var lista = new List<ProvedorDeIngresso>();
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            lista.Add(new ProvedorDeIngresso(
                leitor.GetString(0),
                leitor.GetString(1),
                leitor.GetString(2),
                leitor.GetString(3),
                leitor.GetInt32(4) == 1,
                leitor.GetInt32(5) == 1,
                TimeSpan.FromSeconds(leitor.GetInt64(6)),
                leitor.GetInt32(7) == 1));
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
    /// <param name="leitor">
    /// Origem da leitura: leitor 1 (frente) ou leitor 2 (fenda da urna). Nulo quando não
    /// se sabe — e, para provedor que exige urna, não saber é recusar.
    /// </param>
    /// <returns>O resultado, já com o motivo exato da negativa.</returns>
    public (ResultadoDoUso Resultado, Guid TentativaId) TentarUsar(
        string qrNormalizado,
        string gateId,
        string deviceId,
        DateTimeOffset agora,
        Guid? decisionId = null,
        KnownEventOrigin? leitor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(qrNormalizado);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        // A escrita vem primeiro, de propósito: é ela que pega a trava. Ler antes e
        // escrever depois, numa transação adiada, é a receita do SQLITE_BUSY que não
        // se recupera.
        var naUrna = leitor == KnownEventOrigin.Leitor2;
        var consumiu = ConsumirUmUso(conexao, transacao, qrNormalizado, gateId, agora, naUrna) == 1;

        var estado = Estado(conexao, transacao, qrNormalizado);
        var tentativaId = Guid.CreateVersion7(agora);

        ResultadoDoUso resultado;

        if (consumiu && estado is { } e)
        {
            resultado = new ResultadoDoUso(
                MotivoDoUso.Consumido, e.Id, e.Provedor, e.Setor, e.UsosMaximos - e.UsosFeitos, e.Categoria);

            RegistrarTentativa(conexao, transacao, tentativaId, e.Id, e.Provedor, qrNormalizado, gateId, deviceId,
                "consumido", MotivoDoUso.Consumido, decisionId, agora, e.Categoria);

            Espelhar(conexao, transacao, tentativaId, qrNormalizado, deviceId, gateId, agora,
                liberado: true, MotivoDoUso.Consumido, e.Provedor, e.Categoria);

            // O aviso ao provedor sai na MESMA transação do consumo. Se o processo morrer
            // no microssegundo seguinte, ou o ingresso foi consumido e o aviso está na
            // fila, ou nada aconteceu. Não existe "consumiu e esqueceu de avisar".
            EnfileirarAvisoDeUso(conexao, transacao, e, tentativaId, gateId, agora);
        }
        else
        {
            var motivo = Diagnosticar(estado, agora, naUrna);
            resultado = new ResultadoDoUso(
                motivo, estado?.Id, estado?.Provedor, estado?.Setor, Categoria: estado?.Categoria);

            RegistrarTentativa(conexao, transacao, tentativaId, estado?.Id, estado?.Provedor, qrNormalizado,
                gateId, deviceId, "negado", motivo, decisionId, agora, estado?.Categoria);

            Espelhar(conexao, transacao, tentativaId, qrNormalizado, deviceId, gateId, agora,
                liberado: false, motivo, estado?.Provedor, estado?.Categoria);
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
        using var transacao = conexao.BeginTransaction();

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            UPDATE ticket_use_attempt
            SET passage_confirmed_at = $em
            WHERE id = $id AND outcome = 'consumido' AND passage_confirmed_at IS NULL;
            """;
        comando.Parameters.AddWithValue("$em", Iso(em));
        comando.Parameters.AddWithValue("$id", tentativaId.ToString());
        var confirmou = comando.ExecuteNonQuery() == 1;

        if (confirmou && _espelho is not null)
        {
            // O giro entra no item que ainda está esperando na fila, e o libera para sair
            // já. Se o item já saiu (a espera acabou antes do giro), o giro fica só aqui:
            // a nuvem reconhece evento repetido por catraca + cartão + horário, e mandar
            // um segundo evento "liberado" contaria a entrada duas vezes lá.
            using var espelho = conexao.CreateCommand();
            espelho.Transaction = transacao;
            espelho.CommandText =
                """
                UPDATE outbox
                SET payload_json    = json_set(payload_json, '$.giroEm', $em),
                    next_attempt_at = CASE WHEN attempts = 0 THEN NULL ELSE next_attempt_at END
                WHERE connector = $conector
                  AND aggregate_type = $tipo
                  AND aggregate_id = $id
                  AND sent_at IS NULL;
                """;
            espelho.Parameters.AddWithValue("$em", Iso(em));
            espelho.Parameters.AddWithValue("$conector", _espelho.Conector);
            espelho.Parameters.AddWithValue("$tipo", TentativaEspelhada.TipoDoAgregado);
            espelho.Parameters.AddWithValue("$id", tentativaId.ToString());
            espelho.ExecuteNonQuery();
        }

        transacao.Commit();
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

        var (reutilizavel, temConector) = PerfilDoProvedor(conexao, provedorId);

        if (reutilizavel)
        {
            // Na bilheteria local cada linha de ticket é um CARTÃO FÍSICO, não uma venda.
            // Contar linhas diria quantos cartões existem, e isso não é prestação de
            // contas de nada.
            return ConciliarReutilizavel(conexao, provedorId, ate);
        }

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

        // Sem conector não há aviso a confirmar — nem pendência.
        var avisosPendentes = !temConector ? 0 : Escalar(conexao,
            "SELECT COUNT(*) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate AND used_count > 0 AND reported_at IS NULL;",
            provedorId, ate);

        var usosConsumidos = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido';",
            provedorId, ate);

        var comGiro = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido' AND passage_confirmed_at IS NOT NULL;",
            provedorId, ate);

        var negadas = NegadasPorMotivo(conexao, provedorId, ate);

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

    /// <summary>Resultado de uma tentativa de venda de balcão.</summary>
    public enum ResultadoDaVenda
    {
        /// <summary>Cartão carregado com a nova venda.</summary>
        Vendido,

        /// <summary>
        /// O cartão ainda tem uma venda paga e não usada. Sobrescrever apagaria dinheiro.
        /// </summary>
        VendaAnteriorNaoUsada,

        /// <summary>Cartão bloqueado — perdido, suspeito de clone.</summary>
        CartaoBloqueado,

        /// <summary>Este provedor não trabalha com cartão reutilizável.</summary>
        ProvedorNaoReutilizavel,

        /// <summary>Provedor não cadastrado.</summary>
        ProvedorDesconhecido,

        /// <summary>O código deste cartão já pertence a um ingresso de outro provedor.</summary>
        CodigoDeOutroProvedor,
    }

    /// <summary>
    /// Vende no balcão: carrega um cartão físico com uma venda nova.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O cartão é um <b>recipiente</b>; o que se vende é o uso. O mesmo cartão passa pela
    /// catraca, volta para a bilheteria e é vendido de novo — por isso uma linha de
    /// <c>ticket</c> por cartão, e uma linha de <c>ticket_sale</c> por venda.
    /// </para>
    /// <para>
    /// <b>A venda não zera o relógio de reuso.</b> Se zerasse, o golpe seria trivial: a
    /// pessoa entra, joga o cartão por cima da grade, o comparsa entrega na bilheteria,
    /// e ele é revendido na hora para entrar de novo. Com o relógio intacto, o cartão
    /// revendido só gira depois do intervalo — que é o tempo real do ciclo físico.
    /// </para>
    /// </remarks>
    /// <param name="provedorId">A bilheteria local.</param>
    /// <param name="codigoDoCartao">Código do cartão já normalizado — o que a catraca vai ler.</param>
    /// <param name="categoria">Inteira, meia, solidária, ou o que houver.</param>
    /// <param name="agora">Instante da venda.</param>
    /// <param name="usos">Quantas entradas esta venda dá. Normalmente 1.</param>
    /// <param name="operador">Quem vendeu. Entra na trilha da bilheteria.</param>
    public ResultadoDaVenda VenderNoBalcao(
        string provedorId,
        string codigoDoCartao,
        string categoria,
        DateTimeOffset agora,
        int usos = 1,
        string? operador = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoDoCartao);
        ArgumentException.ThrowIfNullOrWhiteSpace(categoria);
        ArgumentOutOfRangeException.ThrowIfLessThan(usos, 1);

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var perfil = PerfilDoProvedor(conexao, provedorId, transacao);
        if (perfil is null)
        {
            return ResultadoDaVenda.ProvedorDesconhecido;
        }

        if (!perfil.Value.Reutilizavel)
        {
            return ResultadoDaVenda.ProvedorNaoReutilizavel;
        }

        var existente = CartaoExistente(conexao, transacao, codigoDoCartao);
        Guid ingressoId;

        if (existente is null)
        {
            // Primeira venda deste cartão: ele passa a existir.
            ingressoId = Guid.CreateVersion7(agora);
            using var insercao = conexao.CreateCommand();
            insercao.Transaction = transacao;
            insercao.CommandText =
                """
                INSERT INTO ticket
                    (id, provider_id, external_ref, qr_raw, qr_normalized,
                     max_uses, used_count, status, ingested_at, category)
                VALUES ($id, $provedor, $codigo, $codigo, $codigo, $usos, 0, 'valido', $em, $categoria);
                """;
            insercao.Parameters.AddWithValue("$id", ingressoId.ToString());
            insercao.Parameters.AddWithValue("$provedor", provedorId);
            insercao.Parameters.AddWithValue("$codigo", codigoDoCartao);
            insercao.Parameters.AddWithValue("$usos", usos);
            insercao.Parameters.AddWithValue("$em", Iso(agora));
            insercao.Parameters.AddWithValue("$categoria", categoria);
            insercao.ExecuteNonQuery();
        }
        else
        {
            var c = existente.Value;

            if (!string.Equals(c.Provedor, provedorId, StringComparison.Ordinal))
            {
                return ResultadoDaVenda.CodigoDeOutroProvedor;
            }

            if (c.Status is not (StatusValido or StatusConsumido))
            {
                return ResultadoDaVenda.CartaoBloqueado;
            }

            if (string.Equals(c.Status, StatusValido, StringComparison.Ordinal) && c.UsosFeitos < c.UsosMaximos)
            {
                return ResultadoDaVenda.VendaAnteriorNaoUsada;
            }

            ingressoId = c.Id;
            using var recarga = conexao.CreateCommand();
            recarga.Transaction = transacao;

            // last_used_at e last_used_epoch ficam como estão: são o relógio de reuso.
            recarga.CommandText =
                """
                UPDATE ticket
                SET used_count = 0, max_uses = $usos, status = 'valido', category = $categoria,
                    first_used_at = NULL, reported_at = NULL
                WHERE id = $id;
                """;
            recarga.Parameters.AddWithValue("$usos", usos);
            recarga.Parameters.AddWithValue("$categoria", categoria);
            recarga.Parameters.AddWithValue("$id", ingressoId.ToString());
            recarga.ExecuteNonQuery();
        }

        using (var venda = conexao.CreateCommand())
        {
            venda.Transaction = transacao;
            venda.CommandText =
                """
                INSERT INTO ticket_sale (id, ticket_id, provider_id, category, uses, sold_at, operator)
                VALUES ($id, $ingresso, $provedor, $categoria, $usos, $em, $operador);
                """;
            venda.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
            venda.Parameters.AddWithValue("$ingresso", ingressoId.ToString());
            venda.Parameters.AddWithValue("$provedor", provedorId);
            venda.Parameters.AddWithValue("$categoria", categoria);
            venda.Parameters.AddWithValue("$usos", usos);
            venda.Parameters.AddWithValue("$em", Iso(agora));
            venda.Parameters.AddWithValue("$operador", (object?)operador ?? DBNull.Value);
            venda.ExecuteNonQuery();
        }

        transacao.Commit();
        return ResultadoDaVenda.Vendido;
    }

    /// <summary>
    /// Vendido e usado, por categoria, até o corte.
    /// </summary>
    /// <remarks>
    /// A categoria de cada uso vem da <b>fotografia</b> tirada na tentativa, não do estado
    /// atual do cartão. O cartão da bilheteria é revendido; se o relatório lesse a
    /// categoria atual, a meia-entrada vendida às 19h viraria a inteira vendida às 21h.
    /// Categoria nula aparece como <c>(sem categoria)</c> — e isso é uma divergência a
    /// explicar, não uma linha a esconder.
    /// </remarks>
    public IReadOnlyList<LinhaPorCategoria> ResumoPorCategoria(string provedorId, DateTimeOffset corte)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provedorId);

        using var conexao = _fabrica.Abrir();
        var ate = Iso(corte);
        var reutilizavel = PerfilDoProvedor(conexao, provedorId).Reutilizavel;

        var vendidos = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = reutilizavel
                ? "SELECT COALESCE(category, ''), SUM(uses) FROM ticket_sale WHERE provider_id = $p AND sold_at <= $ate GROUP BY 1;"
                : "SELECT COALESCE(category, ''), SUM(max_uses) FROM ticket WHERE provider_id = $p AND ingested_at <= $ate GROUP BY 1;";
            comando.Parameters.AddWithValue("$p", provedorId);
            comando.Parameters.AddWithValue("$ate", ate);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                vendidos[leitor.GetString(0)] = leitor.GetInt64(1);
            }
        }

        var usados = new Dictionary<string, long>(StringComparer.Ordinal);
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText =
                """
                SELECT COALESCE(category, ''), COUNT(*)
                FROM ticket_use_attempt
                WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido'
                GROUP BY 1;
                """;
            comando.Parameters.AddWithValue("$p", provedorId);
            comando.Parameters.AddWithValue("$ate", ate);
            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                usados[leitor.GetString(0)] = leitor.GetInt64(1);
            }
        }

        return
        [
            .. vendidos.Keys.Union(usados.Keys, StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => new LinhaPorCategoria(
                    k.Length == 0 ? LinhaPorCategoria.SemCategoria : k,
                    vendidos.GetValueOrDefault(k),
                    usados.GetValueOrDefault(k))),
        ];
    }

    private static ConciliacaoDoProvedor ConciliarReutilizavel(SqliteConnection conexao, string provedorId, string ate)
    {
        var vendas = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_sale WHERE provider_id = $p AND sold_at <= $ate;", provedorId, ate);

        var creditos = Escalar(conexao,
            "SELECT COALESCE(SUM(uses), 0) FROM ticket_sale WHERE provider_id = $p AND sold_at <= $ate;", provedorId, ate);

        var usosConsumidos = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido';",
            provedorId, ate);

        var comGiro = Escalar(conexao,
            "SELECT COUNT(*) FROM ticket_use_attempt WHERE provider_id = $p AND at <= $ate AND outcome = 'consumido' AND passage_confirmed_at IS NOT NULL;",
            provedorId, ate);

        // Na bilheteria local, "recebido" é "vendido", e "nunca usado" é crédito vendido
        // que não passou pela catraca — gente que pagou e não entrou, ou cartão ainda na
        // mão de alguém.
        return new ConciliacaoDoProvedor(
            provedorId,
            IngressosRecebidos: vendas,
            NuncaUsados: Math.Max(0, creditos - usosConsumidos),
            Usados: usosConsumidos,
            UsosConsumidos: usosConsumidos,
            UsosComPassagemFisica: comGiro,
            UsosSemPassagemFisica: usosConsumidos - comGiro,
            CanceladosDepoisDeUsados: 0,
            AvisosPendentesDeConfirmacao: 0,
            TentativasNegadas: NegadasPorMotivo(conexao, provedorId, ate));
    }

    private static Dictionary<MotivoDoUso, long> NegadasPorMotivo(SqliteConnection conexao, string provedorId, string ate)
    {
        var negadas = new Dictionary<MotivoDoUso, long>();
        using var comando = conexao.CreateCommand();
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

        return negadas;
    }

    private static (bool Reutilizavel, bool TemConector) PerfilDoProvedor(SqliteConnection conexao, string provedorId) =>
        PerfilDoProvedor(conexao, provedorId, transacao: null) ?? (false, true);

    private static (bool Reutilizavel, bool TemConector)? PerfilDoProvedor(
        SqliteConnection conexao,
        string provedorId,
        SqliteTransaction? transacao)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT reusable, connector FROM ticket_provider WHERE id = $p;";
        comando.Parameters.AddWithValue("$p", provedorId);

        using var leitor = comando.ExecuteReader();
        return leitor.Read()
            ? (leitor.GetInt32(0) == 1, leitor.GetString(1).Length > 0)
            : null;
    }

    private static (Guid Id, string Provedor, string Status, int UsosFeitos, int UsosMaximos)? CartaoExistente(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        string codigo)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            "SELECT id, provider_id, status, used_count, max_uses FROM ticket WHERE qr_normalized = $c;";
        comando.Parameters.AddWithValue("$c", codigo);

        using var leitor = comando.ExecuteReader();
        return leitor.Read()
            ? (Guid.Parse(leitor.GetString(0)), leitor.GetString(1), leitor.GetString(2), leitor.GetInt32(3), leitor.GetInt32(4))
            : null;
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
                 valid_from, valid_to, max_uses, used_count, status, ingested_at, category)
            VALUES
                ($id, $provedor, $ref, $bruto, $qr, $setor,
                 $de, $ate, $usos, 0, $status, $em, $categoria)
            ON CONFLICT (provider_id, external_ref) DO UPDATE SET
                category      = excluded.category,
                qr_raw        = excluded.qr_raw,
                qr_normalized = excluded.qr_normalized,
                sector        = excluded.sector,
                valid_from    = excluded.valid_from,
                valid_to      = excluded.valid_to,
                max_uses      = MAX(excluded.max_uses, ticket.used_count),
                status        = CASE
                                    WHEN excluded.status = '{StatusCancelado}' THEN '{StatusCancelado}'
                                    WHEN ticket.used_count >= MAX(excluded.max_uses, ticket.used_count) THEN '{StatusConsumido}'
                                    -- Cartão da bilheteria desativado e depois reativado na
                                    -- nuvem (perdido e achado, por exemplo) volta a valer. No
                                    -- ingresso de site o cancelamento é definitivo.
                                    WHEN ticket.status = '{StatusCancelado}'
                                         AND EXISTS (SELECT 1 FROM ticket_provider p
                                                     WHERE p.id = ticket.provider_id AND p.reusable = 1)
                                         THEN '{StatusValido}'
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
        comando.Parameters.AddWithValue("$categoria", (object?)item.Categoria ?? DBNull.Value);

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
        DateTimeOffset agora,
        bool naUrna)
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
                last_used_epoch = $epoch,
                last_gate_id  = $gate
            WHERE qr_normalized = $qr
              AND status = '{StatusValido}'
              AND used_count < max_uses
              AND (valid_from IS NULL OR valid_from <= $em)
              AND (valid_to   IS NULL OR valid_to   >= $em)
              AND EXISTS (
                    SELECT 1 FROM ticket_provider p
                    WHERE p.id = ticket.provider_id
                      AND p.enabled = 1
                      AND (p.urn_only = 0 OR $naUrna = 1)
                      -- O intervalo de reuso: o mesmo cartão de volta cedo demais não é um
                      -- cliente, é o cartão passado por cima da grade. A revenda NÃO zera
                      -- last_used_epoch, então revender na hora não contorna isto.
                      AND (ticket.last_used_epoch IS NULL
                           OR ticket.last_used_epoch + p.reuse_interval_seconds <= $epoch));
            """;
        comando.Parameters.AddWithValue("$qr", qr);
        comando.Parameters.AddWithValue("$em", Iso(agora));
        comando.Parameters.AddWithValue("$epoch", agora.ToUnixTimeSeconds());
        comando.Parameters.AddWithValue("$naUrna", naUrna ? 1 : 0);
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
        bool ProvedorHabilitado,
        string? Categoria,
        long? UltimoUsoEpoch,
        long IntervaloDeReusoSegundos,
        bool Reutilizavel,
        bool SomenteNaUrna);

    private static EstadoDoIngresso? Estado(SqliteConnection conexao, SqliteTransaction transacao, string qr)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            SELECT t.id, t.provider_id, t.sector, t.status, t.used_count, t.max_uses,
                   t.valid_from, t.valid_to, t.external_ref, p.enabled,
                   t.category, t.last_used_epoch, p.reuse_interval_seconds, p.reusable, p.urn_only
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
            leitor.GetInt32(9) == 1,
            leitor.IsDBNull(10) ? null : leitor.GetString(10),
            leitor.IsDBNull(11) ? null : leitor.GetInt64(11),
            leitor.GetInt64(12),
            leitor.GetInt32(13) == 1,
            leitor.GetInt32(14) == 1);
    }

    private static MotivoDoUso Diagnosticar(EstadoDoIngresso? estado, DateTimeOffset agora, bool naUrna)
    {
        if (estado is null)
        {
            return MotivoDoUso.Desconhecido;
        }

        if (!estado.ProvedorHabilitado)
        {
            return MotivoDoUso.ProvedorDesabilitado;
        }

        // Vem cedo de propósito: para quem está na frente da catraca com o cartão na mão,
        // "use a urna" é a única instrução que resolve.
        if (estado.SomenteNaUrna && !naUrna)
        {
            return MotivoDoUso.ForaDaUrna;
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

        // Venda válida, com uso sobrando, e mesmo assim recusada: só resta o relógio de
        // reuso. É o único caso em que "tem crédito" e "não pode entrar" convivem.
        if (string.Equals(estado.Status, StatusValido, StringComparison.Ordinal)
            && estado.UsosFeitos < estado.UsosMaximos
            && EmIntervaloDeReuso(estado, agora))
        {
            return MotivoDoUso.EmIntervaloDeReuso;
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
        DateTimeOffset agora,
        string? categoria)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT INTO ticket_use_attempt
                (id, ticket_id, provider_id, qr_normalized, gate_id, device_id,
                 outcome, reason, decision_id, at, category)
            VALUES
                ($id, $ingresso, $provedor, $qr, $gate, $dispositivo,
                 $desfecho, $motivo, $decisao, $em, $categoria);
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
        comando.Parameters.AddWithValue("$categoria", (object?)categoria ?? DBNull.Value);
        comando.ExecuteNonQuery();
    }

    private static bool EmIntervaloDeReuso(EstadoDoIngresso estado, DateTimeOffset agora) =>
        estado.IntervaloDeReusoSegundos > 0
        && estado.UltimoUsoEpoch is { } ultimo
        && ultimo + estado.IntervaloDeReusoSegundos > agora.ToUnixTimeSeconds();

    private void Espelhar(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        Guid tentativaId,
        string codigo,
        string deviceId,
        string gateId,
        DateTimeOffset agora,
        bool liberado,
        MotivoDoUso motivo,
        string? provedor,
        string? categoria)
    {
        if (_espelho is null)
        {
            return;
        }

        var conteudo = new TentativaEspelhada(
            TentativaEspelhada.VersaoAtual, tentativaId, codigo, deviceId, gateId, agora.ToUniversalTime(),
            liberado, motivo.ToString(), provedor, categoria, GiroEm: null).ParaJson();

        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText =
            """
            INSERT OR IGNORE INTO outbox
                (id, aggregate_type, aggregate_id, payload_json, priority,
                 connector, idempotency_key, created_at, next_attempt_at)
            VALUES ($id, $tipo, $tentativa, $conteudo, $prioridade,
                    $conector, $idempotencia, $em, $aPartirDe);
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
        comando.Parameters.AddWithValue("$tipo", TentativaEspelhada.TipoDoAgregado);
        comando.Parameters.AddWithValue("$tentativa", tentativaId.ToString());
        comando.Parameters.AddWithValue("$conteudo", conteudo);
        comando.Parameters.AddWithValue(
            "$prioridade",
            liberado ? PrioridadeDeSincronizacao.PassagemFisica : PrioridadeDeSincronizacao.DecisaoDeAcesso);
        comando.Parameters.AddWithValue("$conector", _espelho.Conector);
        comando.Parameters.AddWithValue("$idempotencia", $"tentativa:{tentativaId}");
        comando.Parameters.AddWithValue("$em", Iso(agora));

        // A liberação espera o giro antes de sair: assim vai UM evento só, já dizendo se
        // a pessoa passou. A negativa não tem giro a esperar e sai na hora. Entrar na
        // fila acontece de qualquer jeito, e na mesma transação da tentativa — se o
        // processo cair agora, ou as duas existem, ou nenhuma.
        comando.Parameters.AddWithValue(
            "$aPartirDe",
            liberado && _espelho.EsperaPeloGiro > TimeSpan.Zero
                ? Iso(agora + _espelho.EsperaPeloGiro)
                : DBNull.Value);
        comando.ExecuteNonQuery();
    }

    private static void EnfileirarAvisoDeUso(
        SqliteConnection conexao,
        SqliteTransaction transacao,
        EstadoDoIngresso estado,
        Guid tentativaId,
        string gateId,
        DateTimeOffset agora)
    {
        // O uso que está sendo avisado é o de número (usos feitos + 1) — o UPDATE já
        // aconteceu, mas este estado foi lido depois dele, então used_count já inclui o
        // consumo atual.
        var numeroDoUso = estado.UsosFeitos;

        // O formato do retorno também é contrato nosso, e versionado pelo mesmo motivo
        // que o de entrada: docs/18-contrato-do-webhook.md, seção 10.
        var conteudo = System.Text.Json.JsonSerializer.Serialize(new
        {
            versao = 1,
            tipo = "consumo",
            ingresso = estado.ReferenciaExterna,
            provedor = estado.Provedor,
            uso = numeroDoUso,
            de = estado.UsosMaximos,
            categoria = estado.Categoria,
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
            WHERE p.id = $provedor
              -- Provedor sem conector não recebe aviso. É o caso da bilheteria local: o
              -- sistema dela é este aqui, e não há ninguém do outro lado para dar baixa.
              AND p.connector <> '';
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
        //
        // Cartão reutilizável NÃO pode usar essa forma: a revenda zera o contador, e o
        // primeiro uso da segunda venda geraria a mesma chave do primeiro uso da primeira
        // — e o INSERT OR IGNORE descartaria o aviso em silêncio. Ali a tentativa, que é
        // única por consumo, é o discriminador.
        var chave = estado.Reutilizavel
            ? $"uso:{estado.Id}:{tentativaId}"
            : $"uso:{estado.Id}:{numeroDoUso}";

        comando.Parameters.AddWithValue("$idempotencia", chave);
        comando.Parameters.AddWithValue("$em", Iso(agora));
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
