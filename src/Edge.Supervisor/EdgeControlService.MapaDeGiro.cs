using Access.Application.Devices;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using OrigemDoGiroNoContrato = Contracts.Edge.V1.OrigemDoGiro;
using OrigemDoGiroNoDominio = Access.Application.Devices.OrigemDoGiro;

namespace Edge.Supervisor;

/// <summary>
/// Mapa de giro por catraca (decisão D9 do dono do produto, docs/34 §9): ler, gravar e registrar
/// a conferência de comissionamento.
/// </summary>
/// <remarks>
/// <para>
/// Mesmo padrão da Parametrização (Etapa A.6): Obter devolve, para as quatro origens, a regra que
/// vale (a gravada ou o padrão de hoje), a conferência da função dela nesta catraca e as versões
/// salva e aplicada — o mapa entra na versão do salvo (<see cref="VersaoDaConfiguracao"/>), então
/// "aplicada" só aparece depois do "Aplicar nesta catraca". Gravar troca o mapa inteiro com o
/// nome digitado e não aplica.
/// </para>
/// <para>
/// Nada aqui lança para a tela: valor ilegível, base ocupada e catraca não cadastrada voltam em
/// <c>problemas</c>.
/// </para>
/// </remarks>
public sealed partial class EdgeControlService
{
    public override Task<MapaDeGiroDaCatraca> ObterMapaDeGiro(ObterMapaDeGiroRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new MapaDeGiroDaCatraca { Inner = request.Inner };

        if (ProblemaDoMapa(request.Inner) is { } problema)
        {
            resposta.Problemas.Add(problema);
            return Task.FromResult(resposta);
        }

        try
        {
            var gravado = _mapasDeGiro!.Ler(request.Inner);
            var conferencias = _mapasDeGiro.Conferencias(request.Inner);
            var (camada, problemasDaCamada) = _configuracoesDasCatracas!.Ler(request.Inner);
            var efetiva = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, Evento(), camada, gravado.Mapa);
            var perfil = efetiva.PerfilFisico;

            resposta.Problemas.AddRange(gravado.Problemas);
            resposta.Problemas.AddRange(problemasDaCamada);
            resposta.FuncaoDoPerfil = ParaOContrato(perfil.FuncaoDeLiberacaoDaEntrada);

            foreach (var origem in MapaDeGiro.Origens)
            {
                var regra = gravado.Mapa.Regra(origem);
                var vale = perfil.Resolver(origem);
                var linha = new RegraDoMapaDeGiro
                {
                    Origem = ParaOContrato(origem),
                    Definida = regra is not null,
                    Funcao = ParaOContrato(vale.Funcao),
                    ContaComo = vale.ContaComo is SentidoContado.Saida ? ContagemDoGiro.Saida : ContagemDoGiro.Entrada,
                    Texto = vale.Texto,
                    TextoPersonalizado = regra?.Texto is not null,
                    FuncaoDoPerfil = regra?.Funcao is null,
                    Conferencia = SituacaoDaConferencia.NaoConferida,
                    Aviso = AvisoDaOrigem(origem, efetiva),
                };

                if (conferencias.TryGetValue(vale.Funcao, out var conferencia))
                {
                    linha.Conferencia = conferencia.ComoEsperado ? SituacaoDaConferencia.ComoEsperado : SituacaoDaConferencia.AoContrario;
                    linha.ConferidaPor = conferencia.Por;
                    linha.ConferidaEm = Timestamp.FromDateTimeOffset(conferencia.Em);
                }

                resposta.Regras.Add(linha);
            }

            if (gravado.AlteradoPor is { } por && gravado.AlteradoEm is { } em)
            {
                resposta.AlteradoPor = por;
                resposta.AlteradoEm = Timestamp.FromDateTimeOffset(em);
            }

            var problemas = new List<string>();
            resposta.VersaoSalva = VersaoSalva(request.Inner, problemas);
            resposta.Problemas.AddRange(problemas.Select(p => $"O salvo não pode ser aplicado: {p}"));
            if (CatracasPorInner(_relogio()).TryGetValue(request.Inner, out var c))
            {
                resposta.VersaoAplicada = c.Situacao.ConfiguracaoVersao ?? string.Empty;
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException erro)
        {
            resposta.Regras.Clear();
            resposta.Problemas.Add($"O mapa de giro não pôde ser lido agora ({erro.GetType().Name}); tente de novo.");
        }

        return Task.FromResult(resposta);
    }

