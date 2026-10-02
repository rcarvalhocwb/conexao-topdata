# 36 — Inovação sobre a catraca: usar 100% do que ela oferece

**Pedido do dono do produto (01/10/2026):** "Não existia inteligência artificial quando a DLL da
catraca foi criada; as funções eram limitadas. Hoje esta equipe pode usar 100% do que a catraca
oferece, com estudo e estrutura correta, e fazer algo que nem a Topdata conseguiu ainda. Tendo êxito,
vamos demonstrar a atualização para eles."

Estudo feito por três papéis, com relatório completo em [`36-anexos/`](36-anexos/):

| Papel | Relatório |
|---|---|
| Engenheiro de integração Topdata | [01 — 100% da catraca, relés e sentido do giro](36-anexos/01-engenheiro-topdata.md) |
| Arquiteto da camada inteligente | [02 — dados, métodos, arquitetura e etapas](36-anexos/02-camada-inteligente.md) |
| Produto, design e QA | [03 — telas, reclamações públicas, demonstração e plano de prova](36-anexos/03-produto-demo-e-prova.md) |

## 1. O princípio

A inovação acontece **acima da DLL**: o XAcess **compõe** as funções que a catraca já tem e
**interpreta** o fluxo de eventos com mais inteligência. A DLL e o firmware não ganham função nova e
nada é inventado — toda afirmação sobre a catraca cita a fonte, e o que é ambíguo vira pergunta à
Topdata e ensaio de bancada. **Nenhuma capacidade nova decide liberar**: elas explicam, alertam,
sugerem e medem. A decisão do giro continua local, determinística e abaixo de 150 ms, e falha de
base continua negando.

## 2. O que o estudo encontrou

- **Cobertura real (01 §2):** das 130 funções não biométricas com assinatura, 22 rodam na operação
  (17%) e 40 estão no código (31%). Das 48 que servem à Linha 4, 79% estão no código. Display (2/13),
  listas (0/16), sensores (0/5) e relés (2/9) quase não são usados.
- **Sinais desperdiçados (01 §4, 02 §2):** tudo que não é leitura vira uma linha de texto. As tabelas
  de evento bruto, decisão, passagem e transição existem (migração 001) e a operação não grava. As
  métricas de `MetricasDoEdge` foram desenhadas e nunca ligadas.
- **Defeito dormente (01, C1):** o prazo de 8 s do estado "monitorando o giro"
  (`DeviceStateMachine.cs:48`) **nunca é aplicado** — `CurrentTimeout` não tem consumidor. Se o
  evento de tempo esgotado não vier, a pista para de atender sem erro. É a primeira correção.
- **Giro sem liberação** (origem 6 com a catraca ociosa) é descartado em silêncio — e a própria
  Topdata diz que o equipamento conta giro sem cartão.
- **Relés (01 §5):** o relé 1 é o giro. O **relé 2**, em catraca sem urna, é uma saída de 12 Vcc /
  1 A que a página de suporte da Topdata descreve como livre "para lâmpada, campainha"; `AcionarRele2`
  já está no código e ninguém chama. Usos: comando no painel, sinaleiro que apaga se o programa morrer,
  aviso de giro sem liberação, revista sorteada on-line, pulso para câmera, contador externo. **Risco:**
  a origem 5 não diz qual relé expirou — o relé 2 só pulsa com a catraca ociosa e depois do ensaio
  NOVO-HIL-REL-06.
- **Sentido do giro (01 §6, decisão D9):** "entrada/saída" é nomenclatura do sistema. O evento não traz
  o sentido físico; o giro é classificado pela **função de liberação usada × perfil**, e fica
  "desconhecido" sem liberação ou em dois sentidos. Implementado no **Mapa de giro** (migração 017),
  editável clicando no giro do gêmeo.
- **Reclamações públicas (03 §5):** 12 recorrentes — perda de comunicação, "lê mas não libera",
  catraca que não trava, sentido errado, memória cheia, relógio errado, instalação difícil, suporte
  lento. O XAcess já resolve 5 no código; 2 dependem de bancada; mecânica e suporte não são software.
- **Fatos públicos novos:** memória "Para/Segue" (não só circular), sentido de entrada no WebServer,
  liberação de qualquer entrada em off-line (relato de integrador), contador de giros por sentido no
  menu Master. Viraram as perguntas T36–T39; as do engenheiro seguem de T40 em diante.

## 3. Capacidades (nomes unificados)

