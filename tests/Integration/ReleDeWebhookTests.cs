using Microsoft.Data.Sqlite;
using Relay.Ingressos;

namespace Integration.Tests;

/// <summary>
/// O relé que recebe o webhook do provedor e guarda até a borda vir buscar.
/// </summary>
/// <remarks>
/// Ele não sabe o que é um ingresso — guarda bytes. É isso que faz uma mudança de formato
/// não derrubar o recebimento. Ver docs/ADR/ADR-0022-rele-de-webhook.md
/// </remarks>
public sealed class ReleDeWebhookTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly string _diretorio;
    private readonly ArmazenamentoDeEntregas _armazenamento;
    private readonly string _caminho;

    public ReleDeWebhookTests()
    {
        _diretorio = Path.Combine(Path.GetTempPath(), "rele-testes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_diretorio);
        _caminho = Path.Combine(_diretorio, "entregas.db");
        _armazenamento = new ArmazenamentoDeEntregas(_caminho);
        _armazenamento.Preparar();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_diretorio, recursive: true);
        }
        catch (IOException)
        {
            // Sobra de temporário não reprova teste.
        }
    }

    private long Gravar(string corpo, DateTimeOffset? quando = null) =>
        _armazenamento.Gravar(
            System.Text.Encoding.UTF8.GetBytes(corpo),
            "application/json",
            new Dictionary<string, string> { ["User-Agent"] = "zet-webhook/1" },
            quando ?? Agora);

    [Fact]
    public void Preparar_e_idempotente()
    {
        _armazenamento.Preparar();
        _armazenamento.Preparar();

        Assert.Equal(0, _armazenamento.UltimaSequencia());
    }

    [Fact]
    public void Entregas_saem_na_ordem_em_que_chegaram()
    {
        Gravar("""{"venda":1}""");
        Gravar("""{"venda":2}""");
        Gravar("""{"venda":3}""");

        var entregas = _armazenamento.Desde(0, 10);

        Assert.Equal([1L, 2L, 3L], entregas.Select(e => e.Seq));
        Assert.Equal(3, _armazenamento.UltimaSequencia());
    }

    [Fact]
    public void O_cursor_so_devolve_o_que_veio_depois()
    {
        Gravar("""{"venda":1}""");
        Gravar("""{"venda":2}""");

        var depoisDaPrimeira = _armazenamento.Desde(1, 10);

        Assert.Equal(2L, Assert.Single(depoisDaPrimeira).Seq);
    }

    [Fact]
    public void O_limite_de_pagina_e_respeitado()
    {
        for (var i = 0; i < 10; i++)
        {
            Gravar($$"""{"venda":{{i}}}""");
        }

        Assert.Equal(3, _armazenamento.Desde(0, 3).Count);
    }

    [Fact]
    public void O_corpo_volta_byte_a_byte_identico()
    {
        // O relé não interpreta. Se o provedor mandar bytes que não são UTF-8 válido —
        // e provedor manda — eles precisam voltar intactos, porque quem decide o que
        // fazer com isso é a borda, depois.
        byte[] bruto = [0x7B, 0xFF, 0xFE, 0x00, 0x41, 0x7D];

        _armazenamento.Gravar(bruto, "application/octet-stream", new Dictionary<string, string>(), Agora);

        var entrega = Assert.Single(_armazenamento.Desde(0, 10));
        Assert.Equal(bruto, Convert.FromBase64String(entrega.CorpoBase64));
    }

    [Fact]
    public void Uma_entrega_nao_pode_ser_alterada_nem_removida()
    {
        // É a prova de que o provedor entregou. Sem isso ela não vale numa discussão de
        // prestação de contas.
        Gravar("""{"venda":1}""");

        using var conexao = new SqliteConnection($"Data Source={_caminho}");
        conexao.Open();

        var alteracao = Assert.Throws<SqliteException>(() => Executar(conexao, "UPDATE delivery SET body = X'00';"));
        var remocao = Assert.Throws<SqliteException>(() => Executar(conexao, "DELETE FROM delivery;"));

        Assert.Contains("somente insercao", alteracao.Message, StringComparison.Ordinal);
        Assert.Contains("somente insercao", remocao.Message, StringComparison.Ordinal);
        Assert.Single(_armazenamento.Desde(0, 10));
    }

    [Fact]
    public void Os_cabecalhos_ficam_guardados_menos_os_que_carregam_segredo()
    {
        Assert.True(Cabecalhos.EhSensivel("Authorization"));
        Assert.True(Cabecalhos.EhSensivel("X-Api-Key"));
        Assert.True(Cabecalhos.EhSensivel("x-zet-token"));
        Assert.True(Cabecalhos.EhSensivel("Cookie"));
        Assert.True(Cabecalhos.EhSensivel("X-Client-Secret"));

        Assert.False(Cabecalhos.EhSensivel("User-Agent"));
        Assert.False(Cabecalhos.EhSensivel("Content-Type"));
        Assert.False(Cabecalhos.EhSensivel("X-Request-Id"));
    }

    [Fact]
    public void O_segredo_e_comparado_sem_vazar_pelo_tempo_e_recusa_o_que_nao_confere()
    {
        const string bom = "0123456789abcdef0123456789abcdef";

        Assert.True(Segredos.Conferem(bom, bom));
        Assert.False(Segredos.Conferem("0123456789abcdef0123456789abcdee", bom));
        Assert.False(Segredos.Conferem("0123456789abcdef", bom));
        Assert.False(Segredos.Conferem(null, bom));
        Assert.False(Segredos.Conferem("", bom));
    }

    [Fact]
    public void O_rele_nao_sobe_com_segredo_curto()
    {
        // A URL do webhook É a credencial: a tela de cadastro do provedor não oferece
        // campo de assinatura. Subir com segredo fraco é pior que não subir.
        var erro = Assert.Throws<InvalidOperationException>(
            () => Segredos.Exigir("curto-demais", "RELE_TOKEN_ENTRADA"));

        Assert.Contains("32 caracteres", erro.Message, StringComparison.Ordinal);
        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            Segredos.Exigir("0123456789abcdef0123456789abcdef", "RELE_TOKEN_ENTRADA"));
    }

    private static void Executar(SqliteConnection conexao, string sql)
    {
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        comando.ExecuteNonQuery();
    }
}
