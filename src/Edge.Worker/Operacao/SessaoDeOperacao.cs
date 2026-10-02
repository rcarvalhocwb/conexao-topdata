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
    string? UltimaDecisao,
    DateTimeOffset? RelogioAcertadoEm = null,
    DateTimeOffset? RelogioConferidoEm = null,
    TimeSpan? DivergenciaDoRelogio = null,
    bool RelogioDivergente = false,
    DateTimeOffset? ConfiguracaoAplicadaEm = null,
    string? ConfiguracaoVersao = null);

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
    private readonly Dictionary<string, DeviceSlot> _porDispositivo = new(StringComparer.Ordinal);
    private readonly IFilaDeComandos? _comandos;
    private readonly Func<int, (DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas)>? _recarregarConfiguracao;
    private readonly Func<bool>? _coletaLigada;
    private readonly List<(Guid Id, SituacaoDoComando Situacao, string Resultado, DateTimeOffset Em)> _desfechosAGravar = [];
    private DateTimeOffset _comandosConsultadosEm = DateTimeOffset.MinValue;
    private IReadOnlyList<SituacaoDaCatraca> _publicada = [];
    private DateTimeOffset _publicadaEm = DateTimeOffset.MinValue;
    private readonly ColetorDeTelemetria _coletor;
    private DateTimeOffset _ultimoDescarregamentoDoColetor = DateTimeOffset.MinValue;

    /// <summary>Todas as catracas com a mesma configuração (bancada, testes, quem não lê a base).</summary>
    /// <param name="adapter">Acesso à EasyInner.</param>
    /// <param name="inners">Catracas deste worker.</param>
    /// <param name="configuracao">Configuração enviada a todas as catracas.</param>
    /// <param name="decisor">Quem decide, pela base local.</param>
    /// <param name="registrar">Linha de registro, já mascarada.</param>
    /// <param name="publicar">Recebe a situação das catracas.</param>
    /// <param name="intervaloDePublicacao">
    /// Publica pelo menos neste intervalo, mesmo sem mudança: é o batimento que o serviço
    /// usa para saber que o worker está vivo.
    /// </param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="comandos">Fila de comandos do operador. Sem ela, a catraca só opera.</param>
    /// <param name="recarregarConfiguracao">
    /// Relê a configuração do evento para <see cref="TipoDeComando.AplicarConfiguracao"/>, a
    /// mesma para qualquer catraca. Devolve a configuração nova, ou os problemas que impedem usá-la.
    /// </param>
    /// <param name="acertarRelogioAoDivergir">Ver <see cref="DevicePump"/>. Desligado por padrão.</param>
    /// <param name="sequenciaOficial">
    /// Sequência oficial de conexão (Etapa A.7; ver <see cref="DevicePump"/>). Desligada por padrão.
    /// </param>
    /// <param name="gravadorDeBilhetes">
    /// Onde cada bilhete coletado fica durável (Etapa A.9; ver <see cref="DevicePump"/>). Sem ele,
    /// o comando de coleta falha e nada sai da catraca.
    /// </param>
    /// <param name="coletaLigada">
    /// Lê a chave técnica <c>catraca.coletar_bilhetes</c> quando chega um pedido de coleta. Sem
    /// ela, ou desligada, ou ilegível, o pedido termina <see cref="SituacaoDoComando.Falhou"/> sem
    /// chegar à catraca — a mesma regra que o serviço aplica antes de gravar o pedido.
    /// </param>
    /// <param name="exibirTextoDoGiro">
    /// Chave técnica <c>catraca.exibir_texto_do_giro</c> (ver <see cref="DevicePump"/>). Desligada por padrão.
    /// </param>
    /// <param name="coletor">Coletor mínimo (Etapa I.1). Nulo por padrão.</param>
    public SessaoDeOperacao(
        ITopdataInnerAdapter adapter,
        IEnumerable<int> inners,
        DeviceConfiguration configuracao,
        DecisorDeIngresso decisor,
        Action<string> registrar,
        Action<IReadOnlyList<SituacaoDaCatraca>> publicar,
        TimeSpan? intervaloDePublicacao = null,
        Func<DateTimeOffset>? relogio = null,
        IFilaDeComandos? comandos = null,
        Func<(DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas)>? recarregarConfiguracao = null,
        bool acertarRelogioAoDivergir = false,
        bool sequenciaOficial = false,
        IGravadorDeBilhetes? gravadorDeBilhetes = null,
        Func<bool>? coletaLigada = null,
        bool exibirTextoDoGiro = false,
        ColetorDeTelemetria? coletor = null)
        : this(
            adapter,
            inners,
            MesmaParaTodas(configuracao),
            decisor,
            registrar,
            publicar,
            intervaloDePublicacao,
            relogio,
            comandos,
            recarregarConfiguracao is null ? null : _ => recarregarConfiguracao(),
            acertarRelogioAoDivergir,
            sequenciaOficial,
            gravadorDeBilhetes,
            coletaLigada,
            exibirTextoDoGiro,
            coletor)
    {
    }

    /// <summary>Cada catraca com a sua configuração (Etapa A.4 do docs/35).</summary>
    /// <remarks>
    /// <para>
    /// É como o worker x86 sobe: fábrica → evento → camada da catraca (<c>device_config</c>),
    /// com o recuo para o padrão <b>da catraca</b> já resolvido por quem leu a base
    /// (<see cref="ConfiguracaoComRecuo"/>). Configuração inválida que chegue aqui é erro de
    /// quem chama e derruba a subida, como antes da A.4.
    /// </para>
    /// <para>
    /// O "Aplicar agora" relê só a catraca do comando: cada comando de
    /// <see cref="TipoDeComando.AplicarConfiguracao"/> já é de uma catraca (o "aplicar em todas"
    /// do painel é um comando por catraca, gravado pelo serviço), e uma configuração recusada
    /// numa catraca dá <see cref="SituacaoDoComando.Falhou"/> só naquele comando.
    /// </para>
    /// </remarks>
    /// <param name="adapter">Acesso à EasyInner.</param>
    /// <param name="inners">Catracas deste worker.</param>
    /// <param name="configuracaoDaCatraca">A configuração de cada catraca, pelo número; lida uma vez, na subida.</param>
    /// <param name="decisor">Quem decide, pela base local.</param>
    /// <param name="registrar">Linha de registro, já mascarada.</param>
    /// <param name="publicar">Recebe a situação das catracas.</param>
    /// <param name="intervaloDePublicacao">Ver o outro construtor.</param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="comandos">Fila de comandos do operador. Sem ela, a catraca só opera.</param>
    /// <param name="recarregarConfiguracao">
    /// Relê evento e camada da catraca informada, para <see cref="TipoDeComando.AplicarConfiguracao"/>.
    /// Devolve a configuração nova, ou os problemas que impedem usá-la.
    /// </param>
    /// <param name="acertarRelogioAoDivergir">Ver <see cref="DevicePump"/>. Desligado por padrão.</param>
    /// <param name="sequenciaOficial">
    /// Sequência oficial de conexão (Etapa A.7; ver <see cref="DevicePump"/>). Desligada por padrão.
    /// </param>
    /// <param name="gravadorDeBilhetes">
    /// Onde cada bilhete coletado fica durável (Etapa A.9; ver <see cref="DevicePump"/>). Sem ele,
    /// o comando de coleta falha e nada sai da catraca.
    /// </param>
    /// <param name="coletaLigada">
    /// Lê a chave técnica <c>catraca.coletar_bilhetes</c> quando chega um pedido de coleta. Sem
    /// ela, ou desligada, ou ilegível, o pedido termina <see cref="SituacaoDoComando.Falhou"/> sem
    /// chegar à catraca — a mesma regra que o serviço aplica antes de gravar o pedido.
    /// </param>
    /// <param name="exibirTextoDoGiro">
    /// Chave técnica <c>catraca.exibir_texto_do_giro</c> (ver <see cref="DevicePump"/>). Desligada por padrão.
    /// </param>
    /// <param name="coletor">Coletor mínimo (Etapa I.1). Nulo por padrão.</param>
    public SessaoDeOperacao(
        ITopdataInnerAdapter adapter,
        IEnumerable<int> inners,
        Func<int, DeviceConfiguration> configuracaoDaCatraca,
        DecisorDeIngresso decisor,
        Action<string> registrar,
        Action<IReadOnlyList<SituacaoDaCatraca>> publicar,
        TimeSpan? intervaloDePublicacao = null,
        Func<DateTimeOffset>? relogio = null,
        IFilaDeComandos? comandos = null,
        Func<int, (DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas)>? recarregarConfiguracao = null,
        bool acertarRelogioAoDivergir = false,
        bool sequenciaOficial = false,
        IGravadorDeBilhetes? gravadorDeBilhetes = null,
        Func<bool>? coletaLigada = null,
        bool exibirTextoDoGiro = false,
        ColetorDeTelemetria? coletor = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(inners);
        ArgumentNullException.ThrowIfNull(configuracaoDaCatraca);
        ArgumentNullException.ThrowIfNull(decisor);
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(publicar);

        var catracas = inners.ToList();
        var configuracoes = new List<(int Inner, DeviceConfiguration Configuracao)>(catracas.Count);
        foreach (var inner in catracas)
        {
            var configuracao = configuracaoDaCatraca(inner)
                ?? throw new ArgumentException($"Sem configuração para a catraca {inner}.", nameof(configuracaoDaCatraca));

            var problemas = configuracao.Validar();
            if (problemas.Count > 0)
            {
                throw new ArgumentException(
                    $"Configuração da catraca {inner} inválida: " + string.Join(" ", problemas),
                    nameof(configuracaoDaCatraca));
            }

            configuracoes.Add((inner, configuracao));
        }

        _decisor = decisor;
        _registrar = registrar;
        _publicar = publicar;
        _intervaloDePublicacao = intervaloDePublicacao ?? TimeSpan.FromSeconds(2);
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);

        _comandos = comandos;
        _recarregarConfiguracao = recarregarConfiguracao;
        _coletaLigada = coletaLigada;
        _coletor = coletor ?? ColetorDeTelemetria.Nulo;

        var bomba = new DevicePump(
            adapter,
            _relogio,
            decidir: Decidir,
            aoReceberEvento: Receber,
            acertarRelogioAoDivergir: acertarRelogioAoDivergir,
            aoConcluirComando: Concluir,
            antesDaLiberacaoManual: decisor.DescartarPendente,
            sequenciaOficial: sequenciaOficial,
            gravadorDeBilhetes: gravadorDeBilhetes,
            exibirTextoDoGiro: exibirTextoDoGiro,
            aoDesistirDoGiro: decisor.DescartarPendente);

        _laco = new DeviceGroupLoop(
            adapter,
            configuracoes.Select(c => new DeviceSlot(c.Inner, c.Configuracao, _relogio)),
            new Watchdog(TimeSpan.FromSeconds(30), _relogio),
            bomba);

        foreach (var slot in _laco.Dispositivos)
        {
            _porDispositivo[slot.Maquina.DeviceId] = slot;
        }
    }

    // A configuração única do construtor de antes, validada com a mensagem de antes.
    private static Func<int, DeviceConfiguration> MesmaParaTodas(DeviceConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            throw new ArgumentException(
                "Configuração das catracas inválida: " + string.Join(" ", problemas),
                nameof(configuracao));
        }

        return _ => configuracao;
    }

    /// <summary>As catracas deste worker.</summary>
    public IReadOnlyList<DeviceSlot> Dispositivos => _laco.Dispositivos;

    /// <summary>Abre a porta TCP em que as catracas se conectam.</summary>
    public AdapterResult Iniciar(int porta) => _laco.Iniciar(porta);

    /// <summary>Uma passada por todas as catracas, e a publicação se for a hora.</summary>
    public void UmaVolta()
    {
        BuscarComandosSeFor();

        foreach (var (inner, acao) in _laco.UmaVolta())
        {
            if (!string.Equals(acao, "sem eventos", StringComparison.Ordinal))
            {
                _registrar(string.Create(CultureInfo.InvariantCulture, $"inner-{inner}: {acao}"));
            }
        }

        PublicarSeFor();
        DescarregarColetorSeFor();
    }

    /// <summary>Roda até o cancelamento.</summary>
    /// <param name="cancelamento">Parada.</param>
    /// <param name="aCadaVolta">Chamado entre as voltas; o modo simulação entrega leituras aqui.</param>
    public void Executar(CancellationToken cancelamento, Action? aCadaVolta = null)
    {
        while (!cancelamento.IsCancellationRequested)
        {
            UmaVolta();
            aCadaVolta?.Invoke();
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
            _ultimaDecisao.GetValueOrDefault(d.Maquina.DeviceId),
            d.RelogioAcertadoEm,
            d.RelogioConferidoEm,
            d.DivergenciaDoRelogio,
            d.RelogioDivergente,
            d.ConfiguracaoAplicadaEm,
            d.ConfiguracaoVersao))];

    /// <summary>De quanto em quanto tempo o worker olha a fila de comandos.</summary>
    public static readonly TimeSpan IntervaloDosComandos = TimeSpan.FromMilliseconds(500);

    private void BuscarComandosSeFor()
    {
        if (_comandos is null)
        {
            return;
        }

        var agora = _relogio();
        if (agora - _comandosConsultadosEm < IntervaloDosComandos)
        {
            return;
        }

        _comandosConsultadosEm = agora;

        try
        {
            GravarDesfechos();

            var inners = _laco.Dispositivos.Select(d => d.Inner).ToList();
            foreach (var comando in _comandos.Pendentes(inners))
            {
                if (!_comandos.Receber(comando.Id, agora))
                {
                    continue;
                }

                var catraca = _laco.Dispositivos.First(d => d.Inner == comando.Inner);
                DeviceConfiguration? configuracaoNova = null;

                if (comando.Tipo is TipoDeComando.AplicarConfiguracao && (configuracaoNova = Recarregar(comando, agora)) is null)
                {
                    continue;
                }

                if (comando.Tipo is TipoDeComando.ColetarBilhetes && !ColetaLigada(comando, agora))
                {
                    continue;
                }

                catraca.Enfileirar(comando, configuracaoNova);
                _registrar(string.Create(CultureInfo.InvariantCulture, $"inner-{comando.Inner}: comando {comando.Tipo} recebido"));
            }
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Base ocupada: a catraca não para por causa do painel. Tenta na próxima.
            _registrar($"não foi possível ler os comandos: {erro.GetType().Name}");
        }
    }

    private DeviceConfiguration? Recarregar(ComandoDeCatraca comando, DateTimeOffset agora)
    {
        // Só a catraca do comando é relida (Etapa A.4): as outras não são tocadas.
        var (configuracao, problemas) = _recarregarConfiguracao?.Invoke(comando.Inner)
            ?? (null, ["este worker não sabe reler a configuração"]);

        var invalida = configuracao?.Validar() ?? [];
        if (configuracao is null || problemas.Count > 0 || invalida.Count > 0)
        {
            // A catraca segue com a configuração que já tinha, sem sair de Polling: o comando
            // nem chega à fila dela. Os problemas vão para o histórico do comando.
            _registrar(string.Create(
                CultureInfo.InvariantCulture,
                $"inner-{comando.Inner}: configuração não aplicada ({problemas.Count + invalida.Count} problema(s)); segue com a que tinha"));
            _desfechosAGravar.Add((comando.Id, SituacaoDoComando.Falhou,
                "configuração não aplicada: " + string.Join(" ", problemas.Concat(invalida)), agora));
            GravarDesfechos();
            return null;
        }

        return configuracao;
    }

    // A chave lida a cada pedido (Etapa A.9): ligar ou desligar não exige reiniciar o worker.
    // Base ilegível conta como desligada — coletar apaga a memória da catraca, e não acontece
    // por engano.
    private bool ColetaLigada(ComandoDeCatraca comando, DateTimeOffset agora)
    {
        bool ligada;
        try
        {
            ligada = _coletaLigada?.Invoke() ?? false;
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            _registrar($"inner-{comando.Inner}: chave da coleta de bilhetes ilegível ({erro.GetType().Name}); tratada como desligada");
            ligada = false;
        }

        if (ligada)
        {
            return true;
        }

        _registrar(string.Create(CultureInfo.InvariantCulture, $"inner-{comando.Inner}: coleta de bilhetes recusada: chave técnica desligada"));
        _desfechosAGravar.Add((comando.Id, SituacaoDoComando.Falhou,
            "coleta de bilhetes desligada nesta instalação: a chave técnica catraca.coletar_bilhetes fica desligada até INT-REC-03 e CHAOS-REC-01 (docs/21 §6F); nada foi coletado", agora));
        GravarDesfechos();
        return false;
    }

    private void Concluir(ComandoDeCatraca comando, SituacaoDoComando situacao, string resultado)
    {
        _registrar(string.Create(CultureInfo.InvariantCulture, $"inner-{comando.Inner}: comando {comando.Tipo} {situacao}: {resultado}"));
        _desfechosAGravar.Add((comando.Id, situacao, resultado, _relogio()));
        GravarDesfechos();
    }

    // O desfecho não se perde se a base estiver ocupada: fica guardado e vai na próxima.
    private void GravarDesfechos()
    {
        if (_comandos is null)
        {
            _desfechosAGravar.Clear();
            return;
        }

        while (_desfechosAGravar.Count > 0)
        {
            var (id, situacao, resultado, em) = _desfechosAGravar[0];
            try
            {
                _comandos.Concluir(id, situacao, resultado, em);
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                _registrar($"desfecho de comando não gravado ainda: {erro.GetType().Name}");
                return;
            }

            _desfechosAGravar.RemoveAt(0);
        }
    }

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

    /// <summary>Descarrega o coletor de telemetria a cada 2 s (entre voltas, no mesmo try/catch da publicação).</summary>
    private void DescarregarColetorSeFor()
    {
        var agora = _relogio();
        if (agora - _ultimoDescarregamentoDoColetor < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _ultimoDescarregamentoDoColetor = agora;

        try
        {
            // "worker-1" é um placeholder; em produção, será algo como "x86-1" ou "x86-5"
            var (desc, disc) = _coletor.Descarregar(sessionId: null, worker: "worker-1");
            if (disc > 0)
            {
                _registrar($"coletor: {desc} sinais gravados, {disc} descartados (anel cheio)");
            }
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            // Falha de telemetria: não afeta a operação (Invariante I3).
            // Invariante I1: nunca deixa uma catraca presa.
            _registrar($"não foi possível descarregar telemetria: {erro.GetType().Name}");
        }
    }

    private Decision Decidir(DeviceEvent evento)
    {
        // A regra do mapa de giro (D9) para a origem desta leitura, tirada da mesma
        // configuração que o laço usa para escolher a função de liberação: o que a tentativa
        // grava como "conta como" é o que a catraca vai fazer.
        var giro = _porDispositivo.TryGetValue(evento.Key.DeviceId, out var slot)
            && MapaDeGiro.DaLeitura(evento.Origin) is { } origem
                ? slot.Configuracao.PerfilFisico.Resolver(origem)
                : null;

        var decisao = _decisor.Decidir(evento, giro);
        _ultimaDecisao[evento.Key.DeviceId] = decisao.ShouldRelease ? "liberado" : "negado";

        // Telemetria: latência da decisão (G-03)
        if (_porDispositivo.TryGetValue(evento.Key.DeviceId, out var s))
        {
            _coletor.RegistrarLatenciaDecisao(s.Inner, (long)decisao.Elapsed.TotalMilliseconds);
            _coletor.ContarDecisao(s.Inner);
        }

        _registrar(string.Create(
            CultureInfo.InvariantCulture,
            $"{evento.Key.DeviceId}: {(decisao.ShouldRelease ? "LIBERADO" : "NEGADO")} {decisao.Reason} em {decisao.Elapsed.TotalMilliseconds:F0} ms"));

        return decisao;
    }

    private void Receber(DeviceEvent evento)
    {
        var codigo = evento.RawCardData is null ? string.Empty : $" {CredentialValue.Mascarar(evento.RawCardData)}";
        _registrar($"{evento.Key.DeviceId}: origem {evento.Origin}{codigo}");

        // Telemetria: origem (G-01 ou G-02)
        if (_porDispositivo.TryGetValue(evento.Key.DeviceId, out var slot))
        {
            var origemRaw = evento.Origin.Raw;
            if (origemRaw >= 0 && origemRaw <= 255)
            {
                _coletor.Enfileirar(new SinalDaOperacao(
                    Guid.NewGuid().ToString(),
                    slot.Inner,
                    TipoDeSinal.Origem,
                    OrigemBruta: origemRaw,
                    RecebidoEm: _relogio()));
            }
            else
            {
                _coletor.ContarOrigemDesconhecida(slot.Inner);
            }
        }

        _decisor.AoReceberEvento(evento);
    }
}
