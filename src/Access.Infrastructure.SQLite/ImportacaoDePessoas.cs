using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using static Access.Infrastructure.SQLite.CadastroDePessoas;

namespace Access.Infrastructure.SQLite;

/// <summary>Uma linha da planilha, pronta para o cadastro.</summary>
/// <param name="Linha">A linha no arquivo, para apontar o problema.</param>
/// <param name="Dados">A ficha; <see cref="DadosDaPessoa.PerfilId"/> pode vir como código ou como nome do perfil.</param>
/// <param name="Empresa">O nome da empresa; criada se não existir.</param>
/// <param name="Sala">O nome da sala (dentro da empresa da linha); criada se não existir.</param>
/// <param name="Credenciais">(tipo, código) de cada credencial da linha.</param>
public sealed record PessoaParaImportar(
    int Linha,
    DadosDaPessoa Dados,
    string? Empresa,
    string? Sala,
    IReadOnlyList<(string Tipo, string Codigo)> Credenciais);

/// <summary>O que a importação fez (ou faria, na prévia).</summary>
/// <param name="LoteId">O lote gravado; nulo na prévia e quando recusada.</param>
/// <param name="Pessoas">Pessoas que entram.</param>
/// <param name="Credenciais">Credenciais que entram.</param>
/// <param name="EmpresasNovas">Empresas que serão criadas.</param>
/// <param name="SalasNovas">Salas que serão criadas.</param>
/// <param name="Problemas">(linha, problema). Com algum, nada é gravado.</param>
public sealed record DesfechoDaImportacao(
    string? LoteId,
    int Pessoas,
    int Credenciais,
    int EmpresasNovas,
    int SalasNovas,
    IReadOnlyList<(int Linha, string Problema)> Problemas)
{
    public bool Aplicavel => Problemas.Count == 0 && Pessoas > 0;
}

/// <summary>Um lote importado.</summary>
public sealed record LoteDePessoas(string Id, DateTimeOffset Em, string? Por, int Pessoas, int Credenciais, DateTimeOffset? DesfeitoEm);

/// <summary>
/// Importação de pessoas por planilha (docs/43 P5): tudo ou nada, com a mesma regra da ficha, e desfazer.
/// </summary>
/// <remarks>
/// A prévia roda a importação inteira numa transação e a desfaz no fim: o que ela aponta é exatamente o
/// que aplicar recusaria (perfil, campos obrigatórios, CPF, documento e código repetidos, código de
/// ingresso). Aplicar só grava sem nenhum problema.
/// </remarks>
public sealed class ImportacaoDePessoas
{
    /// <summary>Pessoas por arquivo, no máximo.</summary>
    public const int PessoasPorArquivo = 20_000;

    private readonly SqliteConnectionFactory _fabrica;
    private readonly CadastroDePessoas _cadastro;