| Id | Capacidade | Base | Classificação |
|---|---|---|---|
| C1 | Prazo real do giro (correção) | `DeviceStateMachine` | AGORA |
| IN-06 | **Por que negou** — explicação humana de cada negação, e motivo agregado | tentativas + motivo | AGORA |
| IN-01 | **Saúde por catraca** — o pior sinal, com a conta que o produziu (giro, relógio, configuração, comunicação, leitura, laço) | situação, tentativas, coletor | AGORA |
| IN-02 | **Alertas** com ciclo de vida, "ciente" e correlação entre pistas (giro sem liberação, liberou e não girou em série, silêncio em horário de pico, troca de firmware) | coletor + analisador | AGORA (premissas físicas: bancada) |
| IN-03 | **Reuso de ingresso** entre catracas, sem código em claro (HMAC da B.1) | tentativas | AGORA |
| IN-04 | **Ritmo e tempo para escoar** (estimativa, sempre rotulada) e recomendação de abrir/redistribuir pistas | tentativas + ingressos não usados | AGORA |
| IN-05 | Fluxo por sentido lógico e lotação estimada | Mapa de giro (017) | AGORA |
| IN-07 | **Sugestão de parametrização** (ex.: tempo do relé × janela real medida); só preenche a tela da A.6 | coletor | AGORA / bancada |
| C8/IN-10 | **Check-up** antes de abrir os portões | funções documentadas | AGORA |
| C6 | Mensagem no display conforme o motivo da negação, sem dado pessoal | EI-056 + motivo | AGORA (chave) |
| IN-08/09 | Replay no gêmeo e relatório pós-evento com achados | R1–R8 (fase 6), Etapa C | depois |
| — | Sinais físicos pelo relé 2 | NOVO-HIL-REL-04/06 | bancada |
| IN-11 | Assistente com LLM | — | PROPOSTA FUTURA (agora: perguntas prontas determinísticas) |

## 4. Arquitetura (02 §3)

- **No worker**, só um *coletor mínimo*: contadores O(1) e um anel limitado, descarregado **entre
  voltas**, como a publicação da situação. Nada no passo da decisão.
- **No serviço**, o *Analisador*: `BackgroundService` de prioridade baixa, com orçamento, que lê a base
  só para leitura e calcula em funções puras de um projeto novo `Access.Inteligencia` (só BCL).
- **Armazenamento separado (`telemetria.db`):** o SQLite tem um escritor por arquivo; telemetria
  travada perde telemetria, nunca atrasa um giro. Sem código de ingresso nem nome.
- **Métodos explicáveis antes de ML:** janelas de 1 min, histogramas de baldes fixos, mediana/MAD,
  EWMA, comparação com as vizinhas na mesma janela, Poisson para silêncio, Wilson para taxas,
  Theil–Sen e CUSUM para deriva. Todo alerta carrega a conta que o disparou.
- **Desligada por padrão** até passar `NOVO-LOAD-IA-01` (decisão p95 < 150 ms, ≤ 10 ms a mais),
  `NOVO-CHAOS-IA-01` (camada travada não muda nenhuma chamada à catraca) e `NOVO-SOAK-IA-24H`
  (zero falso alarme em 24 h simuladas).

## 5. Etapa I — ordem de implementação (02 §9)

| Etapa | Entrega | Depende de |
|---|---|---|
| I.1b | Prazo real do giro (C1) | — |
| I.0 | Fundação: `Access.Inteligencia`, Analisador, `telemetria.db`, chaves, saúde do Analisador no Diagnóstico | — |
| I.1 | Coletor mínimo no worker; `MetricasDoEdge` ligado | I.0 |
| I.2 | Por que negou | I.0 |
| I.3 | Saúde v1 (giro, relógio, configuração) | I.0 |
| I.4 | Alertas v1 | I.3 |
| I.5 | Saúde e alertas v2 (comunicação, leitura, laço, giro sem liberação) | I.1, I.4 |
| I.6 | Ritmo e portões | I.0 |
| I.7 | Reuso | I.4 |
| I.8 | Sentido (sobre o Mapa de giro) | 017, I.4 |
| I.9 | Sugestões de parametrização | I.1, I.3 |
| I.10 | Pós-evento e replay | I.5, I.6, R1–R8 |
| I.11 | Calibração e "liga por padrão" | I.1–I.10 |

**Situação (docs/34 §11):** **I.0 concluída no código** — `Access.Inteligencia`, `AnalisadorDaOperacao`
(desligado por padrão, chave `inteligencia.ligada`), `acesso.db` só para leitura, `telemetria.db` com o
migrador `T001` e a saúde do Analisador no Diagnóstico; provas `NOVO-ARQ-IA-01/02/03`,
`NOVO-CHAOS-IA-01` e `NOVO-CTR-IA-01`.

**Caminho crítico para a demonstração à Topdata:** I.1b → I.0 → I.2 → I.3 → I.4 → I.6, com os ganchos
do simulador (03 §4.3: chegadas de Poisson, atraso do giro, sinais por catraca, rampa de relógio,
cenário só em simulação, semente fixa no CI) em paralelo.

## 6. Demonstração para a Topdata (03 §4)

Parte A, 19,5 min no simulador, em nove cenas — incluindo "o que seus clientes reclamam e como
resolvemos" e o Mapa de giro —, cada uma com o dado simulado, a frase e como a Topdata confere.
Parte B, na bancada, reaproveitando o docs/21. Material: modo "Demonstração Topdata" (só em
simulação), vídeo gravado pelo CI, documento executivo de 2 páginas e folha de conferência para o
engenheiro deles. **O que pedimos:** respostas a T2, T4, T13/T19, T14, T17, T20/T34/T35, T22, T24,
T25, T27, T33 e T36 em diante. **O que oferecemos:** o relatório de bancada, o simulador como modelo
que eles podem corrigir, cenários de reclamação para o suporte e o formato do check-up — sem prometer
certificação nem solução off-line.
