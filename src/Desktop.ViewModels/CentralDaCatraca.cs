using Contracts.Edge.V1;
using Desktop.ViewModels.GemeoDigital;

namespace Desktop.ViewModels;

/// <summary>
/// A configuração da catraca inteira, dentro do gêmeo digital: clicar numa peça abre os
/// parâmetros daquela peça; as alterações de todas as peças se acumulam num só "o que muda",
/// com um só Salvar e um só Aplicar (docs/33 §9, pedido do dono do produto).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nada de regra nova aqui.</b> Os campos, a validação no campo, a origem, a situação
/// (enviado, chave desligada, a confirmar), o salvar com nome, o aplicar em dois passos e o
/// "aplicada" só com o pedido concluído e a versão igual são os da Parametrização (Etapa A.6,
/// <see cref="ParametrizacaoViewModel"/>), pelos mesmos RPCs. O giro é o mapa de giro (D9,
/// <see cref="MapaDeGiroViewModel"/>), que entra na mesma versão do salvo. Os pedidos imediatos
/// (mensagem temporária, acertar relógio, refazer conexão) são os da "Gerenciar catraca"
/// (<see cref="GerenciarCatracaViewModel"/>). Esta classe só junta as três e diz qual parte
/// cabe a cada peça (<see cref="ConfiguracaoPorPeca"/>).
/// </para>
/// <para>
/// <b>Salvar são duas gravações</b> (o mapa de giro e os campos têm RPCs próprios). Cada uma é
/// inteira; se a do giro for recusada, nada mais é gravado; se a dos campos for recusada depois
/// do giro salvo, a tela diz exatamente isso, e o que falta continua em "o que muda". Aplicar é
/// um pedido só: o "Aplicar configuração" da catraca, que leva tudo o que está salvo.
/// </para>
/// <para>
/// <b>O que a catraca está usando</b> (modo ao vivo): a catraca não devolve a configuração
/// (leitura de volta bloqueada, Etapa A.10). O gêmeo só afirma os valores quando a versão que a
/// catraca aceitou (Etapa A.5) é a do salvo e não há pedido em curso; fora disso, diz que não sabe.
/// </para>
/// </remarks>
public sealed class CentralDaCatracaViewModel : TelaBase
{
    /// <summary>A frase que separa esta configuração (real) dos cenários (só no desenho).</summary>
    public const string AvisoDaConfiguracao = "Configuração real desta catraca — vale depois de Aplicar.";

    /// <summary>Selo do relé 2: o que ele aciona na TopFit 4 e o fim do acionamento ainda vão à bancada.</summary>
    public const string SeloDoRele2 =
        "Aguardando ensaio NOVO-HIL-REL-04/06: o borne e a tensão do relé 2 na TopFit 4, e se o fim do acionamento dele se distingue do relé 1 (origem 5). Até lá, segue o padrão de fábrica e não muda por catraca.";

    private PecaDaCatraca? _peca;
    private bool _painelAberto;
    private IReadOnlyList<LinhaDeMudanca> _mudancas = [];
    private IReadOnlyList<string> _problemas = [];
    private IReadOnlyList<MarcaDaPeca> _marcas = [];
    private int _versaoDasMarcas;
    private Task _carregando = Task.CompletedTask;

