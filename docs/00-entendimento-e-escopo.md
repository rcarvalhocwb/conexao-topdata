# 00 — Entendimento e escopo

## 1. Resumo do entendimento

Deve ser construída uma **plataforma desktop profissional local-first** para operar
catracas, controladores e coletores Topdata, desde instalações pequenas até eventos com
**50.000 pessoas**, muitas catracas, picos intensos e **internet eventualmente indisponível**.

O que isso significa na prática, em ordem de importância:

1. **A decisão de liberar uma catraca é local e nunca depende da nuvem.** A nuvem é um
   destino de sincronização assíncrona, jamais um passo do caminho crítico.
2. **A `EasyInner.dll` é o ponto frágil da arquitetura** — nativa, Windows, x86, síncrona,
   sequencial, com buffers internos possivelmente globais e limite prático de ~30
   equipamentos por instância. Ela precisa ser isolada em processos sacrificáveis, nunca
   carregada pela interface, nunca compartilhada entre grupos grandes de equipamentos.
3. **Autorizar não é passar.** Um comando elétrico enviado com sucesso não prova giro
   físico. O modelo de dados e os relatórios tratam esses dois fatos como eventos
   distintos, e o consumo definitivo de um ingresso ocorre no segundo, não no primeiro.
4. **Capacidade é descoberta, não presumida.** Linha, placa, firmware, leitor e opcionais
   mudam o que é possível. Nada é habilitado antes de identificar e registrar a capacidade
   do equipamento concreto.
5. **O coletor/urna precisa recolher o cartão para liberar a ENTRADA** — uso invertido em
   relação ao típico (saída). O fluxo só libera o giro após a confirmação física do
   recolhimento, e o sentido lógico "entrada" é associado ao acionamento físico correto
   durante o comissionamento, nunca fixado em código.
6. **Inner e Easy/facial não são a mesma integração.** EasyInner/TCP para a linha Inner;
   SDK facial próprio (WebSocket/JSON) para a linha facial. Um equipamento pode exigir os
   dois. Eles nunca se misturam atrás de uma mesma abstração como se fossem uma API só.

## 2. A restrição mais consequente descoberta na Fase 0

A especificação oficial confirma que o **Coletor Urna 4 suporta lista de até 15.000 usuários**
(`FONTE_PRIMARIA`, especificação oficial). Um evento de **50.000 pessoas** não cabe nessa lista.

Isso derruba a hipótese ingênua de "sincronizar todo mundo para dentro das catracas e
deixar rodar offline". A autonomia real do sistema precisa estar **na borda (o PC do
Edge Gateway), não no equipamento**. A lista local do equipamento passa a ser o
*terceiro* nível de degradação, com um subconjunto priorizado de credenciais — não a
estratégia principal.

Isso é tratado formalmente em [ADR-0017](ADR/ADR-0017-niveis-de-degradacao.md) e na
seção "Níveis de degradação" de [`03-arquitetura.md`](03-arquitetura.md).

## 3. Escopo

### Dentro do escopo

- Aplicativo desktop de operação (pt-BR, acessível, modo guiado + modo técnico).
- Edge Gateway x86 supervisionado, com particionamento de equipamentos por worker.
- Núcleo local de decisão de acesso com latência-alvo medida (p50/p95/p99).
- Banco local durável (SQLite/WAL) com outbox/inbox transacional.
- Hub de sincronização com conectores versionados (REST, filas, bancos via backend seguro,
  CSV/JSON de contingência).
- Máquina de estados por equipamento, simulável sem hardware.
- Workflow `CollectCardThenEnter` (coletor/urna liberando entrada).
- Cadastro funcional completo, RBAC, aprovação em duas pessoas, auditoria imutável.
- Observabilidade (logs JSON, métricas, traces, pacote de diagnóstico sanitizado).
- Simulador de catraca e laboratório de bancada.
- Biometria digital e módulo facial **apenas nos modelos comprovados em bancada**.

### Fora do escopo (nesta contratação, salvo repactuação)

- Aplicativo móvel para operador.
- Portal web multiempresa hospedado (a nuvem aqui é destino de sincronização de terceiros,
  não produto nosso).
- Venda de ingressos, bilheteria, meios de pagamento.
- Reconhecimento facial proprietário (usaremos o SDK do fabricante, não algoritmo próprio).
- Alteração de fiação, firmware ou sentido mecânico por software.
- Integração com equipamentos de outros fabricantes.

### Explicitamente recusado como promessa

- "Conecta com qualquer banco de dados automaticamente." Não. Conectores versionados,
  com mapeamento, validação e teste — e nunca um banco em nuvem exposto diretamente à
  rede das catracas.
- "Suporta todos os modelos Topdata." Não. Suporta o que passou no ensaio de bancada, e a
  matriz de compatibilidade diz exatamente quais.
