using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using static Access.Infrastructure.SQLite.RepositorioDeIngressos;

namespace Edge.Supervisor;

/// <summary>
/// Cadastro de cartões na operação (migração 026): a sessão de lote por leitura na urna e o cadastro de
/// um cartão recusado. Nenhum RPC daqui devolve o código: a lista traz a máscara, e o cadastro recebe o
/// identificador da tentativa.
/// </summary>
public partial class EdgeControlService
{
    public override Task<ResultadoDaSessaoDeCadastroResponse> AbrirSessaoDeCadastro(
        AbrirSessaoDeCadastroRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cartoes = Cartoes();

        if (string.IsNullOrWhiteSpace(request.ProvedorId))
        {
            return Task.FromResult(RespostaDaSessao(ResultadoDaSessaoDeCadastro.DadosInvalidos, null));
        }

        var resultado = cartoes.AbrirSessaoDeCadastro(
            request.ProvedorId, request.Categoria, request.Lote, Operador(context), request.Usos, _relogio());
        return Task.FromResult(RespostaDaSessao(ParaProto(resultado), cartoes.SessaoDeCadastroAberta()));
    }

    public override Task<ResultadoDaSessaoDeCadastroResponse> FecharSessaoDeCadastro(
        FecharSessaoDeCadastroRequest request,
        ServerCallContext context)
    {
        var fechada = Cartoes().FecharSessaoDeCadastro(_relogio());
        return Task.FromResult(fechada is null
            ? RespostaDaSessao(ResultadoDaSessaoDeCadastro.NenhumaAberta, null)
            : RespostaDaSessao(ResultadoDaSessaoDeCadastro.Fechada, fechada));
    }

    public override Task<ResultadoDaSessaoDeCadastroResponse> ObterSessaoDeCadastro(
        ObterSessaoDeCadastroRequest request,
        ServerCallContext context)
    {
        var aberta = Cartoes().SessaoDeCadastroAberta();
        return Task.FromResult(aberta is null
            ? RespostaDaSessao(ResultadoDaSessaoDeCadastro.NenhumaAberta, null)
            : RespostaDaSessao(ResultadoDaSessaoDeCadastro.Aberta, aberta));
    }

    public override Task<ListarLeiturasDesconhecidasResponse> ListarLeiturasDesconhecidas(
        ListarLeiturasDesconhecidasRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limite = request.Limite is >= 1 and <= 500 ? request.Limite : 100;

        var resposta = new ListarLeiturasDesconhecidasResponse();
        foreach (var cartao in Cartoes().CartoesNaoReconhecidos(limite))
        {
            resposta.Cartoes.Add(new LeituraDesconhecida
            {
                TentativaId = cartao.TentativaId.ToString(),
                CodigoMascarado = cartao.CodigoMascarado,
                Vezes = cartao.Vezes,
                UltimaVez = Timestamp.FromDateTimeOffset(cartao.UltimaVez),
            });
        }

        return Task.FromResult(resposta);
    }

    public override Task<ResultadoDoRegistroResponse> CadastrarLeituraDesconhecida(
        CadastrarLeituraDesconhecidaRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cartoes = Cartoes();

        if (!Guid.TryParse(request.TentativaId, out var tentativa) || string.IsNullOrWhiteSpace(request.ProvedorId))
        {
            return Task.FromResult(new ResultadoDoRegistroResponse { Resultado = ResultadoDoRegistro.DadosInvalidos });
        }

        var (resultado, _) = cartoes.CadastrarNaoReconhecido(
            tentativa, request.ProvedorId, request.Categoria, request.Lote, Operador(context), request.Usos, _relogio());
        return Task.FromResult(new ResultadoDoRegistroResponse { Resultado = ParaProto(resultado) });
    }

    private RepositorioDeIngressos Cartoes() =>
        _cartoes ?? throw new RpcException(new Status(StatusCode.Unimplemented, "cadastro de cartões indisponível nesta instalação"));

    // Quem está operando, com o tamanho que a base aceita para autoria (2 a 80).
    private static string Operador(ServerCallContext context)
    {
        var nome = QuemNome(context);
        return nome.Length <= 80 ? nome : nome[..80];
    }

    private static ResultadoDaSessaoDeCadastroResponse RespostaDaSessao(ResultadoDaSessaoDeCadastro resultado, SessaoDeLote? sessao)
    {
        var resposta = new ResultadoDaSessaoDeCadastroResponse { Resultado = resultado };
        if (sessao is not null)
        {
            resposta.Sessao = new SessaoDeCadastro
            {
                Id = sessao.Id.ToString(),
                ProvedorId = sessao.ProvedorId,
                Categoria = sessao.Categoria,
                Lote = sessao.Lote,
                Usos = sessao.Usos,
                Operador = sessao.Operador,
                AbertaEm = Timestamp.FromDateTimeOffset(sessao.AbertaEm),
                Cadastrados = sessao.Cadastrados,
            };
        }

        return resposta;
    }

    private static ResultadoDaSessaoDeCadastro ParaProto(ResultadoDaAberturaDeSessao resultado) => resultado switch
    {
        ResultadoDaAberturaDeSessao.Aberta => ResultadoDaSessaoDeCadastro.Aberta,
        ResultadoDaAberturaDeSessao.JaAberta => ResultadoDaSessaoDeCadastro.JaAberta,
        ResultadoDaAberturaDeSessao.ProvedorDesconhecido => ResultadoDaSessaoDeCadastro.ProvedorDesconhecido,
        ResultadoDaAberturaDeSessao.ProvedorNaoReutilizavel => ResultadoDaSessaoDeCadastro.ProvedorNaoReutilizavel,
        _ => ResultadoDaSessaoDeCadastro.DadosInvalidos,
    };

    private static ResultadoDoRegistro ParaProto(ResultadoDoCadastroDeCartao resultado) => resultado switch
    {
        ResultadoDoCadastroDeCartao.Cadastrado => ResultadoDoRegistro.Cadastrado,
        ResultadoDoCadastroDeCartao.JaCadastrado => ResultadoDoRegistro.JaCadastrado,
        ResultadoDoCadastroDeCartao.TentativaNaoEncontrada => ResultadoDoRegistro.TentativaNaoEncontrada,
        ResultadoDoCadastroDeCartao.ProvedorDesconhecido => ResultadoDoRegistro.ProvedorDesconhecido,
        ResultadoDoCadastroDeCartao.ProvedorNaoReutilizavel => ResultadoDoRegistro.ProvedorNaoReutilizavel,
        _ => ResultadoDoRegistro.DadosInvalidos,
    };
}
