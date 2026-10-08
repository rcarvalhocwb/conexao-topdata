using System.Globalization;
using Contracts.Edge.V1;

namespace Desktop.ViewModels;

/// <summary>
/// Como a situação técnica aparece para o operador: português de portaria, sem jargão.
/// </summary>
public static class Textos
{
    /// <summary>Situação de uma catraca em uma frase curta, e a cor dela.</summary>
    public static (string Texto, Sinal Sinal) SituacaoDaCatraca(Equipamento equipamento)
    {
        ArgumentNullException.ThrowIfNull(equipamento);

        if (equipamento.EmOperacao)
        {
            return ("Atendendo", Sinal.Bom);
        }

        var estado = equipamento.Estado ?? string.Empty;

        if (estado.StartsWith("sem notícia", StringComparison.Ordinal))
        {
            return ("Sem notícia do programa da catraca", Sinal.Problema);
        }

        return estado switch
        {
            "aguardando a catraca" => ("Aguardando a catraca conectar", Sinal.Neutro),
            "Discovering" or "Conectar" or "Reconectar" => ("Conectando…", Sinal.Atencao),
            "LendoIdentidade" or "VerificandoCompatibilidade" or "EnviarCfgOffline"
                or "EnviarConfigMudOnlineOffline" or "EnviarCfgOnline" or "ConfigurarEntradasOnline"
                or "EnviarMsgPadrao" or "SincronizandoDadosOffline" => ("Configurando…", Sinal.Atencao),
            "OfflineAutonomo" => ("Operando sozinha, sem o PC", Sinal.Atencao),
            "Degradado" => ("Com problema — veja o diagnóstico", Sinal.Problema),
            "Disabled" => ("Desligada", Sinal.Neutro),
            "Morto" => ("Programa da catraca parou", Sinal.Problema),
            "Quarentena" => ("Programa da catraca parou várias vezes; nova tentativa automática em até 15 min", Sinal.Problema),
            "SemBatimento" => ("Programa da catraca não responde", Sinal.Problema),
            "Parado" => ("Parada", Sinal.Neutro),
            _ => (estado, Sinal.Atencao),
        };
    }

    /// <summary>"há 5 s", "há 3 min", "há 2 h" — ou "nunca".</summary>
    public static string Ha(DateTimeOffset? quando, DateTimeOffset agora)
    {
        if (quando is not { } q || q == DateTimeOffset.UnixEpoch)
        {
            return "nunca";
        }

        var passou = agora - q;

        if (passou < TimeSpan.Zero)
        {
            passou = TimeSpan.Zero;
        }

        return passou.TotalSeconds < 60
            ? string.Create(CultureInfo.InvariantCulture, $"há {passou.TotalSeconds:F0} s")
            : passou.TotalMinutes < 60
                ? string.Create(CultureInfo.InvariantCulture, $"há {passou.TotalMinutes:F0} min")
                : string.Create(CultureInfo.InvariantCulture, $"há {passou.TotalHours:F0} h");
    }

    /// <summary>
    /// O relógio da catraca em poucas palavras: "certo · conferido há 12 min", "atrasado
    /// 45 s", "data inválida" — ou "não conferido" enquanto a catraca não chegou a operar.
    /// </summary>
    public static (string Texto, bool Divergente) Relogio(Equipamento equipamento, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(equipamento);

        if (equipamento.RelogioDivergente)
        {
            if (!equipamento.HasDivergenciaDoRelogioSegundos)
            {
                return ("data inválida na catraca", true);
            }

            var d = equipamento.DivergenciaDoRelogioSegundos;
            return ($"{(d > 0 ? "adiantado" : "atrasado")} {Duracao(Math.Abs(d))}", true);
        }

        if (equipamento.RelogioConferidoEm is { } conferido)
        {
            return ($"certo · conferido {Ha(conferido.ToDateTimeOffset(), agora)}", false);
        }

        return equipamento.RelogioAcertadoEm is { } acertado
            ? ($"acertado {Ha(acertado.ToDateTimeOffset(), agora)}", false)
            : ("não conferido", false);
    }

