using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Contracts;
using Contracts.Edge.V1;
using Desktop.ViewModels;

namespace Desktop.App;

/// <summary>
/// Renderiza cada tela da janela real em PNG, sem precisar de alguém olhando o monitor.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque o projeto é construído em Linux, onde o WPF compila mas não roda. Roda na
/// máquina do evento, com o serviço local ligado: cada tela é preenchida pelo serviço de
/// verdade (as mesmas ViewModels da operação) e fotografada. Sem serviço, as telas saem com
/// a mensagem "sem resposta do serviço local" — o que também é uma imagem útil.
/// </para>
/// <para>
/// Limitação honesta: renderiza o <b>conteúdo</b> da janela, não a moldura do Windows.
/// </para>
/// <para>
/// Roda no CI do Windows (o runner tem sessão de desktop) com o serviço em modo simulação:
/// as imagens dos dois temas saem como artefato e no pré-lançamento, para revisão visual.
/// </para>
/// </remarks>
internal static class CapturaDeTela
{
    private const int Largura = 1366;
    private const int Altura = 768;

    /// <summary>Pontos por polegada da captura. 144 = uma vez e meia o padrão.</summary>
    private const double Resolucao = 144;

    /// <summary>Renderiza um PNG por tela na pasta indicada.</summary>
    /// <returns>Os arquivos gravados.</returns>
    internal static async Task<IReadOnlyList<string>> RenderizarAsync(string pasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        Directory.CreateDirectory(pasta);

        var endereco = Environment.GetEnvironmentVariable("EDGE_ENDERECO") ?? TransporteLocal.EnderecoPadrao();
        var token = InstalacaoLocal.LerToken();
        var janela = new JanelaViewModel(new EdgeControl.EdgeControlClient(TransporteLocal.CriarInvocadorDoPainel(endereco, token)));

        var gravados = new List<Captura>();
        var numero = 1;

        // A abertura (brand board, seção 08), parada no quadro final: marca e produto.
        var abertura = Path.Combine(pasta, $"00-abertura-{Rayzer.Design.TemaRayzer.Aplicado.ToLowerInvariant()}.png");
        Rayzer.Design.RayzerAbertura.SomenteQuadroFinal = true;
        try
        {
            gravados.Add(await FotografarAsync(new Border { Child = new Rayzer.Design.RayzerAbertura(), Width = Largura, Height = Altura }, abertura).ConfigureAwait(true));
        }
        finally
        {
            Rayzer.Design.RayzerAbertura.SomenteQuadroFinal = false;
        }

        var falhas = new List<string>();

        foreach (var tela in janela.Telas)
        {
            var nome = string.Create(
                CultureInfo.InvariantCulture,
                $"{numero:D2}-{Arquivo(tela.Titulo)}-{Rayzer.Design.TemaRayzer.Aplicado.ToLowerInvariant()}");
            numero++;

            // Uma tela que falha não leva as seguintes junto: o erro vai para um arquivo ao
            // lado das imagens e a captura segue. Antes, a primeira falha encerrava tudo, e
            // o erro ia para a saída de console, que num WinExe se perde.
            try
            {
                janela.TelaAtual = tela;

                // Com await, e nunca .GetResult(): as telas voltam para a thread da interface,
                // e bloqueá-la esperando por elas trava o processo para sempre.
                await janela.AtualizarAsync().ConfigureAwait(true);
                await tela.AtualizarAsync().ConfigureAwait(true);

                if (tela is GemeoDigitalViewModel gemeo)
                {
                    gravados.AddRange(await GravarGemeoAsync(pasta, nome, janela, gemeo).ConfigureAwait(true));
                }
                else
                {
                    gravados.Add(await GravarAsync(Path.Combine(pasta, nome + ".png"), janela).ConfigureAwait(true));
                }
            }
            catch (Exception erro) when (erro is not OutOfMemoryException)
            {
                var caminho = Path.Combine(pasta, nome + "-erro.txt");
                File.WriteAllText(caminho, erro.ToString());
                falhas.Add(caminho);
            }
        }

        // Etapa A.6: a Parametrização fica fora do menu (abre pela "Gerenciar catraca"), então
        // o laço acima não passa por ela e ela entra aqui, com o número que o docs/35 deu à
        // captura (as do menu não foram renumeradas).
        gravados.AddRange(await GravarParametrizacaoAsync(pasta, janela, falhas).ConfigureAwait(true));

        // Relatório em arquivo porque num WinExe a saída de console não é confiável. Cada
        // imagem sai com o tamanho em pixels, o DPI e o tamanho lógico: o gêmeo saía com
        // 1044×768 e ninguém percebia (docs/34 §7.1, P4).
        // Acrescenta: o CI roda uma vez por tema na mesma pasta, e o segundo apagava o primeiro.
        File.AppendAllText(Path.Combine(pasta, "relatorio.txt"), Relatar(gravados, falhas));

        // Fora de 1366×768 lógicos, um arquivo à parte: o CI reprova quando ele existe.
        var foraDoTamanho = gravados.Where(c => !c.NoTamanhoDaTela).ToList();
        var marcador = Path.Combine(pasta, ArquivoForaDoTamanho);
        if (foraDoTamanho.Count > 0)
        {
            File.AppendAllLines(marcador, foraDoTamanho.Select(c => c.Descrever()));
        }

        return [.. gravados.Select(c => c.Caminho)];
    }

