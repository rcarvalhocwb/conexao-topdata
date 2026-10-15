using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Unit.Tests.Licenca;

/// <summary>A licença e a atualização na tela (docs/60 §3): a barra avisa sem esconder as catracas.</summary>
public sealed class LicencaNoPainelTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    private static ObterEstadoResponse Normal() => new()
    {
        Nivel = NivelDeDegradacao.T0Normal,
        EquipamentosConectados = 2,
        EquipamentosCadastrados = 2,
    };

    [Fact]
    public void Barra_avisa_a_licenca_so_quando_as_catracas_estao_bem()
    {
        var resposta = Normal();
        resposta.SituacaoDaLicenca = SituacaoDaLicenca.VenceEmBreve;
        resposta.DiasDaLicenca = 7;

        var estado = EstadoDoPainel.De(resposta, Agora);
        Assert.Equal(SaudeDoPainel.Atencao, estado.Saude);
        Assert.Equal("Catracas funcionando — a licença vence em 7 dias (veja Diagnóstico)", estado.Mensagem);

        // Catraca com problema vem primeiro: a licença não esconde o que é da portaria.
        resposta.EquipamentosConectados = 0;
        Assert.StartsWith("Nenhuma catraca conectada", EstadoDoPainel.De(resposta, Agora).Mensagem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SituacaoDaLicenca.Valida, 90, null)]
    [InlineData(SituacaoDaLicenca.NaoExigida, 0, null)]
    [InlineData(SituacaoDaLicenca.VenceEmBreve, 0, "Catracas funcionando — a licença vence hoje (veja Diagnóstico)")]
    [InlineData(SituacaoDaLicenca.EmTolerancia, 1, "Catracas funcionando — licença vencida: renove em até 1 dia (veja Diagnóstico)")]
    [InlineData(SituacaoDaLicenca.Bloqueada, 3, "Catracas funcionando — licença vencida: relatórios e atualizações bloqueados (veja Diagnóstico)")]
    public void Aviso_da_licenca(SituacaoDaLicenca situacao, int dias, string? esperado) =>
        Assert.Equal(esperado, EstadoDoPainel.AvisoDaLicenca(situacao, dias));

    [Fact]
    public void Atualizacao_iminente_chega_ao_painel()
    {
        var resposta = Normal();
        resposta.AtualizacaoIminente = true;

        Assert.True(EstadoDoPainel.De(resposta, Agora).AtualizacaoIminente);
    }

    [Fact]
    public void Diagnostico_descreve_a_licenca()
    {
        var (sinal, resumo, detalhes) = DiagnosticoViewModel.DescreverLicenca(new LicencaDoSistema
        {
            Situacao = SituacaoDaLicenca.EmTolerancia,
            Mensagem = "A licença venceu em 10/10/2026.",
            Cliente = "Evento de Teste",
            Numero = "L-1",
            VenceEm = "2026-10-10",
        });

        Assert.Equal((Sinal.Atencao, "Vencida (em tolerância)"), (sinal, resumo));
        Assert.Contains(detalhes, d => d is { Rotulo: "Vence em", Valor: "10/10/2026" });
        Assert.Equal((Sinal.Atencao, "Sem licença (em tolerância)"),
            DiagnosticoViewModel.DescreverLicenca(new LicencaDoSistema { Situacao = SituacaoDaLicenca.EmTolerancia }) is var (s, r, _) ? (s, r) : default);
    }

    [Fact]
    public void Diagnostico_descreve_a_atualizacao()
    {
        var (sinal, resumo, _) = DiagnosticoViewModel.DescreverAtualizacao(new SituacaoDaAtualizacao
        {
            Ligada = true, Canal = "Produção", VersaoInstalada = "0.1.210", VersaoDisponivel = "0.1.215",
            Situacao = "Versão 0.1.215 baixada.",
        }, Agora);
        Assert.Equal((Sinal.Atencao, "Versão 0.1.215 disponível"), (sinal, resumo));

        var emDia = DiagnosticoViewModel.DescreverAtualizacao(new SituacaoDaAtualizacao
        {
            Ligada = true, VersaoInstalada = "0.1.215", VersaoDisponivel = "0.1.215", Situacao = "Em dia: a versão 0.1.215 é a mais nova.",
        }, Agora);
        Assert.Equal((Sinal.Bom, "Em dia"), (emDia.Sinal, emDia.Resumo));

        Assert.Equal("Desligada", DiagnosticoViewModel.DescreverAtualizacao(new SituacaoDaAtualizacao(), Agora).Resumo);
    }

    [Fact]
    public void Relancador_espera_a_versao_mudar_e_abre_o_painel_na_bandeja()
    {
        var script = Relancador.Script(@"C:\Program Files\Rayzer\XAcess\Painel\Desktop.App.exe");

        Assert.Contains(@"$exe = 'C:\Program Files\Rayzer\XAcess\Painel\Desktop.App.exe'", script, StringComparison.Ordinal);
        Assert.Contains("ConexaoTopdataEdge", script, StringComparison.Ordinal);
        Assert.Contains("'--bandeja'", script, StringComparison.Ordinal);
        // Aspas simples dobradas: o caminho não quebra a string do PowerShell.
        Assert.Contains(@"$exe = 'C:\O''Brien''s\Desktop.App.exe'", Relancador.Script(@"C:\O'Brien's\Desktop.App.exe"), StringComparison.Ordinal);
    }
}
