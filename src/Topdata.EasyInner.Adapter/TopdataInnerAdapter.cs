using System.Diagnostics;
using Access.Application.Devices;
using Access.Domain.Devices;
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

    private readonly IEasyInnerNative _nativo;
    private readonly Dictionary<int, GatePhysicalProfile> _perfis = [];

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
    private bool _portaAberta;
    private bool _descartado;

    public TopdataInnerAdapter(IEasyInnerNative? nativo = null)
    {
        _nativo = nativo ?? new EasyInnerReal();
    }

    public AdapterResult AbrirPorta(int porta)
    {
        ObjectDisposedException.ThrowIf(_descartado, this);

        return Medir(() =>
        {
            // O tipo de conexão precisa vir antes de abrir a porta (manual 3.3.1).
            var tipo = _nativo.DefinirTipoConexao(ConexaoTcpPortaFixa);
            if (tipo != 0)
            {
                return tipo;
            }

            var abertura = _nativo.AbrirPortaComunicacao(porta);
            _portaAberta = abertura == 0;
            return abertura;
        });
    }

    public AdapterResult FecharPorta() => Medir(() =>
    {
        // Devolve void: quem não fecha vaza o socket e a próxima abertura dá retorno 3.
        _nativo.FecharPortaComunicacao();
        _portaAberta = false;
        return (byte)0;
    });

    public AdapterResult TestarConexao(int inner) => Medir(() => _nativo.Ping(inner));

    public AdapterResult Ping(int inner) => Medir(() => _nativo.PingOnLine(inner));

    public (AdapterResult Resultado, FirmwareInfo? Firmware) LerFirmware(int inner)
    {
        byte linha = 0, alta = 0, baixa = 0, sufixo = 0, bio = 0;
        short variacao = 0;

        var resultado = Medir(() =>
            _nativo.ReceberVersaoFirmware(inner, ref linha, ref variacao, ref alta, ref baixa, ref sufixo, ref bio));

        return resultado.Status is AdapterStatus.Ok
            ? (resultado, new FirmwareInfo(linha, variacao, alta, baixa, sufixo, bio != 0))
            : (resultado, null);
    }

    public (AdapterResult Resultado, DateTimeOffset? Relogio) LerRelogio(int inner)
    {
        byte dia = 0, mes = 0, ano = 0, hora = 0, minuto = 0, segundo = 0;

        var resultado = Medir(() =>
            _nativo.ReceberRelogio(inner, ref dia, ref mes, ref ano, ref hora, ref minuto, ref segundo));

        if (resultado.Status is not AdapterStatus.Ok)
        {
            return (resultado, null);
        }

        var relogio = Montar(ano, mes, dia, hora, minuto, segundo);
        return (resultado, relogio);
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

        _perfis[inner] = configuracao.PerfilFisico;

        return Medir(() =>
        {
            // Cada chamada monta um pedaço no buffer da DLL; EnviarConfiguracoes aplica tudo.
            // Quem pula um passo não deixa "como estava": recebe o padrão da fábrica.
            byte[] passos =
            [
                _nativo.DefinirPadraoCartao(configuracao.PadraoCartao),
                configuracao.QuantidadeFixaDeDigitos is { } digitos
                    ? _nativo.DefinirQuantidadeDigitosCartao(digitos)
                    : (byte)0,
                _nativo.ConfigurarTipoLeitor(configuracao.TipoDeLeitor),
                _nativo.ConfigurarLeitor1(configuracao.OperacaoDoLeitor1),
                _nativo.ConfigurarLeitor2(configuracao.OperacaoDoLeitor2),
                _nativo.ConfigurarAcionamento1(configuracao.FuncaoDoAcionamento1, configuracao.TempoDoAcionamento1),
                _nativo.ConfigurarAcionamento2(configuracao.FuncaoDoAcionamento2, configuracao.TempoDoAcionamento2),
                configuracao.Online ? _nativo.ConfigurarInnerOnLine() : _nativo.ConfigurarInnerOffLine(),
                _nativo.HabilitarTeclado(
                    configuracao.TecladoHabilitado ? (byte)1 : (byte)0,
                    configuracao.EcoDoTeclado),
                _nativo.HabilitarMudancaOnLineOffLine(
                    configuracao.MudancaAutomatica,
                    configuracao.TempoDaMudancaAutomatica),
            ];

            foreach (var passo in passos)
            {
                if (passo != 0)
                {
                    return passo;
                }
            }

            var mensagem = _nativo.EnviarMensagemPadraoOnLine(inner, 0, configuracao.MensagemPadrao);
            return mensagem != 0 ? mensagem : _nativo.EnviarConfiguracoes(inner);
        });
    }

    public AdapterResult ConfigurarEntradasOnline(int inner) => Medir(() =>
        // É este passo que rearma o leitor para a próxima leitura. Sem ele, a pista para de
        // ler sem dar erro nenhum — o modo de falha mais caro que existe numa fila.
        _nativo.EnviarFormasEntradasOnLine(
            inner,
            qtdeDigitosTeclado: 0,
            ecoTeclado: 0,
            formaEntrada: EntradaTecladoELeitores,
            tempoTeclado: 0,
            posicaoCursorTeclado: 0));

    public AdapterResult EnviarMensagemPadrao(int inner, string mensagem)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        return Medir(() => _nativo.EnviarMensagemPadraoOnLine(inner, 0, Cortar(mensagem, 32)));
    }

    public (AdapterResult Resultado, DeviceEvent? Evento) AguardarEvento(int inner, TimeSpan limite)
    {
        byte origem = 0, complemento = 0, dia = 0, mes = 0, ano = 0, hora = 0, minuto = 0, segundo = 0;
        var buffer = _nativo.NovoBufferDeCartao();

        var resultado = Medir(() => _nativo.ReceberDadosOnLine(
            inner, ref origem, ref complemento, buffer, ref dia, ref mes, ref ano, ref hora, ref minuto, ref segundo));

        // A_CONFIRMAR: o manual não diz como a DLL sinaliza "nada aconteceu". A leitura aqui
        // é que retorno 0 com origem 0 significa ausência de evento, e não um evento de
        // origem zero — que não existe na tabela. Ensaio HIL-EVT-01.
        if (resultado.Status is not AdapterStatus.Ok || origem == 0)
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

    public AdapterResult LiberarGiro(int inner, GateDirection direcao)
    {
        var invertido = _perfis.TryGetValue(inner, out var perfil) && perfil.SentidoInvertido;

        return Medir(() => direcao switch
        {
            GateDirection.Entrada => invertido
                ? _nativo.LiberarCatracaEntradaInvertida(inner)
                : _nativo.LiberarCatracaEntrada(inner),

            GateDirection.Saida => invertido
                ? _nativo.LiberarCatracaSaidaInvertida(inner)
                : _nativo.LiberarCatracaSaida(inner),

            // Só para evacuação: permite carona e por isso não entra na operação normal.
            GateDirection.DoisSentidos => _nativo.LiberarCatracaDoisSentidos(inner),

            _ => throw new ArgumentOutOfRangeException(nameof(direcao), direcao, "Sentido desconhecido."),
        });
    }

    public AdapterResult AcionarReleDaUrna(int inner) => Medir(() => _nativo.AcionarRele2(inner));

    public (AdapterResult Resultado, Bilhete? Bilhete) ColetarBilhete(int inner)
    {
        byte tipo = 0, dia = 0, mes = 0, ano = 0, hora = 0, minuto = 0;
        var buffer = _nativo.NovoBufferDeCartao();

        var resultado = Medir(() =>
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

        return Medir(() => _nativo.EnviarMensagemTemporariaOnLine(inner, 0, Cortar(mensagem, 32), segundos));
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
    /// Data inválida não vira exceção: o equipamento pode estar com o relógio zerado, e
    /// derrubar o laço por causa disso perderia o evento. Volta como
    /// <see cref="DateTimeOffset.MinValue"/>, que o consumidor reconhece.
    /// </remarks>
    private static DateTimeOffset Montar(byte ano, byte mes, byte dia, byte hora, byte minuto, byte segundo)
    {
        try
        {
            return new DateTimeOffset(2000 + ano, mes, dia, hora, minuto, segundo, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>Corta a mensagem no limite do display, sem quebrar no meio de nada.</summary>
    private static string Cortar(string texto, int limite) =>
        texto.Length <= limite ? texto : texto[..limite];

    private static AdapterResult Medir(Func<byte> chamada)
    {
        var cronometro = Stopwatch.StartNew();
        var retorno = chamada();
        cronometro.Stop();

        return AdapterResult.FromNative(retorno, cronometro.Elapsed);
    }
}
