# 07 — Backlog por fases

Regra que vale para todas: **"compilou" não é critério de pronto.** Cada fase fecha com
testes executados, resultados anexados, limitações conhecidas escritas e o próximo ensaio
de hardware identificado.

---

## Fase 0 — Descoberta · *em andamento*

| Item | Situação |
|---|---|
| Inventário de hardware/modelo/firmware | **Bloqueado em B2** — depende do cliente |
| Perguntas em aberto | Concluído — [`01`](01-perguntas-criticas.md) |
| Matriz de capacidades e funções | **Arcabouço pronto, conteúdo bloqueado em B1** |
| ADRs | Concluído — 18 registros |
| Análise de riscos | Concluído — [`08`](08-riscos-e-validacoes-topdata.md) |
| Plano de bancada | Concluído — [`09`](09-plano-de-bancada.md) |

**Pronto quando:** B1 e B2 respondidos, matriz preenchida a partir do wrapper real, e
ensaio de bancada agendado para ao menos um modelo.

> A Fase 0 **não fecha** sem os SDKs. O que existe hoje é o arcabouço; o conteúdo
> depende da fonte primária.

---

## Fase 1 — Fundação executável

1. Solução .NET organizada conforme [`03`](03-arquitetura.md), com testes de arquitetura
   que proíbem as dependências erradas.
2. Contrato IPC (`.proto`) + gRPC sobre named pipes, com ACL e token.
3. `Edge.Worker.X86` compilando `win-x86`, carregando **adapter mock**, com watchdog,
   backoff com jitter e circuit breaker.
4. `Edge.Supervisor` como Windows Service, supervisionando grupos.
5. SQLite + migrações + outbox/inbox transacional.
6. Simulador com todos os casos da seção 4 de [`06`](06-plano-de-testes-e-dimensionamento.md).
7. Máquina de estados completa, dirigida por tabela, testada sem hardware.
8. `Shared.Observability` com redação no serializador + `SEC-LOG-01` em CI.
9. Matriz de compatibilidade como **teste de contrato** (chamada sem registro quebra o build).
10. Instalador de desenvolvimento.
11. Casca do Desktop.App: conecta, mostra estado, não trava sem internet.

**Pronto quando:** CI verde em Linux e Windows; simulador roda 10 equipamentos virtuais
por 1 h sem vazamento; `kill -9` no worker não perde evento; teste de arquitetura falha
propositalmente ao introduzir a referência proibida (verificação do verificador).

---

## Fase 2 — Operação Inner on-line/off-line

1. Adapter **real** sobre a DLL, com documentação interna por chamada (fonte, assinatura,
   modelos testados, tratamento de retorno).
2. Descoberta de capacidade e checagem de compatibilidade.
3. Configuração com diff, aprovação, envio atômico serializado, rollback e detecção de drift.
4. Leitura → decisão → liberação → confirmação de giro.
5. Fallback off-line, lista priorizada (T2), bilhetes e reconciliação sem duplicar.
6. Dashboard operacional e monitor ao vivo.
7. Assistente "Adicionar equipamento".

**Pronto quando:** `HIL-*` executados em pelo menos um modelo real com relatório assinado;
`CHAOS-DEV-01` comprova isolamento entre grupos; reconciliação de bilhetes sem duplicação
em `CHAOS-REC-01`; latência p95 medida e registrada.

---

## Fase 3 — Urna/coletores e evento

1. Workflow `CollectCardThenEnter` completo ([`04`](04-workflow-collect-card-then-enter.md)).
2. Assistente de comissionamento com teste dos dois sentidos.
3. Estoque e ciclo de vida do cartão, com cadeia de custódia.
4. Urna cheia, exceções e Central de Incidentes.
5. Calculadora de capacidade com alerta de déficit.
6. Testes de pico e de rajada.

**Pronto quando:** `SIM-URNA-01..12` verdes; ensaio real de recolhimento→entrada com
relatório; reconciliação de urna fecha; `LOAD-*` atinge as metas ou o desvio está
documentado com causa.

---

## Fase 4 — Nuvem e extensibilidade

1. SDK de conectores com contrato versionado.
2. Conectores REST, PostgreSQL, SQL Server, MySQL, Supabase (via API/backend seguro).
3. Prioridade de sincronização, DLQ, reprocessamento manual, cursores.
4. Observabilidade completa e exportação OpenTelemetry.
5. Importação/exportação CSV/JSON de contingência.

**Pronto quando:** `CHAOS-WAN-01` de 8 h sem impacto na operação; `LOAD-SYNC-01` drena
backlog de 24 h sem degradar; teste de contrato quebra ao alterar schema externo.

---

## Fase 5 — Biometria/facial, hardening e entrega

1. Módulos biométrico e facial **apenas nos modelos homologados**.
2. LGPD: consentimento, base legal, retenção, expurgo, relatório ao titular.
3. Segurança: MFA, rotação de segredos, atualização assinada, SBOM, verificação de DLLs.
4. Backup, restauração e recuperação testados de ponta a ponta.
5. `SOAK-72H` e caos completo.
6. Manual do operador, instalador de produção e runbooks.

**Pronto quando:** os 13 critérios CA-01..CA-13 verificados com evidência; `UX-01` com 5
operadores reais; instalação limpa e atualização testadas em máquina virgem.

---

## Sequência crítica

```
B1 (SDKs) ──┬─► Matriz preenchida ──► Fase 2 (adapter real) ──► Fase 3 ──► Fase 5
B2 (parque) ┘                                    ▲
B5/B6 ───────────────────────────────────────────┘ (workflow da urna)

Fase 1 NÃO depende de B1: simulador e mock destravam tudo, menos o hardware real.
```

**Recomendação:** iniciar a Fase 1 em paralelo à coleta das respostas. Ela não desperdiça
trabalho — o adapter mock e o simulador continuam sendo a espinha dorsal dos testes
depois que o adapter real existir.
