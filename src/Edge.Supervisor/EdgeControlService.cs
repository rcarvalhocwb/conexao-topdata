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
public sealed partial class EdgeControlService : EdgeControl.EdgeControlBase
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
    private readonly Access.Infrastructure.SQLite.LeiturasSimuladas? _simulacao;
    private readonly Access.Infrastructure.SQLite.FilaDeComandosSqlite? _comandos;
    private readonly Access.Infrastructure.SQLite.ChavesDosComandos? _chavesDosComandos;
    private readonly Access.Infrastructure.SQLite.ConfiguracoesDasCatracas? _configuracoesDasCatracas;
    private readonly Access.Infrastructure.SQLite.ConfiguracaoPorCatraca? _configuracaoPorCatraca;
    private readonly string? _sessao;

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
    /// <param name="simulacao">Fila de leituras simuladas; presente só no modo simulação.</param>
    /// <param name="comandos">Fila de comandos por catraca; sem ela, "Gerenciar" responde que não há base.</param>
    /// <param name="chavesDosComandos">
    /// Chaves técnicas dos comandos da Etapa A.8 (bip, liberar saída, dois sentidos). Sem elas,
    /// todas contam como desligadas e esses comandos são recusados.
    /// </param>
    /// <param name="configuracoesDasCatracas">
    /// Camada de cada catraca (<c>device_config</c>, Etapa A.3); sem ela, a Parametrização
    /// responde que não há base.
    /// </param>
    /// <param name="configuracaoPorCatraca">
    /// A mesma leitura que o worker usa no "Aplicar agora" (Etapa A.4), para a versão do salvo.
    /// </param>
    /// <param name="sessao">
    /// O identificador desta partida do serviço, que ele passa a cada worker (<c>--sessao</c>).
    /// Com ele, só a situação gravada por um worker desta partida conta: a de outra partida —
    /// um worker órfão de um serviço que morreu sem encerrá-lo, ou a sobra de antes de
    /// alternar o modo simulação — é tratada como "sem notícia", nunca como "Atendendo"
    /// (migração 016; docs/29, defeito de 01/10). Nulo: acredita em toda situação (testes).
    /// </param>
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
        IReadOnlyDictionary<int, string>? nomesDasCatracas = null,
        Access.Infrastructure.SQLite.LeiturasSimuladas? simulacao = null,
        Access.Infrastructure.SQLite.FilaDeComandosSqlite? comandos = null,
        Access.Infrastructure.SQLite.ChavesDosComandos? chavesDosComandos = null,
        Access.Infrastructure.SQLite.ConfiguracoesDasCatracas? configuracoesDasCatracas = null,
        Access.Infrastructure.SQLite.ConfiguracaoPorCatraca? configuracaoPorCatraca = null,
        string? sessao = null)
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
        _simulacao = simulacao;
        _comandos = comandos;
        _chavesDosComandos = chavesDosComandos;
        _configuracoesDasCatracas = configuracoesDasCatracas;
        _configuracaoPorCatraca = configuracaoPorCatraca;
        _sessao = string.IsNullOrWhiteSpace(sessao) ? null : sessao;
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
            // Só as catracas cadastradas nesta instalação: a situação de uma catraca que saiu da
            // configuração continua na base e não pode contar como conectada.
            EquipamentosConectados = _operacao is null
                ? _supervisor.Workers.Where(w => situacoes[w.Nome] is SituacaoDoWorker.Saudavel).Sum(w => w.Inners.Count)
                : _supervisor.Workers.SelectMany(w => w.Inners).Distinct()
                    .Count(inner => catracas.TryGetValue(inner, out var c) && c.EmOperacao),

            // Sem internet é o regime NORMAL de um evento, não uma anomalia.
            // Ver docs/ADR/ADR-0017.
            Nivel = internet ? NivelDeDegradacao.T0Normal : NivelDeDegradacao.T1SemInternet,
            InternetDisponivel = internet,
            SemConfiguracao = _semConfiguracao,
            Simulacao = _simulacao is not null,
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
                    equipamento.RelogioDivergente = c.Situacao.RelogioDivergente;

                    if (c.Situacao.RelogioAcertadoEm is { } acertado)
                    {
                        equipamento.RelogioAcertadoEm = Timestamp.FromDateTimeOffset(acertado);
                    }

                    if (c.Situacao.RelogioConferidoEm is { } conferido)
                    {
                        equipamento.RelogioConferidoEm = Timestamp.FromDateTimeOffset(conferido);
                    }

                    if (c.Situacao.DivergenciaDoRelogioSegundos is { } divergencia)
                    {
                        equipamento.DivergenciaDoRelogioSegundos = divergencia;
                    }

                    if (c.Situacao.UltimoEventoEm is { } evento)
                    {
                        equipamento.UltimoEvento = Timestamp.FromDateTimeOffset(evento);
                    }

                    // Etapa A.5: o que a catraca aceitou, como o worker publicou.
                    if (c.Situacao.ConfiguracaoAplicadaEm is { } aplicada)
                    {
                        equipamento.ConfiguracaoAplicadaEm = Timestamp.FromDateTimeOffset(aplicada);
                    }

                    equipamento.ConfiguracaoVersao = c.Situacao.ConfiguracaoVersao ?? string.Empty;
                    equipamento.Simulacao = c.Situacao.Simulacao == true;
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
        // "Há o que aplicar", apesar do nome: o "Aplicar agora nas catracas" leva a mudança
        // sem reiniciar o serviço; só a espera pelo giro espera o próximo início (docs/32).
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

    public override Task<SimularLeituraResponse> SimularLeitura(SimularLeituraRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_simulacao is null)
        {
            // Sem isto, qualquer um com acesso ao painel poderia "passar" um ingresso numa
            // catraca de verdade.
            return Task.FromResult(new SimularLeituraResponse
            {
                Mensagem = "O modo simulação não está ligado nesta instalação.",
            });
        }

        if (!_supervisor.Workers.Any(w => w.Inners.Contains(request.Inner)))
        {
            return Task.FromResult(new SimularLeituraResponse { Mensagem = $"A catraca {request.Inner} não está cadastrada." });
        }

        if (string.IsNullOrWhiteSpace(request.Codigo) || request.Codigo.Trim().Length > 64)
        {
            return Task.FromResult(new SimularLeituraResponse { Mensagem = "Digite o código a passar (até 64 caracteres)." });
        }

        _simulacao.Pedir(request.Inner, request.Codigo, request.NaUrna, request.Girar, _relogio());
        return Task.FromResult(new SimularLeituraResponse { Aceita = true, Mensagem = "Leitura enviada à catraca simulada." });
    }

    public override Task<EnviarComandoResponse> EnviarComando(EnviarComandoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new EnviarComandoResponse();

        if (_comandos is null)
        {
            resposta.Problemas.Add("O serviço não tem base local configurada.");
            return Task.FromResult(resposta);
        }

        if (Tipo(request.Tipo) is not { } tipo)
        {
            resposta.Problemas.Add("Comando desconhecido.");
            return Task.FromResult(resposta);
        }

        // Comandos da Etapa A.8: cada um com a sua chave técnica, desligada; e os dois sentidos
        // também pela decisão D5. Recusado aqui, nada chega à fila nem à auditoria.
        var recusas = Access.Application.Devices.ComandoDeCatraca.RecusasDoServico(tipo, ChaveLigada(tipo));
        if (recusas.Count > 0)
        {
            resposta.Problemas.AddRange(recusas);
            return Task.FromResult(resposta);
        }

        // Coleta de bilhetes (Etapa A.9): só com a chave técnica ligada. Recusado aqui, nada chega
        // à fila nem à auditoria. Base ocupada ou ilegível conta como desligada: coletar apaga a
        // memória da catraca e não acontece por engano.
        if (tipo is Access.Application.Devices.TipoDeComando.ColetarBilhetes && !ColetaDeBilhetesLigada())
        {
            resposta.Problemas.Add(
                "Coleta de bilhetes desligada nesta instalação: a chave técnica catraca.coletar_bilhetes " +
                "fica desligada até os ensaios de bancada INT-REC-03 e CHAOS-REC-01 (docs/21 §6F).");
            return Task.FromResult(resposta);
        }

        var cadastradas = _supervisor.Workers.SelectMany(w => w.Inners).Distinct().Order().ToList();
        List<int> alvos;

        if (request.Inner == 0 && tipo is Access.Application.Devices.TipoDeComando.AplicarConfiguracao)
        {
            alvos = cadastradas;
        }
        else if (cadastradas.Contains(request.Inner))
        {
            alvos = [request.Inner];
        }
        else
        {
            resposta.Problemas.Add($"A catraca {request.Inner} não está cadastrada.");
            return Task.FromResult(resposta);
        }

        var agora = _relogio();
        var pedidos = new List<Access.Application.Devices.ComandoDeCatraca>();

        foreach (var inner in alvos)
        {
            var (comando, problemas) = Access.Application.Devices.ComandoDeCatraca.Criar(
                inner,
                tipo,
                request.Operador,
                agora,
                request.Texto,
                request.DuracaoSegundos == 0 ? 10 : request.DuracaoSegundos,
                request.Motivo,
                request.Confirmacao);

            if (comando is null)
            {
                resposta.Problemas.AddRange(problemas);
                return Task.FromResult(resposta);
            }

            pedidos.Add(comando);
        }

        foreach (var comando in pedidos)
        {
            _comandos.Pedir(comando);
            resposta.Ids.Add(comando.Id.ToString());
        }

        resposta.Aceito = pedidos.Count > 0;
        if (pedidos.Count == 0)
        {
            resposta.Problemas.Add("Nenhuma catraca cadastrada.");
        }

        return Task.FromResult(resposta);
    }

    public override Task<ListarComandosResponse> ListarComandos(ListarComandosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ListarComandosResponse();

        if (_comandos is null)
        {
            return Task.FromResult(resposta);
        }

        try
        {
            // Pedido que nenhum worker pegou a tempo aparece como expirado, e não como
            // pendente para sempre.
            _comandos.ExpirarVencidos(_relogio());

            foreach (var r in _comandos.Listar(request.Inner == 0 ? null : request.Inner, request.Limite == 0 ? 100 : request.Limite))
            {
                var linha = new ComandoRegistrado
                {
                    Id = r.Comando.Id.ToString(),
                    Inner = r.Comando.Inner,
                    Tipo = Tipo(r.Comando.Tipo),
                    Operador = r.Comando.Operador,
                    Texto = r.Comando.Texto ?? string.Empty,
                    Motivo = r.Comando.Motivo ?? string.Empty,
                    PedidoEm = Timestamp.FromDateTimeOffset(r.Comando.PedidoEm),
                    Situacao = r.Situacao switch
                    {
                        Access.Application.Devices.SituacaoDoComando.Pendente => SituacaoDoComando.Pendente,
                        Access.Application.Devices.SituacaoDoComando.Recebido => SituacaoDoComando.Recebido,
                        Access.Application.Devices.SituacaoDoComando.Concluido => SituacaoDoComando.Concluido,
                        Access.Application.Devices.SituacaoDoComando.Falhou => SituacaoDoComando.Falhou,
                        Access.Application.Devices.SituacaoDoComando.Expirado => SituacaoDoComando.Expirado,
                        _ => SituacaoDoComando.NaoEspecificado,
                    },
                    Resultado = r.Resultado ?? string.Empty,
                };

                if (r.ConcluidoEm is { } concluido)
                {
                    linha.ConcluidoEm = Timestamp.FromDateTimeOffset(concluido);
                }

                resposta.Comandos.Add(linha);
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Base ocupada: a tela tenta de novo.
        }

        return Task.FromResult(resposta);
    }

    // Base ocupada ou ilegível conta como desligada: comando de catraca não liga por engano.
    private bool ChaveLigada(Access.Application.Devices.TipoDeComando tipo)
    {
        if (_chavesDosComandos is null || Access.Application.Devices.ComandoDeCatraca.ChaveTecnica(tipo) is null)
        {
            return false;
        }

        try
        {
            return _chavesDosComandos.Ligada(tipo);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return false;
        }
    }

    private bool ColetaDeBilhetesLigada()
    {
        if (_configuracoes is null)
        {
            return false;
        }

        try
        {
            var (configuracao, ilegiveis) = _configuracoes.Ler();
            return configuracao.ColetarBilhetes
                && !ilegiveis.Contains(Access.Infrastructure.SQLite.ConfiguracoesDaBorda.ChaveColetarBilhetes);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return false;
        }
    }

    private static Access.Application.Devices.TipoDeComando? Tipo(TipoDeComando tipo) => tipo switch
    {
        TipoDeComando.AcertarRelogio => Access.Application.Devices.TipoDeComando.AcertarRelogio,
        TipoDeComando.MensagemTemporaria => Access.Application.Devices.TipoDeComando.MensagemTemporaria,
        TipoDeComando.LiberacaoManual => Access.Application.Devices.TipoDeComando.LiberacaoManual,
        TipoDeComando.ReiniciarConexao => Access.Application.Devices.TipoDeComando.ReiniciarConexao,
        TipoDeComando.AplicarConfiguracao => Access.Application.Devices.TipoDeComando.AplicarConfiguracao,
        TipoDeComando.BipCurto => Access.Application.Devices.TipoDeComando.BipCurto,
        TipoDeComando.BipLongo => Access.Application.Devices.TipoDeComando.BipLongo,
        TipoDeComando.LiberarSaida => Access.Application.Devices.TipoDeComando.LiberarSaida,
        TipoDeComando.LiberarDoisSentidos => Access.Application.Devices.TipoDeComando.LiberarDoisSentidos,
        TipoDeComando.ColetarBilhetes => Access.Application.Devices.TipoDeComando.ColetarBilhetes,
        _ => null,
    };

    private static TipoDeComando Tipo(Access.Application.Devices.TipoDeComando tipo) => tipo switch
    {
        Access.Application.Devices.TipoDeComando.AcertarRelogio => TipoDeComando.AcertarRelogio,
        Access.Application.Devices.TipoDeComando.MensagemTemporaria => TipoDeComando.MensagemTemporaria,
        Access.Application.Devices.TipoDeComando.LiberacaoManual => TipoDeComando.LiberacaoManual,
        Access.Application.Devices.TipoDeComando.ReiniciarConexao => TipoDeComando.ReiniciarConexao,
        Access.Application.Devices.TipoDeComando.AplicarConfiguracao => TipoDeComando.AplicarConfiguracao,
        Access.Application.Devices.TipoDeComando.BipCurto => TipoDeComando.BipCurto,
        Access.Application.Devices.TipoDeComando.BipLongo => TipoDeComando.BipLongo,
        Access.Application.Devices.TipoDeComando.LiberarSaida => TipoDeComando.LiberarSaida,
        Access.Application.Devices.TipoDeComando.LiberarDoisSentidos => TipoDeComando.LiberarDoisSentidos,
        Access.Application.Devices.TipoDeComando.ColetarBilhetes => TipoDeComando.ColetarBilhetes,
        _ => TipoDeComando.NaoEspecificado,
    };

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
            // Situação de outra partida do serviço não é notícia desta: fica de fora, e a catraca
            // aparece como aguardando conectar. É a defesa contra o worker órfão que segue
            // gravando "Polling" fresco depois de o serviço que o subiu morrer (migração 016).
            return _operacao.ListarSituacao()
                .Where(s => _sessao is null || string.Equals(s.Sessao, _sessao, StringComparison.Ordinal))
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
