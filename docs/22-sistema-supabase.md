# 22 — O sistema do Supabase: onde ele entra no desenho

> **O que se sabe, pelo usuário:** existe um sistema feito no Lovable, com banco no
> Supabase, que **recebe os payloads do Zet**, **recebe informações da catraca** e
> **cadastra os cartões**. O código ainda não foi visto — o usuário está verificando se
> consegue baixá-lo.
>
> Este documento é o que dá para dizer **antes** de ver o código: onde ele se encaixa, o
> que muda, e o que precisa ser respondido.

---

> ## Atualização de 25/09 — o relatório do sistema
>
> O usuário trouxe um levantamento do sistema existente, gerado pelo próprio Lovable. Ele
> responde a pergunta 1 e muda a pergunta de fundo. O que mudou está na seção 6. O resto
> do documento continua valendo.

---

## 1. Onde ele se encaixa

Nas três memórias de [`15`](15-integracao-e-sincronizacao.md), o sistema do Supabase é a
**nuvem** — o dono do cadastro:

```
  ┌──────────────────────────────┐
  │  Supabase (Lovable)          │  recebe o Zet · cadastra cartões · controle
  │  NUVEM — dono do CADASTRO    │
  └──────────────┬───────────────┘
                 │  ingressos e cartões descem · usos e passagens sobem
  ┌──────────────┴───────────────┐
  │  Edge (este produto)         │  decide na porta, sem internet
  │  BORDA — dono do FATO        │
  └──────────────┬───────────────┘
                 │
          TopFit 4 × 5
```

**A boa notícia:** parte do que foi construído do lado da nuvem pode já existir lá. Se o
webhook `apizet.ruailuminada.com/webhook` já aponta para uma função do Supabase, o relé
([ADR-0022](ADR/ADR-0022-rele-de-webhook.md)) pode não ser necessário — a função do
Supabase faz o papel dele.

**O que não muda:** a decisão na porta continua local. A catraca não pergunta nada ao
Supabase na hora do giro. Se a internet cair no meio do evento, a borda continua decidindo
com o que já desceu.

---

## 2. As perguntas, em ordem de impacto

### 1. Como o sistema recebe as informações da catraca **hoje**?

**É a pergunta que pode mudar o ensaio de bancada.** Há dois jeitos possíveis, e eles não
convivem bem:

| Se hoje é… | Consequência |
|---|---|
| A catraca manda direto para o Supabase, pela internet (modo WebServer / HTTP da Catraca 4) | Para o ensaio pela `EasyInner.dll`, a catraca precisa ser **reapontada para o PC local**. E fica o risco de segurança: a regra do projeto é **nunca expor a catraca à internet** |
| Um programa no PC local lê a catraca e manda para o Supabase | Esse programa e o nosso worker **disputam a mesma catraca** — só um pode ser o servidor dela |
| Ainda não recebe — está previsto | Não há conflito |

### 2. O webhook do Zet cai numa função do Supabase?

Se sim, o relé fica redundante — desde que essa função **guarde o payload bruto antes de
interpretar** e nunca descarte o que não entender. Ver ADR-0022, "o relé não sabe o que é
um ingresso".

### 3. O cadastro de cartões acontece onde, e funciona sem internet?

Se a venda de balcão é feita no aplicativo do Lovable, **ela para quando a internet cai**.
A borda tem venda de balcão local (`VenderNoBalcao`), que funciona sem internet. É preciso
decidir quem é o balcão no dia do evento.

### 4. Para onde vai o aviso de uso?

A volta ([`18`](18-contrato-do-webhook.md), seção 10) pode ir direto para o Zet ou passar
pelo Supabase. Se passar pelo Supabase, é ele quem dá a baixa no Zet — e a borda só precisa
de um conector, não de dois.

---

## 3. O que preciso do código baixado

