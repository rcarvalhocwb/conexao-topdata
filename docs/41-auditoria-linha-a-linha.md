# 41. Auditoria linha a linha do Rayzer XAcess

**Commit auditado:** `29df501`, ramo `claude/gallant-wright-pdloor`.
**Data:** 08/10/2026.
**Como foi feita:** conforme o docs/40. Dez especialistas leram o código só para leitura. O coordenador conferiu no código cada achado CRÍTICO e ALTO (revisão cruzada) e consolidou o resultado.
**Onde estão os relatórios brutos:** um por especialista, em [`docs/41-anexos/`](41-anexos/). Neles estão os achados MÉDIO, BAIXO e INFORMATIVO com caminho e linha, e as tabelas de cobertura completas.

Nada foi corrigido nesta etapa. Este documento é a base para decidir a correção.

---

## 1. Resumo executivo

### Estado geral

Hoje o software é bem testado contra uma catraca simulada. **Contra uma TopFit 4 real, ele nunca foi provado.**

- **Nenhuma das 20 capacidades** do objetivo está PROVADA EM HARDWARE.
- **Uma** está PROVADA EM CI de ponta a ponta: a decisão de acesso.
- **16** estão PARCIAIS, **1** está IMPLEMENTADA SEM PROVA e **2** estão BLOQUEADAS POR DECISÃO OU TERCEIRO.

A suíte tem 1.756 testes, todos verdes. A maior parte é forte: corrida entre catracas, base que falha nega, prazo do giro, sequência oficial byte a byte. Mas três lacunas de prova afetam o objetivo diretamente:

- o teste de queda abrupta (CHAOS-KILL-01) exercita um caminho que a produção não usa (E9-1);
- a camada `EasyInnerReal`, que liga o produto à DLL, não tem nenhum teste (E9-2);
- o serviço instalado nunca foi iniciado pelo Windows no CI (E10-5).

### Os 10 achados mais graves

| # | Achado | Severidade | Efeito no evento | Estado |
|---|---|---|---|---|
| 1 | **E1-01** Socket morto ou catraca muda, em espera de leitura, nunca é detectado: retorno ≠0 vira "sem eventos" e o painel segue mostrando "Atendendo" | CRÍTICO | A catraca para de liberar e ninguém é avisado | Confirmado no código; o valor real do retorno com cabo puxado depende de bancada (T2) |
| 2 | **E2-01 = E1-02** Worker travado dentro da DLL nunca é detectado nem reiniciado. "Saudável" significa só "processo vivo", e o watchdog não é lido por ninguém | CRÍTICO | Até 20 catracas do grupo param. Só reiniciar o serviço resolve | Confirmado por dois especialistas |
| 3 | **E2-02 = E3-02** Uma falha da cópia de segurança (`SqliteException`, por exemplo com disco cheio) derruba o serviço, mata os workers e sai com código 0. O Windows não religa | CRÍTICO | Todas as catracas param, até alguém intervir | Confirmado pelo coordenador |
| 4 | **E5-1** Lista de cartões cortada pela nuvem (limite de 1.000; o evento tem 2.243) é tratada como "nuvem ok" | CRÍTICO, condicionado | ~1.243 cartões válidos negados como desconhecidos | Confirmado do lado da borda; o corte do servidor não é verificável aqui |
| 5 | **E8-1** Sem segredo no cofre, a sincronização roda sem credencial e só escreve uma linha no registro | CRÍTICO, condicionado | Se o Supabase aceita isso (docs/22), qualquer um baixa nomes e grava acessos | Confirmado do lado da borda; o lado Supabase não é verificável aqui |
| 6 | **E10-1** Atualizar pelo Setup deixa o serviço parado: o `ServiceControl` não tem `Start` | ALTO | Depois de toda atualização, nenhuma catraca atende até reiniciar o PC | Confirmado pelo coordenador |
| 7 | **E1-04 = E4-4** Uma exceção ao gravar a confirmação do giro derruba o worker inteiro | ALTO | A passagem física se perde e as catracas do grupo caem | Confirmado por dois especialistas |
| 8 | **E1-05** Quando liberar o braço falha depois da autorização, o ingresso fica consumido e a tentativa fica aberta | ALTO | A pessoa autorizada não passa e pode ser negada por reuso | Confirmado pelo coordenador |
| 9 | **E3-01 = E5-4** Com ~35 min sem internet, as tentativas vão para cartas mortas, e não há como reenviar | ALTO | A prestação de contas na nuvem perde acessos para sempre | Confirmado pelo coordenador (12 tentativas, cerca de 2.110 s) |
| 10 | **E8-3 = E2-08** A restrição de ACL é recursiva e vale para a pasta do *banco*, que é configurável. Com `"banco": "D:\\acesso.db"`, o serviço reescreve a permissão do disco D inteiro | ALTO | Efeito destrutivo em pasta arbitrária | **Regressão introduzida no commit 1e50b2d**, confirmada pelo coordenador |

### Regressões introduzidas nesta própria rodada de trabalho

Três achados foram causados por commits recentes desta sessão. Por isso vão primeiro na correção:

