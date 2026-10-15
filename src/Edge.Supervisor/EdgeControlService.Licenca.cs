using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Grpc.Core;

namespace Edge.Supervisor;

/// <summary>A licença deste computador (docs/60 §1): ver e instalar. Nunca para catraca.</summary>
public sealed partial class EdgeControlService
{
    /// <summary>A licença; nulo sem base (testes antigos): tudo como "não exigida".</summary>
    public ControleDeLicenca? Licenca { get; init; }

    public override Task<LicencaDoSistema> ObterLicenca(ObterLicencaRequest request, ServerCallContext context)
    {
        if (Licenca is null)
        {
            return Task.FromResult(new LicencaDoSistema
            {
                Situacao = SituacaoDaLicenca.NaoExigida,
                Mensagem = "Esta versão não exige licença.",
            });
        }

        var estado = Licenca.Estado();
        return Task.FromResult(new LicencaDoSistema
        {
            Situacao = ParaProto(estado.Situacao),
            Mensagem = estado.Mensagem,
            Cliente = estado.Dados?.Cliente ?? string.Empty,
            Numero = estado.Dados?.Numero ?? string.Empty,
            VenceEm = estado.Dados?.VenceEm.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Dias = estado.Dias,
            Instalacao = Licenca.Exigida ? Licenca.Instalacao : string.Empty,
            Problema = estado.Problema ?? string.Empty,
        });
    }

    public override Task<ResultadoDaGravacao> InstalarLicenca(InstalarLicencaRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Licenca is null)
        {
            return Recusada(SemBase);
        }

        var problemas = Licenca.Instalar(request.Texto, Operador(context));
        var resposta = new ResultadoDaGravacao { Gravada = problemas.Count == 0 };
        resposta.Problemas.AddRange(problemas);
        return Task.FromResult(resposta);
    }

    internal static SituacaoDaLicenca ParaProto(SituacaoDaLicencaLocal situacao) => situacao switch
    {
        SituacaoDaLicencaLocal.NaoExigida => SituacaoDaLicenca.NaoExigida,
        SituacaoDaLicencaLocal.Valida => SituacaoDaLicenca.Valida,
        SituacaoDaLicencaLocal.VenceEmBreve => SituacaoDaLicenca.VenceEmBreve,
        SituacaoDaLicencaLocal.EmTolerancia => SituacaoDaLicenca.EmTolerancia,
        SituacaoDaLicencaLocal.Bloqueada => SituacaoDaLicenca.Bloqueada,
        _ => SituacaoDaLicenca.NaoEspecificado,
    };
}