- "Confirma a passagem física." Só onde o modelo emite o evento de giro. Onde não emite, o
  produto reporta "autorizado sem confirmação" e diz isso ao operador com todas as letras.

## 4. Restrições inegociáveis que orientam toda a arquitetura

| # | Restrição | Origem | Consequência arquitetural |
|---|---|---|---|
| R1 | `EasyInner.dll` é nativa, Windows, **x86** | `FONTE_PRIMARIA` | Processo worker compilado explicitamente `win-x86`, separado da UI x64 |
| R2 | Exige DLLs registradas pelo instalador e .NET Framework 3.5+ | `FONTE_PRIMARIA` | Verificador de pré-requisitos no instalador e no health check do worker |
| R3 | ~30 equipamentos **por instância da DLL** = por thread; cada processo em **porta TCP própria** | `FONTE_PRIMARIA` | Particionamento; teto default **20**, hard cap **25** até teste de carga ([ADR-0005](ADR/ADR-0005-particionamento-por-worker.md)) |
| R4 | `ReceberDadosOnLine` é bloqueante | `FONTE_PRIMARIA` | Nunca na thread de UI; thread dedicada por worker com watchdog |
| R5 | **A DLL inteira é não thread-safe**; `EnviarConfiguracoes` limpa o buffer e envia até os **defaults** para o que não foi setado | `FONTE_PRIMARIA` | Thread única por worker ([ADR-0006](ADR/ADR-0006-serializacao-montar-enviar.md)) e configuração **sempre completa** ([ADR-0020](ADR/ADR-0020-configuracao-sempre-completa.md)) |
| R6 | A catraca inicia a conexão TCP (porta padrão **3570**) | `FONTE_PRIMARIA` | O Edge é servidor; planejamento de IP/porta/firewall/VLAN é parte do produto |
| R7 | `sendlog` facial confirma autorização, **não giro** — a Topdata afirma que a linha Easy não emite confirmação de giro | `FONTE_PRIMARIA` | Separação Autorização × Passagem ([ADR-0007](ADR/ADR-0007-autorizacao-versus-passagem.md)) |
| R8 | Off-line = lista branca/negra + 100 tabelas de horário | `FONTE_PRIMARIA` | Regras complexas ficam na borda; degradação explícita e visível |
| R9 | Capacidades variam por linha/placa/firmware (ex.: LEDs só na Linha 3) | `FONTE_PRIMARIA` | Descoberta de capacidade obrigatória antes de habilitar recurso ([ADR-0010](ADR/ADR-0010-capability-discovery.md)) |
| R10 | Nuvem fora do caminho crítico | `DECISAO_ARQUITETURAL` | Critério de aceite verificável por teste de caos (corte de WAN) |

## 5. Critérios de aceite (rastreáveis)

Cada critério inegociável do briefing recebe um identificador e um teste que o comprova.
A entrega só é declarada pronta com o teste executado e o resultado anexado.

| ID | Critério | Teste que comprova |
|---|---|---|
| CA-01 | Funciona sem internet durante todo o evento | `CHAOS-WAN-01`: WAN cortada por 8 h sob carga |
| CA-02 | Reinicia sem perder, duplicar ou consumir acesso indevidamente | `CHAOS-KILL-01`: `kill -9` em worker/serviço sob rajada |
| CA-03 | Catraca defeituosa não paralisa as demais | `CHAOS-DEV-01`: dispositivo que trava em `ReceberDadosOnLine` |
| CA-04 | Sem chamada de nuvem no caminho crítico | `ARCH-01`: teste de arquitetura (proibição de referência) + captura de rede |
| CA-05 | Urna só libera após confirmação de recolhimento | `SIM-URNA-01..12` |
| CA-06 | Passagem física ≠ autorização | `SIM-GIRO-01..06` + verificação de esquema |
| CA-07 | Capacidade/modelo/firmware validados antes de configurar | `HIL-CAP-01` |
| CA-08 | Rollback e diff de configuração | `INT-CFG-01..04` |
| CA-09 | Retornos e eventos desconhecidos visíveis e auditáveis | `SIM-UNK-01` |
| CA-10 | Logs sem dado sensível | `SEC-LOG-01` (varredura automatizada em CI) |
| CA-11 | Benchmark, soak e caos dentro das metas | `LOAD-*`, `SOAK-72H`, `CHAOS-*` |
| CA-12 | Instalação limpa, atualização, backup e recuperação testados | `REL-01..05` |
| CA-13 | Operador comum opera sem conhecer nomes de funções do SDK | `UX-01`: teste com 5 usuários reais, tarefas cronometradas |

Detalhamento dos testes em [`06-plano-de-testes-e-dimensionamento.md`](06-plano-de-testes-e-dimensionamento.md).
