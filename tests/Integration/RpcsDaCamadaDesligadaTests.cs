using System.Reflection;
using Contracts.Edge.V1;
using Edge.Supervisor;

namespace Integration.Tests;

/// <summary>
/// Achado E7-1 do docs/41: 7 RPCs do contrato não tinham implementação e devolviam <c>UNIMPLEMENTED</c>,
/// um erro de transporte, enquanto o contrato descrevia um serviço funcionando.
/// </summary>
public sealed class RpcsDaCamadaDesligadaTests
{
    [Fact]
    public void Toda_rpc_do_contrato_tem_implementacao_no_servico()
    {
        // Uma RPC nova no proto sem implementação reprova aqui, e não no painel em campo.
        var rpcs = typeof(EdgeControl.EdgeControlBase)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsVirtual)
            .ToList();
        Assert.True(rpcs.Count > 20, $"só {rpcs.Count} RPCs encontradas");

        var semImplementacao = rpcs
            .Where(m => typeof(EdgeControlService).GetMethod(m.Name, [.. m.GetParameters().Select(p => p.ParameterType)])!.DeclaringType != typeof(EdgeControlService))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(semImplementacao);
    }

    [Fact]
    public async Task As_da_camada_inteligente_respondem_desligada_em_vez_de_erro()
    {
        var servico = new EdgeControlService(new WorkerSupervisor([]), semConfiguracao: true);

        Assert.Equal(EdgeControlService.CamadaDesligada, (await servico.ObterSugestoes(new ObterSugestoesRequest(), null!)).Desligada);
        Assert.Equal(EdgeControlService.CamadaDesligada, (await servico.ObterSaudeDasCatracas(new ObterSaudeDasCatracasRequest(), null!)).Desligada);
        Assert.Equal(EdgeControlService.CamadaDesligada, (await servico.ObterRitmo(new ObterRitmoRequest(), null!)).Desligada);
        Assert.Equal(EdgeControlService.CamadaDesligada, (await servico.ListarAlertas(new ListarAlertasRequest(), null!)).Desligada);
        Assert.Equal(EdgeControlService.CamadaDesligada, (await servico.ObterRelatorioPosEvento(new ObterRelatorioPosEventoRequest(), null!)).Desligada);

        var destino = await servico.RegistrarDestinoDaSugestao(new RegistrarDestinoDaSugestaoRequest(), null!);
        Assert.False(destino.Registrada);
        Assert.Equal(EdgeControlService.CamadaDesligada, Assert.Single(destino.Problemas));

        var ciente = await servico.MarcarAlertaComoCiente(new MarcarAlertaComoCienteRequest(), null!);
        Assert.False(ciente.Marcado);
        Assert.Equal(EdgeControlService.CamadaDesligada, Assert.Single(ciente.Problemas));
    }
}
