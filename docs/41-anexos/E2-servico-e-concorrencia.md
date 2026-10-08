> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

RELATÓRIO E2: ARQUITETO DE SERVIÇO E CONCORRÊNCIA (commit 29df501, só leitura)

Comandos executados: `git log -1`, `wc -l`, Read/Grep/sed nos arquivos atribuídos e em alguns arquivos vizinhos para cruzar chamadores, e `gh api .../commits/HEAD/check-runs` (só leitura). O CI do HEAD está todo verde: Build e testes Windows e Linux, Instalador MSI, Soak, Varredura e UI. Não rodei build nem teste, como pedido.

## A) Achados

### Críticos e altos

**E2-01 | CRÍTICO | Worker travado dentro da DLL nunca é detectado nem reiniciado**
- **Onde:** `src/Edge.Supervisor/ProcessoDeWorker.cs:78` (`EstaSaudavel => EstaVivo`), `src/Edge.Supervisor/WorkerSupervisor.cs:186-191`, `src/Edge.Worker/DeviceGroupLoop.cs:99`.
- **Funções:** `ProcessoDeWorker.EstaSaudavel` e `WorkerSupervisor.Situacao/Supervisionar`.
- **O que o código faz:** "Saudável" quer dizer só "processo vivo". O próprio comentário admite isso em `ProcessoDeWorker.cs:72-77`. O `Watchdog` do worker só recebe batidas: o grep não acha ninguém que leia `Watchdog.EstaSaudavel` ou `TempoSemBatimento`. Por isso o estado `SemBatimento`, e o ramo "laço travado" de `WorkerSupervisor.cs:231-237`, nunca acontecem com o worker real.
- **Por que importa no evento:** `Watchdog.cs` documenta que `ReceberDadosOnLine` pode travar sem lançar exceção. O assistente cria um único grupo, com até 20 catracas num worker só (`Instalacao/AssistenteDeConfiguracao.cs:99-102,171`). Uma chamada travada para todas as catracas, e nada as recupera. O painel só mostra "sem notícia do worker" depois de 15 s (`EdgeControlService.cs:28,216`). O comando REINICIAR_CONEXAO entra numa fila que o worker travado não consome. O único remédio é reiniciar o serviço, o que exige administrador.
- **Como provar:** `SupervisorTests.Worker_vivo_porem_travado_e_morto_e_recriado` (`tests/Integration/SupervisorTests.cs:137`) usa um dublê com `Travar()` e não falharia com o defeito real. Falta um teste com `ProcessoDeWorker` real e uma cobaia que fica viva sem bater.
- **Estado:** CONFIRMADO pela leitura (a ausência de detecção). Se a DLL trava de fato é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** batimento real. Por exemplo, o supervisor lê o `atualizado_em` do `device_status` desta sessão por worker (migração 016, já gravado a cada 2 s). Acima da tolerância, mata e reinicia.

**E2-02 | CRÍTICO | Falha num serviço em segundo plano derruba o host, mata todos os workers e o SCM não religa**
- **Onde:** `src/Edge.Supervisor/AgendadorDeCopias.cs:40,62`; `src/Access.Infrastructure.SQLite/CopiaDeSeguranca.cs:59-60`; `src/Edge.Supervisor/Program.cs:288,294-295`; `installer/wix/ConexaoTopdata.wxs:145-149`.
- **Funções:** `AgendadorDeCopias.Executar/ExecuteAsync`. Mesma exposição em `LacoDeSupervisao.ExecuteAsync` e `AcompanhamentoDaOperacao.ExecuteAsync`.
- **O que o código faz:**
  - `Executar` só captura `InvalidOperationException`, `IOException` e `UnauthorizedAccessException`. O `VACUUM INTO` lança `SqliteException` quando o disco enche, há erro de E/S ou a base fica ocupada além do `busy_timeout`, e essa exceção sai do `ExecuteAsync`.
  - Ninguém define `BackgroundServiceExceptionBehavior` (grep vazio), então vale o padrão do .NET, `StopHost`.
  - Na parada, `ApplicationStopping` chama `supervisor.Encerrar`, que mata todos os workers (`Program.cs:288`).
  - `Main` devolve 0 (`Program.cs:295`), o que parece parada normal.
  - O `util:ServiceConfig` não liga `FailureActionsOnNonCrashFailures`, então o SCM não aplica o "restart".
  - Mesmo padrão em outros dois pontos: `LacoDeSupervisao.cs:23` chama `supervisor.Iniciar()` sem `try` (um `Process.Start` que lança `Win32Exception`, com o exe bloqueado por antivírus ou atualização, derruba a partida), e `AcompanhamentoDaOperacao.cs:136` só captura `SqliteException`.
