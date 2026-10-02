# 02 — Arquiteto da camada inteligente: dados, estatística e IA aplicada no PC do evento

> Estudo de inovação (Rayzer XAcess), 01/10/2026. Lido sobre a branch
> `claude/gallant-wright-pdloor` (HEAD `8012904`: Etapa 0, A.1–A.9, B.1–B.3 e a migração 016) e
> sobre a branch `mapa-de-giro` (HEAD `bd1efc2`, migração 017, ainda fora da principal). **Nada no
> repositório foi alterado.** Segue o `brief.md`.
>
> **Alinhamento com os relatórios irmãos.** As composições **C1–C16** são do `01-engenheiro-topdata.md`
> (§7) e as capacidades **CI-01–CI-10** e o plano de prova (`NOVO-LOAD-IA-01`, `NOVO-CHAOS-IA-01`,
> `NOVO-SOAK-IA-24H`, `NOVO-ARQ-IA-01`, `NOVO-SEC-IA-01`, `NOVO-UX-IA-01`, `NOVO-SIM-*`) são do
> `03-produto-demo-qa.md` (§1, §5). Aqui eu **não as redefino**: digo onde rodam, o que gravam, com
> que método decidem um alerta, como provam e em que ordem entram. O que é novo neste relatório tem
> id **IN-xx** (capacidade) e **G-xx** (dado a gravar). Tabela de correspondência na §1.
>
> **Selos** (do brief): `IMPLEMENTÁVEL AGORA` · `DEPENDE DE BANCADA` · `DEPENDE DA TOPDATA` ·
> `PROPOSTA FUTURA`. Afirmação sobre a catraca cita `arquivo:linha`; o resto é `A_CONFIRMAR_COM_TOPDATA`.
> Abreviações de caminho: `MIG/` = `src/Access.Infrastructure.SQLite/Migrations/`; `PUMP` =
> `src/Edge.Worker/DevicePump.cs`; `SESS` = `src/Edge.Worker/Operacao/SessaoDeOperacao.cs`; `DEC` =
> `src/Access.Application/Ingressos/DecisorDeIngresso.cs`; `REPO` =
> `src/Access.Infrastructure.SQLite/RepositorioDeIngressos.cs`; `OPER` =
> `src/Access.Infrastructure.SQLite/Operacao.cs`; `PROTO` = `src/Contracts/Protos/edge_control.proto`.

---

## 0. Resumo em uma página

1. **A matéria-prima existe pela metade.** O que vira linha hoje é a **tentativa**
   (`ticket_use_attempt`, `MIG/003:54-66`, com origem `010:15`) e a **situação atual** de cada catraca
   (`device_status`, `006:6-17` + `008`, `013`, `016`). Mas a situação é *upsert* sem histórico
   (`OPER:92-140`), as tabelas de evento bruto, decisão, passagem e transição (`001:6-38, 59-66,
   125-134`) **não são gravadas pela operação** (só `AccessJournal`, usado só por testes), a leitura
   vazia é negada **sem linha** (`DEC:116-119`), a latência da decisão vai só para o registro em
   texto (`SESS:469-479`), erros de recepção ficam na memória (`PUMP:125, 576, 596`) e as métricas
   desenhadas nunca foram ligadas (`src/Shared.Observability/MetricasDoEdge.cs:22-56`; nenhum
   consumidor). A §2 lista 14 lacunas (G-01…G-14), quase todas baratas.
2. **Onde roda.** Dois pedaços, nunca no passo da decisão:
   - no **worker**, só um *coletor mínimo*: incremento O(1) de contadores e enfileiramento num anel
     limitado, descarregado **entre voltas** como a publicação da situação (`SESS:446-467`);
   - no **serviço** (`Edge.Supervisor`), o *Analisador*: um `BackgroundService` que lê a base da
     decisão **só para leitura** e calcula tudo em funções puras de um projeto novo
     `Access.Inteligencia` (só BCL, como `Access.Importacao`).
3. **Armazenamento separado: `telemetria.db`.** O SQLite tem **um escritor por arquivo** e o
   `busy_timeout` é de 5 s (`SqliteConnectionFactory.cs:56`). Se a camada escrevesse em `acesso.db`,
   cada escrita dela poderia segurar a decisão de outro worker. Num arquivo próprio, o pior caso da
   telemetria travada é **perder telemetria**, nunca atrasar um giro. Por construção, o arquivo não
   tem código de ingresso nem nome.
4. **Método: estatística robusta e explicável antes de ML.** Janelas de 1 min, histogramas de baldes
   fixos (que se somam), mediana/MAD, EWMA, comparação com as vizinhas **na mesma janela** (o público
   é o mesmo), Poisson para "silêncio", Wilson para taxas, Theil–Sen para deriva e CUSUM para
   mudança lenta. Todo alerta carrega **a conta que o disparou**. ML só depois de vários eventos,
   com incidentes rotulados (§4.6).
5. **Capacidades (§5):** as dez CI do 03, com as composições do 01 por baixo, mais quatro novas:
   **IN-05** fluxo por sentido lógico e lotação estimada (sobre o Mapa de giro, 017), **IN-07**
   sugestão de parametrização, com a **janela real do relé medida** (origem 5 − liberação), que também
   confere se a configuração está mesmo na catraca, **IN-04b** tempo para escoar os ingressos ainda
   não usados (demanda conhecida, sem inventar fila) e **IN-09** relatório pós-evento com achados.
   O assistente com LLM (**IN-11**) é `PROPOSTA FUTURA`; agora entram as "perguntas prontas"
   determinísticas.
6. **Nada decide liberar.** Nenhuma capacidade nega, bloqueia ou libera. A sugestão de
   parametrização só **preenche** a tela da A.6; quem salva e aplica é o operador, com nome. O
   bloqueio automático de ingresso "suspeito" foi **descartado** (§5, descartes).
7. **Relés (fato novo do dono):** cada alerta da §5.2 diz se poderia, no futuro, acionar um sinal
   físico pelo relé 2 (sinaleiro, aviso discreto, gatilho de câmera). Sempre atrás de chave, só em
   `Polling` e só depois de NOVO-HIL-REL-04/06. O motivo do ensaio: a origem 5 não diz qual relé
   expirou (01 F13). Câmera é dado pessoal e exige RIPD.
8. **Sentido (fato novo do dono):** com a 017, cada tentativa liberada diz `counted_as` (entrada ou
   saída) e `release_function` (sentido físico pedido), e guarda `turn_complement` bruto. Os
   indicadores passam a ser **por sentido lógico**. "Giro no sentido inesperado" vira sinal assim que
   T14 disser o que o `Complemento` da origem 6 significa. Até lá, o sinal vem da conferência de
   comissionamento (`turn_check`) e de C4.
9. **LGPD:** o Analisador **não seleciona `qr_normalized`**. Correlaciona reuso por `ticket_id` (UUID
   local) e, só para código desconhecido, pela impressão HMAC da B.1, calculada em memória no serviço,
   que já guarda a chave (`ChaveDaImpressao`). Nenhuma tabela nova tem código, máscara ou nome de
   público.
10. **Ranking (§8) e fatiamento em 12 etapas I.0–I.11 (§9)**, cada uma com teste e critério de
    pronto. A primeira entrega visível ("Por que negou", I.2) não muda o worker. A camada só fica
    **ligada por padrão** depois de `NOVO-LOAD-IA-01`, `NOVO-CHAOS-IA-01` e `NOVO-SOAK-IA-24H`
    passarem.

---

## 1. Correspondência de nomes

| Este relatório | 03 (CI) | 01 (C) | O que é |
|---|---|---|---|
| IN-01 Saúde por catraca | CI-01 | C5, C9, C12 (+C16 já existe) | índice explicável, sinais precursores |
| IN-02 Alertas e anomalias | CI-02 | C3, C4, C7, C10, C13 | regras com ciclo de vida, "ciente" |
| IN-03 Reuso e compartilhamento | CI-05 | — | mesmo ingresso em catracas diferentes |
| IN-04 Ritmo, ocupação e recomendação | CI-03 | (docs/14 §1) | ocupação medida, fila **não** observada |
| IN-04b Tempo para escoar a demanda conhecida | — (novo) | — | ingressos válidos ainda não usados ÷ capacidade medida |
| IN-05 Fluxo por sentido lógico e lotação estimada | — (novo; usa CI-09) | §6 do 01 | sobre a 017 (Mapa de giro) |
| IN-06 Por que negou | CI-04 | C6 (display, lado do worker) | explicação ao operador |
| IN-07 Sugestão de parametrização | — (novo) | C1 (prazo), C16 | tempo do relé, leitor, display |
| IN-08 Gêmeo vivo e replay | CI-07 | C2 | replay ⊆ gravado |
| IN-09 Relatório pós-evento com achados | (R8 no 03 §2.2) | — | R1–R8 + achados determinísticos |
| IN-10 Check-up como linha de base | CI-06 | C8 | o resultado vira a referência da saúde |
| IN-11 Assistente do operador | (fora no 03) | (descartado no 01 para decisão) | LLM: PROPOSTA FUTURA |
| IN-12 Conciliação de testemunhas | CI-08 | C14 | tentativas × giros × bilhetes |
| Coletor mínimo (G-01…G-14) | pré-requisito de CI-01/02/07 | C2 + C12 + F12 | o que o worker passa a gravar |

C1 (prazo real do `MonitoraGiroCatraca`, F9 do 01) é **correção do laço**, não inteligência. Entra
em paralelo (§9) porque, sem ele, as estatísticas de "sem giro" contam uma pista presa como
desistência.

---

## 2. Inventário de dados

### 2.1 O que já é gravado ou publicado

