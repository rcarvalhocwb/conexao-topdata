using System.Security.Cryptography;
using Access.Domain.Usuarios;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Data.Sqlite;

namespace Edge.Supervisor;

/// <summary>Direitos do titular (docs/43 P7). Dados completos só com pessoas.ver_dados.</summary>
public sealed partial class EdgeControlService
{
    private readonly RetencaoDePessoas? _retencaoDePessoas;
    private RetencaoDePessoas Lgpd() => _retencaoDePessoas
        ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, "Retenção de pessoas indisponível."));

    public override async Task ExportarDadosDaPessoa(ObterPessoaRequest request,
        IServerStreamWriter<BlocoDosDadosDaPessoa> responseStream, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        try
        {
            ConferirPermissaoLgpd(context, Permissoes.PessoasVerDados);
            foreach (var bloco in Lgpd().Exportar(QuemId(context), request.Id, _relogio(), context.CancellationToken))
            {
                // A exportação pode durar: sair ou perder a permissão interrompe os próximos blocos.
                ConferirPermissaoLgpd(context, Permissoes.PessoasVerDados);
                await responseStream.WriteAsync(new BlocoDosDadosDaPessoa { Conteudo = ByteString.CopyFrom(bloco) });
            }
            ConferirPermissaoLgpd(context, Permissoes.PessoasVerDados);
            await responseStream.WriteAsync(new BlocoDosDadosDaPessoa { Concluido = true });
        }
        catch (KeyNotFoundException) { throw new RpcException(new Status(StatusCode.NotFound, "Pessoa não encontrada.")); }
        catch (Exception erro) when (erro is CryptographicException or InvalidOperationException)
        { throw new RpcException(new Status(StatusCode.FailedPrecondition, "Não foi possível decifrar os dados do titular.")); }
        catch (SqliteException) { throw new RpcException(new Status(StatusCode.Internal, "Não foi possível exportar os dados do titular.")); }
    }

    public override Task<ResultadoDoCadastroDePessoas> ExcluirTitular(ExcluirTitularRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ConferirPermissaoLgpd(context, Permissoes.PessoasExcluir);
        return Resultado(request.Confirmada
            ? Lgpd().Excluir(QuemId(context), request.PessoaId, _relogio())
            : ResultadoDoCadastro.Recusado("Confirme a exclusão definitiva do titular."));
    }

    public override Task<ResultadoDoCadastroDePessoas> GravarTermoDeConsentimento(GravarTermoDeConsentimentoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ConferirPermissaoLgpd(context, Permissoes.CadastroParametros);
        return Resultado(Lgpd().GravarTermo(QuemId(context), request.Versao, request.Texto, _relogio()));
    }

    public override Task<ResultadoDoCadastroDePessoas> RegistrarAceiteDoTermo(RegistrarAceiteDoTermoRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ConferirPermissaoLgpd(context, Permissoes.PessoasEditar);
        return Resultado(request.Confirmado && request.AceitoEm is not null
            ? Lgpd().RegistrarAceite(QuemId(context), request.PessoaId, request.Versao, request.AceitoEm.ToDateTimeOffset(), _relogio())
            : ResultadoDoCadastro.Recusado("Informe o instante e confirme o aceite apresentado pelo titular."));
    }

    private void ConferirPermissaoLgpd(ServerCallContext context, string permissao)
    {
        var chamador = Chamador(context);
        if (chamador is null || _usuarios is null || _sessoes.Usuario(chamador.Token, DateTimeOffset.UtcNow) != chamador.Usuario.Id
            || _usuarios.Obter(chamador.Usuario.Id) is not { Ativo: true, TrocarSenha: false })
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Entre com usuário e senha."));
        if (!_usuarios.PermissoesDe(chamador.Usuario.Id).Contains(permissao))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Seu papel não permite esta ação sobre dados pessoais."));
    }
}
