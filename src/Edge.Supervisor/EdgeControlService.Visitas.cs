using System.Security.Cryptography;
using Access.Domain.Credentials;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>P6: as RPCs de visitas passam pelo mapa de permissões da sessão; respostas só com máscaras.</summary>
public sealed partial class EdgeControlService
{
    private readonly Visitas? _visitas;

    public override Task<ListarVisitasResponse> ListarVisitas(ListarVisitasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new ListarVisitasResponse();
        foreach (var v in AgendaDeVisitas().Listar(_relogio(), Vazio(request.Situacao), request.Limite > 0 ? request.Limite : 200))
        {
            var linha = new VisitaDoCadastro
            {
                Id = v.Id, PessoaId = v.PessoaId ?? string.Empty, AnfitriaoId = v.AnfitriaoId ?? string.Empty,
                Nome = v.Nome, Anfitriao = v.Anfitriao, Motivo = v.Motivo, Situacao = v.Situacao,
                De = Timestamp.FromDateTimeOffset(v.De), Ate = Timestamp.FromDateTimeOffset(v.Ate),
                CodigoMascarado = v.ValorDaCredencial is null ? string.Empty : CredentialValue.Mascarar(v.ValorDaCredencial),
                DocumentoMascarado = MascararDado(v.Documento),
            };
            if (v.Chegada is { } chegada)
            {
                linha.Chegada = Timestamp.FromDateTimeOffset(chegada);
            }

            if (v.Saida is { } saida)
            {
                linha.Saida = Timestamp.FromDateTimeOffset(saida);
            }

            resposta.Visitas.Add(linha);
        }

        return Task.FromResult(resposta);
    }

    public override Task<BuscarAnfitrioesDeVisitaResponse> BuscarAnfitrioesDeVisita(BuscarAnfitrioesDeVisitaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resposta = new BuscarAnfitrioesDeVisitaResponse();
        resposta.Anfitrioes.AddRange(AgendaDeVisitas().Anfitrioes(request.Texto, request.Limite > 0 ? request.Limite : 200)
            .Select(a => new AnfitriaoDaVisita { Id = a.Id, Nome = a.Nome }));
        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDoCadastroDePessoas> AgendarVisita(AgendarVisitaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.De is null || request.Ate is null)
        {
            return Resultado(ResultadoDoCadastro.Recusado("Informe o início e o fim da janela da visita."));
        }

        return Resultado(AgendaDeVisitas().Agendar(QuemId(context), request.Nome, request.AnfitriaoId, request.Motivo,
            request.De.ToDateTimeOffset(), request.Ate.ToDateTimeOffset(), _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> ReceberVisita(ReceberVisitaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return Resultado(AgendaDeVisitas().Receber(QuemId(context), request.Id, request.TipoDoDocumento,
                request.Documento, request.DocumentoConferido, request.TipoDoCodigo, request.Codigo, _relogio()));
        }
        catch (CryptographicException)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Não foi possível ler os dados da visita. Confira a chave do cofre; nenhuma credencial foi entregue."));
        }
    }

    public override Task<ResultadoDoCadastroDePessoas> EncerrarVisita(EncerrarVisitaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(AgendaDeVisitas().Encerrar(QuemId(context), request.Id, _relogio()));
    }

    private Visitas AgendaDeVisitas() => _visitas
        ?? throw new RpcException(new Status(StatusCode.FailedPrecondition,
            "Visitas não estão disponíveis: a chave dos dados pessoais não pôde ser lida do cofre desta máquina."));
}