| Fonte | Onde (arquivo:linha) | Quem grava e quando | Serve para | Limite para a inteligência |
|---|---|---|---|---|
| `ticket_use_attempt` | `MIG/003:54-66`; `category` `004:23`; `reader_origin` `010:15`; índice por momento `006:29` | worker, **na transação da decisão** (`REPO:338-404`, insert `REPO:1231-1235`) | toda leitura com código: resultado, motivo, catraca, hora da decisão (`at`), origem (2, 3, 21), categoria | `qr_normalized` **em claro** (`003:58`): o Analisador não o lê (§6). `gate_id` = `device_id`: não há portão (`DEC:95`, o decisor é criado sem mapa em `Edge.Worker.X86/Program.cs:176`). `passage_confirmed_at` chega **depois**, por `UPDATE` (`REPO:406-418`) |
| Prova de giro | `ticket_use_attempt.passage_confirmed_at` (`003:65`) com o `ReceivedTime` da origem 6 (`DEC:172-181`) | worker, ao receber a origem 6 com tentativa pendente | Δ liberação→giro, "liberado sem giro" | giro **sem** tentativa pendente não grava nada (01 F10); resolução limitada pela volta do laço (`DeviceGroupLoop.cs:75`, 500 ms por catraca) |
| `device_status` | `MIG/006:6-17`; relógio `008:4-7`; config aplicada `013:17-18`; sessão/simulação `016:21-24` | worker, a cada mudança e a cada 2 s (`SESS:446-467`; ADR-0024:23-25) | estado, em operação, firmware, reconexões, último evento, deriva do relógio, versão aplicada | **upsert sem histórico** (`OPER:92-140`): não dá para saber quanto tempo a catraca ficou fora nem como as reconexões evoluíram |
| `operator_command` | `MIG/009:5-20` (motivo `:11`, quem `:12`, resultado `:19`) | serviço grava o pedido; worker o desfecho | liberações manuais, aplicar configuração, coleta, bip | auditoria só-INSERT (`009:25-44`) |
| `collected_ticket` | `MIG/015:21-44` (máscara `:27`, HMAC `:28`) | worker, um por um, antes do próximo (A.9) | segunda testemunha (C14) | chave `catraca.coletar_bilhetes` desligada; hora ao minuto |
| `device_config` / histórico | `MIG/012:12-46, 54-73` | serviço (A.6) | o que foi configurado e quando | — |
| `credential_event` | `MIG/011:261-295` (HMAC `:265`) | cadastro (B.4+) | bloqueio, cancelamento: quem e por quê | ainda sem caminho de produção |
| `ticket` | `MIG/003:14-35`; `004:13,18`; `011:80-126` | sincronização, balcão | demanda conhecida: válidos não usados, janela, categoria | categoria sensível (PCD) só agregada (`docs/34-anexos/03-cartoes-e-lgpd.md:677`) |
| Saída e sincronização | `outbox` `001:72-85`; `dead_letter` `002:7-22`; `sync_cursor` `002:29-35` | serviço e worker | idade da fila, falhas de integração | — |
| Situação do serviço | `PROTO:55-87` (`ObterEstadoResponse`: outbox, idade, nível, internet); `PROTO:323-335` (`Diagnostico`: workers, reinícios, últimas linhas) | serviço | saúde do PC e da nuvem | sem série histórica |
| Mapa de giro (branch `mapa-de-giro`) | `017_mapa_de_giro.sql:16-33` (`turn_map_rule`), `:104-113` (`turn_check`), `:137-140` (`counted_as`, `release_function`, `turn_complement` na tentativa) | serviço (mapa) e worker (por tentativa) | **sentido lógico** de cada giro e sentido físico pedido | ainda não está na branch principal; o significado de `turn_complement` é T14 |
| Agregados prontos | `ConsultasDaOperacao.cs:136-193` (por catraca, hora, motivo); `OPER:254-276` (`Resumir`) | serviço, sob demanda | R1/R3 parciais | `Resumir` faz `COUNT(*)` na tabela inteira a cada chamada: o Analisador **não** pode seguir esse padrão (§3.4) |

Tabelas que existem e **não** são gravadas pela operação: `raw_event`, `access_decision`,
`device_command`, `physical_passage` (só `AccessJournal.cs`, sem uso fora de testes) e
`device_state_transition`, `audit_log`, `inbox` (nenhum `INSERT` em `src/`). Confere com
`docs/34-estudo-modulo-catraca.md:98-100`.

### 2.2 O que falta gravar, quanto custa e o que habilita

"Barato" = O(1) no passo, escrita **fora** do passo, em lote, em `telemetria.db`.

| Id | Dado | Hoje | Como gravar | Custo | Habilita |
|---|---|---|---|---|---|
| G-01 | Origens que não são leitura (4, 5, 6 órfã, 7, 8–10, 20, teclas 35/42/65–67, desconhecidas) com `Complemento`, hora da catraca, estado da máquina e id da tentativa pendente | uma linha de texto (`SESS:481-486`) | `device_signal` (C2) | anel em memória + lote a cada 2 s | C3, C4, C7, C10, IN-07, IN-08 |
| G-02 | Leitura vazia ("leitura sem credencial") | negada sem linha (`DEC:116-119`) | `device_signal`, tipo `leitura_vazia`, **sem conteúdo** | idem | leitor sujo ou mal configurado (IN-01, IN-07) |
| G-03 | Latência da decisão | só no registro (`SESS:469-479`) | histograma por minuto e catraca | incremento de balde | `NOVO-LOAD-IA-01` em campo; saúde do PC |
| G-04 | Erros de recepção | memória (`PUMP:125, 576, 596`) | contador por minuto | incremento | C12 |
| G-05 | Latência de cada chamada nativa (`AdapterResult.Elapsed`) | só no `ToString` (01 S5) | histograma por função e minuto (liga `MetricasDoEdge.LatenciaDoComando`) | incremento | C12, check-up |
| G-06 | Disponibilidade (segundos em operação por minuto) e reconexões no tempo | só o atual (`OPER:92-140`) | `health_minute` | 1 linha/catraca/min | R3 "tempo fora do ar", R8 (docs/25:58-62, 92-96) |
| G-07 | Transições não triviais da máquina (exceto `Polling→Polling` por `SemEventos`) | tabela `001:125-134` vazia | `device_signal`, tipo `transicao` | idem G-01 | replay; diagnóstico de F9/F11 |
| G-08 | Retorno ≠ 0 de `LiberarCatraca*` ("a catraca recusou") | perdido (`access_decision` não gravada) | `device_signal`, tipo `liberacao_recusada` | idem | "lê mas não libera" (03 R3), CI-04 |
| G-09 | Série da deriva do relógio (cada conferência) | só a última (`008:6`) | campo da conferência em `health_minute` | 1 valor/h | Theil–Sen (A7) |
| G-10 | Firmware a cada conexão | só o último (`006:12`) | `device_signal`, tipo `conexao` | 1 por conexão | C9 |
| G-11 | Agrupamento de catracas em **portões** | não existe (nomes por catraca só no serviço, `EdgeControlService.cs:187`) | chave `operacao.portoes` em `edge_setting` (JSON `{"Portão Norte":[1,2]}`), sem migração | nenhum | IN-04 por portão, redistribuição |
| G-12 | Sentido lógico e físico por giro | — | **já feito na 017** (branch `mapa-de-giro`) | — | IN-05 |
| G-13 | Duração da volta do laço (voltas/min, p95) | `DeviceGroupLoop.Voltas` (`:84`) só contado | `health_minute` por worker | incremento | laço perto do limite (NOVO-LOAD-LOOP-01) |
| G-14 | Instante da origem 5 por liberação | morre no texto | parte de G-01 | — | **janela real do relé** (IN-07 P1) |

Fica **fora**, de propósito: o código lido (inclusive o da leitura vazia), a imagem do display, o
nome do operador em tabela nova (ele já está em `operator_command.requested_by`, `009:12`).

---

## 3. Arquitetura

### 3.1 Invariantes (cada um tem teste na §7)

| # | Invariante | Prova |
|---|---|---|
| I1 | Nenhuma chamada da camada no passo da decisão; o worker só incrementa contadores e enfileira | `NOVO-ARQ-IA-01` (do 03) + revisão por dependência |
| I2 | O Analisador abre `acesso.db` **só para leitura** (`Mode=ReadOnly`) e nunca segura transação de leitura por mais de 50 ms | `NOVO-ARQ-IA-02` |
| I3 | A camada parada, lenta, com exceção ou com `telemetria.db` travada **não muda nada** na sequência nativa nem nas decisões | `NOVO-CHAOS-IA-01` |
| I4 | p95 da decisão < 150 ms, diferença com a camada ligada ≤ 10 ms, 0 `FALHA_NA_BASE_LOCAL` | `NOVO-LOAD-IA-01` |
| I5 | Mesma entrada + mesmo relógio + mesmos parâmetros ⇒ mesmos alertas e textos, byte a byte | `NOVO-DET-IA-01` |
| I6 | Nenhuma saída da camada (tabela, RPC, registro, exportação) contém código, máscara de público ou nome de titular | `NOVO-SEC-IA-01/02` |
| I7 | Nenhum resultado da camada altera configuração, ingresso ou catraca sem um ato do operador com nome | `NOVO-ARQ-IA-03` |
| I8 | Sem dados suficientes, a camada diz "aprendendo" e não emite nível nem alerta estatístico | testes de aquecimento por regra |

### 3.2 Onde roda

```
 Catraca ──TCP── Worker x86 (thread única; DLL)                      Serviço (Edge.Supervisor)
                 ├─ passo da decisão (inalterado) ──► acesso.db ◄── leitura RO ── Analisador (BackgroundService)
                 │     ticket_use_attempt, device_status                  │  funções puras: Access.Inteligencia
                 └─ coletor mínimo (entre voltas) ──► telemetria.db ◄─────┤  (TimeProvider injetado)
                       device_signal, health_minute         ▲             ├─ grava: agg_minute, alert,
                                                            └─────────────┤         suggestion, insight
                                                                          └─ gRPC (named pipe) ─► Painel / Gêmeo
```

- **Coletor mínimo, no worker.** `ColetorDeTelemetria` (em `Edge.Worker`) recebe:
  - de `SessaoDeOperacao.Decidir` (`SESS:469`), a latência;
  - de `Receber` (`SESS:481`), o evento sem o código: `RawCardData` é **descartado antes de
    enfileirar**;
  - de `PUMP:549`, as transições e os retornos;
  - de `DeviceSlot`, os contadores.

  Tudo vai para um anel limitado (p. ex. 4.096 itens; cheio = descarta o mais antigo e conta). O
  descarregamento acontece em `UmaVolta()` depois de `PublicarSeFor()` (`SESS:260-273`), com o mesmo
  `try/catch` (`SESS:454-466`) e no máximo a cada 2 s. O caminho do arquivo chega por `--telemetria`,
  como `--banco` (ADR-0024:17-19). **Sem o argumento, o coletor é um objeto nulo**: é como o worker
  roda hoje, e é o que a bancada usa.
- **Analisador, no serviço.** É um `BackgroundService` registrado ao lado de
  `AcompanhamentoDaOperacao` (`Edge.Supervisor/Program.cs:244`). Roda em thread de prioridade
  `BelowNormal`, com orçamento por ciclo. O serviço já está num Job Object junto com os workers
  (commit `619a120`). Por isso o orçamento de CPU da camada é medido no `NOVO-PERF-IA-01`, nunca
  suposto.
