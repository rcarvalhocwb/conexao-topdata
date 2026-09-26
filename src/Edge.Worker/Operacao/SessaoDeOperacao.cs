using System.Globalization;
using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Domain.Access;
using Access.Domain.Credentials;
using Access.Domain.Devices;

namespace Edge.Worker.Operacao;

/// <summary>A situação de uma catraca, para quem está fora do worker.</summary>
public sealed record SituacaoDaCatraca(
    int Inner,
    string DeviceId,
    DeviceState Estado,
    bool EmOperacao,
    string? Firmware,
    int TentativasDeReconexao,
    DateTimeOffset? UltimoEventoEm,
    string? UltimaDecisao);

/// <summary>
/// A operação de verdade: o mesmo laço da bancada, sem tela, publicando a situação de cada
/// catraca e registrando o que acontece sem nunca escrever um código inteiro.
/// </summary>
/// <remarks>
/// A diferença para <c>SessaoDeBancada</c> é só o que sai: lá o código lido aparece
/// inteiro, porque o ensaio existe para vê-lo, com cartões de teste. Aqui é cartão de
/// cliente, e sai mascarado.
/// </remarks>
public sealed class SessaoDeOperacao
{
    /// <summary>Estados em que a catraca está atendendo público.</summary>
    public static readonly IReadOnlySet<DeviceState> EstadosEmOperacao = new HashSet<DeviceState>
    {
        DeviceState.Polling,
        DeviceState.ValidarAcesso,
        DeviceState.EnviarMsgAcessoNegado,
        DeviceState.LiberarCatraca,
        DeviceState.MonitoraGiroCatraca,
        DeviceState.ColetarBilhetes,
    };

    private readonly DeviceGroupLoop _laco;
    private readonly DecisorDeIngresso _decisor;
    private readonly Action<string> _registrar;
    private readonly Action<IReadOnlyList<SituacaoDaCatraca>> _publicar;
    private readonly TimeSpan _intervaloDePublicacao;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly Dictionary<string, string> _ultimaDecisao = new(StringComparer.Ordinal);
    private IReadOnlyList<SituacaoDaCatraca> _publicada = [];
    private DateTimeOffset _publicadaEm = DateTimeOffset.MinValue;

    /// <param name="adapter">Acesso à EasyInner.</param>
    /// <param name="inners">Catracas deste worker.</param>
    /// <param name="configuracao">Configuração enviada às catracas.</param>
    /// <param name="decisor">Quem decide, pela base local.</param>
    /// <param name="registrar">Linha de registro, já mascarada.</param>
    /// <param name="publicar">Recebe a situação das catracas.</param>
    /// <param name="intervaloDePublicacao">
    /// Publica pelo menos neste intervalo, mesmo sem mudança: é o batimento que o serviço
    /// usa para saber que o worker está vivo.
    /// </param>
    /// <param name="relogio">Relógio.</param>
    public SessaoDeOperacao(
        ITopdataInnerAdapter adapter,
        IEnumerable<int> inners,
        DeviceConfiguration configuracao,
        DecisorDeIngresso decisor,
        Action<string> registrar,
        Action<IReadOnlyList<SituacaoDaCatraca>> publicar,
        TimeSpan? intervaloDePublicacao = null,
        Func<DateTimeOffset>? relogio = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(inners);
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(decisor);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(publicar);

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            throw new ArgumentException(
                "Configuração das catracas inválida: " + string.Join(" ", problemas),
                nameof(configuracao));
        }

        _decisor = decisor;
        _registrar = registrar;
        _publicar = publicar;
        _intervaloDePublicacao = intervaloDePublicacao ?? TimeSpan.FromSeconds(2);
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);

        var bomba = new DevicePump(adapter, _relogio, decidir: Decidir, aoReceberEvento: Receber);

        _laco = new DeviceGroupLoop(
            adapter,
            inners.Select(i => new DeviceSlot(i, configuracao, _relogio)),
            new Watchdog(TimeSpan.FromSeconds(30), _relogio),
            bomba);
    }

    /// <summary>As catracas deste worker.</summary>
    public IReadOnlyList<DeviceSlot> Dispositivos => _laco.Dispositivos;

    /// <summary>Abre a porta TCP em que as catracas se conectam.</summary>
    public AdapterResult Iniciar(int porta) => _laco.Iniciar(porta);

    /// <summary>Uma passada por todas as catracas, e a publicação se for a hora.</summary>
    public void UmaVolta()
    {
        foreach (var (inner, acao) in _laco.UmaVolta())
        {
            if (!string.Equals(acao, "sem eventos", StringComparison.Ordinal))
            {
                _registrar(string.Create(CultureInfo.InvariantCulture, $"inner-{inner}: {acao}"));
            }
        }

        PublicarSeFor();
    }

    /// <summary>Roda até o cancelamento.</summary>
    public void Executar(CancellationToken cancelamento)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            UmaVolta();
        }
    }

    /// <summary>Fecha a porta.</summary>
    public AdapterResult Encerrar() => _laco.Encerrar();

    /// <summary>A situação atual de todas as catracas.</summary>
    public IReadOnlyList<SituacaoDaCatraca> Situacao() =>
        [.. _laco.Dispositivos.Select(d => new SituacaoDaCatraca(
            d.Inner,
            d.Maquina.DeviceId,
            d.Maquina.Current,
            EstadosEmOperacao.Contains(d.Maquina.Current),
            d.Firmware?.Versao,
            d.TentativasDeReconexao,
            d.UltimoEvento?.ReceivedTime,
            _ultimaDecisao.GetValueOrDefault(d.Maquina.DeviceId)))];

    private void PublicarSeFor()
    {
        var agora = _relogio();
        var atual = Situacao();

        // Publica quando algo mudou, e no máximo a cada intervalo mesmo sem mudança.
        if (!atual.SequenceEqual(_publicada) || agora - _publicadaEm >= _intervaloDePublicacao)
        {
            try
            {
                _publicar(atual);
                _publicada = atual;
                _publicadaEm = agora;
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                // Base local ocupada: a catraca não pode parar por causa do painel. Tenta
                // de novo na próxima volta.
                _registrar($"não foi possível publicar a situação: {erro.GetType().Name}");
            }
        }
    }

    private Decision Decidir(DeviceEvent evento)
    {
        var decisao = _decisor.Decidir(evento);
        _ultimaDecisao[evento.Key.DeviceId] = decisao.ShouldRelease ? "liberado" : "negado";

        _registrar(string.Create(
            CultureInfo.InvariantCulture,
            $"{evento.Key.DeviceId}: {(decisao.ShouldRelease ? "LIBERADO" : "NEGADO")} {decisao.Reason} em {decisao.Elapsed.TotalMilliseconds:F0} ms"));

        return decisao;
    }

    private void Receber(DeviceEvent evento)
    {
        var codigo = evento.RawCardData is null ? string.Empty : $" {CredentialValue.Mascarar(evento.RawCardData)}";
        _registrar($"{evento.Key.DeviceId}: origem {evento.Origin}{codigo}");
        _decisor.AoReceberEvento(evento);
    }
}
