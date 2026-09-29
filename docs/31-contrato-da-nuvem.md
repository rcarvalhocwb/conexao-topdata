# 31 — Contrato entre a nuvem e o XAcess · versão 1

> **Para quem é este documento:** a equipe que mantém o painel na nuvem (Supabase, funções
> e o Cloudflare Worker da Zet). Ele pode ser enviado como está.
>
> **De quem:** Rayzer Serviços e Tecnologia — XAcess, o sistema que roda no computador das
> catracas.
>
> **Situação:** proposta. O nosso lado já está implementado e testado contra um servidor
> falso que se comporta como descrito aqui. Nada foi testado contra o projeto de produção,
> de propósito: ele tem dados pessoais.

---

## Resumo em uma página

| Prioridade | O que muda na nuvem | Por quê |
|---|---|---|
| **P0** | Tirar da chave pública (`anon`) a leitura de `authorizations` e a gravação de `access_events` | Hoje qualquer pessoa leria titulares (e CPF) e forjaria acessos |
| **P0** | As funções `middleware-sync-cards` e `middleware-sync-events` exigem o segredo de cada equipamento | Hoje não conferem credencial nenhuma |
| **P0** | `middleware-sync-cards` devolve **todos** os cartões e a contagem exata em `total_cards` | São 2.243 cartões; o limite padrão de 1.000 corta a lista sem avisar |
| **P0** | Número do cartão sempre como **texto**, exatamente como cadastrado | 156 cartões começam com zero |
| **P1** | O Worker da Zet repassa cada webhook também ao nosso relé | 61% dos ingressos são comprados no dia; a catraca precisa saber em segundos |
| **P1** | A resposta de cartões não leva nome nem CPF | A catraca não precisa; minimização (LGPD) |
| **P2** | Ajustes de dados: tipo do cartão, validade, cartões de teste, a view `offline_cards` | Relatórios e contingência corretos |

---

## 1. Quem faz o quê

```
 Catraca ◄──TCP local──► XAcess (PC do evento)  ◄──HTTPS──►  Nuvem (painel)
                         decide, conta usos,                   cadastra cartões,
                         funciona sem internet                 recebe fatos, mostra painéis
```

1. **A decisão de liberar é do XAcess, no computador do evento, sem depender de internet.**
   A catraca nunca consulta a nuvem na hora da leitura. Não existe "consulta online
   rápida" por cartão.
2. **A nuvem é dona do cadastro:** quais cartões valem, de que tipo, até quando.
3. **O XAcess é dono do fato:** quem passou, quando, em qual catraca e quantas vezes. A
   contagem de usos da catraca é a verdade. A nuvem recebe esses fatos e mostra os painéis.
4. **O XAcess nunca fala direto com as tabelas** (`/rest/v1/...`) e nunca usa a chave
   `anon`. Fala só com as duas funções abaixo, com o segredo do equipamento.

## 2. Segurança (P0 — antes de qualquer teste com dados reais)

### 2.1 Tirar o acesso público