    private static string Duracao(int segundos) =>
        segundos < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{segundos} s")
            : segundos < 3600
                ? string.Create(CultureInfo.InvariantCulture, $"{segundos / 60} min {segundos % 60} s")
                : string.Create(CultureInfo.InvariantCulture, $"{segundos / 3600} h {segundos % 3600 / 60} min");

    /// <summary>O nome de um comando, como o operador o reconhece.</summary>
    public static string NomeDoComando(TipoDeComando tipo) => tipo switch
    {
        TipoDeComando.AcertarRelogio => "Acertar relógio",
        TipoDeComando.MensagemTemporaria => "Mensagem no display",
        TipoDeComando.LiberacaoManual => "Liberação manual",
        TipoDeComando.ReiniciarConexao => "Refazer conexão",
        TipoDeComando.AplicarConfiguracao => "Aplicar configuração",
        TipoDeComando.BipCurto => "Bip curto",
        TipoDeComando.BipLongo => "Bip longo",
        TipoDeComando.LiberarSaida => "Liberar saída",
        TipoDeComando.LiberarDoisSentidos => "Liberar nos dois sentidos",

        // Só no histórico: não há botão para pedir (Etapa A.9; chave técnica desligada, sem tela).
        TipoDeComando.ColetarBilhetes => "Coletar marcações",
        _ => "Comando",
    };

    /// <summary>Em que pé está um comando, e a cor.</summary>
    public static (string Texto, Sinal Sinal) SituacaoDoComando(SituacaoDoComando situacao) => situacao switch
    {
        Contracts.Edge.V1.SituacaoDoComando.Pendente => ("Aguardando a catraca", Sinal.Neutro),
        Contracts.Edge.V1.SituacaoDoComando.Recebido => ("Na fila da catraca", Sinal.Atencao),
        Contracts.Edge.V1.SituacaoDoComando.Concluido => ("Feito", Sinal.Bom),
        Contracts.Edge.V1.SituacaoDoComando.Falhou => ("Falhou", Sinal.Problema),
        Contracts.Edge.V1.SituacaoDoComando.Expirado => ("Não executado a tempo", Sinal.Atencao),
        _ => ("—", Sinal.Neutro),
    };

    /// <summary>
    /// A saúde do Analisador da camada inteligente (Etapa I.0 do docs/36), em português de
    /// suporte: o resumo, a cor e as linhas da conta. Desligada é o normal desta versão — tom
    /// neutro, nunca alerta.
    /// </summary>
    public static (string Resumo, Sinal Sinal, IReadOnlyList<ParDeTexto> Linhas) SaudeDoAnalisador(SaudeDoAnalisador? saude, DateTimeOffset agora)
    {
        if (saude is null || (!saude.Ligado && !saude.Rodando && saude.Falhas == 0))
        {
            return ("Desligada nesta instalação", Sinal.Neutro,
            [
                new ParDeTexto("Situação", "Desligada (chave técnica inteligencia.ligada). Liga quem faz o ensaio, com reinício do serviço."),
                new ParDeTexto("Por que negou", "Funciona com a camada desligada: a explicação vem do que a catraca já gravou."),
            ]);
        }

        var cultura = CultureInfo.InvariantCulture;
        var ultimo = saude.UltimoCiclo is null ? "nenhum ainda" : Ha(saude.UltimoCiclo.ToDateTimeOffset(), agora);
        var erro = string.IsNullOrEmpty(saude.UltimoErro)
            ? "nenhum"
            : saude.UltimoErroEm is null ? saude.UltimoErro : $"{saude.UltimoErro} · {Ha(saude.UltimoErroEm.ToDateTimeOffset(), agora)}";

        var (resumo, sinal) = (saude.Ligado, saude.Rodando, saude.Falhas, saude.Ciclos) switch
        {
            (false, _, > 0, _) => ("Não subiu — veja o último erro", Sinal.Atencao),
            (true, false, _, _) => ("Parada", Sinal.Atencao),
            (true, true, > 0, 0) => ("Ligada, com erro em todos os ciclos", Sinal.Problema),
            (true, true, > 0, _) => ("Funcionando, com erros", Sinal.Atencao),
            _ => ("Funcionando", Sinal.Bom),
        };

        return (resumo, sinal,
        [
            new ParDeTexto("Último ciclo", ultimo),
            new ParDeTexto("Duração do último ciclo", string.Create(cultura, $"{saude.DuracaoDoUltimoCicloMs} ms (orçamento: {saude.OrcamentoMs} ms)")),
            new ParDeTexto("Ciclos", string.Create(cultura, $"{saude.Ciclos} feitos · {saude.Estouros} acima do orçamento · {saude.Pulados} pulados · {saude.Falhas} com erro")),
            new ParDeTexto("Último erro", erro),
            new ParDeTexto("Tentativas lidas", string.Create(cultura, $"{saude.TentativasLidas} desde a partida do serviço")),
        ]);
    }

