# ADR-0013 — Fail-safe × fail-secure configurável por gate

**Status:** Proposto — depende da resposta a B4 · **Data:** 2026-09-22

## Contexto

Quando a comunicação cai, ou falta energia, o portão deve **abrir** (fail-safe, prioriza
evacuação e evita esmagamento de fluxo) ou **permanecer fechado** (fail-secure, prioriza
controle de acesso e receita). A resposta correta varia por gate — uma saída de emergência
e um portão de área VIP não têm a mesma política — e tem implicação legal e de corpo de
bombeiros.

## Decisão

A política é **configuração por gate**, com quatro valores por situação de falha
(perda de comunicação, falha do Edge, falha de energia, alarme de evacuação). Não existe
default silencioso: o assistente de comissionamento **exige** a escolha, exibe a
consequência em linguagem simples, e registra quem escolheu, quando e com qual aprovação.

Um gate sem política definida não entra em produção.

## Consequências

- Obriga uma conversa que normalmente só acontece depois do incidente.
- A trilha de auditoria guarda a decisão e seu responsável.
- O comportamento em falha é testável: `CHAOS-DEV-01` e `CHAOS-PWR-01` verificam que o
  gate se comporta conforme a política declarada.
- Parte do comportamento depende de fiação e do firmware, não de software — o que o
  software garante é **não contradizer** a política e deixá-la explícita.

## Alternativas recusadas

- **Default global fail-secure.** Cria risco à vida em evacuação.
- **Default global fail-safe.** Transforma qualquer queda de rede em entrada franca.
