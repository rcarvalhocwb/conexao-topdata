using System.Globalization;
using System.Text.RegularExpressions;
using Access.Domain.Credentials;
using Access.Domain.Tempo;

namespace Access.Importacao;

/// <summary>Situação de um cartão já cadastrado (<c>ticket.status</c>).</summary>
public enum SituacaoDoCartao
{
    Valido,
    Consumido,
    Bloqueado,
    Cancelado,
}

/// <summary>Um tipo de entrada já cadastrado (<c>ticket_type</c>).</summary>
public sealed record TipoConhecido(string Codigo, string Nome, int Ordem, bool Ativo);

/// <summary>Um cartão já cadastrado, como a prévia precisa dele para comparar.</summary>
/// <param name="CodigoNormalizado">O código como está em <c>ticket.qr_normalized</c>.</param>
/// <param name="ProvedorId">De quem é.</param>
/// <param name="Tipo">A categoria atual.</param>
/// <param name="Situacao">A situação atual.</param>
/// <param name="ValidoDe">Início da validade.</param>
/// <param name="ValidoAte">Fim da validade.</param>
/// <param name="UsosMaximos">Usos permitidos; nulo = sem limite.</param>
public sealed record CartaoExistente(
    string CodigoNormalizado,
    string ProvedorId,
    string? Tipo,
    SituacaoDoCartao Situacao,
    DateTimeOffset? ValidoDe = null,
    DateTimeOffset? ValidoAte = null,
    int? UsosMaximos = 1)
{
    /// <summary>Mascarado: a lista pode ir parar num log.</summary>
    public override string ToString() => $"CartaoExistente {CredentialValue.Mascarar(CodigoNormalizado)} de {ProvedorId}";
}

/// <summary>O retrato da base contra o qual a prévia compara o arquivo. Só leitura.</summary>
/// <param name="ProvedorId">O provedor em nome de quem se importa (a bilheteria, em geral).</param>
/// <param name="Perfil">O perfil de normalização <b>desse provedor</b> (docs/34 §5.3: o comprimento vem dele).</param>
/// <param name="Tipos">Os tipos cadastrados.</param>
/// <param name="Existentes">Os cartões cadastrados, de todos os provedores (o código é único no evento).</param>
public sealed record ContextoDaPrevia(
    string ProvedorId,
    CredentialNormalization Perfil,
    IReadOnlyCollection<TipoConhecido> Tipos,
    IReadOnlyCollection<CartaoExistente> Existentes);

/// <summary>Como a linha entraria, se a importação fosse confirmada.</summary>
public enum ClasseDaLinha
{
    Novo,
    Alterado,
    Igual,
    Erro,
}

/// <summary>O que a linha diz do cartão, já validado e convertido.</summary>
/// <param name="Tipo">Código do tipo, em maiúsculas.</param>
/// <param name="Bloqueado">A linha diz BLOQUEADO.</param>
/// <param name="ValidoDe">Início da validade, em UTC (lido em horário de Brasília).</param>
/// <param name="ValidoAte">Fim da validade, em UTC.</param>
/// <param name="UsosMaximos">Nulo = sem limite.</param>
public sealed record DadosDoCartao(string Tipo, bool Bloqueado, DateTimeOffset? ValidoDe, DateTimeOffset? ValidoAte, int? UsosMaximos);

/// <summary>Uma linha de cartão na prévia.</summary>
public sealed class LinhaDaPrevia
{
    internal LinhaDaPrevia(int linha) => Linha = linha;

    /// <summary>Linha física no arquivo.</summary>
    public int Linha { get; }

    /// <summary>Novo, alterado, igual ou erro.</summary>
    public ClasseDaLinha Classe { get; internal set; }

    /// <summary>
    /// O código normalizado, para a confirmação (B.4) gravar. Nunca vai para tela nem log:
    /// para isso existe <see cref="CodigoMascarado"/>.
    /// </summary>
    public string? CodigoNormalizado { get; internal set; }

