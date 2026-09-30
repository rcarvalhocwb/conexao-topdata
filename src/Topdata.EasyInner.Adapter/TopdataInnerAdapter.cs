using System.Diagnostics;
using Access.Application.Devices;
using Access.Domain.Devices;
using Access.Domain.Tempo;
using Topdata.EasyInner.Interop;

namespace Topdata.EasyInner.Adapter;

/// <summary>
/// Traduz <see cref="ITopdataInnerAdapter"/> para as chamadas da EasyInner.dll.
/// </summary>
/// <remarks>
/// <para>
/// Substitui a <c>VinculacaoNativaPendente</c>, que recusava toda chamada enquanto as
/// assinaturas não fossem conhecidas. Elas chegaram com o SDK 6.0.2.0, em 24/09/2026.
/// </para>
/// <para>
/// <b>Nada aqui foi executado contra hardware.</b> A tradução é testada contra uma costura
/// falsa; o que só a bancada responde está marcado com <c>A_CONFIRMAR</c> nos comentários.
/// </para>
/// <para>
/// Uma instância por worker, e uma única thread por instância: a DLL não é thread-safe e
/// todas as chamadas bloqueiam.
/// </para>
/// </remarks>
public sealed class TopdataInnerAdapter : ITopdataInnerAdapter
{
    /// <summary>TCP com porta fixa — o modo em que o equipamento conecta em nós.</summary>
    private const byte ConexaoTcpPortaFixa = 2;

    /// <summary>Teclado e os dois leitores aceitos. Enum FormaEntrada do SDK.</summary>
    private const byte EntradaTecladoELeitores = 7;

    /// <summary>
    /// O rearme de sempre: (0, 0, 7, 0, 0). Fica aqui, e não só no modelo, para que a chave
    /// desligada mande isto mesmo que alguém mude o padrão do modelo.
    /// </summary>
    private static readonly FormasDeEntradaOnLine FormasDeEntradaDeSempre = new(0, 0, EntradaTecladoELeitores, 0, 0);

    private readonly IEasyInnerNative _nativo;

    /// <summary>
    /// Identifica esta sessão do adapter, para a chave de deduplicação.
    /// </summary>
    /// <remarks>
    /// A DLL não numera eventos nem expõe um contador de boot do equipamento. Esta é a
    /// aproximação possível: eventos da mesma sessão são distinguíveis entre si. Reiniciar
    /// o worker gera um identificador novo, que é o comportamento desejado — a sequência
    /// recomeça e não colide com a anterior.
    /// </remarks>
    private readonly string _bootId = Guid.CreateVersion7().ToString("N")[..12];

    private long _sequencia;
    private long _errosDeRecepcao;
    private bool _portaAberta;
    private bool _descartado;

    public TopdataInnerAdapter(IEasyInnerNative? nativo = null)
    {
        _nativo = nativo ?? new EasyInnerReal();
    }

    /// <summary>
    /// Trata o retorno ≠ 0 (exceto o 8) de <c>ReceberDadosOnLine</c> como erro, que o laço
    /// leva à reconexão. Desligado por padrão.
    /// </summary>
    /// <remarks>
    /// Desligado, o retorno ≠ 0 é contado (<see cref="ErrosDeRecepcao"/>) e devolvido como
    /// "sem eventos" com o bruto preservado — o que a DLL real sempre teve aqui, agora com
    /// rastro. O manual não diz o que a DLL devolve quando simplesmente não há evento (T2,
    /// docs/34 §10): se for ≠ 0, reconectar a cada volta deixaria a catraca reconectando sem
    /// parar e perderia o giro de quem está passando. <c>A_CONFIRMAR_COM_TOPDATA</c> até o
    /// ensaio HIL-EVT-01; liga pela chave técnica <c>catraca.reconectar_em_erro_de_recepcao</c>.
    /// O 8 (falha de dependência) é fatal com ou sem esta opção.
    /// </remarks>
    public bool ReconectarEmErroDeRecepcao { get; set; }