    public CentralDaCatracaViewModel(EdgeControl.EdgeControlClient cliente, Func<DateTimeOffset>? relogio = null, TimeSpan? esperaPeloResultado = null)
        : base(cliente, relogio)
    {
        Parametrizacao = new ParametrizacaoViewModel(cliente, relogio, esperaPeloResultado);
        Giro.ControlesProprios = false;
        Comandos = new GerenciarCatracaViewModel(cliente, relogio, esperaPeloResultado);

        Parametrizacao.PropertyChanged += (_, e) => AoMudar(e.PropertyName);
        Giro.PropertyChanged += (_, e) => AoMudar(e.PropertyName);
        Comandos.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GerenciarCatracaViewModel.Selecionada))
            {
                Avisar(nameof(Firmware));
                Avisar(nameof(RelogioDaCatraca));
                Avisar(nameof(RelogioDivergente));
            }
        };

        Salvar = new ComandoAssincrono(SalvarAsync, () => PodeSalvar);
        Desfazer = new ComandoAssincrono(DesfazerAsync, () => Catraca > 0 && Mudancas.Count > 0);
        PedirAplicacao = new ComandoAssincrono(
            () => Parametrizacao.PedirAplicacao.ExecutarAsync(),
            () => MotivoParaNaoAplicar.Length == 0 && !ConfirmandoAplicacao);
        ConfirmarAplicacao = new ComandoAssincrono(AplicarAsync, () => MotivoParaNaoAplicar.Length == 0 && ConfirmandoAplicacao);
        CancelarAplicacao = new ComandoAssincrono(() => Parametrizacao.CancelarAplicacao.ExecutarAsync(), () => ConfirmandoAplicacao);
        FecharPainel = new ComandoAssincrono(() =>
        {
            PainelAberto = false;
            return Task.CompletedTask;
        });
    }

    public override string Titulo => "Configuração desta catraca";

    /// <summary>Os campos, as versões, a situação na catraca e o aplicar (Etapa A.6).</summary>
    public ParametrizacaoViewModel Parametrizacao { get; }

    /// <summary>O mapa de giro desta catraca (D9), sem salvar e aplicar próprios.</summary>
    public MapaDeGiroViewModel Giro => Parametrizacao.Giro;

    /// <summary>Os pedidos imediatos da "Gerenciar catraca", para a mesma catraca.</summary>
    public GerenciarCatracaViewModel Comandos { get; }

    /// <summary>Grava tudo o que mudou, em todas as peças, com o nome digitado.</summary>
    public ComandoAssincrono Salvar { get; }

    /// <summary>Volta tudo ao que está salvo para a catraca.</summary>
    public ComandoAssincrono Desfazer { get; }

    /// <summary>Primeiro passo de aplicar: mostra a confirmação.</summary>
    public ComandoAssincrono PedirAplicacao { get; }

    /// <summary>Segundo passo: o "Aplicar configuração" desta catraca (o mesmo da Parametrização).</summary>
    public ComandoAssincrono ConfirmarAplicacao { get; }

    public ComandoAssincrono CancelarAplicacao { get; }

    /// <summary>Fecha o painel da peça (a configuração continua carregada).</summary>
    public ComandoAssincrono FecharPainel { get; }

    /// <summary>A catraca carregada; 0 antes de carregar.</summary>
    public int Catraca => Parametrizacao.Catraca;

    /// <summary>Quem salva, aplica e pede. Não há login: fica registrado como foi digitado.</summary>
    public string Operador
    {
        get => Parametrizacao.Operador;
        set
        {
            Parametrizacao.Operador = value ?? string.Empty;
            Comandos.Operador = Parametrizacao.Operador;
            Avisar();
            Reavaliar();
        }
    }

    /// <summary>Mostra os parâmetros técnicos, com os nomes do SDK. Conveniência, não segurança.</summary>
    public bool ModoTecnico
    {
        get => Parametrizacao.ModoTecnico;
        set => Parametrizacao.ModoTecnico = value;
    }

    // ------------------------------------------------------------------ painel da peça

    /// <summary>O painel da peça está aberto ao lado do desenho.</summary>
    public bool PainelAberto
    {
        get => _painelAberto;
        private set
        {
            if (Definir(ref _painelAberto, value))
            {
                AvisarPainel();
            }
        }
    }

    /// <summary>Nenhum painel de peça aberto: a tela diz como abrir um.</summary>
    public bool SemPainel => !PainelAberto;

    /// <summary>A peça do painel; nula antes do primeiro clique.</summary>
    public PecaDaCatraca? Peca { get => _peca; private set => Definir(ref _peca, value); }

    /// <summary>O que a peça faz (a ficha de sempre do gêmeo).</summary>
    public FichaDaPeca? Ficha => Peca is { } p ? CatalogoDaFit4.De(p) : null;

    /// <summary>O que a peça configura.</summary>
    public PainelDaPeca? Painel => Peca is { } p ? ConfiguracaoPorPeca.De(p) : null;

    public string TituloDoPainel => Painel?.Titulo ?? "Escolha uma peça";

    /// <summary>Os campos desta peça visíveis no modo atual, na ordem do painel.</summary>
    public IReadOnlyList<CampoDaParametrizacao> CamposDaPeca =>
        Painel is not { } painel
            ? []
            : [.. painel.Campos
                .Select(c => Parametrizacao.Campos.FirstOrDefault(x => x.Campo == c))
                .OfType<CampoDaParametrizacao>()
                .Where(c => ModoTecnico || !c.SoTecnico)];

    /// <summary>Nada que o sistema configure ou peça nesta peça: só a ficha.</summary>
    public bool NadaAConfigurar => Painel?.NadaAConfigurar ?? false;

    public bool MostraGiro => Painel?.MostraGiro ?? false;

    public bool MostraMensagemTemporaria => Painel?.Comandos.Contains(ComandoDaPeca.MensagemTemporaria) ?? false;

    public bool MostraAcertarRelogio => Painel?.Comandos.Contains(ComandoDaPeca.AcertarRelogio) ?? false;

    public bool MostraRefazerConexao => Painel?.Comandos.Contains(ComandoDaPeca.RefazerConexao) ?? false;

    /// <summary>Firmware e relógio da catraca (a placa de controle).</summary>
    public bool MostraEquipamento => Painel?.MostraEquipamento ?? false;

    /// <summary>O relé 2 é técnico (o termo é do SDK): só no modo técnico, sempre desabilitado.</summary>
    public bool MostraRele2 => (Painel?.MostraRele2 ?? false) && ModoTecnico;

    public const string RotuloDoRele2 = "Relé 2 (ConfigurarAcionamento2, EI-017)";

    /// <summary>O que a catraca recebe hoje (padrão de fábrica, <c>MontadorDaConfiguracao</c>).</summary>
    public const string ValorDoRele2 = "Função 0, tempo 0 s — o padrão de fábrica, igual em todas as catracas";

    /// <summary>
    /// Quando o modo guiado esconde parâmetros desta peça, diz quantos (sem termo técnico). Vazio
    /// quando não esconde nada.
    /// </summary>
    public string AvisoDoModo
    {
        get
        {
            if (Painel is not { } painel || ModoTecnico)
            {
                return string.Empty;
            }

            var escondidos = painel.Campos.Count(c => Parametrizacao.Campos.FirstOrDefault(x => x.Campo == c) is { SoTecnico: true })
                + (painel.MostraRele2 ? 1 : 0);
            return escondidos switch
            {
                0 => string.Empty,
                1 => "No modo técnico aparece mais 1 ajuste desta peça.",
                var n => $"No modo técnico aparecem mais {n} ajustes desta peça.",
            };
        }
    }

    public string Firmware => Comandos.Selecionada?.Firmware ?? "—";

    public string RelogioDaCatraca => Comandos.Selecionada?.Relogio ?? "não conferido";

    public bool RelogioDivergente => Comandos.Selecionada?.RelogioDivergente ?? false;

    /// <summary>Abre o painel de uma peça; os braços e a urna levam ao mapa de giro.</summary>
    public void Abrir(PecaDaCatraca peca)
    {
        Peca = peca;
        if (ConfiguracaoPorPeca.De(peca) is { MostraGiro: true } painel)
        {
            Giro.Focar(painel.OrigemDoGiro);
            Giro.SomenteEmFoco = painel.OrigemDoGiro is not null;
        }

        PainelAberto = true;
        AvisarPainel();
    }

    // ------------------------------------------------------------------ o que muda, salvar, aplicar

    /// <summary>"O que muda (atual → novo)" da catraca inteira: campos de todas as peças e o giro.</summary>
    public IReadOnlyList<LinhaDeMudanca> Mudancas { get => _mudancas; private set => Definir(ref _mudancas, value); }

    public string ResumoDasMudancas => Mudancas.Count switch
    {
        0 => "Nenhuma alteração. Clique numa peça e mude um parâmetro para ver aqui o que muda.",
        1 => "1 alteração não salva.",
        var n => $"{n} alterações não salvas.",
    };

    /// <summary>"3 alterações não salvas", para a faixa do desenho; vazio quando não há.</summary>
    public string AlteracoesPendentes => Mudancas.Count switch
    {
        0 => string.Empty,
        1 => "1 alteração não salva",
        var n => $"{n} alterações não salvas",
    };

    /// <summary>O que o serviço recusou na última gravação ou no último pedido.</summary>
    public IReadOnlyList<string> Problemas { get => _problemas; private set => Definir(ref _problemas, value); }

    /// <summary>Problemas do que está salvo (do serviço).</summary>
    public IReadOnlyList<string> AvisosDoSalvo => [.. Parametrizacao.AvisosDoSalvo, .. Giro.AvisosDoSalvo];

    /// <summary>"Aplicada", "Salva, não aplicada", "Aplicando…" (Etapa A.5/A.6).</summary>
    public string SituacaoNaCatraca => Parametrizacao.SituacaoNaCatraca;

    /// <summary>
    /// A situação em poucas palavras ("Aplicada", "Salva, não aplicada", "Aplicando…"), para o
    /// selo caber na coluna estreita do gêmeo; a frase inteira vai na dica e no detalhe.
    /// </summary>
    public string SituacaoCurta
    {
        get
        {
            var texto = SituacaoNaCatraca ?? string.Empty;
            var corte = texto.IndexOfAny([':', ';']);
            return corte > 0 ? texto[..corte] : texto;
        }
    }

    public Sinal SinalDaSituacao => Parametrizacao.SinalDaSituacao;

    public string DetalheDaSituacao => Parametrizacao.DetalheDaSituacao;

    /// <summary>Os últimos pedidos de aplicar desta catraca, do histórico.</summary>
    public IReadOnlyList<LinhaDeComando> Aplicacoes => Parametrizacao.Aplicacoes;

    public bool ConfirmandoAplicacao => Parametrizacao.ConfirmandoAplicacao;

    public string TextoDaConfirmacao =>
        $"Aplicar a configuração salva na catraca {Catraca}, de todas as peças e do giro? Ela reconecta para receber e fica alguns segundos sem atender. Se estiver no pico, prefira aplicar depois.";

    /// <summary>Por que não dá para aplicar agora; vazio quando dá.</summary>
    public string MotivoParaNaoAplicar =>
        Catraca == 0 ? "Escolha a catraca."
        : Mudancas.Count > 0 ? "Salve as alterações antes de aplicar: o que vai para a catraca é o que está salvo."
        : Parametrizacao.MotivoParaNaoAplicar;

    private bool OperadorInformado => Operador.Trim().Length >= 2;

    private bool PodeSalvar =>
        Catraca > 0 && OperadorInformado && Mudancas.Count > 0
        && Parametrizacao.Campos.All(c => c.Erro.Length == 0)
        && Giro.Linhas.All(l => l.Erro.Length == 0);

    // ------------------------------------------------------------------ marcações do desenho

    /// <summary>As marcações das peças: alteração não salva e diferente do padrão do evento.</summary>
    public IReadOnlyList<MarcaDaPeca> Marcas { get => _marcas; private set => Definir(ref _marcas, value); }

    /// <summary>Muda quando as marcações mudam: o aviso para o desenho refazê-las.</summary>
    public int VersaoDasMarcas { get => _versaoDasMarcas; private set => Definir(ref _versaoDasMarcas, value); }

    // ------------------------------------------------------------------ o que a catraca usa

    /// <summary>
    /// A catraca confirmou a versão do salvo e não há pedido em curso: os valores salvos são os
    /// que ela está usando. Falso: o gêmeo não sabe o que ela usa (a catraca não devolve a
    /// configuração).
    /// </summary>
    public bool AplicadaConhecida =>
        Parametrizacao.VersaoSalva.Length > 0
        && string.Equals(Parametrizacao.VersaoSalva, Parametrizacao.VersaoAplicada, StringComparison.Ordinal)
        && !Parametrizacao.AplicacaoEmAndamento
        && Parametrizacao.Campos.Count > 0;

    /// <summary>Em uma frase: o que se sabe do que a catraca está usando.</summary>
    public string TextoNaCatracaAgora
    {
        get
        {
            var aplicada = Parametrizacao.VersaoAplicada;
            var salva = Parametrizacao.VersaoSalva;

            if (Catraca == 0 || Parametrizacao.Campos.Count == 0)
            {
                return "Configuração desta catraca ainda não carregada.";
            }

            if (AplicadaConhecida)
            {
                return $"A catraca confirmou a configuração salva (versão {Curta(aplicada)}). É o que ela está usando.";
            }

            if (Parametrizacao.AplicacaoEmAndamento)
            {
                return "Aplicando: a catraca ainda não confirmou a configuração salva.";
            }

            if (aplicada.Length == 0)
            {
                return "A catraca ainda não confirmou nenhuma configuração desde que o programa dela começou: o gêmeo não sabe o que ela está usando.";
            }

            return $"A catraca está com outra versão ({Curta(aplicada)}), não a salva ({Curta(salva)}). Ela não devolve a própria configuração: aplique a salva para o gêmeo mostrar o que ela usa.";
        }
    }

    /// <summary>Os valores que a catraca está usando, só quando <see cref="AplicadaConhecida"/>.</summary>
    public IReadOnlyList<ParDeTexto> NaCatracaAgora
    {
        get
        {
            if (!AplicadaConhecida)
            {
                return [];
            }

            var campos = Parametrizacao.Campos
                .Where(c => c.Situacao is SituacaoDoCampo.Enviado && (ModoTecnico || !c.SoTecnico))
                .Select(c => new ParDeTexto(c.Rotulo, c.Nome(c.ValorEfetivo)));
            var giro = Giro.Linhas.Select(l => new ParDeTexto($"Giro · {l.Nome}", l.Descrever(salvo: true)));
            return [.. campos, .. giro];
        }
    }

    /// <summary>A mensagem padrão que a catraca está usando; nula quando não se sabe.</summary>
    public string? MensagemPadraoAplicada =>
        AplicadaConhecida ? Parametrizacao.Campos.FirstOrDefault(c => c.Campo is CampoDaCatraca.MensagemPadrao)?.ValorEfetivo : null;

    /// <summary>O leitor da urna está ligado na catraca; nulo quando não se sabe.</summary>
    public bool? UrnaLigadaAplicada =>
        AplicadaConhecida && Parametrizacao.Campos.FirstOrDefault(c => c.Campo is CampoDaCatraca.OperacaoDoLeitor2) is { } urna
            ? urna.ValorEfetivo != "0"
            : null;

    // ------------------------------------------------------------------ carga

    public override Task AtualizarAsync(CancellationToken cancelamento = default) =>
        Catraca == 0 ? Task.CompletedTask : CarregarAsync(Catraca, cancelamento);

    /// <summary>Carrega a catraca: campos, giro, situação e o que os pedidos precisam.</summary>
    /// <remarks>Trocar de catraca joga fora o que não foi salvo, como na Parametrização.</remarks>
    public Task CarregarAsync(int inner, CancellationToken cancelamento = default)
    {
        _carregando = CarregarInternoAsync(inner, cancelamento);
        return _carregando;
    }

    /// <summary>Espera a carga em curso, se houver (a troca de catraca no seletor não espera).</summary>
    public Task EsperarCargaAsync() => _carregando;

    /// <summary>
    /// A atualização periódica: a situação na catraca e os pedidos. Os campos não são recarregados
    /// por cima do operador nem a cada volta (o editor aberto fecharia): só quando o salvo mudou em
    /// outro lugar (a Parametrização, outro painel) e não há nada pendente aqui. Com algo pendente,
    /// a tela avisa, para ninguém gravar por cima sem saber.
    /// </summary>
    public async Task AcompanharAsync(CancellationToken cancelamento = default)
    {
        if (Catraca == 0)
        {
            return;
        }

        var versaoAntes = Parametrizacao.VersaoSalva;
        var alteradaAntes = Parametrizacao.DetalheDaSituacao;
        await Parametrizacao.AcompanharAsync(cancelamento).ConfigureAwait(true);
        var mudouLaFora = !string.Equals(versaoAntes, Parametrizacao.VersaoSalva, StringComparison.Ordinal)
            || !string.Equals(alteradaAntes, Parametrizacao.DetalheDaSituacao, StringComparison.Ordinal);

        if (mudouLaFora && Mudancas.Count == 0)
        {
            await Parametrizacao.CarregarCatracaAsync(Catraca, cancelamento).ConfigureAwait(true);
        }
        else if (mudouLaFora)
        {
            Mensagem = "A configuração desta catraca foi salva em outro lugar enquanto você mudava. Desfaça para ver a nova, ou salve para gravar a sua por cima.";
        }
        else if (Giro.Mudancas.Count == 0 && PainelAberto && MostraGiro)
        {
            // As conferências do sentido, registradas na bancada enquanto o painel está aberto.
            await Giro.CarregarAsync(Catraca, cancelamento).ConfigureAwait(true);
        }

        await Comandos.AtualizarAsync(cancelamento).ConfigureAwait(true);
        Recalcular();
    }

    private async Task CarregarInternoAsync(int inner, CancellationToken cancelamento)
    {
        if (inner <= 0)
        {
            return;
        }

        Comandos.Catraca = inner;
        Problemas = [];
        await Parametrizacao.CarregarCatracaAsync(inner, cancelamento).ConfigureAwait(true);
        await Comandos.AtualizarAsync(cancelamento).ConfigureAwait(true);
        Mensagem = Parametrizacao.Mensagem;
        Avisar(nameof(Catraca));
        Recalcular();
    }

    private async Task SalvarAsync()
    {
        var inner = Catraca;
        var giroMudou = Giro.Mudancas.Count > 0;
        var camposMudaram = Parametrizacao.Mudancas.Count > 0;
        Problemas = [];

        if (giroMudou)
        {
            await Giro.Salvar.ExecutarAsync().ConfigureAwait(true);
            var avisoDoGiro = Giro.Mensagem;
            Giro.LimparMensagem();
            if (Giro.Problemas.Count > 0 || Giro.Mudancas.Count > 0)
            {
                Problemas = [.. Giro.Problemas];
                Mensagem = Problemas.Count > 0 ? "Nada foi salvo: corrija o que o giro indica abaixo." : avisoDoGiro;
                return;
            }
        }

        if (camposMudaram)
        {
            await Parametrizacao.Salvar.ExecutarAsync().ConfigureAwait(true);
            if (Parametrizacao.Problemas.Count > 0 || Parametrizacao.Mudancas.Count > 0)
            {
                Problemas = [.. Parametrizacao.Problemas];
                Mensagem = giroMudou
                    ? "O giro foi salvo; os outros parâmetros não. Corrija os itens indicados e salve de novo."
                    : Problemas.Count > 0 ? "Não foi salvo. Corrija os itens indicados." : Parametrizacao.Mensagem;
                return;
            }
        }
        else
        {
            // O mapa de giro entra na versão do salvo: a situação na catraca muda com ele.
            await Parametrizacao.AcompanharAsync().ConfigureAwait(true);
        }

        Mensagem = $"Salvo para a catraca {inner}. Ela só passa a usar depois de \"Aplicar nesta catraca\".";
        Recalcular();
    }

    private async Task DesfazerAsync()
    {
        var inner = Catraca;
        await Parametrizacao.CarregarCatracaAsync(inner).ConfigureAwait(true);
        Problemas = [];
        Mensagem = $"Alterações desfeitas: a tela voltou ao que está salvo para a catraca {inner}.";
        Recalcular();
    }

    private async Task AplicarAsync()
    {
        var inner = Catraca;
        await Parametrizacao.ConfirmarAplicacao.ExecutarAsync().ConfigureAwait(true);
        Problemas = [.. Parametrizacao.Problemas];
        Mensagem = Parametrizacao.Mensagem;

        if (inner == Catraca)
        {
            await Giro.CarregarAsync(inner).ConfigureAwait(true);
        }

        Recalcular();
    }

    // ------------------------------------------------------------------ cálculo

    private void AoMudar(string? propriedade)
    {
        switch (propriedade)
        {
            case nameof(ParametrizacaoViewModel.Mudancas) or nameof(ParametrizacaoViewModel.Campos) or nameof(MapaDeGiroViewModel.Linhas)
                or nameof(ParametrizacaoViewModel.ModoTecnico) or nameof(ParametrizacaoViewModel.VersaoSalva)
                or nameof(ParametrizacaoViewModel.VersaoAplicada) or nameof(ParametrizacaoViewModel.Catraca):
                Recalcular();
                break;
            case nameof(ParametrizacaoViewModel.SituacaoNaCatraca) or nameof(ParametrizacaoViewModel.SinalDaSituacao)
                or nameof(ParametrizacaoViewModel.DetalheDaSituacao) or nameof(ParametrizacaoViewModel.Aplicacoes)
                or nameof(ParametrizacaoViewModel.ConfirmandoAplicacao) or nameof(ParametrizacaoViewModel.AvisosDoSalvo):
                Avisar(propriedade);
                Avisar(nameof(SituacaoCurta));
                AvisarNaCatraca();
                Reavaliar();
                break;
            default:
                break;
        }
    }

    private void Recalcular()
    {
        Mudancas =
        [
            .. Parametrizacao.Mudancas,
            .. Giro.Mudancas.Select(m => m with { Campo = $"Giro · {m.Campo}" }),
        ];
        Avisar(nameof(ResumoDasMudancas));
        Avisar(nameof(AlteracoesPendentes));
        Avisar(nameof(ModoTecnico));
        Avisar(nameof(Catraca));
        Avisar(nameof(TextoDaConfirmacao));
        Avisar(nameof(AvisosDoSalvo));
        AvisarPainel();
        AvisarNaCatraca();
        CalcularMarcas();
        Reavaliar();
    }

    private void CalcularMarcas()
    {
        var marcas = new List<MarcaDaPeca>();
        foreach (var peca in Enum.GetValues<PecaDaCatraca>())
        {
            var painel = ConfiguracaoPorPeca.De(peca);
            var campos = painel.Campos
                .Select(c => Parametrizacao.Campos.FirstOrDefault(x => x.Campo == c))
                .OfType<CampoDaParametrizacao>()
                .ToList();
            var linhas = painel.MostraGiro
                ? Giro.Linhas.Where(l => painel.OrigemDoGiro is null || l.Origem == painel.OrigemDoGiro).ToList()
                : [];
            var nome = CatalogoDaFit4.De(peca).Nome;

            if (campos.Any(c => c.Alterado) || linhas.Any(l => l.Alterada))
            {
                marcas.Add(new MarcaDaPeca(peca, TipoDeMarca.AlteracaoNaoSalva, $"{nome}: alteração não salva"));
            }

            if (campos.Any(c => c.ValorSalvo is { } salvo && !string.Equals(salvo, c.ValorHerdado, StringComparison.Ordinal))
                || linhas.Any(l => l.Regra.Definida))
            {
                marcas.Add(new MarcaDaPeca(peca, TipoDeMarca.DiferenteDoEvento, $"{nome}: diferente do padrão do evento"));
            }
        }

        if (!marcas.SequenceEqual(Marcas))
        {
            Marcas = marcas;
            VersaoDasMarcas++;
        }
    }

    private void AvisarPainel()
    {
        Avisar(nameof(SemPainel));
        Avisar(nameof(Ficha));
        Avisar(nameof(Painel));
        Avisar(nameof(TituloDoPainel));
        Avisar(nameof(CamposDaPeca));
        Avisar(nameof(NadaAConfigurar));
        Avisar(nameof(MostraGiro));
        Avisar(nameof(MostraMensagemTemporaria));
        Avisar(nameof(MostraAcertarRelogio));
        Avisar(nameof(MostraRefazerConexao));
        Avisar(nameof(MostraEquipamento));
        Avisar(nameof(MostraRele2));
        Avisar(nameof(AvisoDoModo));
    }

    private void AvisarNaCatraca()
    {
        Avisar(nameof(AplicadaConhecida));
        Avisar(nameof(TextoNaCatracaAgora));
        Avisar(nameof(NaCatracaAgora));
        Avisar(nameof(MensagemPadraoAplicada));
        Avisar(nameof(UrnaLigadaAplicada));
        Avisar(nameof(MotivoParaNaoAplicar));
    }

    private void Reavaliar()
    {
        Avisar(nameof(MotivoParaNaoAplicar));
        Salvar.ReavaliarDisponibilidade();
        Desfazer.ReavaliarDisponibilidade();
        PedirAplicacao.ReavaliarDisponibilidade();
        ConfirmarAplicacao.ReavaliarDisponibilidade();
        CancelarAplicacao.ReavaliarDisponibilidade();
    }

    private static string Curta(string versao) => versao.Length > 8 ? versao[..8] : versao;
}
