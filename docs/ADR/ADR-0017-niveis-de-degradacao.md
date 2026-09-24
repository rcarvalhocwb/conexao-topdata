# ADR-0017 — Níveis de degradação T0–T3

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

O briefing declara que o Coletor Urna 4 comporta lista de **15.000 usuários**
(`BRIEFING_NAO_VERIFICADO`). O evento-alvo tem **50.000 pessoas**. A estratégia intuitiva
— "sincroniza todo mundo para dentro das catracas e deixa rodar off-line" — **não cabe**.

## Decisão

A autonomia principal é da **borda** (host do Edge), não do equipamento. Quatro níveis:

| Nível | Situação | Quem decide | Cobertura |
|---|---|---|---|
| T0 | Tudo no ar | Edge | 100% das regras |
| T1 | Internet caiu | Edge | 100% das regras |
| T2 | Edge/PC caiu | Equipamento (lista local) | Subconjunto priorizado, regras simples |
| T3 | Equipamento isolado | Equipamento | Política do gate (ADR-0013) |

**T1 é o regime normal de operação de evento.** A lista local do equipamento é rede de
segurança de terceiro nível, com subconjunto priorizado por política explícita (setor,
lote, horário previsto de chegada).

## Consequências

- A cobertura estimada de T2 é **calculada e exibida antes do evento**: o operador precisa
  saber que, se o Edge cair, aquele portão atende 30% do público — e decidir com essa
  informação, não descobrir durante.
- Exige política de seleção de subconjunto como configuração de primeira classe.
- Redundância do host do Edge passa a ser a mitigação mais relevante para T2
  (fora do escopo das Fases 1–3; candidata a ADR próprio).
- Se o número de 15.000 estiver errado, este ADR muda — mas a direção (autonomia na borda)
  continua correta, porque regras complexas não existem no equipamento de qualquer forma.

## Alternativas recusadas

- **Confiar na lista do equipamento como plano de contingência principal.** Não cabe, e o
  conjunto de regras suportado é mais pobre.
- **Recusar operar acima de 15.000 pessoas.** Resolveria o problema errado.
