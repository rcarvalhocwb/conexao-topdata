using System.Globalization;
using System.Text.RegularExpressions;

namespace Access.Importacao;

/// <summary>Uma pessoa lida da planilha, ainda sem as regras do cadastro (perfil, CPF, repetidos).</summary>
/// <param name="Linha">A linha no Excel (o cabeçalho é a 1).</param>
public sealed record PessoaDaPlanilha(int Linha)
{
    public string Nome { get; init; } = string.Empty;

    public string? NomeSocial { get; init; }

    /// <summary>O perfil como escrito: o código ("visitante") ou o nome ("Visitante").</summary>
    public string Perfil { get; init; } = string.Empty;

    public string? TipoDoDocumento { get; init; }

    public string? Documento { get; init; }

    public DateOnly? Nascimento { get; init; }

    public string? Telefone { get; init; }

    public string? Email { get; init; }

    /// <summary>O nome da empresa; criada na importação se ainda não existir.</summary>
    public string? Empresa { get; init; }

    /// <summary>O nome da sala ou unidade (da empresa da linha); criada se ainda não existir.</summary>
    public string? Sala { get; init; }

    public string? Veiculo { get; init; }

    public string? Responsavel { get; init; }

    public string? Departamento { get; init; }

    public string? Cargo { get; init; }

    public string? Matricula { get; init; }

    public string? Observacao { get; init; }

    public DateOnly? ValidoDe { get; init; }

    public DateOnly? ValidoAte { get; init; }

    public IReadOnlyList<int> Catracas { get; init; } = [];

    /// <summary>As credenciais da linha: (cartao|qr|senha, código como a catraca lê).</summary>
    public IReadOnlyList<(string Tipo, string Codigo)> Credenciais { get; init; } = [];

    /// <summary>Sem caixa nem pontos: o código não aparece.</summary>
    public override string ToString() => $"Linha {Linha}";
}

/// <summary>O arquivo de pessoas lido: as linhas boas e os problemas de cada linha ruim.</summary>
/// <param name="Pessoas">As linhas lidas sem problema de formato.</param>
/// <param name="Problemas">(linha, problema); linha 0 é o arquivo inteiro.</param>
/// <param name="Avisos">Avisos que não impedem a importação.</param>
/// <param name="Sha256">A impressão do arquivo (reconhece o mesmo arquivo de novo).</param>
public sealed record LeituraDePessoas(
    IReadOnlyList<PessoaDaPlanilha> Pessoas,
    IReadOnlyList<(int Linha, string Problema)> Problemas,
    IReadOnlyList<string> Avisos,
    string Sha256);

/// <summary>
/// Lê a planilha de pessoas (docs/43 P5): .csv (separado por ponto e vírgula, UTF-8) ou .xlsx com a aba
/// "Pessoas". Só formato e colunas; as regras do cadastro (perfil, campos obrigatórios, CPF, documento e
/// código repetidos) quem confere é o serviço, na prévia, com a mesma regra da ficha.
/// </summary>
/// <remarks>
/// Coluna de dado de saúde (doença, alergia, medicamento, deficiência, CID, tipo sanguíneo) recusa o
/// arquivo inteiro: é dado sensível (LGPD, art. 11) que o controle de acesso não precisa.
/// </remarks>
public static partial class PlanilhaDePessoas
{
    /// <summary>As colunas reconhecidas e o nome no modelo.</summary>
    public static IReadOnlyList<(string Chave, string Cabecalho)> Colunas { get; } =
    [
        ("nome", "Nome"),
        ("nome_social", "Nome social"),
        ("perfil", "Perfil"),
        ("tipo_documento", "Tipo de documento"),
        ("documento", "Documento"),
        ("nascimento", "Nascimento"),
        ("telefone", "Telefone"),
        ("email", "E-mail"),
        ("empresa", "Empresa"),
        ("sala", "Sala"),
        ("veiculo", "Veículo"),
        ("responsavel", "Responsável"),
        ("departamento", "Departamento"),
        ("cargo", "Cargo"),
        ("matricula", "Matrícula"),
        ("observacao", "Observação"),
        ("valido_de", "Vale de"),
        ("valido_ate", "Vale até"),
        ("catracas", "Catracas"),
        ("cracha", "Crachá"),
        ("qr", "QR"),
        ("senha", "Senha"),
    ];

