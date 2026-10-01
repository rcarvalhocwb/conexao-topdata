using System.Globalization;
using Access.Application.Devices;
using Contracts.Edge.V1;

namespace Edge.Supervisor;

/// <summary>
/// Tradução, campo a campo, entre a camada da catraca (<see cref="SobreposicoesDaCatraca"/>) e
/// os campos de texto do contrato da Parametrização (<see cref="CampoDaCatraca"/>).
/// </summary>
/// <remarks>
/// <para>
/// Etapa A.6 do docs/35. O contrato leva cada valor como texto num formato fixo (descrito no
/// <c>edge_control.proto</c>, junto do enum <c>CampoDaCatraca</c>) para que a tela trate os oito
/// campos do mesmo jeito: atual → novo, herda, origem, situação. A validação de faixas e das
/// regras entre campos continua num lugar só, <see cref="DeviceConfiguration.Validar"/>, chamado
/// pela gravação (<c>ConfiguracoesDasCatracas.Gravar</c>); aqui só se recusa o que não é
/// legível como o tipo do campo.
/// </para>
/// <para>
/// A situação de cada campo vem das chaves técnicas do evento e da matriz FUN: o que não chega
/// à catraca, ou cujo comportamento não está confirmado, a tela mostra desabilitado, com o selo
/// "Aguardando confirmação" e o motivo (regra inegociável do docs/35: nada parece existir sem
/// existir).
/// </para>
/// </remarks>
internal static class CamposDaParametrizacao
{
    /// <summary>Os campos, na ordem em que a tela os apresenta.</summary>
    public static IReadOnlyList<CampoDaCatraca> Todos { get; } =
    [
        CampoDaCatraca.TipoDeLeitor,
        CampoDaCatraca.OperacaoDoLeitor1,
        CampoDaCatraca.OperacaoDoLeitor2,
        CampoDaCatraca.TempoDoAcionamento1,
        CampoDaCatraca.FuncaoDeLiberacaoDaEntrada,
        CampoDaCatraca.MensagemPadrao,
        CampoDaCatraca.WiegandDoisLeitores,
        CampoDaCatraca.FormasDeEntradaOnLine,
    ];

    /// <summary>
    /// As variantes da função de liberação que não podem ser escolhidas pela tela. Qual delas
    /// serve à catraca instalada ao contrário só a bancada diz (HIL-DIR-05/06, docs/34 §2, F1):
    /// até lá, só <see cref="FuncaoDeLiberacao.Entrada"/>, a de sempre.
    /// </summary>
    private static readonly string[] FuncoesAguardando =
    [
        nameof(FuncaoDeLiberacao.EntradaInvertida),
        nameof(FuncaoDeLiberacao.Saida),
        nameof(FuncaoDeLiberacao.SaidaInvertida),
    ];

    /// <summary>
    /// Operações dos leitores 1 e 2 (FUN:15/16) que leem na saída: 2 somente saída, 3 entrada e
    /// saída, 4 entrada e saída invertida. O produto só decide e libera entrada, e saída e dois
    /// sentidos estão fora até a bancada e a decisão B4 (docs/32; docs/34-anexos/04 §3.1).
    /// </summary>
    private static readonly string[] LeiturasNaSaida = ["2", "3", "4"];

    /// <summary>Nome do campo nos problemas devolvidos à tela.</summary>
    public static string Rotulo(CampoDaCatraca campo) => campo switch
    {
        CampoDaCatraca.TipoDeLeitor => "Tipo de leitor",
        CampoDaCatraca.OperacaoDoLeitor1 => "Leitor da frente",
        CampoDaCatraca.OperacaoDoLeitor2 => "Leitor da urna",
        CampoDaCatraca.TempoDoAcionamento1 => "Tempo de liberação",
        CampoDaCatraca.FuncaoDeLiberacaoDaEntrada => "Função de liberação da entrada",
        CampoDaCatraca.MensagemPadrao => "Mensagem do display",
        CampoDaCatraca.WiegandDoisLeitores => "Wiegand com dois leitores",
        CampoDaCatraca.FormasDeEntradaOnLine => "Formas de entrada",
        _ => "Campo desconhecido",
    };

    /// <summary>O valor que a camada da catraca define para o campo; nulo = herda.</summary>
    public static string? DaCamada(CampoDaCatraca campo, SobreposicoesDaCatraca camada) => campo switch
    {
        CampoDaCatraca.TipoDeLeitor => Numero(camada.TipoDeLeitor),
        CampoDaCatraca.OperacaoDoLeitor1 => Numero(camada.OperacaoDoLeitor1),
        CampoDaCatraca.OperacaoDoLeitor2 => Numero(camada.OperacaoDoLeitor2),
        CampoDaCatraca.TempoDoAcionamento1 => Numero(camada.TempoDoAcionamento1),
        CampoDaCatraca.FuncaoDeLiberacaoDaEntrada => camada.FuncaoDeLiberacaoDaEntrada?.ToString(),
        CampoDaCatraca.MensagemPadrao => camada.MensagemPadrao,
        CampoDaCatraca.WiegandDoisLeitores => camada.WiegandDoisLeitores is { } w ? Texto(w) : null,
        CampoDaCatraca.FormasDeEntradaOnLine => camada.FormasDeEntradaOnLine is { } f ? Texto(f) : null,
        _ => null,
    };

