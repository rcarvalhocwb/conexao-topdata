using Access.Domain.Devices;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;
using Microsoft.Data.Sqlite;
using static Access.Infrastructure.SQLite.RepositorioDeIngressos;

namespace Integration.Tests;

/// <summary>
/// Cadastro de cartões na operação (migração 026): sessão de lote por leitura na urna e cadastro de um
/// cartão recusado como desconhecido. Regras: a leitura durante a sessão nunca libera; o cartão do lote
/// não é consumido na urna durante a sessão; a lista de recusados não mostra o código.
/// </summary>
public sealed class CadastroPorLeituraTests : IDisposable
{
    private const string Balcao = "balcao-local";
    private const string Operador = "Maria Operadora";
    private static readonly DateTimeOffset Agora = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _repo;

    public CadastroPorLeituraTests()
    {
        _banco.Migrar();
        _repo = new RepositorioDeIngressos(_banco.Fabrica);
        _repo.RegistrarProvedor(new ProvedorDeIngresso(Balcao, "Bilheteria local", "raw", "", Reutilizavel: true), Agora);
        _repo.RegistrarProvedor(new ProvedorDeIngresso("online", "Online", "raw", "zet-rest"), Agora);
    }

    public void Dispose() => _banco.Dispose();

    private (ResultadoDoUso Resultado, Guid TentativaId) Ler(string codigo, KnownEventOrigin leitor, int segundos = 0) =>
        _repo.TentarUsar(codigo, "portao-1", "catraca-01", Agora.AddSeconds(segundos), leitor: leitor);

