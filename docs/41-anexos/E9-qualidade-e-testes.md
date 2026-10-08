> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

E9: Qualidade e Testes. Relatório final, commit 29df501, somente leitura.

Respeitei as regras: não editei nem criei arquivos no repositório e não rodei build nem test. Usei Read, Grep, git e `gh api` só de leitura.

Cobertura da leitura:
- Li inteiros todos os arquivos de Unit, Contract, CrashProbe, LoadAndSoak e HardwareInLoop, além de `.github/workflows/ci.yml`.
- Em Integration, li inteiros 61 dos 63 arquivos. SimuladorTests e TelasTests foram só varridos por grep, por decisão do coordenador.

Histórico do CI: consultei as últimas 60 execuções por `gh api`. Foram 21 com sucesso, 8 com falha e 31 canceladas pelo `cancel-in-progress`.

## A) Achados

### E9-1 | ALTO | O CHAOS-KILL-01 prova durabilidade num caminho que a produção não usa

**Onde está:**
- `tests/Integration/QuedaAbruptaTests.cs:49`
- `tests/Integration/QuedaAbruptaTests.cs:96`
- `tests/CrashProbe/Program.cs:56`
- `tests/CrashProbe/Program.cs:76`

**Teste/job:** QuedaAbruptaTests (2 métodos) e a cobaia do CrashProbe.

**O que faz:**
- Mata a cobaia com SIGKILL no meio da gravação.
- Confere que nenhum acesso confirmado se perdeu nem foi duplicado em `access_decision` e na outbox.
- A gravação passa por `AccessJournal.Registrar`.

**Por que é problema:**
- `grep AccessJournal src/` não acha nenhum chamador fora da própria classe.
- A produção registra o acesso por `RepositorioDeIngressos.TentarUsar` e `ConfirmarPassagemFisica`, que gravam em `ticket_use_attempt`.
- Portanto o CA-02 ("nenhum acesso se perde no kill -9") está provado num caminho morto.
- O único SIGKILL sobre código real é o da coleta de bilhetes, em `tests/Integration/QuedaNaColetaDeBilhetesTests.cs:39-99`.
- Uma regressão transacional em `TentarUsar` (por exemplo, um commit em dois passos) passaria no CI.

**Como provar:**
1. Trocar `TentarUsar` por duas transações separadas.
2. Rodar o CI.
3. Nenhum teste de queda falha.

**Estado:** IMPLEMENTADO SEM PROVA. A capacidade 7 fica PARCIAL.

**Correção mínima:**
- A cobaia do CrashProbe deve gravar com `RepositorioDeIngressos.TentarUsar` e `ConfirmarPassagemFisica`.
- O teste de queda deve contar `ticket_use_attempt` e a outbox.
- `AccessJournal` deve ser apagado ou marcado como obsoleto.

### E9-2 | ALTO | A costura falsa esconde a DLL real e o EasyInnerReal não tem nenhum teste

**Onde está:**
- `tests/HardwareInLoop/CosturaFalsa.cs:13-209`, em particular:
  - linha 45: retorno padrão 0;
  - linha 66: firmware fixo em linha 4;
  - linhas 195-201: buffer de 64 bytes e `LerCartao` reimplementado na própria costura.
- `src/Topdata.EasyInner.Interop/EasyInnerReal.cs:131-155`: delegação 1:1, sem nenhum teste.
- `src/Topdata.EasyInner.Interop/EasyInnerNative.cs:66-74`: o `LerCartao` real nunca roda.
- `tests/Contract/LimitesDeCapacidadeTests.cs:157-173`: `InventarioDaDll` confere só nomes.
- `tests/HardwareInLoop/AdapterTests.cs:468-479` e `523-534`: codificam a hipótese "retorno 0 + origem 0 = sem evento" (HIL-EVT-01, não confirmada).
- `tests/HardwareInLoop/AdapterTests.cs:491-492`: com a chave desligada, retorno 1 vira `SemEventos`.
- `tests/HardwareInLoop/RecepcaoComErroTests.cs:451-491`.
- `tests/HardwareInLoop/LiberacaoDePontaAPontaTests.cs:31`: `LinhaDaCosturaFalsa = [4]`.

**Teste/job:** toda a suíte HardwareInLoop (154 testes). Nenhum job do `ci.yml` carrega a EasyInner.dll. O job instalador até confere que ela está ausente do MSI (`ci.yml:307-333`).

**O que faz:**
- A costura responde de forma síncrona e instantânea.
- Não guarda estado de conexão: `ReceberDadosOnLine` funciona com a porta fechada.
- Não faz marshalling: não exercita StdCall, string ANSI, `ref short` nem buffer real.