    public override Task<GravarMapaDeGiroResponse> GravarMapaDeGiro(GravarMapaDeGiroRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new GravarMapaDeGiroResponse();

        if (ProblemaDoMapa(request.Inner) is { } problema)
        {
            resposta.Problemas.Add(problema);
            return Task.FromResult(resposta);
        }

        var problemas = new List<string>();
        var mapa = MapaDeGiro.Vazio;
        var vistas = new HashSet<OrigemDoGiroNoDominio>();

        foreach (var pedida in request.Regras)
        {
            if (DoContrato(pedida.Origem) is not { } origem)
            {
                problemas.Add("Origem do giro desconhecida.");
                continue;
            }

            var nome = MapaDeGiro.Nome(origem);
            if (!vistas.Add(origem))
            {
                problemas.Add($"Mapa de giro, {nome}: informado mais de uma vez.");
                continue;
            }

            if (pedida.Herdar)
            {
                continue;
            }

            SentidoContado? contaComo = pedida.ContaComo switch
            {
                ContagemDoGiro.Entrada => SentidoContado.Entrada,
                ContagemDoGiro.Saida => SentidoContado.Saida,
                _ => null,
            };
            if (contaComo is null)
            {
                problemas.Add($"Mapa de giro, {nome}: diga se o giro conta como entrada ou como saída.");
                continue;
            }

            FuncaoDeLiberacao? funcao = null;
            if (pedida.Funcao is not FuncaoDoGiro.NaoEspecificado)
            {
                funcao = DoContrato(pedida.Funcao);
                if (funcao is null)
                {
                    problemas.Add($"Mapa de giro, {nome}: função desconhecida. Os dois sentidos ficam só para a evacuação (D5).");
                    continue;
                }
            }

            var texto = string.IsNullOrWhiteSpace(pedida.Texto) ? null : pedida.Texto;
            mapa = mapa.Com(origem, new RegraDeGiro(funcao, contaComo.Value, texto));
        }

        if (problemas.Count > 0)
        {
            resposta.Problemas.AddRange(problemas);
            return Task.FromResult(resposta);
        }

        try
        {
            problemas.AddRange(_mapasDeGiro!.Gravar(request.Inner, mapa, _relogio(), request.Operador ?? string.Empty));
            if (problemas.Count > 0)
            {
                resposta.Problemas.AddRange(problemas);
                return Task.FromResult(resposta);
            }

            resposta.Gravado = true;
            resposta.VersaoSalva = VersaoSalva(request.Inner, []);
        }
        catch (Microsoft.Data.Sqlite.SqliteException erro)
        {
            resposta.Gravado = false;
            resposta.Problemas.Add($"Não foi gravado: a base local está ocupada ({erro.GetType().Name}); tente de novo.");
        }

        return Task.FromResult(resposta);
    }

    public override Task<RegistrarConferenciaDoGiroResponse> RegistrarConferenciaDoGiro(
        RegistrarConferenciaDoGiroRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new RegistrarConferenciaDoGiroResponse();

        if (ProblemaDoMapa(request.Inner) is { } problema)
        {
            resposta.Problemas.Add(problema);
            return Task.FromResult(resposta);
        }

        if (DoContrato(request.Funcao) is not { } funcao)
        {
            resposta.Problemas.Add("Diga qual função foi conferida.");
            return Task.FromResult(resposta);
        }

        try
        {
            var problemas = _mapasDeGiro!.RegistrarConferencia(
                request.Inner, funcao, request.ComoEsperado, _relogio(), request.Operador ?? string.Empty);
            resposta.Problemas.AddRange(problemas);
            resposta.Registrada = problemas.Count == 0;
        }
        catch (Microsoft.Data.Sqlite.SqliteException erro)
        {
            resposta.Problemas.Add($"Não foi registrada: a base local está ocupada ({erro.GetType().Name}); tente de novo.");
        }

        return Task.FromResult(resposta);
    }

