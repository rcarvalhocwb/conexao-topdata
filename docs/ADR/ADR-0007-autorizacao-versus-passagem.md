# ADR-0007 — Autorização e passagem física são eventos distintos

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

Um comando elétrico aceito não prova que alguém passou. Na linha facial, o `sendlog`
confirma autenticação, autorização e comando — não o giro. Em modelos sem sensor de giro,
não existe confirmação nenhuma. Tratar "autorizei" como "entrou" produz contagem de
público errada, ingresso consumido indevidamente e reconciliação impossível.

## Decisão

Dois conceitos separados no domínio, no banco, nos relatórios e na tela:

- **`AccessAuthorized`** — a decisão foi favorável e o comando foi emitido.
- **`PhysicalPassage`** — houve evento físico comprovando a passagem (origem 6, em modelos
  que a emitem).

O consumo definitivo de ingresso ocorre em `PhysicalPassage`. Onde o modelo não emite
confirmação, o sistema registra `AuthorizedWithoutPassage` e **diz isso ao operador** em
vez de fingir certeza.

## Consequências

- Relatórios trazem "autorizados", "passagens confirmadas" e "autorizados sem confirmação"
  como colunas distintas — e essa terceira coluna é uma métrica de saúde da instalação.
- Exige política explícita de reconciliação para autorização sem giro (ver
  [`04`](../04-workflow-collect-card-then-enter.md)).
- Em modelo sem origem 6, o cliente precisa decidir onde consumir o ingresso — decisão de
  negócio, formalizada em B5, nunca assumida pelo software.

## Alternativas recusadas

- **Assumir passagem após comando bem-sucedido.** Simples, cômodo e errado; inflaciona
  ocupação e consome ingresso de quem desistiu.
