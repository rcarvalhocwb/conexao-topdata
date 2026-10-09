using Access.Domain.Tempo;
using Access.Importacao;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>Importação de pessoas por planilha (docs/43 P5): prévia, aplicar tudo ou nada, desfazer.</summary>
/// <remarks>
/// O serviço lê o arquivo (<see cref="PlanilhaDePessoas"/>) e o cadastro confere cada linha com a mesma regra
/// da ficha. Os problemas apontam a linha e o motivo, nunca o código nem o documento.
/// </remarks>
public sealed partial class EdgeControlService
{
    private readonly ImportacaoDePessoas? _importacaoDePessoas;

    public override Task<ResultadoDaImportacaoDePessoas> PreverImportacaoDePessoas(ImportacaoDePessoasRequest request, ServerCallContext context) =>
        Task.FromResult(Importar(request, context, aplicar: false));

    public override Task<ResultadoDaImportacaoDePessoas> ImportarPessoas(ImportacaoDePessoasRequest request, ServerCallContext context) =>
        Task.FromResult(Importar(request, context, aplicar: true));

    public override Task<ListarImportacoesDePessoasResponse> ListarImportacoesDePessoas(ListarImportacoesDePessoasRequest request, ServerCallContext context)
    {
        var resposta = new ListarImportacoesDePessoasResponse();
        foreach (var lote in Importacao().Lotes())
        {
            var msg = new LoteDeImportacaoDePessoas
            {
                Id = lote.Id,
                Em = Timestamp.FromDateTimeOffset(lote.Em),
                Por = lote.Por is { } por && _usuarios?.Obter(por) is { } usuario ? $"{usuario.Nome} ({usuario.Login})" : lote.Por ?? string.Empty,
                Pessoas = lote.Pessoas,
                Credenciais = lote.Credenciais,
            };
            if (lote.DesfeitoEm is { } desfeito)
            {
                msg.DesfeitoEm = Timestamp.FromDateTimeOffset(desfeito);
            }

            resposta.Lotes.Add(msg);
        }

        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDoCadastroDePessoas> DesfazerImportacaoDePessoas(DesfazerImportacaoDePessoasRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Resultado(Importacao().Desfazer(QuemId(context), request.LoteId, _relogio()));
    }

    private ResultadoDaImportacaoDePessoas Importar(ImportacaoDePessoasRequest request, ServerCallContext context, bool aplicar)
    {
        ArgumentNullException.ThrowIfNull(request);
        var importacao = Importacao();
        using var fluxo = new MemoryStream(request.Conteudo.ToByteArray(), writable: false);
        var leitura = PlanilhaDePessoas.Ler(fluxo, request.NomeDoArquivo);

        var linhas = leitura.Pessoas.Select(Linha).ToList();
        var resultado = leitura.Problemas.Any(p => p.Linha <= 1) || linhas.Count == 0
            ? new DesfechoDaImportacao(null, 0, 0, 0, 0, [])
            : aplicar && leitura.Problemas.Count == 0
                ? importacao.Aplicar(QuemId(context), linhas, leitura.Sha256, _relogio())
                : importacao.Prever(QuemId(context), linhas, _relogio());

        var problemas = leitura.Problemas.Concat(resultado.Problemas).OrderBy(p => p.Linha).ToList();
        var resposta = new ResultadoDaImportacaoDePessoas
        {
            Aplicavel = problemas.Count == 0 && resultado.Pessoas > 0,
            Aplicada = resultado.LoteId is not null,
            LoteId = resultado.LoteId ?? string.Empty,
            Pessoas = resultado.Pessoas,
            Credenciais = resultado.Credenciais,
            EmpresasNovas = resultado.EmpresasNovas,
            SalasNovas = resultado.SalasNovas,
        };
        resposta.Problemas.AddRange(problemas.Select(p => new ProblemaDaLinha { Linha = p.Linha, Problema = p.Problema }));
        resposta.Avisos.AddRange(leitura.Avisos);
        return resposta;
    }

    private static PessoaParaImportar Linha(PessoaDaPlanilha p) => new(
        p.Linha,
        new DadosDaPessoa
        {
            PerfilId = p.Perfil,
            NomeCompleto = p.Nome,
            NomeSocial = p.NomeSocial,
            TipoDoDocumento = p.TipoDoDocumento,
            Documento = p.Documento,
            Nascimento = p.Nascimento,
            Telefone = p.Telefone,
            Email = p.Email,
            Veiculo = p.Veiculo,
            Responsavel = p.Responsavel,
            Departamento = p.Departamento,
            Cargo = p.Cargo,
            Matricula = p.Matricula,
            Observacao = p.Observacao,
            ValidoDe = p.ValidoDe is { } de ? HoraDeBrasilia.DoEvento(de.ToDateTime(TimeOnly.MinValue)) : null,
            ValidoAte = p.ValidoAte is { } ate ? HoraDeBrasilia.DoEvento(ate.AddDays(1).ToDateTime(TimeOnly.MinValue)).AddSeconds(-1) : null,
            Catracas = p.Catracas,
        },
        p.Empresa,
        p.Sala,
        p.Credenciais);

    private ImportacaoDePessoas Importacao() => _importacaoDePessoas
        ?? throw new RpcException(new Status(StatusCode.FailedPrecondition,
            "A importação de pessoas não está disponível: a chave dos dados pessoais não pôde ser lida do cofre desta máquina."));
}
