using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Access.Domain.Credentials;
using Access.Domain.Ticketing;

namespace Access.Infrastructure.SQLite;

/// <summary>Um arquivo da carga inicial: o nome (sem o caminho) e o conteúdo.</summary>
public sealed record ArquivoDeCarga(string Nome, byte[] Conteudo);

/// <summary>O que a carga inicial fez. Nenhum número de cartão completo sai daqui.</summary>
/// <param name="Arquivos">Arquivos lidos.</param>
/// <param name="Cartoes">Cartões distintos encontrados nos arquivos.</param>
/// <param name="Inseridos">Cartões que passaram a existir na base.</param>
/// <param name="Atualizados">Cartões que já existiam e foram atualizados (tipo, situação, validade).</param>
/// <param name="Cancelados">Cartões que entram desativados (inativos na origem).</param>
/// <param name="PorTipo">Quantos cartões de cada tipo.</param>
/// <param name="TiposCriados">Tipos de entrada que não existiam e foram criados.</param>
/// <param name="JaCarregada">O mesmo conjunto de arquivos já tinha sido carregado: nada foi feito.</param>
/// <param name="Problemas">O que não entrou, e por quê, com o número mascarado.</param>
public sealed record ResultadoDaCargaInicial(
    int Arquivos,
    int Cartoes,
    int Inseridos,
    int Atualizados,
    int Cancelados,
    IReadOnlyDictionary<string, int> PorTipo,
    int TiposCriados,
    bool JaCarregada,
    IReadOnlyList<string> Problemas)
{
    /// <summary>Uma linha para o registro do serviço.</summary>
    public string Resumo() => JaCarregada
        ? $"carga inicial: os mesmos {Arquivos} arquivo(s) já tinham sido carregados; nada mudou."
        : $"carga inicial: {Arquivos} arquivo(s), {Cartoes} cartão(ões): {Inseridos} novo(s), {Atualizados} atualizado(s), " +
          $"{Cancelados} desativado(s); por tipo: {string.Join(", ", PorTipo.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}"))}" +
          (TiposCriados > 0 ? $"; {TiposCriados} tipo(s) criado(s)" : string.Empty) +
          (Problemas.Count > 0 ? $"; {Problemas.Count} problema(s)" : string.Empty) + ".";
}

/// <summary>
/// A carga inicial dos cartões já validados: os arquivos exportados do sistema anterior (cartões,
/// cartões autorizados e autorizações da catraca) entram na base local na primeira partida do
/// serviço, sem nuvem e sem digitar nada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que uma pasta, e não o instalador.</b> O repositório e o instalador de teste são
/// públicos; os números dos cartões abrem a catraca e nunca entram neles. Quem instala copia os
/// arquivos para <c>%ProgramData%\ConexaoTopdata\carga-inicial</c> (o
/// <c>carregar-cartoes.ps1</c> faz isso) e o serviço carrega ao subir. Depois de carregados, os
/// arquivos vão para <c>carga-inicial\importados</c>, fora do caminho de uma nova carga.
/// </para>
/// <para>
/// <b>A mesma regra da nuvem.</b> Cada cartão vira um ingresso do provedor da bilheteria local,
/// exatamente como a sincronização com o painel na nuvem grava um cartão autorizado
/// (<c>FonteDeCartoesDoPainel</c>): sem limite de usos (o controle é a urna e o intervalo de
/// reuso), referência igual ao número, e inativo na origem entra cancelado. Repetir a carga, ou
/// a nuvem mandar o mesmo cartão depois, é atualização, nunca duplicata.
/// </para>
/// <para>
/// <b>Os três arquivos, juntos.</b> Os formatos de exportação conhecidos têm o número em
/// <c>numero_cartao</c>; o tipo em <c>tipo_ingresso</c> (preferido) ou <c>tipo_cartao</c>; e a
/// situação em <c>ativo</c>, <c>autorizado</c> ou <c>status</c>. Os arquivos são juntados pelo
/// número antes de gravar: inativo em qualquer um deles vale (o lado seguro). O nome do titular
/// <b>não</b> é lido: a base de ingressos não guarda dado pessoal.
/// </para>
/// <para>
/// O número é gravado como veio, com os zeros à esquerda (ADR-0008): o que a catraca entrega
/// precisa casar com ele sem conversão. Se a bancada mostrar outro formato, o perfil do provedor
/// muda e a carga se repete.
/// </para>
/// </remarks>
public static class CargaInicialDeCartoes
{
    /// <summary>A pasta, dentro da pasta de dados, onde os arquivos esperam a carga.</summary>
    public const string NomeDaPasta = "carga-inicial";

    /// <summary>O provedor dos cartões da bilheteria (o mesmo da sincronização com a nuvem).</summary>
    public const string ProvedorLocal = "bilheteria-local";