**Por que é problema:**
- Um fio trocado em `EasyInnerReal` passa em todo o CI. Exemplos: `LiberarCatracaEntrada` delegando para a Saida, ou argumentos invertidos.
- Assinatura e convenção de chamada nunca são verificadas, só os nomes.
- Socket morto com retorno diferente de 0 não reconecta por padrão; com a chave desligada, só aparece por contagem.
- Os testes HIL "homologam" apenas o firmware de linha 4.

**Como provar:**
1. Inverter duas linhas de delegação em `EasyInnerReal.cs:131-155`.
2. Rodar o CI.
3. Tudo continua verde.

**Estado:** PROVADO EM CI até a costura. A DLL, o marshalling e o sentido físico estão BLOQUEADOS pela bancada (HIL-STACK-01).

**Correção mínima:**
- Um teste de Contract que, por reflexão, confira que cada método de `EasyInnerReal` chama o `DllImport` de mesmo nome com os mesmos parâmetros e na mesma ordem.
- Conferir `CallingConvention` e `CharSet` dos `DllImport` contra o manual.
- Registrar no plano de bancada os itens que a costura não cobre.

### E9-3 | MÉDIO | O soak de 20 s mede quase nada

**Onde está:**
- `tests/LoadAndSoak/SoakDoWorkerTests.cs:36-44`: duração padrão de 20 s.
- `tests/LoadAndSoak/SoakDoWorkerTests.cs:71`: memória medida só com `GC.GetTotalMemory`.
- `tests/LoadAndSoak/SoakDoWorkerTests.cs:104-114`: decisão é um lambda constante `Allowed`, e `aoReceberEvento` só incrementa um contador.
- `tests/LoadAndSoak/SoakDoWorkerTests.cs:180`: teto fixo de 32 MB para qualquer duração.
- `tests/LoadAndSoak/SoakDoWorkerTests.cs:241-247`: teste que protege o próprio ensaio.
- `ci.yml:9-12`: padrão de 60 min só no disparo manual.
- `ci.yml:644-647`: `SOAK_MINUTOS` vazio em push e PR.

**Teste/job:** SoakDoWorkerTests (2 métodos). Roda nos jobs linux, windows e soak.

**O que faz:** exercita só DeviceGroupLoop, DevicePump e o simulador por 20 s.

**Por que é problema:**
- Não passa por SQLite, DecisorDeIngresso, outbox, supervisor, IPC nem DLL.
- Mede só a memória gerenciada, então não vê handle, memória nativa nem crescimento do WAL.
- O teto de 32 MB não escala com a duração.
- `O_ensaio_padrao_processa_volume_suficiente` só falha se alguém configurar menos de 20 s. No padrão é tautológico.
- O CI roda o mesmo soak de 20 s três vezes. No commit 29df501, o job soak inteiro durou 46 s (01:36:02 a 01:36:48).
- Não achei registro de execução do soak de 60 min.

**Como provar:**
1. Introduzir um vazamento em `RepositorioDeIngressos`, por exemplo uma conexão não descartada.
2. Rodar o soak.
3. Ele passa.

**Estado:** PARCIAL (capacidade 20).

**Correção mínima:**
- Um soak noturno ou semanal de 60 min, com o decisor real, SQLite em arquivo e a outbox.
- Medir o working set e o número de handles do processo.
- Teto proporcional à duração.
- Manter o de 20 s como fumaça.

### E9-4 | MÉDIO | O teste de LGPD da telemetria não consegue falhar

**Onde está:** `tests/Integration/SegurancaDaTelemetriaTests.cs:27-46` e `85-167`; o `catch (SqliteException)` que engole o erro está em `158-163`.

**Teste/job:** SegurancaDaTelemetriaTests (3 métodos), que alega provar NOVO-SEC-IA-02.

**O que faz:**
- Cria um banco `:memory:` com tabelas escritas à mão.
- Não insere nenhum dado.
- Varre tabelas vazias, de modo que o laço nunca itera, e engole exceções.

**Por que é problema:** se o coletor gravar o código do ingresso em claro no `telemetria.db`, o teste continua verde.

**Como provar:** fazer o `ColetorDeTelemetriaReal` gravar o `Raw` numa coluna. O teste passa.

**Estado:** IMPLEMENTADO SEM PROVA (capacidade 17).

**Correção mínima:**
- Rodar o coletor e o analisador reais sobre um `telemetria.db` em arquivo, com códigos conhecidos.
- Varrer todas as colunas e bytes, como já faz `CadastroDeCartoesEsquemaTests.cs:246-263`.
- Remover o `catch`.

### E9-5 | MÉDIO | Testes tautológicos de sentido do giro

**Onde está:**
- `tests/Integration/SentidoDoGiroTests.cs:56-76`: afirma sobre um dicionário literal criado no próprio teste, mas alega NOVO-SIM-MAP-01.
- `tests/Integration/SentidoDoGiroTests.cs:83-96`: chama duas vezes a mesma função com os mesmos argumentos e compara.
- `tests/Integration/SentidoDoGiroTests.cs:124-136`: "gera alerta A14" não é testado.
- `tests/Unit/Inteligencia/AvaliacaoDaFluidezTests.cs:131-144`: `IgnoraGirosSemSentido` nem passa giros sem sentido.

