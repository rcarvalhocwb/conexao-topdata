using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>P6: transações reais, decisão de acesso, dados cifrados e reutilização segura de credenciais.</summary>
public sealed class VisitasTests : IDisposable
{
    internal static readonly DateTimeOffset Agora = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private readonly BancoTemporario _banco = new();
    private readonly CifraDeDadosPessoais _cifra = new(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave));
    private readonly CadastroDePessoas _pessoas;
    private readonly Visitas _visitas;
    private readonly RepositorioDeIngressos _ingressos;
    private readonly string _host;

    public VisitasTests()
    {
        _banco.Migrar();
        _pessoas = new CadastroDePessoas(_banco.Fabrica, _cifra);
        _visitas = new Visitas(_banco.Fabrica, _cifra);
        _ingressos = new RepositorioDeIngressos(_banco.Fabrica, new EspelhoDeTentativas("nuvem", TimeSpan.Zero));
        _host = _pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Beatriz Lima" }, Agora).Id!;
    }

    public void Dispose() => _banco.Dispose();

    private string Agendar(DateTimeOffset? de = null, DateTimeOffset? ate = null, string nome = "Rafaela Souza")
    {
        var r = _visitas.Agendar("usuario-anfitriao", nome, _host, "Reunião de trabalho", de ?? Agora, ate ?? Agora.AddHours(1), Agora);
        Assert.True(r.Gravado, string.Join(" ", r.Problemas));
        return r.Id!;
    }

    private ResultadoDoCadastro Receber(string id, string codigo = "77001234", string documento = "RG-FICTICIO-001", DateTimeOffset? agora = null) =>
        _visitas.Receber("recepcao", id, "rg", documento, true, "cartao", codigo, agora ?? Agora);

    private ResultadoDoUso Ler(string codigo = "77001234", DateTimeOffset? agora = null) =>
        _ingressos.TentarUsar(codigo, "portao-1", "inner-1", agora ?? Agora).Resultado;

    private long Contar(string sql)
    {
        using var c = _banco.Fabrica.Abrir();
        using var s = c.CreateCommand();
        s.CommandText = sql;
        return Convert.ToInt64(s.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Pre_cadastro_nao_libera_chegada_conferida_libera_e_expira_sem_laco_de_limpeza()
    {
        var id = Agendar();
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person;")); // Só o anfitrião.
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM person_credential;"));
        Assert.False(Ler().Liberou);
        var pendentesAntesDaChegada = Contar("SELECT COUNT(*) FROM outbox;"); // A leitura ainda desconhecida segue o fluxo normal.

        Assert.False(_visitas.Receber("recepcao", id, "rg", "", true, "cartao", "77001234", Agora).Gravado);
        Assert.False(_visitas.Receber("recepcao", id, "rg", "RG-FICTICIO-001", false, "cartao", "77001234", Agora).Gravado);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person;"));
        Assert.True(Receber(id).Gravado);
        Assert.True(Ler().Liberou);
        Assert.Equal(MotivoDoUso.ForaDaValidade, Ler(agora: Agora.AddHours(1).AddTicks(1)).Motivo);
        Assert.Equal(MotivoDoUso.ForaDaValidade, Ler(agora: Agora.AddTicks(-1)).Motivo);
        Assert.Equal("expirada", Assert.Single(_visitas.Listar(Agora.AddHours(2))).Situacao);
        Assert.Equal(pendentesAntesDaChegada, Contar("SELECT COUNT(*) FROM outbox;"));
    }

    [Fact]
    public void Saida_nega_no_mesmo_instante_e_nao_pode_ser_reaberta_alterando_a_credencial()
    {
        var id = Agendar();
        Assert.True(Receber(id).Gravado);
        var visita = Assert.Single(_visitas.Listar(Agora));
        var credencial = Assert.Single(_pessoas.Obter(visita.PessoaId!)!.Credenciais);
        Assert.True(_visitas.Encerrar("recepcao", id, Agora).Gravado);
        Assert.False(Ler().Liberou);
        Assert.False(_visitas.Encerrar("recepcao", id, Agora).Gravado);
        Assert.False(Receber(id, "88001234").Gravado);
        Assert.True(_pessoas.MudarCredencial("admin", credencial.Id, "ativa", null, Agora).Gravado);
        Assert.Equal(MotivoDoUso.ForaDaValidade, Ler().Motivo);
        Assert.Equal("encerrada", Assert.Single(_visitas.Listar(Agora, "encerrada")).Situacao);
    }

    [Theory]
    [InlineData("bloqueado")]
    [InlineData("inativo")]
    public void Anfitriao_bloqueado_ou_inativo_nao_agenda_nem_entrega_credencial(string situacao)
    {
        var id = Agendar();
        Assert.True(_pessoas.MudarSituacao("admin", _host, situacao, "Indisponível para visitas", Agora).Gravado);
        Assert.False(_visitas.Agendar("host", "Outra visitante", _host, "Reunião", Agora, Agora.AddHours(1), Agora).Gravado);
        Assert.False(Receber(id).Gravado);
        Assert.Empty(_visitas.Anfitrioes());
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM person_credential;"));
        Assert.Equal("agendada", Assert.Single(_visitas.Listar(Agora)).Situacao);
    }

    [Fact]
    public void Janela_invalida_passada_ou_chegada_fora_da_janela_nao_cria_credencial()
    {
        Assert.False(_visitas.Agendar("host", "Rafaela Souza", _host, "Reunião", Agora.AddHours(1), Agora, Agora).Gravado);
        Assert.False(_visitas.Agendar("host", "Rafaela Souza", _host, "Reunião", Agora.AddHours(-2), Agora.AddHours(-1), Agora).Gravado);
        var id = Agendar(Agora.AddHours(1), Agora.AddHours(2));
        Assert.False(Receber(id).Gravado);
        Assert.False(Receber(id, agora: Agora.AddHours(3)).Gravado);
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM person_credential;"));
        Assert.False(_visitas.Encerrar("recepcao", id, Agora).Gravado);
    }

    [Fact]
    public async Task Duas_chegadas_concorrentes_entregam_uma_unica_credencial_e_um_unico_evento()
    {
        var id = Agendar();
        var resultados = await Task.WhenAll(Task.Run(() => Receber(id)), Task.Run(() => Receber(id, "88001234")));
        Assert.Single(resultados, r => r.Gravado);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person_credential;"));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person_event WHERE action = 'visita.chegada';"));
    }

    [Fact]
    public void Codigo_invalido_desfaz_pessoa_trilha_e_chegada_e_cpf_invalido_nao_entrega()
    {
        var id = Agendar();
        Assert.False(Receber(id, "codigo-invalido").Gravado);
        Assert.False(_visitas.Receber("recepcao", id, "cpf", "11111111111", true, "cartao", "77001234", Agora).Gravado);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person;"));
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM person_event WHERE action IN ('visita.chegada', 'credencial.adicionar');"));
        Assert.Null(Assert.Single(_visitas.Listar(Agora)).Chegada);
    }

    [Fact]
    public void Cracha_provisorio_so_e_reutilizado_apos_saida_e_muda_para_a_nova_visita()
    {
        var primeira = Agendar();
        Assert.True(Receber(primeira).Gravado);
        var segunda = Agendar(nome: "Luciana Prado");
        Assert.False(Receber(segunda, documento: "RG-FICTICIO-002").Gravado);
        Assert.Equal(2, Contar("SELECT COUNT(*) FROM person;")); // Recebimento recusado desfez a nova pessoa.
        Assert.True(_visitas.Encerrar("recepcao", primeira, Agora).Gravado);
        Assert.True(Receber(segunda, documento: "RG-FICTICIO-002").Gravado);
        Assert.True(Ler().Liberou);
        Assert.Equal(Assert.Single(_visitas.Listar(Agora, "em_visita")).PessoaId, Ler().PessoaId);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person_credential;"));
        Assert.Equal("encerrada", Assert.Single(_visitas.Listar(Agora, "encerrada")).Situacao);
    }

    [Theory]
    [InlineData("perdida")]
    [InlineData("bloqueada")]
    public void Credencial_perdida_ou_bloqueada_nao_e_reativada_pela_recepcao(string situacao)
    {
        var id = Agendar();
        Assert.True(Receber(id).Gravado);
        var pessoa = Assert.Single(_visitas.Listar(Agora)).PessoaId!;
        var credencial = Assert.Single(_pessoas.Obter(pessoa)!.Credenciais).Id;
        Assert.True(_visitas.Encerrar("recepcao", id, Agora).Gravado);
        Assert.True(_pessoas.MudarCredencial("admin", credencial, situacao, "Credencial indisponível", Agora).Gravado);
        Assert.False(Receber(Agendar(nome: "Luciana Prado"), documento: "RG-FICTICIO-002").Gravado);
        Assert.Equal(situacao, Assert.Single(_pessoas.Obter(pessoa)!.Credenciais).Situacao);
    }

    [Fact]
    public void Visitante_que_volta_mantem_a_ficha_sem_duplicar_documento_e_nao_reativa_bloqueio()
    {
        var primeira = Agendar();
        Assert.True(Receber(primeira).Gravado);
        var pessoa = Assert.Single(_visitas.Listar(Agora)).PessoaId!;
        Assert.True(_visitas.Encerrar("recepcao", primeira, Agora).Gravado);
        var segunda = Agendar(Agora.AddDays(1), Agora.AddDays(1).AddHours(1), "Nome digitado diferente");
        Assert.True(Receber(segunda, agora: Agora.AddDays(1)).Gravado);
        Assert.Equal(2, Contar("SELECT COUNT(*) FROM person;"));
        Assert.Equal(pessoa, Ler(agora: Agora.AddDays(1)).PessoaId);
        Assert.Equal("Rafaela Souza", Assert.Single(_visitas.Listar(Agora.AddDays(1), "em_visita")).Nome);
        Assert.True(_visitas.Encerrar("recepcao", segunda, Agora.AddDays(1)).Gravado);
        Assert.True(_pessoas.MudarSituacao("admin", pessoa, "bloqueado", "Acesso suspenso", Agora.AddDays(1)).Gravado);
        var terceira = Agendar(Agora.AddDays(2), Agora.AddDays(2).AddHours(1));
        Assert.False(Receber(terceira, agora: Agora.AddDays(2)).Gravado);
    }

    [Fact]
    public void Cartao_pessoal_e_documento_de_outro_perfil_nao_sao_convertidos_em_visita()
    {
        Assert.True(_pessoas.AdicionarCredencial("admin", _host, "cartao", "88001234", null, null, Agora).Gravado);
        var id = Agendar();
        Assert.False(Receber(id, "88001234").Gravado);
        Assert.Equal(_host, Ler("88001234").PessoaId);
        var outraPessoa = _pessoas.Gravar("admin", new DadosDaPessoa
        {
            PerfilId = "aluno", NomeCompleto = "Luciana Prado", TipoDoDocumento = "rg", Documento = "RG-FICTICIO-001",
        }, Agora).Id!;
        Assert.False(Receber(id).Gravado);
        Assert.Equal("aluno", _pessoas.Obter(outraPessoa)!.Dados.PerfilId);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person_credential;"));
    }

    [Fact]
    public void Perfil_sem_exigencia_de_documento_permanece_configuravel()
    {
        using (var c = _banco.Fabrica.Abrir())
        {
            SqliteConnectionFactory.Executar(c, "UPDATE person_profile SET required_fields = '[\"nome\",\"anfitriao\"]' WHERE id = 'visitante';");
        }

        Assert.True(_visitas.Receber("recepcao", Agendar(), "rg", "", false, "cartao", "77001234", Agora).Gravado);
        Assert.True(Ler().Liberou);
    }

    [Fact]
    public void Retorno_confere_os_campos_atuais_do_perfil_e_preserva_telefone_e_catracas_da_ficha()
    {
        var primeira = Agendar();
        Assert.True(Receber(primeira).Gravado);
        var pessoa = Assert.Single(_visitas.Listar(Agora)).PessoaId!;
        Assert.True(_visitas.Encerrar("recepcao", primeira, Agora).Gravado);
        using (var c = _banco.Fabrica.Abrir())
        {
            SqliteConnectionFactory.Executar(c, "UPDATE person_profile SET required_fields = '[\"nome\",\"documento\",\"anfitriao\",\"telefone\"]' WHERE id = 'visitante';");
        }

        var segunda = Agendar();
        Assert.Contains("Telefone", string.Join(" ", Receber(segunda).Problemas), StringComparison.Ordinal);
        var dados = _pessoas.Obter(pessoa)!.Dados;
        Assert.True(_pessoas.Gravar("admin", dados with { Telefone = "41 98888-7777", Catracas = [1] }, Agora).Gravado);
        Assert.True(Receber(segunda).Gravado);
        Assert.Equal("41 98888-7777", _pessoas.Obter(pessoa)!.Dados.Telefone);
        Assert.Equal([1], _pessoas.Obter(pessoa)!.Dados.Catracas);
        Assert.True(Ler().Liberou);
    }

    [Fact]
    public void Nome_motivo_documento_e_trilha_nao_vazam_em_claro_e_a_busca_do_anfitriao_funciona()
    {
        var id = Agendar();
        Assert.True(Receber(id).Gravado);
        Assert.Single(_visitas.Anfitrioes("beat"));
        Assert.Empty(_visitas.Anfitrioes("inexistente"));
        using (var c = _banco.Fabrica.Abrir())
        {
            using var s = c.CreateCommand();
            s.CommandText = "SELECT detail FROM person_event WHERE action LIKE 'visita.%';";
            using var l = s.ExecuteReader();
            while (l.Read())
            {
                Assert.DoesNotContain("Rafaela", l.GetString(0), StringComparison.Ordinal);
                Assert.DoesNotContain("RG-FICTICIO", l.GetString(0), StringComparison.Ordinal);
                Assert.DoesNotContain("77001234", l.GetString(0), StringComparison.Ordinal);
            }
        }

        foreach (var arquivo in Directory.GetFiles(Path.GetDirectoryName(_banco.Caminho)!, "acesso.db*"))
        {
            var texto = Encoding.UTF8.GetString(File.ReadAllBytes(arquivo));
            Assert.DoesNotContain("Rafaela Souza", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Beatriz Lima", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("RG-FICTICIO-001", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Reunião de trabalho", texto, StringComparison.Ordinal);
        }

        var semChave = new Visitas(_banco.Fabrica, new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(32)));
        Assert.Equal("(nome ilegível)", Assert.Single(semChave.Listar(Agora)).Nome);
        var pendente = Agendar(nome: "Luciana Prado");
        Assert.ThrowsAny<CryptographicException>(() => semChave.Receber("recepcao", pendente, "rg", "RG-FICTICIO-002", true, "cartao", "88001234", Agora));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM person_credential;"));
    }
}
