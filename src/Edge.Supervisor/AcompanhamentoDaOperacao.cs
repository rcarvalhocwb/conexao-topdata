using System.Globalization;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;

namespace Edge.Supervisor;

/// <summary>
/// Leva ao painel, ao vivo, cada tentativa que os workers gravam na base local.
/// </summary>
/// <remarks>
/// O worker grava e segue girando catraca; este laço lê o que é novo a cada meio segundo
/// e difunde. Se o serviço cair, a tentativa já está gravada — o painel só a vê depois.
/// Ver ADR-0024.
/// </remarks>
public sealed class AcompanhamentoDaOperacao : BackgroundService
{
    private readonly Operacao _operacao;
    private readonly DifusorDeEventos _difusor;
    private readonly TimeSpan _intervalo;
    private long _ultima;

    public AcompanhamentoDaOperacao(Operacao operacao, EdgeControlService servico)
        : this(operacao, servico.Eventos, TimeSpan.FromMilliseconds(500))
    {
    }

    public AcompanhamentoDaOperacao(Operacao operacao, DifusorDeEventos difusor, TimeSpan intervalo)
    {
        ArgumentNullException.ThrowIfNull(operacao);
        ArgumentNullException.ThrowIfNull(difusor);

        _operacao = operacao;
        _difusor = difusor;
        _intervalo = intervalo;
    }

    /// <summary>Começa do fim: o painel não relê o dia inteiro ao abrir.</summary>
    public void ComecarDoFim() => _ultima = _operacao.UltimaSequencia();

    /// <summary>Uma leitura: difunde o que é novo e devolve quantas tentativas eram.</summary>
    public int UmaLeitura()
    {
        var novas = _operacao.TentativasDepoisDe(_ultima);

        foreach (var t in novas)
        {
            _difusor.Writer.TryWrite(Converter(t));
            _ultima = t.Sequencia;
        }

        return novas.Count;
    }

    /// <summary>Como uma tentativa aparece no painel.</summary>
    public static EventoDeAcesso Converter(TentativaParaOPainel t)
    {
        ArgumentNullException.ThrowIfNull(t);

        return new EventoDeAcesso
        {
            EventoId = t.Id.ToString(),
            Inner = InnerDe(t.DeviceId),
            RecebidoEm = Timestamp.FromDateTimeOffset(t.Em),
            CredencialMascarada = t.CodigoMascarado,
            Resultado = t.Liberou ? ResultadoDoAcesso.Permitido : ResultadoDoAcesso.Negado,
            Motivo = t.Motivo,
            MensagemAoOperador = MensagemPara(t),
            PassagemConfirmada = t.Girou,
            CorrelationId = t.Id.ToString(),
            Categoria = t.Categoria ?? string.Empty,
            Portao = t.Portao,
        };
    }

    /// <summary>O motivo em português de operador, sem jargão.</summary>
    public static string MensagemPara(TentativaParaOPainel t)
    {
        ArgumentNullException.ThrowIfNull(t);

        if (t.Liberou)
        {
            return t.Categoria is { Length: > 0 } c ? $"Liberado · {c}" : "Liberado";
        }

        return t.Motivo switch
        {
            "Desconhecido" => "Negado · código não cadastrado",
            "UsosEsgotados" => "Negado · já utilizado",
            "Cancelado" => "Negado · cancelado",
            "Bloqueado" => "Negado · bloqueado",
            "ForaDaJanela" => "Negado · fora do horário de validade",
            "ProvedorDesabilitado" => "Negado · venda desabilitada",
            "EmIntervaloDeReuso" => "Negado · cartão usado há pouco",
            "VendaAnteriorNaoUsada" => "Negado · venda anterior não usada",
            "ForaDaUrna" => "Negado · use a fenda da urna",
            _ => $"Negado · {t.Motivo}",
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            ComecarDoFim();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Base ocupada na subida: começa do zero; os eventos guardados ficam limitados
            // pelo difusor.
        }

        using var temporizador = new PeriodicTimer(_intervalo);

        while (await temporizador.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                UmaLeitura();
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // Base ocupada por um instante. A próxima leitura pega o que ficou.
            }
        }
    }

    private static int InnerDe(string deviceId) =>
        deviceId.StartsWith("inner-", StringComparison.Ordinal)
        && int.TryParse(deviceId.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var inner)
            ? inner
            : 0;
}
