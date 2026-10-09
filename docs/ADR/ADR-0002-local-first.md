# ADR-0002 — Local-first: a nuvem fora do caminho crítico

**Status:** Aceito · **Data:** 2026-09-22

## Contexto

Eventos acontecem em ginásios, galpões e áreas abertas, onde o link cai, o 4G satura com
o público e o roteador do local é o mais barato disponível. Uma arquitetura que consulte
a nuvem para liberar uma catraca transforma qualquer soluço de rede em fila na porta.

## Decisão

A decisão de acesso é tomada integralmente no host local, a partir de dados
pré-sincronizados. Nenhuma chamada de rede externa participa do caminho
`leitura → decisão → comando`. A nuvem é destino assíncrono da outbox.

## Consequências

- Exige pré-sincronização de pessoas, credenciais, permissões, horários, bloqueios e
  zonas, com cache versionado e política explícita para dados vencidos.
- Alterações feitas na nuvem durante o evento têm latência de propagação — aceitável e
  **mostrada na tela** ("dados de 3 min atrás"), nunca escondida.
- Bloqueios emergenciais precisam de canal prioritário na sincronização, porque são o
  único caso em que o atraso tem consequência de segurança.
- Verificável: `CHAOS-WAN-01` corta a WAN por 8 h sob carga; `ARCH-01` proíbe por
  compilação que o caminho de decisão alcance um conector.

## Alternativas recusadas

- **Decisão na nuvem com cache local de fallback.** O fallback vira o caminho mais testado
  em produção e o menos testado em desenvolvimento — inversão perigosa.
- **Decisão híbrida por tipo de credencial.** Dobra a superfície de teste e produz
  comportamento inconsistente entre pessoas na mesma fila.
