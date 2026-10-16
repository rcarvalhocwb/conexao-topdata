using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>
/// As seções das configurações do evento (docs/57): o evento em si, os tipos de entrada, as regras da
/// bilheteria local e as chaves técnicas. O autor de cada gravação é o usuário do login (ADR-0026).
/// </summary>
public sealed partial class EdgeControlService
{
    // Faixa do intervalo entre usos do mesmo cartão da bilheteria: de nenhum a duas horas.
    private const int MaiorIntervaloDeReuso = 7200;

    private const string SemBase = "O serviço não tem base local configurada.";

    public override Task<DadosDoEvento> ObterEvento(ObterEventoRequest request, ServerCallContext context)
    {
        var resposta = new DadosDoEvento { HoraDeCorte = "00:00" };
        if (_configuracoes is null)
        {
            return Task.FromResult(resposta);
        }

        var (evento, por, em) = _configuracoes.LerEvento();
        resposta.Nome = evento.Nome;
        resposta.HoraDeCorte = evento.Corte.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
        resposta.RecusarForaDoPeriodo = evento.RecusarForaDoPeriodo;
        resposta.AlteradoPor = por ?? string.Empty;
        if (evento.Inicio is { } inicio)
        {
            resposta.Inicio = Timestamp.FromDateTimeOffset(inicio);
        }

        if (evento.Fim is { } fim)
        {
            resposta.Fim = Timestamp.FromDateTimeOffset(fim);
        }

        if (em is { } quando)
        {
            resposta.AlteradoEm = Timestamp.FromDateTimeOffset(quando);
        }

        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDaGravacao> GravarEvento(GravarEventoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_configuracoes is null || request.Evento is null)
        {
            return Recusada(SemBase);
        }

        var pedido = request.Evento;
        if (!TimeSpan.TryParseExact(pedido.HoraDeCorte ?? string.Empty, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var corte))
        {
            return Recusada("A hora de corte vai de 00:00 a 23:59, no formato HH:MM.");
        }

        var evento = new EventoConfigurado(
            Nome: pedido.Nome ?? string.Empty,
            Inicio: pedido.Inicio?.ToDateTimeOffset(),
            Fim: pedido.Fim?.ToDateTimeOffset(),
            Corte: corte,
            RecusarForaDoPeriodo: pedido.RecusarForaDoPeriodo);

        var problemas = _configuracoes.GravarEvento(evento, _relogio(), QuemNome(context));
        return Task.FromResult(Resultado(problemas));
    }

    public override Task<ListarTiposDeEntradaResponse> ListarTiposDeEntrada(ListarTiposDeEntradaRequest request, ServerCallContext context)
    {
        var resposta = new ListarTiposDeEntradaResponse();
        if (_cartoes is not null)
        {
            resposta.Tipos.AddRange(_cartoes.TiposDeEntrada().Select(t => new Contracts.Edge.V1.TipoDeEntrada
            {
                Codigo = t.Codigo,
                Nome = t.Nome,
                Ordem = t.Ordem,
                Ativo = t.Ativo,
                EmUso = t.Cartoes,
            }));
        }

        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDaGravacao> GravarTipoDeEntrada(GravarTipoDeEntradaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_cartoes is null || request.Tipo is null)
        {
            return Recusada(SemBase);
        }

        var tipo = request.Tipo;
        var resultado = _cartoes.GravarTipoDeEntrada(
            new Access.Infrastructure.SQLite.TipoDeEntrada((tipo.Codigo ?? string.Empty).Trim().ToUpperInvariant(), tipo.Nome ?? string.Empty, tipo.Ordem, tipo.Ativo),
            Operador(context),
            _relogio());

        return resultado switch
        {
            RepositorioDeIngressos.ResultadoDoTipo.Gravado => Task.FromResult(new ResultadoDaGravacao { Gravada = true }),
            RepositorioDeIngressos.ResultadoDoTipo.CodigoInvalido =>
                Recusada("O código vai de 2 a 20 letras maiúsculas, números ou _ (ex.: INTEIRA, MEIA, SOCIAL)."),
            RepositorioDeIngressos.ResultadoDoTipo.NomeInvalido => Recusada("O nome vai de 1 a 40 letras."),
            RepositorioDeIngressos.ResultadoDoTipo.OrdemInvalida => Recusada("A ordem vai de 0 a 9999."),
            _ => Recusada("Entre de novo no painel: a gravação precisa do seu usuário."),
        };
    }

