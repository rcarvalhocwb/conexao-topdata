using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;

namespace Integration.Tests;

/// <summary>
/// Um evento com três bilheterias diferentes vendendo ingresso com QR.
/// </summary>
/// <remarks>
/// Ver docs/16-multiplos-provedores-de-ingresso.md. O que estes testes protegem é a
/// prestação de contas: cada número do relatório final sai de uma linha que alguém
/// precisou gravar na hora certa, inclusive as negativas.
/// </remarks>
public sealed class IngressosDeVariosProvedoresTests
{
    private static readonly DateTimeOffset Abertura = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Corte = Abertura.AddHours(12);

    private static RepositorioDeIngressos Preparar(BancoTemporario banco, params string[] provedores)
    {
        banco.Migrar();
        var repositorio = new RepositorioDeIngressos(banco.Fabrica);

        foreach (var id in provedores.Length == 0 ? ["bilheteria-a"] : provedores)
        {
            repositorio.RegistrarProvedor(
                new ProvedorDeIngresso(id, id.ToUpperInvariant(), "qr-maiusculo", $"rest-{id}"),
                Abertura);
        }

        return repositorio;
    }

    private static IngressoRecebido Ingresso(
        string provedor,
        string referencia,
        string qr,
        int usos = 1,
        bool cancelado = false,
        DateTimeOffset? de = null,
        DateTimeOffset? ate = null) =>
        new(provedor, referencia, qr, qr, Setor: "pista", ValidoDe: de, ValidoAte: ate,
            UsosMaximos: usos, Cancelado: cancelado);

    [Fact]
    public void Ingresso_de_provedor_nao_cadastrado_nao_entra()
    {
        // Não é rigor burocrático: sem o provedor cadastrado não se sabe para onde mandar
        // o aviso de uso, e o ingresso entraria para nunca ser prestado conta.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");

        var resultado = repositorio.Ingerir([Ingresso("bilheteria-fantasma", "X1", "QR-X1")], Abertura);

        Assert.Equal(1, resultado.ProvedorDesconhecido);
        Assert.True(resultado.Vazia);
    }

    [Fact]
    public void Reenviar_o_mesmo_lote_atualiza_e_nao_duplica()
    {
        // Os três provedores vão reenviar. Um por retentativa, outro por rotina noturna,
        // o terceiro porque o operador clicou duas vezes.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");

        var lote = new[] { Ingresso("bilheteria-a", "A1", "QR-A1"), Ingresso("bilheteria-a", "A2", "QR-A2") };

        var primeira = repositorio.Ingerir(lote, Abertura);
        var segunda = repositorio.Ingerir(lote, Abertura.AddMinutes(5));

        Assert.Equal(2, primeira.Inseridos);
        Assert.Equal(0, segunda.Inseridos);
        Assert.Equal(2, segunda.Atualizados);
        Assert.Equal(2, repositorio.Conciliar("bilheteria-a", Corte).IngressosRecebidos);
    }

    [Fact]
    public void Qr_repetido_entre_provedores_e_recusado_na_ingestao_sem_derrubar_o_lote()
    {
        // A catraca lê uma string e nada mais. Se dois provedores emitirem o mesmo QR, o
        // portão não tem como saber de quem é — e descobrir isso com cem pessoas na fila
        // é tarde demais.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a", "bilheteria-b");

        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-IGUAL")], Abertura);

        var resultado = repositorio.Ingerir(
            [
                Ingresso("bilheteria-b", "B1", "QR-IGUAL"),
                Ingresso("bilheteria-b", "B2", "QR-B2"),
                Ingresso("bilheteria-b", "B3", "QR-B3"),
            ],
            Abertura.AddMinutes(1));

        // O conflito é recusado com nome e sobrenome...
        var colisao = Assert.Single(resultado.Colisoes);
        Assert.Equal("bilheteria-a", colisao.ProvedorExistente);
        Assert.Equal("A1", colisao.ReferenciaExistente);
        Assert.Equal("bilheteria-b", colisao.ProvedorNovo);

        // ...e os outros dois ingressos da mesma bilheteria entram normalmente.
        Assert.Equal(2, resultado.Inseridos);
    }

    [Fact]
    public void Uso_valido_consome_registra_e_enfileira_o_aviso_na_mesma_transacao()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1")], Abertura);

