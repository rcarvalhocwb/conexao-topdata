using System.Globalization;

namespace Access.Infrastructure.SQLite;

/// <summary>
/// O que o operador ajusta para o evento e o worker lê ao subir.
/// </summary>
/// <param name="TipoDeLeitor">
/// Tipo de leitor passado à catraca. 8 na bancada; 5 é a alternativa se o QR não for lido.
/// <c>A_CONFIRMAR_COM_TOPDATA</c>, ver docs/20.
/// </param>
/// <param name="LeitorDaUrna">Se o leitor 2 (fenda da urna) fica ligado.</param>
/// <param name="TempoDeAcionamento">Segundos que a catraca fica liberada esperando o giro.</param>
/// <param name="MensagemPadrao">Texto do display em repouso.</param>
/// <param name="ConectorDoEspelho">
/// Conector da nuvem que recebe as tentativas. Vazio desliga o espelho.
/// </param>
/// <param name="EsperaPeloGiroSegundos">Quanto a liberação espera o giro antes de subir (ADR-0023).</param>
public sealed record ConfiguracaoDaOperacao(
    byte TipoDeLeitor = 8,
    bool LeitorDaUrna = true,
    byte TempoDeAcionamento = 5,
    string MensagemPadrao = "Aproxime o ingresso",
    string ConectorDoEspelho = "",
    int EsperaPeloGiroSegundos = 10)
{
    /// <summary>Espelho ligado?</summary>
    public bool EspelhoLigado => !string.IsNullOrWhiteSpace(ConectorDoEspelho);

    /// <summary>Problemas que impedem a catraca de operar com esta configuração.</summary>
    public IReadOnlyList<string> Validar()
    {
        var problemas = new List<string>();

        if (TempoDeAcionamento is < 1 or > 50)
        {
            problemas.Add("O tempo de acionamento vai de 1 a 50 segundos.");
        }

        if (EspelhoLigado && EsperaPeloGiroSegundos <= TempoDeAcionamento)
        {
            // Senão o evento sobe antes de a catraca ter tido tempo de girar.
            problemas.Add("A espera pelo giro precisa ser maior que o tempo de acionamento.");
        }

        if (string.IsNullOrWhiteSpace(MensagemPadrao) || MensagemPadrao.Length > 32)
        {
            problemas.Add("A mensagem do display precisa ter de 1 a 32 caracteres.");
        }

        return problemas;
    }
}

/// <summary>
/// Configuração do evento guardada na base local, tabela <c>edge_setting</c>.
/// </summary>
/// <remarks>
/// Valor ausente é o padrão de <see cref="ConfiguracaoDaOperacao"/>. Valor ilegível
/// também — e é devolvido como problema, para o painel mostrar, em vez de derrubar o
/// worker na hora de subir.
/// </remarks>
public sealed class ConfiguracoesDaBorda
{
    public const string ChaveTipoDeLeitor = "leitor.tipo";
    public const string ChaveLeitorDaUrna = "leitor.urna";
    public const string ChaveTempoDeAcionamento = "catraca.acionamento_segundos";
    public const string ChaveMensagemPadrao = "catraca.mensagem";
    public const string ChaveConectorDoEspelho = "nuvem.conector_tentativas";
    public const string ChaveEsperaPeloGiro = "nuvem.espera_giro_segundos";

    private readonly SqliteConnectionFactory _fabrica;

    public ConfiguracoesDaBorda(SqliteConnectionFactory fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
    }

