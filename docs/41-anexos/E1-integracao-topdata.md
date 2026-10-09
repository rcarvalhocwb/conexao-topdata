> Relatório bruto de um especialista da auditoria linha a linha (docs/40), commit auditado 29df501.
> Os estados aqui são os do especialista, antes da revisão cruzada; o consolidado é o docs/41.

RELATÓRIO E1 — ENGENHEIRO DE INTEGRAÇÃO TOPDATA (commit 29df501, somente leitura)

Comandos usados: `git log --oneline -1`, `wc -l`, Read, Grep e `sed -n`/`grep` para ler. Não rodei build nem testes; uso o resultado que o coordenador já tinha (build ok, 1756 testes verdes). Li também, só para ter contexto: DeviceStateMachine.cs e ITopdataInnerAdapter.cs inteiros, DecisorDeIngresso.cs:155-274, ConfiguracoesDaBorda.cs:60-139, trechos de DeviceConfiguration.cs, ProcessoDeWorker.cs:60-95, docs/34 e o anexo 01.

## Conferência do levantamento de 07/10

1. **Retorno ≠0 de ReceberDadosOnLine virando "sem eventos": procede.** Está em TopdataInnerAdapter.cs:315-321. Melhorou num ponto: agora o erro é contado (`_errosDeRecepcao`, :317; `DeviceSlot.ErrosDeRecepcao`, DevicePump.cs:617) e entra no registro só nos marcos 1, 10, 100… (DevicePump.cs:618-622). A reconexão continua atrás da chave `ReconectarEmErroDeRecepcao`, que vem desligada por padrão (ConfiguracoesDaBorda.cs:96; Program.cs:248-255).
2. **Disjuntor que nunca abre: procede em parte.**
   - Na recepção, procede. O ramo SemEventos chama `RegistrarSucesso()` mesmo com NativeReturn≠0 (DevicePump.cs:609). O teste `RecepcaoComErroTests.Com_a_chave_desligada_erro_na_recepcao_conta_e_segue_em_polling` congela esse comportamento: `Assert.Equal(0, catraca.Disjuntor.FalhasSeguidas)` em RecepcaoComErroTests.cs:113.
   - No caminho de conexão, não procede: o disjuntor abre. Ping falha → `Falhar` → `RegistrarFalha` (DevicePump.cs:480 e 1219), que abre na 5ª falha seguida (CircuitBreaker.cs:33 e 75-78).
3. **Dígitos variáveis desligados: procede.** O padrão `EnviarDigitosVariaveis = false` está em ConfiguracoesDaBorda.cs:95. Com ele desligado, o adapter não chama `InserirQuantidadeDigitoVariavel` (TopdataInnerAdapter.cs:490-497). Ver E1-07.
4. **Firmware {14,16}: procede.** O padrão está em DevicePump.cs:344 e não tem fonte (docs/34-anexos/01:149-151, pendência T5). Nem SessaoDeOperacao.cs:237-248 nem Program passam outras linhas, então não há como configurar sem mudar código. Ver E1-06.

## A) Achados

**E1-01 | CRÍTICO | Socket morto ou catraca muda em Polling nunca é detectada**
- **Onde:** TopdataInnerAdapter.cs:315-321 e 326-329; DevicePump.cs:573-653 (sobretudo :607-636 e :609); SessaoDeOperacao.cs:39-47; CircuitBreaker.cs:65-69.
- **Funções:** TopdataInnerAdapter.AguardarEvento, DevicePump.Aguardar, SessaoDeOperacao.Situacao.
- **O que o código faz:**
  - Com a chave no padrão, todo retorno ≠0 (menos o 8) volta como SemEventos.
  - Retorno 0 com origem 0 também é SemEventos.
  - O laço zera o disjuntor e continua em Polling.
  - Em Polling e MonitoraGiro não existe outra sonda de vida: `Ping`/`PingOnLine` do adapter nunca são chamados no laço. Grep: `adapter.Ping` não tem chamador; TestarConexao só roda em Conectar, DevicePump.cs:464.
  - A situação publicada marca Polling como `EmOperacao=true`, com notícia sempre fresca.
