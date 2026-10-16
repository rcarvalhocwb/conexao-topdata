using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>Os relatórios R1, R2, R3 e R7 da prestação de contas (docs/25 §3), com o cabeçalho do §4.</summary>
public sealed partial class EdgeControlService
{
    public override Task<RelatoriosDoEvento> ObterRelatorios(ObterRelatoriosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var agora = _relogio();
        var evento = _configuracoes?.LerEvento().Evento ?? new EventoConfigurado();
        var desde = request.Desde?.ToDateTimeOffset() ?? RelatoriosDoEvento_DiaDeHoje(agora, evento.Corte);
        var ate = request.Ate?.ToDateTimeOffset() ?? agora;

        var resposta = new RelatoriosDoEvento
        {
            NomeDoEvento = evento.Nome,
            HoraDeCorte = evento.Corte.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture),
            PeriodoDesde = Timestamp.FromDateTimeOffset(desde),
            PeriodoAte = Timestamp.FromDateTimeOffset(ate),
            GeradoEm = Timestamp.FromDateTimeOffset(agora),
            GeradoPor = context is null ? "painel" : QuemNome(context),
            Versao = _versao,
        };

        if (_consultas is null)
        {
            return Task.FromResult(resposta);
        }

        var relatorio = _consultas.Relatorios(desde, ate, evento.Corte);
        resposta.Conferencia = relatorio.Conferencia;
        resposta.Tipos.AddRange(relatorio.Tipos.Select(t => new TipoNoRelatorio { Tipo = t, Nome = relatorio.NomesDosTipos[t] }));
        resposta.Dias.AddRange(relatorio.Dias.Select(d => Boletim(d, agora)));
        resposta.PorHora.AddRange(relatorio.PorHora.Select(h => new ValidadosPorHoraETipo
        {
            Hora = Timestamp.FromDateTimeOffset(h.Hora),
            Tipo = h.Tipo,
            Quantidade = h.Quantidade,
        }));
        resposta.PorCatraca.AddRange(relatorio.PorCatraca.Select(c =>
        {
            var inner = InnerDe(c.Dispositivo);
            return new MovimentoPorCatraca
            {
                Inner = inner,
                Nome = _nomes.TryGetValue(inner, out var nome) ? nome : c.Dispositivo,
                Validados = c.Validados,
                SemPassagem = c.SemPassagem,
                Negados = c.Negados,
            };
        }));

        return Task.FromResult(resposta);
    }

    private static BoletimDoDiaDeOperacao Boletim(BoletimDoDia dia, DateTimeOffset agora)
    {
        var boletim = new BoletimDoDiaDeOperacao
        {
            Inicio = Timestamp.FromDateTimeOffset(dia.Inicio),
            Distintos = dia.Distintos,
            NoPico = dia.NoPico,
        };
        boletim.Validados.AddRange(dia.Validados.Select(c => new QuantidadePorChave { Chave = c.Chave, Quantidade = c.Quantidade }));
        boletim.SemPassagem.AddRange(dia.SemPassagem.Select(c => new QuantidadePorChave { Chave = c.Chave, Quantidade = c.Quantidade }));
        boletim.Negados.AddRange(dia.Negados.Select(c => new QuantidadePorChave
        {
            Chave = c.Chave,
            Quantidade = c.Quantidade,
            Descricao = AcompanhamentoDaOperacao.MensagemPara(new TentativaParaOPainel(
                0, Guid.Empty, string.Empty, string.Empty, agora, false, c.Chave, null, null, string.Empty, false)),
        }));

        if (dia.Primeiro is { } primeiro)
        {
            boletim.Primeiro = Timestamp.FromDateTimeOffset(primeiro);
        }

        if (dia.Ultimo is { } ultimo)
        {
            boletim.Ultimo = Timestamp.FromDateTimeOffset(ultimo);
        }

        if (dia.HoraDePico is { } pico)
        {
            boletim.HoraDePico = Timestamp.FromDateTimeOffset(pico);
        }

        return boletim;
    }

    // Sem período, o dia de operação de agora.
    private static DateTimeOffset RelatoriosDoEvento_DiaDeHoje(DateTimeOffset agora, TimeSpan corte) =>
        GeradorDeRelatorios.DiaDeOperacao(agora, corte);
}
