using Access.Application.Devices;
using Access.Infrastructure.SQLite;
using Contracts.Edge.V1;
using Edge.Supervisor;
using CampoDaCatraca = Contracts.Edge.V1.CampoDaCatraca;
using ConfiguracaoDaCatraca = Contracts.Edge.V1.ConfiguracaoDaCatraca;

namespace Integration.Tests;

/// <summary>
/// Os RPCs da Parametrização por catraca (Etapa A.6 do docs/35) contra a base de verdade:
/// herda e sobrepõe com a origem certa, gravação com problema não grava, o que aguarda
/// confirmação não muda pela tela, e a versão do salvo × a aplicada.
/// </summary>
/// <remarks>Dados sintéticos; catracas de número baixo, nenhum equipamento real.</remarks>
public sealed class ParametrizacaoDaCatracaTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly BancoTemporario _banco = new();
    private readonly EdgeControlService _servico;

    private sealed class WorkerFalso(string nome, int porta, params int[] inners) : IWorkerHost
    {
        public string Nome { get; } = nome;

        public int Porta { get; } = porta;

        public IReadOnlyList<int> Inners { get; } = inners;

        public bool EstaVivo { get; private set; }

        public bool EstaSaudavel => true;

        public string Diagnostico => "ok";

        public void Iniciar() => EstaVivo = true;

        public void Matar() => EstaVivo = false;

        public void Dispose() => EstaVivo = false;
    }

    public ParametrizacaoDaCatracaTests()
    {
        _banco.Migrar();
        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1, 2)]);
        supervisor.Iniciar();
        _servico = Servico(supervisor);
    }

    public void Dispose() => _banco.Dispose();

    private EdgeControlService Servico(WorkerSupervisor supervisor) => new(
        supervisor,
        relogio: () => Agora,
        operacao: new Operacao(_banco.Fabrica),
        configuracoes: new ConfiguracoesDaBorda(_banco.Fabrica),
        configuracoesDasCatracas: new ConfiguracoesDasCatracas(_banco.Fabrica),
        configuracaoPorCatraca: new ConfiguracaoPorCatraca(_banco.Fabrica));

    private ConfiguracaoDaCatraca Obter(int inner = 1) =>
        _servico.ObterConfiguracaoDaCatraca(new ObterConfiguracaoDaCatracaRequest { Inner = inner }, null!).Result;

    private GravarConfiguracaoDaCatracaResponse Gravar(int inner, string operador, params (CampoDaCatraca Campo, string? Valor)[] valores)
    {
        var pedido = new GravarConfiguracaoDaCatracaRequest { Inner = inner, Operador = operador };
        foreach (var (campo, valor) in valores)
        {
            var item = new ValorDaCatraca { Campo = campo };
            if (valor is not null)
            {
                item.Valor = valor;
            }

            pedido.Valores.Add(item);
        }

        return _servico.GravarConfiguracaoDaCatraca(pedido, null!).Result;
    }

    private static CampoConfiguradoDaCatraca Campo(ConfiguracaoDaCatraca c, CampoDaCatraca campo) =>
        c.Campos.Single(x => x.Campo == campo);

    [Fact]
    public void Sem_camada_cada_campo_herda_com_a_origem_e_a_situacao_certas()
    {
        var c = Obter();

        Assert.Empty(c.Problemas);
        Assert.Equal(8, c.Campos.Count);
        Assert.All(c.Campos, x => Assert.False(x.HasValorDaCatraca));
        Assert.All(c.Campos, x => Assert.Equal(x.ValorHerdado, x.ValorEfetivo));

        // O evento define tipo de leitor, urna, tempo e mensagem; o resto é de fábrica.
        Assert.Equal(OrigemDoValor.Evento, Campo(c, CampoDaCatraca.TipoDeLeitor).Origem);
        Assert.Equal(OrigemDoValor.Evento, Campo(c, CampoDaCatraca.OperacaoDoLeitor2).Origem);
        Assert.Equal(OrigemDoValor.Evento, Campo(c, CampoDaCatraca.TempoDoAcionamento1).Origem);
        Assert.Equal(OrigemDoValor.Evento, Campo(c, CampoDaCatraca.MensagemPadrao).Origem);
        Assert.Equal(OrigemDoValor.Fabrica, Campo(c, CampoDaCatraca.OperacaoDoLeitor1).Origem);
        Assert.Equal(OrigemDoValor.Fabrica, Campo(c, CampoDaCatraca.FuncaoDeLiberacaoDaEntrada).Origem);
        Assert.Equal(OrigemDoValor.Fabrica, Campo(c, CampoDaCatraca.WiegandDoisLeitores).Origem);

        // Valores no formato do contrato.
        Assert.Equal("8", Campo(c, CampoDaCatraca.TipoDeLeitor).ValorEfetivo);
        Assert.Equal("5", Campo(c, CampoDaCatraca.TempoDoAcionamento1).ValorEfetivo);
        Assert.Equal("Entrada", Campo(c, CampoDaCatraca.FuncaoDeLiberacaoDaEntrada).ValorEfetivo);
        Assert.Equal("0,0", Campo(c, CampoDaCatraca.WiegandDoisLeitores).ValorEfetivo);
        Assert.Equal("0,0,7,0,0", Campo(c, CampoDaCatraca.FormasDeEntradaOnLine).ValorEfetivo);

        // Atrás de chave técnica desligada: não chega à catraca, com o ensaio que liga.
        var wiegand = Campo(c, CampoDaCatraca.WiegandDoisLeitores);
        Assert.Equal(SituacaoDoCampo.ChaveTecnicaDesligada, wiegand.Situacao);
        Assert.Contains("HIL-CARD-05", wiegand.Motivo, StringComparison.Ordinal);
        var formas = Campo(c, CampoDaCatraca.FormasDeEntradaOnLine);
        Assert.Equal(SituacaoDoCampo.ChaveTecnicaDesligada, formas.Situacao);
        Assert.Contains("INT-SM-032", formas.Motivo, StringComparison.Ordinal);

        // A função de liberação vai, mas as variantes aguardam a bancada.
        var funcao = Campo(c, CampoDaCatraca.FuncaoDeLiberacaoDaEntrada);
        Assert.Equal(SituacaoDoCampo.Enviado, funcao.Situacao);
        Assert.Equal(["EntradaInvertida", "Saida", "SaidaInvertida"], funcao.ValoresAguardando);
        Assert.Contains("HIL-DIR-05/06", funcao.MotivoDosValoresAguardando, StringComparison.Ordinal);

        // 5 × 8 continua A_CONFIRMAR: aviso, sem impedir.
        Assert.Contains("NOVO-HIL-QR-02", Campo(c, CampoDaCatraca.TipoDeLeitor).Aviso, StringComparison.Ordinal);
        Assert.Equal(string.Empty, c.AlteradaPor);
    }

    [Fact]
    public void Gravar_sobrepoe_o_evento_e_ausente_volta_a_herdar()
    {
        var r = Gravar(1, "Operadora Sintetica",
            (CampoDaCatraca.TempoDoAcionamento1, "7"),
            (CampoDaCatraca.MensagemPadrao, "Portao sintetico 2"),
            (CampoDaCatraca.OperacaoDoLeitor2, "0"));

        Assert.True(r.Gravada, string.Join(" | ", r.Problemas));
        var c = Obter();
        var tempo = Campo(c, CampoDaCatraca.TempoDoAcionamento1);
        Assert.Equal(("7", "5", "7", OrigemDoValor.Catraca), (tempo.ValorDaCatraca, tempo.ValorHerdado, tempo.ValorEfetivo, tempo.Origem));
        Assert.Equal("Portao sintetico 2", Campo(c, CampoDaCatraca.MensagemPadrao).ValorEfetivo);
        Assert.Equal("0", Campo(c, CampoDaCatraca.OperacaoDoLeitor2).ValorEfetivo);
        Assert.False(Campo(c, CampoDaCatraca.TipoDeLeitor).HasValorDaCatraca);
        Assert.Equal("Operadora Sintetica", c.AlteradaPor);
        Assert.Equal(Agora, c.AlteradaEm.ToDateTimeOffset());

        // A outra catraca não muda.
        Assert.All(Obter(2).Campos, x => Assert.False(x.HasValorDaCatraca));

        // Gravar de novo sem o tempo: a camada é trocada inteira e ele volta a herdar.
        Assert.True(Gravar(1, "Operadora Sintetica", (CampoDaCatraca.MensagemPadrao, "Portao sintetico 2")).Gravada);
        tempo = Campo(Obter(), CampoDaCatraca.TempoDoAcionamento1);
        Assert.Equal(("5", OrigemDoValor.Evento), (tempo.ValorEfetivo, tempo.Origem));
        Assert.False(tempo.HasValorDaCatraca);
    }

    [Theory]
    [InlineData(CampoDaCatraca.TempoDoAcionamento1, "0", "Operadora")]
    [InlineData(CampoDaCatraca.TempoDoAcionamento1, "51", "Operadora")]
    [InlineData(CampoDaCatraca.TempoDoAcionamento1, "sete", "Operadora")]
    [InlineData(CampoDaCatraca.TipoDeLeitor, "9", "Operadora")]
    [InlineData(CampoDaCatraca.MensagemPadrao, "Uma mensagem longa demais para o visor", "Operadora")]
    [InlineData(CampoDaCatraca.FuncaoDeLiberacaoDaEntrada, "entrada", "Operadora")]
    [InlineData(CampoDaCatraca.TempoDoAcionamento1, "7", "  ")]
    public void Gravar_com_problema_nao_grava_nada(CampoDaCatraca campo, string valor, string operador)
    {
        var r = Gravar(1, operador, (CampoDaCatraca.MensagemPadrao, "Valida"), (campo, valor));

        Assert.False(r.Gravada);
        Assert.NotEmpty(r.Problemas);
        Assert.Equal(string.Empty, r.VersaoSalva);
        Assert.All(Obter().Campos, x => Assert.False(x.HasValorDaCatraca));
        Assert.Empty(new ConfiguracoesDasCatracas(_banco.Fabrica).Historico(1));
    }

    [Fact]
    public void Campo_repetido_ou_desconhecido_volta_como_problema()
    {
        var repetido = Gravar(1, "Operadora", (CampoDaCatraca.TempoDoAcionamento1, "7"), (CampoDaCatraca.TempoDoAcionamento1, "8"));
        var desconhecido = Gravar(1, "Operadora", (CampoDaCatraca.NaoEspecificado, "7"));

        Assert.False(repetido.Gravada);
        Assert.Contains(repetido.Problemas, p => p.Contains("mais de uma vez", StringComparison.Ordinal));
        Assert.False(desconhecido.Gravada);
        Assert.Contains("Campo desconhecido.", desconhecido.Problemas);
    }

    [Fact]
    public void O_que_aguarda_confirmacao_nao_muda_pela_tela()
    {
        var wiegand = Gravar(1, "Operadora", (CampoDaCatraca.WiegandDoisLeitores, "1,0"));
        var formas = Gravar(1, "Operadora", (CampoDaCatraca.FormasDeEntradaOnLine, "0,0,3,0,0"));
        var invertida = Gravar(1, "Operadora", (CampoDaCatraca.FuncaoDeLiberacaoDaEntrada, "EntradaInvertida"));

        Assert.False(wiegand.Gravada);
        Assert.Contains(wiegand.Problemas, p => p.Contains("HIL-CARD-05", StringComparison.Ordinal));
        Assert.False(formas.Gravada);
        Assert.False(invertida.Gravada);
        Assert.Contains(invertida.Problemas, p => p.Contains("HIL-DIR-05/06", StringComparison.Ordinal));
        Assert.Empty(new ConfiguracoesDasCatracas(_banco.Fabrica).Historico(1));

        // "Entrada", a de sempre, pode.
        Assert.True(Gravar(1, "Operadora", (CampoDaCatraca.FuncaoDeLiberacaoDaEntrada, "Entrada")).Gravada);
    }

    [Fact]
    public void Valor_aguardando_ja_gravado_continua_e_nao_impede_mudar_o_resto()
    {
        // Gravada fora da tela (comissionamento), como a A.3 permite.
        var problemas = new ConfiguracoesDasCatracas(_banco.Fabrica).Gravar(
            1, new SobreposicoesDaCatraca { FuncaoDeLiberacaoDaEntrada = FuncaoDeLiberacao.EntradaInvertida }, Agora, "Instalador");
        Assert.Empty(problemas);

        var r = Gravar(1, "Operadora",
            (CampoDaCatraca.FuncaoDeLiberacaoDaEntrada, "EntradaInvertida"),
            (CampoDaCatraca.TempoDoAcionamento1, "6"));

        Assert.True(r.Gravada, string.Join(" | ", r.Problemas));
        var funcao = Campo(Obter(), CampoDaCatraca.FuncaoDeLiberacaoDaEntrada);
        Assert.Equal(("EntradaInvertida", OrigemDoValor.Catraca), (funcao.ValorDaCatraca, funcao.Origem));
    }

    [Fact]
    public void Com_a_chave_tecnica_ligada_o_campo_passa_a_ser_enviado_e_gravavel()
    {
        new ConfiguracoesDaBorda(_banco.Fabrica).Gravar(new ConfiguracaoDaOperacao(EnviarWiegandDoisLeitores: true), Agora, "Bancada");

        Assert.Equal(SituacaoDoCampo.Enviado, Campo(Obter(), CampoDaCatraca.WiegandDoisLeitores).Situacao);
        Assert.Equal(SituacaoDoCampo.ChaveTecnicaDesligada, Campo(Obter(), CampoDaCatraca.FormasDeEntradaOnLine).Situacao);

        var r = Gravar(1, "Operadora", (CampoDaCatraca.WiegandDoisLeitores, "1,0"));
        Assert.True(r.Gravada, string.Join(" | ", r.Problemas));
        Assert.Equal("1,0", Campo(Obter(), CampoDaCatraca.WiegandDoisLeitores).ValorEfetivo);
    }

    [Fact]
    public void Versao_salva_e_a_do_que_o_aplicar_enviaria_e_a_aplicada_vem_da_catraca()
    {
        var c = Obter();
        var esperada = VersaoDaConfiguracao.Calcular(new ConfiguracaoPorCatraca(_banco.Fabrica).ParaAplicar(1).Configuracao!);

        Assert.Equal(esperada, c.VersaoSalva);
        Assert.Equal(string.Empty, c.VersaoAplicada);
        Assert.Null(c.AplicadaEm);

        // O worker publica que a catraca aceitou o salvo.
        new Operacao(_banco.Fabrica).GravarSituacao(
        [
            new SituacaoDoEquipamento(
                "inner-1", 1, "setor-a", "Polling", true, "4.2.0", 0, null, null, Agora,
                ConfiguracaoAplicadaEm: Agora.AddSeconds(-30), ConfiguracaoVersao: esperada),
        ]);
        c = Obter();
        Assert.Equal(c.VersaoSalva, c.VersaoAplicada);
        Assert.Equal(Agora.AddSeconds(-30), c.AplicadaEm.ToDateTimeOffset());

        // Salvar uma mudança muda a salva; a aplicada só muda quando a catraca aceitar.
        var r = Gravar(1, "Operadora", (CampoDaCatraca.TempoDoAcionamento1, "7"));
        Assert.True(r.Gravada);
        c = Obter();
        Assert.Equal(r.VersaoSalva, c.VersaoSalva);
        Assert.NotEqual(esperada, c.VersaoSalva);
        Assert.Equal(esperada, c.VersaoAplicada);

        // Mudança que não chega à catraca (chave desligada) não muda a versão: nada a aplicar.
        // (Só gravável com a chave ligada; aqui, pelo repositório, como a bancada faria.)
        var antes = c.VersaoSalva;
        new ConfiguracoesDasCatracas(_banco.Fabrica).Gravar(
            1, new SobreposicoesDaCatraca { TempoDoAcionamento1 = 7, WiegandDoisLeitores = new WiegandDoisLeitores(true, true) }, Agora, "Bancada");
        Assert.Equal(antes, Obter().VersaoSalva);
    }

    [Fact]
    public void Salvo_que_o_worker_recusaria_volta_sem_versao_e_com_o_motivo()
    {
        // Fica válido com o evento de hoje e deixa de ser quando o evento muda (A.4).
        Assert.True(Gravar(1, "Operadora", (CampoDaCatraca.OperacaoDoLeitor2, "0")).Gravada);
        using (var conexao = _banco.Fabrica.Abrir())
        using (var comando = conexao.CreateCommand())
        {
            comando.CommandText = "INSERT INTO edge_setting (key, value, updated_at) VALUES ('catraca.acionamento_segundos', 'x', '2026-10-01T12:00:00Z');";
            comando.ExecuteNonQuery();
        }

        var c = Obter();
        Assert.Equal(string.Empty, c.VersaoSalva);
        Assert.Contains(c.Problemas, p => p.StartsWith("O salvo não pode ser aplicado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Catraca_nao_cadastrada_ou_servico_sem_base_vira_problema_e_nunca_excecao()
    {
        Assert.Contains("A catraca 9 não está cadastrada.", Obter(9).Problemas);
        Assert.Contains("A catraca 9 não está cadastrada.", Gravar(9, "Operadora", (CampoDaCatraca.TempoDoAcionamento1, "7")).Problemas);

        var supervisor = new WorkerSupervisor([new WorkerFalso("setor-a", 3570, 1)]);
        var semBase = new EdgeControlService(supervisor);
        Assert.Contains(
            "O serviço não tem base local configurada.",
            (await semBase.ObterConfiguracaoDaCatraca(new ObterConfiguracaoDaCatracaRequest { Inner = 1 }, null!)).Problemas);
        Assert.False((await semBase.GravarConfiguracaoDaCatraca(new GravarConfiguracaoDaCatracaRequest { Inner = 1 }, null!)).Gravada);
    }
}