- **E8-3 / E2-08**, commit `1e50b2d`: ACL recursiva aplicada à pasta do banco.
- **E6-1**, commit `3a6cc94`: os botões "Confirmar a aplicação" e "Cancelar", de "Aplicar agora nas catracas", nascem desabilitados. O setter não reavalia os comandos. O teste chama `ExecutarAsync` direto e por isso não percebeu.
- **E10-3**, commit `39a876a`: o `verificar-ambiente.ps1` entrou no MSI ainda escrito para o layout de desenvolvimento. Instalado, ele sempre falha, e o RB-01 manda rodá-lo.

### Resposta: estamos muito longe do objetivo?

**Sim, ainda estamos longe do objetivo de integração total, 100% operacional e confiável, mas a distância é conhecida e não exige refazer a arquitetura.**

- Os números: 0 de 20 capacidades provadas em hardware, 1 de 20 provada em CI de ponta a ponta, 5 achados críticos e 23 altos.
- O que falta é quase todo de três tipos:
  - correções localizadas de código, sem bancada (Fase 1 do caminho crítico);
  - ensaio em VM Windows (Fase 2);
  - bancada com uma TopFit 4 real (Fase 3).
- Há também decisões do dono e de terceiros.
- Nenhum achado indica que o desenho (serviço, worker x86, SQLite local, gRPC local) esteja errado.

---

## 2. Placar das 20 capacidades (docs/40 §7)

Estados possíveis: PROVADO EM HARDWARE, PROVADO EM CI, IMPLEMENTADO SEM PROVA, PARCIAL, AUSENTE e BLOQUEADO POR DECISÃO OU TERCEIRO.

| # | Capacidade | Estado | Evidência e o que falta |
|---|---|---|---|
| 1 | Carregar a `EasyInner.dll` no worker x86 e conectar | **IMPLEMENTADO SEM PROVA** | O CI prova o binário PE x86 (ci.yml:266-281). A DLL nunca foi carregada: HIL-STACK-01 não rodou. `EasyInnerReal` não tem teste (E9-2) |
| 2 | Configurar a catraca | **PARCIAL** | Cobertura e sequência oficial provadas até a costura falsa. Faltam: dígitos no padrão desconhecido da DLL (E1-07), sequência oficial desligada, tipo de leitor (T25) |
| 3 | Ler QR e cartão e normalizar | **PARCIAL** | A normalização preserva zeros (provado em CI). Faltam: perfil por leitor (E4-2), Mifare só dígitos (E4-3), buffer e leitura reais (E9-2) |
| 4 | Decidir: válido, reuso, janela, categoria, provedor, urna | **PROVADO EM CI** | Testes que conferem `Motivo` e `Liberou`, inclusive a corrida de 8 catracas com 1 vencedor. Lacunas: o limite exato da janela e o relógio do PC voltando (E4-5, E4-6) |
| 5 | Liberar no sentido certo | **PARCIAL** | A escolha da função nativa está provada até a costura. Mas um fio trocado em `EasyInnerReal` passaria no CI (E9-2), o sentido físico é A_CONFIRMAR (HIL-DIR) e o texto da liberação manual afirma "entrada" sem consultar o mapa (E6-6) |
| 6 | Confirmar o giro e encerrar sem giro no prazo | **PARCIAL** | A lógica do prazo está provada em CI (PrazoDoGiro). Falta encerrar a tentativa quando a liberação falha (E1-05). Os valores reais das origens 5 e 6 dependem de bancada |
| 7 | Registrar sem perder e sem duplicar, com queda | **PARCIAL** | A transação única está correta e a corrida está provada. O teste de queda exercita `AccessJournal`, que é código morto (E9-1 = E3-06). A exceção no giro derruba o worker e perde a passagem (E1-04) |
| 8 | Operar sem o PC e coletar bilhetes | **BLOQUEADO POR DECISÃO OU TERCEIRO** | A contingência depende da D8 (dono) e de T24 e T13 (Topdata). A coleta está provada no simulador, atrás de chave desligada. Os bilhetes coletados ficam fora da prestação de contas (E3-08) |
| 9 | Detectar falha de comunicação, socket morto e worker travado, e recuperar | **PARCIAL** | Funcionam: ping que falha ao conectar, backoff, disjuntor e worker morto. Faltam: socket morto em espera (E1-01), worker travado (E2-01), quarentena sem saída (E2-03) |
| 10 | Sincronizar com a nuvem | **PARCIAL** | Fila, cursor e falha parcial estão provados contra um painel falso. Faltam: corte tratado como sucesso (E5-1), carta morta em 35 min (E3-01), 401 sem volta (E5-2), `device_id` (E5-3), vazão de 200/min (E3-05) |
| 11 | Receber da Zet e conciliar | **BLOQUEADO POR DECISÃO OU TERCEIRO** | O tradutor e o relé existem e têm testes, mas **nenhum caminho de produção os liga**. Bloqueios: Z2 (caminho A/B/C) e Z1 (maquininha e cortesia, 5,8%; E5-5) |
| 12 | Liberação manual, display, relógio e aplicar, com auditoria | **PARCIAL** | Provado em CI no simulador, com a auditoria gravada. Na tela, "Aplicar em todas" está inoperante (E6-1), e "Refazer conexão" age com um clique só (E6-4) |
| 13 | Prestação de contas: resumo, R1–R8, PDF, corte fechado | **PARCIAL** | O resumo por janela está correto. Faltam: R1–R8 não existem (a tela os mostra como "Aguardando confirmação", E6-10), não há corte fechado, a conciliação não respeita o corte (E3-07) e os bilhetes coletados ficam fora (E3-08) |
| 14 | Painel do operador | **PARCIAL** | Todos os bindings foram conferidos (E6-19). Faltam: serviço travado aparece "Operacional" (E6-2), sinais verdes velhos (E6-3), textos técnicos ou falsos (E6-10, E6-11, E6-12), ações perigosas sem confirmação (E6-4, E6-5) |
| 15 | Alertas ao operador | **PARCIAL** | Existem balões da bandeja para catraca que sai de "Atendendo" e para o serviço. Faltam relógio, nuvem e cartas mortas (E6-14). As regras da camada inteligente não rodam e 7 RPCs devolvem `UNIMPLEMENTED` (E7-1) |
| 16 | Segurança local e da nuvem | **PARCIAL** | O token está provado em CI. Sem prova: ACL, que só um teste Windows vê. Faltam: elevação via `workers.json` (E8-2), ACL recursiva (E8-3), nuvem sem segredo (E8-1), token do relé em log (E8-4). Nuvem: depende de terceiro |
| 17 | Proteção de dado pessoal (LGPD) | **PARCIAL** | Redator, máscara e SEC-LOG-01 estão provados em CI. Faltam: retenção e expurgo (bloqueado pela D7), código em claro fora de `ticket` (E3-10), teste da telemetria que não falha (E9-4), relé guardando CPF para sempre (E8-5) |
| 18 | Instalar, atualizar, desinstalar, backup e restauração | **PARCIAL** | Instalar e desinstalar estão provados no CI, e a cópia também. Faltam: a atualização deixa o serviço parado (E10-1), instaladores colidem (E10-2), não há restauração (E3-03, E9-9), a migração roda sem cópia antes (E10-7), DISM sem caminho offline (E10-8) |
| 19 | Diagnóstico e runbooks | **PARCIAL** | O pacote de diagnóstico está provado em CI. 4 de 12 runbooks estão escritos e nenhum foi ensaiado. RB-01 passo 2 é inexecutável (E10-3), RB-06 testa na direção errada (E10-13), falha antes do registro não deixa rastro (E10-6) |
| 20 | Ensaios de longa duração, caos e carga | **PARCIAL** | O soak no CI tem 20 s, decisão constante e não passa pelo SQLite (E9-3). O caos de queda está no caminho morto (E9-1). Só a coleta tem SIGKILL real |

