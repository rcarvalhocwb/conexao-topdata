# 07 — Backlog por fases

Regra que vale para todas: **"compilou" não é critério de pronto.** Cada fase fecha com
testes executados, resultados anexados, limitações conhecidas escritas e o próximo ensaio
de hardware identificado.

---

## Fase 0 — Descoberta · *concluída*

| Item | Situação |
|---|---|
| Inventário de hardware/modelo/firmware | **Bloqueado em B2** — depende do cliente |
| Perguntas em aberto | Concluído — [`01`](01-perguntas-criticas.md) |
| Matriz de capacidades e funções | Concluído — 51 de 58 funções em fonte primária |
| ADRs | Concluído — 21 registros |
| Análise de riscos | Concluído — [`08`](08-riscos-e-validacoes-topdata.md) |
| Plano de bancada | Concluído — [`09`](09-plano-de-bancada.md) |

> **Limitação que atravessa a fase:** falta o pacote de **exemplos de código**, com o
> `EasyInner.cs` e o enum `Enumeradores.Retorno`. As assinaturas P/Invoke **não foram
> deduzidas** — ver [`11`](11-capacidades-do-sdk.md), seção 5. O inventário do parque
> (B2) continua com o cliente e não bloqueou a Fase 1.

---

## Fase 1 — Fundação executável · *concluída*

> **Verde contra o simulador, não contra hardware.** Nenhuma catraca real foi acionada.
>
> **Correção de 24/09, depois de eu ter declarado a fase concluída.** O item 4 pedia
> "`Edge.Supervisor` como Windows Service, supervisionando grupos". Eu havia construído a
> *lógica* de supervisão e o serviço gRPC, ambos como **biblioteca** — não havia processo
> nenhum. Pior: `IWorkerHost` só tinha dublês de teste, então nada no produto sabia **subir**
> um worker, e o instalador mandava executar um `Edge.Supervisor.exe` que nunca era gerado.
> Corrigido com `Program.cs`, `ProcessoDeWorker` e `ConfiguracaoDoSupervisor`, e travado por
> um teste que reprova se o instalador publicar projeto sem executável.

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

### Como cada critério foi fechado

| Critério | Como foi verificado |
|---|---|
| CI verde nos dois sistemas | Jobs `linux` e `windows` em `.github/workflows/ci.yml` |
| Soak sem vazamento | `tests/LoadAndSoak`. **Achou um vazamento real** — o histórico da máquina de estados crescia sem limite, ~400 B/evento, 1 MB → 115 MB em 282 mil eventos. Corrigido com janela de 100 transições e contador total |
| `kill -9` não perde evento | `tests/Integration/QuedaAbruptaTests.cs` mata o `CrashProbe` com SIGKILL de verdade e confere o banco depois |
| Verificação do verificador | Cada trava foi violada de propósito, a falha foi observada, e só então restaurada — arquitetura, contrato IPC, redação de log, sincronia do instalador e vazamento de token |

### Limitações conhecidas ao fim da Fase 1

1. **A ligação nativa não existe.** `VinculacaoNativaPendente` lança em todos os métodos,
   de propósito: as assinaturas P/Invoke não foram deduzidas.
2. **HIL-STACK-01 não foi executado.** Não se sabe ainda se um processo .NET 10 `win-x86`
   carrega a `EasyInner.dll`, que exige .NET Framework 3.5 ([`12`](12-decisao-de-stack.md)).
3. **O Desktop compila mas não foi visto rodando** — a verificação visual exige Windows.
4. **Os scripts do instalador não foram executados**, só analisados. O job `windows` os
   submete ao parser do PowerShell; rodar de verdade é tarefa de bancada.
5. **B2, B4, B7 e B8 seguem sem resposta** ([`01`](01-perguntas-criticas.md)).

### Próximo ensaio de hardware necessário

**HIL-STACK-01**, antes de qualquer outro: publicar com `installer/publicar.ps1` numa
máquina Windows com o SDK Inner Acesso instalado e confirmar o carregamento da DLL. É o
que decide se o worker segue em .NET 10 ou volta para .NET Framework 4.8 atrás do mesmo
IPC. Nenhum item da Fase 2 começa antes desse resultado.

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

> **Antecipado em 25/09:** os itens 1 e 3 foram construídos fora de ordem, de propósito.
> Não dependem de hardware — o que estava bloqueando as Fases 2 e 3 é `HIL-STACK-01`, e
> drenagem de fila não toca em catraca nenhuma. Ver
> [`15`](15-integracao-e-sincronizacao.md).

1. SDK de conectores com contrato versionado. **`IConectorDeSincronizacao` construído.**
2. Conectores REST, PostgreSQL, SQL Server, MySQL, Supabase (via API/backend seguro).
   *Nenhum construído: o drenador foi exercitado só contra conectores de teste.*
3. Prioridade de sincronização, DLQ, reprocessamento manual, cursores.
   **Drenador, prioridades e cartas mortas construídos** (`Sync.Core`, migração `002`);
   o reprocessamento manual e a descida por cursor continuam pendentes.
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
6. Manual do operador, runbooks e **assinatura de código** do instalador.
   O MSI em si foi antecipado para a Fase 1: a CI já o constrói a cada commit.

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

**Foi o que se fez:** a Fase 1 correu em paralelo à coleta das respostas, e não
desperdiçou trabalho — o adapter mock e o simulador seguem sendo a espinha dorsal dos
testes depois que o adapter real existir. O que ficou parado é o que só hardware
responde.
