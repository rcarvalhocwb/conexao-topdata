using Contracts.Edge.V1;

namespace Desktop.ViewModels.GemeoDigital;

/// <summary>Um pedido imediato à catraca que o painel de uma peça oferece (os da "Gerenciar catraca").</summary>
public enum ComandoDaPeca
{
    /// <summary>Mensagem temporária no display (até 32 letras, 1 a 60 s).</summary>
    MensagemTemporaria,

    /// <summary>Acertar o relógio da catraca pelo da borda.</summary>
    AcertarRelogio,

    /// <summary>Desconectar e conectar de novo.</summary>
    RefazerConexao,
}

/// <summary>As duas marcações do desenho 3D sobre uma peça.</summary>
public enum TipoDeMarca
{
    /// <summary>Há alteração na tela, ainda não salva, num parâmetro desta peça.</summary>
    AlteracaoNaoSalva,

    /// <summary>Esta catraca usa, nesta peça, um valor diferente do padrão do evento.</summary>
    DiferenteDoEvento,
}

/// <summary>Uma marcação de peça, para o desenho e para a lista em texto (não só cor).</summary>
/// <param name="Peca">A peça marcada.</param>
/// <param name="Tipo">Qual das duas marcações.</param>
/// <param name="Texto">O que a marcação quer dizer, em palavras, com o nome da peça.</param>
public sealed record MarcaDaPeca(PecaDaCatraca Peca, TipoDeMarca Tipo, string Texto)
{
    /// <summary>A marcação de alteração não salva (a outra é "diferente do padrão do evento").</summary>
    public bool NaoSalva => Tipo is TipoDeMarca.AlteracaoNaoSalva;

    /// <summary>O mesmo símbolo do desenho: bola = não salva; quadrado = diferente do evento.</summary>
    public string Simbolo => NaoSalva ? "●" : "■";
}

/// <summary>
/// O que o painel de configuração de uma peça mostra: os campos da Parametrização (Etapa A.6),
/// a parte do mapa de giro (D9) e os pedidos imediatos da "Gerenciar catraca" que tratam
/// daquela peça.
/// </summary>
/// <param name="Peca">A peça.</param>
/// <param name="Titulo">O nome do painel.</param>
/// <param name="Campos">Os campos da catraca desta peça, na ordem da tela (os técnicos só aparecem no modo técnico).</param>
/// <param name="MostraGiro">Mostra o mapa de giro.</param>
/// <param name="OrigemDoGiro">Só a linha desta origem do mapa; nulo = o mapa inteiro.</param>
/// <param name="Comandos">Pedidos imediatos à catraca (não são configuração: não passam por Salvar/Aplicar).</param>
/// <param name="MostraRele2">Linha do relé 2, sempre desabilitada (aguarda ensaio), só no modo técnico.</param>
/// <param name="MostraEquipamento">Firmware e relógio da catraca, como o serviço os conhece.</param>
public sealed record PainelDaPeca(
    PecaDaCatraca Peca,
    string Titulo,
    IReadOnlyList<CampoDaCatraca> Campos,
    bool MostraGiro,
    OrigemDoGiro? OrigemDoGiro,
    IReadOnlyList<ComandoDaPeca> Comandos,
    bool MostraRele2,
    bool MostraEquipamento)
{
    /// <summary>A peça não tem nada que o sistema configure nem peça: só a ficha.</summary>
    public bool NadaAConfigurar => Campos.Count == 0 && !MostraGiro && Comandos.Count == 0 && !MostraRele2 && !MostraEquipamento;
}