**Por que é problema:**
- O critério NOVO-SIM-SEN-01 ("giro sem pedido aparece como sem sentido") e o alerta A14 ficam sem prova.
- O relatório de cobertura conta esses testes como prova.
- O MapaDeGiro tem prova real em outro lugar: `Integration/MapaDeGiroTests.cs` e HIL `MapaDeGiroDePontaAPontaTests`.

**Como provar:**
1. Desligar a classificação "sem sentido" no código.
2. Rodar os dois arquivos.
3. Ambos ficam verdes.

**Estado:** IMPLEMENTADO SEM PROVA.

**Correção mínima:**
- Gerar um giro sem pedido no InnerSimulator e afirmar a classificação e o alerta A14.
- Apagar os dois testes tautológicos.

### E9-6 | MÉDIO | Dependência do relógio de parede (flaky)

**Onde está:**
- `tests/Unit/Inteligencia/ColetorDeTelemetriaTests.cs:270-293` usa o relógio real (`src/Access.Infrastructure.SQLite/ColetorDeTelemetriaReal.cs:40`). Se o teste cruzar a virada do minuto, `health_minute` ganha duas linhas e a asserção de 3 na linha 292 vira 2.
- Asserções com Stopwatch:
  - `ColetorDeTelemetriaTests.cs:60`, `90` e `217`: menos de 500 ms;
  - `tests/Integration/AnalisadorDaOperacaoTests.cs:256-260` e `309-322`: menos de 2 s;
  - `AnalisadorDaOperacaoTests.cs:188-191`: corrigido no commit 479d697;
  - `tests/Unit/Importacao/PreviaDaImportacaoTests.cs:605` e `627`: menos de 60 s.
- `tests/Integration/TelasTests.cs:155` e `181`: o resultado de `SpinUntil` é descartado. Se a assinatura não chegar a tempo, o teste falha 30 s depois com um cancelamento, sem uma mensagem útil.

**Por que é problema:**
- Falhas intermitentes no CI Windows, que roda em paralelo.
- A run de 02b2fed (id 37711749128) falhou no passo "Testar" do Windows. O commit seguinte que terminou, 479d697, corrige o teste do analisador.
- A anotação só diz "exit code 1", e o log bruto está num blob que o proxy não alcança. Atribuir a falha ao teste do analisador é HIPÓTESE.

**Como provar:** rodar `MultiplosDescarregamentosNoMesmoMinuto_Somam` com o relógio da máquina em hh:mm:59.9. Ele falha.

**Estado:** risco confirmado no código; a ocorrência no CI é HIPÓTESE.

**Correção mínima:**
- Injetar um relógio fixo no coletor dentro do teste.
- Trocar os limites de tempo de parede por sinais (eventos ou contagem) ou por limites só no soak.
- Afirmar o retorno do `SpinUntil`.

### E9-7 | MÉDIO | Os contadores atômicos não são verificados

**Onde está:**
- `tests/Unit/Inteligencia/ColetorDeTelemetriaTests.cs:100-128` (`ContadoresAtomicos_SemCorrida`).
- `ColetorDeTelemetriaTests.cs:191`: `Assert.Contains("\"", json)`.
- `ColetorDeTelemetriaTests.cs:262-263`: `Contains("{")`.

**O que faz:**
- Dispara 10.000 incrementos concorrentes.
- Afirma só descartes e descarregamentos iguais a 0 (126-127), o que é verdade sem nenhum sinal enfileirado.

**Por que é problema:** uma corrida que perca incrementos passaria no teste.

**Como provar:** trocar `Interlocked.Increment` por `++` no coletor. O teste passa.

**Estado:** IMPLEMENTADO SEM PROVA.

**Correção mínima:**
- Descarregar e afirmar que o total é exatamente 10.000.
- Desserializar o JSON e afirmar os campos.

### E9-8 | MÉDIO | Testes que voltam sem afirmar fora do Windows, e o job Windows não publica resultado

**Onde está:**
- `tests/Integration/InstalacaoRealTests.cs:31-34`.
- `tests/Integration/WorkerMorreComOServicoTests.cs:136-139` e `172-175`.
- `tests/Contract/ArquiteturaTests.cs:127-130` e `167-170`: um `return` silencioso se o projeto Desktop não existir.
- `ci.yml:80-81`: job windows sem logger TRX e sem upload.

**Por que é problema:**
- No Linux esses testes contam como "passou".
- No Windows não há TRX nem anotação por teste. Na falha de 02b2fed, a anotação só diz "Process completed with exit code 1", sem o nome do teste.
- Não há evidência auditável de que o ACL e o Job Object rodaram e passaram.
- Na Arquitetura, se o projeto for renomeado, a trava some em silêncio.