    private string Texto(string sql)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return Convert.ToString(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    [Fact]
    public void Sessao_so_abre_com_provedor_reutilizavel_e_so_uma_por_vez()
    {
        Assert.Equal(ResultadoDaAberturaDeSessao.Aberta, _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento 1", Operador, 1, Agora));
        Assert.Equal(ResultadoDaAberturaDeSessao.JaAberta, _repo.AbrirSessaoDeCadastro(Balcao, "MEIA", "Meia 1", Operador, 1, Agora));
        Assert.Equal(ResultadoDaAberturaDeSessao.ProvedorNaoReutilizavel, _repo.AbrirSessaoDeCadastro("online", "INTEIRA", "x", Operador, 1, Agora));
        Assert.Equal(ResultadoDaAberturaDeSessao.ProvedorDesconhecido, _repo.AbrirSessaoDeCadastro("nao-existe", "INTEIRA", "x", Operador, 1, Agora));
    }

    [Fact]
    public void Dados_invalidos_nao_abrem_sessao()
    {
        Assert.Equal(ResultadoDaAberturaDeSessao.DadosInvalidos, _repo.AbrirSessaoDeCadastro(Balcao, "inteira", "Evento", Operador, 1, Agora));
        Assert.Equal(ResultadoDaAberturaDeSessao.DadosInvalidos, _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "   ", Operador, 1, Agora));
        Assert.Equal(ResultadoDaAberturaDeSessao.DadosInvalidos, _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento", "X", 1, Agora));
        Assert.Equal(ResultadoDaAberturaDeSessao.DadosInvalidos, _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento", Operador, 0, Agora));
        Assert.Null(_repo.SessaoDeCadastroAberta());
    }

    [Fact]
    public void Desconhecido_lido_na_urna_durante_a_sessao_entra_no_lote_e_nao_libera()
    {
        _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento 1", Operador, 1, Agora);

        var (resultado, _) = Ler("A1B2C3D4", KnownEventOrigin.Leitor2);

        Assert.False(resultado.Liberou);
        Assert.Equal(MotivoDoUso.CadastradoNoLote, resultado.Motivo);
        Assert.Equal("INTEIRA", resultado.Categoria);
        Assert.Equal(1, _repo.SessaoDeCadastroAberta()!.Cadastrados);

        // Cartão criado como fonte local, com autoria e lote.
        Assert.Equal("cartao_bilheteria", Texto("SELECT kind FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
        Assert.Equal("manual", Texto("SELECT source FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
        Assert.Equal("local", Texto("SELECT owner_of_fields FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
        Assert.Equal(Operador, Texto("SELECT created_by FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
        Assert.Equal("Evento 1", Texto("SELECT batch_label FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
    }

    [Fact]
    public void Desconhecido_lido_na_frente_durante_a_sessao_nao_cadastra()
    {
        _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento 1", Operador, 1, Agora);

        var (resultado, _) = Ler("A1B2C3D4", KnownEventOrigin.Leitor1);

        Assert.Equal(MotivoDoUso.Desconhecido, resultado.Motivo);
        Assert.Equal(0, _repo.SessaoDeCadastroAberta()!.Cadastrados);
        Assert.Equal("0", Texto("SELECT COUNT(*) FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
    }

    [Fact]
    public void Cartao_do_lote_lido_de_novo_na_sessao_so_registra_e_nao_consome()
    {
        _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento 1", Operador, 1, Agora);
        Ler("A1B2C3D4", KnownEventOrigin.Leitor2);

        var (segunda, _) = Ler("A1B2C3D4", KnownEventOrigin.Leitor2, 5);

        Assert.False(segunda.Liberou);
        Assert.Equal(MotivoDoUso.CadastradoNoLote, segunda.Motivo);
        // O uso continua intacto: ninguém passou.
        Assert.Equal("0", Texto("SELECT used_count FROM ticket WHERE qr_normalized = 'A1B2C3D4';"));
    }

    [Fact]
    public void Depois_de_fechar_a_sessao_o_cartao_do_lote_passa_normalmente()
    {
        _repo.AbrirSessaoDeCadastro(Balcao, "INTEIRA", "Evento 1", Operador, 1, Agora);
        Ler("A1B2C3D4", KnownEventOrigin.Leitor2);

        var fechada = _repo.FecharSessaoDeCadastro(Agora.AddMinutes(1));
        Assert.Equal(1, fechada!.Cadastrados);
        Assert.Null(_repo.SessaoDeCadastroAberta());

        var (passou, _) = Ler("A1B2C3D4", KnownEventOrigin.Leitor2, 120);
        Assert.True(passou.Liberou);
        Assert.Equal("INTEIRA", passou.Categoria);
    }

    [Fact]
    public void Sem_sessao_o_desconhecido_da_urna_continua_desconhecido_e_aparece_mascarado()
    {
        var (resultado, _) = Ler("A1B2C3D4", KnownEventOrigin.Leitor2);

        Assert.Equal(MotivoDoUso.Desconhecido, resultado.Motivo);
        var item = Assert.Single(_repo.CartoesNaoReconhecidos());
        Assert.Equal(1, item.Vezes);
        Assert.DoesNotContain("A1B2C3D4", item.CodigoMascarado, StringComparison.Ordinal);
    }

    [Fact]
    public void Cadastrar_recusado_usa_o_id_da_tentativa_e_depois_o_cartao_passa()
    {
        var (_, tentativa) = Ler("99887766", KnownEventOrigin.Leitor1);
        var item = Assert.Single(_repo.CartoesNaoReconhecidos());
        Assert.Equal(tentativa, item.TentativaId);

        var (resultado, ingresso) = _repo.CadastrarNaoReconhecido(tentativa, Balcao, "MEIA", "Meia lote 2", Operador, 1, Agora.AddSeconds(5));

        Assert.Equal(ResultadoDoCadastroDeCartao.Cadastrado, resultado);
        Assert.NotNull(ingresso);
        Assert.Empty(_repo.CartoesNaoReconhecidos());

        var (passou, _) = Ler("99887766", KnownEventOrigin.Leitor2, 60);
        Assert.True(passou.Liberou);
        Assert.Equal("MEIA", passou.Categoria);
        Assert.Equal(Operador, Texto("SELECT created_by FROM ticket WHERE qr_normalized = '99887766';"));
    }

    [Fact]
    public void Cadastrar_duas_vezes_recusa_a_segunda()
    {
        var (_, tentativa) = Ler("99887766", KnownEventOrigin.Leitor1);
        _repo.CadastrarNaoReconhecido(tentativa, Balcao, "MEIA", "Meia lote 2", Operador, 1, Agora);

        var (segunda, _) = _repo.CadastrarNaoReconhecido(tentativa, Balcao, "MEIA", "Meia lote 2", Operador, 1, Agora);

        Assert.Equal(ResultadoDoCadastroDeCartao.JaCadastrado, segunda);
    }

    [Fact]
    public void Cadastrar_com_tentativa_inexistente_ou_dados_invalidos_nao_grava()
    {
        Assert.Equal(ResultadoDoCadastroDeCartao.TentativaNaoEncontrada,
            _repo.CadastrarNaoReconhecido(Guid.NewGuid(), Balcao, "MEIA", "Lote", Operador, 1, Agora).Resultado);

        var (_, tentativa) = Ler("99887766", KnownEventOrigin.Leitor1);
        Assert.Equal(ResultadoDoCadastroDeCartao.DadosInvalidos,
            _repo.CadastrarNaoReconhecido(tentativa, Balcao, "meia", "Lote", Operador, 1, Agora).Resultado);
        Assert.Equal("0", Texto("SELECT COUNT(*) FROM ticket WHERE qr_normalized = '99887766';"));
    }
}
