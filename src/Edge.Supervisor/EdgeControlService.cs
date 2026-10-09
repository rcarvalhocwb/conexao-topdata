using System.Globalization;
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
    private readonly Access.Infrastructure.SQLite.MapasDeGiro? _mapasDeGiro;
    private readonly AnalisadorDaOperacao? _analisador;

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
    /// <param name="mapasDeGiro">
    /// Mapa de giro de cada catraca (migração 017, D9); sem ele, o "Giro desta catraca" responde
    /// que não há base.
    /// </param>
    /// <param name="analisador">
    /// O Analisador da camada inteligente (Etapa I.0 do docs/36), só para o Diagnóstico mostrar a
    /// saúde dele. Nulo: o Diagnóstico diz que ele não existe neste serviço.
    /// </param>
    /// <param name="filaDeSaida">A outbox, para reenviar as cartas mortas a pedido do operador (E5-2).</param>
    /// <param name="estornos">Usos sem passagem e o estorno pelo operador (E1-05).</param>
    /// <param name="usuarios">Usuários do sistema (ADR-0026). Sem eles, o login fica desligado (ferramentas e testes).</param>
    /// <param name="sessoes">Sessões abertas pelo login.</param>
    /// <param name="pessoas">Cadastro local de pessoas (docs/43); nulo sem a chave dos dados pessoais.</param>
    /// <param name="parametrosDoCadastro">Empresas, salas, horários, feriados e perfis do cadastro.</param>
    /// <param name="importacaoDePessoas">Importação de pessoas por planilha; nula sem o cadastro.</param>
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
        string? sessao = null,
        Access.Infrastructure.SQLite.MapasDeGiro? mapasDeGiro = null,
        AnalisadorDaOperacao? analisador = null,
        Access.Infrastructure.SQLite.FilaDeSaidaSqlite? filaDeSaida = null,
        Access.Infrastructure.SQLite.EstornosDeUso? estornos = null,
        Access.Infrastructure.SQLite.UsuariosDoSistema? usuarios = null,
        SessoesDoPainel? sessoes = null,
        Access.Infrastructure.SQLite.CadastroDePessoas? pessoas = null,
        Access.Infrastructure.SQLite.ParametrosDoCadastro? parametrosDoCadastro = null,
        Access.Infrastructure.SQLite.ImportacaoDePessoas? importacaoDePessoas = null)
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
        _mapasDeGiro = mapasDeGiro;
        _analisador = analisador;
        _filaDeSaida = filaDeSaida;
        _estornos = estornos;
        _usuarios = usuarios;
        _sessoes = sessoes ?? new SessoesDoPainel();
        _pessoas = pessoas;
        _parametrosDoCadastro = parametrosDoCadastro;
        _importacaoDePessoas = importacaoDePessoas;
    }

    private readonly Access.Infrastructure.SQLite.FilaDeSaidaSqlite? _filaDeSaida;
    private readonly Access.Infrastructure.SQLite.EstornosDeUso? _estornos;

    public override Task<ListarUsosSemPassagemResponse> ListarUsosSemPassagem(ListarUsosSemPassagemRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ListarUsosSemPassagemResponse { AlcanceDoEstorno = Access.Infrastructure.SQLite.EstornosDeUso.Alcance };

        if (_estornos is null)
        {
            return Task.FromResult(resposta);
        }

        foreach (var uso in _estornos.Listar(_relogio(), request.SomenteComFalha, request.Limite > 0 ? request.Limite : 100))
        {
            resposta.Usos.Add(new UsoSemPassagem
            {
                TentativaId = uso.Tentativa.ToString(),
                Em = Timestamp.FromDateTimeOffset(uso.Em),
                Catraca = uso.Catraca,
                Portao = uso.Portao,
                Provedor = uso.Provedor ?? string.Empty,
                Categoria = uso.Categoria ?? string.Empty,
                CodigoMascarado = uso.Codigo,
                FalhaDaLiberacao = uso.FalhaDaLiberacao ?? string.Empty,
            });
        }

        return Task.FromResult(resposta);
    }

    public override Task<EstornarUsoResponse> EstornarUso(EstornarUsoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new EstornarUsoResponse();

        if (_estornos is null)
        {
            resposta.Problemas.Add("Este serviço não tem base local para estornar.");
            return Task.FromResult(resposta);
        }

        if (!Guid.TryParse(request.TentativaId, out var tentativa))
        {
            resposta.Problemas.Add("Tentativa inválida.");
            return Task.FromResult(resposta);
        }

        var (estornado, problemas) = _estornos.Estornar(tentativa, request.Operador, request.Motivo, _relogio());
        resposta.Estornado = estornado;
        resposta.Problemas.AddRange(problemas);
        return Task.FromResult(resposta);
    }

    public override Task<ReenviarCartasMortasResponse> ReenviarCartasMortas(ReenviarCartasMortasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        var operador = request.Operador?.Trim() ?? string.Empty;
        if (operador.Length is < 2 or > 80)
        {
            return Task.FromResult(new ReenviarCartasMortasResponse
            {
                Mensagem = "Informe o nome de quem está pedindo (2 a 80 caracteres).",
            });
        }

        if (_filaDeSaida is null)
        {
            return Task.FromResult(new ReenviarCartasMortasResponse
            {
                Mensagem = "Este serviço não tem base local para reenviar.",
            });
        }

        try
        {
            var reenviadas = _filaDeSaida.ReenviarCartasMortas(_relogio(), operador);
            return Task.FromResult(new ReenviarCartasMortasResponse
            {
                Aceito = true,
                Reenviadas = reenviadas,
                Mensagem = reenviadas == 0
                    ? "Não havia nada recusado para reenviar."
                    : string.Create(CultureInfo.CurrentCulture, $"{reenviadas} tentativa(s) de volta à fila; sobem na próxima sincronização."),
            });
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return Task.FromResult(new ReenviarCartasMortasResponse
            {
                Mensagem = "A base local está ocupada agora. Tente de novo em alguns segundos.",
            });
        }
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
            resposta.Entradas = resumo.Entradas;
            resposta.Saidas = resumo.Saidas;
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
                    NomeDoGate = NomeDaCatraca(inner),
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

        // Sem intervalo: as últimas 24 h até agora (o painel sempre manda o período). O corte
        // volta explícito na resposta, para o relatório ser reproduzível (docs/16, docs/25 §4).
        var desde = request.Desde?.ToDateTimeOffset() ?? agora.AddDays(-1);
        var ate = request.Ate?.ToDateTimeOffset() ?? agora;
        var contas = _consultas.Contas(desde, ate);
        resposta.PeriodoDesde = Timestamp.FromDateTimeOffset(desde);
        resposta.PeriodoAte = Timestamp.FromDateTimeOffset(ate);

        resposta.Liberados = contas.Liberados;
        resposta.Giros = contas.Giros;
        resposta.Negados = contas.Negados;
        resposta.Entradas = contas.Entradas;
        resposta.Saidas = contas.Saidas;
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
            Entradas = l.Entradas,
            Saidas = l.Saidas,
            Negados = l.Negados,
        }));
        resposta.PorHora.AddRange(contas.PorHora.Select(l => new LinhaPorHora
        {
            Hora = Timestamp.FromDateTimeOffset(l.Hora),
            Liberados = l.Liberados,
            Negados = l.Negados,
        }));
        // Etapa I.2 (docs/36): os principais motivos de negação, com o que fazer diante de cada um e
        // a parte dos negados do período. Texto determinístico: vale com a camada inteligente desligada.
        var semContexto = new Access.Inteligencia.ContextoDaNegativa(agora, 0);
        resposta.Negativas.AddRange(contas.Negativas.Select(n => new LinhaDeNegativa
        {
            Motivo = n.Motivo,
            Mensagem = AcompanhamentoDaOperacao.MensagemPara(new Access.Infrastructure.SQLite.TentativaParaOPainel(
                0, Guid.Empty, string.Empty, string.Empty, agora, false, n.Motivo, null, null, string.Empty, false)),
            Quantidade = n.Quantidade,
            OQueFazer = Access.Inteligencia.PorQueNegou.ExplicarNegativa(n.Motivo, semContexto).OQueFazer,
            PercentualDosNegados = contas.Negados > 0 ? (int)Math.Round(100.0 * n.Quantidade / contas.Negados, MidpointRounding.AwayFromZero) : 0,
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

    /// <summary>
    /// "Por que negou" (Etapa I.2 do docs/36, IN-06): a explicação de uma tentativa, montada sob
    /// pedido pela função pura <see cref="Access.Inteligencia.PorQueNegou"/> com o contexto da base.
    /// Não depende da chave da camada inteligente nem do Analisador. Só leitura.
    /// </summary>
    /// <summary>
    /// O nome que o operador vê para uma catraca: o configurado no assistente; sem nome, "Catraca NN".
    /// É a mesma regra em todas as telas e respostas.
    /// </summary>
    private string NomeDaCatraca(int inner) => NomeDaCatracaPara(_nomes, inner);

    /// <summary>A regra do nome, pura: testável sem o serviço.</summary>
    public static string NomeDaCatracaPara(IReadOnlyDictionary<int, string> nomes, int inner) =>
        nomes.TryGetValue(inner, out var nome) && !string.IsNullOrWhiteSpace(nome)
            ? nome
            : string.Create(CultureInfo.InvariantCulture, $"Catraca {inner:D2}");

    public override Task<ExplicacaoDaNegativa> ExplicarNegativa(ExplicarNegativaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ExplicacaoDaNegativa();

        if (_consultas is null || !Guid.TryParse(request.EventoId, out var id)
            || _consultas.ContextoDaTentativa(id) is not { } tentativa)
        {
            return Task.FromResult(resposta);
        }

        var agora = _relogio();
        var contexto = new Access.Inteligencia.ContextoDaNegativa(
            tentativa.Em,
            InnerDe(tentativa.DeviceId),
            tentativa.UltimoUso is { } uso ? new Access.Inteligencia.UsoAnterior(uso.Em, InnerDe(uso.DeviceId), uso.Girou) : null,
            tentativa.IntervaloDeReusoSegundos > 0 ? TimeSpan.FromSeconds(tentativa.IntervaloDeReusoSegundos) : null,
            _nuvem is { Configurada: true, UltimoSucesso: { } sucesso } ? agora - sucesso : null);

        var explicacao = (tentativa.Liberou, tentativa.Girou) switch
        {
            (false, _) => Access.Inteligencia.PorQueNegou.ExplicarNegativa(tentativa.Motivo, contexto),
            (true, false) => Access.Inteligencia.PorQueNegou.ExplicarLiberadoSemGiro(contexto),
            (true, true) => Access.Inteligencia.PorQueNegou.ExplicarLiberadoComGiro(contexto),
        };

        resposta.Encontrada = true;
        resposta.Negada = !tentativa.Liberou;
        resposta.Inner = contexto.Catraca;
        resposta.NomeDaCatraca = NomeDaCatraca(contexto.Catraca);
        resposta.Em = Timestamp.FromDateTimeOffset(tentativa.Em);
        resposta.OndeFoiLido = Access.Inteligencia.PorQueNegou.OndeFoiLido(tentativa.Origem);
        resposta.OQueAconteceu = explicacao.OQueAconteceu;
        resposta.OQueDizer = explicacao.OQueDizer;
        resposta.OQueFazer = explicacao.OQueFazer;
        return Task.FromResult(resposta);
    }

    /// <summary>
    /// O pacote de diagnóstico para o suporte. Monta no serviço, que lê a pasta de dados; falha vira
    /// mensagem na resposta, nunca erro de comunicação.
    /// </summary>
    public override Task<PacoteDeDiagnostico> ObterPacoteDeDiagnostico(ObterPacoteDeDiagnosticoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var agora = _relogio();

        try
        {
            var linhas = new List<string>();
            var situacoes = _supervisor.Situacoes;
            linhas.Add("Programas das catracas:");
            foreach (var worker in _supervisor.Workers)
            {
                linhas.Add($"  {worker.Nome}: {situacoes[worker.Nome]}, reinícios {_supervisor.Reinicios(worker.Nome)}, {worker.Diagnostico}");
            }

            linhas.Add(string.Empty);
            linhas.Add("Pré-requisitos:");
            foreach (var pre in Edge.Worker.VerificadorDePreRequisitos.Verificar())
            {
                linhas.Add($"  {pre.Id}: {(pre.Atendido is null ? "não conferido" : pre.Atendido is true ? "ok" : "falta")} · {pre.Mensagem}");
            }

            var zip = MontadorDoPacoteDeDiagnostico.Montar(_pastaDeDados, _versao, linhas, agora);
            return Task.FromResult(new PacoteDeDiagnostico
            {
                Gerado = true,
                Zip = Google.Protobuf.ByteString.CopyFrom(zip),
                NomeDoArquivo = string.Create(CultureInfo.InvariantCulture, $"rayzer-xacess-diagnostico-{agora:yyyyMMdd-HHmmss}.zip"),
                Mensagem = "Pacote montado.",
            });
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return Task.FromResult(new PacoteDeDiagnostico
            {
                Gerado = false,
                Mensagem = $"Não foi possível montar o pacote ({erro.GetType().Name}). Os registros podem estar em uso; tente de novo em instantes.",
            });
        }
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

        resposta.Analisador = SaudeDoAnalisadorPara(_analisador?.Situacao);
        return Task.FromResult(resposta);
    }

    /// <summary>A saúde do Analisador como vai ao painel (Etapa I.0). Sem Analisador: desligado, parado.</summary>
    public static SaudeDoAnalisador SaudeDoAnalisadorPara(Access.Inteligencia.SituacaoDoAnalisador? situacao)
    {
        if (situacao is null)
        {
            return new SaudeDoAnalisador();
        }

        var saude = new SaudeDoAnalisador
        {
            Ligado = situacao.Ligada,
            Rodando = situacao.Rodando,
            DuracaoDoUltimoCicloMs = (long)situacao.DuracaoDoUltimoCiclo.TotalMilliseconds,
            Ciclos = situacao.Ciclos,
            Estouros = situacao.Estouros,
            Pulados = situacao.Pulados,
            Falhas = situacao.Falhas,
            UltimoErro = situacao.UltimoErro,
            OrcamentoMs = (long)situacao.Orcamento.TotalMilliseconds,
            TentativasLidas = situacao.TentativasLidas,
        };

        if (situacao.UltimoCicloEm is { } ciclo)
        {
            saude.UltimoCiclo = Timestamp.FromDateTimeOffset(ciclo);
        }

        if (situacao.UltimoErroEm is { } erro)
        {
            saude.UltimoErroEm = Timestamp.FromDateTimeOffset(erro);
        }

        return saude;
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

    /// <summary>
    /// O que as RPCs da camada inteligente ainda não implementada respondem (achado E7-1 do docs/41).
    /// Antes caíam na base gerada e devolviam <c>UNIMPLEMENTED</c>, um erro de transporte; o contrato
    /// descrevia um serviço funcionando.
    /// </summary>
    public const string CamadaDesligada =
        "desligada nesta instalação: a camada inteligente ainda não calcula isto (docs/29; só I.0 e I.2 existem)";

    public override Task<SugestoesDaCatraca> ObterSugestoes(ObterSugestoesRequest request, ServerCallContext context) =>
        Task.FromResult(new SugestoesDaCatraca { Desligada = CamadaDesligada });

    public override Task<RegistrarDestinoDaSugestaoResponse> RegistrarDestinoDaSugestao(
        RegistrarDestinoDaSugestaoRequest request, ServerCallContext context) =>
        Task.FromResult(new RegistrarDestinoDaSugestaoResponse { Registrada = false, Problemas = { CamadaDesligada } });

    public override Task<SaudeDasCatracas> ObterSaudeDasCatracas(ObterSaudeDasCatracasRequest request, ServerCallContext context) =>
        Task.FromResult(new SaudeDasCatracas { Desligada = CamadaDesligada });

    public override Task<DadosDoRitmo> ObterRitmo(ObterRitmoRequest request, ServerCallContext context) =>
        Task.FromResult(new DadosDoRitmo { Desligada = CamadaDesligada });

    public override Task<ListarAlertasResponse> ListarAlertas(ListarAlertasRequest request, ServerCallContext context) =>
        Task.FromResult(new ListarAlertasResponse { Desligada = CamadaDesligada });

    public override Task<MarcarAlertaComoCienteResponse> MarcarAlertaComoCiente(
        MarcarAlertaComoCienteRequest request, ServerCallContext context) =>
        Task.FromResult(new MarcarAlertaComoCienteResponse { Marcado = false, Problemas = { CamadaDesligada } });

    public override Task<RelatorioPosEvento> ObterRelatorioPosEvento(ObterRelatorioPosEventoRequest request, ServerCallContext context) =>
        Task.FromResult(new RelatorioPosEvento { Desligada = CamadaDesligada });
}