Pelas políticas descritas ("Middleware can read authorizations" e "Middleware can insert
access events"), a chave `anon`, que vai dentro do site e qualquer navegador enxerga, lê e
grava as tabelas de acesso. Pedimos:

1. **Remover** essas políticas para `anon` em `authorizations`, `access_events`,
   `authorized_cards`, `rfid_cards` e em qualquer tabela com nome, CPF, e-mail ou telefone.
2. Se outra aplicação precisar ler cartões direto, criar uma **view sem dado pessoal**
   (número, tipo, ativo, validade) e liberar só ela, só para leitura.
3. Conferir nos logs de acesso do Supabase se houve leitura dessas tabelas por quem não
   devia. Se houve, é incidente de dado pessoal (LGPD).

### 2.2 Segredo por equipamento nas funções

Cada computador de evento tem um identificador (`device_id`) e um segredo próprio.

| Item | Como |
|---|---|
| Onde vai | Cabeçalho `Authorization: Bearer <segredo>` em toda chamada às duas funções |
| Tamanho | Pelo menos 32 bytes aleatórios (ex.: `openssl rand -base64 48`) |
| Onde a nuvem guarda | **Só o hash** (SHA-256) do segredo, numa tabela de equipamentos (`device_id`, `secret_hash`, `ativo`, `criado_em`, `revogado_em`) |
| Como confere | Hash do que chegou comparado ao guardado **em tempo constante** (`crypto.subtle` + comparação sem atalho), e o `device_id` do pedido tem de ser o do segredo |
| Sem segredo ou errado | `401`, sem dizer qual dos dois estava errado; nunca `200` com lista vazia |
| Revogar | `ativo = false` derruba aquele computador sem afetar os outros |
| Nunca | Registrar o segredo em log, devolvê-lo em resposta, reaproveitá-lo entre equipamentos |

Do nosso lado, o segredo fica no cofre do Windows (DPAPI) e é posto no cabeçalho pela
camada de transporte, nunca em arquivo de configuração nem em log.

## 3. Cartões — `middleware-sync-cards` (P0)

### 3.1 Pedido (já é assim hoje)

```http
POST <base>/functions/v1/middleware-sync-cards
Authorization: Bearer <segredo do equipamento>
Content-Type: application/json

{ "device_id": "borda-01", "full_sync": true }
```

Nas rodadas seguintes, a cada 30 segundos:

```json
{ "device_id": "borda-01", "full_sync": false, "last_sync_at": "<o sync_timestamp da resposta anterior, sem mudar nada>" }
```

### 3.2 Resposta

```json
{
  "success": true,
  "cards": [
    { "card_number": "00000000000014", "active": true, "admission_type": "social",
      "valid_from": null, "valid_until": null, "max_uses": null, "times_used": null }
  ],
  "removed_cards": ["100000000099"],
  "total_cards": 1,
  "sync_timestamp": "2026-11-14T20:00:00.000+00:00"
}
```

| Campo | Regra | O que acontece se quebrar |
|---|---|---|
| `cards` | **Todos** os cartões que a consulta encontrou: sem o limite padrão de 1.000. Na completa, os ativos; na incremental, os alterados desde `last_sync_at` | — |
| `total_cards` | **Novo significado, obrigatório:** a contagem exata (`count(*)`) do que a consulta encontrou, sem limite. Se vierem menos cartões do que `total_cards`, tratamos a lista como cortada | Sem o campo, 1.000 ou mais é tratado como corte, e a sincronização não avança |
| `card_number` | **Texto**, exatamente como cadastrado: sem tirar zeros, sem espaços, sem máscara. Nunca número JSON | Número JSON é recusado, porque já perdeu os zeros à esquerda |
| `active` | `true` em `cards`; os desativados vão em `removed_cards` | — |
| `admission_type` | `inteira`, `meia`, `social` ou `cortesia`, tirado do **tipo do cartão**, não deduzido do nome do cliente (§5) | O relatório por tipo sai errado |
| `valid_from`, `valid_until` | `null` (sem limite) ou data ISO 8601 **com fuso** (`Z` ou `-03:00`) | Data sem fuso é recusada: é ambígua em 3 horas |
| `max_uses`, `times_used` | Inteiros ≥ 0 ou `null`. `max_uses: null` = sem limite (o controle é a urna) | Descontamos `times_used` de `max_uses`; nunca liberamos a mais |
| `removed_cards` | Números (texto) desativados ou apagados desde `last_sync_at`, só na incremental | Cartão removido continuaria valendo até a próxima completa |
| `sync_timestamp` | Relógio do **servidor**, marcado **antes** de a consulta rodar, com fuso | Sem ele, a resposta é recusada: o relógio do PC não serve de cursor |
| Nome, CPF, `customer_name`, `metadata` | **Não enviar** (P1). A catraca não usa | Dado pessoal circulando sem necessidade |

A implementação do nosso lado (`FonteDeCartoesDoPainel`) já segue estas regras, com
testes automáticos. Eles cobrem número em JSON, data sem fuso, usos já feitos, resposta sem
`sync_timestamp`, `total_cards` acima de 1.000 e os formatos reais do cadastro (12 e 14
dígitos, com zero à esquerda).

## 4. Tentativas — `middleware-sync-events` (P1 — já funciona assim; pedimos que não mude)

```json
{
  "device_id": "CATRACA-ENTRADA-01",
  "sync_reason": "batch",
  "events": [
    { "event_id": "0192f1c2-…", "card_id": "100000000012", "occurred_at": "2026-11-14T21:15:03.120Z",
      "authorized": true, "reason": "AUTORIZADO", "admission_type": "inteira",
      "extra": { "origem": "borda", "tentativa": "0192f1c2-…", "portao": "portao-1", "provedor": "bilheteria-local",
                 "giro_confirmado": true, "giro_em": "2026-11-14T21:15:05.004Z" } }
  ]
}
```

| Regra | Detalhe |
|---|---|
| Lote | Até 100 eventos, um `device_id` por pedido |
| Repetição | Reconhecida por `device_id` + `card_id` + `occurred_at` **como texto**. Mandamos sempre UTC, milissegundos e `Z`. Reenviar depois de uma resposta perdida tem de cair como repetido |
| Resposta | `200` com `saved`, `failed`, `duplicates_ignored` e `failed_events: [{card_id, occurred_at}]`. Com falha parcial, continua `200`, mas a lista precisa dizer quais falharam |
| Negadas | Também sobem (`authorized: false`), com o motivo em `reason` (tabela abaixo) |
| Quem entrou de fato | `extra.giro_confirmado = true` quer dizer que a catraca **girou**. Autorizado sem giro é quem desistiu. Contar público pelo giro, não pela autorização (ADR-0007) |
| `extra` | Guardar como veio (JSON). Novos campos podem aparecer; campos existentes não mudam de sentido |

Valores de `reason` que o XAcess envia (os mais comuns):

| `reason` | Significado |
|---|---|
| `AUTORIZADO` | Liberou |
| `CREDENCIAL_DESCONHECIDA` | Cartão ou QR não cadastrado |
| `INGRESSO_CANCELADO` / `CREDENCIAL_BLOQUEADA` | Cancelado ou desativado |
| `USOS_ESGOTADOS` | Já usado |
| `INTERVALO_DE_REUSO` | Reapresentado cedo demais (repasse pela grade) |
| `CREDENCIAL_FORA_DA_JANELA` | Fora da data ou do horário |
| `FORA_DA_URNA` | Cartão de urna apresentado no leitor errado |
| `CREDENCIAL_COMPRIMENTO_INVALIDO` | Leitura com tamanho que não existe no cadastro |
| `LIBERACAO_MANUAL` | Operador liberou, com motivo registrado |

A lista completa está em `src/Access.Domain/Access/ReasonCodes.cs`. Motivo desconhecido
deve ser guardado como veio, nunca recusado.

## 5. Dados do cadastro (P2)

Achados nos backups de 29/09 (docs/22 §9):

1. **Tipo do cartão:** `admission_type` deve vir da coluna de tipo do cartão. Hoje é deduzido
   do nome do cliente, e a leitura de `metadata` prevista no código não acontece (docs/22
   §8.2.7).
2. **Validade vazia:** nenhum dos 2.243 cartões tem `valid_from` ou `valid_until`. Pode
   continuar assim (`null` = sem limite). Mas a view `offline_cards`, se filtrar com
   `valid_from <= now()` sem tratar o vazio, devolve **zero** cartões. Tratar com
   `(valid_from IS NULL OR valid_from <= now())`, e o mesmo para `valid_until`.
3. **Cartões de teste:** os 6 cartões de 6 dígitos, criados em 17/11/2025 ("AUTO" no
   inventário), devem sair do cadastro do evento ou ficar inativos.
4. **Exportações:** manter o número do cartão como texto (o CSV com `;` já faz isso). Abrir
   num Excel e salvar de novo destrói zeros à esquerda: o original é a fonte.
5. **Contagem de usos:** a nuvem pode somar usos para os painéis a partir dos eventos, mas
   `times_used` não deve voltar para a catraca como verdade (§1, item 3).

## 6. Zet → catraca (P1) — o Worker repassa

Hoje a Zet chama o Cloudflare Worker, que assina e entrega ao Supabase. Pedimos que o
Worker **também** entregue o mesmo corpo ao nosso relé de ingressos (docs/18, docs/30 §4
caminho A):

```js
// depois de validar o JSON e antes de responder à Zet
ctx.waitUntil(fetch(RELE_XACESS_URL, {            // https://<relé>/webhook/<token>
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: corpoOriginal,                             // os bytes exatos que a Zet mandou
}).catch(() => {}));                               // falha do relé nunca atrasa nem derruba a Zet
```

| Regra | Por quê |
|---|---|
| Corpo **exato**, sem reformatar | O mesmo tradutor lê o backup e o ao vivo (`TradutorDaZet`) |
| `RELE_XACESS_URL` como segredo do Worker | O token na URL é a credencial do relé |
| Não esperar a resposta do relé para responder à Zet | A venda nunca pode falhar por causa da catraca |
| Não exigir CPF para processar | Em 2025, 37 webhooks falharam no processamento por "CPF nulo" |

Para a **Zet** (não é da equipe da nuvem, mas bloqueia a mesma coisa): as vendas na
maquininha e as cortesias nunca chegam por webhook (5,8% dos pedidos em 2025). Precisamos
de uma exportação ou API **com o voucher** desses ingressos (docs/30, Z1).

## 7. O que a nuvem não deve fazer

- Decidir acesso, ou ser consultada na hora da leitura.
- Enviar número de cartão como número JSON, ou com zeros cortados.
- Mudar o significado de um campo sem mudar a versão deste contrato.
- Devolver `200` com lista vazia quando algo falhou (use `4xx` ou `5xx`).
- Aceitar chamada das funções sem o segredo do equipamento.

## 8. Como testar sem tocar em produção

1. **Um projeto de teste** no Supabase, com as mesmas funções e **cartões fictícios** (os
   números reais e os titulares não saem de produção).
2. Um `device_id` e um segredo de teste, entregues por canal separado (nunca por e-mail
   junto com a URL).
3. Conferências de aceite. O XAcess tem testes automáticos para o nosso lado de cada uma:

| # | Faça | Tem de acontecer |
|---|---|---|
| 1 | Chamar as funções sem `Authorization` | `401` |
| 2 | Chamar com o segredo de outro `device_id` | `401` |
| 3 | Ler `authorizations` com a chave `anon` pela API REST | Recusado |
| 4 | `full_sync` com 2.500 cartões fictícios | 2.500 em `cards` e `total_cards = 2500` |
| 5 | Cartão `"00000000000014"` | Volta idêntico, como texto |
| 6 | Desativar um cartão e pedir a incremental | Aparece em `removed_cards` |
| 7 | Enviar o mesmo evento duas vezes | Segunda vez em `duplicates_ignored` |
| 8 | Enviar lote com um evento inválido | `200`, `failed: 1` e o evento em `failed_events` |
| 9 | Webhook da Zet de teste no Worker | Chega ao Supabase **e** ao relé, com o mesmo corpo |

## 9. Versão e mudanças

Este é o contrato **versão 1**. Campo novo pode entrar sem aviso: nós ignoramos o que não
conhecemos. Mudar o sentido de um campo existente, tirar um campo ou mudar um formato exige
versão 2, combinada antes. O que está aqui corresponde ao código em
`src/Sync.Connectors.Rest/Painel/` e aos testes em `tests/Unit/Conectores/PainelTests.cs`.