    /// <summary>Lê a configuração e os valores que não puderam ser lidos.</summary>
    public (ConfiguracaoDaOperacao Configuracao, IReadOnlyList<string> Ilegiveis) Ler()
    {
        var valores = Todos();
        var ilegiveis = new List<string>();
        var padrao = new ConfiguracaoDaOperacao();

        byte Byte(string chave, byte atual)
        {
            if (!valores.TryGetValue(chave, out var texto))
            {
                return atual;
            }

            if (byte.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }

            ilegiveis.Add(chave);
            return atual;
        }

        int Inteiro(string chave, int atual)
        {
            if (!valores.TryGetValue(chave, out var texto))
            {
                return atual;
            }

            if (int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }

            ilegiveis.Add(chave);
            return atual;
        }

        bool Logico(string chave, bool atual)
        {
            if (!valores.TryGetValue(chave, out var texto))
            {
                return atual;
            }

            switch (texto)
            {
                case "1":
                    return true;
                case "0":
                    return false;
                default:
                    ilegiveis.Add(chave);
                    return atual;
            }
        }

        var configuracao = new ConfiguracaoDaOperacao(
            TipoDeLeitor: Byte(ChaveTipoDeLeitor, padrao.TipoDeLeitor),
            LeitorDaUrna: Logico(ChaveLeitorDaUrna, padrao.LeitorDaUrna),
            TempoDeAcionamento: Byte(ChaveTempoDeAcionamento, padrao.TempoDeAcionamento),
            MensagemPadrao: valores.GetValueOrDefault(ChaveMensagemPadrao, padrao.MensagemPadrao),
            ConectorDoEspelho: valores.GetValueOrDefault(ChaveConectorDoEspelho, padrao.ConectorDoEspelho),
            EsperaPeloGiroSegundos: Inteiro(ChaveEsperaPeloGiro, padrao.EsperaPeloGiroSegundos));

        return (configuracao, ilegiveis);
    }

    /// <summary>Grava a configuração inteira, de uma vez, com quem mudou.</summary>
    /// <exception cref="ArgumentException">A configuração não passa em <see cref="ConfiguracaoDaOperacao.Validar"/>.</exception>
    public void Gravar(ConfiguracaoDaOperacao configuracao, DateTimeOffset agora, string? quem = null)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problemas), nameof(configuracao));
        }

        var pares = new Dictionary<string, string>
        {
            [ChaveTipoDeLeitor] = configuracao.TipoDeLeitor.ToString(CultureInfo.InvariantCulture),
            [ChaveLeitorDaUrna] = configuracao.LeitorDaUrna ? "1" : "0",
            [ChaveTempoDeAcionamento] = configuracao.TempoDeAcionamento.ToString(CultureInfo.InvariantCulture),
            [ChaveMensagemPadrao] = configuracao.MensagemPadrao,
            [ChaveConectorDoEspelho] = configuracao.ConectorDoEspelho,
            [ChaveEsperaPeloGiro] = configuracao.EsperaPeloGiroSegundos.ToString(CultureInfo.InvariantCulture),
        };

        using var conexao = _fabrica.Abrir();
        using var transacao = conexao.BeginTransaction();

        foreach (var (chave, valor) in pares)
        {
            using var comando = conexao.CreateCommand();
            comando.Transaction = transacao;
            comando.CommandText =
                """
                INSERT INTO edge_setting (key, value, updated_at, updated_by)
                VALUES ($chave, $valor, $em, $quem)
                ON CONFLICT (key) DO UPDATE SET
                    value = excluded.value, updated_at = excluded.updated_at, updated_by = excluded.updated_by
                WHERE edge_setting.value <> excluded.value;
                """;
            comando.Parameters.AddWithValue("$chave", chave);
            comando.Parameters.AddWithValue("$valor", valor);
            comando.Parameters.AddWithValue("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            comando.Parameters.AddWithValue("$quem", (object?)quem ?? DBNull.Value);
            comando.ExecuteNonQuery();
        }

        transacao.Commit();
    }

    private Dictionary<string, string> Todos()
    {
        var valores = new Dictionary<string, string>(StringComparer.Ordinal);

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = "SELECT key, value FROM edge_setting;";
        using var leitor = comando.ExecuteReader();

        while (leitor.Read())
        {
            valores[leitor.GetString(0)] = leitor.GetString(1);
        }

        return valores;
    }
}
