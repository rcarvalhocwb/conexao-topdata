using System.Globalization;
using Access.Application.Devices;
using Microsoft.Data.Sqlite;

namespace Access.Infrastructure.SQLite;

/// <summary>A configuração de uma catraca como está gravada, com quem mudou e quando.</summary>
/// <param name="Inner">Número da catraca (1 a 99).</param>
/// <param name="Sobreposicoes">O que ela sobrepõe ao evento; campo nulo herda.</param>
/// <param name="Revisao">Revisão atual (a primeira gravação é a 1).</param>
/// <param name="AlteradoPor">Nome digitado por quem gravou (não há login, docs/27 §11).</param>
/// <param name="AlteradoEm">Quando, em UTC.</param>
/// <param name="Problemas">Valores ilegíveis desta linha; cada um volta a herdar do evento.</param>
public sealed record ConfiguracaoDaCatraca(
    int Inner,
    SobreposicoesDaCatraca Sobreposicoes,
    int Revisao,
    string AlteradoPor,
    DateTimeOffset AlteradoEm,
    IReadOnlyList<string> Problemas);

/// <summary>
/// Configuração por catraca, tabela <c>device_config</c> (migração 012), e o seu histórico.
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.3 do docs/35. Fica ao lado de <see cref="ConfiguracoesDaBorda"/> (o evento) e
/// segue o mesmo contrato com quem chama pela tela: <b>nada de exceção</b> para valor
/// inválido. Gravar devolve os problemas e não grava nada; ler devolve o valor ilegível como
/// problema e aquele campo volta a herdar do evento, em vez de derrubar o worker ao subir.
/// </para>
/// <para>
/// Gravar substitui a camada inteira da catraca: campo nulo = herda (é assim que se "limpa").
/// O histórico é escrito pelo gatilho da 012, um retrato completo por gravação, e não pode
/// ser mudado nem apagado.
/// </para>
/// <para>
/// A validação da gravação monta o resultado (fábrica → evento de agora → esta catraca) e
/// passa em <see cref="DeviceConfiguration.Validar"/>, como o adapter faria: o que a catraca
/// recusaria não chega à base. O evento pode mudar depois e tornar a combinação inválida;
/// quem aplica (worker, Etapa A.4) valida de novo e cai no padrão daquela catraca.
/// </para>
/// </remarks>
public sealed class ConfiguracoesDasCatracas
{
    /// <summary>Faixa do número da catraca (docs/34 §4.2, regra 12; T9).</summary>
    public const int InnerMinimo = 1;

    /// <inheritdoc cref="InnerMinimo"/>
    public const int InnerMaximo = 99;

    private const string Colunas =
        "inner_number, reader_type, reader1_operation, reader2_operation, relay1_seconds, " +
        "release_function, default_message, wiegand_enabled, wiegand_show_message, " +
        "entry_keypad_digits, entry_keypad_echo, entry_form, entry_keypad_time, entry_cursor_position, " +
        "revision, updated_by, updated_at";

    private readonly SqliteConnectionFactory _fabrica;
    private readonly Func<DeviceConfiguration> _padraoDeFabrica;