    public ImportacaoDePessoas(SqliteConnectionFactory fabrica, CadastroDePessoas cadastro)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(cadastro);
        _fabrica = fabrica;
        _cadastro = cadastro;
    }

    /// <summary>Confere tudo sem gravar nada.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="linhas">As linhas lidas.</param>
    /// <param name="agora">O instante.</param>
    public DesfechoDaImportacao Prever(string? quem, IReadOnlyList<PessoaParaImportar> linhas, DateTimeOffset agora) =>
        Rodar(quem, linhas, arquivoSha256: string.Empty, aplicar: false, agora);

    /// <summary>Grava tudo, ou nada se alguma linha tiver problema.</summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="linhas">As linhas lidas.</param>
    /// <param name="arquivoSha256">A impressão do arquivo, para o lote.</param>
    /// <param name="agora">O instante.</param>
    public DesfechoDaImportacao Aplicar(string? quem, IReadOnlyList<PessoaParaImportar> linhas, string arquivoSha256, DateTimeOffset agora) =>
        Rodar(quem, linhas, arquivoSha256, aplicar: true, agora);

    /// <summary>Os lotes, do mais novo para o mais antigo.</summary>
    public IReadOnlyList<LoteDePessoas> Lotes()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT id, at, by, people, credentials, undone_at FROM person_import ORDER BY at DESC LIMIT 100;";
        var lotes = new List<LoteDePessoas>();
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            lotes.Add(new LoteDePessoas(l.GetString(0), Instante(l, 1)!.Value, Texto(l, 2), l.GetInt32(3), l.GetInt32(4), Instante(l, 5)));
        }

        return lotes;
    }

    /// <summary>
    /// Desfaz um lote: apaga as pessoas que ele criou (com as credenciais) e as empresas e salas que ele
    /// criou e ninguém mais usa. As passagens dessas pessoas ficam, sem a pessoa.
    /// </summary>
    /// <param name="quem">O usuário do sistema.</param>
    /// <param name="loteId">O lote.</param>
    /// <param name="agora">O instante.</param>
    public ResultadoDoCadastro Desfazer(string? quem, string loteId, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loteId);
        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        string empresasCriadas;
        string salasCriadas;
        using (var comando = conexao.CreateCommand())
        {
            comando.Transaction = transacao;
            comando.CommandText = "SELECT created_companies, created_places, undone_at FROM person_import WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", loteId);
            using var l = comando.ExecuteReader();
            if (!l.Read())
            {
                return ResultadoDoCadastro.Recusado("Lote não encontrado.");
            }

            if (!l.IsDBNull(2))
            {
                return ResultadoDoCadastro.Recusado("Este lote já foi desfeito.");
            }

            empresasCriadas = l.GetString(0);
            salasCriadas = l.GetString(1);
        }

        if (Escalar(conexao, transacao,
                """
                SELECT 1 FROM person p
                WHERE (p.import_id IS NULL OR p.import_id <> $lote)
                  AND p.host_person_id IN (SELECT id FROM person WHERE import_id = $lote)
                LIMIT 1;
                """,
                ("$lote", loteId)) is not null)
        {
            return ResultadoDoCadastro.Recusado("Há visitantes de outros cadastros recebidos por pessoas deste lote. Mude o anfitrião deles antes de desfazer.");
        }

        Executar(conexao, transacao, "UPDATE person SET host_person_id = NULL WHERE import_id = $lote;", ("$lote", loteId));
        var apagadas = Executar(conexao, transacao, "DELETE FROM person WHERE import_id = $lote;", ("$lote", loteId));

        foreach (var sala in JsonSerializer.Deserialize<string[]>(salasCriadas) ?? [])
        {
            Executar(conexao, transacao, "DELETE FROM place WHERE id = $id AND NOT EXISTS (SELECT 1 FROM person WHERE place_id = $id);", ("$id", sala));
        }

        foreach (var empresa in JsonSerializer.Deserialize<string[]>(empresasCriadas) ?? [])
        {
            Executar(conexao, transacao,
                "DELETE FROM company WHERE id = $id AND NOT EXISTS (SELECT 1 FROM person WHERE company_id = $id) AND NOT EXISTS (SELECT 1 FROM place WHERE company_id = $id);",
                ("$id", empresa));
        }

        Executar(conexao, transacao, "UPDATE person_import SET undone_at = $em, undone_by = $quem WHERE id = $lote;",
            ("$em", Iso(agora)), ("$quem", Nulo(quem)), ("$lote", loteId));
        Registrar(conexao, transacao, agora, quem, null, "importacao.desfazer", $"lote={loteId}; pessoas={apagadas}");
        transacao.Commit();
        return ResultadoDoCadastro.Ok(apagadas.ToString(CultureInfo.InvariantCulture));
    }

    private DesfechoDaImportacao Rodar(string? quem, IReadOnlyList<PessoaParaImportar> linhas, string arquivoSha256, bool aplicar, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        if (linhas.Count == 0)
        {
            return new DesfechoDaImportacao(null, 0, 0, 0, 0, [(0, "Nenhuma pessoa para importar.")]);
        }

        if (linhas.Count > PessoasPorArquivo)
        {
            return new DesfechoDaImportacao(null, 0, 0, 0, 0,
                [(0, $"O arquivo tem {linhas.Count} pessoas; o máximo por arquivo é {PessoasPorArquivo}. Divida-o em partes.")]);
        }

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        var loteId = Guid.CreateVersion7(agora).ToString();
        Executar(conexao, transacao,
            "INSERT INTO person_import (id, at, by, file_sha256, people, credentials) VALUES ($id, $em, $quem, $sha, 0, 0);",
            ("$id", loteId), ("$em", Iso(agora)), ("$quem", Nulo(quem)), ("$sha", arquivoSha256));

        var perfis = Perfis(conexao, transacao);
        var empresas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var salas = new Dictionary<(string, string), string>();
        var empresasNovas = new List<string>();
        var salasNovas = new List<string>();
        var problemas = new List<(int, string)>();
        var pessoas = 0;
        var credenciais = 0;

        foreach (var linha in linhas)
        {
            var antes = problemas.Count;
            if (!perfis.TryGetValue(linha.Dados.PerfilId.Trim(), out var perfilId))
            {
                problemas.Add((linha.Linha, $"Perfil \"{linha.Dados.PerfilId}\" não existe ou está inativo."));
                continue;
            }

            string? empresaId = null;
            if (!string.IsNullOrWhiteSpace(linha.Empresa))
            {
                empresaId = Empresa(conexao, transacao, linha.Empresa.Trim(), empresas, empresasNovas, agora);
            }

            string? salaId = null;
            if (!string.IsNullOrWhiteSpace(linha.Sala))
            {
                salaId = Sala(conexao, transacao, linha.Sala.Trim(), empresaId, salas, salasNovas, agora);
            }

            var gravada = _cadastro.GravarNa(conexao, transacao, quem, linha.Dados with
            {
                Id = null,
                PerfilId = perfilId,
                EmpresaId = empresaId ?? linha.Dados.EmpresaId,
                SalaId = salaId ?? linha.Dados.SalaId,
            }, agora);
            if (!gravada.Gravado)
            {
                problemas.AddRange(gravada.Problemas.Select(p => (linha.Linha, p)));
                continue;
            }

            Executar(conexao, transacao, "UPDATE person SET import_id = $lote WHERE id = $id;", ("$lote", loteId), ("$id", gravada.Id!));
            foreach (var (tipo, codigo) in linha.Credenciais)
            {
                var credencial = AdicionarCredencialNa(conexao, transacao, quem, gravada.Id!, tipo, codigo, null, null, agora);
                if (!credencial.Gravado)
                {
                    problemas.AddRange(credencial.Problemas.Select(p => (linha.Linha, p)));
                }
                else
                {
                    credenciais++;
                }
            }

            if (problemas.Count == antes)
            {
                pessoas++;
            }
        }

        if (aplicar && problemas.Count == 0)
        {
            Executar(conexao, transacao,
                "UPDATE person_import SET people = $p, credentials = $c, created_companies = $e, created_places = $s WHERE id = $id;",
                ("$p", pessoas), ("$c", credenciais), ("$e", JsonSerializer.Serialize(empresasNovas)), ("$s", JsonSerializer.Serialize(salasNovas)), ("$id", loteId));
            Registrar(conexao, transacao, agora, quem, null, "importacao.aplicar", $"lote={loteId}; pessoas={pessoas}; credenciais={credenciais}");
            transacao.Commit();
            return new DesfechoDaImportacao(loteId, pessoas, credenciais, empresasNovas.Count, salasNovas.Count, []);
        }

        transacao.Rollback();
        return new DesfechoDaImportacao(null, pessoas, credenciais, empresasNovas.Count, salasNovas.Count, problemas);
    }

    // Código e nome de cada perfil ativo, para a planilha poder escrever "visitante" ou "Visitante".
    private static Dictionary<string, string> Perfis(SqliteConnection conexao, SqliteTransaction transacao)
    {
        using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        comando.CommandText = "SELECT id, name FROM person_profile WHERE status = 'ativo';";
        var perfis = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var l = comando.ExecuteReader();
        while (l.Read())
        {
            perfis[l.GetString(0)] = l.GetString(0);
            perfis[l.GetString(1)] = l.GetString(0);
        }

        return perfis;
    }

    private static string Empresa(SqliteConnection conexao, SqliteTransaction transacao, string nome, Dictionary<string, string> cache, List<string> novas, DateTimeOffset agora)
    {
        if (cache.TryGetValue(nome, out var id))
        {
            return id;
        }

        id = Escalar(conexao, transacao, "SELECT id FROM company WHERE name = $n COLLATE NOCASE ORDER BY status LIMIT 1;", ("$n", nome)) as string;
        if (id is null)
        {
            id = Guid.CreateVersion7(agora).ToString();
            Executar(conexao, transacao, "INSERT INTO company (id, name, created_at, updated_at) VALUES ($id, $n, $em, $em);",
                ("$id", id), ("$n", nome.Length > 120 ? nome[..120] : nome), ("$em", Iso(agora)));
            novas.Add(id);
        }

        cache[nome] = id;
        return id;
    }

    private static string Sala(
        SqliteConnection conexao, SqliteTransaction transacao, string nome, string? empresaId,
        Dictionary<(string, string), string> cache, List<string> novas, DateTimeOffset agora)
    {
        var chave = (nome.ToUpperInvariant(), empresaId ?? string.Empty);
        if (cache.TryGetValue(chave, out var id))
        {
            return id;
        }

        id = Escalar(conexao, transacao,
            "SELECT id FROM place WHERE name = $n COLLATE NOCASE AND COALESCE(company_id, '') = $e ORDER BY status LIMIT 1;",
            ("$n", nome), ("$e", empresaId ?? string.Empty)) as string;
        if (id is null)
        {
            id = Guid.CreateVersion7(agora).ToString();
            Executar(conexao, transacao, "INSERT INTO place (id, company_id, name, created_at, updated_at) VALUES ($id, $e, $n, $em, $em);",
                ("$id", id), ("$e", (object?)empresaId ?? DBNull.Value), ("$n", nome.Length > 80 ? nome[..80] : nome), ("$em", Iso(agora)));
            novas.Add(id);
        }

        cache[chave] = id;
        return id;
    }
}