    /// <summary>O código mascarado (<c>cred:****01(10)</c>), para tela e relatório.</summary>
    public string CodigoMascarado => CodigoNormalizado is { } c ? CredentialValue.Mascarar(c) : "(sem código)";

    /// <summary>Os dados da linha, quando ela é válida.</summary>
    public DadosDoCartao? Dados { get; internal set; }

    /// <summary>Motivos de erro, sem o código.</summary>
    public IReadOnlyList<string> Erros => ErrosDaLinha;

    /// <summary>Avisos, sem o código. Não impedem a linha.</summary>
    public IReadOnlyList<string> Avisos => AvisosDaLinha;

    /// <summary>Colunas que mudam em relação ao cadastro (só em <see cref="ClasseDaLinha.Alterado"/>).</summary>
    public IReadOnlyList<string> CamposAlterados => AlteradosDaLinha;

    internal readonly List<string> ErrosDaLinha = [];

    internal readonly List<string> AvisosDaLinha = [];

    internal readonly List<string> AlteradosDaLinha = [];

    /// <summary>Mascarado.</summary>
    public override string ToString() => $"Linha {Linha}: {Classe} {CodigoMascarado}";
}

/// <summary>Uma linha da aba Tipos na prévia.</summary>
public sealed record LinhaDeTipo(
    int Linha,
    ClasseDaLinha Classe,
    TipoConhecido? Tipo,
    IReadOnlyList<string> Erros);

/// <summary>
/// A prévia da importação de cartões (Etapa B.3 do docs/35; docs/26 §3): novos, alterados,
/// iguais e erros por linha, sem gravar nada.
/// </summary>
/// <remarks>
/// <para>
/// Pura: recebe o arquivo e o retrato da base (<see cref="ContextoDaPrevia"/>) e devolve o
/// que <i>aconteceria</i>. Quem grava, e quando pode gravar (só sem conexão com a nuvem,
/// ADR-0025), é a Etapa B.4.
/// </para>
/// <para>
/// O código passa pela normalização única (<see cref="PerfisDeLeitura.Normalizar"/>) com o
/// perfil do provedor, e o comprimento é o que o perfil aceita, dentro do teto de 4 a 16 da
/// catraca 4 (docs/26 §3; docs/34 §5.3). Nenhuma mensagem repete o código.
/// </para>
/// </remarks>
public sealed partial class PreviaDaImportacao
{
    /// <summary>Teto do comprimento do código (docs/26 §3): o limite documentado da catraca 4.</summary>
    public const int ComprimentoMinimo = 4;

    /// <summary>Teto do comprimento do código.</summary>
    public const int ComprimentoMaximo = 16;

    private const string Titular =
        "O titular não é aceito: a proteção do nome (cifra por evento) e os prazos de retenção ainda não existem " +
        "(docs/35, Etapa B.9). Deixe a coluna titular vazia.";

    private static readonly string[] Obrigatorias = ["codigo", "tipo", "situacao"];

    private static readonly string[] Opcionais = ["titular", "validade_inicio", "validade_fim", "usos_maximos", "observacao"];

    private static readonly string[] FormatosDeData = ["dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm", "dd/MM/yyyy H:mm", "d/M/yyyy H:mm"];

    private PreviaDaImportacao(ArquivoLido arquivo)
    {
        Formato = arquivo.Formato;
        Bytes = arquivo.Bytes;
        Sha256 = arquivo.Sha256;
        _problemas.AddRange(arquivo.Problemas);
        _avisos.AddRange(arquivo.Avisos);
    }

    /// <summary><c>csv</c> ou <c>xlsx</c>.</summary>
    public string Formato { get; }

    /// <summary>Tamanho do arquivo.</summary>
    public long Bytes { get; }

    /// <summary>SHA-256 do arquivo (para <c>import_batch.file_sha256</c>).</summary>
    public string Sha256 { get; }

    /// <summary>Motivos para recusar o arquivo inteiro.</summary>
    public IReadOnlyList<string> ProblemasDoArquivo => _problemas;

    /// <summary>Avisos do arquivo.</summary>
    public IReadOnlyList<string> AvisosDoArquivo => _avisos;