**Como provar:** no TRX do Linux, os quatro testes aparecem como Passed, com duração de cerca de 0 ms.

**Estado:** PARCIAL (capacidades 9 e 16).

**Correção mínima:**
- Usar `Skip` explícito: `[SkippableFact]` ou um `Fact` condicional.
- Fazer a Arquitetura falhar quando o projeto estiver ausente.
- No job windows, adicionar `--logger trx` e `upload-artifact`.

### E9-9 | MÉDIO | Restauração de cópia de segurança sem função e sem teste

**Onde está:**
- `tests/Integration/CopiaDeSegurancaTests.cs:26-86` testa só Criar, Limpar e `integrity_check`.
- `grep "Restaur" src/` acha apenas uma mensagem em `ChaveDaImpressao.cs:73` e o Simulator.

**Por que é problema:** a capacidade 18 exige restauração. Um backup que nunca foi restaurado em teste não prova recuperação: WAL, versão de esquema e chave de impressão.

**Como provar:** o próprio grep acima.

**Estado:** AUSENTE.

**Correção mínima:**
- Implementar a restauração, ou documentar o procedimento manual.
- Teste: criar o backup, apagar o banco, restaurar e afirmar os ingressos, as tentativas e a outbox.

### Achados BAIXO e INFORMATIVO, uma linha cada

- **E9-10 | BAIXO.** `tests/Unit/Inteligencia/AvaliacaoDeReusoTests.cs:6-9` promete "20 de 20 reusos; 0 alertas passe 2 usos" e não faz isso; o teste do passe usa o outcome "consumido", que nunca dispara (210-235). Em `RelatorioDosPosEventoTests.cs:180-195`, "mesmo hash" é só chamar duas vezes a mesma função.
- **E9-11 | BAIXO.** `tests/Unit/AvaliacaoDeSugestoesTests.cs:149-166`: o nome diz `RetornaNull` e o teste afirma `NotNull`.
- **E9-12 | BAIXO.** `tests/Integration/AssistenteDeConfiguracaoTests.cs:221` e `229` afirmam sobre um supervisor criado e não usado (tautologia).
- **E9-13 | BAIXO.** Cerca de 70 métodos só leem texto: `GemeoDigitalTests.cs:119-151` (docs/02), `InstaladorTests.cs:29-167` (.ps1 e README), LigacoesDasTelas, RayzerDesign, RayzerMarca, ModelosDePlanilha, Contract ContratoIpc, CamadaInteligente e a matriz CSV. São travas estáticas válidas, mas não provam comportamento.
- **E9-14 | BAIXO.** `tests/Unit/Credentials/PerfisDeLeituraTests.cs:130-135` testa aritmética (`uint.MaxValue`), não código do produto.
- **E9-15 | BAIXO.** `tests/Unit/MensagemDeFalhaTests.cs:13-41` altera o estático global `MensagemDeFalha.TokenSemPermissao`; hoje não há corrida dentro de Unit.
- **E9-16 | BAIXO.** Namespaces inconsistentes: `Unit.Inteligencia` e `Tests.Unit.Inteligencia`. `RayzerMarcaTests.cs:11` declara `partial class RayzerDesignTests`.
- **E9-17 | BAIXO.** `InstaladorTests.cs:127-142` (`O_instalador_nunca_imprime_o_token`) só pega `Write-Host` e `$token` na mesma linha.
- **E9-18 | BAIXO.** `SecLog01Tests.cs:138-151` tem um docstring órfão; `O_proprio_teste_acusaria` (169-181) não roda o fluxo com a redação desligada.
- **E9-19 | INFORMATIVO.** `DateTimeOffset.UtcNow` misturado com relógios fixos em 2026-12-06 (`ComandosNovosDaCatracaTests.cs:74-75`, `GerenciarCatracaTests.cs:62-63`). Inofensivo enquanto os ingressos não têm janela; possível bomba-relógio (HIPÓTESE).
- **E9-20 | INFORMATIVO.** Testes fortes, para referência:
  - `IngressosDeVariosProvedoresTests.cs:261-299`: 8 catracas com um vencedor;
  - `DecisorDeIngressoTests.cs:191-202`: base que falha nega;
  - QuedaNaColetaDeBilhetes: SIGKILL real;
  - PrazoDoGiro HIL: relógio simulado;
  - CoberturaDaConfiguracao: todo campo chega à costura;
  - SequenciaOficial HIL: byte a byte;
  - WorkerTests e SupervisorTests: reconexão, backoff, disjuntor, watchdog e quarentena;
  - `SimuladorTests.cs:335-362`: recusa de uso concorrente com thread própria, já endurecido contra o esgotamento do pool.