- **Por que é problema no evento:** com cabo puxado ou socket morto, a catraca para de ler e de liberar. O painel continua mostrando "Atendendo" e o operador não é avisado. Pela escala do docs/40 §5, isso é "deixar de liberar quem deveria sem aviso". O docs/34-anexos/01:477 diz que "só o watchdog pega", mas o watchdog só percebe thread presa, não socket morto que responde rápido (ver E1-02).
- **Como provar:**
  - Teste que deveria existir: costura falsa com `ReceberDadosOnLine` = 1 constante e a chave no padrão; esperar sair de Polling ou `EmOperacao=false` em N segundos. Hoje o teste citado acima afirma o contrário.
  - Na bancada: HIL-EVT-01 e CHAOS-NET-01 (cabo puxado).
- **Estado:** CONFIRMADO pela leitura quanto ao caminho do código. NÃO VERIFICÁVEL AQUI qual valor a DLL devolve com cabo puxado (pendência T2).
- **Correção mínima:** em Polling, um `TestarConexao` (Ping) periódico, por exemplo a cada N segundos sem evento, uma chamada por passo. Contar uma falha no disjuntor quando NativeReturn≠0. Publicar a catraca como não operante depois de K erros de recepção seguidos.

**E1-02 | ALTO | Worker travado dentro da DLL não é recuperado; o watchdog é decorativo**
- **Onde:** Watchdog.cs:7-11 e 50; DeviceGroupLoop.cs:16-19 e 99; Edge.Supervisor/ProcessoDeWorker.cs:69-78; EdgeControlService.cs:28 e 952.
- **O que o código faz:**
  - O laço bate o watchdog a cada passo, mas nada em produção lê `Watchdog.EstaSaudavel` (grep: só ProcessoDeWorker tem uma propriedade com o mesmo nome).
  - O supervisor considera o worker saudável enquanto o processo existe (`EstaSaudavel => EstaVivo`). O próprio comentário diz "Não é batimento de verdade".
  - O painel só marca "notícia velha" depois de 15 s.
- **Por que é problema:** se `ReceberDadosOnLine` não voltar (Watchdog.cs:8-9 descreve exatamente isso), todas as catracas daquele worker param, até 20. Ninguém mata nem reinicia o processo. O comentário "a ação é matar o worker" (Watchdog.cs:10-11; DeviceGroupLoop.cs:18-19) não corresponde ao código.
- **Como provar:** `WorkerTests.Catraca_travada_bloqueia_o_worker_e_o_watchdog_percebe` (WorkerTests.cs:302) prova só que a classe percebe. Falta um teste de ponta a ponta: worker travado → supervisor mata e sobe de novo.
- **Estado:** CONFIRMADO pela leitura. A parte do supervisor deve ser revista pelo E2.
- **Correção mínima:** o supervisor trata `device_status.AtualizadoEm` mais velho que X segundos (já existe `NoticiaVelha`) como SemBatimento e mata o processo; ou o worker expõe o batimento do Watchdog pelo IPC.

**E1-03 | ALTO | No caminho real o laço não tem pausa: um núcleo a 100% e o registro inundado quando catracas estão em espera ou com disjuntor aberto**
- **Onde:** Program.cs:392-411 (`aCadaVolta` só existe no simulador, com `Thread.Sleep(20)`); SessaoDeOperacao.cs:291-294 e 304-311; DevicePump.cs:396-404; RegistroEmArquivo.cs:45-63.
- **O que o código faz:**
  - Um passo em espera de reconexão ("aguardando backoff") ou com disjuntor aberto devolve na hora, sem nenhuma chamada nativa.
  - SessaoDeOperacao registra toda ação que não seja exatamente "sem eventos", e cada linha abre, escreve e fecha o arquivo e ainda vai para o Console.
  - O arquivo do dia não tem limite de tamanho nem expurgo.
