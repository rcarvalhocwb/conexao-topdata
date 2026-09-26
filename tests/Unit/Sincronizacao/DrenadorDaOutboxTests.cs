using Sync.Core;

namespace Unit.Tests.Sincronizacao;

/// <summary>
/// A política de drenagem da outbox, testada sem banco, sem rede e sem esperar.
/// </summary>
/// <remarks>
/// O que estes testes protegem é a promessa operacional: depois de horas sem internet,
/// o que sobe primeiro é o que importa primeiro, nada se perde e nada trava a fila.
/// Ver docs/15-integracao-e-sincronizacao.md
/// </remarks>
public sealed class DrenadorDaOutboxTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static TimeSpan EsperaFixa(int tentativa) => TimeSpan.FromSeconds(tentativa);

    [Fact]
    public async Task Envia_primeiro_a_prioridade_mais_alta_mesmo_sendo_a_mais_nova()
    {
        var fila = new FilaEmMemoria();
        // O histórico chegou de manhã; a revogação chegou agora.
        fila.Enfileirar("historico", "erp", PrioridadeDeSincronizacao.Historico, Inicio.AddHours(-8));
        fila.Enfileirar("revogacao", "erp", PrioridadeDeSincronizacao.RevogacaoDeCredencial, Inicio);

        var conector = new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito, tamanhoMaximoDoLote: 1);
        var drenador = Criar(fila, [conector]);

        await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal("revogacao", Assert.Single(conector.Lotes[0]).Id);
    }

    [Fact]
    public async Task Item_aceito_sai_da_fila()
    {
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.PassagemFisica, Inicio);

        var drenador = Criar(fila, [new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito)]);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Enviados);
        Assert.Empty(fila.Pendentes());
        Assert.Equal(["a"], fila.Enviados());
    }

    [Fact]
    public async Task Duplicado_conta_como_entregue()
    {
        // É o caminho normal depois de uma queda no meio do envio: o destino já gravou,
        // a resposta se perdeu na volta. Repetir e ouvir "já tenho" é sucesso.
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.DecisaoDeAcesso, Inicio);

        var drenador = Criar(fila, [new ConectorFalso("erp", _ => ResultadoDoEnvio.Duplicado)]);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Enviados);
        Assert.Empty(fila.CartasMortas);
        Assert.Empty(fila.Pendentes());
    }

    [Fact]
    public async Task Falha_temporaria_adia_e_conta_a_tentativa()
    {
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.Alarme, Inicio);

        var relogio = new RelogioDeTeste(Inicio);
        var drenador = Criar(fila, [new ConectorFalso("erp", _ => ResultadoDoEnvio.FalhaTemporaria)], relogio);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Adiados);
        var (id, tentativas, proxima) = Assert.Single(fila.Adiamentos);
        Assert.Equal("a", id);
        Assert.Equal(1, tentativas);
        Assert.Equal(Inicio.AddSeconds(1), proxima);
        Assert.Equal(["a"], fila.Pendentes());
    }

    [Fact]
    public async Task Item_adiado_nao_e_relido_antes_da_hora()
    {
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.Alarme, Inicio);

        var relogio = new RelogioDeTeste(Inicio);
        var conector = new ConectorFalso("erp", _ => ResultadoDoEnvio.FalhaTemporaria);
        var drenador = Criar(fila, [conector], relogio);

        await drenador.DrenarUmaVezAsync(CancellationToken.None);
        var segunda = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.False(segunda.TeveTrabalho);
        Assert.Single(conector.Lotes);

        relogio.Avancar(TimeSpan.FromSeconds(2));
        var terceira = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, terceira.Adiados);
        Assert.Equal(2, conector.Lotes.Count);
    }

    [Fact]
    public async Task Falha_permanente_vai_direto_para_cartas_mortas()
    {
        // Payload que o destino recusa não melhora com repetição: repetir só empurra a
        // fila inteira para trás.
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.DecisaoDeAcesso, Inicio);

        var drenador = Criar(fila, [new ConectorFalso("erp", _ => ResultadoDoEnvio.FalhaPermanente)]);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.CartasMortas);
        Assert.Empty(fila.Adiamentos);
        Assert.Equal("a", Assert.Single(fila.CartasMortas).Id);
    }

    [Fact]
    public async Task Esgotar_as_tentativas_manda_para_cartas_mortas_e_libera_a_fila()
    {
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.Metrica, Inicio, tentativas: 2);

        var drenador = Criar(
            fila,
            [new ConectorFalso("erp", _ => ResultadoDoEnvio.FalhaTemporaria)],
            maximoDeTentativas: 3);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.CartasMortas);
        Assert.Contains("esgotadas 3 tentativas", Assert.Single(fila.CartasMortas).Erro, StringComparison.Ordinal);
        Assert.Empty(fila.Pendentes());
    }

    [Fact]
    public async Task Item_sem_resposta_do_conector_e_repetido_nunca_dado_como_enviado()
    {
        // Silêncio não é aceitação. O custo de repetir é um duplicado que a chave de
        // idempotência descarta; o de assumir entrega é uma passagem que some do relatório.
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.PassagemFisica, Inicio);
        fila.Enfileirar("b", "erp", PrioridadeDeSincronizacao.PassagemFisica, Inicio.AddSeconds(1));

        var conector = new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito) { ResponderSoOPrimeiro = true };
        var drenador = Criar(fila, [conector]);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Enviados);
        Assert.Equal(1, resumo.Adiados);
        Assert.Equal(["b"], fila.Pendentes());
    }

    [Fact]
    public async Task Conector_que_lanca_excecao_nao_perde_o_lote()
    {
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp", PrioridadeDeSincronizacao.PassagemFisica, Inicio);
        fila.Enfileirar("b", "erp", PrioridadeDeSincronizacao.PassagemFisica, Inicio.AddSeconds(1));

        var conector = new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito)
        {
            Explodir = () => new HttpRequestException("destino fora do ar"),
        };

        var drenador = Criar(fila, [conector]);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(0, resumo.Enviados);
        Assert.Equal(2, resumo.Adiados);
        Assert.Empty(fila.CartasMortas);
        Assert.Equal(2, fila.Pendentes().Count);
    }

    [Fact]
    public async Task Um_conector_doente_nao_impede_o_saudavel()
    {
        // É a razão de a drenagem ser por conector: a bilheteria fora do ar não pode
        // segurar a contagem de lotação que vai para o painel.
        var fila = new FilaEmMemoria();
        fila.Enfileirar("doente", "bilheteria", PrioridadeDeSincronizacao.DecisaoDeAcesso, Inicio);
        fila.Enfileirar("saudavel", "painel", PrioridadeDeSincronizacao.PassagemFisica, Inicio);

        var drenador = Criar(
            fila,
            [
                new ConectorFalso("bilheteria", _ => ResultadoDoEnvio.Aceito)
                {
                    Explodir = () => new TimeoutException("sem resposta"),
                },
                new ConectorFalso("painel", _ => ResultadoDoEnvio.Aceito),
            ]);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(1, resumo.Enviados);
        Assert.Equal(1, resumo.Adiados);
        Assert.Equal(["saudavel"], fila.Enviados());
    }

    [Fact]
    public async Task Conector_nao_registrado_preserva_os_itens_intactos()
    {
        // Configuração faltando não é payload ruim. Mandar para cartas mortas aqui
        // destruiria a fila inteira por causa de um arquivo de configuração.
        var fila = new FilaEmMemoria();
        fila.Enfileirar("a", "erp-do-cliente", PrioridadeDeSincronizacao.DecisaoDeAcesso, Inicio);

        var drenador = Criar(fila, []);

        var resumo = await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(["erp-do-cliente"], resumo.ConectoresSemImplementacao);
        Assert.Empty(fila.CartasMortas);
        Assert.Equal(["a"], fila.Pendentes());
        Assert.False(resumo.TeveTrabalho);
    }

    [Fact]
    public async Task O_lote_respeita_o_limite_declarado_pelo_conector()
    {
        var fila = new FilaEmMemoria();
        for (var i = 0; i < 10; i++)
        {
            fila.Enfileirar($"i{i}", "erp", PrioridadeDeSincronizacao.Historico, Inicio.AddSeconds(i));
        }

        var conector = new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito, tamanhoMaximoDoLote: 3);
        var drenador = Criar(fila, [conector]);

        await drenador.DrenarUmaVezAsync(CancellationToken.None);

        Assert.Equal(3, Assert.Single(conector.Lotes).Count);
        Assert.Equal(7, fila.Pendentes().Count);
    }

    [Fact]
    public async Task Fila_vazia_nao_chama_conector_nenhum()
    {
        var fila = new FilaEmMemoria();
        var conector = new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito);

        var resumo = await Criar(fila, [conector]).DrenarUmaVezAsync(CancellationToken.None);

        Assert.False(resumo.TeveTrabalho);
        Assert.Empty(conector.Lotes);
        Assert.Equal(0, fila.Consultas);
    }

    [Fact]
    public void Dois_conectores_com_o_mesmo_nome_sao_recusados_na_construcao()
    {
        // O nome é a chave de roteamento gravada na outbox. Ambiguidade aqui manda
        // evento para o destino errado, em silêncio.
        var erro = Assert.Throws<ArgumentException>(() => Criar(
            new FilaEmMemoria(),
            [
                new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito),
                new ConectorFalso("erp", _ => ResultadoDoEnvio.Aceito),
            ]));

        Assert.Contains("mesmo nome", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prioridade_fora_da_faixa_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PrioridadeDeSincronizacao.Exigir(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => PrioridadeDeSincronizacao.Exigir(-1));

        PrioridadeDeSincronizacao.Exigir(PrioridadeDeSincronizacao.BloqueioEmergencial);
        PrioridadeDeSincronizacao.Exigir(PrioridadeDeSincronizacao.Historico);
    }

    private static DrenadorDaOutbox Criar(
        FilaEmMemoria fila,
        IEnumerable<IConectorDeSincronizacao> conectores,
        TimeProvider? relogio = null,
        int maximoDeTentativas = 12) =>
        new(fila, conectores, EsperaFixa, relogio ?? new RelogioDeTeste(Inicio), maximoDeTentativas);
}
