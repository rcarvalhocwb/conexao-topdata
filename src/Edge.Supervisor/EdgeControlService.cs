using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>
/// Implementa o contrato IPC a partir do estado do supervisor.
/// </summary>
/// <remarks>
/// <para>
/// A interface gráfica conversa só com isto. Ela nunca carrega a DLL, nunca abre o
/// banco e nunca fala com um equipamento — o que a mantém viva quando um worker morre.
/// Ver docs/ADR/ADR-0001 e ADR-0004.
/// </para>
/// <para>
/// Nenhum método devolve credencial em texto claro: o próprio contrato não tem campo
/// para isso.
/// </para>
/// </remarks>
public sealed class EdgeControlService : EdgeControl.EdgeControlBase
{
    /// <summary>
    /// Sem notícia do worker há mais que isto, a catraca deixa de contar como em operação.
    /// O worker publica a cada 2 s; a folga cobre uma base ocupada por alguns segundos.
    /// </summary>
    public static readonly TimeSpan NoticiaVelha = TimeSpan.FromSeconds(15);

    private readonly WorkerSupervisor _supervisor;
    private readonly Func<DateTimeOffset> _relogio;
    private readonly string _versao;
    private readonly Access.Infrastructure.SQLite.Operacao? _operacao;
    private readonly EstadoDaNuvem? _nuvem;
    private readonly Access.Infrastructure.SQLite.ConsultasDaOperacao? _consultas;
    private readonly Access.Infrastructure.SQLite.ConfiguracoesDaBorda? _configuracoes;
    private readonly string _pastaDeDados;
    private readonly bool _semConfiguracao;
    private readonly IReadOnlyDictionary<int, string> _nomes;