| Arquivo | Para quê |
|---|---|
| `supabase/migrations/*.sql` | O esquema: tabelas de ingresso, cartão, evento de catraca |
| `supabase/functions/*` | A função que recebe o webhook do Zet, e a que recebe a catraca |
| `src/integrations/supabase/types.ts` | Os tipos que o Lovable gera a partir do banco — o retrato mais fiel do esquema |
| O `README`, se houver | Como foi pensado |

**Não preciso do `.env`, e ele não deve vir.** Se vier, eu não uso.

**Este repositório é público.** O código do Lovable não entra nele — eu leio, extraio o
que a integração precisa (nomes de tabela, formato de payload), e só isso vira código aqui.
Antes de qualquer coisa, confiro se há chave ou senha no que chegou.

---

## 4. As regras que valem para a integração, qualquer que seja o esquema

Vêm do escopo original do projeto e não dependem do que o código mostrar:

1. **A borda nunca guarda a chave `service_role` do Supabase.** Ela dá acesso total ao
   banco, e a máquina local fica numa rede com catracas. A borda usa uma credencial
   própria, com acesso só ao que precisa — por política de linha (RLS) ou por uma função
   do Supabase com token dedicado.
2. **A rede das catracas nunca alcança o Supabase.** Quem fala com a nuvem é o PC, pela
   rede dele.
3. **A borda puxa; a nuvem não empurra para ela.** A máquina local não abre porta para a
   internet ([ADR-0022](ADR/ADR-0022-rele-de-webhook.md)).
4. **A decisão de acesso nunca consulta a nuvem.** O Supabase fora do ar não barra
   ninguém na porta.

---

## 5. O que muda no código, provavelmente

Sem ter visto o esquema, a expectativa é pequena — foi para isso que as portas existem:

- Uma implementação de `IFonteDeIngressos` que puxa do Supabase, no lugar da `FonteDeRelay`.
  O laço de ingestão, o cursor e a idempotência não mudam.
- Uma configuração do `ConectorRest` apontando para o Supabase, se a volta passar por lá.
- Talvez a origem dos cartões vendidos no balcão, se o balcão continuar sendo o Lovable.

O ensaio de bancada ([`21`](21-roteiro-da-bancada.md)) **não depende de nada disto**: ele
usa ingressos de um arquivo, e pode acontecer antes.

---

## 6. O que o relatório mostrou

### 6.1 O fluxo que já rodou em produção

```
TopData ──► Middleware C# (Windows, PC local) ──► Edge Functions ──► tabelas ──► painel
```

**A catraca não fala direto com a internet** — há um programa no PC local no meio. A
preocupação da pergunta 1 (catraca exposta) não se confirma. A outra, sim: **esse programa
e o nosso worker não podem ser o servidor da mesma catraca ao mesmo tempo.** Para a
bancada, o middleware antigo precisa estar parado.

O sistema operou numa edição anterior do evento, com três catracas de entrada, e o
volume de um único dia dá a ordem de grandeza que faltava para dimensionar (pergunta B7).
Os números ficam fora deste documento por serem dado de operação num repositório público.

### 6.2 As tabelas

| Tabela | Papel |
|---|---|
| `rfid_cards` | Cartões físicos: número, código de barras, tipo, situação, **`current_ticket_id`**, datas de atribuição e devolução, última validação, `cooldown_until`, total de usos |
| `authorized_cards` | Espelho dos autorizados, sincronizado por gatilho |
| `authorizations` | Regra por cartão e dispositivo, com janela de validade |
| `access_events` | Log de cada passagem, deduplicado por função |
| `validations` | Validações consolidadas — contagem de público e painéis |
| `middleware_devices`, `terminal_heartbeats` | Cadastro e saúde das catracas |
| `middleware_log_audits`, `middleware_alerts`, `middleware_integrity_checks` | Auditoria |

