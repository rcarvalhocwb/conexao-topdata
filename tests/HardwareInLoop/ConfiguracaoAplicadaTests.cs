using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;
using Topdata.EasyInner.Interop;

namespace HardwareInLoop.Tests;

/// <summary>
/// Etapa A.5 do docs/35, no nível da DLL: a configuração só passa a "aplicada" (versão e
/// momento) quando <c>EnviarConfiguracoes</c> (EI-030) devolve 0 — com o laço, o adapter de
/// verdade e a costura falsa no lugar da EasyInner.dll.
/// </summary>
/// <remarks>
/// O simulador trabalha no nível do adapter; aqui o retorno ≠ 0 sai da própria função nativa,
/// e um retorno ≠ 0 numa função de montagem (antes do envio) também não conta como aplicado.
/// </remarks>
public sealed class ConfiguracaoAplicadaTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 12, 6, 21, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _agora = Inicio;

    // Cada passo anda um minuto: backoff e disjuntor (30 s) nunca seguram o teste.
    private void Passos(DevicePump laco, DeviceSlot catraca, int quantos, Func<bool>? ate = null)
    {
        for (var i = 0; i < quantos && !(ate?.Invoke() ?? false); i++)
        {
            _agora += TimeSpan.FromMinutes(1);
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }
    }

    [Fact]
    public void Envio_recusado_pela_dll_nao_muda_versao_nem_momento_e_o_retorno_zero_muda()
    {
        var costura = new CosturaFalsa();
        costura.Retornos[nameof(IEasyInnerNative.EnviarConfiguracoes)] = 1;
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => _agora, new HashSet<byte> { 4 });
        var configuracao = PadroesDeFabrica.TopFit4;
        var catraca = new DeviceSlot(1, configuracao, () => _agora);

        Passos(laco, catraca, 40);

        // A DLL recebeu a montagem e o envio, várias vezes, e recusou todas.
        Assert.True(costura.Chamadas.Count(c => c == nameof(IEasyInnerNative.EnviarConfiguracoes)) >= 2);
        Assert.NotEqual(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Null(catraca.ConfiguracaoVersao);
        Assert.Null(catraca.ConfiguracaoAplicadaEm);

        // A catraca passa a aceitar: o primeiro envio com retorno 0 marca a versão e o momento.
        costura.Retornos.Remove(nameof(IEasyInnerNative.EnviarConfiguracoes));
        var enviosAntes = costura.Chamadas.Count(c => c == nameof(IEasyInnerNative.EnviarConfiguracoes));
        DateTimeOffset? primeiroAceito = null;
        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            _agora += TimeSpan.FromMinutes(1);
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
            if (primeiroAceito is null
                && costura.Chamadas.Count(c => c == nameof(IEasyInnerNative.EnviarConfiguracoes)) > enviosAntes)
            {
                primeiroAceito = _agora;
                Assert.Equal(VersaoDaConfiguracao.Calcular(configuracao), catraca.ConfiguracaoVersao);
            }
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(VersaoDaConfiguracao.Calcular(configuracao), catraca.ConfiguracaoVersao);

        // Três envios por conexão (cfg off-line, mudança, on-line): o momento é o do último aceito.
        Assert.NotNull(primeiroAceito);
        Assert.True(catraca.ConfiguracaoAplicadaEm >= primeiroAceito);
    }

    /// <summary>Recusa numa função de montagem: <c>EnviarConfiguracoes</c> nem é chamada, nada é aplicado.</summary>
    [Fact]
    public void Montagem_recusada_antes_do_envio_nao_conta_como_aplicada()
    {
        var costura = new CosturaFalsa();
        costura.Retornos[nameof(IEasyInnerNative.ConfigurarLeitor2)] = 129;
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => _agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, PadroesDeFabrica.TopFit4, () => _agora);

        Passos(laco, catraca, 40);

        Assert.Contains(nameof(IEasyInnerNative.ConfigurarLeitor2), costura.Chamadas);
        Assert.DoesNotContain(nameof(IEasyInnerNative.EnviarConfiguracoes), costura.Chamadas);
        Assert.Null(catraca.ConfiguracaoVersao);
        Assert.Null(catraca.ConfiguracaoAplicadaEm);
    }
}