- **Por que importa no evento:** com disco cheio, o serviço cai 1 min depois de cada partida (primeira cópia em `AgendadorDeCopias.cs:24`) e fica parado. Todas as catracas perdem o sistema.
- **Como provar:** não há teste do `AgendadorDeCopias` (grep). Falta um teste de `Executar` com uma fábrica que lança `SqliteException`, e um ensaio em VM: disco cheio e conferir o estado do serviço.
- **Estado:** o caminho no código está CONFIRMADO pela leitura. O gatilho e a reação do SCM são NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** captura geral (menos cancelamento) em cada laço; `HostOptions.BackgroundServiceExceptionBehavior = Ignore` com registro em arquivo; código de saída diferente de 0 quando a parada não foi pedida; `FailureActionsOnNonCrashFailures="yes"` no WiX.

**E2-03 | ALTO | Quarentena não tem saída e pode começar na primeira exceção**
- **Onde:** `src/Edge.Supervisor/WorkerSupervisor.cs:155-163,176-179,202-205,221-229`.
- **Funções:** `WorkerSupervisor.SupervisionarTodos` e `Supervisionar`.
- **O que o código faz:**
  - Nenhum código volta `EmQuarentena` para falso (grep), e não existe RPC para isso. Só reiniciar o serviço tira um worker da quarentena.
  - Qualquer exceção ao supervisionar (`Win32Exception` em `Kill` ou `Process.Start`) põe o worker em quarentena na hora, sem backoff e sem contar como reinício.
- **Por que importa no evento:** uma causa passageira (porta presa por alguns minutos, antivírus) deixa o grupo, ou seja todas as catracas, parado até um administrador reiniciar o serviço. O painel só diz "Programa da catraca parou várias vezes" (`src/Desktop.ViewModels/Textos.cs:39`), sem dizer o que fazer.
- **Como provar:** `SupervisorTests.Reinicios_demais_na_janela_levam_a_quarentena` (linha 188) prova a entrada. Não há teste de saída.
- **Estado:** CONFIRMADO pela leitura.
- **Correção mínima:** sair da quarentena sozinho depois de uma janela longa; comando "tentar de novo" auditado; exceção tratada como reinício que falhou, com backoff.

**E2-04 | ALTO | Cofre ilegível impede o serviço de subir**
- **Onde:** `src/Edge.Supervisor/Program.cs:200-208`; `src/Edge.Supervisor/CofreDeSegredos.cs:56-58`.
- **Funções:** o bloco de partida da nuvem e `CofreDpapi.Ler`.
- **O que o código faz:** `cofre.Ler(...)` roda fora de `try`. `ProtectedData.Unprotect` lança `CryptographicException` com arquivo `.segredo` corrompido ou com chave de máquina diferente (restauração ou clone). A chave da impressão é protegida (`Program.cs:119-127`), mas esta leitura não. `SincronizacaoComANuvem.Montar` (`Program.cs:206`) também lança `SqliteException` sem tratamento.
- **Por que importa no evento:** o serviço cai na partida e o SCM entra num laço de reinícios. Os workers nunca sobem.
- **Como provar:** teste com um cofre falso que lança na leitura.
- **Estado:** caminho CONFIRMADO pela leitura. O gatilho DPAPI é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** capturar, registrar e seguir sem segredo, como já é feito com a chave da impressão.

### Médios

