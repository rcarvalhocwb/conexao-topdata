# 06 — Plano de testes e dimensionamento

## 1. Dimensionamento: público total ≠ simultaneidade

50.000 pessoas não é o número que dimensiona o sistema. O que dimensiona é **quantas
passam por minuto no pico**.

### Fórmulas da calculadora

```
vazão_média        = público / janela_entrada_min
pico_por_minuto    = público × %_no_pico / janela_pico_min
capacidade_gate    = 60 / tempo_p95_por_passagem_seg          [passagens/min]
capacidade_efetiva = capacidade_gate × (1 − indisponibilidade) × (1 − inspeção_manual)
gates_necessários  = ⌈ pico_por_minuto / capacidade_efetiva ⌉
déficit            = gates_necessários − gates_ativos
```

### Exemplo trabalhado — **cálculo, não promessa**

| Entrada | Valor |
|---|---|
| Público | 50.000 |
| Janela de entrada | 120 min |
| Percentual no pico | 60% |
| Janela do pico | 30 min |
| Tempo p95 por passagem | 4 s |
| Indisponibilidade | 10% |
| Inspeção manual | 5% |

```
vazão_média        = 50.000 / 120                    = 417 /min
pico_por_minuto    = 50.000 × 0,60 / 30              = 1.000 /min
capacidade_gate    = 60 / 4                          = 15 /min por gate
capacidade_efetiva = 15 × 0,90 × 0,95                = 12,8 /min por gate
gates_necessários  = ⌈ 1.000 / 12,8 ⌉                = 78 gates
```

**A leitura correta deste resultado:** se a instalação tem 40 catracas, nenhum software
resolve — faltam catracas, ou a janela de entrada precisa ser maior, ou o público precisa
chegar mais distribuído. A calculadora existe para essa conversa acontecer **semanas
antes**, não no portão.

O produto **alerta** quando a quantidade física é insuficiente, mostrando as três saídas
possíveis (mais gates, janela maior, escalonamento de chegada) com o número de cada uma.

### Efeito colateral do recolhimento de cartão

Com `CollectCardThenEnter`, o tempo por passagem **cresce** (leitura → recolhimento →
confirmação → giro). Se o p95 for 6 s em vez de 4 s, a capacidade por gate cai para 10/min
e os gates necessários sobem de 78 para **117**. Esse número precisa vir de **medição em
bancada** (`HIL-PERF-01`), não de estimativa — é a variável mais sensível de todo o
dimensionamento.

Além disso: ~67 esvaziamentos de urna ao longo do evento (750 cartões cada), cada um
tirando o gate de operação por alguns minutos. Isso entra na taxa de indisponibilidade,
e não é desprezível.

## 2. Metas a validar em benchmark

| Meta | Como é medida | Teste |
|---|---|---|
| Decisão local p95 ≤ 100 ms | Histograma por etapa, sem tempo humano/mecânico | `LOAD-DEC-01` |
| Nenhum acesso perdido após commit local | `kill -9` sob rajada; reconciliação | `CHAOS-KILL-01` |
| UI não bloqueante com dezenas de milhares de eventos | Frame time e responsividade | `LOAD-UI-01` |
| Sincronização recupera backlog sem degradar operação | Backlog de 24 h drenado sob carga | `LOAD-SYNC-01` |
| Zero duplicação lógica após retry/restart | Contagem por chave de dedupe | `CHAOS-DUP-01` |
| Worker estável 24–72 h | Memória, handles, threads | `SOAK-72H` |

Massa de teste: **50.000 credenciais** e **milhões de eventos históricos**. Rajadas, não
carga uniforme — o perfil real é 30 min de fúria dentro de 2 h de calmaria.

## 3. Estratégia de testes

