# ADR-0025 — Fonte da verdade do cadastro de cartões: a nuvem, e o local na queda

**Status:** Aceito · **Data:** 2026-09-30 · **Decidido por:** o dono do produto (D1 do docs/34 §9)
**Complementa:** ADR-0023 (a nuvem é dona do cadastro), ADR-0002 (local-first) · **Desbloqueia:** Etapa B.4 em diante do docs/35

## Contexto

A Etapa B do [docs/35](../35-prompt-modulo-catraca.md) traz cadastro manual e importação de
cartões no PC local. A ADR-0023 diz que "a nuvem continua dona do cadastro". Um cadastro local
sem regra contradiz isso: ou a sincronização (`middleware-sync-cards`) sobrescreve o que foi
feito no PC, ou o PC sobrescreve a nuvem ([docs/34](../34-estudo-modulo-catraca.md) §9, D1;
anexo 02, item B0).

## Decisão

> **Com conexão, a nuvem é a fonte da verdade e o PC só grava e importa o que vem dela.
> Sem conexão, o cadastro local pode substituir o da nuvem. Quando a conexão volta, o que foi
> feito na queda sobe para a nuvem.**

| Situação | Cadastro e importação no PC | O que vale |
|---|---|---|
| **Conectado** | Só consulta. Cadastrar, editar e importar ficam desabilitados, com o motivo "feito na nuvem". **Exceção:** bloqueio emergencial de um cartão, porque bloquear nunca libera ninguém | O que desce da nuvem |
| **Sem conexão** | Cadastrar, editar, bloquear, desbloquear, cancelar e importar liberados, com trilha (`credential_event`) | O cadastro local, inclusive sobre um cartão que veio da nuvem |
| **Reconectou** | Continua liberado até a fila da queda subir inteira; depois volta a "só consulta" | Cada mudança da queda sobe para a nuvem e passa a valer lá |

### Reconciliação ao reconectar

1. Toda mudança feita sem conexão é gravada numa fila local, só-INSERT, na ordem em que
   aconteceu, e sobe para a nuvem ao reconectar.
2. Se a nuvem também mudou o **mesmo cartão** durante a queda, é **conflito**. Ele vai para uma
   lista que o operador resolve. Nada é resolvido sozinho.
3. Enquanto o conflito não é resolvido, vale **o estado mais restritivo**: se um dos lados
   bloqueou ou cancelou, o cartão fica negado.
4. O bloqueio emergencial feito com conexão também sobe para a nuvem. O bloqueio feito na
   nuvem continua vencendo assim que desce (ADR-0023, consequência 3).

## Consequências

- `ticket.owner_of_fields` deixa de ser uma escolha permanente por cartão. Ele registra quem
  mudou por último: `nuvem`, ou `local` enquanto a mudança não sobe. Depois de subir e ser
  aceita, volta a ser `nuvem`. Um cartão não fica "local para sempre".
- "Sem conexão" precisa de uma definição que o serviço mede e o painel mostra: a sincronização
  com a nuvem sem sucesso há mais que um limite. O limite é uma chave em `edge_setting`, e o
  valor padrão sai da Etapa B.4 (**MELHORIA RECOMENDADA:** 5 minutos, para uma queda curta não
  abrir o cadastro local). Não é o estado da catraca: catraca e nuvem são ligações diferentes.
- A importação local só roda sem conexão. Com conexão, o arquivo é importado na nuvem e desce
  pela sincronização.
- A subida da fila precisa de uma função na nuvem para receber mudanças de cadastro. Hoje só
  existem `sync-cards` (desce) e `sync-events` (sobe tentativas). O contrato é **PROPOSTA
  FUTURA** até ser combinado com quem mantém o painel na nuvem ([docs/31](../31-contrato-da-nuvem.md)).
  Até lá, a fila fica guardada e visível, sem se perder e sem subir.
- Para mudar o modo (conectado → sem conexão) não é preciso reiniciar nada. A decisão da
  catraca continua local e não depende disso (ADR-0023, consequência 1).

## O que ainda não se sabe

- O contrato da função da nuvem que recebe a fila (acima).
- O limite de tempo "sem conexão" que o evento aceita (B.4, com o teste de carga LOAD-IMPORT-01).
