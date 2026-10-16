# 59 — Solicitação técnica à equipe da nuvem: requisitos para a integração total

> **Para quem é este documento:** a equipe que mantém o painel na nuvem (Supabase, as funções
> `middleware-*` e o Cloudflare Worker da Zet). Pode ser enviado como está.
>
> **De quem:** Rayzer Serviços e Tecnologia, dono do XAcess (o sistema que roda no computador
> das catracas).
>
> **Data da verificação:** 15/10/2026. A parte de "situação atual" foi conferida por leitura
> do projeto de produção (políticas de acesso e código publicado das funções). Nada foi alterado.
>
> **Base:** o contrato da versão 1 (docs/31). Este documento não o substitui: cobra o que ainda
> falta dele, e pede três coisas novas (§5).

---

## 1. Resumo

O XAcess já fala com a nuvem: sobe as passagens (`middleware-sync-events`) e baixa a lista de
cartões (`middleware-sync-cards`), com o cabeçalho `Authorization: Bearer <segredo do equipamento>`.
A lista de cartões já chega inteira (a `middleware-sync-cards` v43 pagina tudo e manda
`contract_version: 1` e `total_cards`). Falta:

1. **Segurança** (bloqueia a ligação em produção): as funções não conferem o segredo, e as
   tabelas ainda estão abertas à chave pública.
2. **Um teste de conexão leve** (novo, §5.1), para o botão "Testar conexão com a nuvem" não
   precisar baixar 2.243 cartões a cada toque.
3. **Dois ajustes de dados** (§4.2 e §4.3).

---

## 2. Situação atual (verificada em 15/10/2026)

| Item | O que foi visto | Situação |
|---|---|---|
| Lista de cartões completa | `middleware-sync-cards` v43 pagina até o fim, devolve `total_cards` e `contract_version: 1` | **Atendido** |
| Número como texto | `card_number` sai como texto | **Atendido** |
| Nome e CPF fora da resposta de cartões | Não saem | **Atendido** |
| Resposta de eventos (`saved`, `failed`, `duplicates_ignored`, `failed_events`) | Como no §4 do docs/31 | **Atendido** |
| `extra` guardado como veio (`giro_confirmado`, `simulado`) | Gravado em `access_events.extra` | **Atendido** (a separação de `simulado` nos painéis ainda precisa ser confirmada, §4.4) |
| Segredo de equipamento nas funções | `middleware-sync-cards`, `middleware-sync-events` e `middleware-heartbeat` **não** conferem o cabeçalho nem o `device_id` | **Pendente — P0** |
| Acesso público (`anon`) às tabelas | Ainda existem políticas para `anon` em `authorizations` (leitura), `access_events` (inserção), `middleware_commands` (leitura e alteração) e `middleware_devices` (acesso total para `public`) | **Pendente — P0** |
| `middleware-heartbeat` e `middleware-connection-health` | O XAcess **não chama** nenhuma das duas hoje | Ver §3.3 |

---

## 3. Segurança (P0): antes de qualquer dado real em produção

### 3.1 Tirar o acesso público às tabelas

Remover, para as roles `anon` e `public`, estas políticas (lista da verificação):

| Tabela | Política hoje | Deve ficar |
|---|---|---|
| `authorizations` | `Middleware can read authorizations`, `anon`, SELECT | Nada para `anon`. Leitura só para `authenticated` (administradores) |
| `access_events` | `Middleware can insert access events`, `anon`, INSERT | Nada para `anon`. Escrita só pela função, com a chave de serviço |
| `middleware_commands` | `Middleware can read pending commands` e `Middleware can update command status`, `public` | Nada para `public`. Só a função, com a chave de serviço |
| `middleware_devices` | `Admins can manage middleware_devices`, `public`, ALL | Restringir a `authenticated` com papel de administrador |

Se outra aplicação precisar ler cartões direto, criar uma **view sem dado pessoal** (número,
tipo, ativo, validade) e liberar só ela, só para leitura.

Depois, conferir nos logs de acesso do Supabase se houve leitura dessas tabelas por quem não
devia. Se houve, é incidente de dado pessoal (LGPD). Avisar a Rayzer antes de qualquer
comunicação externa.