- **Por que é problema:** numa queda de rede ou de switch, todas as catracas ficam até 2 min em espera (BackoffComJitter.cs:30). O laço gira sem parar, gravando milhares de linhas por segundo. Isso pode encher o disco, que é o mesmo do SQLite, e então a base falha e acessos deixam de ser registrados. O próprio Program.cs:403 reconhece o risco ("sem pausa, o laço ocuparia um núcleo inteiro"), mas só trata o simulador. A frequência real das voltas com a DLL depende do bloqueio interno de ReceberDadosOnLine (T2).
- **Como provar:** teste que deveria existir: SessaoDeOperacao com adaptador cujo `TestarConexao` sempre falha, relógio real, 2 s rodando; contar as linhas escritas. Ensaio NOVO-LOAD-LOOP-01.
- **Estado:** CONFIRMADO pela leitura. A vazão real é NÃO VERIFICÁVEL AQUI.
- **Correção mínima:**
  - Quando nenhum passo da volta fez chamada nativa, dormir alguns milissegundos (por exemplo até o menor `EsperarAte`).
  - Registrar "aguardando backoff" e "disjuntor aberto" só na mudança de estado ou nos marcos (o `EhMarco` já existe).
  - Pôr limite de tamanho no RegistroEmArquivo.

**E1-04 | ALTO | Exceção ao gravar a confirmação do giro derruba o worker inteiro e perde a passagem**
- **Onde:** DecisorDeIngresso.cs:220-230 (`ConfirmarPassagemFisica` sem try/catch); SessaoDeOperacao.cs:550-575; DevicePump.cs:589; DeviceGroupLoop.cs:97-113; SessaoDeOperacao.cs:304-311; Program.cs:71-130 (só captura DllNotFound e BadImageFormat).
- **O que o código faz:** o evento de giro vai por `_aoReceberEvento` até `_validador.ConfirmarPassagemFisica`. Ao contrário de `Decidir` (DecisorDeIngresso.cs:189), esse caminho não tem proteção. Uma exceção, por exemplo base ocupada ou disco cheio, sobe até `Main`.
- **Por que é problema:**
  - O processo morre e todas as catracas do worker caem até o supervisor subir outro.
  - O giro já saiu da DLL e não foi gravado: a passagem física se perde, porque o pendente está só em memória.
  - O mesmo vale para qualquer exceção do registro de transição ou do decisor no laço.
- **Como provar:** teste com um validador que lança em `ConfirmarPassagemFisica` e verificar se o laço sobrevive. Hoje não sobrevive.
- **Estado:** CONFIRMADO pela leitura que não há proteção. HIPÓTESE quanto à frequência da exceção (o E3 deve ver `busy_timeout` e transação).
- **Correção mínima:** try/catch em `Receber` e `Decidir` da SessaoDeOperacao, guardando o evento de giro numa fila de regravação, como já se faz com os desfechos (SessaoDeOperacao.cs:447-470). E um try/catch por passo em `UmaVolta`, para uma catraca não derrubar as outras.

**E1-05 | ALTO | Liberação que falha depois da autorização: ingresso consumido, braço não liberado e tentativa não encerrada**
- **Onde:** DevicePump.cs:818-834 (falha em `LiberarGiro` → `Falhar(ErroDeComunicacao)`, sem `_aoDesistirDoGiro`); DevicePump.cs:644-651 (erro de recepção em MonitoraGiro, mesmo caso). Compare com DevicePump.cs:1188-1192, onde o prazo em LiberarCatraca encerra a tentativa. O consumo acontece antes, em DecisorDeIngresso.cs:180-207.
- **O que o código faz:** o ingresso já foi consumido na decisão. Se `LiberarCatraca*` devolve ≠0, a catraca vai para reconexão (reenvia a configuração três vezes), a pessoa não recebe mensagem nenhuma no display e o pendente fica aberto no decisor.
- **Por que é problema:**
  - A pessoa autorizada não passa. Ao apresentar de novo, pode ser negada por reuso ou por usos esgotados (regra do E4).
  - Um giro (origem 6) que chegue depois da reconexão confirma a tentativa antiga.
- **Como provar:** costura falsa com `LiberarCatracaEntrada` = 1 e então: (a) verificar `AutorizacoesSemGiro` e o pendente; (b) fazer outra leitura do mesmo código. Não encontrei teste desse caminho.
- **Estado:** CONFIRMADO pela leitura. O efeito na regra de reuso fica para o E4.
- **Correção mínima:** chamar `_aoDesistirDoGiro` em toda saída de LiberarCatraca e MonitoraGiro por erro, e marcar a tentativa como "liberação não executada" para estornar o uso, decisão do E4. Opcional: mensagem de erro no display antes de reconectar.