    /// <summary>As linhas de cartão, na ordem do arquivo.</summary>
    public IReadOnlyList<LinhaDaPrevia> Linhas => _linhas;

    /// <summary>As linhas da aba Tipos, quando há.</summary>
    public IReadOnlyList<LinhaDeTipo> Tipos { get; private set; } = [];

    /// <summary>Linhas vazias ignoradas.</summary>
    public int LinhasVazias { get; private set; }

    /// <summary>Verdadeiro quando o arquivo inteiro foi recusado.</summary>
    public bool Recusada => _problemas.Count > 0;

    public int Novos => _linhas.Count(l => l.Classe == ClasseDaLinha.Novo);

    public int Alterados => _linhas.Count(l => l.Classe == ClasseDaLinha.Alterado);

    public int Iguais => _linhas.Count(l => l.Classe == ClasseDaLinha.Igual);

    public int ComErro => _linhas.Count(l => l.Classe == ClasseDaLinha.Erro);

    public int ComAviso => _linhas.Count(l => l.Avisos.Count > 0);

    private readonly List<string> _problemas = [];

    private readonly List<string> _avisos = [];

    private readonly List<LinhaDaPrevia> _linhas = [];

    /// <summary>Prévia de um CSV de cartões (e, opcionalmente, de um CSV de tipos).</summary>
    public static PreviaDaImportacao DeCsv(Stream cartoes, ContextoDaPrevia contexto, Stream? tipos = null)
    {
        ArgumentNullException.ThrowIfNull(cartoes);
        ArgumentNullException.ThrowIfNull(contexto);

        var arquivo = LeitorDeCsv.Ler(cartoes);
        Planilha? planilhaDeTipos = null;
        if (tipos is not null && !arquivo.Recusado)
        {
            var arquivoDeTipos = LeitorDeCsv.Ler(tipos);
            if (arquivoDeTipos.Recusado)
            {
                return new PreviaDaImportacao(arquivo with { Problemas = [.. arquivoDeTipos.Problemas.Select(p => "Arquivo de tipos: " + p)] });
            }

            planilhaDeTipos = arquivoDeTipos.Cartoes;
        }

        return Prever(arquivo, contexto, planilhaDeTipos);
    }

    /// <summary>Prévia de um .xlsx do modelo (abas Cartões e, se houver, Tipos).</summary>
    public static PreviaDaImportacao DeXlsx(Stream arquivo, ContextoDaPrevia contexto)
    {
        ArgumentNullException.ThrowIfNull(arquivo);
        ArgumentNullException.ThrowIfNull(contexto);
        return Prever(LeitorDeXlsx.Ler(arquivo), contexto, null);
    }