    // Outros nomes aceitos no cabeçalho, já sem acento e sem caixa.
    private static readonly Dictionary<string, string> Sinonimos = new(StringComparer.Ordinal)
    {
        ["nome completo"] = "nome",
        ["nome social"] = "nome_social",
        ["tipo de documento"] = "tipo_documento",
        ["tipo documento"] = "tipo_documento",
        ["cpf"] = "documento",
        ["data de nascimento"] = "nascimento",
        ["celular"] = "telefone",
        ["e-mail"] = "email",
        ["unidade"] = "sala",
        ["apartamento"] = "sala",
        ["placa"] = "veiculo",
        ["veiculo"] = "veiculo",
        ["responsavel legal"] = "responsavel",
        ["observacoes"] = "observacao",
        ["vale de"] = "valido_de",
        ["valido de"] = "valido_de",
        ["vale ate"] = "valido_ate",
        ["valido ate"] = "valido_ate",
        ["cartao"] = "cracha",
        ["cracha"] = "cracha",
        ["qr code"] = "qr",
        ["senha de teclado"] = "senha",
    };

    private static readonly string[] Saude = ["saude", "doenca", "alergi", "medicament", "deficien", "cid", "sangu", "gestant", "laudo"];

    private static readonly string[] FormatosDeData = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy"];

    /// <summary>Lê o arquivo.</summary>
    /// <param name="arquivo">O conteúdo.</param>
    /// <param name="nome">O nome do arquivo, para saber se é .csv ou .xlsx.</param>
    public static LeituraDePessoas Ler(Stream arquivo, string nome)
    {
        ArgumentNullException.ThrowIfNull(arquivo);
        var lido = (nome ?? string.Empty).EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? LeitorDeXlsx.Ler(arquivo, "Pessoas")
            : LeitorDeCsv.Ler(arquivo);

        if (lido.Recusado || lido.Cartoes is not { } planilha)
        {
            return new LeituraDePessoas([], [.. lido.Problemas.Select(p => (0, p))], lido.Avisos, lido.Sha256);
        }

        var problemas = new List<(int, string)>();
        var avisos = new List<string>(lido.Avisos);
        var indice = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < planilha.Cabecalho.Count; i++)
        {
            var chave = Apoio.Chave(planilha.Cabecalho[i]);
            if (chave.Length == 0)
            {
                continue;
            }

            if (Saude.Any(s => chave.Contains(s, StringComparison.Ordinal)))
            {
                return new LeituraDePessoas([], [(1, $"A coluna \"{planilha.Cabecalho[i]}\" parece dado de saúde. Esse dado não entra no cadastro de acesso (LGPD, art. 11): tire a coluna do arquivo.")], avisos, lido.Sha256);
            }

            var coluna = Sinonimos.GetValueOrDefault(chave)
                ?? Colunas.FirstOrDefault(c => c.Chave == chave.Replace(' ', '_') || Apoio.Chave(c.Cabecalho) == chave).Chave;
            if (coluna is null)
            {
                avisos.Add($"A coluna \"{planilha.Cabecalho[i]}\" não é do modelo e foi ignorada.");
                continue;
            }

            if (!indice.TryAdd(coluna, i))
            {
                return new LeituraDePessoas([], [(1, $"A coluna \"{planilha.Cabecalho[i]}\" aparece duas vezes.")], avisos, lido.Sha256);
            }
        }

        if (!indice.ContainsKey("nome") || !indice.ContainsKey("perfil"))
        {
            return new LeituraDePessoas([], [(1, "O arquivo precisa das colunas Nome e Perfil. Use o modelo.")], avisos, lido.Sha256);
        }

