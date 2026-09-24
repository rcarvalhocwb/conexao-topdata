# 03 — Arquitetura

## 1. Princípio organizador

> **O que decide se a catraca gira roda na borda, em processo próprio, com banco próprio,
> e não pergunta nada para ninguém pela internet.**

Tudo o mais — nuvem, relatórios, integrações, painéis — é consequência assíncrona disso.

## 2. Topologia de processos

```
┌─────────────────────────────────────── Host Windows x64 (Edge) ───────────────────────────────────────┐
│                                                                                                        │
│  ┌──────────────────────────┐            gRPC sobre named pipe            ┌─────────────────────────┐  │
│  │  Desktop.App (x64, WPF)  │ ─────────  (ACL + token de sessão)  ──────► │  Edge.Supervisor (x64)  │  │
│  │  MVVM · pt-BR · a11y     │ ◄────────  stream de eventos/estado ─────── │  Windows Service        │  │
│  └──────────────────────────┘                                            └───────────┬─────────────┘  │
│        NUNCA carrega a DLL                                                           │                 │
│                                                                    supervisão, backoff+jitter,        │
│                                                                    circuit breaker, health, watchdog   │
│                                                                                      │                 │
│                         ┌────────────────────────────┬───────────────────────────────┼──────────┐     │
│                         ▼                            ▼                               ▼          │     │
│            ┌─────────────────────┐      ┌─────────────────────┐        ┌─────────────────────┐  │     │
│            │ Edge.Worker.X86 #1  │      │ Edge.Worker.X86 #2  │  ...   │ Edge.Worker.X86 #N  │  │     │
│            │ win-x86 · ≤20 disp. │      │ win-x86 · ≤20 disp. │        │ win-x86 · ≤20 disp. │  │     │
│            │ EasyInner.dll       │      │ EasyInner.dll       │        │ EasyInner.dll       │  │     │
│            │ 1 thread bloqueante │      │ (instância própria) │        │ (instância própria) │  │     │
│            │ + fila de comandos  │      │                     │        │                     │  │     │
│            └──────────┬──────────┘      └──────────┬──────────┘        └──────────┬──────────┘  │     │
│                       │                            │                              │             │     │
│  ┌────────────────────┴────────────────────────────┴──────────────────────────────┴──────────┐  │     │
│  │  Access.Application — núcleo de decisão (in-process no Supervisor)                        │  │     │
│  │  cache versionado · anti-passback · idempotência · anti-replay · p50/p95/p99              │  │     │
│  └────────────────────────────────────┬─────────────────────────────────────────────────────┘  │     │
│                                       ▼                                                        │     │
│  ┌──────────────────────────────────────────────────────────────────────────────────────────┐ │     │
│  │  SQLite (WAL) — decisão, evento, comando, outbox, inbox, auditoria: UMA transação        │ │     │
│  └────────────────────────────────────┬─────────────────────────────────────────────────────┘ │     │
│                                       ▼                                                        │     │
│  ┌──────────────────────────────────────────────────────────────────────────────────────────┐ │     │
│  │  Sync.Core — drena a outbox em segundo plano, por prioridade. FORA do caminho crítico.   │ │     │
│  └────────────────────────────────────┬─────────────────────────────────────────────────────┘ │     │
└───────────────────────────────────────┼────────────────────────────────────────────────────────┘     │
                                        ▼                                                               │
                            Internet (pode estar ausente por dias)                                      │
                                        ▼                                                               │
                        REST · filas · bancos via backend · webhooks assinados                          │
                                                                                                        │
  ┌──────────────────── VLAN isolada de equipamentos (ACL: só o Edge alcança) ─────────────────────┐    │
  │   Catracas / coletores / urnas  ──── TCP iniciado PELO equipamento ────►  Edge (servidor)      │    │
  └───────────────────────────────────────────────────────────────────────────────────────────────┘    │
```

Observações que valem mais que o desenho:

- O **equipamento é o cliente TCP**; o Edge é quem escuta. Isso dita porta, firewall,
  NAT e o fato de que "catraca sumiu" muitas vezes é um problema de rota, não de software.
- O **Desktop.App não referencia**, nem transitivamente, nenhum projeto de interop. Isso é
  garantido por teste de arquitetura, não por disciplina.