### Contagem por estado (de 20)

| Estado | Quantas | Quais |
|---|---|---|
| PROVADO EM HARDWARE | **0** | nenhuma |
| PROVADO EM CI | **1** | 4 |
| IMPLEMENTADO SEM PROVA | **1** | 1 |
| PARCIAL | **16** | 2, 3, 5, 6, 7, 9, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20 |
| AUSENTE | **0** | nenhuma |
| BLOQUEADO POR DECISÃO OU TERCEIRO | **2** | 8, 11 |

A contagem não tem ponderação. Uma capacidade PARCIAL pode estar a uma linha de PROVADO EM CI, caso da 12 com a E6-1, ou a semanas, caso da 13 com R1–R8.

---

## 3. Achados CRÍTICOS e ALTOS consolidados

Cada achado está com caminho e linha no anexo do especialista. Aqui fica a lista única, depois da fusão das duplicatas.

### CRÍTICO (5)

| ID | Onde | Resumo | Correção mínima |
|---|---|---|---|
| E1-01 | `TopdataInnerAdapter.cs:315-321`; `DevicePump.cs:609`; `SessaoDeOperacao.cs:39-47` | Em Polling, retorno ≠0 vira SemEventos, o disjuntor é zerado e a catraca segue marcada como "em operação". `Ping` nunca é chamado no laço | Ping periódico em Polling; retorno ≠0 conta como falha; publicar a catraca como não operante depois de K erros |
| E2-01 = E1-02 | `ProcessoDeWorker.cs:78`; `Watchdog.cs`; `WorkerSupervisor.cs:186-191,231-237` | `EstaSaudavel => EstaVivo`. Ninguém lê o watchdog, e o ramo "laço travado" nunca acontece com o worker real | O supervisor lê `device_status.atualizado_em` (gravado a cada 2 s) e, acima de 30 s, mata e reinicia |
| E2-02 = E3-02 | `AgendadorDeCopias.cs:58-61`; `CopiaDeSeguranca.cs:59-60`; `Program.cs:288,295`; `ConexaoTopdata.wxs:145-149` | O `catch` não pega `SqliteException`. Não há `BackgroundServiceExceptionBehavior`, então vale StopHost. Os workers são encerrados, `Main` devolve 0, e falta `FailureActionsOnNonCrashFailures` | Captura geral nos laços; `BackgroundServiceExceptionBehavior.Ignore`; código ≠0; `FailureActionsOnNonCrashFailures="yes"`; apagar a cópia parcial |
| E5-1 | `FonteDeCartoesDoPainel.cs:195-199`; `SincronizacaoComANuvem.cs:211-246` | O corte devolve página com cursor nulo, sem erro, e a rodada chama `RegistrarSucesso`. O teste correspondente não confere o estado da nuvem | Suspeita de corte vira falha da rodada, visível no painel |
| E8-1 | `CofreDeSegredos.cs:160-177`; `Program.cs:208-211` | Sem segredo, as requisições saem sem cabeçalho e o serviço só escreve uma linha no registro | Com `nuvem` configurada e sem segredo, não sincronizar e alertar no painel. No Supabase: exigir o segredo e tirar as permissões do `anon` |

