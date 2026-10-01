using System.Globalization;
using Access.Application.Devices;

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
/// <param name="AcertarRelogioAoDivergir">
/// Acerta o relógio da catraca sozinho, em operação, quando a conferência horária achar
/// divergência. Desligado: <c>A_CONFIRMAR_COM_TOPDATA</c> até o passo 6A do docs/21. Não
/// aparece no painel; é chave técnica (<c>relogio.acertar_ao_divergir</c>).
/// </param>
/// <param name="EnviarDigitosVariaveis">
/// Envia os tamanhos aceitos de dígitos variáveis à catraca
/// (<c>InserirQuantidadeDigitoVariavel</c>, EI-012). Desligado: nada é enviado e a catraca
/// segue com o padrão da DLL, como sempre foi (defeito F2, docs/34 §2). Fica desligado até
/// o ensaio HIL-CARD-02 (<c>A_CONFIRMAR_COM_TOPDATA</c>). Não aparece no painel; é chave
/// técnica (<c>catraca.enviar_digitos_variaveis</c>).
/// </param>
/// <param name="ReconectarEmErroDeRecepcao">
/// Reconecta quando <c>ReceberDadosOnLine</c> devolve retorno ≠ 0 (exceto o 8, sempre fatal).
/// Desligado: o erro é contado e registrado, e o laço segue como "sem eventos". Fica desligado
/// até o ensaio HIL-EVT-01 dizer o que a DLL devolve sem evento (<c>A_CONFIRMAR_COM_TOPDATA</c>).
/// Chave técnica (<c>catraca.reconectar_em_erro_de_recepcao</c>).
/// </param>
/// <param name="EnviarDataHoraNoEvento">
/// Envia <c>ReceberDataHoraDadosOnLine(1)</c> (EI-027). Desligado: a catraca segue com o padrão
/// da DLL. Chave técnica <c>catraca.enviar_data_hora_no_evento</c>, desligada até INT-CFG-07
/// (Etapa A.2, docs/34 §4.1).
/// </param>
/// <param name="RegistrarAcessoNegado">
/// Valor de <c>RegistrarAcessoNegado</c> (EI-021), 0 a 3. Nulo (chave vazia): não é enviado e a
/// catraca segue com o padrão da DLL. A chave leva o valor porque o significado de cada um é
/// <c>A_CONFIRMAR_COM_TOPDATA</c> e é o ensaio INT-OFF-08 que o descobre. Chave técnica
/// <c>catraca.registrar_acesso_negado</c>.
/// </param>
/// <param name="EnviarTipoDeLista">
/// Envia <c>DefinirTipoListaAcesso(0)</c> (EI-033): "não usar lista", explícito. Chave técnica
/// <c>catraca.enviar_tipo_de_lista</c>, desligada até INT-OFF-02.
/// </param>
/// <param name="EnviarWiegandDoisLeitores">
/// Envia <c>ConfigurarWiegandDoisLeitores(0, 0)</c> (EI-024). Chave técnica
/// <c>catraca.enviar_wiegand_dois_leitores</c>, desligada até HIL-CARD-05.
/// </param>
/// <param name="EnviarFormasDeEntrada">
/// Rearma o leitor com os valores do modelo em vez das constantes de sempre (EI-032; hoje são
/// os mesmos). Chave técnica <c>catraca.enviar_formas_de_entrada</c>, desligada até INT-SM-032
/// (T26).
/// </param>
/// <param name="SequenciaOficial">
/// Conecta pela sequência oficial — cfg off-line → mudança automática
/// (<c>EnviarConfiguracoesMudancaAutomaticaOnLineOffLine</c>, EI-029) → cfg on-line, com os mesmos
/// campos comuns nos dois envios (docs/34 §4.3). Desligada: os três envios iguais de sempre
/// (defeito F3, docs/34 §2). Só muda a ordem e a forma de envio, <b>não liga a contingência</b> (a
/// mudança automática segue 0, D8). Chave técnica <c>catraca.sequencia_oficial</c>, desligada até o
/// ensaio INT-SM-021 (Etapa A.7). É do laço, não da catraca: vale para todas as catracas do worker e
/// muda no próximo início dele, como <see cref="ReconectarEmErroDeRecepcao"/>.
/// </param>
/// <param name="ColetarBilhetes">
/// Permite o comando "coletar bilhetes" (<c>ColetarBilhete</c>, EI-039; Etapa A.9). Desligada: o
/// serviço recusa o pedido e o worker não o executa — nada sai da memória da catraca. Chave técnica
/// <c>catraca.coletar_bilhetes</c>, desligada até os ensaios INT-REC-03 e CHAOS-REC-01 (docs/21 §6F):
/// a coleta remove o bilhete do equipamento (FUN:40), e o que a catraca faz com o bilhete devolvido
/// e não confirmado é <c>A_CONFIRMAR_COM_TOPDATA</c>. Lida a cada pedido, sem reiniciar. Não liga a
/// coleta automática na volta do off-line (D8, docs/34 §9).
/// </param>
/// <param name="ExibirTextoDoGiro">
/// Mostra no display, antes de liberar, o texto do giro do mapa de giro ("Entrada liberada",
/// "Saida liberada" ou o personalizado; D9, docs/34 §9). Desligada: nada a mais vai à catraca no
/// caminho da passagem. Chave técnica <c>catraca.exibir_texto_do_giro</c>, desligada até o ensaio
/// NOVO-HIL-DIR-12 (docs/21 §6H): cada chamada a mais na passagem reduz a vazão (docs/34 §8), e se
/// a mensagem no meio da liberação atrapalha o giro é <c>A_CONFIRMAR_COM_TOPDATA</c>. É do laço:
/// muda no próximo início do worker.
/// </param>
public sealed record ConfiguracaoDaOperacao(
    byte TipoDeLeitor = 8,
    bool LeitorDaUrna = true,
    byte TempoDeAcionamento = 5,
    string MensagemPadrao = "Aproxime o ingresso",
    string ConectorDoEspelho = "",
    int EsperaPeloGiroSegundos = 10,
    bool AcertarRelogioAoDivergir = false,
    bool EnviarDigitosVariaveis = false,
    bool ReconectarEmErroDeRecepcao = false,
    bool EnviarDataHoraNoEvento = false,
    byte? RegistrarAcessoNegado = null,
    bool EnviarTipoDeLista = false,
    bool EnviarWiegandDoisLeitores = false,
    bool EnviarFormasDeEntrada = false,
    bool SequenciaOficial = false,
    bool ColetarBilhetes = false,
    bool ExibirTextoDoGiro = false)
{
    /// <summary>Espelho ligado?</summary>
    public bool EspelhoLigado => !string.IsNullOrWhiteSpace(ConectorDoEspelho);

    /// <summary>
    /// A parte desta configuração que vai para a catraca, como camada do
    /// <see cref="MontadorDaConfiguracao"/> (Etapa A.1 do docs/35).
    /// </summary>
    /// <remarks>
    /// Todo campo sai preenchido: o que o operador não mudou já chega aqui com o padrão deste
    /// registro, igual ao de fábrica. Ficam de fora, de propósito, o que não é parâmetro da
    /// catraca: o conector e a espera da nuvem (repositório), <see cref="AcertarRelogioAoDivergir"/>
    /// e <see cref="SequenciaOficial"/> (laço), <see cref="ColetarBilhetes"/> (comando) e <see cref="ReconectarEmErroDeRecepcao"/> (adapter).
    /// </remarks>
    public SobreposicoesDoEvento ParaACatraca() => new()
    {
        TipoDeLeitor = TipoDeLeitor,
        LeitorDaUrna = LeitorDaUrna,
        TempoDeAcionamento = TempoDeAcionamento,
        MensagemPadrao = MensagemPadrao,
        EnviarDigitosVariaveis = EnviarDigitosVariaveis,
        EnviarDataHoraNoEventoOnLine = EnviarDataHoraNoEvento,
        RegistrarAcessoNegado = RegistrarAcessoNegado,
        EnviarTipoDeLista = EnviarTipoDeLista,
        EnviarWiegandDoisLeitores = EnviarWiegandDoisLeitores,
        EnviarFormasDeEntradaOnLine = EnviarFormasDeEntrada,
    };

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

        // A faixa da função (FUN:22). Recusar aqui evita que o worker monte uma configuração
        // que o adapter recusaria.
        if (RegistrarAcessoNegado is > 3)
        {
            problemas.Add("O registro de acesso negado vai de 0 a 3.");
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
    public const string ChaveAcertarRelogioAoDivergir = "relogio.acertar_ao_divergir";
    public const string ChaveEnviarDigitosVariaveis = "catraca.enviar_digitos_variaveis";
    public const string ChaveReconectarEmErroDeRecepcao = "catraca.reconectar_em_erro_de_recepcao";

    // Etapa A.2 (docs/34 §4.1): todas sem tela e desligadas por padrão; desligada = não enviado.
    public const string ChaveEnviarDataHoraNoEvento = "catraca.enviar_data_hora_no_evento";

    /// <summary>Leva o próprio valor, 0 a 3; vazia = desligada (EI-021, INT-OFF-08).</summary>
    public const string ChaveRegistrarAcessoNegado = "catraca.registrar_acesso_negado";
    public const string ChaveEnviarTipoDeLista = "catraca.enviar_tipo_de_lista";
    public const string ChaveEnviarWiegandDoisLeitores = "catraca.enviar_wiegand_dois_leitores";
    public const string ChaveEnviarFormasDeEntrada = "catraca.enviar_formas_de_entrada";

    /// <summary>Etapa A.7 (docs/34 §4.3): sequência oficial de conexão; desligada até INT-SM-021.</summary>
    public const string ChaveSequenciaOficial = "catraca.sequencia_oficial";

    /// <summary>Etapa A.9: comando de coleta de bilhetes; desligada até INT-REC-03 e CHAOS-REC-01.</summary>
    public const string ChaveColetarBilhetes = "catraca.coletar_bilhetes";

    /// <summary>Chave técnica do texto do giro no display (mapa de giro, D9). Desligada.</summary>
    public const string ChaveExibirTextoDoGiro = "catraca.exibir_texto_do_giro";

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

        // Vazia (ou ausente) é desligada; um byte é o valor; o resto é ilegível e desliga.
        byte? ByteOpcional(string chave)
        {
            if (!valores.TryGetValue(chave, out var texto) || texto.Length == 0)
            {
                return null;
            }

            if (byte.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }

            ilegiveis.Add(chave);
            return null;
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
            EsperaPeloGiroSegundos: Inteiro(ChaveEsperaPeloGiro, padrao.EsperaPeloGiroSegundos),
            AcertarRelogioAoDivergir: Logico(ChaveAcertarRelogioAoDivergir, padrao.AcertarRelogioAoDivergir),
            EnviarDigitosVariaveis: Logico(ChaveEnviarDigitosVariaveis, padrao.EnviarDigitosVariaveis),
            ReconectarEmErroDeRecepcao: Logico(ChaveReconectarEmErroDeRecepcao, padrao.ReconectarEmErroDeRecepcao),
            EnviarDataHoraNoEvento: Logico(ChaveEnviarDataHoraNoEvento, padrao.EnviarDataHoraNoEvento),
            RegistrarAcessoNegado: ByteOpcional(ChaveRegistrarAcessoNegado),
            EnviarTipoDeLista: Logico(ChaveEnviarTipoDeLista, padrao.EnviarTipoDeLista),
            EnviarWiegandDoisLeitores: Logico(ChaveEnviarWiegandDoisLeitores, padrao.EnviarWiegandDoisLeitores),
            EnviarFormasDeEntrada: Logico(ChaveEnviarFormasDeEntrada, padrao.EnviarFormasDeEntrada),
            SequenciaOficial: Logico(ChaveSequenciaOficial, padrao.SequenciaOficial),
            ColetarBilhetes: Logico(ChaveColetarBilhetes, padrao.ColetarBilhetes),
            ExibirTextoDoGiro: Logico(ChaveExibirTextoDoGiro, padrao.ExibirTextoDoGiro));

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
            [ChaveAcertarRelogioAoDivergir] = configuracao.AcertarRelogioAoDivergir ? "1" : "0",
            [ChaveEnviarDigitosVariaveis] = configuracao.EnviarDigitosVariaveis ? "1" : "0",
            [ChaveReconectarEmErroDeRecepcao] = configuracao.ReconectarEmErroDeRecepcao ? "1" : "0",
            [ChaveEnviarDataHoraNoEvento] = configuracao.EnviarDataHoraNoEvento ? "1" : "0",
            [ChaveRegistrarAcessoNegado] = configuracao.RegistrarAcessoNegado?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            [ChaveEnviarTipoDeLista] = configuracao.EnviarTipoDeLista ? "1" : "0",
            [ChaveEnviarWiegandDoisLeitores] = configuracao.EnviarWiegandDoisLeitores ? "1" : "0",
            [ChaveEnviarFormasDeEntrada] = configuracao.EnviarFormasDeEntrada ? "1" : "0",
            [ChaveSequenciaOficial] = configuracao.SequenciaOficial ? "1" : "0",
            [ChaveColetarBilhetes] = configuracao.ColetarBilhetes ? "1" : "0",
            [ChaveExibirTextoDoGiro] = configuracao.ExibirTextoDoGiro ? "1" : "0",
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
