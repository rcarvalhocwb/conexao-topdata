using Access.Application.Devices;
using Access.Domain.Devices;
using Edge.Worker;
using Topdata.EasyInner.Adapter;

namespace HardwareInLoop.Tests;

/// <summary>
/// Erro na recepção de eventos entra no caminho de falha, e não no silêncio (defeito F6,
/// docs/34 §2).
/// </summary>
/// <remarks>
/// Antes, todo retorno ≠ 0 de <c>ReceberDadosOnLine</c> virava "sem eventos" sem deixar
/// rastro. Agora o adaptador sempre conta e preserva o bruto, e o laço o registra. Reconectar
/// depende de <see cref="TopdataInnerAdapter.ReconectarEmErroDeRecepcao"/> (chave
/// <c>catraca.reconectar_em_erro_de_recepcao</c>), desligado até HIL-EVT-01: o manual não diz
/// o que a DLL devolve quando não há evento, e reconectar por engano faria a catraca
/// reconectar sem parar.
/// </remarks>
public sealed class RecepcaoComErroTests
{
    private static DeviceConfiguration Configuracao() => new()
    {
        PadraoCartao = 1,
        QuantidadeFixaDeDigitos = 10,
        TipoDeLeitor = 8,
        OperacaoDoLeitor1 = 1,
        OperacaoDoLeitor2 = 0,
        FuncaoDoAcionamento1 = 2,
        TempoDoAcionamento1 = 5,
        FuncaoDoAcionamento2 = 0,
        TempoDoAcionamento2 = 0,
        Online = true,
        TecladoHabilitado = false,
        EcoDoTeclado = 0,
        MudancaAutomatica = 0,
        TempoDaMudancaAutomatica = 10,
        MensagemPadrao = "Aproxime o ingresso",
        PerfilFisico = GatePhysicalProfile.Padrao,
    };