### ALTO (23)

| ID | Resumo |
|---|---|
| E1-03 | No caminho real, o laço não tem pausa. Em backoff ou com disjuntor aberto, ele gira a 100% de um núcleo e grava milhares de linhas por segundo, num arquivo de registro sem limite (mesmo disco do SQLite) |
| E1-04 = E4-4 | `ConfirmarPassagemFisica` sem `try`: uma exceção mata o worker e a passagem só existia em memória |
| E1-05 | Falha em `LiberarGiro` → `Falhar(ErroDeComunicacao)` sem `_aoDesistirDoGiro`: ingresso consumido, tentativa aberta |
| E1-06 | Firmware fora de {14,16} leva a catraca a `FirmwareIncompativel`, um estado terminal que nenhuma configuração muda. Esses valores não têm fonte. A costura falsa usa a linha 4 e o simulador a 16 |
| E1-07 | Dígitos de cartão e QR ficam no padrão desconhecido da DLL (chave `EnviarDigitosVariaveis = false`) |
| E2-03 | A quarentena não tem saída, só reiniciando o serviço, e pode começar na primeira exceção |
| E2-04 | Um cofre DPAPI ilegível impede o serviço de subir |
| E3-01 = E5-4 | 12 falhas temporárias, cerca de 35 min, levam a carta morta, sem reenvio |
| E3-03 (+E9-9) | A restauração não existe no código. O RB-09 manual reverte os consumos, então quem já entrou volta a passar |
| E5-2 | 401/403 manda toda tentativa para cartas mortas, sem caminho de volta |
| E5-3 | O `device_id` das tentativas não bate com o segredo único do equipamento (decisão S10) |
| E5-5 | Maquininha e cortesia (5,8% dos pedidos) não têm nenhum caminho até a catraca |
| E5-6 | O relé aceita ingresso forjado de quem tiver a URL. Está latente porque o relé não está ligado em produção |
| E8-2 | O serviço (SYSTEM) lê `workers.json` e o token antes de restringir a pasta, e `Executavel` aceita qualquer caminho. Possível elevação local (HIPÓTESE; precisa de VM) |
| E8-3 = E2-08 | ACL recursiva na pasta do banco configurável. **Regressão de 1e50b2d** |
| E8-4 | O token do relé vai no caminho da URL, que o ASP.NET registra em nível Information (HIPÓTESE; depende da hospedagem) |
| E6-1 | "Aplicar agora nas catracas": o segundo passo nasce desabilitado. **Regressão de 3a6cc94** |
| E6-2 | As chamadas gRPC do painel não têm prazo: um serviço travado aparece "Operacional" e as chamadas se acumulam a cada 2 s |
| E9-1 = E3-06 | O CHAOS-KILL-01 prova durabilidade em `AccessJournal`, que é código morto. O caminho real, `TentarUsar`, não tem teste de queda |
| E9-2 | A costura falsa esconde a DLL. `EasyInnerReal` (delegação 1:1) não tem teste, e uma troca de fio passa no CI |
| E10-1 | Uma atualização deixa o serviço parado. A tela final do Setup diz o contrário |
| E10-2 | O instalador público e o privado usam o mesmo `UpgradeCode`, e as versões vêm de contadores independentes. Um instalador de teste pode remover a `EasyInner.dll` de uma máquina de evento |
| E10-3 | O `verificar-ambiente.ps1` instalado procura o layout de desenvolvimento e sempre falha. O RB-01 depende dele. **Regressão de 39a876a** |

### MÉDIO, BAIXO e INFORMATIVO

Antes da fusão de duplicatas, os especialistas reportaram ao todo 65 achados MÉDIO e 141 BAIXO ou INFORMATIVO, um por linha nos anexos. Destaques MÉDIO que afetam o evento:

- **E3-04:** `DefaultTimeout` do SQLite não definido. Uma negação por base ocupada pode levar ~30 s, não 5 s.
- **E3-05:** a nuvem tem vazão de no máximo 200 tentativas por minuto.
- **E3-09:** comando `recebido` cujo worker morreu fica sem desfecho para sempre.
- **E4-1:** transição latente `ValidarAcesso + TempoEsgotado → LiberarCatraca`.
- **E4-2:** a leitura usa o perfil `raw`, mas o cadastro usa `mifare-catraca4`.
- **E6-3:** sinais verdes ficam velhos quando o serviço cai.
- **E6-4 / E6-5:** "Refazer conexão" e "Gravar e iniciar o serviço" agem sem confirmação.
- **E6-6:** a liberação manual diz "entrada", mas segue o mapa de giro.
- **E7-1 = E2-09:** 7 RPCs do contrato devolvem `UNIMPLEMENTED`.
- **E7-2:** chaves `inteligencia.*` gravadas por builds antigos continuam ligando a camada.
- **E10-4:** a pré-release pública sai mesmo com teste falhando (run 157).
- **E10-6:** erro antes de abrir o registro não deixa rastro.
- **E10-7:** migração sem cópia antes e sem caminho de volta.
- **E10-8:** DISM sem caminho offline.
- **E10-13:** RB-06 testa na direção errada.