- **E9-21 | BAIXO.** `tests/Integration/TokenIlegivelTests.cs:6-8` admite que não testa a negação real com token ilegível.
- **E9-22 | INFORMATIVO.** Histórico do CI (últimas 60 execuções: 21 sucesso, 8 falha, 31 canceladas):
  - 29df501 e 479d697 verdes nos seis jobs;
  - 02b2fed falhou no Windows "Testar" (provável flaky do analisador, ver E9-6);
  - 9202de8 falhou por erro de compilação em `AvaliacaoDaHealthTests.cs`, no Linux e no Windows;
  - aviso de Node 20 descontinuado em `checkout`, `setup-dotnet` e `upload-artifact` (só aviso).
  - O que reprova o CI: falha de teste, warning de compilação (`TreatWarningsAsErrors`), NuGetAudit, captura de tela que falha (`ci.yml:243-246`), PE diferente de x86, conteúdo do MSI, instalar e desinstalar, fundo branco no Setup, a varredura `Raw`/`Normalized` e o typecheck da UI.
  - O que é só aviso: upload sem arquivos (`if-no-files-found: warn`, `ci.yml:256-262` e `651-657`).

## B) Cobertura

Formato: arquivo | linhas | lido inteiro | nº de métodos de teste | achados.

**Contract**
- ArquiteturaTests | 364 | sim | 13 | E9-8
- CamadaInteligenteTests | 294 | sim | 9 | E9-13
- ContratoIpcTests | 180 | sim | 8 | E9-13
- LimitesDeCapacidadeTests | 224 | sim | 10 | E9-2
- OrigensDeEventoTests | 149 | sim | 7 | 0
- RepositorioDeMatriz | 87 | sim | 0 (auxiliar) | E9-13
- RetornosDocumentadosTests | 75 | sim | 2 | 0

**CrashProbe**
- Program | 89 | sim | 0 | E9-1
- ColetaDaCobaia | 160 | sim | 0 | 0

**LoadAndSoak**
- SoakDoWorkerTests | 248 | sim | 2 | E9-3

**HardwareInLoop**
- AdapterTests | 716 | sim | 40 | E9-2
- ChavesDaConfiguracao | 277 | sim | 8 | 0
- CoberturaDaConfiguracao | 396 | sim | 5 | 0
- ConfiguracaoAplicada | 93 | sim | 2 | 0
- CosturaFalsa | 209 | sim | 0 | E9-2
- LiberacaoDePontaAPonta | 263 | sim | 7 | E9-2
- MapaDeGiroDePontaAPonta | 158 | sim | 7 | 0
- PrazoDoGiro | 317 | sim | 7 | 0
- RecepcaoComErro | 148 | sim | 3 | E9-2
- SequenciaDeConexao | 64 | sim | 1 | 0
- SequenciaOficial | 404 | sim | 12 | 0