    /// <summary>Existe só quando alguma imagem não saiu com 1366×768 lógicos (o CI reprova).</summary>
    internal const string ArquivoForaDoTamanho = "tamanho-fora-de-1366x768.txt";

    /// <summary>Uma imagem gravada, com o tamanho em pixels e o DPI em que foi desenhada.</summary>
    private sealed record Captura(string Caminho, int LarguraPx, int AlturaPx, double Dpi)
    {
        public int LarguraLogica => (int)Math.Round(LarguraPx * 96 / Dpi);

        public int AlturaLogica => (int)Math.Round(AlturaPx * 96 / Dpi);

        /// <summary>1366×768 lógicos: a resolução mínima suportada (docs/10), em qualquer DPI.</summary>
        public bool NoTamanhoDaTela => LarguraLogica == Largura && AlturaLogica == Altura;

        public string Descrever() => string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(Caminho)} — {LarguraPx}×{AlturaPx} px a {Dpi:0.#} dpi = {LarguraLogica}×{AlturaLogica} lógicos{(NoTamanhoDaTela ? string.Empty : " — FORA DE 1366×768")}");
    }

    private static string Relatar(List<Captura> capturas, List<string> falhas)
    {
        var texto = new System.Text.StringBuilder();
        texto.Append(CultureInfo.InvariantCulture, $"Tema {Rayzer.Design.TemaRayzer.Aplicado.ToLowerInvariant()} — {capturas.Count} imagem(ns) gravada(s):").AppendLine();
        foreach (var captura in capturas)
        {
            texto.AppendLine(captura.Descrever());
        }

        if (falhas.Count > 0)
        {
            texto.Append(CultureInfo.InvariantCulture, $"{falhas.Count} tela(s) com erro:").AppendLine();
            foreach (var falha in falhas)
            {
                texto.AppendLine(falha);
            }
        }

        return texto.ToString();
    }

