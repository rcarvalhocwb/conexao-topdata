using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Access.Application.Devices;

/// <summary>
/// A "versão" de uma <see cref="DeviceConfiguration"/>: o SHA-256 de uma forma canônica do que
/// a catraca recebe com ela. É o <c>configuracao_versao</c> do equipamento (Etapa A.5 do docs/35).
/// </summary>
/// <remarks>
/// <para>
/// Serve para o painel separar <b>salva</b> de <b>aplicada</b> (docs/34-anexos/04 §"Ao vivo"):
/// o worker publica a versão da configuração que a catraca <b>aceitou</b> (retorno 0 de
/// <c>EnviarConfiguracoes</c>, EI-030), e quem quiser saber se o que está salvo já chegou
/// calcula a versão do salvo com esta mesma função e compara.
/// </para>
/// <para>
/// Por que não <c>GetHashCode</c> nem a igualdade do <c>record</c>: a igualdade da
/// <see cref="DeviceConfiguration"/> compara listas por referência (duas listas com os mesmos
/// tamanhos dão "diferente"), e <c>GetHashCode</c> muda a cada execução. A forma canônica tem
/// ordem de campos fixa, listas pelo conteúdo, números em cultura invariante e texto com o
/// comprimento na frente — a mesma configuração dá a mesma versão em qualquer máquina, em
/// qualquer execução, montada em qualquer ordem.
/// </para>
/// <para>
/// <b>O que entra</b> (<see cref="CamposNaVersao"/>): só o que vai para a catraca, ou decide o
/// que vai, <b>no valor efetivo</b>. Um campo atrás de chave técnica desligada entra como "não
/// enviado" ("-"), e não pelo valor do modelo: mudar um valor que não chega à catraca não muda a
/// versão, e ligar a chave muda. As formas de entrada entram como o adapter as manda (as de
/// sempre, com a chave desligada). Os tamanhos variáveis de dígitos entram na ordem em que são
/// enviados (sem repetição, como o adapter): se a ordem das chamadas importa para a catraca não
/// está documentado (T30), e na dúvida a versão diz "diferente" — o erro barato (o operador
/// aplica de novo) — e nunca "igual" sobre o que foi mandado diferente.
/// </para>
/// <para>
/// <b>O que fica fora</b> (<see cref="CamposForaDaVersao"/>, com o motivo de cada um): o que não
/// é enviado (WebServer e mensagens de apresentação e off-line, sem linha na matriz; o modo de
/// dígitos, que só valida) e o <b>número</b> do cartão master. O master é credencial (FUN:24,
/// docs/34 §6): um hash sem segredo de um número de até 14 dígitos se inverte por força bruta
/// em segundos, e a versão vai para a base, o contrato e a tela. Entra só se ele existe ou não
/// (isso decide se <c>DefinirNumeroCartaoMaster</c> é chamada); trocar um master por outro não
/// muda a versão — limitação registrada no docs/34 §11. HMAC com a chave local permitiria
/// incluí-lo, mas a chave mora no cofre do serviço e o worker não a tem (PROPOSTA FUTURA, junto
/// com a custódia SEC-MASTER-01).
/// </para>
/// <para>
/// A primeira linha da forma canônica é o nome e a versão do próprio formato: mudar o formato
/// muda todas as versões (o painel passa a mostrar "diferente" até a próxima aplicação), nunca
/// faz duas configurações diferentes parecerem iguais.
/// </para>
/// </remarks>
public static class VersaoDaConfiguracao
{
    /// <summary>Primeira linha da forma canônica.</summary>
    public const string Formato = "xacess.configuracao-da-catraca.v1";

    /// <summary>Marca de "não enviado" (campo atrás de chave técnica desligada, ou nulo).</summary>
    public const string NaoEnviado = "-";

    /// <summary>Comprimento da versão: SHA-256 em hexadecimal minúsculo.</summary>
    public const int Comprimento = 64;