    /// <summary>Prévia de um arquivo já lido.</summary>
    public static PreviaDaImportacao Prever(ArquivoLido arquivo, ContextoDaPrevia contexto, Planilha? tiposEmSeparado = null)
    {
        ArgumentNullException.ThrowIfNull(arquivo);
        ArgumentNullException.ThrowIfNull(contexto);

        var previa = new PreviaDaImportacao(arquivo);
        if (arquivo.Recusado || arquivo.Cartoes is not { } cartoes)
        {
            if (!previa.Recusada)
            {
                previa._problemas.Add("O arquivo não tem cartões.");
            }

            return previa;
        }

        var tipos = contexto.Tipos
            .GroupBy(t => t.Codigo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if ((arquivo.Tipos ?? tiposEmSeparado) is { } planilhaDeTipos)
        {
            previa.Tipos = PreverTipos(planilhaDeTipos, tipos, previa._problemas);
            foreach (var linha in previa.Tipos.Where(t => t.Classe != ClasseDaLinha.Erro && t.Tipo is not null))
            {
                tipos[linha.Tipo!.Codigo] = linha.Tipo;
            }
        }

        var colunas = MapearColunas(cartoes.Cabecalho, Obrigatorias, Opcionais, previa._problemas, previa._avisos, "cartões");
        if (previa.Recusada)
        {
            return previa;
        }

        previa.LinhasVazias = cartoes.LinhasVazias;
        if (cartoes.Linhas.Count > LimitesDaImportacao.LinhasParaAviso)
        {
            previa._avisos.Add(string.Create(CultureInfo.InvariantCulture,
                $"O arquivo tem {cartoes.Linhas.Count} linhas. A confirmação vai demorar; prefira fazê-la fora da operação."));
        }

        var existentes = new Dictionary<string, CartaoExistente>(StringComparer.Ordinal);
        foreach (var cartao in contexto.Existentes)
        {
            existentes.TryAdd(cartao.CodigoNormalizado, cartao);
        }

        foreach (var linha in cartoes.Linhas)
        {
            previa._linhas.Add(AnalisarLinha(linha, colunas, contexto, tipos, existentes, cartoes.Sistema1904));
        }

        MarcarRepetidos(previa._linhas);
        AvisarZerosAMaisOuAMenos(previa._linhas, contexto.Existentes);

        foreach (var linha in previa._linhas)
        {
            if (linha.ErrosDaLinha.Count > 0)
            {
                linha.Classe = ClasseDaLinha.Erro;
                linha.Dados = null;
            }
        }

        return previa;
    }

    // -------------------------------------------------------------------------- colunas

    private static Dictionary<string, int> MapearColunas(
        IReadOnlyList<string> cabecalho,
        string[] obrigatorias,
        string[] opcionais,
        List<string> problemas,
        List<string> avisos,
        string aba)
    {
        var colunas = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cabecalho.Count; i++)
        {
            var nome = Apoio.Chave(cabecalho[i]);
            if (nome.Length == 0)
            {
                continue;
            }

            if (!obrigatorias.Contains(nome) && !opcionais.Contains(nome))
            {
                avisos.Add($"A coluna \"{Curto(cabecalho[i])}\" ({aba}) não é do modelo e foi ignorada.");
                continue;
            }

            if (!colunas.TryAdd(nome, i))
            {
                problemas.Add($"A coluna \"{nome}\" ({aba}) aparece mais de uma vez.");
            }
        }

        foreach (var nome in obrigatorias.Where(n => !colunas.ContainsKey(n)))
        {
            problemas.Add($"Falta a coluna obrigatória \"{nome}\" ({aba}). Use o cabeçalho do modelo (docs/26).");
        }

        return colunas;
    }

    // ------------------------------------------------------------------------ uma linha