    /// <summary>
    /// O gêmeo digital é fotografado numa janela aberta de verdade, não pelo
    /// <see cref="RenderTargetBitmap"/>: renderizar o Viewport3D fora de uma janela derrubava
    /// o processo com estouro de pilha (0xC00000FD) no CI, sem exceção que desse para pegar.
    /// Na janela aberta o 3D é desenhado pelo caminho normal, e a foto é a que o operador vê,
    /// com o laço de animação rodando.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A janela é uma <see cref="HwndSource"/> sem moldura, e não a <see cref="JanelaPrincipal"/>
    /// mostrada. Com a JanelaPrincipal, a foto saía com 1044×768 e o menu recolhido
    /// (docs/34 §7.1, P4): o monitor do runner do CI tem 1024×768, e o Windows limita uma
    /// janela com moldura ao tamanho do monitor mais a borda (SM_CXMAXTRACK = 1024 + 20). O
    /// próprio WPF repete esse limite na medida da Window (<c>GetWindowMinMax</c>), então nem
    /// pedir a largura de novo adiantava. A 1044 px, abaixo de <c>Rayzer.Breakpoint.Compact</c>,
    /// o menu recolhia sozinho, e a foto ainda levava a barra de título.
    /// </para>
    /// <para>
    /// Uma HwndSource popup não tem esse limite nem moldura: o conteúdo da JanelaPrincipal vai
    /// para a mesma moldura das outras capturas (<see cref="Moldura"/>, 1366×768 lógicos), a
    /// janela toma o tamanho dele em pixels do DPI do monitor (<see cref="HwndSource.SizeToContent"/>)
    /// e o menu fica aberto, como nas outras telas. Por garantia, a janela ainda responde a
    /// WM_GETMINMAXINFO sem teto. O tamanho vai para o relatório, e o CI reprova se não for
    /// 1366×768 lógicos.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<Captura>> GravarGemeoAsync(string pasta, string nome, JanelaViewModel vm, GemeoDigitalViewModel gemeo)
    {
        var gravados = new List<Captura>();
        var janela = new JanelaPrincipal(vm);
        var raiz = (FrameworkElement)janela.Content;
        janela.Content = null;
        var moldura = Moldura(raiz, vm);

        var parametros = new HwndSourceParameters("Rayzer XAcess — captura do gêmeo digital")
        {
            WindowStyle = WsPopup | WsVisible,
            ExtendedWindowStyle = WsExTopmost | WsExToolWindow,
            PositionX = 0,
            PositionY = 0,
            Width = Largura,
            Height = Altura,
            HwndSourceHook = SemTetoDeTamanho,
        };

        var fonte = new HwndSource(parametros) { SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            fonte.RootVisual = moldura;

            // 1. Como a tela abre: a catraca livre, a câmera na vista inicial.
            await EsperarAsync(moldura, TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            ConferirTamanho(moldura);
            gravados.Add(FotografarJanela(fonte, Path.Combine(pasta, nome + ".png")));

            // 2. No meio de um cenário, de frente: o QR lido, braço solto.
            await gemeo.MudarVista.ExecutarAsync("Frente").ConfigureAwait(true);
            await EsperarAsync(moldura, TimeSpan.FromSeconds(1.5)).ConfigureAwait(true);
            await gemeo.RodarRoteiro.ExecutarAsync(Desktop.ViewModels.GemeoDigital.Roteiros.QrValido).ConfigureAwait(true);
            await EsperarAsync(moldura, TimeSpan.FromSeconds(2.2)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(fonte, Path.Combine(pasta, nome + "-cenario.png")));

            // 3. As peças separadas, com a ficha dos braços.
            await EsperarAsync(moldura, TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            gemeo.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Rotor);
            await gemeo.FecharPainelDoGiro.ExecutarAsync().ConfigureAwait(true); // a captura 3 continua a de sempre
            gemeo.PecasSeparadas = true;
            await gemeo.MudarVista.ExecutarAsync("Inicial").ConfigureAwait(true);
            await EsperarAsync(moldura, TimeSpan.FromSeconds(2.5)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(fonte, Path.Combine(pasta, nome + "-separadas.png")));

            // 4. Mapa de giro (D9): clique na urna abre "Giro desta catraca" no leitor 2, com a
            //    seta no desenho e a pré-visualização do sentido. Nada é salvo nem enviado.
            gemeo.PecasSeparadas = false;
            gemeo.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
            await gemeo.CarregarGiroAsync().ConfigureAwait(true);
            await gemeo.MudarVista.ExecutarAsync("Bracos").ConfigureAwait(true);
            if (gemeo.Giro.LinhaEmFoco is { } urna)
            {
                await gemeo.Giro.PreVisualizar.ExecutarAsync(urna).ConfigureAwait(true);
            }

            await EsperarAsync(moldura, TimeSpan.FromSeconds(1.8)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(fonte, Path.Combine(pasta, nome + "-giro.png")));

            // 5. Gêmeo como central da configuração (docs/33 §9): o painel do leitor da frente
            //    aberto ao lado do desenho, com o tipo de leitor, a origem e o selo do 5 × 8.
            await gemeo.PararRoteiro.ExecutarAsync().ConfigureAwait(true);
            gemeo.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.LeitorQr);
            await gemeo.MudarVista.ExecutarAsync("Painel").ConfigureAwait(true);
            await EsperarAsync(moldura, TimeSpan.FromSeconds(2)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(fonte, Path.Combine(pasta, nome + "-config-peca.png")));

            // 6. Alterações pendentes em duas peças (display e urna), nada salvo: as marcações no
            //    desenho e o único "o que muda" embaixo dele, com Salvar e Aplicar.
            gemeo.Central.Operador = "Operador sintetico";
            gemeo.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Display);
            foreach (var campo in gemeo.Central.CamposDaPeca)
            {
                campo.Herda = false;
                campo.Valor = "Entrada pelo portao 2";
            }

            gemeo.Escolher(Desktop.ViewModels.GemeoDigital.PecaDaCatraca.Urna);
            foreach (var campo in gemeo.Central.CamposDaPeca)
            {
                campo.Herda = false;
                campo.Escolhida = campo.Opcoes.FirstOrDefault(o => o.Valor == "0") ?? campo.Escolhida;
            }

            await gemeo.MudarVista.ExecutarAsync("Inicial").ConfigureAwait(true);
            await EsperarAsync(moldura, TimeSpan.FromSeconds(1.5)).ConfigureAwait(true);
            Descendente<Telas.Gemeo>(moldura)?.MostrarConfiguracao();
            await EsperarAsync(moldura, TimeSpan.FromSeconds(1.5)).ConfigureAwait(true);
            gravados.Add(FotografarJanela(fonte, Path.Combine(pasta, nome + "-config-pendente.png")));
        }
        finally
        {
            gemeo.PecasSeparadas = false;

            // Nada da captura fica salvo: o rascunho volta ao que está gravado.
            await gemeo.Central.Desfazer.ExecutarAsync().ConfigureAwait(true);
            await gemeo.FecharPainelDoGiro.ExecutarAsync().ConfigureAwait(true);

            // Sem a raiz, a tela recebe Unloaded e desliga o laço de animação antes de a janela sumir.
            fonte.RootVisual = null;
            fonte.Dispose();
            janela.Close();
        }