**E1-06 | ALTO | Linhas de firmware homologadas {14,16} sem fonte, e FirmwareIncompativel é beco sem saída**
- **Onde:** DevicePump.cs:344 e 499-510; DevicePump.cs:449 (estado sem ação); DeviceStateMachine.cs:247 e 327 (a saída é só `EntrarEmManutencao`, e grep mostra que nada a dispara); SessaoDeOperacao.cs:237-248 e Program.cs não passam outras linhas.
- **O que o código faz:** se a TopFit 4 informar uma Linha diferente de 14 ou 16, a catraca vai para FirmwareIncompativel e fica parada para sempre, sem chamada nem saída. Nenhuma configuração muda isso.
- **Por que é problema:** a catraca não opera no dia e o operador vê o estado mas não consegue resolver sem nova versão do software. A tabela de `Linha` do manual está desalinhada (docs/11:249). O simulador usa a linha 16 (SimulatedDevice.cs:51), então os testes passam por construção.
- **Como provar:** HIL-CAP-01, que lê `ReceberVersaoFirmware` numa TopFit 4 real.
- **Estado:** HIPÓTESE quanto ao valor real; CONFIRMADO pela leitura que o estado é terminal.
- **Correção mínima:** linhas homologadas como chave técnica lida na subida, com registro. Depois de HIL-CAP-01, fixar o valor lido.

**E1-07 | ALTO | Dígitos do cartão e do QR ficam com o padrão desconhecido da DLL**
- **Onde:** ConfiguracoesDaBorda.cs:95; DeviceConfiguration.cs:150-176; MontadorDaConfiguracao.cs:45-47 (padrão Livre, tamanhos de 4 a 16, `QuantidadeFixaDeDigitos` nula); TopdataInnerAdapter.cs:482-497.
- **O que o código faz:** com a chave no padrão, não chama nem `DefinirQuantidadeDigitosCartao` nem `InserirQuantidadeDigitoVariavel`. A catraca fica com o valor de fábrica da DLL, que ninguém conhece (docs/34:32-37, F2).
- **Por que é problema:** se o padrão da DLL não aceitar os comprimentos de QR em uso (4 a 16, conforme a Topdata em docs/34:42-44), a catraca não lê, ou trunca, ingresso válido.
- **Como provar:** HIL-CARD-02, além de T25 (tipo de leitor 5 ou 8).
- **Estado:** NÃO VERIFICÁVEL AQUI. É decisão pendente com ensaio.
- **Correção mínima:** ligar a chave depois de HIL-CARD-02, ou já para o primeiro evento se a bancada confirmar.

**E1-08 | MÉDIO | O simulador difere da DLL real justamente onde os testes deveriam pegar falhas**
- **Onde:** InnerSimulator.cs:235-238 e 254-257 (queda vira `ErroDeComunicacao` ou retorno bruto sem a conversão do adapter); TopdataInnerAdapter.cs:318-320 (a DLL real converte para SemEventos); ITopdataInnerAdapter.cs:119-134 (FromNative nunca devolve ErroDeComunicacao para ReceberDadosOnLine); InnerSimulator.cs:262 (sem evento devolve na hora); Program.cs:403-408 (pausa só no simulador); InnerSimulator.cs:126-155 (configuração tratada no nível do adapter, sem os 128/129 por função); SimulatedDevice.cs:51 (linha 16); InnerSimulator.cs:360-361 ("sem bilhetes" por nulo, no adapter real é buffer vazio, :397).
- **Por que é problema:** `WorkerTests.Catraca_que_cai_volta_a_operar_quando_a_conexao_volta` (WorkerTests.cs:94-120) passa no simulador. Com a DLL real e a chave no padrão, o mesmo cenário fica em Polling (E1-01). O verde do CI infla a capacidade 9. O próprio Program.cs:246 admite: "o simulador declara a queda por si".
- **Estado:** CONFIRMADO pela leitura.
- **Correção mínima:** rodar os testes de queda e reconexão também com `TopdataInnerAdapter` sobre a `CosturaFalsa`, que é o padrão de tests/HardwareInLoop, e deixar o simulador devolver o retorno bruto passando pelo mapeamento real.