- O **worker é sacrificável**. Ele existe para poder morrer sem levar nada junto.

## 3. Projetos da solução

```
src/
  Desktop.App/                    net-windows, x64, WPF + MVVM
  Edge.Supervisor/                net-windows, x64, Worker Service
  Edge.Worker.X86/                net-windows, RuntimeIdentifier=win-x86  ← ÚNICO que carrega a DLL
  Topdata.EasyInner.Interop/      seam nativo (P/Invoke ou COM — ver B3); AnyCPU compilável, x86 em runtime
  Topdata.EasyInner.Adapter/      ITopdataInnerAdapter: real, mock e gravador/reprodutor
  Topdata.Facial.Adapter/         WebSocket/JSON — SDK distinto, jamais misturado
  Access.Domain/                  entidades, invariantes, EventOrigin, Decision, ReasonCode. SEM dependências
  Access.Application/             casos de uso, motor de decisão, máquinas de estado
  Access.Infrastructure.SQLite/   repositórios, migrações, outbox/inbox
  Sync.Core/                      drenagem, prioridades, DLQ, cursores
  Sync.Connectors.Rest/
  Sync.Connectors.Databases/
  Contracts/                      .proto do IPC + DTOs versionados
  Shared.Observability/           logs JSON, métricas, traces, redação de dado sensível
tests/
  Unit/ Integration/ Contract/ Simulator/ HardwareInLoop/ LoadAndSoak/
docs/  installer/
```

**Regra de dependência (verificada por `ArchUnitNET` na Fase 1):**

- `Access.Domain` não referencia nada. Nem SQLite, nem interop, nem log.
- Nada em `Desktop.App`, `Access.*` ou `Sync.*` alcança `Topdata.*.Interop`.
- `Access.Application` conhece `ITopdataInnerAdapter`, nunca a implementação.
- Nenhum conector de nuvem é alcançável a partir do caminho de decisão.

Clean Architecture aplicada onde paga: o domínio precisa ser testável sem Windows, sem
DLL e sem hardware — porque 90% dos testes vão rodar em CI Linux. Onde não paga (um
repositório que só faz `SELECT`), não haverá interface de uma implementação só.

## 4. O worker e a DLL

O que o worker garante, e por quê:

| Garantia | Motivo |
|---|---|
| Processo `win-x86` explícito | R1 — a DLL é 32 bits; x64 dá `BadImageFormatException` ou erro 8 |
| Uma instância de DLL por processo | R3/R5 — isola estado global da DLL entre grupos |
| Uma thread dedicada para o laço bloqueante | R4 — `ReceberDadosOnLine` bloqueia |
| Fila de comandos serializada **por worker** | R5 — buffers possivelmente globais; nunca intercalar `montar→enviar` de dois equipamentos |
| `AbrirPortaComunicacao` singleton com lifecycle explícito | Abrir duas vezes ou esquecer de fechar é fonte conhecida de falha |
| Watchdog com heartbeat para o supervisor | Detecta laço travado que nenhum `try/catch` pega |
| Sem estado durável próprio | Pode ser morto a qualquer instante; a verdade está no SQLite |

**A questão do "por worker" versus "por dispositivo"** é a decisão de throughput mais
importante da Fase 2. Se a Topdata confirmar que os buffers são por handle e não globais
(pauta item 2), a serialização pode ser relaxada para por dispositivo e a janela de
configuração de um parque grande cai de horas para minutos. Até lá, serializa por worker —
lento e correto, em vez de rápido e corrompido. Ver
[ADR-0006](ADR/ADR-0006-serializacao-montar-enviar.md).

### Particionamento

Teto default **20** equipamentos por worker, hard cap **25**, limite do briefing **30**.
A margem não é timidez: o limite de 30 é "prático", não contratual, e o custo de errar é
um worker que degrada sob pico — exatamente quando ninguém pode mexer. Grupos são
formados por **afinidade física** (mesmo portão/setor), para que a morte de um worker
concentre o impacto em uma área que a operação consegue isolar, em vez de espalhar uma
falha parcial por todo o evento. Ver [ADR-0005](ADR/ADR-0005-particionamento-por-worker.md).

## 5. Máquina de estados por equipamento

```
Disabled → Discovering → Connecting → ReadingIdentity → CheckingCompatibility
        → Configuring → SyncingOfflineData → OnlinePolling
        → AccessPending → CommandIssued → AwaitingPhysicalConfirmation → Completed
```