**Integration**
- AnalisadorDaOperacao | 431 | sim | 12 | E9-6
- AssistenteDeConfiguracao | 258 | sim | 9 | E9-12
- Bancada | 218 | sim | 8 | 0
- BancoTemporario | 39 | sim | 0 | 0
- BilheteriaLocal | 387 | sim | 19 | 0
- BilhetesColetados | 206 | sim | 11 | 0
- CadastroDeCartoesEsquema | 552 | sim | 22 | 0
- ChavesDaConfiguracao | 218 | sim | 6 | 0
- CicloDoIngresso | 369 | sim | 9 | 0
- ColetaDeBilhetes | 453 | sim | 13 | 0
- ComandosDaCatracaPorInner | 349 | sim | 7 | 0
- ComandosDaCatraca | 286 | sim | 11 | 0
- ComandosNovosDaCatraca | 482 | sim | 16 | E9-19
- ConfiguracaoAplicada | 297 | sim | 5 | 0
- ConfiguracoesDasCatracas | 273 | sim | 7 | 0
- CopiaDeSeguranca | 102 | sim | 5 | E9-9
- DrenagemDaOutbox | 256 | sim | 8 | 0
- GerenciarCatraca | 257 | sim | 8 | E9-19
- IngressosDeVariosProvedores | 377 | sim | 15 | 0
- InstalacaoReal | 89 | sim | 4 | E9-8
- Instalador | 193 | sim | 7 | E9-13, E9-17
- InteligenciaDesligada | 39 | sim | 2 | 0
- IpcLocal | 210 | sim | 7 | 0
- LigacoesDasTelas | 192 | sim | 4 | E9-13
- MapaDeGiro | 474 | sim | 15 | 0
- ModelosDePlanilha | 73 | sim | 3 | E9-13
- ModoSimulacao | 230 | sim | 8 | 0
- MontadorDaConfiguracao | 234 | sim | 8 | 0
- MotivosEmPortugues | 39 | sim | 2 | 0
- NomeDaCatraca | 33 | sim | 3 | 0
- NormalizacaoSimetrica | 175 | sim | 4 | 0
- Operacao | 255 | sim | 8 | 0
- PacoteDeDiagnostico | 118 | sim | 5 | 0
- PainelNaNuvem | 480 | sim | 13 | 0
- Painel | 275 | sim | 10 | 0
- ParametrizacaoDaCatraca | 323 | sim | 11 | 0
- Periodo | 68 | sim | 5 | 0
- Persistencia | 233 | sim | 13 | E9-1 (AccessJournal)
- PorQueNegou | 351 | sim | 4 | 0
- PrazoDoGiroNoSimulador | 246 | sim | 4 | 0
- ProcessoDeWorker | 71 | sim | 1 | 0
- QuedaAbrupta | 191 | sim | 2 | E9-1
- QuedaNaColetaDeBilhetes | 199 | sim | 2 | 0
- RayzerDesign | 502 | sim | 9 | E9-13
- RayzerMarca | 132 | sim | 5 | E9-13, E9-16
- ReleDeWebhook | 171 | sim | 9 | 0
- RelogioDaCatraca | 220 | sim | 7 | 0
- SecLog01 | 210 | sim | 3 | E9-18
- SegurancaDaTelemetria | 168 | sim | 3 | E9-4
- SentidoDoGiro | 137 | sim | 5 | E9-5
- SequenciaOficial | 153 | sim | 4 | 0
- ServicoDaOperacao | 373 | sim | 12 | 0
- SessaoDoServico | 238 | sim | 7 | 0
- SimuladorTests | 363 | **não, varrido por grep** e linhas 330-363 lidas | 19 | 0
- SinalDaCatraca | 141 | sim | 3 | 0
- SincronizacaoDoServico | 171 | sim | 6 | 0
- Supervisor | 272 | sim | 11 | 0
- TelasTests | 1772 | **não, varrido por grep** e linhas 136-175 lidas | 52 | E9-6
- TokenIlegivel | 41 | sim | 2 | E9-21
- WorkerMorreComOServico | 235 | sim | 5 | E9-8
- Worker | 357 | sim | 13 | 0
- ZeladorDeRegistros | 94 | sim | 5 | 0
- ZetDeCompraAoEstorno | 78 | sim | 3 | 0

**Unit (todos lidos inteiros)**
- AvaliacaoDeSugestoes | 203 | sim | 10 | E9-11
- ConectorRest | 210 | sim | 12 | 0
- Conectores/Painel | 300 | sim | 19 | 0
- ConfiguracaoDoEquipamento | 171 | sim | 14 | 0
- CredentialValue | 127 | sim | 11 | 0
- ImpressaoDeCodigo | 61 | sim | 5 | 0
- PerfisDeLeitura | 136 | sim | 11 | E9-14
- AdapterResult | 88 | sim | 5 | 0
- CamadaDaCatraca | 197 | sim | 5 | 0
- ComandosNovos | 154 | sim | 11 | 0
- ConfiguracaoComRecuo | 112 | sim | 6 | 0
- DeviceStateMachine | 280 | sim | 17 | 0
- EventOrigin | 125 | sim | 8 | 0
- MapaDeGiro | 159 | sim | 11 | 0
- PrazoDosEstados | 111 | sim | 6 | 0
- VersaoDaConfiguracao | 316 | sim | 14 | 0
- CenaDaCatraca | 197 | sim | 13 | 0
- GemeoDigital | 350 | sim | 20 | E9-13
- GeometriaFit4 | 221 | sim | 15 | 0
- PlanilhasDeTeste | 176 | sim | 0 (auxiliar) | 0
- PreviaDaImportacao | 640 | sim | 36 | E9-6
- FonteDeRelay | 219 | sim | 10 | 0
- LacoDeIngestao | 289 | sim | 11 | 0
- TradutorDaZet | 147 | sim | 12 | 0
- TradutorDoContratoV1 | 223 | sim | 19 | 0
- DecisorDeIngresso | 233 | sim | 11 | 0
- AvaliacaoDaFluidez | 145 | sim | 7 | E9-5, E9-16
- AvaliacaoDaHealth | 162 | sim | 11 | E9-16
- AvaliacaoDeReuso | 372 | sim | 9 | E9-10
- ColetorDeTelemetria | 294 | sim | 7 | E9-6, E9-7, E9-16
- FundacaoDaCamada | 58 | sim | 4 | 0
- PorQueNegou | 214 | sim | 13 | 0
- RegrasDeAlerta | 215 | sim | 17 | 0
- RelatorioDosPosEvento | 196 | sim | 11 | E9-10, E9-16
- LimitesDeCapacidade | 127 | sim | 10 | 0
- MensagemDeFalha | 65 | sim | 5 | E9-15
- Novidades | 132 | sim | 10 | 0
- LogEstruturado | 99 | sim | 6 | 0
- Redacao | 149 | sim | 12 | 0
- PreRequisitos | 68 | sim | 6 | 0
- RegrasEntreCampos | 400 | sim | 30 | 0
- VigiaDoProcessoPai | 136 | sim | 6 | 0
- DrenadorDaOutbox | 291 | sim | 15 | 0
- DublesDeSincronizacao | 202 | sim | 0 (auxiliar) | 0
- TransporteLocal | 95 | sim | 4 | 0

