using Access.Application.Devices;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>
/// Parametrização por catraca (Etapa A.6 do docs/35): ler e gravar a camada de uma catraca.
/// </summary>
/// <remarks>
/// <para>
/// Obter monta, para cada campo que a catraca pode sobrepor, o valor dela (ou "herda"), o que
/// ela herdaria (fábrica + evento), o efetivo (as três camadas), a origem e a situação (chega à
/// catraca, chave técnica desligada ou a confirmar). Devolve também a versão do <b>salvo</b> —
/// <see cref="VersaoDaConfiguracao"/> sobre o que o "Aplicar agora" desta catraca enviaria,
/// lido por <c>ConfiguracaoPorCatraca.ParaAplicar</c>, o mesmo caminho do worker — e a
/// <b>aplicada</b>, a que o worker publicou em <c>device_status</c> (Etapa A.5). A tela compara as
/// duas para dizer "aplicada" ou "salva, não aplicada".
/// </para>
/// <para>
/// Gravar troca a camada inteira com <c>ConfiguracoesDasCatracas.Gravar</c>, com o nome digitado
/// (não há login), e não aplica: aplicar é o comando <c>APLICAR_CONFIGURACAO</c> daquela catraca,
/// que já existe. Nada aqui lança para a tela: valor ilegível, base ocupada e catraca não
/// cadastrada voltam em <c>problemas</c>.
/// </para>
/// </remarks>
public sealed partial class EdgeControlService
{
    public override Task<ConfiguracaoDaCatraca> ObterConfiguracaoDaCatraca(
        ObterConfiguracaoDaCatracaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ConfiguracaoDaCatraca { Inner = request.Inner };

        if (ProblemaDaCatraca(request.Inner) is { } problema)
        {
            resposta.Problemas.Add(problema);
            return Task.FromResult(resposta);
        }

        try
        {
            PreencherCampos(request.Inner, resposta);
            PreencherVersoes(request.Inner, resposta, _relogio());
        }
        catch (Microsoft.Data.Sqlite.SqliteException erro)
        {
            // Base ocupada: a tela mostra o motivo e tenta de novo.
            resposta.Campos.Clear();
            resposta.Problemas.Add($"A configuração não pôde ser lida agora ({erro.GetType().Name}); tente de novo.");
        }

        return Task.FromResult(resposta);
    }

    public override Task<GravarConfiguracaoDaCatracaResponse> GravarConfiguracaoDaCatraca(
        GravarConfiguracaoDaCatracaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new GravarConfiguracaoDaCatracaResponse();

        if (ProblemaDaCatraca(request.Inner) is { } problema)
        {
            resposta.Problemas.Add(problema);
            return Task.FromResult(resposta);
        }

        try
        {
            var problemas = new List<string>();
            var (atual, _) = _configuracoesDasCatracas!.Ler(request.Inner);
            var evento = Evento();
            var efetivaAtual = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento, atual);
            var nova = SobreposicoesDaCatraca.Nenhuma;
            var vistos = new HashSet<CampoDaCatraca>();

            foreach (var item in request.Valores)
            {
                if (!CamposDaParametrizacao.Todos.Contains(item.Campo))
                {
                    problemas.Add("Campo desconhecido.");
                    continue;
                }

                if (!vistos.Add(item.Campo))
                {
                    problemas.Add($"{CamposDaParametrizacao.Rotulo(item.Campo)}: informado mais de uma vez.");
                    continue;
                }

                var valor = item.HasValor ? item.Valor : null;
                nova = CamposDaParametrizacao.Aplicar(nova, item.Campo, valor, problemas);
            }

            // O que a tela mostra desabilitado não muda por aqui; o que já estava gravado continua.
            foreach (var campo in CamposDaParametrizacao.Todos)
            {
                var antes = CamposDaParametrizacao.DaCamada(campo, atual);
                var depois = CamposDaParametrizacao.DaCamada(campo, nova);
                if (string.Equals(antes, depois, StringComparison.Ordinal))
                {
                    continue;
                }

                var (situacao, motivo) = CamposDaParametrizacao.Situacao(campo, efetivaAtual);
                if (situacao is not SituacaoDoCampo.Enviado)
                {
                    problemas.Add($"{CamposDaParametrizacao.Rotulo(campo)}: aguardando confirmação, não pode ser mudado. {motivo}");
                    continue;
                }

                var (aguardando, motivoDosValores) = CamposDaParametrizacao.Aguardando(campo);
                if (depois is not null && aguardando.Contains(depois, StringComparer.Ordinal))
                {
                    problemas.Add($"{CamposDaParametrizacao.Rotulo(campo)}: \"{depois}\" aguarda confirmação. {motivoDosValores}");
                }
            }

            if (problemas.Count == 0)
            {
                problemas.AddRange(_configuracoesDasCatracas.Gravar(request.Inner, nova, _relogio(), request.Operador ?? string.Empty));
            }

            if (problemas.Count > 0)
            {
                resposta.Problemas.AddRange(problemas);
                return Task.FromResult(resposta);
            }

            resposta.Gravada = true;
            resposta.VersaoSalva = VersaoSalva(request.Inner, []);
        }
        catch (Microsoft.Data.Sqlite.SqliteException erro)
        {
            resposta.Gravada = false;
            resposta.Problemas.Add($"Não foi gravado: a base local está ocupada ({erro.GetType().Name}); tente de novo.");
        }