### 3.2 Segredo por equipamento nas funções do XAcess

Vale o §2.2 do docs/31. Resumo do que pedimos:

- `middleware-sync-cards`, `middleware-sync-events` e o novo `middleware-ping` (§5.1): conferir
  o `Authorization: Bearer` contra o hash do segredo do `device_id` do pedido, em tempo
  constante. Sem segredo, ou com segredo de outro equipamento: `401`, sem dizer qual dos dois
  estava errado.
- A tabela de equipamentos guarda **só o hash** do segredo, com `ativo` e `revogado_em`.
- Nunca registrar o segredo em log nem devolvê-lo em resposta.
- O XAcess já envia o cabeçalho e não guarda o segredo em arquivo: ele fica no cofre do Windows.

### 3.3 `middleware-heartbeat` e `middleware-connection-health`

Lemos o código do `middleware-heartbeat`: quem souber um `device_id` consegue, sem segredo,
marcar o equipamento como online, criar alertas, e ler e marcar como enviados os comandos
pendentes (`middleware_commands`). O `middleware-connection-health` ainda não foi lido por nós; deve
ser revisto pelo mesmo critério. O XAcess **não chama** nenhuma das duas. Pedimos uma destas
opções, a que a equipe preferir:

- **a)** desligar as duas funções até terem o mesmo controle de segredo do §3.2; ou
- **b)** proteger as duas com o mesmo controle, e nos avisar antes de qualquer uso.

Se o painel de vocês precisar do heartbeat, conversamos sobre o formato. Por enquanto, o XAcess
não depende dele.

### 3.4 Teste de aceite de segurança

Estas verificações devem passar **antes** de qualquer teste com dado real:

| # | Faça | Tem de acontecer |
|---|---|---|
| S1 | Chamar `middleware-sync-cards` e `middleware-sync-events` sem `Authorization` | `401` |
| S2 | Chamar com o segredo de outro `device_id` | `401` |
| S3 | Chamar com um segredo revogado (`ativo = false`) | `401` |
| S4 | Ler `authorizations` pela API REST com a chave `anon` | Recusado |
| S5 | Inserir em `access_events` com a chave `anon` | Recusado |
| S6 | Ler ou alterar `middleware_commands` com a chave `anon` | Recusado |
| S7 | Chamar `middleware-heartbeat` sem segredo (se mantida) | `401` ou função desligada |

---

## 4. Dados e eventos

### 4.1 Cartões (`middleware-sync-cards`): o que já serve

Mantém o que está no §3 do docs/31. Confirmado na v43: `cards` completo, `total_cards`,
`contract_version: 1`, `card_number` como texto, `sync_timestamp` com fuso.

### 4.2 Desativados na sincronização completa

Hoje a sincronização **completa** manda só os cartões ativos, e `removed_cards` vem vazio. Um
cartão desativado antes da primeira sincronização nunca aparece aqui, e a carga inicial que
trouxe esse cartão continua valendo no computador da portaria.

**Pedimos:** na resposta completa, mandar em `removed_cards` os números desativados ou apagados
do evento (todos, não só os desde `last_sync_at`). O XAcess já sabe cancelar cartões dessa lista.

### 4.3 Validade vazia (P2 do docs/31 §5.2)

Pedimos confirmação de que a view `offline_cards` (se existir) trata `valid_from` e `valid_until`
vazios: `(valid_from IS NULL OR valid_from <= now())`, e o equivalente para `valid_until`. Hoje
os cartões do evento não têm validade, e um filtro mal feito zera a lista.

### 4.4 Passagens simuladas separadas

O XAcess marca `extra.simulado = true` quando o computador está em modo de simulação (sem catraca
física). **Pedimos** que os painéis, as contagens de público e a prestação de contas da nuvem
**excluam** esses eventos, e que isso seja confirmado por escrito. Hoje o campo é gravado, mas não
vimos onde ele é usado.

### 4.5 Cartões de teste (P2 do docs/31 §5.3)

Os 6 cartões de 6 dígitos criados em 17/11/2025 devem sair do cadastro do evento ou ficar
inativos. Confirmar quando for feito.

---

## 5. Pedidos novos

### 5.1 `middleware-ping`: teste de conexão sem baixar a lista