---

## 4. Revisão cruzada: convergências e desacordos

### Convergências: dois especialistas, sozinhos, chegaram ao mesmo defeito

| Par | Defeito |
|---|---|
| E1-02 / E2-01 | Worker travado não detectado |
| E1-04 / E4-4 | `ConfirmarPassagemFisica` sem proteção |
| E2-02 / E3-02 | Falha da cópia derruba o serviço |
| E3-01 / E5-4 | Carta morta em ~35 min |
| E8-3 / E2-08 | ACL recursiva na pasta do banco |
| E3-06 / E9-1 | Teste de queda no caminho morto |
| E7-1 / E2-09 | 7 RPCs não implementadas |
| E3-03 / E9-9 / E10-14 | Restauração inexistente e não ensaiada |
| E2-02 / E10-34 | Recuperação do Windows não age em parada "limpa" |

### Desacordos e como o coordenador decidiu

| Tema | Posições | Decisão |
|---|---|---|
| Severidade do worker travado | E1: ALTO; E2: CRÍTICO | **CRÍTICO**: para até 20 catracas sem recuperação |
| Severidade da falha da cópia | E3: ALTO; E2: CRÍTICO | **CRÍTICO**: para todas as catracas e não volta sozinho |
| Severidade de E8-3 | E8 e E2: MÉDIO | **ALTO**: reescreve ACL de pasta arbitrária, é regressão e o gatilho é configuração comum |
| Severidade de E4-4 | E4: MÉDIO; E1: ALTO | **ALTO**: derruba o grupo inteiro |
| Severidade do teste de queda | E3: MÉDIO; E9: ALTO | **ALTO**: esconde a lacuna da invariante mais importante (não perder acesso) |
| Capacidade 5 | E1 e E9: PROVADO EM CI (até a costura) | **PARCIAL**: `EasyInnerReal` não testado (E9-2) e texto da liberação manual (E6-6) |
| Capacidade 6 | E1 e E9: PROVADO EM CI (lógica) | **PARCIAL**: a tentativa não é encerrada quando a liberação falha (E1-05) |
| Capacidade 7 | E3: IMPLEMENTADO SEM PROVA; E2 e E9: PARCIAL | **PARCIAL**: a corrida está provada; a queda, não |
| Capacidade 8 | E9: PROVADO EM CI no simulador; E1: BLOQUEADO | **BLOQUEADO**: sem a D8 não há contingência, e a coleta nasce desligada |
| Capacidade 10 | E9: PROVADO EM CI; E3 e E5: PARCIAL | **PARCIAL**: E5-1 e E3-01 estão confirmados no código |
| Capacidade 11 | E9: PROVADO EM CI (payload sintético); E5: não ligado em produção | **BLOQUEADO**: o código testado não está em nenhum caminho de produção |
| Capacidade 12 | E2 e E9: PROVADO EM CI; E6: PARCIAL | **PARCIAL**: E6-1 confirmado; o defeito está na tela, onde o teste não alcança |
| Capacidade 15 | E7: AUSENTE (camada); E6: PARCIAL (bandeja); E9: função pura | **PARCIAL**: há alerta real na bandeja; a camada não roda |

O registro da revisão está em `docs/41-anexos/` (seção "Estado" de cada achado) e nas confirmações da tabela da seção 3.

---

## 5. Cobertura (docs/40 §6)

| Especialista | Área | Leitura | Lacunas declaradas |
|---|---|---|---|
| E1 | Topdata, worker, simulador | 21 arquivos, 6.144 linhas, todos inteiros | nenhuma |
| E2 | Serviço, supervisor, gRPC, observabilidade | todos inteiros, inclusive `EdgeControlService.cs` (961) e o `.proto` (980) | nenhuma |
| E3 | SQLite, migrações (16 + 9 da telemetria), Sync.Core | todos inteiros | nenhuma |
| E4 | Domínio, decisor, perfis, importação | todos inteiros; `SessaoDeOperacao` em trechos (lido inteiro pelo E1) | nenhuma |
| E5 | Conectores, ingestão, Zet, relé | todos inteiros | lado nuvem não verificável |
| E6 | ViewModels, telas XAML, configurador, Rayzer.Design | telas, ViewModels e configurador inteiros | estilos e temas do Rayzer.Design (`Generic.xaml`, `Controles.xaml`, `Tokens.xaml`, `Temas/*`, `Componentes.cs` 90-470) só varridos; risco apenas visual |
| E7 | Camada inteligente, telemetria | todos inteiros; `EdgeControlService` e `SessaoDeOperacao` em trechos (lidos inteiros por E2 e E1) | `HistogramaDeBaldes.cs` (lido pelo E1) |
| E8 | Segurança e LGPD | transversal, por ameaça, com `git grep` de segredos e CPF | nenhum segredo real encontrado |
| E9 | Testes e CI | Unit, Contract, CrashProbe, LoadAndSoak, HIL e `ci.yml` inteiros; Integration 61 de 63 inteiros | `TelasTests.cs` (1.772) e `SimuladorTests.cs` (363) varridos por grep |
| E10 | Instalador, CI, scripts, runbooks | todos inteiros | binários (`.xlsx`, `.png`) |