    /// <summary>Hora no relógio do evento (Brasília), no formato do painel.</summary>
    public static string Hora(DateTimeOffset quando) =>
        FusoDoEvento.NoEvento(quando).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>Uma catraca, pronta para a tela.</summary>
/// <remarks>
/// <c>Simulacao</c>: a situação veio de uma catraca simulada desta partida do serviço. O cartão
/// mostra o selo "Simulação", coerente com o aviso geral do modo simulação — nunca uma catraca
/// simulada passando por real (docs/29, defeito de 01/10).
/// </remarks>
public sealed record LinhaDeCatraca(
    int Inner,
    string Nome,
    string Situacao,
    Sinal Sinal,
    string UltimaDecisao,
    string UltimoEvento,
    string Firmware,
    string Grupo,
    int Porta,
    int Reconexoes,
    string Relogio = "não conferido",
    bool RelogioDivergente = false,
    bool Simulacao = false)
{
    /// <summary>"CATRACA 01": como a catraca é chamada no cartão do dispositivo.</summary>
    public string Rotulo => string.Create(CultureInfo.InvariantCulture, $"CATRACA {Inner:D2}");

    /// <summary>A linha técnica do cartão: firmware, grupo, porta e reconexões.</summary>
    public string Tecnico => string.Create(
        CultureInfo.InvariantCulture,
        $"Firmware {(string.IsNullOrWhiteSpace(Firmware) ? "—" : Firmware)} · grupo {Grupo} · porta {Porta} · {Reconexoes} reconex{(Reconexoes == 1 ? "ão" : "ões")}");
}

/// <summary>Um acesso, pronto para a tela. O código já vem mascarado do serviço.</summary>
/// <remarks>
/// <c>EventoId</c> é o identificador da tentativa, para pedir o "Por quê?" (Etapa I.2 do docs/36);
/// vazio em linha montada sem o serviço.
/// </remarks>
public sealed record LinhaDeAcesso(
    string Hora,
    int Inner,
    string Mensagem,
    bool Liberado,
    bool Girou,
    string Categoria,
    string Codigo,
    Sinal Sinal,
    string EventoId = "")
{
    /// <summary>A linha mostra "Por quê?": toda negação que o serviço pode explicar.</summary>
    public bool PodeExplicar => !Liberado && EventoId.Length > 0;

    public static LinhaDeAcesso De(EventoDeAcesso e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var liberado = e.Resultado == ResultadoDoAcesso.Permitido;

        return new LinhaDeAcesso(
            e.RecebidoEm is null ? string.Empty : Textos.Hora(e.RecebidoEm.ToDateTimeOffset()),
            e.Inner,
            e.MensagemAoOperador,
            liberado,
            e.PassagemConfirmada,
            e.Categoria,
            e.CredencialMascarada,
            liberado ? Sinal.Bom : Sinal.Problema,
            e.EventoId);
    }
}