    [Fact]
    public void Com_a_chave_ligada_erro_na_recepcao_conta_reconecta_e_volta_a_operar()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura) { ReconectarEmErroDeRecepcao = true };
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        void Ate(DeviceState alvo)
        {
            for (var i = 0; i < 40 && catraca.Maquina.Current != alvo; i++)
            {
                agora += TimeSpan.FromMinutes(1);
                laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
            }

            Assert.Equal(alvo, catraca.Maquina.Current);
        }

        Ate(DeviceState.Polling);
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio

        costura.Retornos["ReceberDadosOnLine"] = 1;
        var feito = laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);
        Assert.Equal(1, catraca.ErrosDeRecepcao);
        Assert.Equal(1, adapter.ErrosDeRecepcao);
        Assert.Equal(1, catraca.Disjuntor.FalhasSeguidas);
        Assert.Contains("retorno=1", feito, StringComparison.Ordinal);
        Assert.Contains("ReceberDadosOnLine", feito, StringComparison.Ordinal);

        // A comunicação volta: o mesmo laço reconecta sozinho, sem ninguém reiniciar nada.
        costura.Retornos.Remove("ReceberDadosOnLine");
        Ate(DeviceState.Polling);
        Assert.Equal(1, catraca.ErrosDeRecepcao);
    }

    /// <summary>
    /// Padrão (chave desligada): o erro é contado e registrado, mas a catraca continua em
    /// Polling, sem reconectar nem mexer no disjuntor — o comportamento de antes, agora com rastro.
    /// </summary>
    [Fact]
    public void Com_a_chave_desligada_erro_na_recepcao_conta_e_segue_em_polling()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            agora += TimeSpan.FromMinutes(1);
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio
        var enviosAntes = costura.Chamadas.Count(c => c == "EnviarConfiguracoes");

        costura.Retornos["ReceberDadosOnLine"] = 1;
        var feitos = new List<string>();
        for (var i = 0; i < 25; i++)
        {
            feitos.Add(laco.Passo(catraca, TimeSpan.FromMilliseconds(10)));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(25, catraca.ErrosDeRecepcao);
        Assert.Equal(25, adapter.ErrosDeRecepcao);
        Assert.Equal(0, catraca.Disjuntor.FalhasSeguidas);
        Assert.Equal(enviosAntes, costura.Chamadas.Count(c => c == "EnviarConfiguracoes"));

        // Só os marcos (1ª e 10ª) vão ao registro; o resto é contado sem uma linha por volta.
        var registrados = feitos.Where(f => f != "sem eventos").ToList();
        Assert.Equal(2, registrados.Count);
        Assert.All(registrados, f =>
        {
            Assert.Contains("retorno=1", f, StringComparison.Ordinal);
            Assert.Contains("HIL-EVT-01", f, StringComparison.Ordinal);
        });
        Assert.Contains("nº 10", registrados[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// Achado E1-01 do docs/41: com a chave desligada (padrão), o retorno ≠ 0 em espera de leitura
    /// vira "sem eventos" e a catraca seguia em Polling para sempre, aparecendo "Atendendo" com o cabo
    /// puxado. A sonda de vida testa a conexão depois de 10 s sem sinal; sem resposta, reconecta.
    /// </summary>
    [Fact]
    public void Catraca_muda_em_polling_e_detectada_pela_sonda_de_vida_e_vai_para_reconexao()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio

        // Cabo puxado: a espera devolve ≠ 0 (que vira "sem eventos") e o teste de conexão falha.
        costura.Retornos["ReceberDadosOnLine"] = 1;
        costura.Retornos["Ping"] = 1;

        agora += TimeSpan.FromSeconds(9);
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.DoesNotContain("Ping", costura.Chamadas.Skip(costura.Chamadas.LastIndexOf("EnviarRelogio")));

        agora += TimeSpan.FromSeconds(1);
        costura.Chamadas.Clear();
        var feito = laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal(["Ping"], costura.Chamadas);
        Assert.Equal(DeviceState.Reconectar, catraca.Maquina.Current);
        Assert.Contains("não respondeu à sonda de vida", feito, StringComparison.Ordinal);
        Assert.Equal(1, catraca.Disjuntor.FalhasSeguidas);
    }

    /// <summary>A catraca que responde à sonda segue atendendo, e a sonda só volta depois de mais 10 s.</summary>
    [Fact]
    public void Catraca_que_responde_a_sonda_segue_em_polling_sem_sondar_a_cada_volta()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio
        costura.Chamadas.Clear();

        for (var segundo = 1; segundo <= 25; segundo++)
        {
            agora += TimeSpan.FromSeconds(1);
            Assert.Equal("sem eventos", laco.Passo(catraca, TimeSpan.FromMilliseconds(10)));
        }

        Assert.Equal(DeviceState.Polling, catraca.Maquina.Current);
        Assert.Equal(2, costura.Chamadas.Count(c => c == "Ping"));
        Assert.Equal(23, costura.Chamadas.Count(c => c == "ReceberDadosOnLine"));
    }

    /// <summary>O 8 continua sendo falha de dependência: insistir não adianta.</summary>
    [Fact]
    public void Retorno_oito_na_recepcao_continua_falha_de_dependencia()
    {
        var agora = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        var costura = new CosturaFalsa { OrigemADevolver = 0 };
        using var adapter = new TopdataInnerAdapter(costura);
        var laco = new DevicePump(adapter, () => agora, new HashSet<byte> { 4 });
        var catraca = new DeviceSlot(1, Configuracao(), () => agora);

        for (var i = 0; i < 40 && catraca.Maquina.Current != DeviceState.Polling; i++)
        {
            laco.Passo(catraca, TimeSpan.FromMilliseconds(10));
        }

        laco.Passo(catraca, TimeSpan.FromMilliseconds(10)); // acerto do relógio
        costura.Retornos["ReceberDadosOnLine"] = 8;
        laco.Passo(catraca, TimeSpan.FromMilliseconds(10));

        Assert.Equal(DeviceState.FalhaFatalDeDependencia, catraca.Maquina.Current);
    }
}