**E1-09 | MÉDIO | "Sem bilhetes" pressupõe retorno 0 com buffer vazio**
- **Onde:** TopdataInnerAdapter.cs:388-400; DevicePump.cs:702-704 e 977-981.
- **O que o código faz:** se a DLL sinalizar "sem bilhetes" com um código ≠0 (o manual cita `RET_SEM_BILHETES` sem valor, docs/11:250), toda coleta termina em erro de comunicação. A catraca vai para reconexão, o comando termina "Falhou / coleta interrompida" e reconfigura três vezes.
- **Estado:** HIPÓTESE. Está atrás da chave `catraca.coletar_bilhetes`, desligada; o ensaio é HIL-BIL-01.
- **Correção:** confirmar na bancada e mapear o código em RetornosDocumentados para SemBilhetes.

**E1-10 | MÉDIO | AguardarEvento ignora o parâmetro de limite**
- **Onde:** TopdataInnerAdapter.cs:303-309; o contrato em ITopdataInnerAdapter.cs:274-277 diz "bloqueia até haver evento, timeout ou erro"; o limite de 500 ms vem de DeviceGroupLoop.cs:75.
- **Por que é problema:** a duração de cada volta fica entregue ao bloqueio interno desconhecido da DLL (T2). Com até 20 catracas por worker, isso afeta a latência da decisão e o prazo do giro. O laço não tem como limitar.
- **Estado:** CONFIRMADO pela leitura que o parâmetro não é usado; o efeito é NÃO VERIFICÁVEL AQUI (NOVO-LOAD-LOOP-01).
- **Correção:** documentar no contrato que o limite não vale para a DLL real e medir a pior volta na bancada.

**BAIXO e INFORMATIVO (uma linha cada)**
- E1-11 | INFORMATIVO | `PingOnLine` nunca é chamado (grep): sem contingência off-line, sem o PC a catraca para de liberar (docs/34:38-41). É decisão D8, ainda não tomada.
- E1-12 | BAIXO | DevicePump.cs:594-602 com DeviceStateMachine.cs:308-312: leitura que chega durante MonitoraGiroCatraca é registrada mas não decidida (a transição não existe), e `UltimoEvento` é sobrescrito. A pessoa precisa apresentar de novo.
- E1-13 | BAIXO | Os gatilhos `Quarentenar`, `CairParaListaLocal`, `EntrarEmManutencao` e `Habilitar` não são disparados em lugar nenhum de src (grep): Quarentena, OfflineAutonomo e Manutencao são inalcançáveis ou sem saída.
- E1-14 | BAIXO | Program.cs:114-130 não captura `EntryPointNotFoundException`: um export ausente na versão da DLL derruba o worker sem mensagem orientadora (só com as chaves da etapa A.2 ligadas).
- E1-15 | BAIXO | EasyInnerGerada.cs:23-24 diz "nada aqui é chamado pelo produto hoje", mas EasyInnerReal.cs:54-88 chama seis funções desse arquivo.
- E1-16 | BAIXO | EasyInnerGerada.cs:139-143: `ReceberDigitalUsuario` está declarada com duas assinaturas para o mesmo export, e no máximo uma está certa. Não é usada.
- E1-17 | INFORMATIVO | EasyInnerNative.cs:282 e 286: as strings vão como ANSI (CharSet padrão), e acentos dependem da página de código do Windows. O conjunto de caracteres do display é T27.
- E1-18 | INFORMATIVO | EasyInnerNative.cs:45: o buffer de cartão tem 64 bytes e a DLL não recebe o tamanho. Basta para cartão numérico de até 16 dígitos; QR com letras pode estourar (docs/34 §8, T25).
- E1-19 | INFORMATIVO | P/Invoke: todos usam `Winapi` (StdCall em x86), `ExactSpelling`, retorno byte, `int Inner`, `ref short Variacao`, e `byte[]` blittable é fixado e visível nos dois sentidos. Não encontrei defeito de marshaling nas 38 funções usadas.
- E1-20 | BAIXO | DevicePump.cs:847: se a mensagem de negação falha, a catraca reconecta inteira e reenvia a configuração três vezes.
- E1-21 | BAIXO, HIPÓTESE | DevicePump.cs:464: a reconexão só faz `Ping` e nunca reabre a porta TCP (`AbrirPorta` só na subida, Program.cs:73). Se o listener da DLL degradar, só reiniciar o worker resolve.
- E1-22 | INFORMATIVO | O coletor de telemetria nunca é passado em produção (Program.cs:354-377) e o nome do worker está fixo em "worker-1" (SessaoDeOperacao.cs:509).
- E1-23 | BAIXO | SessaoDeOperacao.cs:46: `ColetarBilhetes` é contado como "em operação", mas nesse estado a catraca não lê (DevicePump.cs:936-937).

