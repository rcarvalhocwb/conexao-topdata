# Engenharia reversa do middleware antigo: conexão, mensagens e passagens

Análise do middleware que operou a catraca em produção em 2025 (zip `Middleware_topdata_supabase-main`)
comparada com o sistema atual. Objetivo: mapear a ordem exata de conexão, configuração, leitura, urna e
giro, listar as mensagens que o operador via no display, e separar o que é lacuna confirmada no código
do que depende de bancada ou de decisão.

Fontes (dentro do zip, `src/Middleware.App/`):

- `Inner/TopdataEasyInnerDeviceClient.cs` (1.372 linhas): máquina de estados do dispositivo.
- `Inner/EasyInnerManager.cs`: abertura da porta.
- `Inner/EasyInnerWrapper.cs`: enums (função de acionamento, operação de leitor, padrão de cartão).
- `Services/DeviceController.cs`: ligação entre a passagem e a nuvem.
- `docs/SOLUCAO_LEITURA_CONTINUA_CARTOES.md`, `docs/CORRECAO_DUPLICACAO_EVENTOS.md`,
  `docs/CORRECAO_FRAUDE_FISHING_CARD.md`, `docs/LOGGING_NAO_DEPOSITOU.md`.

O zip é igual ao código analisado antes (`src/` idêntico). Quando a documentação do zip e o código
divergem, vale o código: é o que rodou.

## 1. Ordem de conexão

1. `DefinirTipoConexao(TCP_IP_Porta_Fixa)` → `FecharPortaComunicacao` → `AbrirPortaComunicacao(porta)`.
   Mesma ordem que o nosso `TopdataInnerAdapter.AbrirPorta` (corrigido em `bd09275`).
2. `StateConnectingAsync` e `StateTestConnectionAsync`: teste de conexão com `PingOnLine` (o mesmo que
   o nosso `Conectar`).
3. Enquanto está em `Polling`, `PingOnline` a cada 3 s (keep-alive). Falha: `Reconnecting`.
4. `StateSendConfigAsync` (linhas 276 a 345), nesta ordem:

   | Passo | Chamada | Valor do antigo | Nosso (`PadroesDeFabrica.TopFit4`) |
   |---|---|---|---|
   | 1 | `DefinirPadraoCartao` | 1 (Livre) | 1, enviado em `TopdataInnerAdapter.cs:515` |
   | 2 | `ConfigurarInnerOnLine` | — | Sequência oficial, `SequenciaOficialDeConexao` |
   | 3 | `ConfigurarTipoLeitor` | **2** (Abatrack2) | **8** (ver §4, item 1) |
   | 4 | `ConfigurarLeitor1` | 1 (só entrada) | só entrada |
   | 5 | `ConfigurarLeitor2` | 1 (só entrada, fenda da urna) | só entrada |
   | 6 | `ConfigurarAcionamento1` | **1** (entrada ou saída), **15 s** | **2** (registro entrada), **5 s** (ver §4, itens 2 e 3) |
   | 7 | `ConfigurarAcionamento2` | 3 (registro saída), 15 s | 3, 15 s (`FuncaoPadraoDoReleDaUrna`, `TempoPadraoDaUrna`) |
   | 8 | `EnviarConfiguracoes` | aplica tudo | aplica tudo |

5. Depois da configuração, `ConfigurarEntradasOnline` com `EnviarFormasEntradasOnLine(0, 0, 6, 0, 0)`:
   forma de entrada 6 (leitor 1 e leitor 2, sem teclado). O nosso também usa forma 6.
6. Mensagem padrão "Modo Online" e entrada em `Polling`.

## 2. Leitura, urna e giro: sequência do antigo

Evento por origem, como o antigo trata (`HandleEvent`, linhas 480 a 560, e máquina de estados):