- **Funções puras em `Access.Inteligencia`** (só BCL + `Access.Domain`). Toda regra é
  `Avaliar(JanelaDeDados, Parametros, DateTimeOffset agora) → Resultado(nível, conta, texto)`. O
  host só lê, chama e grava. Esse é o padrão de `ConfiguracaoComRecuo` e de `Access.Importacao`, e é
  o que permite testar no Linux, sem Windows.

### 3.3 Armazenamento: `telemetria.db`

Arquivo próprio, ao lado de `acesso.db`, com migrador próprio (pasta `MigracoesDaTelemetria/`,
prefixo `T`). A numeração não colide com a de `acesso.db`, onde 016 já existe e 017 é do Mapa de giro.
Conexão com `synchronous=NORMAL`: em WAL, isso não corrompe o arquivo e pode perder as últimas
transações num corte de energia. É aceitável para telemetria, e não para prestação de contas, que
continua em `FULL` (`SqliteConnectionFactory.cs:53`).

Esboço da `T001` (nomes ilustrativos, `STRICT`, ISO-8601 UTC):

```sql
-- Caderno de sinais (C2, G-01/02/07/08/10/14). Só-INSERT. Sem coluna para código.
CREATE TABLE device_signal (
  id TEXT PRIMARY KEY,               -- UUIDv7
  session_id TEXT NULL,              -- partida do serviço (016)
  inner_number INTEGER NOT NULL CHECK (inner_number BETWEEN 1 AND 99),
  kind TEXT NOT NULL CHECK (kind IN ('origem','leitura_vazia','transicao','liberacao_recusada','conexao')),
  origin_raw INTEGER NULL,           -- bruto, sem CHECK (ADR-0018)
  complement INTEGER NULL,           -- 0..255, bruto
  state_from TEXT NULL, state_to TEXT NULL, trigger TEXT NULL,
  native_return INTEGER NULL,
  firmware TEXT NULL,                -- só em 'conexao'
  pending_attempt_id TEXT NULL,      -- tentativa pendente no instante (referência fraca a acesso.db)
  device_time TEXT NULL, received_at TEXT NOT NULL);
CREATE INDEX ix_signal_catraca ON device_signal (inner_number, received_at);
-- gatilhos: não muda, não se apaga (exceto pela rotina de retenção, que registra o expurgo)

-- Uma linha por catraca por minuto (G-03/04/05/06/09/13). Escrita pelo worker.
CREATE TABLE health_minute (
  inner_number INTEGER NOT NULL, minute TEXT NOT NULL,    -- 'yyyy-MM-ddTHH:mm'
  session_id TEXT NULL, worker TEXT NOT NULL,
  seconds_in_operation INTEGER NOT NULL, reconnects INTEGER NOT NULL,
  recv_errors INTEGER NOT NULL, empty_reads INTEGER NOT NULL, unknown_origins INTEGER NOT NULL,
  loop_turns INTEGER NOT NULL, decisions INTEGER NOT NULL,
  decision_ms_hist TEXT NOT NULL,     -- JSON de baldes fixos (§4.2)
  recv_ms_hist TEXT NOT NULL, loop_ms_hist TEXT NOT NULL,
  clock_drift_s INTEGER NULL,         -- só no minuto da conferência
  dropped INTEGER NOT NULL,           -- itens descartados pelo anel
  PRIMARY KEY (inner_number, minute, worker)) STRICT;

-- Escritas pelo Analisador (serviço):
-- agg_minute: por catraca e minuto, a partir das tentativas: leituras, liberados, negados por motivo
--   (JSON de contagens), giros, sem giro, hist. de Δ liberação→giro, hist. de ciclo, entradas/saídas
--   (017), por origem de leitura. Sem ticket_id.
-- alert: id, regra, catraca|portão, aberto_em, atualizado_em, fechado_em, nível, evidência (JSON
--   sem código), ciente_por, ciente_em, elegivel_a_rele. Situação só anda para a frente (gatilho).
-- suggestion: id, catraca, campo (o mesmo enum CampoDaCatraca da A.6), atual, sugerido, evidência,
--   criada_em, situação ('aberta','usada_pelo_operador','descartada'), quem, quando.
-- insight: achados do pós-evento, por corte (hash do corte, docs/25:108-110).
```

**Retenção.** `device_signal` e `health_minute` não têm dado pessoal: ficam com o evento
(proposta: 1 ano, para a calibração entre eventos, §4.6). `alert` e `suggestion` podem citar
`ticket_id` na evidência de reuso (IN-03), que é um pseudônimo. Seguem o prazo das tentativas
proposto no anexo 03 (`docs/34-anexos/03-cartoes-e-lgpd.md:751`: corte + 90 dias). Depois disso,
o `ticket_id` é apagado da evidência e a contagem fica. Prazos são proposta para o jurídico (D7).

**Por que não reaproveitar `raw_event`/`device_state_transition` de `acesso.db`?** Elas serviriam,
mas gravar nelas põe o worker disputando o escritor de `acesso.db` com as decisões dos **outros**
workers a cada lote. Elas continuam existindo para o uso para que foram criadas (ADR-0009/0018), se
um dia a decisão passar a gravá-las.

### 3.4 Cadência

| Ciclo | O que faz | Leitura |
|---|---|---|
| 1 s | segue as tentativas novas por `rowid` (como `AcompanhamentoDaOperacao.UmaLeitura`, `AcompanhamentoDaOperacao.cs:44-55`, que pode ser o mesmo leitor) e os sinais novos; atualiza baldes em memória; avalia as regras **rápidas** (urna cheia, giro órfão, liberação recusada, série de "sem giro", reuso) | O(linhas novas) |
| 1 s | relê as tentativas consumidas **ainda sem giro** dos últimos 30 s, pelo índice de `at` (`006:29`): o giro chega por `UPDATE` e o cursor por `rowid` não o vê | janela curta |
| 60 s | fecha o minuto: grava `agg_minute`; recalcula linhas de base (EWMA, MAD de 30 min), saúde, ritmo, regras de janela (leitor calado, picos, comunicação) | memória |
| 15 min | tendências (CUSUM do Δ, Theil–Sen do relógio), sugestões, previsão curta | `agg_minute`, `health_minute` |
| sob pedido | "Por que negou", replay, pós-evento | consulta pontual |

**Reinício do serviço:** o estado é reconstruído relendo 60 min de `agg_minute` e `health_minute` e
as tentativas da última hora. Alertas abertos são retomados pela chave de deduplicação (regra +
catraca), sem duplicar (`NOVO-REST-IA-01`). Até completar a janela, a camada mostra "Aprendendo".

**Orçamento:** o ciclo de 1 s precisa de p95 ≤ 50 ms e o de 60 s de p95 ≤ 300 ms, com 20 catracas e
30 mil tentativas no dia (`NOVO-PERF-IA-01`). Se um ciclo estourar, o seguinte é pulado e o
estouro é contado no Diagnóstico.

### 3.5 Chaves (em `edge_setting`, sem tela, como as da A.2/A.8)

| Chave | Padrão | Efeito |
|---|---|---|
| `inteligencia.ligada` | **desligada** até I.11 | o serviço sobe o Analisador; com ela desligada, as RPCs devolvem "desligada nesta instalação" |
| `inteligencia.coletor` | **desligada** até `NOVO-LOAD-IA-01` | o serviço passa `--telemetria` ao worker |
| `operacao.portoes` | vazia (cada catraca é o seu portão) | agrupamento para ritmo e redistribuição (G-11) |
| `inteligencia.parametros` | os da §4.5 | limiares versionados; o hash dos parâmetros vai em todo alerta, para o resultado ser reproduzível |
| `rele2.sinal_de_alerta.<regra>` | **desligada**, e o serviço recusa ligar sem NOVO-HIL-REL-04/06 | §5.2, coluna "relé no futuro" |

### 3.6 Contrato gRPC (acréscimos ao `PROTO`)

As regras do contrato continuam valendo:
- números de campo novos e nunca reaproveitados;
- o valor `0` de todo enum é `*_NAO_ESPECIFICADO`;
- **nenhuma** das palavras proibidas do teste `O_contrato_nao_tem_campo_para_dado_sensivel`
  (`tests/Contract/ContratoIpcTests.cs:18-28`): `cartao`, `card`, `credencial =`, `senha`,
  `password`, `template`, `foto`, `image`.

Três armadilhas: "imagem" contém `image`, "discard"/"scorecard" contêm `card`, e "Credencial" não
pode virar nome de campo. Para isso há o teste `NOVO-CTR-IA-01`, que roda o mesmo teste sobre o proto
estendido.

```proto
  // Camada inteligente. Só leitura, exceto marcar alerta como ciente e registrar o destino de uma
  // sugestão. Nada aqui comanda catraca nem muda configuração.
  rpc ObterSaudeDasCatracas(ObterSaudeDasCatracasRequest) returns (SaudeDasCatracas);
  rpc AcompanharAlertas(AcompanharAlertasRequest) returns (stream Alerta);
  rpc ListarAlertas(ListarAlertasRequest) returns (ListarAlertasResponse);
  rpc MarcarAlertaComoCiente(MarcarAlertaComoCienteRequest) returns (MarcarAlertaComoCienteResponse);
  rpc ObterRitmo(ObterRitmoRequest) returns (RitmoDasCatracas);
  rpc ExplicarNegativa(ExplicarNegativaRequest) returns (ExplicacaoDaNegativa);   // por evento_id
  rpc ObterSugestoes(ObterSugestoesRequest) returns (SugestoesDaCatraca);          // por inner
  rpc RegistrarDestinoDaSugestao(RegistrarDestinoDaSugestaoRequest) returns (RegistrarDestinoDaSugestaoResponse);
  rpc ObterReplay(ObterReplayRequest) returns (ReplayDaCatraca);                   // inner, de, ate
  rpc ObterRelatorioPosEvento(ObterRelatorioPosEventoRequest) returns (RelatorioPosEvento);

message SinalDeSaude {               // uma linha do "Por quê?"
  TipoDeSinal tipo = 1;              // COMUNICACAO, GIRO, LEITURA, RELOGIO, CONFIGURACAO, GIRO_SEM_PEDIDO, LACO
  NivelDeSinal nivel = 2;            // NAO_ESPECIFICADO=0, NORMAL, ATENCAO, ACAO, SEM_DADOS, APRENDENDO
  string resumo = 3;                 // "4 reconexões em 15 min (normal: até 1)"
  double valor = 4; double referencia = 5; int64 amostras = 6;
  google.protobuf.Timestamp desde = 7; google.protobuf.Timestamp ate = 8;
  string o_que_fazer = 9;
}
message Alerta {
  string alerta_id = 1; RegraDeAlerta regra = 2; int32 inner = 3; string portao = 4;
  NivelDeSinal nivel = 5; string texto = 6; string conta = 7;   // a conta que disparou, legível
  google.protobuf.Timestamp aberto_em = 8; google.protobuf.Timestamp fechado_em = 9;
  string ciente_por = 10; google.protobuf.Timestamp ciente_em = 11;
  string versao_dos_parametros = 12; bool simulacao = 13;
}
```