**Para a revisão cruzada (E2):** E1-01, E1-02, E1-03, E1-04, E1-05, E1-06 e E1-07.

## B) Cobertura

| Arquivo | Linhas | Lido inteiro | Funções analisadas | Achados |
|---|---|---|---|---|
| Interop/EasyInnerNative.cs | 319 | sim | 40 (38 externs + 2) | 3 (E1-17, 18, 19) |
| Interop/IEasyInnerNative.cs | 212 | sim | 40 | 0 |
| Interop/EasyInnerReal.cs | 156 | sim | 40 | 0 (citado em E1-15) |
| Interop/EasyInnerGerada.cs | 655 | sim | cerca de 150 declarações (6 usadas) | 2 (E1-15, 16) |
| Adapter/TopdataInnerAdapter.cs | 589 | sim | 30 | 3 (E1-01, 09, 10) |
| Edge.Worker/DevicePump.cs | 1224 | sim | 38 | 7 (E1-01, 05, 06, 12, 13, 20, 21) |
| Edge.Worker/DeviceGroupLoop.cs | 133 | sim | 5 | 2 (E1-02, 03) |
| Edge.Worker/Watchdog.cs | 69 | sim | 6 | 1 (E1-02) |
| Edge.Worker/Resiliencia/CircuitBreaker.cs | 80 | sim | 4 | 1 (E1-01) |
| Edge.Worker/Resiliencia/BackoffComJitter.cs | 54 | sim | 2 | 0 |
| Edge.Worker/Resiliencia/VigiaDoProcessoPai.cs | 198 | sim | 6 | 0 |
| Edge.Worker/Operacao/SessaoDeOperacao.cs | 576 | sim | 16 | 5 (E1-01, 03, 04, 22, 23) |
| Edge.Worker/Operacao/RegistroEmArquivo.cs | 65 | sim | 2 | 1 (E1-03) |
| Edge.Worker/Bancada/SessaoDeBancada.cs | 147 | sim | 8 | 0 |
| Edge.Worker/VerificadorDePreRequisitos.cs | 91 | sim | 2 | 0 |
| Edge.Worker/ColetorDeTelemetria.cs | 109 | sim | 14 | 0 |
| Edge.Worker/HistogramaDeBaldes.cs | 160 | sim | 6 | 0 |
| Edge.Worker.X86/Program.cs | 491 | sim | 7 | 3 (E1-03, 14, 22) |
| Simulator/InnerSimulator.cs | 445 | sim | 22 | 1 (E1-08) |
| Simulator/SimulatedDevice.cs | 266 | sim | 20 | 1 (E1-08) |
| Simulator/ConducaoDeLeituras.cs | 105 | sim | 3 | 0 |

Os 21 arquivos atribuídos, 6.144 linhas, foram lidos inteiros.

## C) Placar da minha área (docs/40 §7)