    private string? ProblemaDoMapa(int inner) =>
        _mapasDeGiro is null ? "O serviço não tem base local configurada." : ProblemaDaCatraca(inner);

    // O que a configuração desta catraca diz sobre a origem, sem impedir a regra.
    private static string AvisoDaOrigem(OrigemDoGiroNoDominio origem, DeviceConfiguration efetiva) => origem switch
    {
        OrigemDoGiroNoDominio.Teclado when !efetiva.TecladoHabilitado =>
            "O teclado está desligado nesta catraca: esta regra só vale se ele for ligado.",
        OrigemDoGiroNoDominio.Leitor2 when efetiva.OperacaoDoLeitor2 == MontadorDaConfiguracao.LeitorDesabilitado =>
            "O leitor da urna está desligado nesta catraca: esta regra só vale com ele ligado.",
        _ => string.Empty,
    };

    /// <summary>Tradução da origem do domínio para o contrato.</summary>
    internal static OrigemDoGiroNoContrato ParaOContrato(OrigemDoGiroNoDominio origem) => origem switch
    {
        OrigemDoGiroNoDominio.Leitor1 => OrigemDoGiroNoContrato.Leitor1,
        OrigemDoGiroNoDominio.Leitor2 => OrigemDoGiroNoContrato.Leitor2,
        OrigemDoGiroNoDominio.Teclado => OrigemDoGiroNoContrato.Teclado,
        OrigemDoGiroNoDominio.LiberacaoManual => OrigemDoGiroNoContrato.LiberacaoManual,
        _ => OrigemDoGiroNoContrato.NaoEspecificado,
    };

    internal static OrigemDoGiroNoDominio? DoContrato(OrigemDoGiroNoContrato origem) => origem switch
    {
        OrigemDoGiroNoContrato.Leitor1 => OrigemDoGiroNoDominio.Leitor1,
        OrigemDoGiroNoContrato.Leitor2 => OrigemDoGiroNoDominio.Leitor2,
        OrigemDoGiroNoContrato.Teclado => OrigemDoGiroNoDominio.Teclado,
        OrigemDoGiroNoContrato.LiberacaoManual => OrigemDoGiroNoDominio.LiberacaoManual,
        _ => null,
    };

    /// <summary>Tradução da função de liberação para o contrato (EI-041 a EI-044).</summary>
    internal static FuncaoDoGiro ParaOContrato(FuncaoDeLiberacao funcao) => funcao switch
    {
        FuncaoDeLiberacao.Entrada => FuncaoDoGiro.Entrada,
        FuncaoDeLiberacao.Saida => FuncaoDoGiro.Saida,
        FuncaoDeLiberacao.EntradaInvertida => FuncaoDoGiro.EntradaInvertida,
        FuncaoDeLiberacao.SaidaInvertida => FuncaoDoGiro.SaidaInvertida,
        _ => FuncaoDoGiro.NaoEspecificado,
    };

    internal static FuncaoDeLiberacao? DoContrato(FuncaoDoGiro funcao) => funcao switch
    {
        FuncaoDoGiro.Entrada => FuncaoDeLiberacao.Entrada,
        FuncaoDoGiro.Saida => FuncaoDeLiberacao.Saida,
        FuncaoDoGiro.EntradaInvertida => FuncaoDeLiberacao.EntradaInvertida,
        FuncaoDoGiro.SaidaInvertida => FuncaoDeLiberacao.SaidaInvertida,
        _ => null,
    };
}