| Camada | O que cobre | Onde roda |
|---|---|---|
| **Unitários** | Domínio, regras, máquina de estados, normalização de credencial | CI Linux, sem Windows |
| **Integração** | SQLite, outbox, repositórios, IPC | CI Linux + Windows |
| **Contrato** | `.proto` do IPC, conectores, **matriz de compatibilidade** | CI |
| **Simulador** | Equipamento inteiro, sem hardware | CI |
| **Hardware-in-the-loop** | Equipamento real, por modelo/firmware | Bancada |
| **Carga e soak** | Pico, backlog, 72 h | Ambiente dedicado |
| **Caos** | WAN, energia, processo morto, disco cheio | Ambiente dedicado |
| **Segurança** | Redação de log, permissão, segredo, dependência | CI |
| **Migração** | Cada migração aplicada e revertida com dados | CI |
| **Aceitação** | Os 13 critérios de CA-01 a CA-13 | Homologação |

O domínio roda em CI Linux **de propósito**: força a separação que o [ADR-0001](ADR/ADR-0001-isolar-easyinner-em-processo-x86.md)
exige. Se um teste de domínio precisar de Windows, a camada vazou.

## 4. Simulador (obrigatório desde a Fase 1)

Implementa `ITopdataInnerAdapter` e produz, de forma dirigível por script:

- leitor 1 e 2, teclado, QR, biometria;
- origens 5, 6, 7, 20 e **origens desconhecidas**;
- sucesso, negado, timeout, desconexão e **retorno 8**;
- urna cheia, cartão preso, desistência, giro não realizado, giro contrário;
- operação on-line, queda para off-line, reconexão, coleta de bilhetes;
- múltiplos equipamentos e múltiplos workers;
- relógio incorreto, duplicação, reordenação e retry.

Há também um **gravador/reprodutor**: sessões reais de bancada são gravadas e reproduzidas
em CI. Assim um defeito visto uma vez em hardware vira teste automatizado permanente —
que é a única forma de não repetir o mesmo erro no próximo evento.

### Casos nomeados do workflow da urna

`SIM-URNA-01` aprovado com giro · `-02` aprovado sem giro · `-03` sem origem 7 ·
`-04` negado (não recolhe, não libera) · `-05` relé falha · `-06` urna cheia no meio ·
`-07` desistência · `-08` liberação de entrada pelo perfil comissionado ·
`-09` duas leituras · `-10` dois cartões · `-11` tentativa simultânea em dois gates ·
`-12` queda de energia entre recolhimento e giro.

## 5. Métricas mínimas

Conexões, reconexões e uptime por equipamento · eventos/s · fila local · outbox pendente e
**idade do item mais antigo** · latência de leitura, decisão, comando, confirmação de
coleta e de giro · autorizações, negações e motivos · timeouts, retornos EasyInner e
crashes por worker · drift de relógio e de configuração · uso de disco, WAL, backup e
integridade · status da urna e capacidade operacional estimada ·
`unknown_origin_total{device,firmware,raw}`.

Painel local, exportação OpenTelemetry e **pacote de diagnóstico sanitizado** (um clique,
sem dado sensível, pronto para anexar em chamado).

## 6. Testes de caos

| ID | O que quebra | Resultado esperado |
|---|---|---|
| `CHAOS-WAN-01` | WAN cortada 8 h sob carga | Operação normal (T1); outbox cresce e drena depois |
| `CHAOS-KILL-01` | `kill -9` no supervisor e em workers | Nenhum acesso perdido ou duplicado |
| `CHAOS-DEV-01` | Equipamento trava em `ReceberDadosOnLine` | Watchdog reinicia o worker; **demais grupos intactos** |
| `CHAOS-PWR-01` | Energia cai durante configuração/recolhimento/giro | Rollback ou exceção registrada; nunca estado ambíguo |
| `CHAOS-DISK-01` | Disco enche | Degradação anunciada, sem corrupção |
| `CHAOS-CLK-01` | Relógio do equipamento salta | Drift alertado; ordem preservada por `received_time` |
| `CHAOS-DUP-01` | Reenvio massivo após reconexão | Dedupe por `(device, boot, seq)` |
| `CHAOS-REC-01` | Falha durante coleta de bilhetes | Memória só é limpa após commit local |