O resto segue o mesmo padrão: `LinhaDeRitmo` (ocupação, ciclo, chegadas por minuto, rótulo
`ESTIMATIVA`), `ExplicacaoDaNegativa` (o que aconteceu, o que dizer, o que conferir,
`credencial_mascarada` como já existe em `EventoDeAcesso`), `SugestaoDeParametrizacao` (campo pelo
enum `CampoDaCatraca` já existente, atual, sugerido, evidência) e `MarcoDoReplay`. Nenhuma mensagem
devolve impressão HMAC nem `ticket_id`: a tela não precisa deles.

### 3.7 Telas (alinhadas com o 03 §2.2)

Nenhum menu novo. O que muda em cada tela:

| Tela | O que entra |
|---|---|
| Painel ao vivo | faixa de alertas (03 T2); em cada cartão de catraca, "Saúde" e "Ritmo" (03 T1, T3) |
| Lista de acessos | "Por quê?" (03 T4) |
| Catracas | coluna Saúde |
| Diagnóstico | a conta completa de cada índice; a saúde do próprio Analisador (ciclos, estouros, descartes do anel) |
| Parametrização | chip "Sugestão" (IN-07) |
| Gêmeo | chips de situação, camada Replay e Mapa de giro (03 T6, T7) |
| Prestação de contas | pós-evento (IN-09) |

Selo de origem sempre visível: `AO VIVO`, `ESTIMATIVA`, `REPLAY`, `DEMONSTRAÇÃO`.

---

## 4. Métodos

### 4.1 Princípios

1. **Comparar com as vizinhas na mesma janela antes de comparar com o passado.** O público muda a
   cada minuto (abertura, intervalo, chuva), e as catracas do mesmo portão veem o mesmo público. A
   comparação lateral cancela a demanda. A linha de base temporal (EWMA da própria catraca) é o
   recurso de quem tem uma catraca só.
2. **Contar antes de modelar.** As regras usam contagens e quantis de janelas fechadas.
3. **Toda decisão estatística tem mínimo de amostras, persistência e histerese.** Abrir um alerta
   exige 2 janelas seguidas acima do limite; fechá-lo exige 2 min abaixo de 80% do limite.
4. **A conta acompanha o alerta.** Exemplo: "esperávamos ~21 leituras em 3 min, pelo ritmo das
   vizinhas; vieram 0. Chance disso por acaso: < 0,1%".
5. **Parâmetros versionados.** O hash vai em cada alerta (I5). A calibração é feita por soak
   simulado e, depois, por evento real, nunca no meio do evento.

### 4.2 Caixa de ferramentas

| Técnica | Onde | Fórmula / regra | Por que esta |
|---|---|---|---|
| Baldes de 1 min | tudo | contagens e histogramas por (catraca, minuto) | janelas maiores = soma de baldes; reprocessável |
| Histograma de baldes fixos | latências, Δ, ciclo | limites log-espaçados (1, 2, 5, 10, 20, 50… ms; 0,25, 0,5, 1, 1,5, 2, 3, 4, 6, 8, 12 s) | soma-se entre minutos e entre catracas sem guardar amostras; quantil por interpolação no balde; determinístico |
| Mediana e MAD; z robusto | picos, latência | z = (x − mediana) / (1,4826 · MAD), com MAD mínimo de 1 contagem | não é arrastado pelo próprio pico |
| EWMA | linha de base própria | m ← m + α(x − m), α = 1 − 2^(−1/h), meia-vida h = 10 min | memória curta, uma variável por série |
| Poisson | "leitor calado" (C13) | λ = taxa esperada × janela; P(0) = e^(−λ); dispara se λ ≥ 7 (P < 0,1%) e 0 leituras | explicável em uma frase |
| Wilson / binomial | taxas (sem giro, leitura vazia, negação por motivo) | limite inferior de Wilson da catraca > limite superior das vizinhas somadas, com n ≥ 30 em cada | taxa com poucos casos não acende nada |
| Comparação de medianas | Δ liberação→giro (C5) | razão das medianas ≥ 1,5, n ≥ 30 de cada lado, em 2 janelas de 15 min seguidas | sem teste de hipótese difícil de explicar; persistência no lugar do p-valor |
| Theil–Sen | deriva do relógio | inclinação = mediana das inclinações entre pares de conferências (≥ 3) | robusto a um acerto no meio da série |
| CUSUM (Page) | mudança lenta do Δ ou da latência de recepção | S ← max(0, S + z − k), alarme em S > h (k = 0,5, h = 5 sobre z robusto) | pega "endurecendo aos poucos", que a média esconde |
| Regras de sequência | C4 (liberou e não girou), C3 (giro sem pedido) | k = 3 seguidas; janela de correlação de 10 s entre catracas (C10) | determinístico |
| Holt (suavização dupla) | previsão curta de chegadas | nível + tendência em baldes de 5 min; faixa = ± 2 · MAD dos resíduos | só como estimativa rotulada; censurado na saturação (§5, IN-04) |

### 4.3 Aquecimento e falta de dados

- Mínimo por regra (n e minutos) na §4.5. Abaixo dele, o nível é `APRENDENDO` e a tela diz
  "Poucos dados ainda (12 passagens; precisa de 30)" (03 §2.1 item 2).
- Catraca **fora de operação** (`device_status.online = 0`) não entra como vizinha.
- Catraca em **simulação** (`016:23`) não se mistura com catraca real.
- Lacunas do `health_minute` (worker morto) viram `SEM_DADOS`, nunca zero.

### 4.4 Supressão e correlação (C10)

Antes de publicar, os alertas de um mesmo ciclo passam por um agregador:
- ≥ 2 catracas caindo no mesmo minuto viram **um** "queda de rede ou energia do setor";
- giros sem pedido em ≥ 2 catracas em 10 s viram **um** "possível liberação por alarme ou
  anti-pânico";
- alerta de catraca já coberto por um de portão é suprimido e citado na evidência.

Regra determinística, testada com 4 catracas simuladas (01 C10).

### 4.5 Parâmetros iniciais (a calibrar no `NOVO-SOAK-IA-24H`)

| Regra | Janela | Mínimo | Limite | Fecha |
|---|---|---|---|---|
| Leitor calado | 3 min | λ ≥ 7, ≥ 2 vizinhas em operação | 0 leituras | 1 leitura |
| Pico de negação (por motivo) | 5 min contra 60 min | ≥ 10 negações | z robusto ≥ 4 | z < 2 por 2 min |
| Desconhecidos e base atrasada | 5 min | ≥ 10 | z ≥ 4 **e** sincronização sem sucesso há > 5 min | — |
| Comunicação | 15 min | — | ≥ 3 reconexões, ou erros de recepção com z ≥ 4 | 15 min sem queda |
| Δ liberação→giro | 15 min, 2 seguidas | n ≥ 30 por lado | razão ≥ 1,5 ou CUSUM | razão < 1,2 |
| Sem giro (taxa) | 30 min | n ≥ 30 | Wilson acima das vizinhas | — |
| Sem giro (série, C4) | — | — | 3 seguidas | 1 giro |
| Leitura vazia (taxa) | 30 min | n ≥ 50 leituras | Wilson acima das vizinhas e ≥ 5% | — |
| Relógio | ≥ 3 conferências | — | \|deriva\| > 30 s (já existe) ou \|inclinação\| > 2 s/h | — |
| Ocupação alta | 5 min | ≥ 20 ciclos | ρ ≥ 0,9 | ρ < 0,8 |

### 4.6 E o ML?

Só quando houver dado e ganho claro:

| Candidato | Pré-condição | Ganho esperado | Classificação |
|---|---|---|---|
| Previsão de falha de catraca (sobrevivência, *gradient boosting*) | incidentes rotulados (manutenção, troca de peça) de vários eventos, com `health_minute` | antecedência maior que a do CUSUM | PROPOSTA FUTURA |
| Curva de chegada por tipo de evento (regressão sobre eventos anteriores) | R7 de ≥ 5 eventos parecidos | dimensionar catracas antes de abrir (docs/14 §1) | PROPOSTA FUTURA |
| Detecção de anomalia não supervisionada (*isolation forest*) | — | **nenhum** sobre as regras: perde a explicação | descartado |

Os dados que alimentariam essas ideias saem do coletor e de `agg_minute`, sem dado pessoal. Podem
ser exportados por evento, com opt-in do cliente, sem quebrar a LGPD (§6).

---

## 5. Capacidades

Formato de cada uma: problema · como funciona · novo · risco e mitigação · classificação · teste ·
esforço. "Vizinhas" = catracas do mesmo portão (G-11) em operação, ou todas, se não houver portões.

### IN-01 Saúde por catraca (CI-01; C5, C9, C12)

- **Problema.** "Atendendo" esconde a catraca que reconecta a cada 4 min ou onde metade desiste (03
  T1).