O botão **"Testar conexão com a nuvem"** do XAcess (tela Sincronização, desde a versão 0.1.209)
faz uma sincronização de verdade. Com a lista completa, isso são 2.243 cartões a cada toque. Pedimos
uma função leve:

- **Pedido:** `POST /functions/v1/middleware-ping`, corpo `{ "device_id": "..." }`, com o
  mesmo `Authorization: Bearer` do §3.2.
- **Resposta `200`:**

  ```json
  { "success": true, "contract_version": 1, "server_time": "2026-10-15T23:00:00.000Z" }
  ```

- **Erros:** `401` sem segredo ou com segredo errado (sem dizer qual); `5xx` se o banco falhar.
- Não lê nem escreve cartões nem eventos.

Com ela, o XAcess passa a usar o ping para o teste de conexão e só baixa a lista na sincronização
normal. Se não for possível, avisem: o XAcess continua funcionando, só com o teste mais pesado.

### 5.2 `contract_version` também na resposta de eventos

Pedimos que `middleware-sync-events` também devolva `contract_version: 1`, como a de cartões. Isso
deixa o XAcess checar, a cada envio, que fala com a versão do contrato que ele conhece. Um campo
novo não quebra nada do lado do XAcess; a ausência, por ora, também não.

### 5.3 Aviso de mudança de contrato

Quando mudar qualquer função usada pelo XAcess (`middleware-sync-cards`, `middleware-sync-events`,
`middleware-ping`), avisar a Rayzer **antes** de publicar, com a data. Mudança de sentido de campo
ou remoção de campo exige a versão 2 do contrato (docs/31 §9), combinada antes.

---

## 6. Zet → catraca (P1 do docs/31 §6): situação a confirmar

O Worker da Zet deve repassar o **mesmo corpo**, sem esperar a resposta, ao relé de ingressos do
XAcess. Pedimos confirmação de que isso está ligado, e que o `RELE_XACESS_URL` é segredo do Worker.
Não é bloqueio para a integração com a nuvem, mas é para as vendas chegarem à catraca em segundos.

---

## 7. Operação

- **Projeto de teste** com cartões fictícios (docs/31 §8), antes de qualquer teste com número
  real. Não usar os números de produção em nenhum ambiente de teste.
- **Entrega do segredo** de cada equipamento por canal separado da URL (nunca no mesmo e-mail).
- **Cadastro e revogação** de equipamentos: quem cadastra, como gera o segredo (32 bytes ou mais)
  e como revoga (`ativo = false`). Pedimos o procedimento escrito.
- **Contato** para incidente de segurança e a janela em que a Rayzer pode fazer o teste de
  aceite (§3.4 e §8 do docs/31).

---

## 8. Critérios de aceite da integração

A integração total está pronta quando:

1. Todos os itens S1 a S7 (§3.4) passam.
2. Os itens 4 a 9 do docs/31 §8 passam no projeto de teste (lista completa com 2.500 cartões
   fictícios, cartão com zeros à esquerda, cancelamento por `removed_cards`, reenvio marcado como
   duplicado, lote com um evento inválido, Zet com o relé).
3. §4.2 e §4.4 estão respondidos por escrito.
4. `middleware-ping` (§5.1) responde, ou a Rayzer é avisada de que não haverá.
5. A equipe da nuvem confirma por escrito a versão publicada de cada função usada pelo XAcess.

Depois disso, a Rayzer faz o teste no computador do evento (catraca de bancada) e só então
recomenda a instalação para o evento.

---

## 9. O que a Rayzer já fez do lado do XAcess

- Envia o segredo em `Authorization: Bearer` a cada chamada.
- Recebe a lista de cartões inteira e recusa lista cortada (`contract_version` e `total_cards`).
- Marca passagens simuladas (`extra.simulado`) e grava `extra.giro_confirmado`.
- Botão "Testar conexão com a nuvem" (versão 0.1.209); passa a usar `middleware-ping` quando existir.

---

## 10. Versão

Este documento é o pedido de integração total, aplicado sobre o contrato versão 1 (docs/31).
Quando a nuvem responder, a resposta vai para o `docs/59` com data, e o que não for aceito fica
registrado com o motivo.