    /// <summary>O valor do campo numa configuração montada.</summary>
    public static string DaConfiguracao(CampoDaCatraca campo, DeviceConfiguration configuracao) => campo switch
    {
        CampoDaCatraca.TipoDeLeitor => Numero(configuracao.TipoDeLeitor)!,
        CampoDaCatraca.OperacaoDoLeitor1 => Numero(configuracao.OperacaoDoLeitor1)!,
        CampoDaCatraca.OperacaoDoLeitor2 => Numero(configuracao.OperacaoDoLeitor2)!,
        CampoDaCatraca.TempoDoAcionamento1 => Numero(configuracao.TempoDoAcionamento1)!,
        CampoDaCatraca.FuncaoDeLiberacaoDaEntrada => configuracao.PerfilFisico.FuncaoDeLiberacaoDaEntrada.ToString(),
        CampoDaCatraca.MensagemPadrao => configuracao.MensagemPadrao,
        CampoDaCatraca.WiegandDoisLeitores => Texto(configuracao.WiegandDoisLeitores),
        CampoDaCatraca.FormasDeEntradaOnLine => Texto(configuracao.FormasDeEntradaOnLine),
        _ => string.Empty,
    };

    /// <summary>
    /// De onde vem o valor efetivo: a camada mais alta que o define. O evento define tipo de
    /// leitor, leitor 2 (pela urna), tempo do relé 1 e mensagem (<c>ConfiguracaoDaOperacao.ParaACatraca</c>);
    /// o resto, sem sobreposição da catraca, é o padrão de fábrica.
    /// </summary>
    public static OrigemDoValor Origem(CampoDaCatraca campo, SobreposicoesDoEvento evento, SobreposicoesDaCatraca camada)
    {
        if (DaCamada(campo, camada) is not null)
        {
            return OrigemDoValor.Catraca;
        }

        var doEvento = campo switch
        {
            CampoDaCatraca.TipoDeLeitor => evento.TipoDeLeitor is not null,
            CampoDaCatraca.OperacaoDoLeitor2 => evento.LeitorDaUrna is not null,
            CampoDaCatraca.TempoDoAcionamento1 => evento.TempoDeAcionamento is not null,
            CampoDaCatraca.MensagemPadrao => evento.MensagemPadrao is not null,
            _ => false,
        };

        return doEvento ? OrigemDoValor.Evento : OrigemDoValor.Fabrica;
    }

    /// <summary>
    /// Se o campo chega à catraca com a configuração efetiva (as chaves técnicas são do
    /// evento), e o motivo quando não chega.
    /// </summary>
    public static (SituacaoDoCampo Situacao, string Motivo) Situacao(CampoDaCatraca campo, DeviceConfiguration efetiva) => campo switch
    {
        CampoDaCatraca.WiegandDoisLeitores when !efetiva.EnviarWiegandDoisLeitores => (
            SituacaoDoCampo.ChaveTecnicaDesligada,
            "Não chega à catraca: a chave técnica catraca.enviar_wiegand_dois_leitores do evento está desligada " +
            "até o ensaio HIL-CARD-05 (EI-024, docs/21 §6C). Enquanto isso, a catraca segue com o padrão dela."),
        CampoDaCatraca.FormasDeEntradaOnLine when !efetiva.EnviarFormasDeEntradaOnLine => (
            SituacaoDoCampo.ChaveTecnicaDesligada,
            "Não chega à catraca: a chave técnica catraca.enviar_formas_de_entrada do evento está desligada até o " +
            "ensaio INT-SM-032 (EI-032, T26, docs/21 §6C). O rearme do leitor segue com os valores de sempre (0,0,7,0,0)."),
        _ => (SituacaoDoCampo.Enviado, string.Empty),
    };

    /// <summary>Valores que a tela mostra e não deixa escolher, com o motivo.</summary>
    public static (IReadOnlyList<string> Valores, string Motivo) Aguardando(CampoDaCatraca campo) => campo switch
    {
        CampoDaCatraca.FuncaoDeLiberacaoDaEntrada => (
            FuncoesAguardando,
            "Qual função libera a entrada numa catraca instalada ao contrário só se sabe na bancada " +
            "(HIL-DIR-05/06, EI-042 a EI-044; docs/34 §2, F1). Até lá, só \"Entrada\" pode ser escolhida."),
        CampoDaCatraca.OperacaoDoLeitor1 or CampoDaCatraca.OperacaoDoLeitor2 => (
            LeiturasNaSaida,
            "Ler na saída: o sistema só decide e libera entrada; saída e dois sentidos aguardam a bancada " +
            "(HIL-DIR-01/02, EI-014/EI-015) e a decisão sobre evacuação (B4, docs/32)."),
        _ => ([], string.Empty),
    };