        return gravados;
    }

    /// <summary>
    /// Três fotos da Parametrização da catraca (Etapa A.6 do docs/35; a terceira, a aba Giro do
    /// mapa de giro, <c>09-parametrizacao-giro-{tema}</c>):
    /// <c>09-parametrizacao-{tema}</c>, como a tela abre (modo guiado, aba Leitura), e
    /// <c>09-parametrizacao-diff-{tema}</c>, com duas alterações não salvas na lista "o que
    /// muda (atual → novo)", no modo técnico e na aba Instalação, onde ficam os campos que
    /// aguardam confirmação (desabilitados, com o selo e o motivo).
    /// </summary>
    /// <remarks>
    /// Nada é salvo nem aplicado: as alterações ficam só na tela e são desfeitas no fim, para o
    /// serviço da captura continuar como estava.
    /// </remarks>
    private static async Task<IReadOnlyList<Captura>> GravarParametrizacaoAsync(string pasta, JanelaViewModel janela, List<string> falhas)
    {
        var tema = Rayzer.Design.TemaRayzer.Aplicado.ToLowerInvariant();
        var nome = $"09-parametrizacao-{tema}";
        var gravados = new List<Captura>();
        var tela = janela.Parametrizacao;

        try
        {
            tela.ModoTecnico = false;
            tela.AbaSelecionada = (int)AbaDaParametrizacao.Leitura;
            janela.TelaAtual = tela;
            await janela.AtualizarAsync().ConfigureAwait(true);
            await tela.AtualizarAsync().ConfigureAwait(true);
            gravados.Add(await GravarAsync(Path.Combine(pasta, nome + ".png"), janela).ConfigureAwait(true));

            // O rascunho que um operador faria: duas mudanças, nada salvo.
            foreach (var campo in tela.Campos)
            {
                if (campo.Campo is CampoDaCatraca.TempoDoAcionamento1)
                {
                    campo.Herda = false;
                    campo.Valor = "7";
                }
                else if (campo.Campo is CampoDaCatraca.MensagemPadrao)
                {
                    campo.Herda = false;
                    campo.Valor = "Entrada pelo portao 2";
                }
            }

            tela.ModoTecnico = true;
            tela.AbaSelecionada = (int)AbaDaParametrizacao.Instalacao;
            gravados.Add(await GravarAsync(Path.Combine(pasta, $"09-parametrizacao-diff-{tema}.png"), janela).ConfigureAwait(true));

            // A aba Giro (mapa de giro, D9), no modo guiado, como o operador a abre.
            tela.ModoTecnico = false;
            tela.AbaSelecionada = (int)AbaDaParametrizacao.Giro;
            gravados.Add(await GravarAsync(Path.Combine(pasta, $"09-parametrizacao-giro-{tema}.png"), janela).ConfigureAwait(true));
        }
        catch (Exception erro) when (erro is not OutOfMemoryException)
        {
            var caminho = Path.Combine(pasta, nome + "-erro.txt");
            File.WriteAllText(caminho, erro.ToString());
            falhas.Add(caminho);
        }
        finally
        {
            tela.ModoTecnico = false;
            tela.AbaSelecionada = (int)AbaDaParametrizacao.Leitura;
        }

        return gravados;
    }

    /// <summary>
    /// A moldura tem 1366×768 fixos: se o layout não ficou com esse tamanho, a foto não é da
    /// tela que o operador vê em 1366×768, e é melhor falhar com o motivo do que gravar outra.
    /// </summary>
    private static void ConferirTamanho(FrameworkElement moldura)
    {
        if (Math.Abs(moldura.ActualWidth - Largura) > 0.5 || Math.Abs(moldura.ActualHeight - Altura) > 0.5)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A tela do gêmeo ficou com {moldura.ActualWidth:0.#}×{moldura.ActualHeight:0.#} lógicos, e não {Largura}×{Altura}."));
        }
    }

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WmGetMinMaxInfo = 0x0024;

    /// <summary>
    /// Tira o teto de tamanho da janela da captura: o padrão do Windows é o tamanho do
    /// monitor, e o runner do CI tem 1024×768 (ver <see cref="GravarGemeoAsync"/>).
    /// </summary>
    private static IntPtr SemTetoDeTamanho(IntPtr janela, int mensagem, IntPtr w, IntPtr l, ref bool tratada)
    {
        if (mensagem == WmGetMinMaxInfo && l != IntPtr.Zero)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(l);
            info.MaxTrackX = Math.Max(info.MaxTrackX, 16384);
            info.MaxTrackY = Math.Max(info.MaxTrackY, 16384);
            System.Runtime.InteropServices.Marshal.StructureToPtr(info, l, fDeleteOld: false);
            tratada = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>O primeiro elemento de um tipo na árvore visual, em largura.</summary>
    private static T? Descendente<T>(DependencyObject raiz)
        where T : DependencyObject
    {
        var fila = new Queue<DependencyObject>([raiz]);
        while (fila.Count > 0)
        {
            var atual = fila.Dequeue();
            if (atual is T achado)
            {
                return achado;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(atual); i++)
            {
                fila.Enqueue(VisualTreeHelper.GetChild(atual, i));
            }
        }

        return null;
    }

    private static async Task EsperarAsync(FrameworkElement tela, TimeSpan quanto)
    {
        await Task.Delay(quanto).ConfigureAwait(true);
        await tela.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Copia a janela como o Windows a desenhou (PrintWindow com PW_RENDERFULLCONTENT, o mesmo
    /// recurso da foto do Setup no CI): funciona mesmo com parte da janela fora do monitor.
    /// </summary>
    private static Captura FotografarJanela(HwndSource fonte, string caminho)
    {
        var alca = fonte.Handle;
        if (!GetWindowRect(alca, out var r))
        {
            throw new InvalidOperationException("Não foi possível ler o tamanho da janela para a foto.");
        }

        // O DPI do monitor em que a janela foi desenhada: 96 a 100 %, 144 a 150 %.
        var dpi = 96.0 * (fonte.CompositionTarget?.TransformToDevice.M11 ?? 1.0);

        using var foto = new System.Drawing.Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        foto.SetResolution((float)dpi, (float)dpi);
        using (var g = System.Drawing.Graphics.FromImage(foto))
        {
            var dc = g.GetHdc();
            try
            {
                if (!PrintWindow(alca, dc, 2))
                {
                    throw new InvalidOperationException("O Windows recusou a foto da janela (PrintWindow).");
                }
            }
            finally
            {
                g.ReleaseHdc(dc);
            }
        }

        foto.Save(caminho, System.Drawing.Imaging.ImageFormat.Png);
        return new Captura(caminho, foto.Width, foto.Height, dpi);
    }

    // Preenchida pelo Windows em GetWindowRect: o compilador não vê a atribuição.
#pragma warning disable CS0649
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Retangulo
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // MINMAXINFO do Windows. Só o teto de arraste (ptMaxTrackSize) é mudado; o resto volta como veio.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public int ReservadoX;
        public int ReservadoY;
        public int MaxSizeX;
        public int MaxSizeY;
        public int MaxPositionX;
        public int MaxPositionY;
        public int MinTrackX;
        public int MinTrackY;
        public int MaxTrackX;
        public int MaxTrackY;
    }
#pragma warning restore CS0649

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr janela, out Retangulo retangulo);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr janela, IntPtr dc, uint opcoes);

    private static async Task<Captura> GravarAsync(string caminho, JanelaViewModel vm)
    {
        var janela = new JanelaPrincipal(vm);

        // A janela nunca é exibida. O conteúdo é desligado dela e medido num contêiner
        // próprio: renderizar uma Window que nunca apareceu devolve imagem vazia.
        var raiz = (FrameworkElement)janela.Content;
        janela.Content = null;

        var captura = await FotografarAsync(Moldura(raiz, vm), caminho).ConfigureAwait(true);
        janela.Close();
        return captura;
    }

    /// <summary>
    /// O conteúdo da janela numa moldura de 1366×768 lógicos, com o fundo, o texto e a fonte
    /// que a janela daria (Rayzer.Janela), para a captura sair no tema.
    /// </summary>
    private static Border Moldura(FrameworkElement raiz, JanelaViewModel vm)
    {
        var moldura = new Border
        {
            Child = raiz,
            Width = Largura,
            Height = Altura,
            DataContext = vm,
        };

        moldura.SetResourceReference(Border.BackgroundProperty, "Rayzer.Background");
        moldura.SetResourceReference(TextElement.ForegroundProperty, "Rayzer.Text.Primary");
        moldura.SetResourceReference(TextElement.FontFamilyProperty, "Rayzer.Font.Text");
        moldura.SetResourceReference(TextElement.FontSizeProperty, "Rayzer.FontSize.Body");
        return moldura;
    }

    private static async Task<Captura> FotografarAsync(Border moldura, string caminho)
    {
        moldura.Measure(new Size(Largura, Altura));
        moldura.Arrange(new Rect(0, 0, Largura, Altura));
        moldura.UpdateLayout();

        // A DataGrid calcula as colunas de largura proporcional (Width="*") num passo
        // adiado do despachante. Sem deixá-lo rodar, a captura saía com essas colunas em
        // largura zero — o aplicativo aberto não tem esse problema, só a foto.
        await moldura.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        moldura.UpdateLayout();

        var escala = Resolucao / 96.0;
        var bitmap = new RenderTargetBitmap(
            (int)(Largura * escala),
            (int)(Altura * escala),
            Resolucao,
            Resolucao,
            PixelFormats.Pbgra32);

        bitmap.Render(moldura);

        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(bitmap));

        using (var arquivo = File.Create(caminho))
        {
            codificador.Save(arquivo);
        }

        return new Captura(caminho, bitmap.PixelWidth, bitmap.PixelHeight, Resolucao);
    }

    private static string Arquivo(string titulo) =>
        new string([.. titulo.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ')])
        .Replace(' ', '-');

    internal static string Resumir(IReadOnlyList<string> arquivos, IReadOnlyList<string>? falhas = null)
    {
        var resumo = string.Create(
            CultureInfo.InvariantCulture,
            $"{arquivos.Count} imagem(ns) gravada(s):{Environment.NewLine}{string.Join(Environment.NewLine, arquivos)}");

        return falhas is { Count: > 0 }
            ? resumo + string.Create(
                CultureInfo.InvariantCulture,
                $"{Environment.NewLine}{falhas.Count} tela(s) com erro:{Environment.NewLine}{string.Join(Environment.NewLine, falhas)}")
            : resumo;
    }
}