- **Como.** Sete sinais, cada um com nível (§4.5), conta e "o que fazer":

  | Sinal | Dado | Regra |
  |---|---|---|
  | Comunicação | `health_minute.reconnects`, `recv_errors`, `recv_ms_hist` (G-04/05/06) | reconexões em 15 min; z robusto dos erros; p95 de recepção ≥ 3× a mediana das vizinhas; CUSUM da latência |
  | Giro | `agg_minute`: Δ = `passage_confirmed_at − at` (`003:64-65`), sem giro | razão das medianas, Wilson, CUSUM |
  | Leitura | leituras vazias (G-02), origens desconhecidas, desconhecidos com tamanho fora do perfil do provedor (o tamanho é calculado na leitura e só ele entra no agregado) | Wilson contra as vizinhas |
  | Relógio | `008:4-7` + G-09 | > 30 s ou inclinação > 2 s/h |
  | Configuração | versão salva ≠ aplicada (A.5/A.6) há > 2 min sem pedido na fila; firmware mudou (C9, G-10) | regra |
  | Giro sem pedido | origem 6 sem tentativa pendente (C3, G-01) | contagem por hora; nível Atenção se ≥ 3 sem explicação |
  | Laço | `loop_ms_hist` do worker (G-13) | p95 da volta ≥ 1,5 s (3× o `limiteDeEspera`, `DeviceGroupLoop.cs:75`) |

  **Índice 0–100 (só no modo técnico, como pede o 03 T1):** 100 − Σ peso × penalidade, com
  penalidade 0 (Normal), 0,5 (Atenção) ou 1 (Ação). Pesos: Comunicação 25, Giro 25, Leitura 20,
  Relógio 10, Configuração 10, Giro sem pedido 10. Sinal sem dados não penaliza e aparece como "não
  medido". O operador vê o **pior sinal** e a lista.

  **"Antes da falha":** os sinais precursores são reconexões e erros de recepção crescendo, CUSUM
  da latência e Δ subindo só numa catraca. A antecedência é medida **em simulação**, com rampas
  injetadas (`NOVO-SIM-SAU-02`). Em campo, o produto **não promete** prever, mostra a tendência.
- **Novo.** Cruza sete sinais por catraca **contra as vizinhas na mesma janela**. A DLL entrega
  eventos soltos.
- **Risco.** Falso alarme que ensina o operador a ignorar → mínimo de amostras, persistência,
  histerese e soak sem alerta (`NOVO-SOAK-IA-24H`). "Desgaste" nunca é afirmado (03 §2.4).
- **Classificação.** IMPLEMENTÁVEL AGORA. Giro, relógio e configuração já têm dado. Comunicação,
  leitura e laço dependem do coletor (I.1). "Giro sem pedido" tem premissa física DEPENDE DE BANCADA
  (T42, NOVO-HIL-GIRO-01).
- **Teste.** `NOVO-SIM-SAU-01` (03: cada sinal injetado sozinho leva só aquele sinal a Atenção em
  ≤ 2 min, 20 de 20). `NOVO-SIM-SAU-02` (novo): rampa de atraso no giro de 0 a +3 s em 60 min numa
  catraca leva a Atenção antes de o atraso chegar a +1,5 s. Unidade: cada regra com caso no limite.
- **Esforço.** M (v1 sem coletor: P).

### IN-02 Alertas e anomalias operacionais (CI-02; C3, C4, C7, C10, C13)

- **Problema.** O operador olha a fila, não a tela: precisa de poucos avisos, certos e com ação.
- **Como.** Ciclo de vida igual para todos:
  - aberto → atualizado → fechado;
  - deduplicação por (regra, catraca ou portão);
  - "ciente" com nome digitado (vai ao R8, `docs/25-relatorios-da-prestacao-de-contas.md:92-96`);
  - agregação da §4.4.

#### 5.2 Regras, dados e relé no futuro

"Relé no futuro" = poderia acionar um sinal físico pelo relé 2 (`AcionarRele2`, EI-047, na costura
e nunca chamado; 01 §5). Vale **sempre** com chave `rele2.sinal_de_alerta.<regra>` desligada, **só
em `Polling`** (01 F13: a origem 5 não diz qual relé expirou) e só depois de NOVO-HIL-REL-04/06.
Nunca é sirene ao público (01 §5.7).

| # | Regra | Dados | Conta (§4.5) | Texto ao operador | Classificação | Relé no futuro |
|---|---|---|---|---|---|---|
| A1 | Leitor calado (C13) | `agg_minute`, `device_status` | Poisson | "Catraca 05 sem leituras há 4 min; as vizinhas leram 63. Confira o leitor e o display." | AGORA | sim: sinaleiro de pista "verificar" (aviso ao orientador) |
| A2 | Pico de negação por motivo | `agg_minute` (negados por `reason`) | z robusto por motivo | "Negações por *ingresso já usado* 6× acima do normal em 5 min." | AGORA | não (é público, não pista) |
| A3 | Desconhecidos e base atrasada | idem + `sync_cursor`, `outbox`, `ObterEstado` | z + idade da sincronização | "12 códigos desconhecidos em 5 min e a base não atualiza há 9 min: pode ser lote que não chegou." | AGORA | não |
| A4 | Reuso ou compartilhamento | IN-03 | regra | "Um ingresso já usado foi apresentado em 2 catracas em 3 min (final ••42)." | AGORA | **gatilho de câmera: só com RIPD**; luz discreta ao orientador: sim |
| A5 | Liberação manual fora do padrão | `operator_command` (`009:5-20`) | > max(3, 3 × mediana das catracas) por hora; ou liberação manual **sem negação** na mesma catraca nos 60 s anteriores | "Catraca 2: 7 liberações manuais na última hora (as outras: 1 a 2); 4 sem negação antes." | AGORA | não |
| A6 | Comunicação instável (C12) | `health_minute` | §4.5 | "Catraca 03 reconectou 4 vezes em 15 min. Confira cabo e porta do switch." | AGORA (com I.1) | não (sem comunicação o relé não chega) |
| A7 | Relógio derivando | G-09 | Theil–Sen | "O relógio da catraca 02 adianta cerca de 3 s por hora." | AGORA | não |
| A8 | Configuração não aplicada / equipamento trocado (C9) | `device_status`, G-10 | regra | "A catraca 04 não está com a configuração salva." / "Firmware mudou: confira o sentido no Mapa de giro." | AGORA | não |
| A9 | Liberou e não girou em série (C4) | tentativas sem giro + G-14 | 3 seguidas | "Catraca 3: 3 liberações seguidas sem giro. Verifique o braço; se foi reinstalada, confira o sentido." | AGORA | sim: sinaleiro "pista com problema" |
| A10 | Giro sem pedido (C3) e correlação (C10) | G-01 | regra + §4.4 | "Catraca 1 girou sem liberação às 19:42 (sem explicação)." / "4 catracas giraram sem liberação em 3 s: possível alarme." | AGORA (lógica); DEPENDE DE BANCADA (T42, NOVO-HIL-GIRO-01/03) | sim: aviso discreto ao orientador (01 R3); câmera só com RIPD |
| A11 | Urna cheia (C7) | origem 20 (G-01) | regra | "Urna da catraca 01 cheia. Esvazie ou oriente para outra." | AGORA (simulador); DEPENDE DE BANCADA (T33) | sim: sinaleiro "urna cheia" |
| A12 | Queda simultânea (C10b) | `health_minute` | ≥ 2 catracas no mesmo minuto | "Catracas 1, 2 e 4 caíram juntas: rede ou energia do setor." | AGORA | não |
| A13 | Liberação recusada pela catraca | G-08 | ≥ 2 em 10 min | "A catraca 2 recusou 3 pedidos de liberação (retorno 3)." | AGORA (com I.1); retorno real DEPENDE DE BANCADA | não |
| A14 | Giro no sentido inesperado | 017: `release_function`, `turn_complement`, `turn_check` | hoje: última conferência `ao_contrario` para a função em uso, ou "saídas" contadas numa catraca mapeada só para entrada. Com T14: complemento ≠ sentido pedido | "Catraca 3: a última conferência registrou giro ao contrário do esperado para *Entrada*." | AGORA (conferência); DEPENDE DA TOPDATA (T14) para o sinal por giro | sim: sinaleiro "pista invertida" |
| A15 | Ocupação alta e redistribuição | IN-04 | ρ ≥ 0,9 | "Catraca 3 sem folga há 6 min; a 4 está a 45%. Oriente parte da fila para a 4." | AGORA (estimativa) | não (é orientação de equipe) |

- **Novo.** Alertas por comparação entre catracas, com a conta e o "o que fazer". A ponte para o
  relé 2 transforma a catraca num sinaleiro de pista sem hardware novo.
- **Risco.** Tempestade de alertas → agregação (§4.4), teto de 3 na faixa (03 T2), soak. Câmera
  disparada por suspeita → dado pessoal: só com RIPD e decisão do dono, e o gatilho nunca leva
  código.
- **Classificação.** Por regra, na tabela.
- **Teste.** `NOVO-SIM-ALR-01` (03: caso que dispara e caso no limite que não dispara, por regra).
  `NOVO-SOAK-IA-24H` (0 alertas em 10 catracas saudáveis por 24 h simuladas). `NOVO-REST-IA-01`
  (reinício não duplica nem perde alerta aberto). Relé: NOVO-HIL-REL-04/06 (01 §5.4).
- **Esforço.** M.

### IN-03 Reuso e compartilhamento de print (CI-05)