## 7. Bancada por modelo e firmware

**Antes de qualquer liberação para produção**: ensaio por modelo/firmware com relatório
assinado, arquivado em `docs/compatibility-matrix/relatorios/`. Sem ele, o modelo continua
`NAO_ENSAIADO` e o produto recusa configurá-lo fora do modo de manutenção.

Protocolo em [`09-plano-de-bancada.md`](09-plano-de-bancada.md).

**Suporte universal não é afirmado sem evidência.** A matriz diz o que foi ensaiado; o
resto é lacuna declarada.

---

## Resultados medidos — ensaio de soak

Ensaio `SOAK` com **10 equipamentos simulados**, laço de uma volta por vez, relógio
simulado. O relatório sai em `TestResults/soak-relatorio.txt` a cada execução, inclusive
quando passa, e a CI publica o arquivo. "Passou" não distingue 2 MB de 31 MB, e é a
tendência entre execuções que revela vazamento lento.

### 24/09/2026 — primeira execução de 1 h com medição

| Medida | Valor |
|---|---|
| Duração | 60,0 min |
| Voltas do laço | 232.722.200 |
| Eventos processados | **775.740.640** |
| Eventos por segundo | 215.483 |
| Memória inicial → final | 0,9 MB → 5,2 MB |
| Crescimento | 4,3 MB (teto do ensaio: 32 MB) |

**Passou — mas o número estava contaminado pelo próprio instrumento.**

A versão original guardava uma amostra de memória por lote num `List<long>`. Em 1 h isso
deu **465.444 amostras**, cujo array de apoio ocupa **4,00 MB** — contra os 4,30 MB de
crescimento total medido. O medidor respondia por **93% do que ele próprio media**.

Corrigido: a amostragem virou uma janela das 240 mais recentes, com máximo e contagem
guardados como agregados. A janela basta para mostrar a tendência na mensagem de falha, e
o ensaio deixou de crescer junto com o que mede.

Para efeito de comparação, o vazamento real encontrado em 22/09 — o histórico da máquina
de estados sem poda — era de **~400 bytes por evento**, cerca de 69 mil vezes maior que o
resíduo acima. A trava funciona; o que faltava era o instrumento não mentir a seu próprio
favor.

### 24/09/2026 — execução de 1 h com o instrumento corrigido

| Medida | Valor |
|---|---|
| Duração | 60,0 min |
| Voltas do laço | 181.880.200 |
| Eventos processados | **606.267.310** |
| Eventos por segundo | 168.408 |
| Amostras colhidas (240 retidas) | 363.760 |
| Memória inicial → final | 0,9 MB → 1,2 MB |
| **Crescimento** | **0,3 MB** |
| Bytes por evento | 0,00 |

O crescimento caiu de **4,3 MB para 0,3 MB**. A diferença de 4,0 MB é exatamente o array de
amostras que foi removido — o diagnóstico fecha com o valor previsto, e não por
aproximação.

Sobram 0,3 MB de acomodação do heap depois de 606 milhões de eventos. Em bytes por evento
isso arredonda para zero, e a leitura correta é que **o laço não retém nada por evento** —
não que ele retenha pouco.

> As duas execuções processaram volumes diferentes (776 milhões contra 606 milhões) porque
> a máquina estava mais carregada na segunda. Isso não afeta a conclusão: o critério é
> crescimento de memória, não vazão.

### Limitações deste ensaio

- Roda contra o **simulador**, não contra hardware. Não cobre vazamento na DLL nativa,
  que é justamente onde `SOAK-72H` terá de olhar depois do ensaio `HIL-STACK-01`.
- Usa relógio simulado; não detecta problema que dependa de tempo de parede real.
- 1 h não é 72 h. O critério da Fase 1 pedia 1 h; `SOAK-72H` continua aberto.