**O modelo de cartão deles coincide com o nosso** ([`19`](19-bilheteria-local-e-divisao-das-catracas.md),
seção 5): o cartão é recipiente, `current_ticket_id` é a venda corrente, e há cooldown por
cartão. Tipos em uso: `INTEIRA`, `MEIA`, `SOCIAL` — o que chamávamos de "solidária".

### 6.3 O contrato das funções (pelo lado do pedido)

Todas com `Authorization: Bearer <segredo>`:

| Função | Pedido |
|---|---|
| `middleware-event-receiver` | `{device_id, event_id, card_number, timestamp, validation_type}` → 200 liberado · 403 negado · 401 · 5xx |
| `middleware-sync-cards` | `{device_id, last_sync_at, full_sync}` |
| `middleware-sync-events` | `{device_id, events: [{event_id, event_type, timestamp, payload}]}` |
| `middleware-heartbeat` | estado do dispositivo; **a resposta pode trazer comandos** |

**O que não se sabe:** o formato das **respostas** de `sync-cards`, `sync-events` e
`heartbeat`. Só os pedidos foram descritos. Sem isso, as implementações de ingestão e de
confirmação não podem ser escritas sem inventar.

### 6.4 A divergência de desenho

O fluxo descrito valida **na nuvem, a cada passagem**: a leitura vai ao
`middleware-event-receiver`, e só a resposta libera a catraca. O cache local é o plano B.

Este projeto faz o contrário: **decide sempre na borda**, e manda os fatos para a nuvem
em segundo plano ([`15`](15-integracao-e-sincronizacao.md)).

| | Nuvem a cada passagem | Borda decide |
|---|---|---|
| Internet fora | cai para o cache | nada muda |
| Internet **lenta** — o caso difícil | cada passagem espera o tempo-limite antes de cair para o cache | nada muda |
| Painel em tempo real | sim | sim, enquanto houver internet — a fila drena por prioridade |
| Regra única entre catracas | a nuvem garante | a borda garante, **desde que todas as catracas estejam no mesmo PC** |

A última linha é a que decide a arquitetura: **uma borda por PC, com as cinco catracas
nela**, e não um programa por catraca. O prompt do Manus configura um `DEVICE_ID` por
instância, o que leva a um programa por catraca — e, sem internet, dois PCs não enxergam
o uso um do outro.

### 6.5 O prompt para o Manus descreve este projeto

| O que o prompt pede | Neste projeto |
|---|---|
| Integração EasyInner | **Existe** — 229 funções declaradas, worker x86 isolado, modo bancada |
| Fila offline em SQLite, sem duplicar | **Existe** — outbox com chave de idempotência, testada com `kill -9` |
| Cache local de cartões | **Existe** — falta a fonte que puxa do `sync-cards` |
| Cooldown local | **Existe** — intervalo de reuso, que a revenda não zera |
| Reenvio em lote ao reconectar | **Existe** — drenador por prioridade |
| Serviço Windows, instalador | **Existe** — MSI |
| Heartbeat e comandos remotos | **Não existe** |
| Log em arquivo diário | **Não existe** — há log estruturado com redação, sem arquivo |
| Painel desktop completo | **Parcial** — casca WPF e console de bancada |

### 6.6 Se o caminho for o Manus mesmo, o que o prompt precisa corrigir

1. **Uma instância por catraca não funciona com a EasyInner.** A DLL escuta uma porta
   (3570) e atende várias catracas; dois programas no mesmo PC disputam a porta.
2. **A DLL é de 32 bits e não é segura entre threads.** O processo tem de ser x86 e a
   chamada, serializada. Em 64 bits, falha com retorno 8.
3. **`POLL_INTERVAL_MS`** sugere consulta periódica; a leitura on-line da EasyInner é uma
   chamada que espera o evento.
4. **`open_turnstile` vindo da nuvem abre a catraca pela internet.** Quem tiver o segredo
   abre o portão. Exige registro de quem mandou, e idealmente aprovação de duas pessoas.