Estados alternativos: `OfflineAutonomous`, `Degraded`, `Reconnecting`, `Quarantined`,
`Maintenance`, `FirmwareMismatch`, `FatalDependencyError`.

| Estado | Entra quando | Sai quando | Timeout | Se estourar |
|---|---|---|---|---|
| `Discovering` | Dispositivo cadastrado, sem contato | Equipamento conecta | 30 s | `Degraded`, alerta |
| `Connecting` | Socket aceito | `TestarConexaoInner` OK | 5 s, 3 tentativas | `Reconnecting` com backoff+jitter |
| `ReadingIdentity` | Conexão OK | Firmware/modelo lidos | 5 s | `Quarantined` |
| `CheckingCompatibility` | Identidade lida | Matriz confere | imediato | `FirmwareMismatch` — **não configura** |
| `Configuring` | Compatível + diff aprovado | Envio atômico confirmado | 30 s | rollback, `Quarantined` |
| `SyncingOfflineData` | Configurado | Subconjunto enviado | por volume | `Degraded`, segue on-line |
| `OnlinePolling` | Pronto | Evento ou queda | — | laço bloqueante + watchdog |
| `AccessPending` | Leitura recebida | Decisão emitida | **150 ms** | `OfflineFallback` |
| `CommandIssued` | Comando enviado | Confirmação/origem 5 | conforme acionamento | registra autorização sem confirmação |
| `AwaitingPhysicalConfirmation` | Comando OK | Origem 6 | configurável, default 8 s | `AuthorizedWithoutPassage` → reconciliação |
| `Quarantined` | Falha repetida | Ação humana ou backoff longo | — | **não contamina o grupo** |

Regras não negociáveis:

- **Toda** transição é auditável: `(de, para, gatilho, timestamp, correlationId, retorno nativo)`.
- Após evento de leitura, a reabilitação/liberação do leitor é **obrigatória e rastreada**
  por timer próprio — um leitor esquecido em estado pendente é uma catraca morta que
  parece viva.
- Nenhum estado depende de rede externa ao host.
- A máquina inteira é **simulável sem hardware** — é uma classe pura em
  `Access.Application`, dirigida por eventos, testada por tabela de transição.
- `PingOnline` é usado conforme a configuração de mudança automática on-line/off-line, e a
  transição de regime **nunca** ocorre sem que a lista off-line tenha sido sincronizada
  antes (senão o equipamento cai para off-line já cego).

## 6. Núcleo de decisão

Entrada: `(deviceId, credencial string, origem, timestampEquipamento, timestampRecepção)`.
Saída: `Decision`.

```csharp
public sealed record Decision(
    DecisionOutcome Outcome,      // Allowed | Denied | Review | OfflineFallback
    ReasonCode Reason,            // estável, versionado, nunca traduzido no domínio
    string OperatorMessage,       // pt-BR, acionável, montado na camada de apresentação
    IReadOnlyList<RuleTrace> Trace,
    TimeSpan Elapsed);
```

`ReasonCode` é um contrato: entra em relatório, em integração e em auditoria. Muda com
versionamento, nunca por conveniência de texto. A mensagem amigável é separada — o mesmo
`CREDENCIAL_FORA_DA_JANELA` vira "Ingresso válido só a partir das 19h" na tela e continua
sendo o mesmo código no CSV exportado.

