# ADR-0023 — A catraca decide no PC local; a nuvem recebe e envia informações

**Status:** Aceito · **Data:** 2026-09-25 · **Decidido por:** o usuário
**Confirma:** ADR-0002 (local-first) · **Complementa:** ADR-0022, docs/22

## Contexto

O sistema que já operou numa edição anterior do evento validava **na nuvem, a cada
passagem**: a leitura ia ao Supabase, e só a resposta liberava a catraca. O cache local
era o plano B ([`22`](../22-sistema-supabase.md), seção 6.4).

A pergunta era qual dos dois modelos seguir daqui em diante.

## Decisão

> **A catraca decide no PC local. O PC recebe da nuvem e manda para a nuvem.**

| Sentido | O quê | Função do Supabase |
|---|---|---|
| **Desce** — nuvem → PC | Cartões autorizados, ingressos, regras | `middleware-sync-cards` |
| **Sobe** — PC → nuvem | Toda tentativa, liberada ou negada, e a confirmação de giro | `middleware-sync-events` |
| **Sobe** — PC → nuvem | Saúde de cada catraca e do PC | `middleware-heartbeat` |
| **Fora do caminho do giro** | Validação a cada passagem | `middleware-event-receiver` — não é chamada para decidir |

## Consequências

1. **A nuvem nunca está no caminho do giro.** Internet fora ou lenta não atrasa ninguém na
   porta; o que muda é quando os fatos chegam ao painel.
2. **Um PC com todas as catracas.** A regra de uso único e o cooldown valem entre catracas
   porque todas decidem contra a mesma base local. Dois PCs, sem internet, não enxergam o
   uso um do outro.
3. **A nuvem continua dona do cadastro, e a borda é dona do fato.** Cartão bloqueado na
   nuvem vence a base local assim que desce; passagem registrada na borda vence o que a
   nuvem achar ([`15`](../15-integracao-e-sincronizacao.md), seção 2).
4. **Comando remoto da nuvem (abrir catraca, reiniciar) não é executado** até ter desenho
   próprio, com registro de quem mandou. Abrir o portão pela internet é o tipo de ação que
   precisa de trilha e, idealmente, de duas pessoas.

## O que ainda não se sabe

| Pergunta | Onde se resolve |
|---|---|
| O formato das **respostas** de `sync-cards`, `sync-events` e `heartbeat` | Código do middleware C# atual, ou das funções |
| O evento sobe **na liberação** ou **no giro**? O painel de vocês conta público pelas validações — uma liberação sem giro seria contada como entrada | Mesma fonte, e uma decisão sobre o que o painel deve contar |
| Como a nuvem trata `offline_validated: true` — como fato consumado, ou revalida? | Código de `middleware-sync-events` |
| O QR do Zet sobe pelo mesmo evento que o cartão? | Código das funções |

Nenhuma dessas respostas muda a decisão; todas mudam o código que a implementa.