5. **Um segredo só para todas as catracas.** Se vazar, qualquer um grava evento. Um por
   dispositivo, rotacionável.
6. **`customer_name` no cache local** leva dado pessoal para a máquina da portaria sem
   necessidade: a catraca não precisa do nome para decidir.
7. **Os fatos do SDK já apurados** — retorno em `byte`, `LiberarLeitor` não existe, o tipo 8
   é QR, `AcionarRele2` não tem tempo — teriam de ser redescobertos.

## 7. O que preciso para ligar este projeto ao Supabase

1. **O código do middleware C# que já rodou.** É o artefato mais valioso: tem o contrato
   exato **e** o uso da EasyInner que funcionou na catraca real.
2. ~~Os formatos de resposta de `sync-cards`, `sync-events` e `heartbeat`.~~ **Lidos no
   código das funções em 25/09.** Ver a seção 8.
3. ~~A decisão da seção 6.4.~~ **Decidido em 25/09: a catraca decide no PC local, que
   recebe e envia informações para a nuvem.** Ver
   [ADR-0023](ADR/ADR-0023-borda-decide-nuvem-sincroniza.md).


## 8. O que o código das funções mostrou (25/09)

O código do sistema foi lido em 25/09, com acesso concedido pelo dono. **Ele não está
neste repositório e não vai estar**: este repositório é público. O que segue descreve o
comportamento com as nossas palavras, para que a integração possa ser revisada aqui. O
código do middleware C# **não** estava no repositório — só um manual de instalação.

### 8.1 Segurança — resolver antes do evento

`middleware-sync-cards`, `middleware-sync-events` e `middleware-heartbeat` estão com a
verificação de JWT desligada, e o código delas não confere segredo nenhum. O relatório
dizia que havia um segredo compartilhado; o código não o confere. Se o que está
publicado for igual ao repositório, qualquer um com o endereço do projeto pode, sem
credencial:

- baixar a lista de cartões ativos, com nome do cliente, categoria e validade;
- gravar eventos de acesso falsos, que viram validações no painel;
- criar equipamentos e alertas falsos.

**Não foi testado contra o servidor real**, de propósito. A correção é a função exigir
um segredo por equipamento, comparado em tempo constante; do nosso lado, o segredo fica
no cofre do Windows e é posto por um `DelegatingHandler`, nunca pelo conector (ver
ADR-0023 e docs/15).

### 8.2 Cartões — sentido "desce" (`middleware-sync-cards`)

| | |
|---|---|
| Pedido | `device_id`, `last_sync_at` (opcional), `full_sync` |
| Resposta | `cards[]`, `removed_cards[]`, `total_cards`, `sync_timestamp` |
| Cada cartão | `card_number`, `active`, `max_uses`, `times_used`, `valid_from`, `valid_until`, `ticket_type`, `admission_type`, `customer_name` |
| Cursor | `sync_timestamp`, relógio **do servidor**, marcado antes da consulta |

O que isso obriga do nosso lado (`FonteDeCartoesDoPainel`):

1. **Sem paginação.** A resposta traz tudo de uma vez, e o Supabase costuma limitar uma
   consulta a 1.000 linhas. Com 1.000 ou mais cartões na resposta, a borda **avisa e não
   avança o cursor**. A correção de verdade é paginar no servidor. `A_CONFIRMAR`: o
   limite configurado no projeto.
2. **Removidos só na incremental.** A lista completa traz só os ativos. Como a varredura
   completa nunca move o cursor incremental (docs/16), a remoção ainda chega pela
   incremental seguinte.
3. **A nuvem não soma usos a partir dos eventos.** Nada no código incrementa
   `times_used`. Quem conta é a borda, e o contador local nunca é sobrescrito pela
   sincronização. `max_uses` nulo vira "sem limite de usos": o controle é físico, pela
   urna e pelo intervalo de reuso. Com `max_uses` preenchido, os usos que já estiverem em
   `times_used` são descontados (no pior caso nega cedo, nunca libera a mais).