**Verificação do coordenador:** todos os 251 arquivos `.cs`, `.xaml`, `.proto` e `.sql` de `src/` (50.735 linhas) aparecem em pelo menos uma tabela de cobertura, contando as migrações pelo número. As tabelas por arquivo estão nos anexos.

---

## 6. Caminho crítico até o objetivo

Tipos: **C** código, **V** VM Windows, **B** bancada com TopFit 4, **D** decisão do dono, **T** terceiro.

### Fase 0: decisões que destravam o resto

| Item | Tipo | Quem resolve |
|---|---|---|
| D8: o que acontece com a catraca sem o PC (fail-secure por escrito ou contingência) | D | dono do produto |
| D5: dois sentidos na liberação | D | dono do produto |
| Hora de corte do dia e meia-entrada (decisões E9 e E10 do levantamento de produção, não dos especialistas) | D | dono do produto |
| D7: prazos de retenção e expurgo (LGPD) | D | dono e jurídico |
| Z2: caminho da Zet até a catraca; Z1: maquininha e cortesia | D + T | dono, equipe da nuvem e Zet |
| Supabase: exigir o segredo e tirar as permissões do `anon` | T | quem mantém o Supabase |
| Certificado de assinatura de código | T | dono (compra) |

### Fase 1: código, sem bancada

Na ordem:

1. **Regressões:**
   - E8-3: restringir só `InstalacaoLocal.PastaDeDados` e recusar "banco" fora dela;
   - E6-1;
   - E10-3.
2. **O serviço não cai e volta sozinho:** E2-02, E10-1, E10-6.
3. **Não perder passagem:** E1-04 e E1-05.
4. **Detectar catraca muda e worker travado:** E1-01 (ping periódico), E2-01 (batimento por `device_status`), E1-03 (pausa e limite do registro), E2-03 (saída da quarentena).
5. **Nuvem:** E5-1, E8-1, E3-01/E5-4 com reenvio, E5-2, E5-3.
6. **Prova:**
   - E9-1: CrashProbe sobre `TentarUsar`;
   - E9-2: teste de reflexão de `EasyInnerReal`;
   - E9-8: TRX no job Windows;
   - E10-4: `needs` no job do instalador.
7. **Instalador:** E10-2 (separar teste de produção) e E10-7 (cópia antes da migração).
8. **Painel:** E6-2 (prazo por chamada), E6-3 e E6-4.
9. **Restauração:** E3-03 (até existir a ferramenta, o RB-09 proíbe restaurar durante o evento).

### Fase 2: VM Windows

| Ensaio | Fecha |
|---|---|
| Serviço iniciado pelo SCM como LocalSystem; ACL da pasta aplicada | E10-5, E8-2 |
| Atualização N→N+1, com o serviço voltando sozinho | E10-1 |
| Disco cheio durante a cópia | E2-02 |
| DISM offline | E10-8 |
| Os 4 runbooks executados de verdade | E10-3, E10-13, E10-14 |

### Fase 3: bancada com TopFit 4 real

Na ordem:

1. **HIL-STACK-01:** carregar a DLL no worker x86 com .NET 10. Fecha a capacidade 1.
2. **HIL-CAP-01:** firmware real (fecha E1-06).
3. **HIL-EVT-01:** retorno com cabo puxado, T2 (fecha a severidade de E1-01).
4. **HIL-CARD-02 e T25:** dígitos e tipo de leitor (E1-07, capacidade 3).
5. **HIL-DIR:** sentido físico de cada função (capacidade 5).
6. **Origens 5 e 6 reais** (capacidade 6).
7. **HIL-BIL-01:** coleta (capacidade 8).
8. **CHAOS-NET-01:** cabo, switch e PC.
9. **Soak de 60 min** com a DLL real, depois 24 h (capacidade 20).

### Fase 4: piloto com 1 catraca, depois o evento

Só depois que as fases 1 a 3 estiverem verdes.

### Estimativa de esforço (estimativa, não compromisso)

| Fase | Esforço | Observação |
|---|---|---|
| 1. Código | **10 a 15 dias** de desenvolvimento | Cada item tem correção mínima conhecida e teste definido. O teto cobre retrabalho de CI |
| 2. VM Windows | **2 a 3 dias** | Precisa de uma VM Windows 10/11 limpa |
| 3. Bancada | **3 a 5 dias** de ensaio, **mais 3 a 5 dias** para corrigir o que a bancada revelar | Precisa da TopFit 4, do SDK e de rede igual à do evento. A DLL real pode contradizer hipóteses (T2, firmware, dígitos) |
| 4. Piloto | 1 evento ou 1 dia de operação controlada | Uma catraca, com liberação manual de contingência |
| Terceiros (Zet, Supabase, certificado, Topdata T24/T13) | **sem prazo controlável** | Correm em paralelo; as capacidades 8 e 11 dependem deles |

---

## 7. O que não foi possível verificar, e o que seria preciso