    /// <summary>Aviso que não impede a escolha.</summary>
    public static string Aviso(CampoDaCatraca campo) => campo switch
    {
        CampoDaCatraca.TipoDeLeitor =>
            "Tipo 5 (barras serial) ou 8 (QR Code por letras) para o QR da TopFit 4: aguardando confirmação " +
            "(NOVO-HIL-QR-02, T25; EI-013). Na bancada, 8; troque para 5 se o QR não for lido.",
        _ => string.Empty,
    };

    /// <summary>
    /// Põe o valor pedido para o campo na camada. Valor ilegível vira problema e a camada não
    /// muda; a faixa e as regras entre campos ficam com quem grava.
    /// </summary>
    public static SobreposicoesDaCatraca Aplicar(
        SobreposicoesDaCatraca camada, CampoDaCatraca campo, string? valor, List<string> problemas)
    {
        if (valor is null)
        {
            return camada;
        }

        var rotulo = Rotulo(campo);

        byte? Byte()
        {
            if (byte.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
            {
                return numero;
            }

            problemas.Add($"{rotulo}: \"{valor}\" não é um número de 0 a 255.");
            return null;
        }

        byte[]? Bytes(int quantos)
        {
            var partes = valor.Split(',');
            var numeros = new byte[partes.Length];
            for (var i = 0; i < partes.Length; i++)
            {
                if (!byte.TryParse(partes[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out numeros[i]))
                {
                    numeros = [];
                    break;
                }
            }

            if (numeros.Length != quantos)
            {
                problemas.Add($"{rotulo}: informe {quantos} números de 0 a 255 separados por vírgula.");
                return null;
            }

            return numeros;
        }

        switch (campo)
        {
            case CampoDaCatraca.TipoDeLeitor:
                return Byte() is { } tipo ? camada with { TipoDeLeitor = tipo } : camada;
            case CampoDaCatraca.OperacaoDoLeitor1:
                return Byte() is { } leitor1 ? camada with { OperacaoDoLeitor1 = leitor1 } : camada;
            case CampoDaCatraca.OperacaoDoLeitor2:
                return Byte() is { } leitor2 ? camada with { OperacaoDoLeitor2 = leitor2 } : camada;
            case CampoDaCatraca.TempoDoAcionamento1:
                return Byte() is { } tempo ? camada with { TempoDoAcionamento1 = tempo } : camada;
            case CampoDaCatraca.FuncaoDeLiberacaoDaEntrada:
                // Só o nome exato, como a leitura da base (ConfiguracoesDasCatracas).
                if (Enum.GetNames<FuncaoDeLiberacao>().Contains(valor, StringComparer.Ordinal))
                {
                    return camada with { FuncaoDeLiberacaoDaEntrada = Enum.Parse<FuncaoDeLiberacao>(valor) };
                }

                problemas.Add($"{rotulo}: \"{valor}\" não é uma função conhecida.");
                return camada;
            case CampoDaCatraca.MensagemPadrao:
                return camada with { MensagemPadrao = valor };
            case CampoDaCatraca.WiegandDoisLeitores:
                if (Bytes(2) is { } par)
                {
                    if (par.Any(v => v > 1))
                    {
                        problemas.Add($"{rotulo}: cada valor é 0 ou 1.");
                        return camada;
                    }

                    return camada with { WiegandDoisLeitores = new WiegandDoisLeitores(par[0] == 1, par[1] == 1) };
                }

                return camada;
            case CampoDaCatraca.FormasDeEntradaOnLine:
                return Bytes(5) is { } f
                    ? camada with { FormasDeEntradaOnLine = new FormasDeEntradaOnLine(f[0], f[1], f[2], f[3], f[4]) }
                    : camada;
            default:
                problemas.Add("Campo desconhecido.");
                return camada;
        }
    }

    private static string? Numero(byte? valor) => valor?.ToString(CultureInfo.InvariantCulture);

    private static string Texto(WiegandDoisLeitores w) =>
        string.Create(CultureInfo.InvariantCulture, $"{(w.Habilitado ? 1 : 0)},{(w.ExibirMensagem ? 1 : 0)}");

    private static string Texto(FormasDeEntradaOnLine f) => string.Create(
        CultureInfo.InvariantCulture,
        $"{f.QtdeDigitosTeclado},{f.EcoTeclado},{f.FormaEntrada},{f.TempoTeclado},{f.PosicaoCursorTeclado}");
}
