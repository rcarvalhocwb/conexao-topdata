# ADR-0005 — Particionamento de equipamentos por worker

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

O briefing informa limite prático de **~30 equipamentos por instância/thread** da DLL
(`BRIEFING_NAO_VERIFICADO`). Não está claro se o limite é por instância, por thread ou por
processo (pauta Topdata, item 1). Operar perto do teto declarado, em evento, é aceitar
degradar exatamente no pico.

## Decisão

Teto **default de 20** equipamentos por worker, **hard cap de 25**, configurável mas
impedido de ultrapassar 25 sem um teste de carga aprovado e registrado. Acima disso,
novos workers; acima da capacidade do host, novos hosts. Grupos formados por **afinidade
física** (mesmo portão/setor).

## Consequências

- Margem de ~33% sobre o limite declarado absorve variação de firmware e de rede.
- Mais processos, mais memória, mais supervisão — custo baixo diante do risco.
- A afinidade física faz a falha de um worker ter fronteira compreensível para a operação
  ("o setor B está em lista local"), em vez de um conjunto arbitrário de catracas.
- Ao elevar o limite, o sistema exige a referência do teste de carga que o autorizou.

## Alternativas recusadas

- **Um worker por equipamento.** Isolamento perfeito, custo de processos e de conexões
  proibitivo em parques grandes, e provavelmente conflita com o socket servidor único.
- **Usar 30 direto.** Opera no limite declarado sem margem e sem evidência própria.
