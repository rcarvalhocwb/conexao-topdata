using System.Globalization;
using Access.Application.Devices;
using Access.Application.Ingressos;
using Access.Infrastructure.SQLite;
using Edge.Worker;
using Edge.Worker.Bancada;
using Edge.Worker.Operacao;
using Edge.Worker.Resiliencia;
using Topdata.EasyInner.Adapter;

namespace Edge.Worker.X86;

/// <summary>
/// Hospedeiro do worker: confere pré-requisitos e sobe o laço.
/// </summary>
/// <remarks>
/// Deliberadamente fino. Toda a lógica está em <c>Edge.Worker</c>, que roda e é testada
/// em qualquer plataforma; aqui só se decide qual adapter injetar. Um assembly x86 não
/// carrega num processo de teste x64, então código que precisa de teste não pode morar
/// neste projeto.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("Conexao Topdata — Edge.Worker.X86");

        var preRequisitos = VerificadorDePreRequisitos.Verificar();

        foreach (var item in preRequisitos)
        {
            var marca = item.Atendido switch
            {
                true => "ok  ",
                false => "FALHA",
                null => "conferir",
            };

            Console.WriteLine($"[{marca}] {item.Id}: {item.Mensagem}");
        }

        // Modo simulação: catracas simuladas, sem EasyInner.dll e sem hardware. O resto —
        // decisão, base, giro, envio à nuvem — é o caminho de verdade.
        if (Array.IndexOf(args, "--simulador") >= 0 && Valor(args, "--banco") is { } bancoSimulado)
        {
            var portaSimulada = LerPorta(args);
            // Relógio de verdade: sem ele o simulador usa a data fixa dos testes e o painel
            // mostrava "último evento há 99 h" logo depois de uma passagem.
            using var simulador = new Simulator.InnerSimulator(() => DateTimeOffset.UtcNow);
            simulador.AbrirPorta(portaSimulada);
            Console.WriteLine($"MODO SIMULAÇÃO: catracas simuladas na porta {portaSimulada}; nenhuma catraca física é acionada.");
            return ExecutarOperacao(simulador, portaSimulada, bancoSimulado, args, simulador);
        }

        var impeditivos = VerificadorDePreRequisitos.Impeditivos(preRequisitos);
        if (impeditivos.Count > 0)
        {
            Console.Error.WriteLine($"{impeditivos.Count} pré-requisito(s) impeditivo(s). O worker não sobe.");
            return 1;
        }

        // O adapter real existe desde que o SDK 6.0.2.0 chegou. O que ainda não aconteceu é
        // ele conversar com uma catraca: este passo é o ensaio HIL-STACK-01, e a primeira
        // chamada nativa é onde se descobre se um processo .NET 10 de 32 bits carrega a DLL.
        var porta = LerPorta(args);

        using var adapter = new TopdataInnerAdapter();

        Console.WriteLine($"Abrindo a porta {porta}...");

        try
        {
            var abertura = adapter.AbrirPorta(porta);
            Console.WriteLine($"  {abertura}");

            if (abertura.Status is AdapterStatus.FalhaDeDependencia && abertura.Significado is { } dependencia)
            {
                // Retornos 4 a 6 de AbrirPortaComunicacao (EI-002): DLL de apoio ausente.
                Console.Error.WriteLine(
                    $"Retorno {abertura.NativeReturn} ({dependencia}). Rode installer/verificar-ambiente.ps1.");
                return 2;
            }

            if (abertura.Status is AdapterStatus.FalhaDeDependencia)
            {
                Console.Error.WriteLine(
                    "Retorno 8 (GPF). As causas documentadas são: DLL não registrada, .NET " +
                    "Framework 3.5 ausente, versões incompatíveis das DLLs de apoio, ou " +
                    "arquitetura errada. Rode installer/verificar-ambiente.ps1.");
                return 2;
            }

            if (abertura.Status is not AdapterStatus.Ok)
            {
                Console.Error.WriteLine("A porta não abriu. O worker não sobe sem ela.");
                return 1;
            }

            if (Array.IndexOf(args, "--bancada") >= 0)
            {
                return ExecutarBancada(adapter, args);
            }

            if (Valor(args, "--banco") is { } banco)
            {
                return ExecutarOperacao(adapter, porta, banco, args);
            }

            Console.WriteLine(
                "Porta aberta. Para o ensaio com catraca, rode com --bancada (docs/21); " +
                "em operação, o serviço passa --banco.");
            return 0;
        }
        catch (DllNotFoundException erro)
        {
            // É aqui que HIL-STACK-01 responde "não" — e é uma resposta, não um defeito.
            Console.Error.WriteLine(
                $"A EasyInner.dll não foi encontrada ou não pôde ser carregada: {erro.Message}");
            Console.Error.WriteLine(
                "Se o ambiente estiver correto e mesmo assim falhar, o caminho é mover só este " +
                "hospedeiro para .NET Framework 4.8, atrás do mesmo IPC. Ver docs/12.");
            return 2;
        }
        catch (BadImageFormatException erro)
        {
            Console.Error.WriteLine(
                $"A DLL foi encontrada mas é de arquitetura incompatível: {erro.Message}");
            Console.Error.WriteLine("Este processo precisa ser de 32 bits. Confira PlatformTarget.");
            return 2;
        }
    }

    /// <summary>
    /// Ensaio de bancada: o laço de verdade contra catracas de verdade, decidindo pela base
    /// local e mostrando cada passo. Ver docs/21-roteiro-da-bancada.md.
    /// </summary>
    private static int ExecutarBancada(TopdataInnerAdapter adapter, string[] args)
    {
        var caminhoDoBanco = Valor(args, "--banco") ?? "bancada.db";
        var fabrica = new SqliteConnectionFactory(caminhoDoBanco);
        new Migrator(fabrica).Aplicar();
        var repositorio = new RepositorioDeIngressos(fabrica);

        Console.WriteLine($"Base local: {Path.GetFullPath(caminhoDoBanco)}");

        var arquivo = Valor(args, "--bancada");
        if (arquivo is not null && !arquivo.StartsWith("--", StringComparison.Ordinal))
        {
            var carga = ArquivoDeBancada.Carregar(File.ReadAllText(arquivo), repositorio, DateTimeOffset.UtcNow);
            Console.WriteLine(
                $"Carga: {carga.Provedores} provedor(es), {carga.Ingressos} ingresso(s), {carga.Cartoes} cartão(ões).");
            foreach (var problema in carga.Problemas)
            {
                Console.WriteLine($"  atenção: {problema}");
            }
        }

        var inners = (Valor(args, "--inner") ?? "1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
            .ToList();

        var tipoDeLeitor = byte.Parse(Valor(args, "--tipo-leitor") ?? "8", CultureInfo.InvariantCulture);
        var comUrna = Array.IndexOf(args, "--sem-urna") < 0;

        Console.WriteLine(
            $"Catracas: {string.Join(", ", inners)} · tipo de leitor {tipoDeLeitor} · leitor da urna {(comUrna ? "ligado" : "desligado")}");
        Console.WriteLine("ATENÇÃO: o código lido aparece inteiro na tela. Use só cartões e ingressos de teste.");
        Console.WriteLine("Ctrl+C encerra e mostra a prestação de contas.");
        Console.WriteLine();

        var sessao = new SessaoDeBancada(
            adapter,
            inners,
            ConfiguracaoDeBancada.TopFit4(tipoDeLeitor, comUrna),
            new DecisorDeIngresso(repositorio),
            Console.WriteLine);

        using var cancelamento = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancelamento.Cancel();
        };

        sessao.Executar(cancelamento.Token);

        Console.WriteLine();
        Console.WriteLine(sessao.Resumo());

        var agora = DateTimeOffset.UtcNow;
        foreach (var provedor in repositorio.Provedores())
        {
            var conta = repositorio.Conciliar(provedor.Id, agora);
            Console.WriteLine(
                $"{provedor.Id}: consumidos {conta.UsosConsumidos} · com giro {conta.UsosComPassagemFisica} · " +
                $"sem giro {conta.UsosSemPassagemFisica} · negados {conta.TentativasNegadas.Values.Sum()}");
        }

        var (tentativas, codigos) = repositorio.QrDesconhecidos(agora);
        Console.WriteLine($"códigos desconhecidos: {tentativas} tentativa(s), {codigos} código(s) distinto(s)");

        return 0;
    }

    /// <summary>
    /// Operação: o laço de verdade, sem tela, decidindo pela base local compartilhada com o
    /// serviço. É como o serviço sobe o worker (ADR-0024).
    /// </summary>
    private static int ExecutarOperacao(
        ITopdataInnerAdapter adapter,
        int porta,
        string banco,
        string[] args,
        Simulator.InnerSimulator? simulador = null)
    {
        var nome = Valor(args, "--worker") ?? $"porta-{porta}";
        var pastaDeRegistros = Valor(args, "--registros")
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(banco)) ?? ".", "registros");
        var registro = new RegistroEmArquivo(pastaDeRegistros, $"worker-{nome}");

        void Registrar(string linha)
        {
            registro.Escrever(linha);
            Console.WriteLine(linha);
        }

        var fabrica = new SqliteConnectionFactory(banco);
        new Migrator(fabrica).Aplicar();

        var (configuracao, ilegiveis) = new ConfiguracoesDaBorda(fabrica).Ler();
        foreach (var chave in ilegiveis)
        {
            Registrar($"configuração '{chave}' ilegível; usando o padrão.");
        }

        var problemas = configuracao.Validar();
        if (problemas.Count > 0)
        {
            // Configuração ruim não pode parar a catraca: sobe com o padrão e avisa.
            Registrar("configuração do evento inválida, usando o padrão: " + string.Join(" ", problemas));
            configuracao = new ConfiguracaoDaOperacao();
        }

        // Retorno ≠ 0 de ReceberDadosOnLine: reconectar só com a chave técnica, desligada até
        // HIL-EVT-01 (F6, docs/34 §2). Vale para a DLL real; o simulador declara a queda por si.
        // Como o espelho, muda no próximo início do worker, não no "Aplicar agora".
        if (adapter is TopdataInnerAdapter real)
        {
            real.ReconectarEmErroDeRecepcao = configuracao.ReconectarEmErroDeRecepcao;
            if (real.ReconectarEmErroDeRecepcao)
            {
                Registrar("reconexão em erro de recepção ligada (chave técnica, ensaio HIL-EVT-01)");
            }
        }

        // Sequência oficial de conexão (cfg off-line → mudança → cfg on-line): só com a chave
        // técnica, desligada até INT-SM-021 (Etapa A.7, docs/34 §4.3). É do laço: vale para todas
        // as catracas deste worker e muda no próximo início, não no "Aplicar agora".
        if (configuracao.SequenciaOficial)
        {
            Registrar("sequência oficial de conexão ligada (chave técnica, ensaio INT-SM-021)");
        }

        // Texto do giro no display (mapa de giro, D9): só com a chave técnica, desligada até
        // NOVO-HIL-DIR-12. Do laço, como a sequência oficial: muda no próximo início.
        if (configuracao.ExibirTextoDoGiro)
        {
            Registrar("texto do giro no display ligado (chave técnica, ensaio NOVO-HIL-DIR-12)");
        }

        var espelho = configuracao.EspelhoLigado
            ? new EspelhoDeTentativas(configuracao.ConectorDoEspelho, TimeSpan.FromSeconds(configuracao.EsperaPeloGiroSegundos))
            : null;
        var repositorio = new RepositorioDeIngressos(fabrica, espelho);
        var operacao = new Access.Infrastructure.SQLite.Operacao(fabrica);

        var inners = (Valor(args, "--inners") ?? Valor(args, "--inner") ?? "1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.Parse(v, CultureInfo.InvariantCulture))
            .ToList();

        // Etapa A.4 do docs/35: cada catraca com a sua configuração — fábrica → evento →
        // camada da catraca (device_config, A.3), pelo MontadorDaConfiguracao. Camada ilegível
        // ou recusada vira aviso e aquela catraca sobe com o padrão dela (fábrica + evento),
        // sem derrubar as outras (ConfiguracaoComRecuo). Com a tabela vazia, é o de antes.
        // EnviarDigitosVariaveis e as chaves da Etapa A.2 continuam do evento, desligadas por
        // padrão (docs/34 §2, F2, e §4.1).
        var porCatraca = new ConfiguracaoPorCatraca(fabrica);
        var configuracaoDasCatracas = new Dictionary<int, DeviceConfiguration>();
        foreach (var inner in inners)
        {
            var (daCatraca, avisos) = porCatraca.NaSubida(inner, configuracao);
            foreach (var aviso in avisos)
            {
                Registrar("configuração da catraca, aviso: " + aviso);
            }

            // O que é permitido mas depende de bancada (docs/34 §4.2, regras 9 e 11): fica no
            // registro do worker, sem impedir a subida.
            foreach (var alerta in daCatraca.Alertas())
            {
                Registrar(string.Create(CultureInfo.InvariantCulture, $"inner-{inner}: configuração da catraca, atenção: {alerta}"));
            }

            Registrar(string.Create(
                CultureInfo.InvariantCulture,
                $"inner-{inner}: leitor {daCatraca.TipoDeLeitor} · leitor 2 {daCatraca.OperacaoDoLeitor2} · " +
                $"relé 1 {daCatraca.TempoDoAcionamento1} s · liberação {daCatraca.PerfilFisico.FuncaoDeLiberacaoDaEntrada}" +
                $"{(daCatraca.PerfilFisico.MapaDeGiro.EstaVazio ? string.Empty : " · mapa de giro próprio")}"));

            configuracaoDasCatracas[inner] = daCatraca;
        }

        // "Aplicar agora" (fase 4b), por catraca desde a A.4: relê o evento e a camada da
        // catraca do comando, e só ela reconecta. Só o que vai para a catraca muda sem
        // reiniciar — leitor, urna, tempo, mensagem, a camada da catraca e as chaves técnicas
        // da configuração. Nuvem e espera pelo giro continuam valendo a partir do próximo
        // início do worker. Qualquer problema (do evento, de leitura da camada ou do
        // Validar) faz o comando falhar e a catraca segue com o que tinha.
        (DeviceConfiguration? Configuracao, IReadOnlyList<string> Problemas) Recarregar(int inner) =>
            porCatraca.ParaAplicar(inner);

        Registrar(string.Create(
            CultureInfo.InvariantCulture,
            $"worker {nome} · porta {porta} · catracas {string.Join(", ", inners)} · leitor {configuracao.TipoDeLeitor} · " +
            $"urna {(configuracao.LeitorDaUrna ? "ligada" : "desligada")} · nuvem {(espelho is null ? "desligada" : "ligada")}"));

        // Coleta de bilhetes (Etapa A.9): cada bilhete vai para collected_ticket (015) antes do
        // próximo, com máscara e impressão — a chave chega do serviço pela entrada padrão. Sem
        // ela, não há gravador e o comando de coleta falha sem tocar na catraca. O comando só
        // é aceito com a chave técnica catraca.coletar_bilhetes, lida a cada pedido.
        var gravadorDeBilhetes = LerChaveDaImpressao(args, Registrar) is { } impressao
            ? new BilhetesColetados(fabrica, impressao)
            : null;
        var configuracoesDaBorda = new ConfiguracoesDaBorda(fabrica);

        // A partida do serviço que subiu este worker (migração 016). Vai junto de cada situação
        // gravada, com "simulada ou real": o serviço só acredita na situação da partida dele, e um
        // worker órfão de uma partida anterior nunca mais aparece como "Atendendo" (docs/29).
        var partida = Valor(args, "--sessao");
        if (partida is { Length: > 64 } || string.IsNullOrWhiteSpace(partida))
        {
            if (partida is not null)
            {
                Registrar("identificador de sessão do serviço inválido; a situação vai sem ele.");
            }

            partida = null;
        }

        var simulada = simulador is not null;

        var sessao = new SessaoDeOperacao(
            adapter,
            inners,
            inner => configuracaoDasCatracas[inner],
            new DecisorDeIngresso(repositorio),
            Registrar,
            situacoes => operacao.GravarSituacao(
                [.. situacoes.Select(c => new SituacaoDoEquipamento(
                    c.DeviceId, c.Inner, nome, c.Estado.ToString(), c.EmOperacao, c.Firmware,
                    c.TentativasDeReconexao, c.UltimoEventoEm, c.UltimaDecisao, DateTimeOffset.UtcNow,
                    c.RelogioAcertadoEm, c.RelogioConferidoEm,
                    c.DivergenciaDoRelogio is { } divergencia ? (int)divergencia.TotalSeconds : null,
                    c.RelogioDivergente,
                    c.ConfiguracaoAplicadaEm, c.ConfiguracaoVersao,
                    partida, simulada))]),
            comandos: new FilaDeComandosSqlite(fabrica),
            recarregarConfiguracao: Recarregar,
            acertarRelogioAoDivergir: configuracao.AcertarRelogioAoDivergir,
            sequenciaOficial: configuracao.SequenciaOficial,
            exibirTextoDoGiro: configuracao.ExibirTextoDoGiro,
            gravadorDeBilhetes: gravadorDeBilhetes,
            coletaLigada: () => configuracoesDaBorda.Ler() is var (lida, ilegiveis)
                && lida.ColetarBilhetes
                && !ilegiveis.Contains(ConfiguracoesDaBorda.ChaveColetarBilhetes));

        using var cancelamento = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancelamento.Cancel();
        };

        // O worker morre com o serviço (--pai): se o processo que o subiu sumir sem matá-lo
        // (Gerenciador de Tarefas, queda), ele encerra sozinho em vez de seguir gravando
        // situação na base como órfão. Parada limpa primeiro; à força se a DLL o prender.
        using var vigia = VigiaDoPai(args, cancelamento, Registrar);
        vigia?.Iniciar();

        Action? aCadaVolta = null;

        if (simulador is not null)
        {
            var leituras = new LeiturasSimuladas(fabrica);
            var conducao = new Simulator.ConducaoDeLeituras(
                simulador,
                catracas => [.. leituras.Retirar(catracas, DateTimeOffset.UtcNow)
                    .Select(l => new Simulator.LeituraParaSimular(l.Inner, l.Codigo, l.NaUrna, l.Girar))],
                inners);

            // O simulador responde na hora; sem pausa, o laço ocuparia um núcleo inteiro.
            aCadaVolta = () =>
            {
                conducao.UmaVolta();
                Thread.Sleep(20);
            };
        }

        sessao.Executar(cancelamento.Token, aCadaVolta);
        Registrar("encerrado.");
        return 0;
    }

    /// <summary>
    /// A chave da impressão de código, entregue pelo serviço na entrada padrão (uma linha em
    /// Base64), quando ele passa <c>--chave-da-impressao-na-entrada</c>. Nunca vai para o registro.
    /// </summary>
    private static Access.Domain.Credentials.ImpressaoDeCodigo? LerChaveDaImpressao(string[] args, Action<string> registrar)
    {
        if (Array.IndexOf(args, "--chave-da-impressao-na-entrada") < 0)
        {
            registrar("sem a chave da impressão de código: a coleta de bilhetes fica recusada neste worker.");
            return null;
        }

        var linha = Console.In.ReadLine();
        var chave = new byte[linha?.Length ?? 0];
        if (linha is null
            || !Convert.TryFromBase64String(linha.Trim(), chave, out var tamanho)
            || tamanho < Access.Domain.Credentials.ImpressaoDeCodigo.TamanhoMinimoDaChave)
        {
            registrar("chave da impressão de código ilegível na entrada: a coleta de bilhetes fica recusada neste worker.");
            return null;
        }

        return new Access.Domain.Credentials.ImpressaoDeCodigo(chave.AsSpan(0, tamanho));
    }

    /// <summary>
    /// A vigia do processo pai, quando o serviço informa <c>--pai &lt;pid&gt;</c>. Sem o
    /// argumento (bancada, terminal), nada: quem roda à mão encerra à mão.
    /// </summary>
    private static VigiaDoProcessoPai? VigiaDoPai(string[] args, CancellationTokenSource cancelamento, Action<string> registrar)
    {
        if (Valor(args, "--pai") is not { } texto)
        {
            return null;
        }

        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
        {
            registrar("--pai inválido; o worker não vigia o serviço.");
            return null;
        }

        return new VigiaDoProcessoPai(
            VigiaDoProcessoPai.PaiPorPid(pid),
            encerrar: () =>
            {
                registrar(string.Create(
                    CultureInfo.InvariantCulture,
                    $"o serviço que subiu este worker (processo {pid}) não existe mais: encerrando."));
                cancelamento.Cancel();
            },
            encerrarAForca: () =>
            {
                registrar("a parada limpa não terminou a tempo: saída forçada.");
                Environment.Exit(3);
            });
    }

    private static string? Valor(string[] args, string nome)
    {
        var indice = Array.IndexOf(args, nome);
        return indice >= 0 && indice + 1 < args.Length ? args[indice + 1] : null;
    }

    /// <summary>Lê a porta de <c>--porta N</c>. O supervisor sempre a informa.</summary>
    private static int LerPorta(string[] args)
    {
        var indice = Array.IndexOf(args, "--porta");

        return indice >= 0
            && indice + 1 < args.Length
            && int.TryParse(args[indice + 1], out var porta)
                ? porta
                : VerificadorDePreRequisitos.PortaPadrao;
    }
}