    /// <summary>Propriedades da <see cref="DeviceConfiguration"/> que entram na versão.</summary>
    public static IReadOnlySet<string> CamposNaVersao { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(DeviceConfiguration.PadraoCartao),
        nameof(DeviceConfiguration.QuantidadeFixaDeDigitos),
        nameof(DeviceConfiguration.QuantidadesVariaveisDeDigitos),
        nameof(DeviceConfiguration.EnviarDigitosVariaveis),
        nameof(DeviceConfiguration.TipoDeLeitor),
        nameof(DeviceConfiguration.OperacaoDoLeitor1),
        nameof(DeviceConfiguration.OperacaoDoLeitor2),
        nameof(DeviceConfiguration.FuncaoDoAcionamento1),
        nameof(DeviceConfiguration.TempoDoAcionamento1),
        nameof(DeviceConfiguration.FuncaoDoAcionamento2),
        nameof(DeviceConfiguration.TempoDoAcionamento2),
        nameof(DeviceConfiguration.Online),
        nameof(DeviceConfiguration.TecladoHabilitado),
        nameof(DeviceConfiguration.EcoDoTeclado),
        nameof(DeviceConfiguration.MudancaAutomatica),
        nameof(DeviceConfiguration.TempoDaMudancaAutomatica),
        nameof(DeviceConfiguration.MensagemPadrao),
        nameof(DeviceConfiguration.PerfilFisico),
        nameof(DeviceConfiguration.DataHoraNoEventoOnLine),
        nameof(DeviceConfiguration.EnviarDataHoraNoEventoOnLine),
        nameof(DeviceConfiguration.RegistrarAcessoNegado),
        nameof(DeviceConfiguration.TipoDeLista),
        nameof(DeviceConfiguration.EnviarTipoDeLista),
        nameof(DeviceConfiguration.WiegandDoisLeitores),
        nameof(DeviceConfiguration.EnviarWiegandDoisLeitores),
        nameof(DeviceConfiguration.FormasDeEntradaOnLine),
        nameof(DeviceConfiguration.EnviarFormasDeEntradaOnLine),
        nameof(DeviceConfiguration.CartaoMaster),
    };