**E2-05 | MÉDIO | A supervisão não fica em registro nenhum**
- **Onde:** `src/Edge.Supervisor/LacoDeSupervisao.cs:31`; `Program.cs:54,66,74,126,290`; `SegurancaLocal.cs:91`; `ProcessoDeWorker.cs:30-34`.
- **O que o código faz:** reinícios, quarentena e "grupo de operadores não existe" vão só para `Console`. Como serviço, não há console. Não entram no `RegistroEmArquivo` "servico" (`Program.cs:173-180`) nem no pacote, que só lê `registros/*.log`. A saída do worker fica só nas 50 linhas em memória e se perde quando o serviço reinicia.
- **Por que importa:** depois do evento, o suporte não reconstitui por que uma catraca parou.
- **Estado:** CONFIRMADO pela leitura. Para onde vai o Console sob o SCM é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:** usar `Registrar` na supervisão; gravar as últimas linhas quando o worker sai.

**E2-06 | MÉDIO | Pacote de diagnóstico pode passar do limite de 4 MB do cliente gRPC**
- **Onde:** `src/Contracts/TransporteLocal.cs:106`; `EdgeControlService.cs:587-591`; `MontadorDoPacoteDeDiagnostico.cs:283,306-316`; `edge_control.proto:387`.
- **O que o código faz:** o zip vai inteiro num único campo `bytes`, com 7 dias de registros. `CriarCanal` não ajusta `MaxReceiveMessageSize`, que fica no padrão de 4 MB.
- **Por que importa:** o painel recebe `ResourceExhausted` justamente depois de um evento grande.
- **Estado:** HIPÓTESE (depende do tamanho real dos registros). Prova: teste com registros de mais de 4 MB comprimidos.
- **Correção mínima:** limitar o tamanho, fatiar o envio, ou subir o limite no cliente.

**E2-07 | MÉDIO | Ordem e ACL da pasta de dados: o token fica legível, mas o diagnóstico do S03 é perdido**
- **Onde:** `Program.cs:87` (`GarantirToken`) → `:103` (`RestringirPastaDeDados`) → `:106` (`Migrator`); `SegurancaLocal.cs:139-144,159-162,179-194`; `InstalacaoLocal.cs:32-37,71`.
- **O que o código faz:**
  - A ordem está certa. O token recebe uma ACL própria e protegida, com leitura para o grupo dos operadores, e é pulado pela restrição da pasta. A raiz fica protegida, só SYSTEM e Administradores, e não sobrescreve o arquivo protegido.
  - O painel lê o token pelo caminho completo. Isso só funciona com o direito "Ignorar verificação completa" (SeChangeNotifyPrivilege), que é padrão no Windows (NÃO VERIFICÁVEL AQUI). Uma GPO de endurecimento que o remova quebra o painel.
  - Efeito colateral: para o usuário fora do grupo, `File.Exists(token)` fica sem direito de atributos e sem listagem na pasta. Devolve falso, então `TokenIlegivel()` dá falso e a mensagem específica do S03 nunca aparece.
  - Igual em `src/Desktop.App/JanelaDeNovidades.xaml.cs:37`: `File.Exists(workers.json)` passa a dar falso para o operador.
  - `RestringirPastaDeDados` não troca o dono. Uma pasta pré-criada em ProgramData por usuário comum mantém o WRITE_DAC implícito do dono.
  - Nenhum teste chama `RestringirPastaDeDados` (grep).
- **Estado:** a leitura do código está CONFIRMADA. O efeito no Windows é HIPÓTESE / NÃO VERIFICÁVEL AQUI (precisa de VM).
- **Correção mínima:** dar ao grupo dos operadores `ReadAttributes` e listagem só na raiz; definir o dono como Administradores; teste em VM.