**Workflow**
- `.github/workflows/ci.yml` | 727 | sim | 6 jobs | E9-3, E9-8, E9-22

Os números de métodos vêm da contagem de `[Fact]` e `[Theory]`. Uma Theory conta como 1, embora gere vários casos. Por isso os totais não batem com os 857/58/154/687 do runner.

## C) As 20 capacidades de §7

1. **Carregar a DLL no worker x86 e conectar.**
   - Testes: verificação PE x86 (`ci.yml:266-281`); `InstaladorTests` (texto).
   - Falharia com a função errada? Não: a DLL nunca é carregada.
   - Estado: IMPLEMENTADO SEM PROVA, BLOQUEADO pela bancada (HIL-STACK-01).
2. **Configurar a catraca.**
   - Testes: HIL CoberturaDaConfiguracao, ChavesDaConfiguracao, SequenciaOficial, ConfiguracaoAplicada; Unit VersaoDaConfiguracao, RegrasEntreCampos; Integration ConfiguracaoAplicada e ComandosDaCatracaPorInner.
   - Falharia? Sim até a costura; não para `EasyInnerReal` e o marshalling.
   - Estado: PARCIAL.
3. **Ler QR ou cartão e normalizar.**
   - Testes: CredentialValue, PerfisDeLeitura, DecisorDeIngresso (124-133), NormalizacaoSimetrica, AdapterTests (613-627).
   - Falharia? Sim para a normalização; não para o buffer real e `EasyInnerNative.LerCartao`.
   - Estado: PROVADO EM CI para a normalização; a leitura real fica sem prova.
4. **Decidir.**
   - Testes: IngressosDeVariosProvedores, BilheteriaLocal, CicloDoIngresso, ZetDeCompraAoEstorno, DecisorDeIngresso, Bancada.
   - Falharia? Sim.
   - Estado: PROVADO EM CI.
5. **Liberar no sentido certo.**
   - Testes: HIL LiberacaoDePontaAPonta e MapaDeGiroDePontaAPonta; AdapterTests (384-401); Integration MapaDeGiro.
   - Falharia? Sim até a costura; não com um fio trocado em `EasyInnerReal` (E9-2).
   - Estado: PROVADO EM CI até a costura; o sentido físico fica para a bancada (NOVO-HIL-DIR-11).
6. **Confirmar o giro (origem 6) e encerrar sem giro.**
   - Testes: HIL PrazoDoGiro; PrazoDoGiroNoSimulador; Unit EventOrigin, DeviceStateMachine, PrazoDosEstados; DecisorDeIngresso (153-177).
   - Falharia? Sim.
   - Estado: PROVADO EM CI; hardware pendente.
7. **Registrar sem perder e sem duplicar, com queda.**
   - Testes: Persistencia e QuedaAbrupta, ambos sobre `AccessJournal` (caminho morto); CicloDoIngresso sem kill.
   - Falharia? Não para `TentarUsar` sob SIGKILL.
   - Estado: PARCIAL (E9-1).
8. **Offline e coleta de bilhetes.**
   - Testes: ColetaDeBilhetes, BilhetesColetados, QuedaNaColetaDeBilhetes (SIGKILL real).
   - Falharia? Sim.
   - Estado: PROVADO EM CI no simulador. A semântica do tipo 128 está A_CONFIRMAR (NOVO-INT-REC-07) e a chave nasce desligada.
9. **Detectar sem comunicação, socket morto e worker travado, e recuperar.**
   - Testes: WorkerTests (reconexão 93-120, watchdog 304-346, disjuntor), SupervisorTests, VigiaDoProcessoPai, RecepcaoComErro, WorkerMorreComOServico (só Windows).
   - Falharia? Sim para watchdog e disjuntor. Retorno diferente de 0 só é coberto com a chave ligada. O Job Object não é afirmado no Linux.
   - Estado: PARCIAL (E9-2, E9-8).
10. **Sincronizar com a nuvem.**
    - Testes: DrenadorDaOutbox, DrenagemDaOutbox, PainelNaNuvem, SincronizacaoDoServico, ConectorRest, Unit Painel.
    - Falharia? Sim, contra HTTP falso.
    - Estado: PROVADO EM CI; a nuvem real não é verificável.