        var (resultado, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));

        Assert.True(resultado.Liberou);
        Assert.Equal("bilheteria-a", resultado.ProvedorId);
        Assert.Equal(0, resultado.UsosRestantes);

        // O aviso ao provedor já está na fila de saída, com o conector do provedor.
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT connector, priority, idempotency_key, payload_json FROM outbox;";
        using var leitor = comando.ExecuteReader();

        Assert.True(leitor.Read());
        Assert.Equal("rest-bilheteria-a", leitor.GetString(0));
        Assert.Equal(5, leitor.GetInt32(1));
        Assert.EndsWith(":1", leitor.GetString(2), StringComparison.Ordinal);
        Assert.Contains("\"ingresso\":\"A1\"", leitor.GetString(3), StringComparison.Ordinal);
    }

    [Fact]
    public void O_mesmo_ingresso_nao_entra_duas_vezes()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1")], Abertura);

        repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        var (segunda, _) = repositorio.TentarUsar("QR-A1", "portao-2", "catraca-02", Abertura.AddHours(1).AddMinutes(3));

        Assert.False(segunda.Liberou);
        Assert.Equal(MotivoDoUso.UsosEsgotados, segunda.Motivo);
    }

    [Fact]
    public void Passe_de_dois_dias_permite_dois_usos_e_gera_dois_avisos_distintos()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1", usos: 2)], Abertura);

        var (primeiro, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        var (segundo, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(5));
        var (terceiro, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(9));

        Assert.True(primeiro.Liberou);
        Assert.Equal(1, primeiro.UsosRestantes);
        Assert.True(segundo.Liberou);
        Assert.Equal(0, segundo.UsosRestantes);
        Assert.False(terceiro.Liberou);

        // Dois avisos, não um repetido: a chave de idempotência inclui o número do uso.
        using var conexao = banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT COUNT(DISTINCT idempotency_key) FROM outbox;";
        Assert.Equal(2L, (long)(comando.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void Qr_desconhecido_vira_tentativa_gravada_e_aparece_no_relatorio()
    {
        // É o número mais importante da prestação de contas: ou é falsificação, ou é
        // ingresso vendido que nunca chegou até aqui — e aí quem errou fomos nós.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");

        var (resultado, _) = repositorio.TentarUsar("QR-QUE-NINGUEM-VENDEU", "portao-3", "catraca-03", Abertura.AddHours(2));

        Assert.Equal(MotivoDoUso.Desconhecido, resultado.Motivo);
        Assert.Null(resultado.ProvedorId);

        var (tentativas, distintos) = repositorio.QrDesconhecidos(Corte);
        Assert.Equal(1, tentativas);
        Assert.Equal(1, distintos);
    }

    [Fact]
    public void Ingresso_cancelado_pelo_provedor_e_negado_com_o_motivo_certo()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1")], Abertura);

        // Estorno: o provedor reenvia o mesmo ingresso marcado como cancelado.
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1", cancelado: true)], Abertura.AddMinutes(30));

        var (resultado, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));

        Assert.Equal(MotivoDoUso.Cancelado, resultado.Motivo);
    }

    [Fact]
    public void Cancelamento_que_chega_depois_da_entrada_nao_desfaz_a_entrada()
    {
        // O provedor não é dono do fato local. A pessoa entrou; o estorno virou
        // divergência financeira, não uma entrada apagada.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1")], Abertura);

        repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1", cancelado: true)], Abertura.AddHours(2));

        var conta = repositorio.Conciliar("bilheteria-a", Corte);

        Assert.Equal(1, conta.Usados);
        Assert.Equal(1, conta.CanceladosDepoisDeUsados);
        Assert.False(conta.Fecha);
    }

    [Fact]
    public void Fora_da_janela_de_validade_e_negado()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir(
            [Ingresso("bilheteria-a", "A1", "QR-A1", de: Abertura.AddHours(2), ate: Abertura.AddHours(6))],
            Abertura);

        var (cedo, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        var (tarde, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(7));
        var (naHora, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(3));

        Assert.Equal(MotivoDoUso.ForaDaJanela, cedo.Motivo);
        Assert.Equal(MotivoDoUso.ForaDaJanela, tarde.Motivo);
        Assert.True(naHora.Liberou);
    }

    [Fact]
    public void Provedor_desabilitado_para_de_validar_sem_apagar_nada()
    {
        // O botão que a operação aperta quando uma bilheteria é pega vendendo errado.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1")], Abertura);

        repositorio.RegistrarProvedor(
            new ProvedorDeIngresso("bilheteria-a", "A", "qr-maiusculo", "rest-bilheteria-a", Habilitado: false),
            Abertura);

        var (resultado, _) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));

        Assert.Equal(MotivoDoUso.ProvedorDesabilitado, resultado.Motivo);
        Assert.Equal(1, repositorio.Conciliar("bilheteria-a", Corte).IngressosRecebidos);
    }

    [Fact]
    public void Duas_catracas_lendo_o_mesmo_qr_no_mesmo_instante_tem_exatamente_um_vencedor()
    {
        // Duas pessoas com print do mesmo ingresso, em portões diferentes, no mesmo
        // segundo. Sem trava distribuída e sem perguntar nada para a nuvem.
        //
        // O QUE ESTE TESTE NÃO PROVA, e eu cheguei a afirmar que provava: que o UPDATE
        // de instrução única é NECESSÁRIO. Substituí a implementação pela ingênua — ler
        // used_count, decidir em memória, escrever — e o teste passou igual. A razão é
        // que cada tentativa abre a própria transação, e o SQLite serializa transações
        // de escrita no arquivo: com busy_timeout, as oito acabam em fila.
        //
        // O UPDATE de instrução única continua sendo a implementação certa — ele não
        // depende de repetição por SQLITE_BUSY e não passa pela promoção de leitura para
        // escrita, que é onde mora o impasse. Mas quem garante o vencedor único aqui é o
        // banco, não a minha cláusula WHERE, e o teste mede o resultado, não a causa.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-DISPUTADO")], Abertura);

        const int catracas = 8;
        var resultados = new ResultadoDoUso[catracas];
        using var largada = new Barrier(catracas);

        Parallel.For(0, catracas, i =>
        {
            largada.SignalAndWait();
            var (resultado, _) = repositorio.TentarUsar(
                "QR-DISPUTADO", $"portao-{i}", $"catraca-{i:00}", Abertura.AddHours(1));
            resultados[i] = resultado;
        });

        Assert.Single(resultados, r => r.Liberou);
        Assert.Equal(catracas - 1, resultados.Count(r => r.Motivo == MotivoDoUso.UsosEsgotados));

        // E uma linha por tentativa: sete negativas ficam no relatório, não somem.
        var conta = repositorio.Conciliar("bilheteria-a", Corte);
        Assert.Equal(1, conta.UsosConsumidos);
        Assert.Equal(catracas - 1, conta.TentativasNegadas[MotivoDoUso.UsosEsgotados]);
    }

    [Fact]
    public void A_conta_fecha_quando_todo_consumo_tem_giro_e_aviso_confirmado()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir(
            [Ingresso("bilheteria-a", "A1", "QR-A1"), Ingresso("bilheteria-a", "A2", "QR-A2")],
            Abertura);

        var (usoA1, tentativaA1) = repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));

        // Antes da prova de giro, a conta NÃO fecha: autorizar não é passar.
        var parcial = repositorio.Conciliar("bilheteria-a", Corte);
        Assert.Equal(1, parcial.UsosSemPassagemFisica);
        Assert.False(parcial.Fecha);

        repositorio.ConfirmarPassagemFisica(tentativaA1, Abertura.AddHours(1).AddSeconds(3));
        repositorio.ConfirmarAvisoDeUso(usoA1.IngressoId!.Value, Abertura.AddHours(1).AddSeconds(9));

        var conta = repositorio.Conciliar("bilheteria-a", Corte);

        Assert.Equal(2, conta.IngressosRecebidos);
        Assert.Equal(1, conta.NuncaUsados);          // no-show legítimo
        Assert.Equal(1, conta.Usados);
        Assert.Equal(1, conta.UsosComPassagemFisica);
        Assert.Equal(0, conta.UsosSemPassagemFisica);
        Assert.Equal(0, conta.AvisosPendentesDeConfirmacao);
        Assert.True(conta.Fecha);
    }

    [Fact]
    public void O_corte_torna_o_relatorio_reproduzivel()
    {
        // Prestação de contas que muda sozinha depois de assinada não é prestação de
        // contas. O que chegar depois do corte entra no relatório seguinte.
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a");
        repositorio.Ingerir([Ingresso("bilheteria-a", "A1", "QR-A1")], Abertura);
        repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));

        var corteCedo = repositorio.Conciliar("bilheteria-a", Abertura.AddMinutes(30));
        var corteDepois = repositorio.Conciliar("bilheteria-a", Corte);

        Assert.Equal(0, corteCedo.UsosConsumidos);
        Assert.Equal(1, corteDepois.UsosConsumidos);

        // Rodar de novo com o mesmo corte tem de dar o mesmo número.
        Assert.Equal(corteDepois, repositorio.Conciliar("bilheteria-a", Corte));
    }

    [Fact]
    public void Cada_provedor_presta_a_propria_conta()
    {
        using var banco = new BancoTemporario();
        var repositorio = Preparar(banco, "bilheteria-a", "bilheteria-b", "bilheteria-c");

        repositorio.Ingerir(
            [
                Ingresso("bilheteria-a", "A1", "QR-A1"),
                Ingresso("bilheteria-a", "A2", "QR-A2"),
                Ingresso("bilheteria-b", "B1", "QR-B1"),
                Ingresso("bilheteria-c", "C1", "QR-C1"),
            ],
            Abertura);

        repositorio.TentarUsar("QR-A1", "portao-1", "catraca-01", Abertura.AddHours(1));
        repositorio.TentarUsar("QR-B1", "portao-1", "catraca-01", Abertura.AddHours(1));
        repositorio.TentarUsar("QR-B1", "portao-2", "catraca-02", Abertura.AddHours(2));

        Assert.Equal(1, repositorio.Conciliar("bilheteria-a", Corte).UsosConsumidos);
        Assert.Equal(1, repositorio.Conciliar("bilheteria-a", Corte).NuncaUsados);
        Assert.Equal(1, repositorio.Conciliar("bilheteria-b", Corte).UsosConsumidos);
        Assert.Equal(1, repositorio.Conciliar("bilheteria-b", Corte).TentativasNegadas[MotivoDoUso.UsosEsgotados]);
        Assert.Equal(0, repositorio.Conciliar("bilheteria-c", Corte).UsosConsumidos);
        Assert.Equal(1, repositorio.Conciliar("bilheteria-c", Corte).NuncaUsados);
    }
}
