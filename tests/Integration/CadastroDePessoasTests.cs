using System.Security.Cryptography;
using System.Text;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;
using Access.Infrastructure.SQLite;

namespace Integration.Tests;

/// <summary>
/// Cadastro local de pessoas (docs/43, ADR-0026, migração 021): a decisão na catraca, a cifra dos dados
/// pessoais, a busca e as regras do formulário.
/// </summary>
public sealed class CadastroDePessoasTests : IDisposable
{
    // Segunda-feira, 16/11/2026, 09:00 em Brasília.
    private static readonly DateTimeOffset Segunda9h = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly RepositorioDeIngressos _ingressos;
    private readonly CadastroDePessoas _pessoas;
    private readonly ParametrosDoCadastro _parametros;
    private readonly CifraDeDadosPessoais _cifra = new(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave));

    public CadastroDePessoasTests()
    {
        _banco.Migrar();
        _ingressos = new RepositorioDeIngressos(_banco.Fabrica, new EspelhoDeTentativas("nuvem", TimeSpan.Zero));
        _pessoas = new CadastroDePessoas(_banco.Fabrica, _cifra);
        _parametros = new ParametrosDoCadastro(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private string Cadastrar(string nome = "José da Silva Araújo", string perfil = "colaborador", Func<DadosDaPessoa, DadosDaPessoa>? ajuste = null)
    {
        var empresa = _parametros.GravarEmpresa("admin", new Empresa(null, "Acme", null), Segunda9h).Id;
        var dados = new DadosDaPessoa { PerfilId = perfil, NomeCompleto = nome, EmpresaId = empresa };
        var resultado = _pessoas.Gravar("admin", ajuste?.Invoke(dados) ?? dados, Segunda9h);
        Assert.True(resultado.Gravado, string.Join(" ", resultado.Problemas));
        return resultado.Id!;
    }

    private void DarCartao(string pessoa, string codigo)
    {
        var resultado = _pessoas.AdicionarCredencial("admin", pessoa, "cartao", codigo, null, null, Segunda9h);
        Assert.True(resultado.Gravado, string.Join(" ", resultado.Problemas));
    }

    private (ResultadoDoUso Resultado, Guid Tentativa) Ler(string codigo, DateTimeOffset? em = null, int inner = 1) =>
        _ingressos.TentarUsar(codigo, $"portao-{inner}", $"inner-{inner}", em ?? Segunda9h);

    private long Contar(string sql)
    {
        using var conexao = _banco.Fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        return Convert.ToInt64(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Pessoa_ativa_entra_e_a_tentativa_fica_so_aqui_com_a_pessoa()
    {
        var pessoa = Cadastrar();
        DarCartao(pessoa, "12345678");

        var (resultado, _) = Ler("12345678");

        Assert.True(resultado.Liberou, resultado.Motivo.ToString());
        Assert.Equal(pessoa, resultado.PessoaId);
        Assert.Equal("Colaborador", resultado.Categoria);
        Assert.Null(resultado.IngressoId);
        Assert.Equal(1, Contar($"SELECT COUNT(*) FROM ticket_use_attempt WHERE person_id = '{pessoa}' AND outcome = 'consumido' AND ticket_id IS NULL;"));

        // Nunca para a nuvem: nem a liberação, nem a negativa.
        _pessoas.MudarSituacao("admin", pessoa, "bloqueado", "Crachá retido", Segunda9h);
        Ler("12345678");
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM outbox;"));

        // E não aparece como código desconhecido nem como uso a estornar.
        Assert.Equal((0, 0), _ingressos.QrDesconhecidos(Segunda9h.AddDays(1)));
        Assert.Empty(new EstornosDeUso(_banco.Fabrica).Listar(Segunda9h.AddHours(1)));
    }

    [Fact]
    public void Bloquear_e_desbloquear_valem_na_leitura_seguinte()
    {
        var pessoa = Cadastrar();
        DarCartao(pessoa, "555");

        Assert.False(_pessoas.MudarSituacao("admin", pessoa, "bloqueado", "x", Segunda9h).Gravado);
        Assert.True(_pessoas.MudarSituacao("admin", pessoa, "bloqueado", "Inadimplente no mês", Segunda9h).Gravado);
        Assert.Equal(MotivoDoUso.PessoaBloqueada, Ler("555").Resultado.Motivo);

        _pessoas.MudarSituacao("admin", pessoa, "ativo", null, Segunda9h);
        Assert.True(Ler("555").Resultado.Liberou);

        _pessoas.MudarSituacao("admin", pessoa, "inativo", "Desligado da empresa", Segunda9h);
        Assert.Equal(MotivoDoUso.PessoaInativa, Ler("555").Resultado.Motivo);

        Assert.Equal(3, Contar($"SELECT COUNT(*) FROM person_event WHERE person_id = '{pessoa}' AND action LIKE 'pessoa.%' AND action <> 'pessoa.criar';"));
    }

    [Fact]
    public void Credencial_perdida_nega_e_a_nova_entra()
    {
        var pessoa = Cadastrar();
        DarCartao(pessoa, "111");
        var antiga = _pessoas.Obter(pessoa)!.Credenciais.Single();

        Assert.True(_pessoas.MudarCredencial("admin", antiga.Id, "perdida", "Perdeu o crachá", Segunda9h).Gravado);
        DarCartao(pessoa, "222");

        Assert.Equal(MotivoDoUso.CredencialInativa, Ler("111").Resultado.Motivo);
        Assert.True(Ler("222").Resultado.Liberou);
    }

    [Fact]
    public void Visitante_vale_so_no_dia_pela_validade_padrao_do_perfil()
    {
        var anfitriao = Cadastrar("Maria Recebe");
        var visitante = Cadastrar("Carlos Visita", "visitante", d => d with
        {
            TipoDoDocumento = "rg",
            Documento = "12.345.678-9",
            AnfitriaoId = anfitriao,
        });
        DarCartao(visitante, "900");

        Assert.True(Ler("900").Resultado.Liberou);

        // Validade padrão 0 dia: até 23:59:59 de hoje em Brasília (02:59:59 UTC do dia seguinte).
        Assert.True(Ler("900", Segunda9h.AddHours(14).AddMinutes(59)).Resultado.Liberou);
        Assert.Equal(MotivoDoUso.ForaDaValidade, Ler("900", Segunda9h.AddHours(15)).Resultado.Motivo);
    }

    [Fact]
    public void Catracas_da_pessoa_sobrepoem_as_do_perfil()
    {
        var perfil = _parametros.Perfis().Single(p => p.Id == "colaborador");
        Assert.True(_parametros.GravarPerfil("admin", perfil with { Catracas = [1] }, Segunda9h).Gravado);

        var comum = Cadastrar("Ana Comum");
        var especial = Cadastrar("Beto Especial", ajuste: d => d with { Catracas = [2] });
        DarCartao(comum, "301");
        DarCartao(especial, "302");

        Assert.True(Ler("301", inner: 1).Resultado.Liberou);
        Assert.Equal(MotivoDoUso.PortaoNaoPermitido, Ler("301", inner: 2).Resultado.Motivo);
        Assert.Equal(MotivoDoUso.PortaoNaoPermitido, Ler("302", inner: 1).Resultado.Motivo);
        Assert.True(Ler("302", inner: 2).Resultado.Liberou);
    }

    [Fact]
    public void Horario_de_Brasilia_com_feriado()
    {
        var tabela = _parametros.GravarHorario("admin", new TabelaDeHorario(0, "Comercial",
        [
            .. Enumerable.Range(1, 5).Select(dia => new FaixaDeHorario(dia, 8 * 60, 18 * 60)),
            new FaixaDeHorario(7, 9 * 60, 12 * 60),
        ]), Segunda9h);
        Assert.True(tabela.Gravado, string.Join(" ", tabela.Problemas));
        var pessoa = Cadastrar(ajuste: d => d with { TabelaDeHorario = int.Parse(tabela.Id!, System.Globalization.CultureInfo.InvariantCulture) });
        DarCartao(pessoa, "400");

        Assert.True(Ler("400").Resultado.Liberou);                                                    // segunda 09:00
        Assert.Equal(MotivoDoUso.ForaDoHorario, Ler("400", Segunda9h.AddHours(9)).Resultado.Motivo);  // segunda 18:00 (fim exclusivo)
        Assert.Equal(MotivoDoUso.ForaDoHorario, Ler("400", Segunda9h.AddDays(-2)).Resultado.Motivo);  // sábado

        // Feriado na terça: vale a faixa do dia 7 (09:00–12:00), não a de terça.
        _parametros.GravarFeriado("admin", new Feriado(new DateOnly(2026, 11, 17), "Feriado local"), Segunda9h);
        Assert.True(Ler("400", Segunda9h.AddDays(1).AddHours(1)).Resultado.Liberou);                     // terça 10:00
        Assert.Equal(MotivoDoUso.ForaDoHorario, Ler("400", Segunda9h.AddDays(1).AddHours(4)).Resultado.Motivo); // terça 13:00
    }

    [Fact]
    public void Limite_do_dia_conta_quem_girou_e_volta_no_dia_seguinte()
    {
        var pessoa = Cadastrar(ajuste: d => d with { LimiteDiario = 1 });
        DarCartao(pessoa, "500");

        var (primeira, tentativa) = Ler("500");
        Assert.True(primeira.Liberou);
        Assert.Equal(MotivoDoUso.LimiteDiario, Ler("500", Segunda9h.AddSeconds(5)).Resultado.Motivo);

        // Liberou e não girou: depois da espera, a entrada do dia não foi gasta.
        Assert.True(Ler("500", Segunda9h.AddMinutes(3)).Resultado.Liberou);

        Assert.True(Ler("500", Segunda9h.AddMinutes(10)).Resultado.Liberou);
        Assert.Equal(3, Contar($"SELECT COUNT(*) FROM ticket_use_attempt WHERE person_id = '{pessoa}' AND outcome = 'consumido';"));

        // A primeira girou: a entrada do dia está gasta.
        _ingressos.ConfirmarPassagemFisica(tentativa, Segunda9h.AddSeconds(2));
        Assert.Equal(MotivoDoUso.LimiteDiario, Ler("500", Segunda9h.AddHours(5)).Resultado.Motivo);
        Assert.True(Ler("500", Segunda9h.AddDays(1)).Resultado.Liberou);
    }

    [Fact]
    public void Catraca_fechada_nega_todos_sem_consumir_e_reabre_com_motivo()
    {
        _ingressos.RegistrarProvedor(new ProvedorDeIngresso("site", "Site", CredentialNormalization.Raw.Name, "rest-site"), Segunda9h);
        _ingressos.Ingerir([new IngressoRecebido("site", "ref-1", "QR-1", "QR-1", UsosMaximos: 1)], Segunda9h);
        var pessoa = Cadastrar();
        DarCartao(pessoa, "600");

        Assert.False(_pessoas.FecharCatraca("admin", 1, "", Segunda9h).Gravado);
        Assert.True(_pessoas.FecharCatraca("admin", 1, "Manutenção do braço", Segunda9h).Gravado);

        Assert.Equal(MotivoDoUso.CatracaFechada, Ler("QR-1").Resultado.Motivo);
        Assert.Equal(MotivoDoUso.CatracaFechada, Ler("600").Resultado.Motivo);
        Assert.Equal(MotivoDoUso.CatracaFechada, Ler("NAO-EXISTE").Resultado.Motivo);
        Assert.True(Ler("600", inner: 2).Resultado.Liberou);
        Assert.Single(_pessoas.CatracasFechadas());

        // A negativa do ingresso vai para a nuvem; a da pessoa, não.
        Assert.Equal(2, Contar("SELECT COUNT(*) FROM outbox;"));

        Assert.True(_pessoas.AbrirCatraca("admin", 1, "Braço trocado", Segunda9h).Gravado);
        Assert.True(Ler("QR-1").Resultado.Liberou); // nada foi consumido enquanto fechada
        Assert.Empty(_pessoas.CatracasFechadas());
    }

    [Fact]
    public void Codigo_de_credencial_nao_repete_ingresso_nem_outra_credencial()
    {
        _ingressos.RegistrarProvedor(new ProvedorDeIngresso("site", "Site", CredentialNormalization.Raw.Name, "rest-site"), Segunda9h);
        _ingressos.Ingerir([new IngressoRecebido("site", "ref-1", "QR-1", "QR-1", UsosMaximos: 1)], Segunda9h);
        var a = Cadastrar("Pessoa Um");
        var b = Cadastrar("Pessoa Dois");
        DarCartao(a, "700");

        Assert.False(_pessoas.AdicionarCredencial("admin", b, "cartao", "700", null, null, Segunda9h).Gravado);
        Assert.False(_pessoas.AdicionarCredencial("admin", b, "qr", "QR-1", null, null, Segunda9h).Gravado);
        Assert.False(_pessoas.AdicionarCredencial("admin", b, "senha", "12", null, null, Segunda9h).Gravado);
        Assert.True(_pessoas.AdicionarCredencial("admin", b, "senha", "4321", null, null, Segunda9h).Gravado);
    }

    [Fact]
    public void Dados_pessoais_nao_ficam_em_claro_na_base_e_a_busca_acha_sem_acento()
    {
        var pessoa = Cadastrar("Joaquina Pérez Gonçalves", ajuste: d => d with
        {
            TipoDoDocumento = "cpf",
            Documento = "529.982.247-25",
            Telefone = "11 98888-7777",
            Email = "joaquina@exemplo.com",
        });

        var ficha = _pessoas.Obter(pessoa)!;
        Assert.Equal("Joaquina Pérez Gonçalves", ficha.Dados.NomeCompleto);
        Assert.Equal("529.982.247-25", ficha.Dados.Documento);
        Assert.Equal("joaquina@exemplo.com", ficha.Dados.Email);

        Assert.Equal(pessoa, Assert.Single(_pessoas.Buscar("goncalves")).Id);
        Assert.Equal(pessoa, Assert.Single(_pessoas.Buscar("joaq")).Id);
        Assert.Equal(pessoa, Assert.Single(_pessoas.Buscar("goncalves joaq")).Id);
        Assert.Equal(pessoa, Assert.Single(_pessoas.Buscar("52998224725")).Id);
        Assert.Empty(_pessoas.Buscar("Silva"));

        // Nenhum dado pessoal em claro nos arquivos da base (nem no WAL).
        var bytes = Directory.GetFiles(Path.GetDirectoryName(_banco.Caminho)!).SelectMany(File.ReadAllBytes).ToArray();
        foreach (var claro in new[] { "Joaquina", "Gonçalves", "52998224725", "529.982.247-25", "98888", "exemplo.com" })
        {
            Assert.DoesNotContain(claro, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Documento_repetido_cpf_invalido_e_campos_do_perfil_sao_recusados()
    {
        Cadastrar("Primeira Pessoa", ajuste: d => d with { TipoDoDocumento = "cpf", Documento = "52998224725" });

        var repetido = _pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Outra Pessoa", TipoDoDocumento = "cpf", Documento = "529.982.247-25" }, Segunda9h);
        Assert.Contains("Já existe uma pessoa com este documento.", repetido.Problemas);

        var invalido = _pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Outra Pessoa", TipoDoDocumento = "cpf", Documento = "111.111.111-11" }, Segunda9h);
        Assert.Contains("CPF inválido (confira os dígitos).", invalido.Problemas);

        var visitante = _pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "visitante", NomeCompleto = "Sem Dados" }, Segunda9h);
        Assert.Contains("Documento: obrigatório para este perfil.", visitante.Problemas);
        Assert.Contains("Quem recebe (anfitrião): obrigatório para este perfil.", visitante.Problemas);

        var menor = _pessoas.Gravar("admin", new DadosDaPessoa { PerfilId = "aluno", NomeCompleto = "Criança Teste", Nascimento = new DateOnly(2015, 3, 1) }, Segunda9h);
        Assert.Contains("Menor de 18 anos: informe o responsável legal.", menor.Problemas);
    }

    [Fact]
    public void Cifrado_de_uma_pessoa_nao_decifra_em_outra_nem_com_outra_chave()
    {
        var cifrado = _cifra.Cifrar("Nome Secreto", "pessoa-a")!;

        Assert.Equal("Nome Secreto", _cifra.Decifrar(cifrado, "pessoa-a"));
        Assert.ThrowsAny<CryptographicException>(() => _cifra.Decifrar(cifrado, "pessoa-b"));
        var outra = new CifraDeDadosPessoais(RandomNumberGenerator.GetBytes(CifraDeDadosPessoais.TamanhoDaChave));
        Assert.ThrowsAny<CryptographicException>(() => outra.Decifrar(cifrado, "pessoa-a"));
        Assert.NotEqual(cifrado, _cifra.Cifrar("Nome Secreto", "pessoa-a"));
    }

    [Fact]
    public void Tabela_de_horario_recusa_sobreposicao_avisa_do_limite_do_inner_e_nao_se_apaga_em_uso()
    {
        var sobreposta = _parametros.GravarHorario("admin", new TabelaDeHorario(0, "Ruim", [new(1, 480, 720), new(1, 700, 800)]), Segunda9h);
        Assert.Contains("Segunda: faixas sobrepostas.", sobreposta.Problemas);

        var tres = _parametros.GravarHorario("admin", new TabelaDeHorario(0, "Três turnos", [new(1, 0, 480), new(1, 480, 960), new(1, 960, 1440)]), Segunda9h);
        Assert.True(tres.Gravado);
        Assert.Single(tres.Avisos);

        var id = int.Parse(tres.Id!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1, id);
        Cadastrar(ajuste: d => d with { TabelaDeHorario = id });
        Assert.False(_parametros.ExcluirHorario("admin", id, Segunda9h).Gravado);
    }

    [Fact]
    public void Validacao_de_cpf_e_cnpj()
    {
        Assert.True(CadastroDePessoas.CpfValido("529.982.247-25"));
        Assert.False(CadastroDePessoas.CpfValido("529.982.247-24"));
        Assert.False(CadastroDePessoas.CpfValido("000.000.000-00"));
        Assert.True(ParametrosDoCadastro.CnpjValido("11.222.333/0001-81"));
        Assert.False(ParametrosDoCadastro.CnpjValido("11.222.333/0001-80"));
    }

    [Fact]
    public void Duas_catracas_ao_mesmo_tempo_com_limite_de_uma_entrada_so_uma_libera()
    {
        var pessoa = Cadastrar(ajuste: d => d with { LimiteDiario = 1 });
        DarCartao(pessoa, "800");

        var resultados = new ResultadoDoUso[2];
        Parallel.For(0, 2, i => resultados[i] = Ler("800", inner: i + 1).Resultado);

        Assert.Single(resultados, r => r.Liberou);
        Assert.Single(resultados, r => r.Motivo == MotivoDoUso.LimiteDiario);
    }

    [Fact]
    public void Decisao_com_cinco_mil_pessoas_fica_abaixo_de_150_ms()
    {
        var tabela = _parametros.GravarHorario("admin", new TabelaDeHorario(0, "Todo dia", [.. Enumerable.Range(0, 8).Select(d => new FaixaDeHorario(d, 0, 1440))]), Segunda9h);
        using (var conexao = _banco.Fabrica.Abrir())
        using (var transacao = conexao.BeginTransaction())
        {
            using var comando = conexao.CreateCommand();
            comando.Transaction = transacao;
            comando.CommandText =
                """
                INSERT INTO person (id, profile_id, full_name_enc, schedule_id, daily_limit, created_at, updated_at)
                VALUES ($id, 'colaborador', x'00', $tabela, 10, $em, $em);
                INSERT INTO person_gate (person_id, inner_number) VALUES ($id, 1);
                INSERT INTO person_credential (id, person_id, kind, value_normalized, created_at, updated_at)
                VALUES ($id, $id, 'cartao', $codigo, $em, $em);
                """;
            var id = comando.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
            var codigo = comando.Parameters.Add("$codigo", Microsoft.Data.Sqlite.SqliteType.Text);
            comando.Parameters.AddWithValue("$tabela", int.Parse(tabela.Id!, System.Globalization.CultureInfo.InvariantCulture));
            comando.Parameters.AddWithValue("$em", Segunda9h.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            for (var i = 0; i < 5000; i++)
            {
                id.Value = $"p{i:D5}";
                codigo.Value = $"{1_000_000 + i}";
                comando.ExecuteNonQuery();
            }

            transacao.Commit();
        }

        Ler("1000000"); // aquece a conexão e o plano das consultas
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var (resultado, _) = Ler("1004999", Segunda9h.AddMinutes(1));
        relogio.Stop();

        Assert.True(resultado.Liberou, resultado.Motivo.ToString());
        Assert.True(relogio.ElapsedMilliseconds < 150, $"A decisão levou {relogio.ElapsedMilliseconds} ms.");
    }
}
