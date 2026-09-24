# ADR-0006 — Serializar a sequência montar→enviar por worker

**Status:** Aceito, revisável · **Data:** 2026-09-22

## Contexto

As configurações são montadas em buffers internos da DLL e depois enviadas. Esses buffers
**podem ser globais ao módulo** (`BRIEFING_NAO_VERIFICADO`). Se forem, intercalar a
montagem de dois equipamentos produz o pior defeito possível: configuração de um
equipamento aplicada em outro, silenciosamente, sem erro de retorno.

## Decisão

Enquanto a Topdata não confirmar o escopo dos buffers (pauta, item 2), **toda** sequência
`montar…→enviar` é serializada por um lock exclusivo **do worker**. Nenhuma outra chamada
de configuração ocorre entre o início da montagem e a confirmação do envio. A sequência é
uma unidade auditada: início, fim, dispositivo, retorno.

## Consequências

- Configurar N equipamentos é O(N) sequencial. Em parque grande, a janela de configuração
  é longa — daí a importância de configurar **antes** do evento, com progresso visível.
- Elimina a classe inteira de defeitos de configuração cruzada.
- Se a Topdata confirmar buffers por handle, este ADR é substituído por um que permita
  serialização por dispositivo, e a janela de configuração cai drasticamente.

## Alternativas recusadas

- **Serializar por dispositivo agora.** Rápido e possivelmente corrompido — o modo de
  falha é silencioso, que é o pior tipo.
- **Descobrir empiricamente por tentativa.** Um teste que "passou" não prova ausência de
  corrida; prova que não ocorreu naquela execução.