    /// <param name="fabrica">Conexões com a base local.</param>
    /// <param name="padraoDeFabrica">
    /// A primeira camada da validação. Nulo = <see cref="PadroesDeFabrica.TopFit4"/>, o mesmo
    /// que o worker usa.
    /// </param>
    public ConfiguracoesDasCatracas(SqliteConnectionFactory fabrica, Func<DeviceConfiguration>? padraoDeFabrica = null)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        _fabrica = fabrica;
        _padraoDeFabrica = padraoDeFabrica ?? (() => PadroesDeFabrica.TopFit4);
    }

    /// <summary>
    /// O que a catraca sobrepõe. Sem linha gravada, <see cref="SobreposicoesDaCatraca.Nenhuma"/>.
    /// </summary>
    public (SobreposicoesDaCatraca Sobreposicoes, IReadOnlyList<string> Problemas) Ler(int inner)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = $"SELECT {Colunas} FROM device_config WHERE inner_number = $inner;";
        comando.Parameters.AddWithValue("$inner", inner);
        using var leitor = comando.ExecuteReader();

        if (!leitor.Read())
        {
            return (SobreposicoesDaCatraca.Nenhuma, []);
        }

        var registro = Converter(leitor);
        return (registro.Sobreposicoes, registro.Problemas);
    }

    /// <summary>Todas as catracas com configuração gravada, pela ordem do número.</summary>
    public IReadOnlyList<ConfiguracaoDaCatraca> Listar()
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText = $"SELECT {Colunas} FROM device_config ORDER BY inner_number;";
        return LerTodos(comando);
    }

    /// <summary>Cada gravação da catraca, da primeira à última.</summary>
    public IReadOnlyList<ConfiguracaoDaCatraca> Historico(int inner)
    {
        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            $"SELECT {Colunas} FROM device_config_history WHERE inner_number = $inner ORDER BY revision;";
        comando.Parameters.AddWithValue("$inner", inner);
        return LerTodos(comando);
    }

    /// <summary>
    /// Grava a camada inteira da catraca (campo nulo = herda), com quem mudou.
    /// </summary>
    /// <returns>Os problemas; vazia quando gravou. Com problema, nada é gravado.</returns>
    public IReadOnlyList<string> Gravar(int inner, SobreposicoesDaCatraca catraca, DateTimeOffset agora, string quem)
    {
        ArgumentNullException.ThrowIfNull(catraca);

        var problemas = Validar(inner, catraca, quem);
        if (problemas.Count > 0)
        {
            return problemas;
        }

        using var conexao = _fabrica.Abrir();
        using var comando = conexao.CreateCommand();
        comando.CommandText =
            """
            INSERT INTO device_config (
                inner_number, reader_type, reader1_operation, reader2_operation, relay1_seconds,
                release_function, default_message, wiegand_enabled, wiegand_show_message,
                entry_keypad_digits, entry_keypad_echo, entry_form, entry_keypad_time, entry_cursor_position,
                revision, updated_by, updated_at)
            VALUES (
                $inner, $tipo, $leitor1, $leitor2, $tempo,
                $funcao, $mensagem, $wiegand, $wiegandMensagem,
                $digitos, $eco, $forma, $tempoTeclado, $cursor,
                1, $quem, $em)
            ON CONFLICT (inner_number) DO UPDATE SET
                reader_type = excluded.reader_type,
                reader1_operation = excluded.reader1_operation,
                reader2_operation = excluded.reader2_operation,
                relay1_seconds = excluded.relay1_seconds,
                release_function = excluded.release_function,
                default_message = excluded.default_message,
                wiegand_enabled = excluded.wiegand_enabled,
                wiegand_show_message = excluded.wiegand_show_message,
                entry_keypad_digits = excluded.entry_keypad_digits,
                entry_keypad_echo = excluded.entry_keypad_echo,
                entry_form = excluded.entry_form,
                entry_keypad_time = excluded.entry_keypad_time,
                entry_cursor_position = excluded.entry_cursor_position,
                revision = device_config.revision + 1,
                updated_by = excluded.updated_by,
                updated_at = excluded.updated_at;
            """;

        void Parametro(string nome, object? valor) => comando.Parameters.AddWithValue(nome, valor ?? DBNull.Value);

        var formas = catraca.FormasDeEntradaOnLine;
        Parametro("$inner", inner);
        Parametro("$tipo", catraca.TipoDeLeitor);
        Parametro("$leitor1", catraca.OperacaoDoLeitor1);
        Parametro("$leitor2", catraca.OperacaoDoLeitor2);
        Parametro("$tempo", catraca.TempoDoAcionamento1);
        Parametro("$funcao", catraca.FuncaoDeLiberacaoDaEntrada?.ToString());
        Parametro("$mensagem", catraca.MensagemPadrao);
        Parametro("$wiegand", catraca.WiegandDoisLeitores is { } w ? (w.Habilitado ? 1 : 0) : null);
        Parametro("$wiegandMensagem", catraca.WiegandDoisLeitores is { } m ? (m.ExibirMensagem ? 1 : 0) : null);
        Parametro("$digitos", formas?.QtdeDigitosTeclado);
        Parametro("$eco", formas?.EcoTeclado);
        Parametro("$forma", formas?.FormaEntrada);
        Parametro("$tempoTeclado", formas?.TempoTeclado);
        Parametro("$cursor", formas?.PosicaoCursorTeclado);
        Parametro("$quem", quem.Trim());
        Parametro("$em", agora.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        comando.ExecuteNonQuery();

        return [];
    }

    /// <summary>
    /// O que impede a gravação: as faixas do evento para os mesmos campos, e o resultado
    /// montado passando em <see cref="DeviceConfiguration.Validar"/>.
    /// </summary>
    private List<string> Validar(int inner, SobreposicoesDaCatraca catraca, string? quem)
    {
        var problemas = new List<string>();

        if (inner is < InnerMinimo or > InnerMaximo)
        {
            problemas.Add($"O número da catraca vai de {InnerMinimo} a {InnerMaximo} (recebido {inner}).");
        }

        if (string.IsNullOrWhiteSpace(quem))
        {
            problemas.Add("Informe quem está gravando.");
        }

        // As duas faixas em que o evento é mais estreito que DeviceConfiguration.Validar
        // (ConfiguracaoDaOperacao.Validar): a catraca não aceita o que o evento recusaria.
        if (catraca.TempoDoAcionamento1 is 0)
        {
            problemas.Add("O tempo de acionamento vai de 1 a 50 segundos.");
        }

        if (catraca.MensagemPadrao is { } mensagem && string.IsNullOrWhiteSpace(mensagem))
        {
            problemas.Add("A mensagem do display precisa ter de 1 a 32 caracteres.");
        }

        // O resultado que a catraca receberia hoje. As faixas de cada campo (tipo de leitor,
        // leitores, tempo até 50, mensagem até 32, função de liberação, FormaEntrada) e as
        // regras entre campos vêm todas daqui, sem uma segunda cópia.
        var (evento, _) = new ConfiguracoesDaBorda(_fabrica).Ler();
        var montada = MontadorDaConfiguracao.Montar(_padraoDeFabrica(), evento.ParaACatraca(), catraca);
        problemas.AddRange(montada.Validar());

        return problemas;
    }

    private static List<ConfiguracaoDaCatraca> LerTodos(SqliteCommand comando)
    {
        var registros = new List<ConfiguracaoDaCatraca>();
        using var leitor = comando.ExecuteReader();

        while (leitor.Read())
        {
            registros.Add(Converter(leitor));
        }

        return registros;
    }

    /// <summary>
    /// Uma linha da tabela (ou do histórico) para o modelo. Valor fora da faixa — só possível
    /// se alguém passou por cima dos CHECK — vira problema e o campo herda do evento.
    /// </summary>
    private static ConfiguracaoDaCatraca Converter(SqliteDataReader leitor)
    {
        var inner = leitor.GetInt32(0);
        var problemas = new List<string>();

        void Ilegivel(string coluna, object valor) =>
            problemas.Add($"Catraca {inner}: {coluna} ilegível ({valor}); herda do evento.");

        long? Inteiro(int indice) => leitor.IsDBNull(indice) ? null : leitor.GetInt64(indice);

        string? Texto(int indice) => leitor.IsDBNull(indice) ? null : leitor.GetString(indice);

        byte? Faixa(int indice, string coluna, int minimo, int maximo)
        {
            var valor = Inteiro(indice);
            if (valor is null)
            {
                return null;
            }

            if (valor < minimo || valor > maximo)
            {
                Ilegivel(coluna, valor);
                return null;
            }

            return (byte)valor;
        }

        var tipo = Faixa(1, "reader_type", 0, 8);
        var leitor1 = Faixa(2, "reader1_operation", 0, 4);
        var leitor2 = Faixa(3, "reader2_operation", 0, 4);
        var tempo = Faixa(4, "relay1_seconds", 1, 50);

        FuncaoDeLiberacao? funcao = null;
        if (Texto(5) is { } nomeDaFuncao)
        {
            // Só o nome exato: "1" ou "entrada" também passariam em Enum.TryParse.
            if (Enum.GetNames<FuncaoDeLiberacao>().Contains(nomeDaFuncao, StringComparer.Ordinal))
            {
                funcao = Enum.Parse<FuncaoDeLiberacao>(nomeDaFuncao);
            }
            else
            {
                Ilegivel("release_function", nomeDaFuncao);
            }
        }

        var mensagem = Texto(6);
        if (mensagem is not null && (string.IsNullOrWhiteSpace(mensagem) || mensagem.Length > 32))
        {
            Ilegivel("default_message", $"{mensagem.Length} caracteres");
            mensagem = null;
        }

        // Um membro já ilegível basta como problema: não se conta de novo o grupo incompleto.
        var antesDoWiegand = problemas.Count;
        WiegandDoisLeitores? wiegand = null;
        var wiegandHabilitado = Faixa(7, "wiegand_enabled", 0, 1);
        var wiegandMensagem = Faixa(8, "wiegand_show_message", 0, 1);
        if (wiegandHabilitado is { } h && wiegandMensagem is { } e)
        {
            wiegand = new WiegandDoisLeitores(h == 1, e == 1);
        }
        else if ((wiegandHabilitado is not null || wiegandMensagem is not null) && problemas.Count == antesDoWiegand)
        {
            Ilegivel("wiegand_*", "par incompleto");
        }

        var antesDasFormas = problemas.Count;
        FormasDeEntradaOnLine? formas = null;
        var digitos = Faixa(9, "entry_keypad_digits", 0, 255);
        var eco = Faixa(10, "entry_keypad_echo", 0, 255);
        var forma = Faixa(11, "entry_form", 0, 255);
        if (forma is { } f && !FormasDeEntradaOnLine.FormaEntradaDocumentada(f))
        {
            Ilegivel("entry_form", f);
            forma = null;
        }

        var tempoTeclado = Faixa(12, "entry_keypad_time", 0, 255);
        var cursor = Faixa(13, "entry_cursor_position", 0, 255);
        byte?[] cinco = [digitos, eco, forma, tempoTeclado, cursor];
        if (cinco.All(v => v is not null))
        {
            formas = new FormasDeEntradaOnLine(digitos!.Value, eco!.Value, forma!.Value, tempoTeclado!.Value, cursor!.Value);
        }
        else if (cinco.Any(v => v is not null) && problemas.Count == antesDasFormas)
        {
            Ilegivel("entry_*", "conjunto incompleto");
        }

        var sobreposicoes = new SobreposicoesDaCatraca
        {
            TipoDeLeitor = tipo,
            OperacaoDoLeitor1 = leitor1,
            OperacaoDoLeitor2 = leitor2,
            TempoDoAcionamento1 = tempo,
            FuncaoDeLiberacaoDaEntrada = funcao,
            MensagemPadrao = mensagem,
            WiegandDoisLeitores = wiegand,
            FormasDeEntradaOnLine = formas,
        };

        // O instante só informa; ilegível, vira problema e não derruba a leitura.
        if (!DateTimeOffset.TryParse(
                leitor.GetString(16), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var em))
        {
            problemas.Add($"Catraca {inner}: updated_at ilegível.");
        }

        return new ConfiguracaoDaCatraca(inner, sobreposicoes, leitor.GetInt32(14), leitor.GetString(15), em, problemas);
    }
}