    /// <summary>
    /// Quantas vezes <c>ReceberDadosOnLine</c> devolveu retorno diferente de zero, em todas as
    /// catracas desta instância.
    /// </summary>
    /// <remarks>
    /// Antes esses retornos viravam "sem eventos" e ninguém os via (F6, docs/34 §2). Contar
    /// é o mínimo da ADR-0018; a contagem por catraca está em <c>DeviceSlot.ErrosDeRecepcao</c>.
    /// </remarks>
    public long ErrosDeRecepcao => Interlocked.Read(ref _errosDeRecepcao);

    public AdapterResult AbrirPorta(int porta)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);

        return Medir(() =>
        {
            // O tipo de conexão precisa vir antes de abrir a porta (manual 3.3.1).
            var tipo = _nativo.DefinirTipoConexao(ConexaoTcpPortaFixa);
            if (tipo != 0)
            {
                return (nameof(IEasyInnerNative.DefinirTipoConexao), tipo);
            }

            var abertura = _nativo.AbrirPortaComunicacao(porta);
            _portaAberta = abertura == 0;
            return (nameof(IEasyInnerNative.AbrirPortaComunicacao), abertura);
        });
    }

    public AdapterResult FecharPorta() => Medir(nameof(IEasyInnerNative.FecharPortaComunicacao), () =>
    {
        // Devolve void: quem não fecha vaza o socket e a próxima abertura dá retorno 3.
        _nativo.FecharPortaComunicacao();
        _portaAberta = false;
        return (byte)0;
    });

    public AdapterResult TestarConexao(int inner) => Medir(nameof(IEasyInnerNative.Ping), () => _nativo.Ping(inner));

    public AdapterResult Ping(int inner) => Medir(nameof(IEasyInnerNative.PingOnLine), () => _nativo.PingOnLine(inner));

    public (AdapterResult Resultado, FirmwareInfo? Firmware) LerFirmware(int inner)
    {
        byte linha = 0, alta = 0, baixa = 0, sufixo = 0, bio = 0;
        short variacao = 0;

        var resultado = Medir(nameof(IEasyInnerNative.ReceberVersaoFirmware), () =>
            _nativo.ReceberVersaoFirmware(inner, ref linha, ref variacao, ref alta, ref baixa, ref sufixo, ref bio));

        return resultado.Status is AdapterStatus.Ok
            ? (resultado, new FirmwareInfo(linha, variacao, alta, baixa, sufixo, bio != 0))
            : (resultado, null);
    }

    public (AdapterResult Resultado, DateTimeOffset? Relogio) LerRelogio(int inner)
    {
        byte dia = 0, mes = 0, ano = 0, hora = 0, minuto = 0, segundo = 0;

        var resultado = Medir(nameof(IEasyInnerNative.ReceberRelogio), () =>
            _nativo.ReceberRelogio(inner, ref dia, ref mes, ref ano, ref hora, ref minuto, ref segundo));

        if (resultado.Status is not AdapterStatus.Ok)
        {
            return (resultado, null);
        }

        var relogio = Montar(ano, mes, dia, hora, minuto, segundo);
        return (resultado, relogio);
    }

    public AdapterResult AcertarRelogio(int inner, DateTimeOffset instante)
    {
        var local = HoraDeBrasilia.NoEvento(instante);
        if (local.Year is < 2000 or > 2099)
        {
            // O ano vai com dois dígitos: fora desse século a catraca guardaria outra data.
            throw new ArgumentOutOfRangeException(nameof(instante), "o relógio da catraca só guarda anos de 2000 a 2099.");
        }

        return Medir(nameof(IEasyInnerNative.EnviarRelogio), () => _nativo.EnviarRelogio(
            inner,
            (byte)local.Day,
            (byte)local.Month,
            (byte)(local.Year - 2000),
            (byte)local.Hour,
            (byte)local.Minute,
            (byte)local.Second));
    }

    public AdapterResult EnviarConfiguracaoCompleta(int inner, DeviceConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            throw new ArgumentException(
                "Configuração inválida, e enviá-la assim gravaria valores padrão da DLL por cima do " +
                $"equipamento: {string.Join(" ", problemas)}",
                nameof(configuracao));
        }

        return Medir(() =>
        {
            // Cada chamada monta um pedaço no buffer da DLL; EnviarConfiguracoes aplica tudo.
            // Quem pula um passo não deixa "como estava": recebe o padrão da fábrica.
            // Cada passo leva o nome da função: o mesmo retorno (128, 129) quer dizer coisas
            // diferentes em funções diferentes (F6, docs/34 §2).
            List<(string Funcao, byte Retorno)> passos =
            [
                (nameof(IEasyInnerNative.DefinirPadraoCartao), _nativo.DefinirPadraoCartao(configuracao.PadraoCartao)),
                .. configuracao.QuantidadeFixaDeDigitos is { } digitos
                    ? [(nameof(IEasyInnerNative.DefinirQuantidadeDigitosCartao), _nativo.DefinirQuantidadeDigitosCartao(digitos))]
                    : Array.Empty<(string, byte)>(),

                // Uma chamada por tamanho aceito (FUN:13), junto das demais funções de cartão
                // e antes de EnviarConfiguracoes. Só com a chave ligada: desligada, a sequência
                // é a de sempre e a catraca fica com o padrão da DLL (F2, docs/34 §2).
                .. configuracao.EnviarDigitosVariaveis
                    ? configuracao.QuantidadesVariaveisDeDigitos.Distinct().Select(tamanho =>
                        (nameof(IEasyInnerNative.InserirQuantidadeDigitoVariavel), _nativo.InserirQuantidadeDigitoVariavel(tamanho)))
                    : [],

                (nameof(IEasyInnerNative.ConfigurarTipoLeitor), _nativo.ConfigurarTipoLeitor(configuracao.TipoDeLeitor)),
                (nameof(IEasyInnerNative.ConfigurarLeitor1), _nativo.ConfigurarLeitor1(configuracao.OperacaoDoLeitor1)),
                (nameof(IEasyInnerNative.ConfigurarLeitor2), _nativo.ConfigurarLeitor2(configuracao.OperacaoDoLeitor2)),
                (nameof(IEasyInnerNative.ConfigurarAcionamento1),
                    _nativo.ConfigurarAcionamento1(configuracao.FuncaoDoAcionamento1, configuracao.TempoDoAcionamento1)),
                (nameof(IEasyInnerNative.ConfigurarAcionamento2),
                    _nativo.ConfigurarAcionamento2(configuracao.FuncaoDoAcionamento2, configuracao.TempoDoAcionamento2)),
                configuracao.Online
                    ? (nameof(IEasyInnerNative.ConfigurarInnerOnLine), _nativo.ConfigurarInnerOnLine())
                    : (nameof(IEasyInnerNative.ConfigurarInnerOffLine), _nativo.ConfigurarInnerOffLine()),
                (nameof(IEasyInnerNative.HabilitarTeclado), _nativo.HabilitarTeclado(
                    configuracao.TecladoHabilitado ? (byte)1 : (byte)0,
                    configuracao.EcoDoTeclado)),
                (nameof(IEasyInnerNative.HabilitarMudancaOnLineOffLine), _nativo.HabilitarMudancaOnLineOffLine(
                    configuracao.MudancaAutomatica,
                    configuracao.TempoDaMudancaAutomatica)),

                // Etapa A.2: o que ia com o padrão da DLL (ADR-0020). Cada um só com a sua
                // chave técnica (desligadas = a sequência acima, idêntica à de antes), no fim da
                // montagem e antes de EnviarConfiguracoes, na ordem dos campos comuns do anexo
                // 01 §3.3. A ordem dentro do buffer é INFERIDO (T13).
                .. Se(configuracao.EnviarWiegandDoisLeitores, () =>
                    (nameof(IEasyInnerNative.ConfigurarWiegandDoisLeitores), _nativo.ConfigurarWiegandDoisLeitores(
                        Byte(configuracao.WiegandDoisLeitores.Habilitado),
                        Byte(configuracao.WiegandDoisLeitores.ExibirMensagem)))),
                .. Se(configuracao.RegistrarAcessoNegado is not null, () =>
                    (nameof(IEasyInnerNative.RegistrarAcessoNegado), _nativo.RegistrarAcessoNegado(configuracao.RegistrarAcessoNegado!.Value))),
                .. Se(configuracao.EnviarDataHoraNoEventoOnLine, () =>
                    (nameof(IEasyInnerNative.ReceberDataHoraDadosOnLine), _nativo.ReceberDataHoraDadosOnLine(
                        Byte(configuracao.DataHoraNoEventoOnLine)))),

                // O número só existe aqui, na chamada; nunca no resultado nem no registro.
                .. Se(configuracao.CartaoMaster is not null, () =>
                    (nameof(IEasyInnerNative.DefinirNumeroCartaoMaster), _nativo.DefinirNumeroCartaoMaster(
                        configuracao.CartaoMaster!.RevelarParaADll()))),
                .. Se(configuracao.EnviarTipoDeLista, () =>
                    (nameof(IEasyInnerNative.DefinirTipoListaAcesso), _nativo.DefinirTipoListaAcesso(configuracao.TipoDeLista))),
            ];

            foreach (var passo in passos)
            {
                if (passo.Retorno != 0)
                {
                    return passo;
                }
            }

            // Nada com Inner entre montar e enviar (ADR-0006). A mensagem padrão ia aqui no
            // meio, e três vezes por conexão; ela tem o seu passo próprio no laço
            // (EnviarMsgPadrao), depois de rearmar o leitor (defeito F7, docs/34 §2).
            return (nameof(IEasyInnerNative.EnviarConfiguracoes), _nativo.EnviarConfiguracoes(inner));
        });
    }

    public AdapterResult ConfigurarEntradasOnline(int inner, DeviceConfiguration? configuracao = null)
    {
        // Só com a chave catraca.enviar_formas_de_entrada os valores vêm do modelo (Etapa A.2);
        // desligada, são as constantes de sempre. O significado de cada FormaEntrada é T26.
        var formas = configuracao is { EnviarFormasDeEntradaOnLine: true }
            ? configuracao.FormasDeEntradaOnLine
            : FormasDeEntradaDeSempre;

        return Medir(nameof(IEasyInnerNative.EnviarFormasEntradasOnLine), () =>
            // É este passo que rearma o leitor para a próxima leitura. Sem ele, a pista para de
            // ler sem dar erro nenhum — o modo de falha mais caro que existe numa fila.
            _nativo.EnviarFormasEntradasOnLine(
                inner,
                formas.QtdeDigitosTeclado,
                formas.EcoTeclado,
                formas.FormaEntrada,
                formas.TempoTeclado,
                formas.PosicaoCursorTeclado));
    }

    public AdapterResult EnviarMensagemPadrao(int inner, string mensagem)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        return Medir(nameof(IEasyInnerNative.EnviarMensagemPadraoOnLine), () => _nativo.EnviarMensagemPadraoOnLine(inner, 0, Cortar(mensagem, 32)));
    }

    public (AdapterResult Resultado, DeviceEvent? Evento) AguardarEvento(int inner, TimeSpan limite)
    {
        byte origem = 0, complemento = 0, dia = 0, mes = 0, ano = 0, hora = 0, minuto = 0, segundo = 0;
        var buffer = _nativo.NovoBufferDeCartao();

        var resultado = Medir(nameof(IEasyInnerNative.ReceberDadosOnLine), () => _nativo.ReceberDadosOnLine(
            inner, ref origem, ref complemento, buffer, ref dia, ref mes, ref ano, ref hora, ref minuto, ref segundo));

        // Retorno diferente de zero não é silêncio (defeito F6, docs/34 §2): é contado e o
        // bruto é preservado (ADR-0018). O 8 é sempre falha de dependência. Os demais só vão
        // ao laço como erro — e daí à reconexão — com ReconectarEmErroDeRecepcao ligado; senão
        // seguem como "sem eventos", com o retorno bruto no resultado (HIL-EVT-01).
        if (resultado.Status is not AdapterStatus.Ok)
        {
            Interlocked.Increment(ref _errosDeRecepcao);
            return resultado.Status is AdapterStatus.FalhaDeDependencia || ReconectarEmErroDeRecepcao
                ? (resultado, null)
                : (resultado with { Status = AdapterStatus.SemEventos }, null);
        }

        // A_CONFIRMAR: o manual não diz como a DLL sinaliza "nada aconteceu". A leitura aqui
        // é que retorno 0 com origem 0 significa ausência de evento, e não um evento de
        // origem zero — que não existe na tabela. Ensaio HIL-EVT-01.
        if (origem == 0)
        {
            return (resultado with { Status = AdapterStatus.SemEventos }, null);
        }

        var recebidoEm = DateTimeOffset.UtcNow;
        var relogioDoEquipamento = Montar(ano, mes, dia, hora, minuto, segundo);

        // A sequência é do adapter porque a DLL não numera evento. Ela existe só para dar
        // chave de deduplicação dentro desta sessão; quem persiste combina com o boot.
        var evento = DeviceEvent.Create(
            new DeviceEventKey($"inner-{inner}", _bootId, ++_sequencia),
            EventOrigin.FromRaw(origem),
            recebidoEm,
            $"ei-{inner}-{Guid.CreateVersion7(recebidoEm)}",
            complemento,
            _nativo.LerCartao(buffer),
            relogioDoEquipamento == DateTimeOffset.MinValue ? null : relogioDoEquipamento);

        return (resultado, evento);
    }

    /// <remarks>
    /// Um para um, sem consultar perfil: a inversão já foi resolvida no comissionamento
    /// (<see cref="GatePhysicalProfile.LiberacaoDaEntrada"/>). Antes o adapter invertia de
    /// novo o que o laço já tinha invertido (defeito F1, docs/34 §2).
    /// </remarks>
    public AdapterResult LiberarGiro(int inner, GateDirection direcao) => Medir(() => direcao switch
    {
        GateDirection.Entrada => (nameof(IEasyInnerNative.LiberarCatracaEntrada), _nativo.LiberarCatracaEntrada(inner)),
        GateDirection.EntradaInvertida =>
            (nameof(IEasyInnerNative.LiberarCatracaEntradaInvertida), _nativo.LiberarCatracaEntradaInvertida(inner)),
        GateDirection.Saida => (nameof(IEasyInnerNative.LiberarCatracaSaida), _nativo.LiberarCatracaSaida(inner)),
        GateDirection.SaidaInvertida =>
            (nameof(IEasyInnerNative.LiberarCatracaSaidaInvertida), _nativo.LiberarCatracaSaidaInvertida(inner)),

        // Só para evacuação: permite carona e por isso não entra na operação normal.
        GateDirection.DoisSentidos =>
            (nameof(IEasyInnerNative.LiberarCatracaDoisSentidos), _nativo.LiberarCatracaDoisSentidos(inner)),

        _ => throw new ArgumentOutOfRangeException(nameof(direcao), direcao, "Sentido desconhecido."),
    });

    public AdapterResult AcionarReleDaUrna(int inner) =>
        Medir(nameof(IEasyInnerNative.AcionarRele2), () => _nativo.AcionarRele2(inner));

    public (AdapterResult Resultado, Bilhete? Bilhete) ColetarBilhete(int inner)
    {
        byte tipo = 0, dia = 0, mes = 0, ano = 0, hora = 0, minuto = 0;
        var buffer = _nativo.NovoBufferDeCartao();

        var resultado = Medir(nameof(IEasyInnerNative.ColetarBilhete), () =>
            _nativo.ColetarBilhete(inner, ref tipo, ref dia, ref mes, ref ano, ref hora, ref minuto, buffer));

        if (resultado.Status is not AdapterStatus.Ok)
        {
            return (resultado, null);
        }

        var cartao = _nativo.LerCartao(buffer);

        // Buffer vazio depois de um retorno 0 é como a memória sinaliza que acabou.
        // A_CONFIRMAR na bancada: ensaio HIL-BIL-01.
        if (cartao.Length == 0)
        {
            return (resultado with { Status = AdapterStatus.SemBilhetes }, null);
        }

        // Sem segundos: o bilhete off-line não os tem. Gravar zero é honesto; inventar não.
        return (resultado, new Bilhete(tipo, Montar(ano, mes, dia, hora, minuto, 0), cartao));
    }

    public AdapterResult ExibirMensagemTemporaria(int inner, string mensagem, TimeSpan duracao)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        ArgumentOutOfRangeException.ThrowIfNegative(duracao.TotalSeconds);

        var segundos = duracao.TotalSeconds > 255 ? (byte)255 : (byte)duracao.TotalSeconds;

        return Medir(
            nameof(IEasyInnerNative.EnviarMensagemTemporariaOnLine),
            () => _nativo.EnviarMensagemTemporariaOnLine(inner, 0, Cortar(mensagem, 32), segundos));
    }

    public void Dispose()
    {
        if (_descartado)
        {
            return;
        }

        _descartado = true;

        if (_portaAberta)
        {
            _nativo.FecharPortaComunicacao();
            _portaAberta = false;
        }
    }

    /// <summary>Monta a data do equipamento, que traz o ano com dois dígitos.</summary>
    /// <remarks>
    /// A catraca não guarda fuso. A borda acerta o relógio dela em horário de Brasília
    /// (<see cref="AcertarRelogio"/>), então é nesse horário que a leitura é interpretada —
    /// e não em UTC, o que deslocaria todo carimbo em três horas.
    /// Data inválida não vira exceção: o equipamento pode estar com o relógio zerado, e
    /// derrubar o laço por causa disso perderia o evento. Volta como
    /// <see cref="DateTimeOffset.MinValue"/>, que o consumidor reconhece.
    /// </remarks>
    private static DateTimeOffset Montar(byte ano, byte mes, byte dia, byte hora, byte minuto, byte segundo)
    {
        try
        {
            return HoraDeBrasilia.DoEvento(new DateTime(2000 + ano, mes, dia, hora, minuto, segundo, DateTimeKind.Unspecified));
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>Corta a mensagem no limite do display, sem quebrar no meio de nada.</summary>
    private static string Cortar(string texto, int limite) =>
        texto.Length <= limite ? texto : texto[..limite];

    /// <summary>Um passo da montagem que só acontece com a condição (a chave técnica) verdadeira.</summary>
    /// <remarks>A chamada nativa só é feita aqui dentro: desligada, a DLL nem fica sabendo.</remarks>
    private static (string Funcao, byte Retorno)[] Se(bool condicao, Func<(string Funcao, byte Retorno)> chamada) =>
        condicao ? [chamada()] : [];

    private static byte Byte(bool valor) => valor ? (byte)1 : (byte)0;

    /// <summary>
    /// Mede uma chamada e interpreta o retorno à luz da função que o devolveu (F6, docs/34 §2).
    /// </summary>
    private static AdapterResult Medir(string funcao, Func<byte> chamada) =>
        Medir(() => (funcao, chamada()));

    /// <summary>Mede uma sequência de chamadas; quem a executa diz qual função deu o retorno final.</summary>
    private static AdapterResult Medir(Func<(string Funcao, byte Retorno)> chamada)
    {
        var cronometro = Stopwatch.StartNew();
        var (funcao, retorno) = chamada();
        cronometro.Stop();

        return AdapterResult.FromNative(retorno, cronometro.Elapsed, funcao);
    }
}