**E2-08 | MÉDIO | A restrição recursiva de ACL vale para qualquer pasta derivada de "banco"**
- **Onde:** `ConfiguracaoDoSupervisor.cs:44,50-54`; `Program.cs:96-103`.
- **O que o código faz:** `PastaDeDados` é a pasta do `Banco`, sem validação. Um caminho relativo resolve contra a pasta atual do serviço (System32). `C:\acesso.db` resolve para `C:\`. Em ambos os casos `RestringirPastaDeDados` aplica, de forma recursiva, uma ACL protegida só para SYSTEM e Administradores.
- **Gatilho:** só edição manual, porque o assistente grava `Banco: null` (`AssistenteDeConfiguracao.cs:132`). A consequência seria quebrar o Windows.
- **Estado:** CONFIRMADO pela leitura.
- **Correção mínima:** exigir caminho absoluto e recusar raiz de disco e pastas do sistema.

**E2-09 | MÉDIO | 7 RPCs do contrato não têm implementação**
- **Onde:** `src/Contracts/Protos/edge_control.proto:72-91`.
- **O que o código faz:** `ObterSugestoes`, `RegistrarDestinoDaSugestao`, `ObterSaudeDasCatracas`, `ObterRitmo`, `ListarAlertas`, `MarcarAlertaComoCiente` e `ObterRelatorioPosEvento` não têm `override` (grep de `public override`) e devolvem `Unimplemented`.
- **Efeito no painel:**
  - `src/Desktop.ViewModels/Parametrizacao.cs:1027-1034` engole o erro em silêncio.
  - `GerenciamentoDeCatracasViewModel`, que chama `ObterSaudeDasCatracas`, nunca é instanciado (código morto, para E6).
- **Divergência:** os comentários do contrato descrevem as funções como se existissem.
- **Estado:** CONFIRMADO pela leitura.
- **Correção mínima:** marcar no `.proto` como "não implementado", ou responder vazio com "desligado".

**E2-10 | MÉDIO | O redator tem lacunas, e a promessa de "redação no serializador" não vale para o serviço**
- **Onde:** `src/Shared.Observability/RedatorDeDadoSensivel.cs:11-15,95-96,110`; `MontadorDoPacoteDeDiagnostico.cs:296-301`; `EdgeControlService.cs:577,623`; `Program.cs:173-180`.
- **Formatos que a expressão regular não redige:**
  - `codigo:12345678` (o lookbehind exclui `:`);
  - `T12345678`;
  - `1234-5678-9012-3456`;
  - QR alfanumérico;
  - códigos de 4 e 5 dígitos.
- **Onde a redação não é aplicada:** só o pacote usa o redator, além do `LogEstruturado`, que é opcional (`DeviceGroupLoop.cs:34`). O registro do serviço, o `diagnostico.txt` (com a 1ª linha de erro do worker) e `ObterDiagnostico.UltimasLinhas` saem sem redação.
- **Estado:** a expressão regular está CONFIRMADA. Se esses formatos aparecem em algum log é HIPÓTESE (cruzar com E1 e E8).
- **Correção mínima:** ampliar a expressão (aceitar `:` e `-`) e redigir também as linhas de diagnóstico.

### Baixos e informativos (uma linha cada)

- **E2-11 | BAIXO | HIPÓTESE:** migração, ACL recursiva, faxina e montagem da nuvem rodam antes de `RunAsync` (`Program.cs:103-206` vs `:294`). Uma migração longa depois de atualizar pode estourar o tempo de partida do SCM (erro 1053). NÃO VERIFICÁVEL AQUI.
- **E2-12 | BAIXO:** vários RPCs não capturam `SqliteException` e devolvem `Unknown`: `ObterEstado` (`EdgeControlService.cs:169`), `ListarAcessos` (313), `ObterConfiguracao` (321), `GravarConfiguracao` (336,362), `ObterSincronizacao` (386,396), `ObterPrestacaoDeContas` (426), `ConsultarCodigo` (482), `ExplicarNegativa` (529), `SimularLeitura` (692) e `EnviarComando` (776; parcial com inner 0, sem transação). WAL e `busy_timeout` atenuam.
- **E2-13 | BAIXO:** `WorkerSupervisor.Iniciar` (`WorkerSupervisor.cs:92-99`) roda sem trava e sem checar `_encerrado`. Uma parada durante a partida pode subir worker depois de `Encerrar`; o Job Object e a vigia do pai cobrem.
- **E2-14 | BAIXO:**
  - `ProcessoDeWorker.Iniciar` sobrescreve `_processo` sem `Dispose` (`ProcessoDeWorker.cs:124`): um handle vaza a cada reinício.
  - `RegistrarSaida` (189-197) lê o `_processo` atual: um `Exited` atrasado faz `WaitForExit` no processo novo e prende uma thread do pool.
- **E2-15 | INFORMATIVO:** `Process.Start` vem antes de `AssignProcessToJobObject` (`ProcessoDeWorker.cs:124→138`). Há uma janela curta fora do job; a vigia do pai cobre.
- **E2-16 | BAIXO:** `ConfiguracaoDoSupervisor.Validar` (82-117) não valida nome de grupo repetido, porta ≤0 nem `grupos` nulo. Nome repetido faz `Situacoes` (`WorkerSupervisor.cs:89`) lançar em todo RPC de estado; os outros casos dão exceção não tratada na partida (`Program.cs:70,140`).
- **E2-17 | BAIXO:** se `ComecarDoFim` falha, `_ultima` fica 0 (`AcompanhamentoDaOperacao.cs:41,132-140`). O histórico inteiro é reenviado ao painel como "ao vivo", 200 tentativas a cada 500 ms.
- **E2-18 | BAIXO | código morto com defeitos latentes:**
  - `RepositorioDosAlertas.cs:51` grava `$sessao = alerta.Id`; `:50` vs `:122` grava a regra como "leitorcalado" e lê como "leitor_calado"; `:141` faz cast `(int)` de Int64 e lança `InvalidCastException`.
  - `ConsultorDeReuso.cs:59` faz `CAST(device_id AS INTEGER)` sobre "inner-NN" e obtém 0; `:88` tem `catch` vazio; `:56` lê `qr_normalized` em claro.
  - `CadernoDosPosEventos` não tem uso.
  - `MetricasDoEdge` nunca é instanciado.
  - Esses arquivos declaram o namespace `Access.Infrastructure.SQLite` dentro de `Edge.Supervisor`.
- **E2-19 | INFORMATIVO:**
  - O token usa `FixedTimeEquals` corretamente (`InterceptadorDeToken.cs:73`). Ele só revela o tamanho, que é fixo em 44.
  - O interceptador cobre unário e streaming do servidor (36-55), o que basta: o `.proto` não tem RPC de streaming do cliente nem bidirecional. Uma RPC futura desses tipos passaria sem token.
- **E2-20 | INFORMATIVO:** a auditoria guarda o nome digitado, sem login (`proto:477-478`; `EdgeControlService.cs:758`). Qualquer processo com o token assina com qualquer nome.
- **E2-21 | BAIXO:**
  - O token nasce com ACL herdada e só depois é restringido (`SegurancaLocal.cs:54-60`).
  - `LerToken` só captura `UnauthorizedAccessException` (`InstalacaoLocal.cs:73`): uma `IOException` com o arquivo travado derruba a partida.
- **E2-22 | BAIXO:** `RestringirPastaDeDados` (157-171) cai com `FileNotFound` se um arquivo some entre a enumeração e a aplicação. Roda antes da faxina de órfãos (`Program.cs:103` vs `186`).
- **E2-23 | BAIXO:**
  - O `catch` do pacote (`EdgeControlService.cs:596`) não cobre `RegexMatchTimeoutException`.
  - `ReadAllLines` concorrente faz o worker perder linha de registro (`RegistroEmArquivo.Perdidas`).
- **E2-24 | BAIXO:** `ListarAcessos.Limite` não tem teto (`EdgeControlService.cs:311`). Em `ListarComandos`, um limite negativo vira "sem limite" (805).
- **E2-25 | BAIXO:** o comentário XML de `ExplicarNegativa` ficou duplicado e órfão, em cima de `NomeDaCatraca` (`EdgeControlService.cs:506-515`).
- **E2-26 | BAIXO:** pedido sem configuração responde "não tem base local" (`EdgeControlService.cs:330-333`), o que é falso.
- **E2-27 | INFORMATIVO:** no Linux (desenvolvimento), o socket fica em /tmp e o token sem `chmod` (`TransporteLocal.cs:58,73-78`; `SegurancaLocal.cs:54`).
- **E2-28 | INFORMATIVO:** configuração inválida faz `return 1` antes de reportar ao SCM (`Program.cs:67,81`). Resulta num laço de reinícios a cada 5 s.

## B) Cobertura (todos lidos inteiros)

| Arquivo | Linhas | Lido inteiro | Funções analisadas | Achados |
|---|---|---|---|---|
| Edge.Supervisor/EdgeControlService.cs | 961 | sim | 29 | 10 (01,06,09,10,12,20,23,24,25,26) |
| EdgeControlService.Parametrizacao.cs | 229 | sim | 7 | 0 |
| EdgeControlService.MapaDeGiro.cs | 274 | sim | 9 | 0 |
| Program.cs | 295 | sim | 2 (+12 etapas) | 9 (02,04,05,07,08,11,16,22,28) |
| SegurancaLocal.cs | 195 | sim | 5 | 4 (07,08,21,22) |
| WorkerSupervisor.cs | 259 | sim | 9 | 3 (03,13,16) |
| ProcessoDeWorker.cs | 238 | sim | 9 | 4 (01,05,14,15) |
| ContencaoDosWorkers.cs | 189 | sim | 6 | 1 (15) |
| FaxinaDeOrfaos.cs | 262 | sim | 8 | 0 |
| LacoDeSupervisao.cs | 35 | sim | 1 | 2 (02,05) |
| IWorkerHost.cs | 61 | sim | 7 membros | 0 |
| AgendadorDeCopias.cs | 86 | sim | 2 | 1 (02) |
| ZeladorDeRegistros.cs | 134 | sim | 2 | 0 |
| ImpedirSuspensao.cs | 65 | sim | 1 | 0 |
| AcompanhamentoDaOperacao.cs | 162 | sim | 6 | 2 (02,17) |
| DifusorDeEventos.cs | 130 | sim | 6 | 0 |
| EstadoDaNuvem.cs | 38 | sim | 3 | 0 |
| SincronizacaoComANuvem.cs | 279 | sim | 5 | 1 (04) |
| AnalisadorDaOperacao.cs | 299 | sim | 8 | 0 |
| CofreDeSegredos.cs | 179 | sim | 8 | 1 (04) |
| ChaveDaImpressao.cs | 78 | sim | 4 | 0 |
| MontadorDoPacoteDeDiagnostico.cs | 120 | sim | 4 | 3 (06,10,23) |
| ConfiguracaoDoSupervisor.cs | 119 | sim | 5 | 2 (08,16) |
| CamposDaParametrizacao.cs | 260 | sim | 10 | 0 |
| RepositorioDosAlertas.cs | 156 | sim | 4 | 1 (18) |
| CadernoDosPosEventos.cs | 69 | sim | 1 | 1 (18) |
| ConsultorDeReuso.cs | 105 | sim | 1 | 1 (18) |
| Instalacao/AssistenteDeConfiguracao.cs | 340 | sim | 7 | 0 (contexto do 01) |
| Instalacao/FirewallDasCatracas.cs | 40 | sim | 2 | 0 |
| Edge.Supervisor.csproj | 44 | sim | – | 0 |
| Contracts/InterceptadorDeToken.cs | 80 | sim | 5 | 1 (19) |
| Contracts/TransporteLocal.cs | 192 | sim | 6 | 2 (06,27) |
| Contracts/InstalacaoLocal.cs | 79 | sim | 4 | 2 (07,21) |
| Contracts/RegistroDeNovidades.cs | 59 | sim | 2 | 0 |
| Contracts/RegistroDeFalhas.cs | 37 | sim | 1 | 0 |
| Contracts/Protos/edge_control.proto | 980 | sim | 26 RPCs | 1 (09) |
| Contracts/Contracts.csproj | 31 | sim | – | 0 |
| Shared.Observability/RedatorDeDadoSensivel.cs | 120 | sim | 4 | 1 (10) |
| Shared.Observability/LogEstruturado.cs | 126 | sim | 8 | 0 |
| Shared.Observability/MetricasDoEdge.cs | 98 | sim | 3 | 1 (18) |
| Shared.Observability.csproj | 16 | sim | – | 0 |

## C) Veredito por capacidade (docs/40 §7)

- **Capacidade 7, parte do serviço: PARCIAL.**
  - Provado em CI: o worker morre com o serviço. O teste `WorkerMorreComOServicoTests.No_windows_fechar_o_job_object_mata_o_worker` (Windows CI verde) falharia sem `KILL_ON_JOB_CLOSE`. O teste `No_windows_a_faxina_encerra_o_orfao_de_verdade_e_poupa_o_filho_vivo` falharia com a faxina errada.
  - O que falta: o serviço pode parar sozinho e não voltar (E2-02), e não há teste do agendador de cópias.
- **Capacidade 9: PARCIAL.**
  - Worker morto: reinício implementado, testado só com dublê (`SupervisorTests.Worker_morto_e_reiniciado`).
  - Worker travado: ausente para o processo real (E2-01). O teste com dublê não falharia com o defeito.
  - Quarentena sem saída (E2-03).
  - Catraca sem comunicação e socket morto ficam no worker (E1).
- **Capacidade 12: PROVADO EM CI, contra o simulador, nunca em hardware.**
  - `GerenciarCatracaTests.Liberacao_manual_pelo_painel_gira_a_catraca_e_fica_auditada` (`tests/Integration/GerenciarCatracaTests.cs:109`) confere a fila, a execução e o registro com operador e motivo. Falharia se a auditoria não gravasse.
  - Ressalva: a identidade é só o nome digitado (E2-20).
- **Capacidade 16, parte local: PARCIAL.**
  - Token: PROVADO EM CI. `IpcLocalTests.Chamada_sem_token_e_recusada` e `Chamada_com_token_errado_e_recusada` falhariam sem o interceptador.
  - ACL do pipe: o teste `InstalacaoRealTests.O_canal_com_acl_propria_desliga_o_somente_usuario_atual` só verifica `PipeSecurity` não nulo; não prova quem entra.
  - Pasta de dados: IMPLEMENTADO SEM PROVA. Nenhum teste chama `RestringirPastaDeDados`, e o efeito colateral do E2-07 é desconhecido.
- **Capacidade 19: PARCIAL.**
  - O pacote é PROVADO EM CI: `PacoteDeDiagnosticoTests.O_zip_nao_traz_banco_token_copias_nem_registro_antigo` e `Numero_de_cartao_nos_registros_nao_sai_no_zip` falhariam com o montador errado.
  - O que falta: o histórico de supervisão e a saída do worker não persistem (E2-05), há limite de 4 MB (E2-06) e lacunas no redator (E2-10).

## D) Perguntas de decisão

1. **Como detectar worker travado?**
   - (a) **Recomendada:** pelo `device_status` desta sessão, que já é gravado a cada 2 s, com tolerância de 30 s, matando e reiniciando. Usa o canal que já existe (ADR-0024), sem IPC novo.
   - (b) Arquivo ou pipe de batimento novo.
   - (c) Aceitar o risco até a bancada.
2. **Como sair da quarentena?**
   - (a) **Recomendada:** sai sozinha depois de 15 min, com backoff longo, mais um comando "tentar de novo" auditado no painel. O operador resolve sem precisar de administrador.
   - (b) Só reiniciando o serviço (como hoje).
   - (c) Sem quarentena.
3. **O que fazer quando um serviço em segundo plano falha?**
   - (a) **Recomendada:** nunca derrubar o host. `BackgroundServiceExceptionBehavior=Ignore`, captura geral com registro em arquivo, e `FailureActionsOnNonCrashFailures` como segurança. As catracas não dependem da cópia nem do painel ao vivo.
   - (b) Derrubar e confiar no SCM, com código de saída diferente de 0.
   - (c) Manter como está.