        return Task.FromResult(resposta);
    }

    /// <summary>Por que não dá para ler nem gravar a configuração desta catraca; nulo se dá.</summary>
    private string? ProblemaDaCatraca(int inner)
    {
        if (_configuracoesDasCatracas is null || _configuracaoPorCatraca is null)
        {
            return "O serviço não tem base local configurada.";
        }

        return _supervisor.Workers.Any(w => w.Inners.Contains(inner))
            ? null
            : $"A catraca {inner} não está cadastrada.";
    }

    /// <summary>A camada do evento como o worker a usa. Ilegível ou ausente: o padrão.</summary>
    private SobreposicoesDoEvento Evento() =>
        (_configuracoes?.Ler().Configuracao ?? new Access.Infrastructure.SQLite.ConfiguracaoDaOperacao()).ParaACatraca();

    private void PreencherCampos(int inner, ConfiguracaoDaCatraca resposta)
    {
        var (camada, problemas) = _configuracoesDasCatracas!.Ler(inner);
        resposta.Problemas.AddRange(problemas);

        var evento = Evento();
        var herdada = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento, SobreposicoesDaCatraca.Nenhuma);
        var efetiva = MontadorDaConfiguracao.Montar(PadroesDeFabrica.TopFit4, evento, camada);

        foreach (var campo in CamposDaParametrizacao.Todos)
        {
            var (situacao, motivo) = CamposDaParametrizacao.Situacao(campo, efetiva);
            var (aguardando, motivoDosValores) = CamposDaParametrizacao.Aguardando(campo);
            var linha = new CampoConfiguradoDaCatraca
            {
                Campo = campo,
                ValorHerdado = CamposDaParametrizacao.DaConfiguracao(campo, herdada),
                ValorEfetivo = CamposDaParametrizacao.DaConfiguracao(campo, efetiva),
                Origem = CamposDaParametrizacao.Origem(campo, evento, camada),
                Situacao = situacao,
                Motivo = motivo,
                MotivoDosValoresAguardando = motivoDosValores,
                Aviso = CamposDaParametrizacao.Aviso(campo),
            };
            linha.ValoresAguardando.AddRange(aguardando);

            if (CamposDaParametrizacao.DaCamada(campo, camada) is { } propria)
            {
                linha.ValorDaCatraca = propria;
            }

            resposta.Campos.Add(linha);
        }

        if (_configuracoesDasCatracas.Listar().FirstOrDefault(c => c.Inner == inner) is { } gravada)
        {
            resposta.AlteradaPor = gravada.AlteradoPor;
            resposta.AlteradaEm = Timestamp.FromDateTimeOffset(gravada.AlteradoEm);
        }
    }

    private void PreencherVersoes(int inner, ConfiguracaoDaCatraca resposta, DateTimeOffset agora)
    {
        var problemas = new List<string>();
        resposta.VersaoSalva = VersaoSalva(inner, problemas);
        resposta.Problemas.AddRange(problemas.Select(p => $"O salvo não pode ser aplicado: {p}"));

        // A aplicada é a que o worker publicou (A.5), a mesma do Equipamento.
        if (CatracasPorInner(agora).TryGetValue(inner, out var c))
        {
            resposta.VersaoAplicada = c.Situacao.ConfiguracaoVersao ?? string.Empty;
            if (c.Situacao.ConfiguracaoAplicadaEm is { } aplicada)
            {
                resposta.AplicadaEm = Timestamp.FromDateTimeOffset(aplicada);
            }
        }
    }

    /// <summary>
    /// A versão do que o "Aplicar agora" desta catraca enviaria, pela mesma leitura do worker
    /// (<c>ConfiguracaoPorCatraca.ParaAplicar</c>). Vazia, com os problemas, quando o worker
    /// recusaria aplicar.
    /// </summary>
    private string VersaoSalva(int inner, List<string> problemas)
    {
        var (configuracao, recusas) = _configuracaoPorCatraca!.ParaAplicar(inner);
        problemas.AddRange(recusas);
        return configuracao is null ? string.Empty : VersaoDaConfiguracao.Calcular(configuracao);
    }
}