11. **Zet e conciliação.**
    - Testes: TradutorDaZet, ZetDeCompraAoEstorno, LacoDeIngestao, FonteDeRelay, ReleDeWebhook.
    - Falharia? Sim.
    - Estado: PROVADO EM CI com payload sintético.
12. **Manual, display, relógio e aplicar, com auditoria.**
    - Testes: GerenciarCatraca, ComandosDaCatraca, ComandosNovos, RelogioDaCatraca, ConfiguracaoAplicada.
    - Falharia? Sim até a costura.
    - Estado: PROVADO EM CI no simulador.
13. **Prestação de contas.**
    - Testes: ServicoDaOperacao (303-333), IngressosDeVariosProvedores (conciliação e corte), BilheteriaLocal.
    - Falharia? Em parte. Não vi testes de R1 a R8 nem do PDF (TelasTests foi só varrido).
    - Estado: PARCIAL.
14. **Painel do operador.**
    - Testes: TelasTests (varrido), PainelTests, LigacoesDasTelas e RayzerDesign (texto), autoteste de telas no job instalador.
    - Falharia? Em parte: as ViewModels sim, o XAML só como texto.
    - Estado: PARCIAL.
15. **Alertas.**
    - Testes: RegrasDeAlerta (funções puras), InteligenciaDesligada.
    - Falharia? Sim como função pura; o A14 não é testado (E9-5).
    - Estado: IMPLEMENTADO, provado em CI só como função pura; a camada nasce desligada.
16. **Segurança local e da nuvem.**
    - Testes: IpcLocal (token), InstalacaoReal (ACL, só Windows), ImpressaoDeCodigo, ReleDeWebhook (segredo), SincronizacaoDoServico (Bearer), TokenIlegivel.
    - Falharia? Sim para o token e o Bearer. A ACL não é afirmada fora do Windows e o Windows não publica TRX. A negação com token ilegível não é testada.
    - Estado: PARCIAL.
17. **LGPD.**
    - Testes: Redacao, SecLog01, CredentialValue, CadastroDeCartoesEsquema (varredura de bytes), PacoteDeDiagnostico, BilhetesColetados, job seguranca (grep).
    - Falharia? Sim para o cadastro e os logs; não para a telemetria (E9-4).
    - Estado: PARCIAL.
18. **Instalar, atualizar, desinstalar, backup e restauração.**
    - Testes: job instalador (instala e desinstala de verdade), CopiaDeSeguranca (Criar e Limpar).
    - Falharia? Sim para instalar e desinstalar. A atualização não tem teste. A restauração não existe.
    - Estado: PARCIAL; restauração AUSENTE (E9-9).
19. **Diagnóstico e runbooks.**
    - Testes: PacoteDeDiagnostico, ZeladorDeRegistros, PreRequisitos.
    - Falharia? Sim para o pacote. Os runbooks não são testáveis.
    - Estado: PARCIAL.
20. **Longa duração, caos e carga.**
    - Testes: Soak de 20 s (E9-3), QuedaAbrupta (caminho morto, E9-1), QuedaNaColetaDeBilhetes, carga de 100 mil em PreviaDaImportacao.
    - Falharia? Fracamente. Vazamento nativo ou no SQLite não seria pego.
    - Estado: PARCIAL.

Nenhuma capacidade está PROVADA EM HARDWARE.

## D) Perguntas de decisão

**1. O que fazer com o CHAOS-KILL-01 (E9-1)?**
- (a) Reescrever a cobaia do CrashProbe sobre `TentarUsar` e `ConfirmarPassagemFisica` antes do evento.
- (b) Manter como está e documentar a lacuna.
- (c) Adiar para depois da bancada.

Recomendo **(a)**. É o único teste de perda e duplicação sob kill -9 do caminho real. Custa pouco, porque a infraestrutura do CrashProbe já existe e funciona (QuedaNaColetaDeBilhetes), e o caminho testado hoje é código morto.

**2. Soak no CI.**
- (a) Manter só o de 20 s.
- (b) Soak noturno ou semanal de 60 min com o caminho real (SQLite em arquivo, decisor, outbox), medindo working set e handles, e manter o de 20 s como fumaça só no job soak.
- (c) Soak de 60 min em todo PR.

Recomendo **(b)**. O de 20 s com decisão constante não pega vazamento de SQLite nem de memória nativa. Rodar 60 min em todo PR travaria o fluxo, porque o `cancel-in-progress` já cancela metade das execuções.

**3. Testes que só valem no Windows e evidência do job Windows.**
- (a) Deixar o `return` silencioso.
- (b) Marcar como Skip explícito fora do Windows, fazer a Arquitetura falhar com projeto ausente, e publicar TRX e anotações por teste no job windows.
- (c) Rodar esses testes só no Windows por filtro de categoria.

Recomendo **(b)**. Hoje um "passou" no Linux não significa nada. Na falha real de 02b2fed, o CI só disse "exit code 1", sem o nome do teste.