    /// <summary>Usos de um cartão sem limite: o mesmo valor da sincronização com o painel.</summary>
    public const int SemLimiteDeUsos = int.MaxValue;

    /// <summary>Quem a trilha mostra como autor da carga.</summary>
    public const string Autor = "Carga inicial";

    private const int TamanhoDoLote = 500;
    private const int MaiorNumero = 32;

    private static readonly string[] ColunasDoNumero = ["numero_cartao", "card_number", "numero", "codigo"];

    /// <summary>
    /// Carrega os <c>.csv</c> que estiverem na pasta da carga e os move para <c>importados</c>.
    /// </summary>
    /// <returns>O resultado, ou nulo quando não há arquivo esperando.</returns>
    public static ResultadoDaCargaInicial? CarregarDaPasta(
        string pastaDeDados,
        SqliteConnectionFactory fabrica,
        DateTimeOffset agora,
        Action<string>? registrar = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDeDados);
        ArgumentNullException.ThrowIfNull(fabrica);

        var pasta = Path.Combine(pastaDeDados, NomeDaPasta);
        Directory.CreateDirectory(pasta);

        var caminhos = Directory.GetFiles(pasta, "*.csv", SearchOption.TopDirectoryOnly)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (caminhos.Count == 0)
        {
            return null;
        }

        var arquivos = caminhos.Select(c => new ArquivoDeCarga(Path.GetFileName(c), File.ReadAllBytes(c))).ToList();
        var resultado = Carregar(arquivos, fabrica, agora, $"servico@{Environment.MachineName}");

        registrar?.Invoke(resultado.Resumo());
        foreach (var problema in resultado.Problemas.Take(20))
        {
            registrar?.Invoke("carga inicial: " + problema);
        }

        var importados = Path.Combine(pasta, "importados");
        Directory.CreateDirectory(importados);
        var carimbo = agora.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        foreach (var caminho in caminhos)
        {
            File.Move(caminho, Path.Combine(importados, $"{carimbo}-{Path.GetFileName(caminho)}"), overwrite: true);
        }