- **Problema.** Um print de QR compartilhado, ou o mesmo ingresso tentado em portões diferentes.
- **Como.**
  - **Ingresso conhecido:** a correlação é pelo `ticket_id` da tentativa (`003:56`), um UUID local.
    **Não precisa do código nem do HMAC.**
  - **Código desconhecido** (`ticket_id` nulo): o Analisador calcula em memória a impressão
    `ImpressaoDeCodigo` (`src/Access.Domain/Credentials/ImpressaoDeCodigo.cs:30`) com a chave que
    o serviço já guarda (`Edge.Supervisor/ChaveDaImpressao.cs`). Só ela vai para a evidência, com
    o id da chave. A função que lê o código é **uma só**, isolada e testada.
  - **Regras:**
    - (a) negação `UsosEsgotados` ou `EmIntervaloDeReuso` do mesmo ingresso em ≥ 2 catracas em
      ≤ 5 min;
    - (b) uso consumido numa catraca e nova apresentação em outra catraca do **mesmo sentido
      lógico** (017) em ≤ 2 min;
    - (c) o mesmo desconhecido tentado ≥ 5 vezes em ≥ 2 catracas em 10 min ("print de código
      inválido circulando").
  - Passes de vários usos (`max_uses > 1`, `003:23`) e cartões reutilizáveis (`004:5-9`) entram
    só pela regra (a).
- **Novo.** É o padrão do R6 (`docs/25-relatorios-da-prestacao-de-contas.md:77-84`) em tempo
  real, sem código em claro.
- **Risco.** Acusar quem voltou ao portão por engano → texto "possível", nenhuma ação automática,
  mascarado na tela. Bloquear o ingresso fica com o operador (cadastro B.6).
- **Classificação.** IMPLEMENTÁVEL AGORA (a chave já está no serviço; fora do Windows, a regra (c)
  fica desligada e diz por quê).
- **Teste.** `NOVO-SIM-RUSO-01` (03: 20 de 20 reusos; 0 alertas com o passe de 2 usos
  `2000000004`). `NOVO-SEC-IA-02`: `telemetria.db` varrida com os códigos de teste dá 0
  ocorrências; a impressão só aparece com prefixo e id da chave.
- **Esforço.** P.

### IN-04 Ritmo, ocupação e recomendação (CI-03) · IN-04b Tempo para escoar

- **Problema.** No pico, onde pôr o orientador, quando abrir mais uma catraca e quanto falta
  (docs/14 §1: o déficit no pico é de catracas, não de software).
- **Como.**
  - *Chegadas* λ = leituras/min (tentativas, inclusive negadas, + leituras vazias).
  - *Ciclo* c = intervalo entre leituras seguidas da mesma catraca **quando ela está ocupada**
    (próxima leitura em até 2 × mediana do ciclo). Mediana em 15 min.
  - *Capacidade* μ = 60/c.
  - *Ocupação* ρ = fração da janela em ciclo ocupado (Σ min(intervalo, c) / janela).
  - **Honestidade estrutural:** na saturação, a catraca lê no máximo μ. A demanda excedente **não
    aparece**: a fila não é observada, e ρ ≈ 1 é o sinal dela ("há gente esperando; não sabemos
    quantos").
  - *Recomendações:* (i) ρ_A ≥ 0,9 e ρ_B ≤ 0,6 no mesmo portão → "oriente parte da fila para B";
    (ii) ρ médio do portão ≥ 0,85 por 10 min → "abra mais uma catraca neste portão, se houver";
    (iii) ciclo por origem de leitura (`reader_origin`: QR 21 × frente 2 × urna 3, `010:15`) com
    diferença ≥ 40% → "dedique a catraca 2 ao QR (ciclo 3,1 s contra 5,4 s do cartão)".
  - *Previsão curta* (Holt, 30 min, por portão), marcada "censurada" quando ρ ≥ 0,9.
  - **IN-04b:** pendentes = ingressos `valido` com `used_count < max_uses` e válidos no dia
    (`003:21-26`). T_escoar ≥ pendentes / Σμ. Texto: "Se todos os 8.200 ingressos ainda não usados
    chegassem agora, as 4 catracas levariam pelo menos 2 h 50 no ritmo de hoje." É um limite
    superior de demanda: não-comparecimento é desconhecido, e o texto diz isso.
- **Novo.** Mede a ocupação real, em vez de inventar fila, e usa a demanda **conhecida** (a base
  sabe o que foi vendido) para dar antecedência. É a aritmética do docs/14 ao vivo.
- **Risco.** Ler estimativa como medida → rótulo `ESTIMATIVA`, nunca "pessoas na fila" (03 §2.4).
  Portões não configurados → cada catraca é o seu portão.
- **Classificação.** IMPLEMENTÁVEL AGORA (como estimativa). Validação do ciclo contra o cronômetro:
  DEPENDE DE BANCADA (`NOVO-INT-FLX-01`).
- **Teste.** `NOVO-SIM-FLX-01` (03: erro do % ≤ 20% depois de 30 ciclos). `NOVO-SIM-RIT-02`
  (novo): chegadas de Poisson com semente; com a catraca 3 saturada e a 4 ociosa, recomendação (i)
  em ≤ 5 min, e nenhuma com as duas abaixo de 0,8. Unidade de IN-04b com base fabricada.
- **Esforço.** M.

### IN-05 Fluxo por sentido lógico e lotação estimada (novo; sobre o Mapa de giro)

- **Problema.** O dono decidiu que entrada e saída são nomenclatura do sistema (D9, `017:1-2`). O
  operador quer entradas, saídas e quantos estão dentro.
- **Como.**
  - Para cada giro confirmado, o rótulo vem de `ticket_use_attempt.counted_as` (`017:137`); nulo
    conta como entrada, como sempre (`017:129-131`).
  - Liberações manuais contam pelo mapa da origem `manual` (`017:18`).
  - Por catraca, portão e minuto: entradas, saídas e giros sem sentido conhecido (giro sem pedido,
    dois sentidos EI-045).
  - Lotação estimada = Σ entradas confirmadas − Σ saídas confirmadas desde a abertura. Mostrada
    **só** se houver ao menos uma catraca mapeada para saída, e sempre com "giros sem sentido
    conhecido: N" ao lado.
  - Sinal A14 ("sentido inesperado") na tabela da §5.2.
- **Novo.** O fluxo deixa de ser "liberações" e vira **entradas e saídas do evento**, com o sentido
  físico preservado para auditoria (`release_function`, `turn_complement`).
- **Risco.** Lotação errada usada para segurança → `ESTIMATIVA`, nunca substitui contagem oficial
  (`docs/25-relatorios-da-prestacao-de-contas.md:135-137`: "sem contagem de saída, o sistema informa
  entradas, não lotação").
- **Classificação.** IMPLEMENTÁVEL AGORA depois do merge da 017. "Sentido relatado pela catraca":
  DEPENDE DA TOPDATA (T14).
- **Teste.** `NOVO-SIM-SEN-01` (novo): mapa com catraca 4 = saída; 10 entradas e 3 saídas
  simuladas dão lotação 7; um giro sem pedido aparece como "sem sentido" e não mexe na lotação.
  `NOVO-SIM-MAP-01` (03).
- **Esforço.** P.

### IN-06 Por que negou (CI-04)

- **Problema.** "Leu e não liberou" é a dúvida nº 2 do público (03 §3.2 R3).
- **Como.** Função pura sobre a tentativa e o contexto: o último uso do mesmo `ticket_id` (catraca,
  hora, giro sim ou não), o intervalo de reuso (`004:8`, `last_used_epoch` `004:18`), "só na urna"
  (`005:10`), o tipo inativo (`011:25-55`), a idade da sincronização e, depois de I.1, a "liberação
  recusada" (G-08). Textos do 03 T4. A frase curta de hoje continua igual
  (`AcompanhamentoDaOperacao.cs:96-118`); o "Por quê?" é a RPC `ExplicarNegativa(evento_id)`.
  Também dá o **motivo agregado**: negações por motivo e por catraca na janela, com a variação
  (A2).
- **Novo.** Explica com o contexto da base, não só com o código do motivo.
- **Risco.** Oráculo para fraude → a explicação é para o operador, nunca para o display (o display
  por motivo é a C6 do 01, com textos genéricos nos motivos sensíveis).
- **Classificação.** IMPLEMENTÁVEL AGORA (sem tocar no worker).
- **Teste.** `NOVO-SIM-NEG-01` (03: 100% dos motivos com texto próprio; catraca e hora certas do uso
  anterior; nenhum código em claro).
- **Esforço.** P.

### IN-07 Sugestão de parametrização (novo)

- **Problema.** Hoje o tempo de acionamento é chute (5 s na bancada) e o leitor ruim só aparece na
  reclamação.
- **Como.** Três sugestões, sempre com a evidência, **nunca aplicadas**:
  - **P1 Tempo do relé 1 × giro medido.** Distribuição de Δ (liberação→giro) e contagem de "giro
    tardio" (origem 6 até 3 s depois da origem 5: C3(a), G-01/G-14):
    - p95(Δ) + 1 s < tempo configurado e nenhum giro tardio em 200 liberações → "pode reduzir para
      X s" (a pista libera mais cedo depois de uma desistência);
    - giros tardios ≥ 2% → "aumente 1 s".
    - Sempre dentro da regra 11 (tempo < espera do giro de 8 s, `docs/34-estudo-modulo-catraca.md:254`,
      `:271`) e da faixa 1–50 (`012:18`).
  - **P1b Janela real do relé (diagnóstico).** A janela medida é (origem 5 recebida − `at` da
    tentativa liberada). Se a mediana diferir do configurado em > 1,5 s, o texto é "a catraca não
    parece estar com o tempo configurado". É um cheque independente da versão aplicada (A.5) e
    insumo para T8/T13. DEPENDE DE BANCADA para calibrar a margem da volta do laço.
  - **P2 Leitor.** Leituras vazias e desconhecidos de tamanho fora do perfil, concentrados num
    leitor (por `reader_origin`) → "limpe/reposicione o leitor 1 da catraca 2". Se persistir:
    "confira o tipo de leitor (aguarda confirmação T25)". **Não sugere** valor que a A.6 recusa
    (campos "aguardando confirmação", `docs/34-estudo-modulo-catraca.md:576`, A.6).
  - **P3 Display.** Muitas `ForaDaUrna` numa catraca → sugerir o texto por motivo da C6 do 01, ou
    uma mensagem padrão "Cartao: use a urna" (≤ 32, `012:21`).
  - **Mecanismo:** `ObterSugestoes(inner)`, e o chip "Sugestão" na Parametrização. "Usar sugestão"
    só **preenche** o formulário. O fluxo de sempre continua: "o que muda (atual → novo)", salvar
    com nome, aplicar em dois passos. `RegistrarDestinoDaSugestao` grava se foi usada ou descartada
    e por quem.
- **Novo.** Fecha o ciclo medir → sugerir → o operador aplica → medir de novo (a próxima janela
  mostra o efeito).
- **Risco.** Mudança em pleno pico → a sugestão diz "aplique fora do pico" quando ρ ≥ 0,7 e nunca
  aparece com uma liberação em curso. Sugestão errada → mínimo de 200 liberações e o efeito
  mostrado depois.
- **Classificação.** P1, P2, P3: IMPLEMENTÁVEL AGORA (com I.1). P1b: DEPENDE DE BANCADA.
- **Teste.** `NOVO-SIM-SUG-01` (novo): simulador com Δ ~ lognormal (mediana 2 s) e relé 5 s
  sugere 4 s; com 3% de giros 0,5–2 s depois da origem 5, sugere 6 s; nenhuma sugestão em campo
  "aguardando confirmação"; usar a sugestão não grava nada sem o "Salvar". `NOVO-ARQ-IA-03`.
- **Esforço.** M.

### IN-08 Gêmeo vivo e replay (CI-07)

- **Problema.** "A pessoa diz que passou e o sistema diz que não."
- **Como.**
  - **Ao vivo:** chips de saúde, alerta e ritmo sobre a peça (`docs/34-estudo-modulo-catraca.md:459-462`).
  - **Replay:** linha do tempo montada de tentativas, sinais (G-01), transições (G-07), comandos e
    lacunas (minutos sem `health_minute` = "sem notícia").
  - Narração por modelo de texto fixo, só com o que existe.
  - Invariante **replay ⊆ gravado**: todo marco aponta para uma linha.
- **Novo.** A catraca de 20 anos ganha caixa-preta reproduzível.
- **Risco.** Preencher lacuna com palpite → a lacuna é dita em texto (03 T6).
- **Classificação.** IMPLEMENTÁVEL AGORA (parcial, só tentativas e comandos); completo com I.1; tela
  depende do P3 do gêmeo (Etapa C).
- **Teste.** `NOVO-SIM-RPL-01` (03). Propriedade: para 1.000 intervalos sorteados com semente, todo
  marco tem linha de origem e a ordem é a de `received_at`/`at`.
- **Esforço.** M/G.

### IN-09 Relatório pós-evento com achados (novo)

- **Problema.** Prestação de contas defensável e lições para o próximo evento. R1–R8 ainda estão
  marcados como ausentes (commit `5ce45a0`; `docs/29-o-que-falta.md`).
- **Como.** Sobre o corte com hash (`docs/25-relatorios-da-prestacao-de-contas.md:108-110`), achados
  **determinísticos**, cada um com o número e a regra:
  - maior pico de 15 min por portão;
  - catraca mais lenta (mediana de Δ) e mais ociosa;
  - desperdício = Σ liberações sem giro × tempo do relé (docs/14:76-79);
  - disponibilidade por catraca (G-06);
  - alertas, tempo até "ciente" e quem marcou;
  - motivos de negação e a variação no dia;
  - **dimensionamento para o próximo evento** = pico de chegadas por hora ÷ μ medido
    (docs/14 §1, "divida o exigido no pico por 800").

  Os textos saem de modelos fixos, sem LLM. Categorias sensíveis (PCD etc.) aparecem só agregadas,
  com supressão de célula < 5 (`docs/34-anexos/03-cartoes-e-lgpd.md:693`).
- **Novo.** O R8 deixa de ser lista e vira diagnóstico do evento.
- **Risco.** Achado que parece acusação (catraca "ruim", operador que "liberou demais") → linguagem
  factual; nome de operador só no R6, como já previsto.
- **Classificação.** IMPLEMENTÁVEL AGORA (depende de R1–R8 na tela, docs/29).
- **Teste.** `NOVO-SIM-POS-01` (novo): roteiro de demo com números fixos gera os mesmos achados e o
  mesmo hash em duas execuções (`NOVO-DET-IA-01`).
- **Esforço.** M.

### IN-10 Check-up como linha de base (CI-06; C8)

- **O que este papel acrescenta ao 01 C8 e ao 03 T5:** o resultado do check-up pré-abertura
  (latência do `Ping`, relógio, firmware, versão) vira a **referência da catraca naquele dia**. A
  saúde passa a comparar "agora × no check-up" além de "agora × vizinhas", o que resolve o caso de
  uma catraca só.
- **Classificação.** IMPLEMENTÁVEL AGORA (simulador); passos físicos DEPENDEM DE BANCADA.
- **Esforço.** P (sobre a C8).

### IN-11 Assistente do operador com LLM

- **Problema.** Perguntas livres ("por que a 3 está lenta?", "quantos entraram pelo Norte depois
  das 20h?").
- **Avaliação rigorosa.**

  | Requisito do brief | Como seria atendido | Situação |
  |---|---|---|
  | Local-first | modelo pequeno rodando no PC | o PC do evento tem DLL x86 em thread única por worker e um laço de 500 ms. Um LLM local disputa CPU e memória com os workers: risco direto ao I4. Exige teto de CPU (Job Object, prioridade *Idle*) e `NOVO-LOAD-IA-01` com o modelo respondendo. **Não provado** |
  | Sem dado pessoal | o modelo só vê as **mesmas RPCs** da §3.6 (agregados, explicações, alertas), por *tool-calling* somente leitura; nenhuma ferramenta devolve código, máscara, `ticket_id` ou nome | desenhável |
  | Fora do caminho do giro | processo separado, sem acesso à base, sem ferramenta de comando | desenhável |
  | Opt-in | chave desligada; nuvem só com segunda chave e aviso do que sai (agregados) | desenhável |
  | Verdade | toda resposta cita os números das ferramentas; sem número, "não sei"; avaliação com conjunto de perguntas-ouro e respostas proibidas (sobre pessoas, sobre liberar) | exige suíte própria |

- **Agora, no lugar dele:** **perguntas prontas** determinísticas na tela de Diagnóstico ("Por que
  a catraca N está em Atenção?", "Onde está a fila?", "O que mudou na última hora?"). Cada uma é a
  composição de RPCs da §3.6 com resposta em modelo de texto. É o mesmo valor, sem risco, e vira a
  base de avaliação do LLM no futuro.
- **Classificação.** LLM: **PROPOSTA FUTURA**. Perguntas prontas: IMPLEMENTÁVEL AGORA.
- **Teste (futuro).** `NOVO-LLM-IA-01`:
  - 200 perguntas-ouro com ≥ 95% de respostas cujos números batem com as RPCs;
  - 0 respostas com código ou nome;
  - 0 ferramentas de escrita;
  - `NOVO-LOAD-IA-01` com o modelo em uso contínuo.
- **Esforço.** G.

### IN-12 Conciliação de testemunhas (CI-08; C14)

- Tentativas × giros × bilhetes coletados (`015:21-44`, por HMAC e minuto) × giros sem pedido. É do
  01 C14. A camada só fornece a reconciliação e o texto. **DEPENDE DE BANCADA** (T41, T35, T37).
  Esforço M.

### Descartes e rebaixamentos

| Ideia | Motivo |
|---|---|
| Bloquear automaticamente o ingresso suspeito de compartilhamento | é inteligência que **decide** (nega o próximo uso de alguém) com base em correlação. O operador bloqueia, com motivo (B.6) |
| "Fila de N pessoas" ou "espera de X min" exatos | a fila não é observada; na saturação a demanda é censurada (IN-04). Fica a ocupação e o limite de IN-04b |
| Índice de saúde como número para o operador | compara o incomparável; só no modo técnico (03 T1) |
| Desgaste mecânico absoluto | Δ é comportamento humano + volta do laço; só comparação relativa e persistente (01 C5) |
| ML não supervisionado para anomalias | perde a explicação sem ganho demonstrado (§4.6) |
| Rearme automático do leitor calado | é ação sobre a catraca: fica com o 01 C13, atrás de chave e bancada (HIL-ERR-02) |

---

## 6. LGPD e segurança

| Tema | Pode | Não pode |
|---|---|---|
| Código do ingresso | o Analisador lê `ticket_id` e motivo; para código **desconhecido**, calcula em memória a impressão HMAC da B.1 com a chave do cofre (DPAPI, ADR-0014) | selecionar `qr_normalized` em qualquer outra consulta; gravar código, máscara ou impressão em `agg_minute`/`health_minute`/`device_signal`; levar código ao registro (o redator já existe; o coletor descarta `RawCardData` antes de enfileirar) |
| Leitura vazia e tamanho | gravar que houve e o tamanho | gravar o conteúdo |
| Titular | — | não existe e continua não existindo (`docs/34-estudo-modulo-catraca.md:413`) |
| Categorias sensíveis (PCD, idoso, criança) | agregadas, com supressão de célula < 5 | ligadas a ingresso em alerta ou replay |
| Operador | nome digitado em "ciente" e no destino da sugestão (auditoria, como `009:12`) | ranking de operadores fora do R6 |
| Reuso (IN-03) | `ticket_id` na evidência, apagado no prazo; tela com a máscara de sempre | expor `ticket_id` ou impressão no contrato (§3.6) |
| Telemetria | `telemetria.db` sem dado pessoal **por construção**: pode ir ao suporte ou à Topdata (check-up, saúde) com opt-in | misturar com `acesso.db` em exportação |
| Câmera pelo relé (A4, A10) | só com RIPD, base legal, sinalização no local e decisão do dono | disparo por "suspeita" sem essas condições |
| LLM | agregados, opt-in, só leitura | qualquer dado de pessoa; ferramenta de comando |
| Retenção | sinais e amostras: com o evento (proposta 1 ano); alertas com `ticket_id`: corte + 90 dias e depois só contagem (`docs/34-anexos/03-cartoes-e-lgpd.md:751`) | expurgo sem registro |
| Segurança do canal | RPCs novas atrás do mesmo pipe e das mesmas permissões (`SegurancaLocal.cs`); chaves sem tela | RPC que mude configuração ou comande catraca |

Testes:
- `NOVO-SEC-IA-01` (03): varredura de todas as saídas com códigos de teste conhecidos.
- `NOVO-SEC-IA-02` (novo): `telemetria.db` inteira e as respostas das RPCs novas com 0 ocorrências;
  teste por reflexão de que só a função de impressão de IN-03 referencia a coluna `qr_normalized`.
- `NOVO-CTR-IA-01`: palavras proibidas no proto.

---

## 7. Como testar de forma determinística

1. **Relógio injetado em tudo.** `TimeProvider` no Analisador e no coletor. O simulador já tem
   relógio injetável (03 §4.3, `InnerSimulator.cs:27-31`), e o modo demo precisa do relógio do
   serviço também injetável.
2. **Funções puras com base fabricada.** Cada regra recebe linhas montadas em memória. Casos no
   limite: λ = 6,9 × 7; n = 29 × 30; razão 1,49 × 1,5.
3. **Gerador de cenários com semente** (ganchos do 03 §4.3):
   - chegadas de Poisson por catraca;
   - atraso do giro lognormal;
   - probabilidade de desistência;
   - intermitência de comunicação com período;
   - rampa de relógio (s/h);
   - taxa de leitura vazia;
   - roteiro de reuso;
   - giro sem pedido;
   - mapa de sentido.

   Cada cenário tem o resultado esperado escrito à mão.
4. **Testes que guardam a operação** (03 §5.1): `NOVO-LOAD-IA-01`, `NOVO-CHAOS-IA-01` (com
   `telemetria.db` bloqueada por outro processo e o Analisador lançando exceção), `NOVO-ARQ-IA-01`,
   `NOVO-SOAK-IA-24H`, `NOVO-SEC-IA-01`, `NOVO-UX-IA-01`.
5. **Novos neste relatório:**

   | Id | Prova |
   |---|---|
   | `NOVO-ARQ-IA-02` | o Analisador só abre `acesso.db` com `Mode=ReadOnly`; uma tentativa de escrita falha no teste |
   | `NOVO-ARQ-IA-03` | nenhuma RPC nova escreve em `device_config`, `ticket`, `operator_command` (varredura das instruções SQL do projeto + teste de integração) |
   | `NOVO-DET-IA-01` | duas execuções do mesmo cenário dão `alert`, `suggestion` e `insight` idênticos byte a byte (fora os ids UUIDv7, gerados com relógio e semente fixos) |
   | `NOVO-REST-IA-01` | matar o serviço no meio de um alerta aberto e subir de novo: o alerta continua o mesmo (mesma chave), sem duplicar |
   | `NOVO-PERF-IA-01` | ciclos de 1 s e 60 s dentro do orçamento (§3.4) com 20 catracas e 30 mil tentativas |
   | `NOVO-COL-IA-01` | coletor: anel cheio descarta e conta; a contagem aparece em `health_minute.dropped`; sem `--telemetria`, a sequência nativa é idêntica byte a byte à de hoje (padrão dos testes congelados da A.1) |
   | `NOVO-SIM-SAU-02`, `NOVO-SIM-RIT-02`, `NOVO-SIM-SEN-01`, `NOVO-SIM-SUG-01`, `NOVO-SIM-POS-01` | descritos em cada capacidade |
   | `NOVO-CTR-IA-01` | contrato |

6. **Bancada** (linhas novas no docs/21, do 01 e do 03): `NOVO-HIL-SAU-01`, `NOVO-HIL-ALR-01`,
   `NOVO-INT-FLX-01`, `NOVO-HIL-GIRO-01/03`, `NOVO-HIL-REL-04/06`, mais `NOVO-HIL-RELE-01` (novo,
   P1b): 20 liberações sem giro com o relé em 3, 5 e 8 s; a janela medida fica a ±(volta do laço +
   0,5 s) do configurado.

---

## 8. Ranking: as 10 melhores para implementar já

| # | O quê | Por que agora | Classificação | Esforço |
|---|---|---|---|---|
| 1 | **Fundação e guardas** (I.0): `Access.Inteligencia`, Analisador vazio, `telemetria.db`, chaves, `NOVO-ARQ-IA-01/02`, `NOVO-CHAOS-IA-01` | sem as guardas nada pode ser ligado | IMPLEMENTÁVEL AGORA | M |
| 2 | **IN-06 Por que negou** | maior valor por esforço; não toca no worker; responde à reclamação R3 | IMPLEMENTÁVEL AGORA | P |
| 3 | **Coletor mínimo** (G-01…G-14; C2 + C12 + F12) | matéria-prima de saúde, alertas, replay e sugestões | IMPLEMENTÁVEL AGORA | M |
| 4 | **IN-01 Saúde por catraca** | o cartão que mais muda o dia do operador | IMPLEMENTÁVEL AGORA | M |
| 5 | **IN-02 Alertas A1–A3, A5–A9, A12** com ciclo de vida e "ciente" | poucos, certos, com ação; alimenta o R8 | IMPLEMENTÁVEL AGORA | M |
| 6 | **IN-04 Ritmo, ocupação e redistribuição + IN-04b** | é onde o evento real dói (docs/14) | IMPLEMENTÁVEL AGORA (estimativa) | M |
| 7 | **A10 Giro sem pedido + correlação** (C3 + C10) | segurança com dado que hoje se perde | IMPLEMENTÁVEL AGORA (premissa: bancada) | P |
| 8 | **IN-03 Reuso e compartilhamento** | fraude típica de evento, sem código em claro | IMPLEMENTÁVEL AGORA | P |
| 9 | **IN-07 P1 Tempo do relé × giro medido** | primeira "parametrização inteligente" e demonstrável à Topdata | IMPLEMENTÁVEL AGORA (com I.1) | P |
| 10 | **IN-05 Fluxo por sentido lógico e A14** | pedido do dono; barato sobre a 017 | IMPLEMENTÁVEL AGORA (depois do merge da 017) | P |

**Logo depois:** IN-09 pós-evento (precisa de R1–R8 na tela); IN-08 replay completo (Etapa C, P3);
IN-07 P2/P3; perguntas prontas (IN-11 alternativa); A11 urna cheia e A13 liberação recusada
(bancada); IN-10 linha de base do check-up (com a C8).

**Para depois:**
- sinais por relé 2 (NOVO-HIL-REL-04/06, chave por regra);
- IN-07 P1b (bancada);
- A14 por giro (T14);
- IN-12 conciliação (T41, T35, T37);
- LLM (PROPOSTA FUTURA);
- ML de falha e curva de chegada entre eventos (PROPOSTA FUTURA, §4.6).

---

## 9. Fatiamento em etapas (Etapa I)

Cada etapa é um PR com testes. "Pronto" = critério de aceite verde e docs/34 §11 atualizado. A
camada fica **desligada por padrão** até a I.11.

| Etapa | Entrega | Depende de | Testes que fecham | Classificação | Esforço |
|---|---|---|---|---|---|
| **I.0 Fundação** | projeto `Access.Inteligencia` (BCL); `AnalisadorDaOperacao` (`BackgroundService`) com orçamento e prioridade baixa; `telemetria.db` + migrador `T001`; chaves da §3.5; Diagnóstico mostra a saúde do Analisador | — | `NOVO-ARQ-IA-01/02/03`, `NOVO-CHAOS-IA-01` (com Analisador que trava ou lança), `NOVO-CTR-IA-01` | AGORA | M |
| **I.1 Coletor mínimo** | `ColetorDeTelemetria` no worker (anel, descarga entre voltas, `--telemetria`); G-01…G-10, G-13, G-14; `MetricasDoEdge` alimentado pelo mesmo coletor (F12) | I.0 | `NOVO-COL-IA-01`, `NOVO-LOAD-IA-01` (coletor ligado × desligado), `NOVO-SEC-IA-02` | AGORA | M |
| **I.1b Prazo real do giro (C1, do 01)** | correção do laço (fora desta camada), em paralelo | — | testes do 01 C1; NOVO-HIL-GIRO-04 | AGORA | P |
| **I.2 Por que negou** | `ExplicarNegativa`; "Por quê?" na lista; motivo agregado | I.0 | `NOVO-SIM-NEG-01` | AGORA | P |
| **I.3 Saúde v1** | sinais Giro, Relógio, Configuração (só com o que já é gravado) + `agg_minute`; cartão e "Por quê?" | I.0 | `NOVO-SIM-SAU-01` (só esses sinais), `NOVO-DET-IA-01`, `NOVO-PERF-IA-01` | AGORA | M |
| **I.4 Alertas v1** | ciclo de vida, "ciente", agregação §4.4; A1, A2, A3, A5, A7, A8, A9, A15 | I.3 | `NOVO-SIM-ALR-01`, `NOVO-REST-IA-01` | AGORA | M |
| **I.5 Saúde v2 e alertas v2** | sinais Comunicação, Leitura, Laço e Giro sem pedido; A6, A10, A12, A13 | I.1, I.4 | `NOVO-SIM-SAU-01` completo, `NOVO-SIM-SAU-02`; premissas: NOVO-HIL-GIRO-01/03 | AGORA (lógica) / BANCADA (premissas) | M |
| **I.6 Ritmo e portões** | G-11; IN-04 e IN-04b; recomendações (i)–(iii) | I.0 | `NOVO-SIM-FLX-01`, `NOVO-SIM-RIT-02`; bancada `NOVO-INT-FLX-01` | AGORA | M |
| **I.7 Reuso** | IN-03 e A4 | I.4 | `NOVO-SIM-RUSO-01`, `NOVO-SEC-IA-02` | AGORA | P |
| **I.8 Sentido** | IN-05 e A14 (conferência) | merge da 017 (`mapa-de-giro`), I.4 | `NOVO-SIM-SEN-01`, `NOVO-SIM-MAP-01` | AGORA após a 017 | P |
| **I.9 Sugestões** | IN-07 P1–P3, chip na A.6, `RegistrarDestinoDaSugestao` | I.1, I.3 | `NOVO-SIM-SUG-01`, `NOVO-ARQ-IA-03`; P1b: `NOVO-HIL-RELE-01` | AGORA / BANCADA (P1b) | M |
| **I.10 Pós-evento e replay** | IN-09 sobre R1–R8; IN-08 com sinais e transições; perguntas prontas | I.5, I.6; R1–R8 (docs/29); P3 do gêmeo (Etapa C) | `NOVO-SIM-POS-01`, `NOVO-SIM-RPL-01`, `NOVO-DET-IA-01` | AGORA | M/G |
| **I.11 Calibração e liga por padrão** | `NOVO-SOAK-IA-24H`; parâmetros versionados; `NOVO-LOAD-IA-01` com tudo ligado; decisão registrada de ligar `inteligencia.ligada` e `inteligencia.coletor` por padrão | I.1–I.10 | `NOVO-SOAK-IA-24H` (0 alertas, 0 catracas fora de Normal), `NOVO-LOAD-IA-01` | AGORA | M |
| I.12+ (futuro) | sinais por relé 2 (regra a regra); IN-12; LLM; ML | bancada de relés; T14, T41; dados de vários eventos | NOVO-HIL-REL-04/06; `NOVO-LLM-IA-01` | BANCADA / TOPDATA / FUTURA | — |

**Caminho crítico para a demonstração à Topdata** (cenas 3, 4 e 5 do 03 §4.2): I.0 → I.2 → I.3 →
I.4 → I.6, com os ganchos do simulador (03 §4.3) em paralelo. A I.1 entra antes da cena 5 completa
(comunicação) e antes da 7 (check-up como linha de base).

---

## 10. Pendências e perguntas que afetam esta camada

| Pendência | Afeta | Onde se resolve |
|---|---|---|
| T14 (`Complemento` da origem 6 traz o sentido? o da origem 5 diz o relé?) | A14 por giro; relé 2 sem colidir com a janela do giro | NOVO-HIL-GIRO-02, NOVO-HIL-REL-06 |
| T42 (botoeira, master, incêndio geram origem 6?) | A10 | NOVO-HIL-GIRO-01/03 |
| T2 (retorno de "sem eventos"; cabo puxado) | sinal Comunicação sem falso alarme | HIL-EVT-01 |
| T33 (quem emite origem 6/20) | A9, A11 | B-07, B-08 |
| T8, T13 (tempo do relé, padrões da DLL) | IN-07 P1b | NOVO-HIL-DIR-09, NOVO-HIL-CFG-10 |
| T25 (tipo de leitor 5 × 8) | IN-07 P2 | NOVO-HIL-QR-02 |
| D7 (retenção, com o jurídico) | prazos da §6 | dono + jurídico |
| Câmera pelo relé | A4, A10 | RIPD + decisão do dono |
| Merge da 017 (`mapa-de-giro`) e numeração de migrações | IN-05 | a branch principal |