`RuleTrace` existe para o simulador de regras ("esta pessoa entraria agora por este
gate?") poder **explicar** o resultado em vez de só responder sim ou não. É o mesmo
código que decide na operação — não uma reimplementação paralela, que inevitavelmente
divergiria.

**Meta:** p95 ≤ 100 ms, excluído tempo humano e mecânico. Cada etapa (lookup, regras,
persistência, comando) tem seu próprio histograma e seu próprio timeout.

### Ordem de avaliação (curto-circuito no primeiro bloqueio)

1. Bloqueio emergencial (lista negra) — sempre primeiro, sempre sincronizado primeiro.
2. Existência e status da credencial.
3. Anti-replay (mesma credencial, mesmo gate, janela curta).
4. Janela temporal / evento / setor / zona.
5. Anti-passback.
6. Número de utilizações / lotação.
7. Regras do gate (sentido, perfil físico).

## 7. Níveis de degradação

Porque a lista do equipamento **não comporta** um evento de 50.000 pessoas
(ver [`02`](02-matriz-compatibilidade.md)), a autonomia mora na borda:

| Nível | Situação | Quem decide | Cobertura | Visível para o operador como |
|---|---|---|---|---|
| **T0** | Tudo no ar | Edge (local) | 100% das regras | "Normal" |
| **T1** | Internet caiu | Edge (local) | 100% das regras | "Operando sem internet — nenhuma ação necessária" |
| **T2** | Edge/PC caiu | Equipamento (lista local) | Subconjunto priorizado, regras simples | "Catraca 08 operando com lista local" |
| **T3** | Equipamento isolado | Equipamento | Política do gate | "Portão em modo de segurança: {abre\|fecha}" |

**T1 é o regime normal de um evento**, não uma exceção. É para ele que o sistema é
projetado; a internet é conveniência.

**T2 exige uma política de subconjunto** — quem cabe na lista de 15.000 quando há 50.000
pessoas? (por setor, por lote, por horário de chegada previsto). Isso é decisão de negócio
e vira configuração explícita, com a cobertura estimada mostrada na tela antes de o evento
começar. O operador precisa saber, **antes**, que em T2 aquele portão atende 30% do
público — não descobrir durante.

## 8. IPC local

gRPC sobre **named pipes** (Kestrel `ListenNamedPipe`, disponível no .NET 8+), com:

- ACL do pipe restrita à conta do serviço e ao grupo de operadores;
- token de sessão por conexão, rotacionado;
- contratos versionados em `Contracts/` (`.proto`), com teste de compatibilidade
  retroativa em CI;
- streaming servidor→cliente para eventos ao vivo (a UI nunca faz polling).

Alternativa considerada e recusada: TCP em `localhost` — exposto a qualquer processo local,
exigiria autenticação própria e abriria porta desnecessária.
Ver [ADR-0004](ADR/ADR-0004-ipc-grpc-named-pipes.md).

## 9. Persistência e sincronização

Detalhado em [`05-modelo-de-dados.md`](05-modelo-de-dados.md). Os dois pontos que definem
a arquitetura:

1. **Uma transação local grava evento + decisão + comando + item de outbox.** Se o processo
   morrer no instante seguinte, ou tudo aconteceu, ou nada aconteceu. Não existe "gravou o
   evento mas perdeu a sincronização".
2. **A nuvem nunca é escrita no caminho crítico.** O `Sync.Core` drena a outbox depois,
   por prioridade: bloqueios emergenciais e configurações críticas na frente, eventos
   históricos atrás, em lote comprimido, com retry exponencial com jitter, DLQ e cursor.

## 10. Observabilidade

Logs JSON estruturados, métricas e traces com `correlationId` que atravessa
UI → Supervisor → Worker → DLL → evento → outbox → conector.

A redação de dado sensível é feita **no serializador**, não em cada chamada de log: um
`CardNumber` é um tipo cujo `ToString()` já devolve mascarado, e o teste `SEC-LOG-01`
varre os logs gerados na suíte inteira procurando vazamento. Confiar em disciplina de
programador para não logar cartão é como confiar em disciplina para não esquecer o
`FecharPortaComunicacao`.

Métricas mínimas em [`06`](06-plano-de-testes-e-dimensionamento.md), seção 5.

## 11. Segurança

- Serviço roda com conta de menor privilégio; **não** administrador (o instalador cuida
  das permissões que exigem elevação, uma vez).
- Segredos em DPAPI (escopo máquina) / Windows Credential Manager; nunca em texto puro,
  nunca em arquivo de configuração.
- Biometria segregada em armazenamento próprio, com chave distinta.
- mTLS e rotação de segredos para saída; webhooks assinados.
- WebServer do equipamento **nunca** exposto à internet; VLAN isolada com ACL.
- API local protegida por ACL de pipe + token — outro processo na mesma máquina não fala
  com o Edge.
- Atualizações assinadas, com rollback; SBOM e verificação de integridade das DLLs no
  startup do worker (uma DLL trocada por antivírus ou por instalação de terceiro é causa
  conhecida de falha, e o produto precisa dizer isso em vez de mostrar "erro 8").