/// <summary>
/// Peça → parâmetros: onde cada campo da catraca aparece no gêmeo, que é a porta principal da
/// configuração da catraca (docs/33 §9).
/// </summary>
/// <remarks>
/// <para>
/// Nenhum campo é inventado aqui: são os mesmos <see cref="CampoDaCatraca"/> da Parametrização,
/// gravados e aplicados pelos mesmos RPCs, com a mesma origem e a mesma situação vindas do
/// serviço. O que aguarda confirmação continua desabilitado, com o selo e o motivo.
/// </para>
/// <list type="bullet">
/// <item>Leitor de QR e leitor de cartão da frente: os dois são o leitor 1 (origens 2 e 21) —
/// tipo de leitor (5 × 8 a confirmar, NOVO-HIL-QR-02) e operação do leitor 1 (técnico).</item>
/// <item>Urna: o leitor 2 (ligado ou desligado) e a linha "leitor 2" do mapa de giro.</item>
/// <item>Braços: tempo de liberação (relé 1), função de liberação (técnico) e o mapa de giro inteiro.</item>
/// <item>Display: a mensagem padrão (configuração) e a mensagem temporária (pedido imediato).</item>
/// <item>Coluna, onde fica a placa de controle (ficha da peça): Wiegand e formas de entrada
/// (técnico, desabilitados atrás de chave), relé 2 (desabilitado, aguardando ensaio),
/// firmware e relógio, com "acertar relógio" e "refazer conexão".</item>
/// </list>
/// </remarks>
public static class ConfiguracaoPorPeca
{
    private static readonly Dictionary<PecaDaCatraca, PainelDaPeca> Paineis = Montar();

    /// <summary>O painel de uma peça; peça sem parâmetro tem painel vazio (só a ficha).</summary>
    public static PainelDaPeca De(PecaDaCatraca peca) =>
        Paineis.TryGetValue(peca, out var painel)
            ? painel
            : new PainelDaPeca(peca, CatalogoDaFit4.De(peca).Nome, [], false, null, [], false, false);

    /// <summary>As peças em que cada campo aparece (um campo pode estar em mais de uma).</summary>
    public static IReadOnlyList<PecaDaCatraca> PecasDoCampo(CampoDaCatraca campo) =>
        [.. Paineis.Values.Where(p => p.Campos.Contains(campo)).Select(p => p.Peca)];

    /// <summary>As peças em que uma linha do mapa de giro aparece.</summary>
    public static IReadOnlyList<PecaDaCatraca> PecasDaOrigem(OrigemDoGiro origem) =>
        [.. Paineis.Values.Where(p => p.MostraGiro && (p.OrigemDoGiro is null || p.OrigemDoGiro == origem)).Select(p => p.Peca)];

    private static Dictionary<PecaDaCatraca, PainelDaPeca> Montar()
    {
        CampoDaCatraca[] leitorDaFrente = [CampoDaCatraca.TipoDeLeitor, CampoDaCatraca.OperacaoDoLeitor1];

        var lista = new[]
        {
            new PainelDaPeca(PecaDaCatraca.LeitorQr, "Leitor da frente (QR)", leitorDaFrente, false, null, [], false, false),
            new PainelDaPeca(PecaDaCatraca.LeitorDeProximidade, "Leitor da frente (cartão)", leitorDaFrente, false, null, [], false, false),
            new PainelDaPeca(
                PecaDaCatraca.Urna, "Urna (leitor 2)", [CampoDaCatraca.OperacaoDoLeitor2], true, OrigemDoGiro.Leitor2, [], false, false),
            new PainelDaPeca(
                PecaDaCatraca.Rotor,
                "Braços e giro",
                [CampoDaCatraca.TempoDoAcionamento1, CampoDaCatraca.FuncaoDeLiberacaoDaEntrada],
                true,
                null,
                [],
                false,
                false),
            new PainelDaPeca(
                PecaDaCatraca.Display, "Display", [CampoDaCatraca.MensagemPadrao], false, null, [ComandoDaPeca.MensagemTemporaria], false, false),
            new PainelDaPeca(
                PecaDaCatraca.Coluna,
                "Placa de controle (dentro da coluna)",
                [CampoDaCatraca.WiegandDoisLeitores, CampoDaCatraca.FormasDeEntradaOnLine],
                false,
                null,
                [ComandoDaPeca.AcertarRelogio, ComandoDaPeca.RefazerConexao],
                true,
                true),
        };

        return lista.ToDictionary(p => p.Peca);
    }
}