**Leitor 1 (origem 2, frente da catraca, sem urna):**
1. `CardRead` vai para o `DeviceController`, que autoriza.
2. Liberado: `LiberarCatracaEntrada` direto e estado `MonitoringGiro`. A mensagem "ACESSO LIBERADO" aparece
   só no caminho da urna (origem 7); no caminho do leitor 1 não localizei mensagem de liberação.
3. Origem 6 (giro confirmado): `DesabilitarRele2` (no caminho do leitor 1 o relé 2 não é usado, mas o
   antigo chama mesmo assim), `ConfigurarEntradasOnline`, mensagem "Modo Online" e volta a `Polling`.
4. Origem 5 (o giro não aconteceu no prazo): mensagem "Modo Online", rearme e volta a `Polling`.

**Leitor 2 (origem 3, fenda da urna):**
1. Se já existe cartão pendente, o novo é ignorado (log "Cartão já está sendo processado").
2. Guarda o cartão pendente, `DesabilitarRele2`, estado `WaitingAuthorization` e `CardRead`.
3. `GrantAccessAsync`: `AcionarRele2` (abre a guia) → estado `ValidatingUrnaCheia`.
4. `ValidatingUrnaCheia`: uma leitura de eventos. Origem 20 vira "urna cheia". Sem evento, segue.
5. `SendingMsgUrna`: "DEPOSITE O CARTAO" (flag 0, sem data e hora).
6. `MonitoringUrna` com `URNA_TIMEOUT_MS = 7000`:
   - Origem 7 (cartão recolhido): "ACESSO LIBERADO", `DesabilitarRele2`, `LiberarCatracaEntrada`,
     estado `MonitoringGiro`.
   - Origem 5 ou 20: negação ("NAO DEPOSITOU" ou "URNA CHEIA / ESVAZIAR URNA"), `DesabilitarRele2`,
     rearme.
   - Timeout sem origem 7: `DesabilitarRele2`, "NAO DEPOSITOU", log de auditoria, rearme.
   - Origem 7 que chega antes de `MonitoringUrna` fica registrada (latch) e é consumida ao entrar.
7. `MonitoringGiro`: origem 6 confirma e rearma; origem 5 ou timeout rearma com "Modo Online".

**Rearme:** o antigo chama `ConfigurarEntradasOnline` depois de giro, timeout, negação e urna cheia.
Esse é o conserto de `SOLUCAO_LEITURA_CONTINUA_CARTOES.md`: sem o rearme, o segundo cartão não gerava
evento nenhum, porque o Inner ficava sem forma de entrada habilitada.

**Reconexão:** 10 tentativas com 5 s entre elas. Esgotadas, o antigo sai do modo on-line e fica em
`Error` até reiniciar o processo.

## 3. Mensagens do display: catálogo

| Quando | Texto do antigo | Nosso texto | Situação |
|---|---|---|---|
| Parado, aguardando cartão | "Modo Online" (com data e hora, flag 1) | "Aproxime o ingresso" | Diferente. Decisão de produto |
| Pedido de depósito na urna | " DEPOSITE O       CARTAO" (espaçado, flag 0) | "Deposite o cartao" | Mesmo sentido. Redação de produto |
| Liberado na urna (origem 7) | "ACESSO LIBERADO" + tipo | Não localizado como texto de display no worker (só o log "giro liberado") | **Verificar em bancada** |
| Negado | " ACESSO NEGADO!" | "Acesso nao autorizado" | Diferente só na redação |
| Urna não depositada | "NAO DEPOSITOU" | "Cartao nao caiu. Tente de novo" | Redação |
| Urna cheia | "URNA CHEIA" + "ESVAZIAR URNA" | "Urna cheia. Procure a equipe" | Redação. Sem "esvaziar" no nosso texto |

Os textos são ASCII (o display só aceita ASCII, 32 caracteres). A troca de redação não muda o fluxo.

## 4. Diferenças que mudam o comportamento

Classificação: **Confirmado** = verificado no código e sem outra explicação. **Bancada** = depende de
medir na catraca. **Decisão** = depende de escolha do dono.