    private static LinhaDaPrevia AnalisarLinha(
        LinhaDaPlanilha linha,
        Dictionary<string, int> colunas,
        ContextoDaPrevia contexto,
        Dictionary<string, TipoConhecido> tipos,
        Dictionary<string, CartaoExistente> existentes,
        bool sistema1904)
    {
        var resultado = new LinhaDaPrevia(linha.Numero);
        var erros = resultado.ErrosDaLinha;
        var avisos = resultado.AvisosDaLinha;

        Celula Coluna(string nome) => colunas.TryGetValue(nome, out var i) ? linha[i] : Celula.Vazia;

        var codigo = AnalisarCodigo(Coluna("codigo"), contexto.Perfil, erros, avisos);
        resultado.CodigoNormalizado = codigo;

        if (!Coluna("titular").EstaVazia)
        {
            erros.Add(Titular);
        }

        var existente = codigo is not null && existentes.TryGetValue(codigo, out var e) ? e : null;
        var tipo = AnalisarTipo(Coluna("tipo"), tipos, existente, erros);
        var bloqueado = AnalisarSituacao(Coluna("situacao"), erros);
        var de = AnalisarData(Coluna("validade_inicio"), "validade_inicio", sistema1904, erros);
        var ate = AnalisarData(Coluna("validade_fim"), "validade_fim", sistema1904, erros);
        var usos = AnalisarUsos(Coluna("usos_maximos"), erros);
        AnalisarObservacao(Coluna("observacao"), erros);

        if (de is { } inicio && ate is { } fim && fim <= inicio)
        {
            erros.Add("validade_fim precisa ser depois de validade_inicio.");
        }

        if (erros.Count > 0 || codigo is null || tipo is null || bloqueado is null)
        {
            resultado.Classe = ClasseDaLinha.Erro;
            return resultado;
        }

        var dados = new DadosDoCartao(tipo, bloqueado.Value, de, ate, usos);
        resultado.Dados = dados;

        if (existente is null)
        {
            resultado.Classe = ClasseDaLinha.Novo;
            return resultado;
        }

        if (!string.Equals(existente.ProvedorId, contexto.ProvedorId, StringComparison.Ordinal))
        {
            erros.Add("O código já pertence a outro provedor. A importação não muda cartão de outro provedor.");
            return resultado;
        }

        if (existente.Situacao == SituacaoDoCartao.Cancelado)
        {
            erros.Add("O cartão está cancelado. A importação não reativa cartão cancelado.");
            return resultado;
        }

        var alterados = resultado.AlteradosDaLinha;
        if (!string.Equals(existente.Tipo, dados.Tipo, StringComparison.Ordinal))
        {
            alterados.Add("tipo");
        }

        if ((existente.Situacao == SituacaoDoCartao.Bloqueado) != dados.Bloqueado)
        {
            alterados.Add("situacao");
            if (!dados.Bloqueado)
            {
                avisos.Add("O cartão está bloqueado e a linha diz ATIVO: confirmar a importação o desbloqueia.");
            }
        }

        if (existente.ValidoDe?.UtcTicks != dados.ValidoDe?.UtcTicks)
        {
            alterados.Add("validade_inicio");
        }

        if (existente.ValidoAte?.UtcTicks != dados.ValidoAte?.UtcTicks)
        {
            alterados.Add("validade_fim");
        }

        if (existente.UsosMaximos != dados.UsosMaximos)
        {
            alterados.Add("usos_maximos");
        }

        resultado.Classe = alterados.Count == 0 ? ClasseDaLinha.Igual : ClasseDaLinha.Alterado;
        return resultado;
    }

    /// <summary>
    /// A regra do código. Devolve o código normalizado, ou nulo se não há código legível.
    /// </summary>
    private static string? AnalisarCodigo(Celula celula, CredentialNormalization perfil, List<string> erros, List<string> avisos)
    {
        switch (celula.Tipo)
        {
            case TipoDaCelula.Numero:
                erros.Add(
                    "A célula do código está como Número no Excel, que pode ter tirado os zeros à esquerda. Formate a " +
                    "coluna como Texto e cole de novo a partir do arquivo original: não redigite, o zero que sumiu não volta.");
                return null;
            case TipoDaCelula.Formula:
                erros.Add("O código vem de uma fórmula. Cole só o valor, como texto.");
                return null;
            case TipoDaCelula.Booleano or TipoDaCelula.Erro:
                erros.Add("A célula do código não é texto.");
                return null;
        }

        var texto = celula.Texto.Trim();
        if (texto.Length == 0)
        {
            erros.Add("Falta o código.");
            return null;
        }

        if (NotacaoCientifica().IsMatch(texto))
        {
            erros.Add("O código está em notação científica: o Excel converteu o número e perdeu dígitos. Formate a coluna como Texto.");
            return null;
        }

        if (NumeroComDecimal().IsMatch(texto))
        {
            erros.Add("O código parece um número convertido em texto (termina em ,0). Formate a coluna como Texto.");
            return null;
        }

        if (ForaDoAlfabeto().IsMatch(texto))
        {
            erros.Add("O código tem espaço, ponto, traço, vírgula ou outro caractere que a catraca não lê.");
            return null;
        }

        // A normalização única do sistema (Etapa 0.7): a mesma da leitura na catraca.
        var normalizado = PerfisDeLeitura.Normalizar(texto, perfil)!;

        if (normalizado.Length is < ComprimentoMinimo or > ComprimentoMaximo)
        {
            erros.Add(string.Create(CultureInfo.InvariantCulture,
                $"O código tem {normalizado.Length} caracteres; a catraca lê de {ComprimentoMinimo} a {ComprimentoMaximo}."));
            return normalizado;
        }

        if (!perfil.IsLengthAccepted(normalizado))
        {
            var aceitos = string.Join(", ", perfil.AllowedLengths!.Order());
            erros.Add(string.Create(CultureInfo.InvariantCulture,
                $"O código tem {normalizado.Length} caracteres; o perfil \"{perfil.Name}\" do provedor aceita {aceitos}."));
            return normalizado;
        }

        if (normalizado.Length > texto.Length)
        {
            avisos.Add(string.Create(CultureInfo.InvariantCulture,
                $"O código foi completado com zeros à esquerda até {normalizado.Length} caracteres pelo perfil \"{perfil.Name}\"."));
        }

        return normalizado;
    }