    public override Task<RegrasDaBilheteria> ObterBilheteriaLocal(ObterBilheteriaLocalRequest request, ServerCallContext context)
    {
        var resposta = new RegrasDaBilheteria { DefinidaPelaNuvem = _nuvem?.Configurada ?? false };
        if (_cartoes?.Provedores().FirstOrDefault(p => p.Id == CargaInicialDeCartoes.ProvedorLocal) is not { } provedor)
        {
            return Task.FromResult(resposta);
        }

        var (ingressos, desativados, cargaEm, cargaQuantidade) = _cartoes.ResumoDoProvedor(provedor.Id);
        resposta.Existe = true;
        resposta.IntervaloDeReusoSegundos = (int)provedor.IntervaloDeReuso.TotalSeconds;
        resposta.SomenteNaUrna = provedor.SomenteNaUrna;
        resposta.Ingressos = ingressos;
        resposta.Desativados = desativados;
        resposta.UltimaCargaQuantidade = cargaQuantidade;
        if (cargaEm is { } em)
        {
            resposta.UltimaCargaEm = Timestamp.FromDateTimeOffset(em);
        }

        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDaGravacao> GravarBilheteriaLocal(GravarBilheteriaLocalRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_cartoes is null)
        {
            return Recusada(SemBase);
        }

        // Com a nuvem ligada, ela regrava o provedor a cada partida (ADR-0025): mudar aqui seria
        // desfeito sem aviso.
        if (_nuvem?.Configurada == true)
        {
            return Recusada("Com a nuvem ligada, estas regras vêm da nuvem e não mudam pelo painel.");
        }

        if (request.IntervaloDeReusoSegundos is < 0 or > MaiorIntervaloDeReuso)
        {
            return Recusada($"O intervalo entre usos vai de 0 a {MaiorIntervaloDeReuso / 60} minutos.");
        }

        var ajustado = _cartoes.AjustarRegrasDoProvedor(
            CargaInicialDeCartoes.ProvedorLocal,
            TimeSpan.FromSeconds(request.IntervaloDeReusoSegundos),
            request.SomenteNaUrna,
            _relogio());

        if (ajustado)
        {
            // O provedor não guarda quem mudou: a trilha fica em edge_setting, com o usuário e a hora.
            _configuracoes?.RegistrarAjusteDaBilheteria(
                $"intervalo={request.IntervaloDeReusoSegundos}s;so_na_urna={(request.SomenteNaUrna ? 1 : 0)}", _relogio(), QuemNome(context));
        }

        return ajustado
            ? Task.FromResult(new ResultadoDaGravacao { Gravada = true })
            : Recusada("A bilheteria local ainda não existe: reinicie o serviço para ele criá-la.");
    }

    public override Task<ListarChavesTecnicasResponse> ListarChavesTecnicas(ListarChavesTecnicasRequest request, ServerCallContext context)
    {
        var resposta = new ListarChavesTecnicasResponse();
        if (_configuracoes is null)
        {
            return Task.FromResult(resposta);
        }

        var c = _configuracoes.Ler().Configuracao;
        resposta.Chaves.AddRange(
        [
            Chave(ConfiguracoesDaBorda.ChaveAcertarRelogioAoDivergir, "Acertar o relógio da catraca sozinho, em operação", c.AcertarRelogioAoDivergir, "docs/21, passo 6A"),
            Chave(ConfiguracoesDaBorda.ChaveEnviarDigitosVariaveis, "Enviar os tamanhos de número aceitos (dígitos variáveis)", c.EnviarDigitosVariaveis, "HIL-CARD-02"),
            Chave(ConfiguracoesDaBorda.ChaveReconectarEmErroDeRecepcao, "Reconectar quando a leitura de eventos falhar", c.ReconectarEmErroDeRecepcao, "HIL-EVT-01"),
            Chave(ConfiguracoesDaBorda.ChaveEnviarDataHoraNoEvento, "Pedir data e hora em cada evento da catraca", c.EnviarDataHoraNoEvento, "INT-CFG-07"),
            Chave(ConfiguracoesDaBorda.ChaveRegistrarAcessoNegado, "Registrar na catraca os acessos negados", c.RegistrarAcessoNegado is not null, "INT-OFF-08"),
            Chave(ConfiguracoesDaBorda.ChaveEnviarTipoDeLista, "Dizer à catraca que não há lista gravada", c.EnviarTipoDeLista, "INT-OFF-02"),
            Chave(ConfiguracoesDaBorda.ChaveEnviarWiegandDoisLeitores, "Configurar Wiegand dos dois leitores", c.EnviarWiegandDoisLeitores, "HIL-CARD-05"),
            Chave(ConfiguracoesDaBorda.ChaveEnviarFormasDeEntrada, "Formas de entrada do leitor pelo modelo", c.EnviarFormasDeEntrada, "INT-SM-032"),
            Chave(ConfiguracoesDaBorda.ChaveSequenciaOficial, "Sequência oficial de conexão da Topdata", c.SequenciaOficial, "INT-SM-021"),
            Chave(ConfiguracoesDaBorda.ChaveColetarBilhetes, "Coletar as marcações da memória da catraca", c.ColetarBilhetes, "INT-REC-03 e CHAOS-REC-01"),
            Chave(ConfiguracoesDaBorda.ChaveExibirTextoDoGiro, "Mostrar no visor o texto do sentido do giro", c.ExibirTextoDoGiro, "mapa de giro (D9)"),
            Chave(
                ConfiguracoesDaBorda.ChaveLinhasHomologadas,
                _configuracoes.LinhasHomologadas() is ({ } linhas, _)
                    ? $"Linhas de firmware aceitas: {string.Join(", ", linhas.Order())} (chave técnica)"
                    : "Linhas de firmware aceitas: 14, 16 e 19 (padrão da versão)",
                _configuracoes.LinhasHomologadas().Linhas is not null,
                "HIL-CAP-01"),
        ]);
        return Task.FromResult(resposta);
    }

    private static ChaveTecnica Chave(string chave, string descricao, bool ligada, string liberadaPor) =>
        new() { Chave = chave, Descricao = descricao, Ligada = ligada, LiberadaPor = liberadaPor };

    private static Task<ResultadoDaGravacao> Recusada(string problema) =>
        Task.FromResult(new ResultadoDaGravacao { Problemas = { problema } });

    private static ResultadoDaGravacao Resultado(IReadOnlyList<string> problemas)
    {
        var resultado = new ResultadoDaGravacao { Gravada = problemas.Count == 0 };
        resultado.Problemas.AddRange(problemas);
        return resultado;
    }
}