    /// <param name="supervisor">Os workers.</param>
    /// <param name="versao">Versão exibida no painel.</param>
    /// <param name="relogio">Relógio.</param>
    /// <param name="operacao">
    /// Base local da operação (ADR-0024). Sem ela, o serviço só sabe dos processos, não
    /// das catracas.
    /// </param>
    /// <param name="nuvem">Situação da sincronização com a nuvem.</param>
    /// <param name="consultas">Consultas das telas; sem elas, as telas respondem vazio.</param>
    /// <param name="configuracoes">Configuração do evento.</param>
    /// <param name="pastaDeDados">Onde ficam base, registros e segredos, para o diagnóstico.</param>
    /// <param name="semConfiguracao">O serviço subiu sem arquivo de configuração.</param>
    /// <param name="nomesDasCatracas">Nome de cada catraca no painel.</param>
    public EdgeControlService(
        WorkerSupervisor supervisor,
        string? versao = null,
        Func<DateTimeOffset>? relogio = null,
        Access.Infrastructure.SQLite.Operacao? operacao = null,
        EstadoDaNuvem? nuvem = null,
        Access.Infrastructure.SQLite.ConsultasDaOperacao? consultas = null,
        Access.Infrastructure.SQLite.ConfiguracoesDaBorda? configuracoes = null,
        string? pastaDeDados = null,
        bool semConfiguracao = false,
        IReadOnlyDictionary<int, string>? nomesDasCatracas = null)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervisor = supervisor;
        _versao = versao ?? "0.1.0-fase1";
        _relogio = relogio ?? (() => DateTimeOffset.UtcNow);
        _operacao = operacao;
        _nuvem = nuvem;
        _consultas = consultas;
        _configuracoes = configuracoes;
        _pastaDeDados = pastaDeDados ?? string.Empty;
        _semConfiguracao = semConfiguracao;
        _nomes = nomesDasCatracas ?? new Dictionary<int, string>();
    }

    /// <summary>Por onde os acessos chegam aos painéis conectados.</summary>
    public DifusorDeEventos Eventos { get; } = new();

    public override Task<ObterEstadoResponse> ObterEstado(ObterEstadoRequest request, ServerCallContext context)
    {
        var situacoes = _supervisor.Situacoes;
        var agora = _relogio();
        var catracas = CatracasPorInner(agora);
        var ultimaSincronizacao = _nuvem?.UltimoSucesso;
        var internet = ultimaSincronizacao is { } s && agora - s < EstadoDaNuvem.ConsideradaFora;

        var resposta = new ObterEstadoResponse
        {
            Versao = _versao,
            WorkersAtivos = situacoes.Count(x => x.Value is SituacaoDoWorker.Saudavel),
            EquipamentosCadastrados = _supervisor.Workers.Sum(w => w.Inners.Count),
            EquipamentosConectados = _operacao is null
                ? _supervisor.Workers.Where(w => situacoes[w.Nome] is SituacaoDoWorker.Saudavel).Sum(w => w.Inners.Count)
                : catracas.Values.Count(c => c.EmOperacao),

            // Sem internet é o regime NORMAL de um evento, não uma anomalia.
            // Ver docs/ADR/ADR-0017.
            Nivel = internet ? NivelDeDegradacao.T0Normal : NivelDeDegradacao.T1SemInternet,
            InternetDisponivel = internet,
            SemConfiguracao = _semConfiguracao,
        };

        if (ultimaSincronizacao is { } quando)
        {
            resposta.UltimaSincronizacao = Timestamp.FromDateTimeOffset(quando);
        }

        if (_operacao is not null)
        {
            var resumo = _operacao.Resumir(agora);
            resposta.Liberados = resumo.Liberados;
            resposta.Negados = resumo.Negados;
            resposta.Giros = resumo.Giros;
            resposta.LiberadosUltimos5Minutos = resumo.LiberadosNosUltimos5Minutos;
            resposta.CartasMortas = resumo.CartasMortas;
            resposta.OutboxPendente = resumo.PendentesDeEnvio;
            resposta.OutboxIdadeMaisAntigoSegundos = resumo.PendenteMaisAntigo is { } antigo
                ? (long)Math.Max(0, (agora - antigo).TotalSeconds)
                : 0;
        }

        return Task.FromResult(resposta);
    }

    public override Task<ListarEquipamentosResponse> ListarEquipamentos(
        ListarEquipamentosRequest request,
        ServerCallContext context)
    {
        var resposta = new ListarEquipamentosResponse();
        var situacoes = _supervisor.Situacoes;
        var agora = _relogio();
        var catracas = CatracasPorInner(agora);

        foreach (var worker in _supervisor.Workers)
        {
            var workerSaudavel = situacoes[worker.Nome] is SituacaoDoWorker.Saudavel;

            foreach (var inner in worker.Inners)
            {
                var equipamento = new Equipamento
                {
                    Inner = inner,
                    NomeDoGate = _nomes.TryGetValue(inner, out var nome) ? nome : $"{worker.Nome}/{inner}",
                    Worker = worker.Nome,
                    Porta = worker.Porta,
                    Estado = situacoes[worker.Nome].ToString(),
                    Saudavel = workerSaudavel,
                    UltimoEvento = Timestamp.FromDateTimeOffset(agora),
                    Firmware = string.Empty,
                    Homologado = false,
                };

                if (catracas.TryGetValue(inner, out var c))
                {
                    equipamento.Estado = c.Velha ? $"sem notícia do worker ({c.Situacao.Estado})" : c.Situacao.Estado;
                    equipamento.EmOperacao = c.EmOperacao;
                    equipamento.Saudavel = workerSaudavel && c.EmOperacao;
                    equipamento.Firmware = c.Situacao.Firmware ?? string.Empty;
                    equipamento.TentativasDeReconexao = c.Situacao.TentativasDeReconexao;
                    equipamento.UltimaDecisao = c.Situacao.UltimaDecisao ?? string.Empty;
                    equipamento.NoticiaEm = Timestamp.FromDateTimeOffset(c.Situacao.AtualizadoEm);

                    if (c.Situacao.UltimoEventoEm is { } evento)
                    {
                        equipamento.UltimoEvento = Timestamp.FromDateTimeOffset(evento);
                    }
                }
                else if (_operacao is not null)
                {
                    equipamento.Estado = workerSaudavel ? "aguardando a catraca" : situacoes[worker.Nome].ToString();
                    equipamento.Saudavel = false;
                }

                resposta.Equipamentos.Add(equipamento);
            }
        }

        return Task.FromResult(resposta);
    }

    public override async Task AcompanharEventos(
        AcompanharEventosRequest request,
        IServerStreamWriter<EventoDeAcesso> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        var filtro = request.Inners.ToHashSet();
        using var assinatura = Eventos.Assinar();

        await foreach (var evento in assinatura.Leitor.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        {
            if (filtro.Count > 0 && !filtro.Contains(evento.Inner))
            {
                continue;
            }

            await responseStream.WriteAsync(evento, context.CancellationToken).ConfigureAwait(false);
        }
    }

    public override Task<ListarAcessosResponse> ListarAcessos(ListarAcessosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ListarAcessosResponse();

        if (_consultas is null)
        {
            return Task.FromResult(resposta);
        }

        var filtro = new Access.Infrastructure.SQLite.FiltroDeTentativas(
            Inner: request.Inner > 0 ? request.Inner : null,
            Liberados: request.Resultado switch
            {
                FiltroDeResultado.Liberados => true,
                FiltroDeResultado.Negados => false,
                _ => null,
            },
            Desde: request.Desde?.ToDateTimeOffset(),
            Ate: request.Ate?.ToDateTimeOffset(),
            Categoria: string.IsNullOrWhiteSpace(request.Categoria) ? null : request.Categoria,
            Limite: request.Limite > 0 ? request.Limite : 200);

        var (tentativas, haMais) = _consultas.ListarTentativas(filtro);
        resposta.Acessos.AddRange(tentativas.Select(AcompanhamentoDaOperacao.Converter));
        resposta.HaMais = haMais;
        return Task.FromResult(resposta);
    }

    public override Task<ConfiguracaoDoEvento> ObterConfiguracao(ObterConfiguracaoRequest request, ServerCallContext context)
    {
        var configuracao = _configuracoes?.Ler().Configuracao ?? new Access.Infrastructure.SQLite.ConfiguracaoDaOperacao();
        return Task.FromResult(Converter(configuracao));
    }

    public override Task<GravarConfiguracaoResponse> GravarConfiguracao(GravarConfiguracaoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new GravarConfiguracaoResponse();

        if (_configuracoes is null || request.Configuracao is null)
        {
            resposta.Problemas.Add("O serviço não tem base local configurada.");
            return Task.FromResult(resposta);
        }

        var atual = _configuracoes.Ler().Configuracao;
        var pedida = request.Configuracao;

        if (pedida.TipoDeLeitor is < 0 or > 255 || pedida.TempoDeAcionamentoSegundos is < 0 or > 255)
        {
            resposta.Problemas.Add("Valores fora do que a catraca aceita.");
            return Task.FromResult(resposta);
        }

        // A nuvem é da instalação, não do operador: o conector do espelho não muda aqui.
        var nova = atual with
        {
            TipoDeLeitor = (byte)pedida.TipoDeLeitor,
            LeitorDaUrna = pedida.LeitorDaUrna,
            TempoDeAcionamento = (byte)pedida.TempoDeAcionamentoSegundos,
            MensagemPadrao = pedida.MensagemPadrao ?? string.Empty,
            EsperaPeloGiroSegundos = pedida.EsperaPeloGiroSegundos > 0 ? pedida.EsperaPeloGiroSegundos : atual.EsperaPeloGiroSegundos,
        };

        var problemas = nova.Validar();
        if (problemas.Count > 0)
        {
            resposta.Problemas.AddRange(problemas);
            return Task.FromResult(resposta);
        }

        _configuracoes.Gravar(nova, _relogio(), string.IsNullOrWhiteSpace(request.Operador) ? null : request.Operador);
        resposta.Gravada = true;
        resposta.ExigeReinicio = nova != atual;
        return Task.FromResult(resposta);
    }

    public override Task<SituacaoDaSincronizacao> ObterSincronizacao(ObterSincronizacaoRequest request, ServerCallContext context)
    {
        var resposta = new SituacaoDaSincronizacao
        {
            Configurada = _nuvem?.Configurada ?? false,
            UltimaFalha = _nuvem?.UltimaFalha ?? string.Empty,
        };

        if (_nuvem?.UltimoSucesso is { } sucesso)
        {
            resposta.UltimoSucesso = Timestamp.FromDateTimeOffset(sucesso);
        }

        if (_operacao is not null)
        {
            var agora = _relogio();
            var resumo = _operacao.Resumir(agora);
            resposta.Pendentes = resumo.PendentesDeEnvio;
            resposta.CartasMortas = resumo.CartasMortas;
            resposta.IdadeDoMaisAntigoSegundos = resumo.PendenteMaisAntigo is { } antigo
                ? (long)Math.Max(0, (agora - antigo).TotalSeconds)
                : 0;
        }

        if (_consultas is not null)
        {
            resposta.Provedores.AddRange(_consultas.Provedores().Select(p => new ProvedorCadastrado
            {
                Id = p.Id,
                Nome = p.Nome,
                Codigos = p.Codigos,
                Reutilizavel = p.Reutilizavel,
                IntervaloDeReusoSegundos = p.IntervaloDeReusoSegundos,
                SomenteNaUrna = p.SomenteNaUrna,
                Habilitado = p.Habilitado,
            }));
        }

        return Task.FromResult(resposta);
    }

    public override Task<PrestacaoDeContas> ObterPrestacaoDeContas(ObterPrestacaoDeContasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var agora = _relogio();
        var resposta = new PrestacaoDeContas { GeradaEm = Timestamp.FromDateTimeOffset(agora) };

        if (_consultas is null)
        {
            return Task.FromResult(resposta);
        }

        // Sem intervalo: o dia inteiro até agora. O corte é sempre explícito na resposta,
        // para o relatório ser reproduzível (docs/16).
        var desde = request.Desde?.ToDateTimeOffset() ?? agora.AddDays(-1);
        var ate = request.Ate?.ToDateTimeOffset() ?? agora;
        var contas = _consultas.Contas(desde, ate);

        resposta.Liberados = contas.Liberados;
        resposta.Giros = contas.Giros;
        resposta.Negados = contas.Negados;
        resposta.PorCategoria.AddRange(contas.PorCategoria.Select(l => new LinhaPorCategoria
        {
            Categoria = string.IsNullOrEmpty(l.Chave) ? "(sem categoria)" : l.Chave,
            Liberados = l.Liberados,
            Giros = l.Giros,
        }));
        resposta.PorCatraca.AddRange(contas.PorCatraca.Select(l => new LinhaPorCatraca
        {
            Inner = InnerDe(l.Chave),
            Liberados = l.Liberados,
            Giros = l.Giros,
            Negados = l.Negados,
        }));
        resposta.PorHora.AddRange(contas.PorHora.Select(l => new LinhaPorHora
        {
            Hora = Timestamp.FromDateTimeOffset(l.Hora),
            Liberados = l.Liberados,
            Negados = l.Negados,
        }));
        resposta.Negativas.AddRange(contas.Negativas.Select(n => new LinhaDeNegativa
        {
            Motivo = n.Motivo,
            Mensagem = AcompanhamentoDaOperacao.MensagemPara(new Access.Infrastructure.SQLite.TentativaParaOPainel(
                0, Guid.Empty, string.Empty, string.Empty, agora, false, n.Motivo, null, null, string.Empty, false)),
            Quantidade = n.Quantidade,
        }));

        return Task.FromResult(resposta);
    }

    public override Task<ConsultarCodigoResponse> ConsultarCodigo(ConsultarCodigoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ConsultarCodigoResponse();

        if (_consultas is null || string.IsNullOrWhiteSpace(request.Codigo))
        {
            return Task.FromResult(resposta);
        }

        var situacao = _consultas.ConsultarCodigo(request.Codigo);
        if (situacao is null)
        {
            // Nem a máscara do que foi digitado volta: o operador sabe o que digitou.
            return Task.FromResult(resposta);
        }

        resposta.Encontrado = true;
        resposta.CodigoMascarado = situacao.CodigoMascarado;
        resposta.Provedor = situacao.Provedor;
        resposta.Categoria = situacao.Categoria ?? string.Empty;
        resposta.Situacao = situacao.Situacao;
        resposta.UsosFeitos = situacao.UsosFeitos;
        resposta.UsosMaximos = situacao.UsosMaximos ?? 0;

        if (situacao.UltimoUso is { } ultimo)
        {
            resposta.UltimoUso = Timestamp.FromDateTimeOffset(ultimo);
        }

        resposta.Historico.AddRange(situacao.Historico.Select(AcompanhamentoDaOperacao.Converter));
        return Task.FromResult(resposta);
    }

    public override Task<Diagnostico> ObterDiagnostico(ObterDiagnosticoRequest request, ServerCallContext context)
    {
        var resposta = new Diagnostico { Versao = _versao, PastaDeDados = _pastaDeDados };
        var situacoes = _supervisor.Situacoes;

        foreach (var worker in _supervisor.Workers)
        {
            var item = new DiagnosticoDeWorker
            {
                Nome = worker.Nome,
                Situacao = situacoes[worker.Nome].ToString(),
                Detalhe = worker.Diagnostico,
                Reinicios = _supervisor.Reinicios(worker.Nome),
            };

            if (worker is ProcessoDeWorker processo)
            {
                item.UltimasLinhas.AddRange(processo.UltimasLinhas());
            }

            resposta.Workers.Add(item);
        }

        return Task.FromResult(resposta);
    }

    private static ConfiguracaoDoEvento Converter(Access.Infrastructure.SQLite.ConfiguracaoDaOperacao c) => new()
    {
        TipoDeLeitor = c.TipoDeLeitor,
        LeitorDaUrna = c.LeitorDaUrna,
        TempoDeAcionamentoSegundos = c.TempoDeAcionamento,
        MensagemPadrao = c.MensagemPadrao,
        NuvemLigada = c.EspelhoLigado,
        EsperaPeloGiroSegundos = c.EsperaPeloGiroSegundos,
    };

    private static int InnerDe(string deviceId) =>
        deviceId.StartsWith("inner-", StringComparison.Ordinal)
        && int.TryParse(deviceId.AsSpan(6), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var inner)
            ? inner
            : 0;

    private sealed record Catraca(Access.Infrastructure.SQLite.SituacaoDoEquipamento Situacao, bool Velha)
    {
        public bool EmOperacao => Situacao.Online && !Velha;
    }

    private Dictionary<int, Catraca> CatracasPorInner(DateTimeOffset agora)
    {
        if (_operacao is null)
        {
            return [];
        }

        try
        {
            return _operacao.ListarSituacao()
                .GroupBy(s => s.Inner)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var s = g.OrderByDescending(x => x.AtualizadoEm).First();
                        return new Catraca(s, agora - s.AtualizadoEm > NoticiaVelha);
                    });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Base ocupada: o painel mostra o que sabe dos processos e tenta de novo.
            return [];
        }
    }
}