4. **Cartão desativado e reativado volta a valer** — só para provedor reutilizável (a
   bilheteria). Ingresso de site cancelado continua cancelado para sempre.
5. **`card_number` precisa ser texto e passar pelo perfil do leitor** (Mifare: 10
   dígitos, zeros à esquerda completados). Número JSON é recusado. O motivo da recusa diz
   o tamanho, nunca o número. `A_CONFIRMAR` na bancada: se o número cadastrado na nuvem
   é o mesmo texto que a catraca entrega.
6. **A categoria é do cartão**, não da venda: o painel manda `admission_type` (meia,
   inteira, social, cortesia) e cada cartão físico tem a sua.
7. O `admission_type` é deduzido de `customer_name` e do tipo de ingresso; a leitura de
   `metadata`, prevista no código, não acontece porque a coluna não é pedida na consulta.
   Não afeta a borda, mas explica categoria "errada" no painel.

### 8.3 Tentativas — sentido "sobe" (`middleware-sync-events`)

| | |
|---|---|
| Pedido | `device_id`, `events[]` (até 100), `sync_reason` |
| Cada evento | `card_id`, `occurred_at`, `authorized`, `reason`, `admission_type`, `ticket_id`, `device_direction`, `extra`, `event_id` |
| Resposta | **200** com `saved`, `failed`, `duplicates_ignored`, `failed_events[]` |
| Repetição | reconhecida por equipamento + cartão + horário; `event_id` é descartado |

Cada evento "autorizado" que entra vira uma validação no painel (gatilho no banco). O
que isso obriga do nosso lado (`ConectorDeTentativasDoPainel` + `EspelhoDeTentativas`):

1. **Um evento por tentativa, nunca dois.** Um segundo evento "autorizado" para a mesma
   passagem seria uma pessoa a mais no relatório.
2. **A liberação espera o giro antes de subir.** Ela entra na fila na mesma transação da
   decisão, mas só sai depois de `EsperaPeloGiro` (maior que o tempo de acionamento). Se
   o giro chegar antes, ele entra no mesmo item e o libera na hora. Assim o evento já sobe
   dizendo se a pessoa passou (`extra.giro_confirmado`, `extra.giro_em`). O painel
   continua contando a liberação, como já contava; contar só quem girou passa a ser um
   filtro nesse campo. **A confirmar com o dono do painel.** Giro que chegar depois de o
   evento ter saído fica só na borda.
3. **Negativa sobe na hora**, com o motivo em `reason`.
4. **Horário sempre com a mesma grafia** (UTC, milissegundos, `Z`), porque ele é parte da
   chave de repetição lá. Reenviar depois de uma resposta perdida cai como repetido.
5. **200 não quer dizer que tudo entrou.** Os itens de `failed_events` vão para cartas
   mortas; se o servidor disser que N falharam e não der para apontar quais, o grupo
   inteiro é reenviado (o que já entrou volta como repetido).
6. **Uma requisição por catraca**, porque o `device_id` é do pedido, não do evento.

### 8.4 O que não mudou

- O webhook do Zet **não** cadastra nada em `authorized_cards`: hoje o QR comprado online
  não chega à lista da catraca. Pergunta aberta ao usuário: como o comprador online
  entra.
- `middleware-heartbeat` e os comandos remotos continuam fora. Comando remoto (abrir
  catraca pela nuvem) só depois de desenhado com trilha de auditoria (ADR-0023).
- Nada disto está ligado ao serviço ainda: as peças existem e estão testadas contra um
  servidor falso que imita o comportamento acima
  (`tests/Integration/PainelNaNuvemTests.cs`, `tests/Unit/Conectores/PainelTests.cs`).
  Falta ligar no serviço, com o segredo no cofre, e testar contra um projeto de teste
  na nuvem — **não** contra o de produção, que tem dados pessoais de clientes.