1. **Carregar a EasyInner.dll no worker de 32 bits e conectar — IMPLEMENTADO SEM PROVA.** O caminho existe (Program.cs:65-130; TopdataInnerAdapter.cs:89-118). `AdapterTests.Abrir_porta_define_o_tipo_de_conexao_antes` usa a costura falsa: prova a ordem das chamadas, não o carregamento da DLL. O HIL-STACK-01 nunca rodou (Program.cs:62-64), e ainda não se sabe se .NET 10 em x86 carrega a DLL que depende do .NET 3.5.
2. **Configurar a catraca — PARCIAL.** Faltam: dígitos (E1-07); sequência oficial desligada (ConfiguracoesDaBorda.cs:102); cerca de 30 funções de montagem com o padrão da DLL (docs/34:32-37); tipo de leitor 8 ou 5 (T25). `CoberturaDaConfiguracaoTests` e `AdapterTests.Configuracao_valida_termina_em_enviar_configuracoes` / `Passo_que_falha_impede_o_envio` falhariam com a sequência errada, mas não provam o efeito na catraca.
3. **Ler QR e cartão e normalizar — PARCIAL.** Só recepção numérica, buffer de 64 bytes, alerta do leitor 8 (DeviceConfiguration.cs:515-520), normalização "raw" igual a Trim (DecisorDeIngresso.cs:163-165). `AdapterTests.Zeros_a_esquerda_da_credencial_sobrevivem` falharia se o adapter cortasse zeros.
5. **Liberar o braço no sentido certo — PROVADO EM CI, só quanto à escolha da função nativa.** `LiberacaoDePontaAPontaTests.Ingresso_autorizado_chama_exatamente_a_funcao_do_perfil`, `Perfil_invertido_nunca_chega_a_saida_invertida` e `AdapterTests.Cada_pedido_de_liberacao_chama_exatamente_uma_funcao` falhariam se a função errada fosse chamada. O sentido físico é A_CONFIRMAR (HIL-DIR), e o caminho de falha tem E1-05.
6. **Confirmar o giro e encerrar sem giro no prazo — PROVADO EM CI, só a lógica do laço.** `PrazoDoGiroTests.Liberacao_sem_origem_5_nem_6_volta_a_atender_depois_do_prazo_rearmando_o_leitor`, `Rele_de_20_s_nao_e_cortado_antes_de_20_s_mais_a_margem` e `Giro_que_ja_esta_na_fila_quando_o_prazo_passa_e_lido_e_nao_cortado` falhariam com prazo errado. Sem prova: os valores reais das origens 5 e 6 e a hipótese "retorno 0 com origem 0 é ausência de evento" (TopdataInnerAdapter.cs:323-329). Lacunas: E1-04 e E1-05.
8. **Operar sem o PC e coletar bilhetes — BLOQUEADO POR DECISÃO OU TERCEIRO.** Não há contingência off-line (`PingOnLine` nunca chamado, mudança automática 0): depende da decisão D8 do dono e de T24 com a Topdata. A coleta está implementada atrás da chave desligada (ConfiguracoesDaBorda.cs:103), testada só com o simulador (`ColetaDeBilhetesTests`, `QuedaNaColetaDeBilhetesTests`) e depende de T35 e HIL-BIL-01 (ver E1-09).
9. **Detectar catraca sem comunicação, socket morto e worker travado — PARCIAL.**
   - Ping que falha em Conectar leva a backoff e disjuntor: funciona.
   - Socket morto em Polling não é detectado com a chave no padrão (E1-01). O teste `WorkerTests.Catraca_que_cai_volta_a_operar_quando_a_conexao_volta` só passa porque o simulador declara a queda (E1-08).
   - Worker travado: só aparece como "notícia velha" no painel, sem recuperação automática (E1-02).

## D) Perguntas de decisão para o dono

1. **Como detectar catraca muda antes do primeiro evento?**
   - (a) Manter tudo desligado até o HIL-EVT-01.
   - (b) Ligar `catraca.reconectar_em_erro_de_recepcao` já.
   - **(c) RECOMENDADA:** acrescentar um Ping periódico em Polling, independente da chave, e fazer o HIL-EVT-01 como primeiro item de bancada.
   - Motivo: (c) detecta a queda seja qual for o código de "sem eventos" e não arrisca reconectar a cada volta, que é o medo registrado em TopdataInnerAdapter.cs:69-75.
2. **Firmware homologado:**
   - (a) Manter {14,16} fixo no código.
   - **(b) RECOMENDADA:** chave técnica com as linhas aceitas, mais HIL-CAP-01 para fixar o valor.
   - (c) Desligar a verificação.
   - Motivo: (a) pode deixar a catraca parada sem conserto em campo; (c) perde a proteção da ADR-0010.
3. **PC caído durante o evento (D8):**
   - **(a) RECOMENDADA para o primeiro evento:** fail-secure assumido por escrito (catraca trava sem PC), com alerta ao operador e procedimento de liberação manual.
   - (b) Implementar a contingência (sequência oficial, PingOnLine, lista local) antes do evento.
   - (c) Adiar a decisão.
   - Motivo: (b) depende de T24 e T13 com a Topdata, que não têm prazo controlável; (c) mantém um comportamento acidental, o que o docs/34:41 pede para evitar.