    private static string? AnalisarTipo(
        Celula celula,
        Dictionary<string, TipoConhecido> tipos,
        CartaoExistente? existente,
        List<string> erros)
    {
        var texto = celula.Tipo == TipoDaCelula.Texto ? celula.Texto.Trim().ToUpperInvariant() : string.Empty;
        if (texto.Length == 0)
        {
            erros.Add(celula.EstaVazia ? "Falta o tipo." : "A célula do tipo não é texto.");
            return null;
        }

        if (!tipos.TryGetValue(texto, out var tipo))
        {
            erros.Add(CodigoDeTipo().IsMatch(texto)
                ? $"O tipo {texto} não está cadastrado. Crie o tipo antes, na aba Tipos ou na tela de tipos."
                : "O tipo não está cadastrado. Use o código do tipo, em maiúsculas (ex.: INTEIRA).");
            return null;
        }

        // Tipo inativo não aceita cartão novo (docs/26 §1); o cartão que já é dele pode ficar.
        if (!tipo.Ativo && !string.Equals(existente?.Tipo, tipo.Codigo, StringComparison.Ordinal))
        {
            erros.Add($"O tipo {tipo.Codigo} está inativo e não aceita cartão novo.");
            return null;
        }

        return tipo.Codigo;
    }

    private static bool? AnalisarSituacao(Celula celula, List<string> erros)
    {
        var texto = celula.Tipo == TipoDaCelula.Texto ? celula.Texto.Trim() : string.Empty;
        if (string.Equals(texto, "ATIVO", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(texto, "BLOQUEADO", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        erros.Add(celula.EstaVazia ? "Falta a situação (ATIVO ou BLOQUEADO)." : "A situação precisa ser ATIVO ou BLOQUEADO.");
        return null;
    }

    private static DateTimeOffset? AnalisarData(Celula celula, string coluna, bool sistema1904, List<string> erros)
    {
        if (celula.EstaVazia)
        {
            return null;
        }

        DateTime? noEvento = null;
        if (celula.Tipo == TipoDaCelula.Texto
            && DateTime.TryParseExact(celula.Texto.Trim(), FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida))
        {
            noEvento = lida;
        }
        else if (celula.Tipo == TipoDaCelula.Numero
                 && double.TryParse(celula.Texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                 && serial is > 1 and < 2958466)
        {
            // Data do Excel é um número de dias; no sistema de 1904 começa 1462 dias depois.
            var data = DateTime.FromOADate(serial + (sistema1904 ? 1462 : 0));
            noEvento = new DateTime(data.AddSeconds(30).Ticks / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Unspecified);
        }

        if (noEvento is null)
        {
            erros.Add($"{coluna} fora do formato dd/mm/aaaa hh:mm (horário de Brasília).");
            return null;
        }

        return HoraDeBrasilia.DoEvento(noEvento.Value).ToUniversalTime();
    }

    private static int? AnalisarUsos(Celula celula, List<string> erros)
    {
        if (celula.EstaVazia)
        {
            return null;
        }

        var texto = celula.Texto.Trim();
        var valido = celula.Tipo switch
        {
            TipoDaCelula.Texto => SoDigitos().IsMatch(texto) && int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 ? n : (int?)null,
            TipoDaCelula.Numero => double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                                   && d >= 1 && d < int.MaxValue && Math.Floor(d) == d ? (int)d : null,
            _ => null,
        };

        if (valido is null)
        {
            erros.Add("usos_maximos precisa ser um número inteiro maior ou igual a 1, ou vazio (sem limite).");
        }

        return valido;
    }

    private static void AnalisarObservacao(Celula celula, List<string> erros)
    {
        if (celula.EstaVazia)
        {
            return;
        }

        var texto = celula.Texto.Trim();
        if (texto.Length > 200)
        {
            erros.Add("A observação passa de 200 caracteres.");
        }

        if (Cpf().IsMatch(texto) || Email().IsMatch(texto) || Telefone().IsMatch(texto))
        {
            erros.Add("A observação não pode ter CPF, e-mail ou telefone (docs/26 §2).");
        }
    }

    // --------------------------------------------------------------- regras do arquivo

    /// <summary>Código repetido no arquivo é erro nas duas linhas: o sistema não adivinha qual vale (docs/26 §3).</summary>
    private static void MarcarRepetidos(List<LinhaDaPrevia> linhas)
    {
        foreach (var grupo in linhas.Where(l => l.CodigoNormalizado is not null)
                     .GroupBy(l => l.CodigoNormalizado!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            foreach (var linha in grupo)
            {
                var outras = string.Join(", ", grupo.Where(o => o != linha).Select(o => o.Linha.ToString(CultureInfo.InvariantCulture)));
                linha.ErrosDaLinha.Add($"O código se repete no arquivo (linha {outras}). Deixe uma linha só.");
            }
        }
    }

    /// <summary>
    /// "Mesmo código com zeros a mais ou a menos" é aviso, nunca junção (docs/26 §4;
    /// docs/34 §5.1): 0000000101 e 00000000000101 podem ser o mesmo cartão impresso de dois
    /// jeitos, ou dois cartões. Quem decide é o operador.
    /// </summary>
    private static void AvisarZerosAMaisOuAMenos(List<LinhaDaPrevia> linhas, IReadOnlyCollection<CartaoExistente> existentes)
    {
        var doArquivo = new Dictionary<string, List<LinhaDaPrevia>>(StringComparer.Ordinal);
        foreach (var linha in linhas.Where(l => l.CodigoNormalizado is not null))
        {
            var chave = SemZerosAEsquerda(linha.CodigoNormalizado!);
            if (!doArquivo.TryGetValue(chave, out var lista))
            {
                doArquivo[chave] = lista = [];
            }

            lista.Add(linha);
        }

        var daBase = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var cartao in existentes)
        {
            var chave = SemZerosAEsquerda(cartao.CodigoNormalizado);
            if (doArquivo.ContainsKey(chave))
            {
                if (!daBase.TryGetValue(chave, out var lista))
                {
                    daBase[chave] = lista = [];
                }

                lista.Add(cartao.CodigoNormalizado);
            }
        }

        foreach (var (chave, grupo) in doArquivo)
        {
            foreach (var linha in grupo)
            {
                var outrasLinhas = grupo
                    .Where(o => !string.Equals(o.CodigoNormalizado, linha.CodigoNormalizado, StringComparison.Ordinal))
                    .Select(o => o.Linha.ToString(CultureInfo.InvariantCulture))
                    .ToList();
                if (outrasLinhas.Count > 0)
                {
                    linha.AvisosDaLinha.Add(
                        $"A linha {string.Join(", ", outrasLinhas)} tem um código igual a este com zeros à esquerda a mais ou a menos. " +
                        "Nada foi juntado: confira qual está certo.");
                }

                if (daBase.TryGetValue(chave, out var cadastrados)
                    && cadastrados.Exists(c => !string.Equals(c, linha.CodigoNormalizado, StringComparison.Ordinal)))
                {
                    linha.AvisosDaLinha.Add(
                        "Já existe um cartão cadastrado igual a este com zeros à esquerda a mais ou a menos. " +
                        "Nada foi juntado: confira qual está certo.");
                }
            }
        }
    }

    private static string SemZerosAEsquerda(string codigo)
    {
        var sem = codigo.TrimStart('0');
        return sem.Length == 0 ? "0" : sem;
    }

    // --------------------------------------------------------------------------- tipos

    private static List<LinhaDeTipo> PreverTipos(Planilha planilha, Dictionary<string, TipoConhecido> cadastrados, List<string> problemas)
    {
        var colunas = MapearColunas(planilha.Cabecalho, ["tipo", "nome_exibido", "ordem", "ativo"], [], problemas, [], "tipos");
        if (problemas.Count > 0)
        {
            return [];
        }

        var resultado = new List<(int Linha, TipoConhecido? Tipo, List<string> Erros)>();
        foreach (var linha in planilha.Linhas)
        {
            var erros = new List<string>();
            var codigo = linha[colunas["tipo"]].Texto.Trim();
            var nome = linha[colunas["nome_exibido"]].Texto.Trim();
            var ordemCelula = linha[colunas["ordem"]];
            var ativo = linha[colunas["ativo"]].Texto.Trim().ToUpperInvariant();

            if (!CodigoDeTipo().IsMatch(codigo))
            {
                erros.Add("O código do tipo tem de 2 a 20 letras maiúsculas, dígitos ou _, sem espaço e sem acento.");
            }

            if (nome.Length is 0 or > 40)
            {
                erros.Add("nome_exibido tem de 1 a 40 caracteres.");
            }

            var ordem = int.TryParse(ordemCelula.Texto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var o) ? o : -1;
            if (ordem < 0)
            {
                erros.Add("ordem precisa ser um número inteiro maior ou igual a 0.");
            }

            if (ativo is not ("SIM" or "NAO" or "NÃO"))
            {
                erros.Add("ativo precisa ser SIM ou NAO.");
            }

            resultado.Add((linha.Numero, erros.Count == 0 ? new TipoConhecido(codigo, nome, ordem, ativo == "SIM") : null, erros));
        }

        foreach (var grupo in resultado.Where(r => r.Tipo is not null).GroupBy(r => r.Tipo!.Codigo, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var item in grupo)
            {
                item.Erros.Add("O tipo se repete na aba Tipos. Deixe uma linha só.");
            }
        }

        return
        [
            .. resultado.Select(r =>
            {
                if (r.Erros.Count > 0 || r.Tipo is null)
                {
                    return new LinhaDeTipo(r.Linha, ClasseDaLinha.Erro, null, r.Erros);
                }

                var classe = !cadastrados.TryGetValue(r.Tipo.Codigo, out var atual) ? ClasseDaLinha.Novo
                    : atual == r.Tipo ? ClasseDaLinha.Igual
                    : ClasseDaLinha.Alterado;
                return new LinhaDeTipo(r.Linha, classe, r.Tipo, r.Erros);
            }),
        ];
    }

    private static string Curto(string texto) => texto.Length <= 30 ? texto : texto[..30] + "…";

    // ------------------------------------------------------------------------ padrões

    /// <summary><c>1,23E+13</c>, <c>1.23457E+11</c>, <c>1E+15</c>: o Excel converteu.</summary>
    [GeneratedRegex(@"^[0-9]+([.,][0-9]+)?[eE][+-]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NotacaoCientifica();

    [GeneratedRegex(@"^[0-9]+[.,]0+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumeroComDecimal();

    [GeneratedRegex("[^0-9A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex ForaDoAlfabeto();

    [GeneratedRegex("^[A-Z0-9_]{2,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodigoDeTipo();

    [GeneratedRegex("^[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SoDigitos();

    [GeneratedRegex(@"\d{3}\.?\d{3}\.?\d{3}-?\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex Cpf();

    [GeneratedRegex(@"[^@\s]+@[^@\s]+\.[^@\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"\(?\d{2}\)?\s?9?\d{4}-?\d{4}", RegexOptions.CultureInvariant)]
    private static partial Regex Telefone();
}