| Item | Por quê | O que é preciso |
|---|---|---|
| Comportamento real da `EasyInner.dll` (retornos, origens, firmware, dígitos, marshalling, bloqueio interno) | Não há DLL nem catraca aqui | Bancada com TopFit 4 e SDK (Fase 3) |
| MSI, SCM, ACL do Windows, DPAPI, DISM, Job Object | Ambiente Linux; o CI só instala e desinstala | VM Windows (Fase 2) e TRX do job Windows |
| Estado do Supabase (JWT, `anon`, paginação de 1.000) | Sem acesso à nuvem | Acesso de leitura ao projeto, ou confirmação de quem o mantém |
| Zet (payload real, maquininha, cortesia) | Terceiro | Contato com a Zet |
| Hospedagem e registro do relé | Não há implantação | Definir onde o relé roda |
| Desempenho real (latência da decisão, vazão, crescimento do WAL) | Sem carga real | Soak e carga na bancada |
| Exibição real das telas WPF | Ambiente Linux | Captura de telas no CI Windows (já existe) e uso manual |

---

## 8. Comandos executados e resultados

**Linha de base no commit 29df501:**

| Etapa | Resultado |
|---|---|
| Build Release | 0 avisos, 0 erros (34 s) |
| Unit | 857 aprovados, 0 falhas |
| Contract | 58 aprovados, 0 falhas |
| HardwareInLoop | 154 aprovados, 0 falhas (**contra a costura falsa, não contra hardware**) |
| Integration | 687 aprovados, 0 falhas |
| CI do GitHub, run 161 em 29df501 | 6 de 6 jobs verdes: Linux, Windows, Instalador MSI, Soak, Rayzer UI e Varredura |
| Histórico do CI (últimas 60 execuções, E9) | 21 sucessos, 8 falhas, 31 canceladas por `cancel-in-progress` |

**Especialistas:** só leitura, com Read, Grep, `git log`, `git show`, `git grep` e `gh api` de leitura. Nenhum build ou teste foi rodado por eles, e nenhum arquivo foi alterado.

**Conferências no código feitas pelo coordenador:**

- E1-01;
- E1-05;
- E2-02 (`AgendadorDeCopias.cs`, `CopiaDeSeguranca.cs`, `Program.cs`, `.wxs`);
- E3-01 (`DrenadorDaOutbox.cs:85,285-297`; espera de `SincronizacaoComANuvem.cs:198`);
- E3-03 (RB-09);
- E5-1 (`FonteDeCartoesDoPainel.cs`, `SincronizacaoComANuvem.cs`);
- E6-1 (`Telas.cs:865-898`, `Infra.cs:40-80`);
- E6-2 (grep de `Deadline`);
- E8-1 (`CofreDeSegredos.cs`, `Program.cs`);
- E8-3 (`Program.cs:103`);
- E10-1 (`.wxs`: nenhum `Start` nem ação de partida);
- E10-3 (`verificar-ambiente.ps1:18`);
- E10-4 (`ci.yml`: nenhum `needs:`);
- cobertura de todos os 251 arquivos de `src/`.

---

## 9. Perguntas de decisão

A opção recomendada está marcada com **[RECOMENDADA]**.

### Produto e operação

1. **Por onde começar a correção?**
   - (a) **[RECOMENDADA]** Fase 1 agora, na ordem da seção 6, começando pelas 3 regressões;
   - (b) bancada primeiro;
   - (c) só piloto.

   Motivo: a Fase 1 não depende de hardware e corrige, do lado do código, os 5 críticos. A severidade final do E1-01 ainda depende da bancada, e o lado Supabase do E8-1 depende de terceiro.
2. **PC caído durante o evento (D8):**
   - (a) **[RECOMENDADA]** no primeiro evento, fail-secure assumido por escrito (a catraca trava sem o PC), com alerta e procedimento de liberação manual;
   - (b) implementar a contingência antes do evento;
   - (c) adiar.

   Motivo: (b) depende da Topdata (T24, T13), sem prazo.
3. **Restauração do banco durante o evento:**
   - (a) **[RECOMENDADA até existir a ferramenta]** proibir e operar com liberação manual até o fim;
   - (b) ferramenta que reaplica os consumos da base antiga;
   - (c) manter o RB-09 como está.

   Motivo: com o RB-09 atual, quem já entrou passa de novo.
4. **Retenção LGPD (D7):**
   - (a) **[RECOMENDADA]** definir agora o prazo e implementar o expurgo (destruição de chave, purga dos códigos e das cópias, purga do relé);
   - (b) adiar para depois do primeiro evento;
   - (c) deixar de guardar o código em claro, revertendo o ADR-0014.
5. **Caminho da Zet:**
   - (a) **[RECOMENDADA]** caminho A (Worker repassa ao relé), com HMAC do corpo, mais caminho C (arquivo diário) de contingência, e exigir da Zet a exportação da maquininha e das cortesias até uma data;
   - (b) só o C;
   - (c) caminho B (ler do Supabase de produção).

### Serviço e catraca

6. **Detectar worker travado:**
   - (a) **[RECOMENDADA]** pelo `device_status`, com tolerância de 30 s, matando e reiniciando o worker;
   - (b) batimento novo por arquivo ou pipe;
   - (c) aceitar o risco até a bancada.