| # | Tema | Antigo | Nosso | Situação | Ação |
|---|---|---|---|---|---|
| 1 | Tipo de leitor | 2 (Abatrack2), usado em produção com cartões reais | 8 (padrão da bancada, marcado "A_CONFIRMAR" em `docs/34`) | **Bancada, prioridade alta.** O tipo muda como o número do cartão é lido. Se o número lido não bater com o cadastro, ninguém passa | Bancada U-L: ler o mesmo cartão com tipo 2 e tipo 8 e comparar o número. Recomendação: usar 2 se o número bater |
| 2 | Função do relé 1 | 1 (registro entrada ou saída) | 2 (registro entrada) | **Decisão.** Com o leitor 1 só de entrada, o efeito prático é o mesmo. Nossa liberação do giro é o comando `LiberarCatracaEntrada`, não a função do relé | Recomendação: 1, para igualar ao que rodou em produção |
| 3 | Tempo do relé 1 | 15 s fixo | 5 s padrão, definido pelo operador, mais margem de 3 s | **Decisão** (já registrada em `docs/63` D-A). Sem a margem, o prazo de giro é o tempo que o operador definiu | Ver item 7 |
| 4 | Ordem: guia e mensagem | Guia (`AcionarRele2`) antes da mensagem "DEPOSITE" | Mensagem antes da guia | **Decisão** (D-B). Sem evidência de que a ordem nossa cause problema | Manter ou seguir o antigo |
| 5 | Urna cheia antes de abrir | Uma leitura de eventos depois da guia aberta | Origem 20 tratada durante a espera, depois da guia aberta | **Equivalente.** Nenhum dos dois bloqueia a guia com base no que já aconteceu antes | Ver item 6 |
| 6 | Flag de urna cheia | Não bloqueia a abertura | `d.UrnaCheia` é marcada, mas nunca bloqueia a abertura | **Decisão.** Bloquear exige um jeito de limpar a marca depois de esvaziar a urna. Hoje ela só é limpa pela origem 7, que não acontece com a urna cheia | Decidir: operador marca "urna esvaziada" no painel, ou a próxima tentativa tenta a guia |
| 7 | Prazo do giro | Relé 1 (15 s) + nada | Relé 1 + 3 s (`MargemDoGiro`) | **Decisão** (`docs/63` D-A e `docs/64` §9.3). A catraca cancela sozinha no tempo configurado e envia a origem 5, então a margem é só reforço | Bancada U-M: cancelar sem margem e ver se a origem 5 é confiável |
| 8 | Prazo de depósito | 7 s | 7 s (`EsperaPeloDeposito`) | **Confirmado** | Nenhuma |
| 9 | Fraude com cartão de pesca (cartão puxado de volta) | O doc diz: 1 s de atraso e nova leitura depois da origem 7. **O código enviado não tem isso**: `grep` de FRAUDE, anti-fraude e atraso de 1 s em `src/` retorna zero. O timeout no código é 7 s; o doc diz 10 s | Também não tem | **Lacuna real** (o antigo também não tinha). Decisão: custo de 1 s por passagem contra o risco de fraude | Bancada U-F: puxar o cartão logo depois da origem 7. Se o cartão for relido, negar |
| 10 | Duplicação de evento | Origem 3 e origem 7 geravam duas leituras (corrigido com "decide só na origem 3") | Origem 7 só libera, nunca decide | **Confirmado** (sem duplicação) | Nenhuma |
| 11 | Origem 7 chegando cedo | Latch, porque consumia eventos fora do caminho | Não precisa de latch: `MonitoraUrna` é marcada na mesma chamada que aciona o relé, e o evento fica na fila da DLL | **Confirmado** | Nenhuma |
| 12 | Origem 7 chegando tarde (depois do prazo) | Só é tratada dentro de `MonitoringUrna` (e no latch); fora dela não libera | Vira `SinalDaCatraca` em `Polling`: registrada, não libera | **Confirmado** (nos dois) | Nenhuma |
| 13 | Rearme depois de cada ciclo | `ConfigurarEntradasOnline` depois de giro, timeout, negação e urna cheia | Igual, pelas transições `MonitoraGiroCatraca`, `EnviarMsgAcessoNegado` e `Reconectar` | **Confirmado** | Nenhuma |
| 14 | Rearme depois de sinal em `Polling` | Não rearma (`HandleEvent` não reconfigura) | Não rearma, por decisão F4: `Polling --SinalDaCatraca--> Polling` (`DeviceStateMachine.cs:290`), coberto por `SinalDaCatracaTests` (exige que o contador de rearme não mude) | **Já decidido, não é lacuna.** O manual manda reenviar `EnviarFormasEntradasOnLine` depois de cada `ReceberDadosOnLine`, mas o rearme por ciclo (item 13) já cobre o defeito que o antigo corrigiu. Reverter contraria F4 e o teste | Manter. Só reverter com decisão sua. Reverter também muda a mensagem do display de "Aproxime" para "Modo Online" a cada sinal |
| 15 | Relé 2 depois do ciclo (`DesabilitarRele2`) | Chamado em todas as saídas da urna | Removido (D5, o manual proíbe) | **Bancada** (U8): o próximo cartão pode esperar até o fim da guia (15 s) | Medir |
| 16 | Leitor 1 durante ciclo da urna | Não localizado no antigo (dependeria de `GrantAccessAsync` em `MonitoringUrna`) | Registrado, sem resposta: a pessoa não recebe nada no display | **Lacuna de experiência.** Com fila grande, quem passa pela frente fica sem retorno | Decisão: exibir "aguarde" e reler depois |
| 17 | Reconexão | 10 tentativas, depois `Error` permanente | Keep-alive a cada 3 s, reconecta após 6 falhas, disjuntor com backoff. Nunca desiste de vez | **Confirmado** (nosso é mais robusto) | Nenhuma. Ver item 18 |
| 18 | Disjuntor aberto | Não se aplica | O worker para de ler essa catraca enquanto o disjuntor está aberto (`DevicePump.cs:473`), e o leitor não recebe resposta | **Decisão.** Com fila grande, o disjuntor deixa a catraca muda para as pessoas | Decidir o tempo de abertura e o aviso ao operador |
| 19 | Registro da passagem | Enviado à nuvem depois de `TurnstileReleased(Success)`, que vem antes do giro | Registrado na origem 7, antes do giro | **Decisão.** O manual diz que o bilhete só grava depois do giro (`docs/64` §5). Os dois registram antes do giro | Decidir: registrar em "liberado" e confirmar no giro |

## 5. O que o antigo tem e nós também

- Abertura e teste de conexão, na mesma ordem.
- Configuração com `EnviarConfiguracoes` e `DefinirPadraoCartao(Livre)`.
- Urna com guia (relé 2) e espera da origem 7 por 7 s.
- Origem 5 e origem 20 com negação e devolução do uso.
- Rearme depois de cada ciclo.

## 6. Próximos passos

1. **Antes de qualquer mudança de hardware:** bancada U-L (tipo de leitor 2 contra 8). Sem isso, nenhuma
   outra medida adianta, porque o número lido pode não bater com o cadastro.
2. **Decisões que o dono precisa tomar** (itens 2, 4, 6, 7, 9, 14, 16, 18, 19). Cada uma tem uma
   recomendação em §4.
3. **Bancada:** U-M, U-F, U8 e U-A, que já estavam em `docs/63` §6 e `docs/64` §11.
4. Depois das decisões, atualizar `MontadorDaConfiguracao` e os testes, e rodar as quatro suítes.

Nenhuma mudança de código foi feita nesta análise. Os itens acima são a lista para decidir antes de
mexer: tocam em parâmetro de hardware, em tempo de passagem e em texto do display, e nenhum deles está
aprovado sem medição na catraca.