    /// <summary>Propriedades que ficam fora da versão, e por quê.</summary>
    public static IReadOnlyDictionary<string, string> CamposForaDaVersao { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(DeviceConfiguration.ModoDeDigitos)] =
            "não é enviado: só liga as regras 1 e 2 de Validar(); o que vai são a quantidade fixa e os tamanhos variáveis, que entram",
        [nameof(DeviceConfiguration.WebServerDesabilitado)] = "não é enviado (sem linha na matriz FUN; T32, NOVO-SEC-WEB-01)",
        [nameof(DeviceConfiguration.MensagemDeApresentacaoDaEntrada)] = "não é enviada (sem linha na matriz FUN; NOVO-INT-MSG-03)",
        [nameof(DeviceConfiguration.MensagemDeApresentacaoDaSaida)] = "não é enviada (sem linha na matriz FUN; NOVO-INT-MSG-03)",
        [nameof(DeviceConfiguration.MensagemPadraoOffLine)] = "não é enviada (passo da sequência oficial, Etapa A.7; NOVO-INT-MSG-04)",
        [nameof(DeviceConfiguration.MensagemDeEntradaOffLine)] = "não é enviada (passo da sequência oficial, Etapa A.7; NOVO-INT-MSG-04)",
        [nameof(DeviceConfiguration.MensagemDeSaidaOffLine)] = "não é enviada (passo da sequência oficial, Etapa A.7; NOVO-INT-MSG-04)",
    };

    /// <summary>A versão: SHA-256 da forma canônica, em hexadecimal minúsculo (64 caracteres).</summary>
    /// <param name="configuracao">A configuração, como vai para a catraca.</param>
    public static string Calcular(DeviceConfiguration configuracao)
    {
        var bytes = Encoding.UTF8.GetBytes(FormaCanonica(configuracao));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// A forma canônica: uma linha por campo, em ordem fixa, só com o que vai para a catraca.
    /// Nunca leva o número do cartão master.
    /// </summary>
    /// <param name="configuracao">A configuração.</param>
    public static string FormaCanonica(DeviceConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        var c = configuracao;
        var texto = new StringBuilder(512);

        void Linha(string campo, string valor) => texto.Append(campo).Append('=').Append(valor).Append('\n');

        texto.Append(Formato).Append('\n');

        // Ordem do envio em TopdataInnerAdapter.EnviarConfiguracaoCompleta.
        Linha("padrao_cartao", N(c.PadraoCartao));
        Linha("quantidade_fixa_de_digitos", c.QuantidadeFixaDeDigitos is { } fixa ? N(fixa) : NaoEnviado);

        // Só com a chave catraca.enviar_digitos_variaveis, e cada tamanho uma vez, na ordem.
        Linha("digitos_variaveis", c.EnviarDigitosVariaveis
            ? string.Join(',', c.QuantidadesVariaveisDeDigitos.Distinct().Select(N))
            : NaoEnviado);

        Linha("tipo_de_leitor", N(c.TipoDeLeitor));
        Linha("leitor1", N(c.OperacaoDoLeitor1));
        Linha("leitor2", N(c.OperacaoDoLeitor2));
        Linha("acionamento1", N(c.FuncaoDoAcionamento1) + "," + N(c.TempoDoAcionamento1));
        Linha("acionamento2", N(c.FuncaoDoAcionamento2) + "," + N(c.TempoDoAcionamento2));
        Linha("regime", c.Online ? "online" : "offline");
        Linha("teclado", B(c.TecladoHabilitado) + "," + N(c.EcoDoTeclado));
        Linha("mudanca_automatica", N(c.MudancaAutomatica) + "," + N(c.TempoDaMudancaAutomatica));

        // Etapa A.2: cada um só com a sua chave técnica; desligada, "não enviado".
        Linha("wiegand_dois_leitores", c.EnviarWiegandDoisLeitores
            ? B(c.WiegandDoisLeitores.Habilitado) + "," + B(c.WiegandDoisLeitores.ExibirMensagem)
            : NaoEnviado);
        Linha("registrar_acesso_negado", c.RegistrarAcessoNegado is { } negado ? N(negado) : NaoEnviado);
        Linha("data_hora_no_evento_online", c.EnviarDataHoraNoEventoOnLine ? B(c.DataHoraNoEventoOnLine) : NaoEnviado);

        // Só se existe: o número fica fora (ver o resumo da classe).
        Linha("cartao_master", c.CartaoMaster is null ? NaoEnviado : "definido");
        Linha("tipo_de_lista", c.EnviarTipoDeLista ? N(c.TipoDeLista) : NaoEnviado);

        // Depois de EnviarConfiguracoes, nos passos próprios do laço: o rearme
        // (EnviarFormasEntradasOnLine, EI-032) e a mensagem padrão (EnviarMensagemPadraoOnLine).
        var formas = c.EnviarFormasDeEntradaOnLine ? c.FormasDeEntradaOnLine : FormasDeEntradaOnLine.DeHoje;
        Linha("formas_de_entrada_online", string.Join(',', new[]
        {
            formas.QtdeDigitosTeclado, formas.EcoTeclado, formas.FormaEntrada, formas.TempoTeclado, formas.PosicaoCursorTeclado,
        }.Select(N)));
        Linha("mensagem_padrao", Texto(c.MensagemPadrao));

        // Não vai no buffer, mas decide qual função libera o giro em cada passagem (F1).
        Linha("liberacao_da_entrada", c.PerfilFisico.FuncaoDeLiberacaoDaEntrada.ToString());

        // Mapa de giro (D9, docs/34 §9): também decide a função de cada passagem e vai à catraca
        // pelo "Aplicar agora". Só aparece quando há regra: com o mapa vazio a forma canônica é
        // byte a byte a de antes, e nenhuma versão já publicada muda.
        if (!c.PerfilFisico.MapaDeGiro.EstaVazio)
        {
            Linha("mapa_de_giro", string.Join(';', MapaDeGiro.Origens.Select(origem =>
                c.PerfilFisico.MapaDeGiro.Regra(origem) is { } regra
                    ? $"{origem}:{regra.Funcao?.ToString() ?? "perfil"},{regra.ContaComo},{(regra.Texto is { } t ? Texto(t) : NaoEnviado)}"
                    : $"{origem}:{NaoEnviado}")));
        }

        return texto.ToString();
    }

    private static string N(byte valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string B(bool valor) => valor ? "1" : "0";

    // Comprimento na frente: nenhum texto (com '=', vírgula ou quebra de linha) se confunde
    // com o campo seguinte.
    private static string Texto(string valor) =>
        string.Create(CultureInfo.InvariantCulture, $"{valor.Length}:{valor}");
}