        return resultado;
    }

    /// <summary>Junta os arquivos pelo número e grava os cartões na base local.</summary>
    /// <param name="arquivos">Os arquivos, em qualquer ordem.</param>
    /// <param name="fabrica">A base local.</param>
    /// <param name="agora">O instante da carga.</param>
    /// <param name="estacao">Conta e máquina que fizeram a carga, para a trilha.</param>
    public static ResultadoDaCargaInicial Carregar(
        IReadOnlyList<ArquivoDeCarga> arquivos,
        SqliteConnectionFactory fabrica,
        DateTimeOffset agora,
        string estacao)
    {
        ArgumentNullException.ThrowIfNull(arquivos);
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentException.ThrowIfNullOrWhiteSpace(estacao);

        var problemas = new List<string>();
        var cartoes = new Dictionary<string, CartaoDaCarga>(StringComparer.Ordinal);
        foreach (var arquivo in arquivos)
        {
            Ler(arquivo, cartoes, problemas);
        }

        var impressao = Impressao(arquivos);
        var repositorio = new RepositorioDeIngressos(fabrica);
        var porTipo = cartoes.Values
            .GroupBy(c => c.Tipo ?? "(sem tipo)", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var cancelados = cartoes.Values.Count(c => c.Inativo);

        if (JaCarregada(fabrica, impressao))
        {
            return new ResultadoDaCargaInicial(arquivos.Count, cartoes.Count, 0, 0, cancelados, porTipo, 0, true, problemas);
        }

        var provedor = GarantirProvedor(repositorio, agora);
        var perfil = PerfisDeLeitura.Todos.TryGetValue(provedor.PerfilDeNormalizacao, out var p) ? p : CredentialNormalization.Raw;

        var tiposCriados = repositorio.GarantirTiposDeEntrada(
            cartoes.Values.Select(c => c.Tipo).OfType<string>(), Autor, agora);

        var lote = new List<IngressoRecebido>(cartoes.Count);
        foreach (var (bruto, cartao) in cartoes)
        {
            var normalizado = PerfisDeLeitura.Normalizar(bruto, perfil);
            if (normalizado is null || !perfil.IsLengthAccepted(normalizado))
            {
                problemas.Add($"cartão {CredentialValue.Mascarar(bruto)}: não serve ao perfil de leitura \"{perfil.Name}\" do provedor.");
                continue;
            }

            lote.Add(new IngressoRecebido(
                ProvedorId: provedor.Id,
                ReferenciaExterna: normalizado,
                QrBruto: bruto,
                QrNormalizado: normalizado,
                ValidoDe: cartao.De,
                ValidoAte: cartao.Ate,
                UsosMaximos: SemLimiteDeUsos,
                Cancelado: cartao.Inativo,
                Categoria: cartao.Tipo));
        }

        var inseridos = 0;
        var atualizados = 0;
        foreach (var parte in lote.Chunk(TamanhoDoLote))
        {
            var ingestao = repositorio.Ingerir(parte, agora);
            inseridos += ingestao.Inseridos;
            atualizados += ingestao.Atualizados;
            problemas.AddRange(ingestao.Colisoes.Select(c =>
                $"cartão {CredentialValue.Mascarar(c.QrNormalizado)}: o número já pertence a outro provedor ({c.ProvedorExistente}); ficou como estava."));
        }

        RegistrarLote(fabrica, arquivos, impressao, cartoes.Count, inseridos, atualizados, problemas.Count, estacao, agora);

        return new ResultadoDaCargaInicial(
            arquivos.Count, cartoes.Count, inseridos, atualizados, cancelados, porTipo, tiposCriados, false, problemas);
    }

    private static void Ler(ArquivoDeCarga arquivo, Dictionary<string, CartaoDaCarga> cartoes, List<string> problemas)
    {
        var texto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(arquivo.Conteudo).TrimStart('﻿');
        var linhas = texto.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var primeira = linhas.FindIndex(l => l.Trim().Length > 0);
        if (primeira < 0)
        {
            problemas.Add($"{arquivo.Nome}: vazio.");
            return;
        }

        var separador = linhas[primeira].Contains(';', StringComparison.Ordinal) ? ';' : ',';
        var cabecalho = Campos(linhas[primeira], separador).Select(c => c.ToLowerInvariant()).ToList();
        int Coluna(params string[] nomes) => nomes.Select(n => cabecalho.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);

        var numero = Coluna(ColunasDoNumero);
        if (numero < 0)
        {
            problemas.Add($"{arquivo.Nome}: sem a coluna do número do cartão (numero_cartao). O arquivo não foi lido.");
            return;
        }

        // O tipo da venda vale mais que o do cartão físico: no arquivo de cartões, "AUTO" é o
        // tipo do plástico; nos autorizados, é o ingresso que ele carrega.
        var tipoDaVenda = Coluna("tipo_ingresso", "tipo", "admission_type");
        var tipoDoCartao = Coluna("tipo_cartao");
        var ativo = Coluna("ativo", "autorizado", "active");
        var situacao = Coluna("status");
        var de = Coluna("valido_de", "valid_from");
        var ate = Coluna("valido_ate", "valid_until");

        for (var i = primeira + 1; i < linhas.Count; i++)
        {
            if (linhas[i].Trim().Length == 0)
            {
                continue;
            }

            var campos = Campos(linhas[i], separador);
            string Valor(int coluna) => coluna >= 0 && coluna < campos.Count ? campos[coluna] : string.Empty;

            var bruto = Valor(numero);
            if (bruto.Length is 0 or > MaiorNumero || bruto.Any(char.IsWhiteSpace))
            {
                problemas.Add($"{arquivo.Nome}, linha {i + 1}: número do cartão vazio ou inválido.");
                continue;
            }

            var deValido = Data(Valor(de), out var validoDe);
            var ateValido = Data(Valor(ate), out var validoAte);
            if (!deValido || !ateValido)
            {
                problemas.Add($"{arquivo.Nome}, linha {i + 1}: validade que não é data com fuso; o cartão entra sem validade.");
            }

            var inativo = Inativo(Valor(ativo)) || Inativo(Valor(situacao));
            var (tipo, peso) = Tipo(Valor(tipoDaVenda), 2) ?? Tipo(Valor(tipoDoCartao), 1) ?? ((string?)null, 0);

            if (cartoes.TryGetValue(bruto, out var existente))
            {
                cartoes[bruto] = existente with
                {
                    Tipo = peso > existente.Peso ? tipo : existente.Tipo,
                    Peso = Math.Max(peso, existente.Peso),
                    Inativo = existente.Inativo || inativo,
                    De = existente.De ?? validoDe,
                    Ate = existente.Ate ?? validoAte,
                };
            }
            else
            {
                cartoes[bruto] = new CartaoDaCarga(tipo, peso, inativo, validoDe, validoAte);
            }
        }
    }

    private static (string? Tipo, int Peso)? Tipo(string valor, int peso)
    {
        var codigo = valor.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
        return RepositorioDeIngressos.CodigoDeTipoValido(codigo) ? (codigo, peso) : null;
    }

    private static bool Inativo(string valor) =>
        valor.Trim().ToUpperInvariant() is "NAO" or "NÃO" or "N" or "FALSE" or "0" or "INACTIVE" or "INATIVO"
            or "BLOCKED" or "BLOQUEADO" or "CANCELADO" or "CANCELLED" or "CANCELED";

    private static bool Data(string valor, out DateTimeOffset? data)
    {
        data = null;
        if (valor.Trim().Length == 0)
        {
            return true;
        }

        if (DateTimeOffset.TryParse(valor.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var lida)
            && (valor.Contains('+', StringComparison.Ordinal) || valor.EndsWith('Z') || valor.LastIndexOf('-') > 9))
        {
            data = lida;
            return true;
        }

        return false;
    }

    private static List<string> Campos(string linha, char separador) =>
        [.. linha.Split(separador).Select(c => c.Trim().Trim('"').Trim())];

    /// <summary>
    /// Garante que o provedor da bilheteria local exista, com os padrões da nuvem. O serviço chama
    /// a cada partida: sem ele, o cadastro de cartões por leitura e a carga não têm onde gravar.
    /// </summary>
    public static ProvedorDeIngresso GarantirProvedorLocal(SqliteConnectionFactory fabrica, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        return GarantirProvedor(new RepositorioDeIngressos(fabrica), agora);
    }

    private static ProvedorDeIngresso GarantirProvedor(RepositorioDeIngressos repositorio, DateTimeOffset agora)
    {
        // Já existe (a nuvem registrou, ou outra carga): fica como está, com o perfil e as
        // regras de quem o registrou.
        if (repositorio.Provedores().FirstOrDefault(p => p.Id == ProvedorLocal) is { } existente)
        {
            return existente;
        }

        // Os mesmos padrões da sincronização com a nuvem (ConfiguracaoDaNuvem): cartão
        // reutilizável, 4 minutos entre usos, só na urna, número como a catraca entrega.
        var novo = new ProvedorDeIngresso(
            ProvedorLocal,
            "Bilheteria local",
            CredentialNormalization.Raw.Name,
            Conector: string.Empty,
            Habilitado: true,
            Reutilizavel: true,
            IntervaloDeReuso: TimeSpan.FromSeconds(240),
            SomenteNaUrna: true);
        repositorio.RegistrarProvedor(novo, agora);
        return novo;
    }

    private static string Impressao(IReadOnlyList<ArquivoDeCarga> arquivos)
    {
        var hashes = arquivos
            .Select(a => Convert.ToHexStringLower(SHA256.HashData(a.Conteudo)))
            .Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(string.Join("\n", hashes))));
    }

    private static bool JaCarregada(SqliteConnectionFactory fabrica, string impressao)
    {
        using var conexao = fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            "SELECT COUNT(*) FROM import_batch WHERE file_sha256 = $sha AND kind = 'cartoes' AND status = 'aplicada';";
        comando.Parameters.AddWithValue("$sha", impressao);
        return Convert.ToInt64(comando.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static void RegistrarLote(
        SqliteConnectionFactory fabrica,
        IReadOnlyList<ArquivoDeCarga> arquivos,
        string impressao,
        int total,
        int novos,
        int alterados,
        int problemas,
        string estacao,
        DateTimeOffset agora)
    {
        var nome = arquivos.Count == 1 ? arquivos[0].Nome : $"carga-inicial-{arquivos.Count}-arquivos.csv";
        nome = nome.Replace('/', '_').Replace('\\', '_');
        if (nome.Length > 255)
        {
            nome = nome[..255];
        }

        var quando = agora.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        using var conexao = fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO import_batch
                (id, kind, provider_id, file_name, file_format, file_sha256, file_bytes, mode,
                 rows_total, rows_new, rows_changed, rows_same, rows_error, rows_warning, reason,
                 requested_by, workstation, previewed_at, applied_at, status)
            VALUES
                ($id, 'cartoes', $provedor, $nome, 'csv', $sha, $bytes, 'incluir_e_atualizar',
                 $total, $novos, $alterados, 0, 0, $avisos, 'Carga inicial dos cartões já validados',
                 $autor, $estacao, $em, $em, 'aplicada');
            """;
        comando.Parameters.AddWithValue("$id", Guid.CreateVersion7(agora).ToString());
        comando.Parameters.AddWithValue("$provedor", ProvedorLocal);
        comando.Parameters.AddWithValue("$nome", nome);
        comando.Parameters.AddWithValue("$sha", impressao);
        comando.Parameters.AddWithValue("$bytes", arquivos.Sum(a => (long)a.Conteudo.Length));
        comando.Parameters.AddWithValue("$total", total);
        comando.Parameters.AddWithValue("$novos", novos);
        comando.Parameters.AddWithValue("$alterados", alterados);
        comando.Parameters.AddWithValue("$avisos", problemas);
        comando.Parameters.AddWithValue("$autor", Autor);
        comando.Parameters.AddWithValue("$estacao", estacao.Length > 200 ? estacao[..200] : estacao);
        comando.Parameters.AddWithValue("$em", quando);
        comando.ExecuteNonQuery();
    }

    private sealed record CartaoDaCarga(string? Tipo, int Peso, bool Inativo, DateTimeOffset? De, DateTimeOffset? Ate);
}