        var pessoas = new List<PessoaDaPlanilha>();
        foreach (var linha in planilha.Linhas)
        {
            var erros = new List<string>();
            Celula C(string coluna) => indice.TryGetValue(coluna, out var i) ? linha[i] : Celula.Vazia;
            string? T(string coluna) => C(coluna) is { EstaVazia: false } c ? c.Texto.Trim() : null;

            var credenciais = new List<(string, string)>();
            foreach (var (coluna, tipo) in new[] { ("cracha", "cartao"), ("qr", "qr"), ("senha", "senha") })
            {
                if (Codigo(C(coluna), coluna, erros) is { } codigo)
                {
                    credenciais.Add((tipo, codigo));
                }
            }

            var pessoa = new PessoaDaPlanilha(linha.Numero)
            {
                Nome = T("nome") ?? string.Empty,
                NomeSocial = T("nome_social"),
                Perfil = T("perfil") ?? string.Empty,
                TipoDoDocumento = T("tipo_documento")?.ToLowerInvariant() ?? (C("documento").EstaVazia ? null : "cpf"),
                Documento = Codigo(C("documento"), "documento", erros),
                Nascimento = Data(C("nascimento"), "Nascimento", planilha.Sistema1904, erros),
                Telefone = T("telefone"),
                Email = T("email"),
                Empresa = T("empresa"),
                Sala = T("sala"),
                Veiculo = T("veiculo"),
                Responsavel = T("responsavel"),
                Departamento = T("departamento"),
                Cargo = T("cargo"),
                Matricula = T("matricula"),
                Observacao = T("observacao"),
                ValidoDe = Data(C("valido_de"), "Vale de", planilha.Sistema1904, erros),
                ValidoAte = Data(C("valido_ate"), "Vale até", planilha.Sistema1904, erros),
                Catracas = Catracas(T("catracas"), erros),
                Credenciais = credenciais,
            };

            if (pessoa.Nome.Length == 0)
            {
                erros.Add("Falta o nome.");
            }

            if (pessoa.Perfil.Length == 0)
            {
                erros.Add("Falta o perfil.");
            }

            if (erros.Count == 0)
            {
                pessoas.Add(pessoa);
            }
            else
            {
                problemas.AddRange(erros.Select(e => (linha.Numero, e)));
            }
        }

        // O mesmo código duas vezes no arquivo: a segunda nunca entraria.
        foreach (var repetido in pessoas.SelectMany(p => p.Credenciais.Select(c => (p.Linha, c.Codigo)))
                     .GroupBy(x => x.Codigo, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            problemas.AddRange(repetido.Skip(1).Select(x => (x.Linha, $"O mesmo código de credencial já está na linha {repetido.First().Linha}.")));
        }

        if (planilha.Linhas.Count == 0)
        {
            problemas.Add((0, "O arquivo não tem nenhuma pessoa depois do cabeçalho."));
        }

        return new LeituraDePessoas(pessoas, problemas, avisos, lido.Sha256);
    }

    // Documento e códigos: só texto. Número do Excel pode ter perdido zeros à esquerda.
    private static string? Codigo(Celula celula, string coluna, List<string> erros)
    {
        if (celula.EstaVazia)
        {
            return null;
        }

        switch (celula.Tipo)
        {
            case TipoDaCelula.Numero:
                erros.Add($"A coluna {coluna} está como Número no Excel, que pode ter tirado zeros à esquerda. Formate a coluna como Texto e cole de novo do original.");
                return null;
            case TipoDaCelula.Formula or TipoDaCelula.Booleano or TipoDaCelula.Erro:
                erros.Add($"A coluna {coluna} não é texto.");
                return null;
        }

        var texto = celula.Texto.Trim();
        if (NotacaoCientifica().IsMatch(texto))
        {
            erros.Add($"A coluna {coluna} está em notação científica: o Excel perdeu dígitos. Formate como Texto.");
            return null;
        }

        return texto;
    }

    private static DateOnly? Data(Celula celula, string coluna, bool sistema1904, List<string> erros)
    {
        if (celula.EstaVazia)
        {
            return null;
        }

        if (celula.Tipo == TipoDaCelula.Texto
            && DateTime.TryParseExact(celula.Texto.Trim(), FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida))
        {
            return DateOnly.FromDateTime(lida);
        }

        if (celula.Tipo == TipoDaCelula.Numero
            && double.TryParse(celula.Texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial is > 1 and < 2958466)
        {
            // Data do Excel é um número de dias; no sistema de 1904 começa 1462 dias depois.
            return DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial) + (sistema1904 ? 1462 : 0)));
        }

        erros.Add($"{coluna} fora do formato dd/mm/aaaa.");
        return null;
    }

    private static List<int> Catracas(string? texto, List<string> erros)
    {
        var catracas = new List<int>();
        foreach (var parte in (texto ?? string.Empty).Split([',', ';', ' ', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(parte, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > 99)
            {
                erros.Add("Catracas: números de 1 a 99 separados por vírgula.");
                return [];
            }

            if (!catracas.Contains(numero))
            {
                catracas.Add(numero);
            }
        }

        return catracas;
    }

    [GeneratedRegex(@"^\d+([.,]\d+)?[eE][+-]?\d+$")]
    private static partial Regex NotacaoCientifica();
}