7. **Detectar catraca muda:**
   - (a) **[RECOMENDADA]** Ping periódico em Polling, independente da chave, mais HIL-EVT-01 como primeiro ensaio de bancada;
   - (b) ligar `reconectar_em_erro_de_recepcao` já;
   - (c) esperar a bancada.
8. **Falha num serviço em segundo plano:**
   - (a) **[RECOMENDADA]** nunca derrubar o host (`Ignore`, captura geral, registro em arquivo) e `FailureActionsOnNonCrashFailures` como rede de segurança;
   - (b) derrubar e confiar no SCM;
   - (c) manter como está.
9. **Saída da quarentena:**
   - (a) **[RECOMENDADA]** sozinha, depois de 15 min, mais um comando "tentar de novo" auditado no painel;
   - (b) só reiniciando o serviço;
   - (c) sem quarentena.
10. **Firmware homologado:**
    - (a) **[RECOMENDADA]** chave técnica com as linhas aceitas, e HIL-CAP-01 para fixar o valor;
    - (b) manter {14,16} fixo;
    - (c) desligar a verificação.

### Nuvem

11. **Rede fora por muito tempo:**
    - (a) **[RECOMENDADA]** falha temporária nunca vira carta morta por contagem, só por idade em dias; 401/403 suspende o conector com alerta; botão "reenviar cartas mortas";
    - (b) manter 12 tentativas e só acrescentar o reenvio;
    - (c) subir para 100 tentativas.
12. **Nuvem configurada sem segredo:**
    - (a) **[RECOMENDADA]** não sincronizar e alertar no painel;
    - (b) manter como está;
    - (c) desligar a nuvem até o Supabase exigir o segredo.
13. **`device_id` das tentativas:**
    - (a) **[RECOMENDADA]** um `device_id` e um segredo por PC, com o nome da catraca em `extra.catraca`;
    - (b) um segredo por catraca;
    - (c) a nuvem aceita qualquer `device_id`.
14. **Número do cartão ou QR em claro fora de `ticket`:**
    - (a) **[RECOMENDADA]** HMAC e máscara nas tentativas e na fila, e cópias cifradas;
    - (b) só cifrar as cópias;
    - (c) manter como está.

### Instalador e entrega

15. **Serviço depois de uma atualização:**
    - (a) **[RECOMENDADA]** sobe sozinho se já houver `workers.json`, com falha que não desfaz a instalação;
    - (b) documentar que deve ser iniciado à mão;
    - (c) iniciar sempre.
16. **Separar o instalador de teste do de produção:**
    - (a) **[RECOMENDADA]** `UpgradeCode` e nome de produto distintos para o teste;
    - (b) versão 1.0.N na produção e 0.1.N no teste, com o mesmo `UpgradeCode`;
    - (c) manter como está.
17. **Pré-release pública por commit:**
    - (a) **[RECOMENDADA]** só em push para main e com `needs` dos testes;
    - (b) por commit de PR, com `needs`;
    - (c) manter como está.
18. **Proteção de `%ProgramData%\ConexaoTopdata`:**
    - (a) **[RECOMENDADA]** o MSI cria a pasta com ACL protegida e dono Administradores, o serviço recusa `workers.json` ou token com dono inesperado, e `Executavel` fica restrito à pasta do programa;
    - (b) só documentar;
    - (c) assinar ou cifrar o `workers.json`.

### Painel e testes

19. **Confirmação em "Refazer conexão" e em "Gravar e iniciar o serviço":**
    - (a) **[RECOMENDADA]** dois passos nas duas;
    - (b) só no assistente;
    - (c) manter.
20. **Prazo nas chamadas do painel:**
    - (a) **[RECOMENDADA]** 5 s nas leituras e 15 s nos comandos, sem atualização sobreposta;
    - (b) 30 s para tudo;
    - (c) sem prazo.
21. **Termos técnicos nas telas do operador:**
    - (a) **[RECOMENDADA]** só em Diagnóstico e no modo técnico, e "Aguardando confirmação" vira "Ainda não existe" nos relatórios R1–R8;
    - (b) esconder só os códigos;
    - (c) manter.
22. **RPCs não implementadas da camada inteligente:**
    - (a) **[RECOMENDADA]** responder "desligada nesta instalação" e migração que apaga as chaves `inteligencia.*` antigas;
    - (b) remover do proto;
    - (c) implementar a I.3–I.10 antes do evento.
23. **Teste de queda abrupta:**
    - (a) **[RECOMENDADA]** reescrever a cobaia sobre `TentarUsar` e `ConfirmarPassagemFisica` e apagar o `AccessJournal`;
    - (b) documentar a lacuna;
    - (c) adiar.
24. **Soak e evidência do CI:**
    - (a) **[RECOMENDADA]** soak noturno de 60 min com o caminho real (SQLite, decisor, outbox), medindo working set e handles; o de 20 s fica como fumaça; Skip explícito fora do Windows; TRX no job Windows;
    - (b) manter;
    - (c) 60 min em todo PR.
25. **Perfil de leitura e código Mifare com letras:**
    - (a) **[RECOMENDADA]** manter `raw`, proibir provedor com outro perfil até existir parametrização por catraca, e recusar Mifare com letras na importação;
    - (b) completar zeros na leitura;
    - (c) esperar a bancada.
